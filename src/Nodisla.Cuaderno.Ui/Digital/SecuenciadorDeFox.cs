using System.Globalization;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Donde va cada cazador dentro de su propio intercambio con el fox.</summary>
public enum PasoDeCazador
{
    /// <summary>
    /// Ha llamado (o se le sigue oyendo llamar) y todavia no se le ha mandado el informe.
    /// </summary>
    EsperandoInforme,

    /// <summary>
    /// Ha contestado con «R + informe»: lo siguiente que se le manda es el <c>RR73</c> que cierra
    /// el contacto desde el lado del fox.
    /// </summary>
    EsperandoRr73,
}

/// <summary>Un cazador dentro del pileup, con su tono y en que paso va.</summary>
public sealed class EstadoDeCazador
{
    /// <summary>Su indicativo.</summary>
    public required string Indicativo { get; init; }

    /// <summary>Tono asignado dentro de la mezcla, en hercios.</summary>
    public int TonoHz { get; set; }

    /// <summary>En que paso del intercambio va.</summary>
    public PasoDeCazador Paso { get; set; } = PasoDeCazador.EsperandoInforme;

    /// <summary>Su localizador, si lo ha dicho.</summary>
    public string Grid { get; set; } = string.Empty;

    /// <summary>Informe que se le va a mandar (o se le mando), en decibelios.</summary>
    public int? InformeAEnviar { get; set; }

    /// <summary>Informe que ha mandado el, con su «R» delante.</summary>
    public int? InformeRecibido { get; set; }

    /// <summary>Emisiones seguidas sin que se le oiga.</summary>
    public int CiclosSinRespuesta { get; set; }
}

/// <summary>Un mensaje que toca emitir en la proxima ventana, para un cazador concreto.</summary>
/// <param name="Indicativo">A quien va dirigido.</param>
/// <param name="Texto">El mensaje, con la gramatica de siempre.</param>
/// <param name="TonoHz">En que tono, dentro de la mezcla.</param>
public readonly record struct TransmisionDeFox(string Indicativo, string Texto, int TonoHz);

/// <summary>Lo que el secuenciador de fox decide tras procesar una ventana.</summary>
/// <param name="Transmisiones">Los mensajes a mezclar y emitir en la proxima ventana propia.</param>
/// <param name="ContactosCompletados">
/// Cazadores a los que se les acaba de poner en cola el <c>RR73</c>: el fox no espera
/// confirmacion (igual que en <see cref="TipoDeOperacion.Hound"/>), asi que se dan por completos
/// y su tono queda libre para el siguiente de la cola.
/// </param>
/// <param name="CazadoresAbandonados">Cazadores que se han quitado por no responder.</param>
public sealed record DecisionDelFox(
    IReadOnlyList<TransmisionDeFox> Transmisiones,
    IReadOnlyList<string> ContactosCompletados,
    IReadOnlyList<string> CazadoresAbandonados)
{
    /// <summary>No hay nada que hacer.</summary>
    public static DecisionDelFox Nada { get; } = new([], [], []);
}

/// <summary>
/// El lado fox de fox/hound: gestiona un pileup de varios cazadores a la vez, cada uno en su
/// propio tono y en su propio paso de la conversacion.
/// </summary>
/// <remarks>
/// <para>
/// No es <see cref="SecuenciadorDeQso"/> con una lista por encima: es un modelo mental distinto.
/// Un QSO normal lleva un corresponsal y un mensaje pendiente; un fox lleva una cola entera de
/// cazadores, cada uno en su propio punto del intercambio, y decide a cuantos atiende a la vez,
/// que tono le toca a cada uno y cuando se le deja de esperar. Comparte con
/// <see cref="SecuenciadorDeQso"/> solo lo que de verdad es igual: la paridad de las ventanas
/// (<see cref="SecuenciadorDeQso.ParidadDe"/> y <see cref="SecuenciadorDeQso.ParidadDeLaSiguiente"/>,
/// reutilizados tal cual) y la gramatica de los mensajes (<see cref="GramaticaDeMensajes"/>, con
/// el fox como «YO» y cada cazador como «DX»).
/// </para>
/// <para>
/// <b>El fox no espera un 73.</b> En cuanto un cazador manda su «R + informe», al fox solo le
/// queda mandarle <c>RR73</c>; igual que en <see cref="TipoDeOperacion.Hound"/>
/// (<see cref="SecuenciadorDeQso"/>), eso basta para dar el contacto por completo desde este
/// lado: no se espera a que el cazador confirme con su propio <c>RR73</c>/<c>73</c>, as­i que el
/// tono queda libre para el siguiente de la cola en cuanto el <c>RR73</c> queda en cola de
/// emision.
/// </para>
/// <para>
/// <b>Nada de reloj de pared.</b> Igual que <see cref="SecuenciadorDeQso"/>, todo se calcula con
/// las horas de las ventanas que le pasan: en pruebas el tiempo es un dato mas.
/// </para>
/// </remarks>
public sealed class SecuenciadorDeFox
{
    /// <summary>Cuantos cazadores simultaneos se admiten como mucho, pase lo que pase.</summary>
    /// <remarks>
    /// No es una cifra de protocolo: es un limite de prudencia sobre lo que de verdad se ha
    /// podido comprobar (el banco de pruebas de mezcla de señales,
    /// <c>MezclaDeSenalesPruebas</c>). Mezclar mas señales a la vez en el mismo audio no se ha
    /// probado ni en banco ni en el aire, y cuantas mas se mezclan, mas joven es la normalizacion
    /// de amplitud y mas dificil decodificarlas todas limpias para un receptor de verdad.
    /// </remarks>
    public const int MaximoDeCazadoresSimultaneos = 10;

