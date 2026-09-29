using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.ReedSolomon;

namespace Nodisla.Cuaderno.Modos.Jt65;

/// <summary>Como fue la decodificacion de una ventana de JT65, con el detalle para poder medir.</summary>
/// <param name="Decodificaciones">Lo que se saco.</param>
/// <param name="Candidatas">Cuantos sitios se miraron.</param>
/// <param name="Examinadas">Cuantas candidatas pasaron el corte de sincronismo y llegaron al decodificador blando.</param>
/// <param name="PalabrasRechazadas">
/// Palabras de codigo que salieron de algun intento del decodificador blando y no pasaron el
/// criterio de aceptacion. <b>Es la cifra que importa</b>: cada una era una decodificacion
/// falsa en potencia.
/// </param>
/// <param name="Duracion">Lo que costo la ventana entera.</param>
public sealed record ResultadoDeVentanaJt65(
    IReadOnlyList<DecodificacionPropia> Decodificaciones,
    int Candidatas,
    int Examinadas,
    int PalabrasRechazadas,
    TimeSpan Duracion);

/// <summary>
/// Decodifica una ventana entera de JT65.
/// </summary>
/// <remarks>
/// <para>
/// Junta las piezas en orden: espectrograma, busqueda de sincronismo, afinado y medida de
/// cada candidata, decodificacion blanda del Reed-Solomon y desempaquetado del mensaje.
/// </para>
/// <para>
/// <b>Los filtros que impiden inventar un contacto</b>, por orden:
/// </para>
/// <list type="number">
/// <item>El sincronismo afinado tiene que pasar de <see cref="SincronismoMinimo"/>. Con menos
/// no hay senal que decodificar, y no se le da al decodificador blando ni la ocasion.</item>
/// <item>El decodificador blando solo acepta palabras de codigo cuya distancia blanda a lo
/// oido queda por debajo de un umbral medido con ruido puro.</item>
/// <item>Los 72 bits tienen que desempaquetarse a un mensaje con sentido: indicativos de forma
/// valida y un tercer campo que exista.</item>
/// </list>
/// <para>
/// JT65 no lleva CRC, asi que aqui el segundo filtro hace el trabajo que en FT8 hace el sello.
/// Por eso se cuenta aparte cuantas palabras rechazo: si esa cifra se disparara, seria la
/// senal de que algo va mal mucho antes de que apareciera un indicativo falso en la pantalla.
/// </para>
/// </remarks>
public sealed class DecodificadorJt65
{
    private readonly ParametrosJt65 _parametros;
    private readonly CodificadorJt65 _codificador = new();
    private readonly DecodificadorBlando _blando;
    private readonly ILogger _registro;

    /// <summary>Crea el decodificador.</summary>
    /// <param name="parametros">Submodo.</param>
    /// <param name="registro">Para dejar constancia; por omision no se traza nada.</param>
    public DecodificadorJt65(ParametrosJt65? parametros = null, ILogger? registro = null)
    {
        _parametros = parametros ?? ParametrosJt65.A;
        _blando = new DecodificadorBlando(_codificador.Codigo);
        _registro = registro ?? NullLogger.Instance;
    }

    /// <summary>Submodo con el que trabaja.</summary>
    public ParametrosJt65 Parametros => _parametros;

    /// <summary>El decodificador blando, con sus mandos, para poder medirlo desde el banco.</summary>
    public DecodificadorBlando Blando => _blando;

    /// <summary>Candidatas que se miran como mucho en cada ventana.</summary>
    public int CandidatasPorVentana { get; set; } = 20;

    /// <summary>
    /// Sincronismo afinado minimo para intentar decodificar una candidata.
    /// </summary>
    /// <remarks>
    /// La puntuacion vale uno con ruido y sube con la senal. En ruido puro, los maximos de una
    /// ventana entera se quedan entre 1,9 y 2,4 (son veinte maximos locales afinados, asi
    /// que siempre sale alguno alto); una senal a −25 dB, el limite practico del decodificador,
    /// da mas de 3,5, y a −26, donde ya no decodifica, 3,0. Medido en el banco. Cada candidata
    /// que pasa cuesta casi medio segundo de decodificador blando, asi que el corte tambien
    /// es lo que mantiene la ventana barata.
    /// </remarks>
    public double SincronismoMinimo { get; set; } = 2.7;

    /// <summary>
    /// Ajuste del informe de senal, en decibelios, sacado de medir en el banco con senales de
    /// relacion conocida.
    /// </summary>
    public double CorreccionDelInforme { get; set; } = 0.0;