    private readonly Dictionary<string, EstadoDeCazador> _cazadores = new(StringComparer.Ordinal);
    private int _cazadoresSimultaneos = 5;
    private int _ciclosSinRespuesta = 3;
    private int _tonoBaseHz = 1000;
    private int _tonoTopeHz = 3000;

    /// <summary>Lo que dura una ventana del modo en curso.</summary>
    public TimeSpan Periodo { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>El indicativo del fox (el propio).</summary>
    public string MiIndicativo { get; set; } = string.Empty;

    /// <summary>
    /// Separacion minima entre los tonos de dos cazadores, para que la mezcla de audio no los
    /// pise (vease <c>ParametrosDelModo.SeparacionMinimaDeTonosHz</c>). Por omision, la de FT8.
    /// </summary>
    public double SeparacionMinimaDeTonosHz { get; set; } = 62.5;

    /// <summary>Tono mas bajo en el que se asigna un cazador.</summary>
    public int TonoBaseHz
    {
        get => _tonoBaseHz;
        set => _tonoBaseHz = Math.Clamp(value, 200, 4000);
    }

    /// <summary>Tono mas alto en el que se asigna un cazador.</summary>
    public int TonoTopeHz
    {
        get => _tonoTopeHz;
        set => _tonoTopeHz = Math.Clamp(value, 200, 5000);
    }

    /// <summary>
    /// Cuantos cazadores se atienden a la vez. De fabrica, 5: conservador aposta —ver
    /// <see cref="MaximoDeCazadoresSimultaneos"/>—, y de sobra para que no se note la cola en un
    /// pileup corriente.
    /// </summary>
    public int CazadoresSimultaneos
    {
        get => _cazadoresSimultaneos;
        set => _cazadoresSimultaneos = Math.Clamp(value, 1, MaximoDeCazadoresSimultaneos);
    }

    /// <summary>
    /// Emisiones seguidas sin oir a un cazador tras las que se le deja de esperar y se libera su
    /// tono. Mas bajo que el de <see cref="SecuenciadorDeQso.CiclosSinRespuesta"/> (5, de fabrica,
    /// para un QSO de uno a uno): en un pileup conviene reciclar el hueco pronto, hay cola
    /// esperando.
    /// </summary>
    public int CiclosSinRespuesta
    {
        get => _ciclosSinRespuesta;
        set => _ciclosSinRespuesta = Math.Clamp(value, 1, 50);
    }

    /// <summary>El pileup esta en marcha.</summary>
    public bool Activo { get; private set; }

    /// <summary>Paridad de las ventanas del fox: 0 o 1, o -1 si aun no se ha fijado.</summary>
    public int Paridad { get; private set; } = -1;

    /// <summary>La ultima ventana que se ha procesado.</summary>
    public DateTimeOffset? UltimaVentana { get; private set; }

    /// <summary>Los cazadores en curso ahora mismo, por indicativo.</summary>
    public IReadOnlyDictionary<string, EstadoDeCazador> Cazadores => _cazadores;

    /// <summary>Empieza a atender el pileup. Sale en la siguiente ventana, sea cual sea su paridad.</summary>
    public void Empezar(DateTimeOffset ahora)
    {
        _cazadores.Clear();
        Activo = true;
        Paridad = SecuenciadorDeQso.ParidadDeLaSiguiente(ahora, Periodo);
    }

    /// <summary>Para el pileup del todo: se olvida de todos los cazadores en curso.</summary>
    public void Parar()
    {
        Activo = false;
        _cazadores.Clear();
    }

    /// <summary>Cambia de modo: el periodo es otro y la paridad no vale.</summary>
    public void CambiarPeriodo(TimeSpan periodo)
    {
        Periodo = periodo;
        Parar();
        Paridad = -1;
    }

    /// <summary>
    /// Ha cerrado una ventana: con lo que se ha oido, decide a quien se le manda que en la
    /// proxima ventana propia.
    /// </summary>
    /// <param name="ventana">Instante en que empezo la ventana que acaba de cerrar.</param>
    /// <param name="oidos">Lo decodificado en ella.</param>
    public DecisionDelFox Procesar(DateTimeOffset ventana, IReadOnlyList<MensajeOido> oidos)
    {
        ArgumentNullException.ThrowIfNull(oidos);
        UltimaVentana = ventana;

        if (!Activo) return DecisionDelFox.Nada;

        // La ventana que acaba de cerrar era la propia: aqui no se decide nada.
        if (Paridad >= 0 && SecuenciadorDeQso.ParidadDe(ventana, Periodo) == Paridad) return DecisionDelFox.Nada;

        var vistos = new HashSet<string>(StringComparer.Ordinal);

        // 1) Progreso de los cazadores ya conocidos con lo que han dicho esta ventana.
        foreach (var oido in oidos)
        {
            var m = oido.Mensaje;
            if (m.Llamante.Length == 0 || !m.VaDirigidoA(MiIndicativo)) continue;
            if (!_cazadores.TryGetValue(m.Llamante, out var cazador)) continue;

            vistos.Add(m.Llamante);
            cazador.CiclosSinRespuesta = 0;

            switch (m.Clase)
            {
                case ClaseDeMensaje.Llamada:
                    if (m.Locator.Length > 0) cazador.Grid = m.Locator;
                    cazador.InformeAEnviar = oido.Decibelios;
                    break;

                case ClaseDeMensaje.InformeConR:
                    cazador.InformeRecibido = m.Informe;
                    cazador.Paso = PasoDeCazador.EsperandoRr73;
                    break;

                case ClaseDeMensaje.Rr73:
                case ClaseDeMensaje.Rrr:
                case ClaseDeMensaje.S73:
                    // Ya deberia estar en EsperandoRr73 (o ya completado); por si acaso, se
                    // cierra igual: no tiene sentido seguir esperando nada mas de el.
                    cazador.Paso = PasoDeCazador.EsperandoRr73;
                    break;
            }
        }

        var transmisiones = new List<TransmisionDeFox>();
        var completados = new List<string>();
        var abandonados = new List<string>();

        // 2) A los que ya tienen el RR73 en cola no se les espera mas (ver comentario de la
        // clase): se emite ese ultimo mensaje y se libera su tono.
        foreach (var cazador in _cazadores.Values.ToList())
        {
            if (cazador.Paso != PasoDeCazador.EsperandoRr73) continue;

            transmisiones.Add(new TransmisionDeFox(cazador.Indicativo, ComponerRr73(cazador), cazador.TonoHz));
            completados.Add(cazador.Indicativo);
            _cazadores.Remove(cazador.Indicativo);
        }

        // 3) A quien no se ha oido esta ventana, un ciclo mas sin respuesta; si se pasa, se le
        // deja de esperar y su tono queda libre.
        foreach (var cazador in _cazadores.Values.ToList())
        {
            if (vistos.Contains(cazador.Indicativo)) continue;

            cazador.CiclosSinRespuesta++;
            if (cazador.CiclosSinRespuesta < CiclosSinRespuesta) continue;

            abandonados.Add(cazador.Indicativo);
            _cazadores.Remove(cazador.Indicativo);
        }

        // 4) Cazadores nuevos que llaman por primera vez, si hay hueco de cupo y de tono. Se
        // atienden en la misma ventana en que entran, igual que SecuenciadorDeQso.LlamarAlPrimero.
        foreach (var oido in oidos)
        {
            var m = oido.Mensaje;
            if (m.Clase != ClaseDeMensaje.Llamada || m.Llamante.Length == 0 || !m.VaDirigidoA(MiIndicativo)) continue;
            if (_cazadores.ContainsKey(m.Llamante)) continue;
            if (_cazadores.Count >= CazadoresSimultaneos) continue;

            var tono = AsignarTono();
            if (tono is null) continue;

            _cazadores[m.Llamante] = new EstadoDeCazador
            {
                Indicativo = m.Llamante,
                Grid = m.Locator,
                TonoHz = tono.Value,
                InformeAEnviar = oido.Decibelios,
            };
        }

        // 5) Los que siguen esperando informe (los que ya tenian el RR73 en cola salieron en el
        // paso 2) se mandan ahora.
        foreach (var cazador in _cazadores.Values)
        {
            if (cazador.Paso != PasoDeCazador.EsperandoInforme) continue;
            transmisiones.Add(new TransmisionDeFox(cazador.Indicativo, ComponerInforme(cazador), cazador.TonoHz));
        }

        return new DecisionDelFox(transmisiones, completados, abandonados);
    }

    /// <summary>Busca el primer tono libre dentro del rango, respetando la separacion minima.</summary>
    private int? AsignarTono()
    {
        var usados = _cazadores.Values.Select(c => c.TonoHz).ToHashSet();
        var paso = Math.Max(1, (int)Math.Ceiling(SeparacionMinimaDeTonosHz));

        for (var tono = TonoBaseHz; tono <= TonoTopeHz; tono += paso)
            if (!usados.Contains(tono)) return tono;

        return null;
    }

    private string ComponerInforme(EstadoDeCazador cazador) =>
        $"{cazador.Indicativo} {MiIndicativo} {Formatear(cazador.InformeAEnviar ?? 0)}";

    private string ComponerRr73(EstadoDeCazador cazador) =>
        $"{cazador.Indicativo} {MiIndicativo} RR73";

    private static string Formatear(int db) => db.ToString("+00;-00;+00", CultureInfo.InvariantCulture);
}