    /// <summary>
    /// Decodifica una ventana de audio.
    /// </summary>
    /// <param name="audio">Ventana a 11025 muestras por segundo, de −1 a 1.</param>
    /// <param name="ventanaUtc">Momento en que empieza la ventana.</param>
    /// <param name="ct">Testigo de cancelacion: se comprueba entre candidatas.</param>
    public ResultadoDeVentanaJt65 Decodificar(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, CancellationToken ct = default)
    {
        var reloj = Stopwatch.StartNew();
        if (audio.Length < _parametros.MuestrasDeLaSenal / 2)
            return new ResultadoDeVentanaJt65([], 0, 0, 0, reloj.Elapsed);

        var muestras = audio.ToArray();
        var espectrograma = EspectrogramaJt65.Calcular(muestras);
        var candidatas = SincronizadorJt65.Buscar(espectrograma, _parametros, CandidatasPorVentana);
        var demodulador = new DemoduladorJt65(muestras, _parametros);

        var salida = new List<DecodificacionPropia>();
        var vistas = new List<(string Texto, double Tono)>();
        var decodificadas = new List<double>();
        var palabra = new byte[63];
        var examinadas = 0;
        var rechazadas = 0;

        for (var i = 0; i < candidatas.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            var candidata = candidatas[i];
            // Una senal fuerte deja candidatas por toda su anchura, en otros instantes: los
            // tonos de datos coinciden aqui y alla con el patron de sincronismo desplazado. Una
            // vez decodificada, todo lo que cae dentro de su banda se da por suyo.
            var frecuenciaGruesa = candidata.Casilla * EspectrogramaJt65.CasillaHz;
            if (decodificadas.Any(f => frecuenciaGruesa > f - 10 && frecuenciaGruesa < f + _parametros.AnchoDeBandaHz + 10)) continue;

            var medida = demodulador.Medir(candidata);
            if (medida.Sincronismo < SincronismoMinimo) continue;
            examinadas++;

            // La semilla depende del sitio, no del reloj: la misma ventana decodifica igual
            // todas las veces.
            var azar = new Random(HashCode.Combine(candidata.MedioSimbolo, candidata.Casilla));
            var resultado = _blando.Decodificar(medida.Potencias, palabra, azar, medida.SenalSobreRuido);
            rechazadas += resultado.PalabrasRechazadas;
            if (!resultado.Decodificada) continue;

            var bits = _codificador.BitsDePalabra(palabra);
            if (!MensajeDe72Bits.TryDesempaquetar(bits, out var mensaje)) continue;

            var tono = medida.TonoBaseHz;
            if (YaEstaba(vistas, mensaje.Texto, tono)) continue;
            vistas.Add((mensaje.Texto, tono));
            decodificadas.Add(tono);

            salida.Add(new DecodificacionPropia(
                mensaje.Texto,
                Informe(medida.SenalSobreRuido),
                Math.Round(medida.ComienzoSegundos - ParametrosJt65.ComienzoNominalSegundos, 2),
                (int)Math.Round(tono),
                ModoDelModem.Jt65,
                ventanaUtc)
            {
                Llamante = mensaje.Llamante,
                Llamado = mensaje.Llamado,
                Locator = mensaje.Locator,
                EsCq = mensaje.EsCq,
            });
        }

        salida.Sort(static (x, y) => x.TonoHz.CompareTo(y.TonoHz));
        _registro.LogDebug(
            "Ventana JT65 {Ventana}: {Candidatas} candidatas, {Examinadas} examinadas, {Rechazadas} palabras rechazadas, {Salieron} decodificaciones en {Milisegundos} ms.",
            ventanaUtc, candidatas.Count, examinadas, rechazadas, salida.Count, reloj.ElapsedMilliseconds);

        return new ResultadoDeVentanaJt65(salida, candidatas.Count, examinadas, rechazadas, reloj.Elapsed);
    }

    /// <summary>
    /// Informe de senal referido a 2500 Hz, como en todos los modos de la familia.
    /// </summary>
    /// <remarks>
    /// La relacion medida es en una casilla de 2,69 Hz (la anchura de la transformada de un
    /// simbolo, igual en los tres submodos); para referirla a 2500 Hz se reparte el ruido.
    /// </remarks>
    private int Informe(double senalSobreRuidoEnCasilla)
    {
        if (senalSobreRuidoEnCasilla <= 0) return -30;
        var db = (10 * Math.Log10(senalSobreRuidoEnCasilla * ParametrosJt65.CasillaHz / 2500.0)) + CorreccionDelInforme;
        return (int)Math.Round(Math.Clamp(db, -30, 50));
    }

    private static bool YaEstaba(List<(string Texto, double Tono)> vistas, string texto, double tono)
    {
        foreach (var (t, f) in vistas)
            if (Math.Abs(f - tono) < 10 && string.Equals(t, texto, StringComparison.Ordinal)) return true;
        return false;
    }
}
