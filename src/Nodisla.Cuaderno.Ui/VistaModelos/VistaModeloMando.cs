using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Con que control se acciona un mando en la pantalla.</summary>
public enum FormaDelMando
{
    /// <summary>Una casilla: encendido o apagado.</summary>
    Interruptor,

    /// <summary>Una lista desplegable con las posiciones que tiene.</summary>
    Posiciones,

    /// <summary>Un deslizador con su escala.</summary>
    Escala,
}

/// <summary>
/// Un mando del equipo, listo para pintarlo.
/// </summary>
/// <remarks>
/// La interfaz no sabe de antemano que mandos tiene el equipo: se los pregunta al control y
/// construye un control de pantalla por cada uno segun lo que diga su rango. Por eso esta
/// clase no tiene nada escrito a mano sobre ningun equipo concreto. Un FT-710 ensena sus
/// treinta y tantos mandos y un equipo modesto ensena los cuatro que tiene, sin tocar nada.
/// </remarks>
public sealed partial class VistaModeloMando : ObservableObject
{
    // Las CLAVES de los textos, no los textos: el nombre se resuelve al leerlo, en el idioma en uso.
    private static readonly IReadOnlyDictionary<MandoDeEquipo, (string Nombre, string Grupo)> Nombres =
        new Dictionary<MandoDeEquipo, (string, string)>
        {
            [MandoDeEquipo.Potencia] = ("Cabina.Mando.Potencia", "Cabina.Mando.Grupo.Nivel"),
            [MandoDeEquipo.GananciaRf] = ("Cabina.Mando.GananciaRf", "Cabina.Mando.Grupo.Nivel"),
            [MandoDeEquipo.GananciaMicrofono] = ("Cabina.Mando.GananciaMicrofono", "Cabina.Mando.Grupo.Nivel"),
            [MandoDeEquipo.Volumen] = ("Cabina.Mando.Volumen", "Cabina.Mando.Grupo.Nivel"),
            [MandoDeEquipo.Monitor] = ("Cabina.Mando.Monitor", "Cabina.Mando.Grupo.Nivel"),
            [MandoDeEquipo.Compresor] = ("Cabina.Mando.Compresor", "Cabina.Mando.Grupo.Nivel"),

            [MandoDeEquipo.Atenuador] = ("Cabina.Mando.Atenuador", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.Preamplificador] = ("Cabina.Mando.Preamplificador", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.Agc] = ("Cabina.Mando.Agc", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.SupresorDeRuido] = ("Cabina.Mando.SupresorDeRuido", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.NivelSupresorDeRuido] = ("Cabina.Mando.NivelSupresorDeRuido", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.ReductorDeRuido] = ("Cabina.Mando.ReductorDeRuido", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.NivelReductorDeRuido] = ("Cabina.Mando.NivelReductorDeRuido", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.MuescaAutomatica] = ("Cabina.Mando.MuescaAutomatica", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.MuescaManual] = ("Cabina.Mando.MuescaManual", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.FrecuenciaDeMuesca] = ("Cabina.Mando.FrecuenciaDeMuesca", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.Contorno] = ("Cabina.Mando.Contorno", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.FrecuenciaDeContorno] = ("Cabina.Mando.FrecuenciaDeContorno", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.DesplazamientoFi] = ("Cabina.Mando.DesplazamientoFi", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.AnchoDeFiltro] = ("Cabina.Mando.AnchoDeFiltro", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.FiltroDeTejado] = ("Cabina.Mando.FiltroDeTejado", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.Silenciador] = ("Cabina.Mando.Silenciador", "Cabina.Mando.Grupo.Recepcion"),

            [MandoDeEquipo.TonoCw] = ("Cabina.Mando.TonoCw", "Cabina.Mando.Grupo.Telegrafia"),
            [MandoDeEquipo.VelocidadKeyer] = ("Cabina.Mando.VelocidadKeyer", "Cabina.Mando.Grupo.Telegrafia"),
            [MandoDeEquipo.BreakIn] = ("Cabina.Mando.BreakIn", "Cabina.Mando.Grupo.Telegrafia"),
            [MandoDeEquipo.RetardoBreakIn] = ("Cabina.Mando.RetardoBreakIn", "Cabina.Mando.Grupo.Telegrafia"),

            [MandoDeEquipo.Vox] = ("Cabina.Mando.Vox", "Cabina.Mando.Grupo.Transmision"),
            [MandoDeEquipo.GananciaVox] = ("Cabina.Mando.GananciaVox", "Cabina.Mando.Grupo.Transmision"),
            [MandoDeEquipo.RetardoVox] = ("Cabina.Mando.RetardoVox", "Cabina.Mando.Grupo.Transmision"),
            [MandoDeEquipo.Sintonizador] = ("Cabina.Mando.Sintonizador", "Cabina.Mando.Grupo.Transmision"),
            [MandoDeEquipo.Antena] = ("Cabina.Mando.Antena", "Cabina.Mando.Grupo.Transmision"),

            [MandoDeEquipo.Split] = ("Cabina.Mando.Split", "Cabina.Mando.Grupo.Frecuencia"),
            [MandoDeEquipo.Rit] = ("Cabina.Mando.Rit", "Cabina.Mando.Grupo.Frecuencia"),
            [MandoDeEquipo.DesplazamientoRit] = ("Cabina.Mando.DesplazamientoRit", "Cabina.Mando.Grupo.Frecuencia"),
            [MandoDeEquipo.Xit] = ("Cabina.Mando.Xit", "Cabina.Mando.Grupo.Frecuencia"),
            [MandoDeEquipo.DesplazamientoXit] = ("Cabina.Mando.DesplazamientoXit", "Cabina.Mando.Grupo.Frecuencia"),

            [MandoDeEquipo.FiltroEstrecho] = ("Cabina.Mando.FiltroEstrecho", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.Apf] = ("Cabina.Mando.Apf", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.FrecuenciaApf] = ("Cabina.Mando.FrecuenciaApf", "Cabina.Mando.Grupo.Recepcion"),
            [MandoDeEquipo.Bloqueo] = ("Cabina.Mando.Bloqueo", "Cabina.Mando.Grupo.Frecuencia"),
            [MandoDeEquipo.SintoniaFinaRapida] = ("Cabina.Mando.SintoniaFinaRapida", "Cabina.Mando.Grupo.Frecuencia"),
            [MandoDeEquipo.TonoDeReferenciaCw] = ("Cabina.Mando.TonoDeReferenciaCw", "Cabina.Mando.Grupo.Telegrafia"),
            [MandoDeEquipo.NivelAmc] = ("Cabina.Mando.NivelAmc", "Cabina.Mando.Grupo.Transmision"),
            [MandoDeEquipo.AntiVox] = ("Cabina.Mando.AntiVox", "Cabina.Mando.Grupo.Transmision"),
            [MandoDeEquipo.ContrastePantalla] = ("Cabina.Mando.ContrastePantalla", "Cabina.Mando.Grupo.Pantalla"),
            [MandoDeEquipo.BrilloPantalla] = ("Cabina.Mando.BrilloPantalla", "Cabina.Mando.Grupo.Pantalla"),
            [MandoDeEquipo.EspectroVelocidad] = ("Cabina.Mando.EspectroVelocidad", "Cabina.Mando.Grupo.Pantalla"),
            [MandoDeEquipo.EspectroAncho] = ("Cabina.Mando.EspectroAncho", "Cabina.Mando.Grupo.Pantalla"),
            [MandoDeEquipo.EspectroModo] = ("Cabina.Mando.EspectroModo", "Cabina.Mando.Grupo.Pantalla"),
            [MandoDeEquipo.EspectroNivel] = ("Cabina.Mando.EspectroNivel", "Cabina.Mando.Grupo.Pantalla"),
            [MandoDeEquipo.EspectroPicos] = ("Cabina.Mando.EspectroPicos", "Cabina.Mando.Grupo.Pantalla"),
            [MandoDeEquipo.EspectroColor] = ("Cabina.Mando.EspectroColor", "Cabina.Mando.Grupo.Pantalla"),
            [MandoDeEquipo.FuncionDelMandoFunc] = ("Cabina.Mando.FuncionDelMandoFunc", "Cabina.Mando.Grupo.Frontal"),
            [MandoDeEquipo.FuncionDelMandoDsp] = ("Cabina.Mando.FuncionDelMandoDsp", "Cabina.Mando.Grupo.Frontal"),
        };

    private readonly IEquipoAvanzado _equipo;
    private readonly string _claveDelNombre;
    private readonly string _claveDelGrupo;
    private readonly IReadOnlyList<string> _etiquetas;

    private static readonly IReadOnlyDictionary<string, string> ClavesDeLasPosiciones =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Apagado"] = "Cabina.Posicion.Apagado",
            ["Encendido"] = "Cabina.Posicion.Encendido",
            ["Rápido"] = "Cabina.Posicion.Rapido",
            ["Medio"] = "Cabina.Posicion.Medio",
            ["Lento"] = "Cabina.Posicion.Lento",
            ["Automático rápido"] = "Cabina.Posicion.AutoRapido",
            ["Automático medio"] = "Cabina.Posicion.AutoMedio",
            ["Automático lento"] = "Cabina.Posicion.AutoLento",
            ["Sintonizar (emite portadora)"] = "Cabina.Posicion.SintonizarEmite",
            ["Sintonizar"] = "Cabina.Posicion.Sintonizar",
            ["Fuera"] = "Cabina.Posicion.Fuera",
            ["En línea"] = "Cabina.Posicion.EnLinea",
            ["Semi"] = "Cabina.Posicion.Semi",
            ["Total"] = "Cabina.Posicion.Total",
            ["Ancho"] = "Cabina.Posicion.Ancho",
            ["Estrecho"] = "Cabina.Posicion.Estrecho",
            ["Normal"] = "Cabina.Posicion.Normal",
            ["Fina (FINE)"] = "Cabina.Posicion.Fina",
            ["Rápida (FAST)"] = "Cabina.Posicion.Rapida",
            ["Sin preamplificador (IPO)"] = "Cabina.Posicion.SinPreampIpo",
            ["Sin preamplificador"] = "Cabina.Posicion.SinPreamp",
            ["Amplificador"] = "Cabina.Posicion.Amplificador",
            ["Amplificador 1"] = "Cabina.Posicion.Amplificador1",
            ["Amplificador 2"] = "Cabina.Posicion.Amplificador2",
            ["Preamplificador"] = "Cabina.Posicion.Preamplificador",
            ["Preamplificador 1"] = "Cabina.Posicion.Preamplificador1",
            ["Preamplificador 2"] = "Cabina.Posicion.Preamplificador2",
            ["Preamplificador externo"] = "Cabina.Posicion.PreampExterno",
            ["Interno y externo"] = "Cabina.Posicion.InternoYExterno",
            ["por omisión"] = "Cabina.Posicion.PorOmision",
            ["índice"] = "Cabina.Unidad.Indice",
            ["ppm"] = "Cabina.Unidad.Ppm",
        };

    private bool _recogiendo;

    /// <summary>Monta el mando a partir de lo que declara el equipo.</summary>
    /// <param name="equipo">Control del equipo.</param>
    /// <param name="rango">Rango que el equipo declara para este mando.</param>
    public VistaModeloMando(IEquipoAvanzado equipo, RangoDeMando rango)
    {
        ArgumentNullException.ThrowIfNull(equipo);
        ArgumentNullException.ThrowIfNull(rango);

        _equipo = equipo;
        Rango = rango;

        var (nombre, grupo) = Nombres.TryGetValue(rango.Mando, out var texto)
            ? texto
            : (string.Empty, "Cabina.Mando.Grupo.Otros");

        _claveDelNombre = nombre;
        _claveDelGrupo = grupo;
        Textos.AlCambiar(this, static vm => vm.OnPropertyChanged(string.Empty));

        Forma = rango switch
        {
            { EsDePosiciones: true } => FormaDelMando.Posiciones,
            { EsInterruptor: true } => FormaDelMando.Interruptor,
            _ => FormaDelMando.Escala,
        };

        // Un mando de posiciones que emite en la ultima (el acoplador: «Sintonizar») no la
        // ofrece en la lista: elegirla ahi la mandaba sin pasar por el vigilante del PTT, el
        // equipo la rechazaba y el mando se quedaba muerto. Se sintoniza desde el equipo.
        var etiquetas = rango.Etiquetas ?? [];
        _etiquetas = rango.TransmiteAlAccionar && etiquetas.Count > 1
            ? [.. etiquetas.Take(etiquetas.Count - 1)]
            : etiquetas;
    }

    /// <summary>
    /// Poner este valor pone el equipo en antena.
    /// </summary>
    /// <remarks>
    /// En un mando de posiciones solo emite la ultima (el acoplador: apagado, encendido,
    /// sintonizar); en los demas, cualquier valor. Es la misma regla que aplica el control.
    /// </remarks>
    /// <param name="valor">Valor que se quiere poner.</param>
    /// <returns>Verdadero si ponerlo transmite.</returns>
    public bool TransmiteCon(double valor) =>
        TransmiteAlAccionar && (!Rango.EsDePosiciones || valor >= Rango.Maximo);

    /// <summary>Rango declarado por el equipo.</summary>
    public RangoDeMando Rango { get; }

    /// <summary>Mando al que corresponde.</summary>
    public MandoDeEquipo Mando => Rango.Mando;

    /// <summary>Nombre del mando en espanol.</summary>
    public string Nombre => _claveDelNombre.Length > 0 ? Textos.T(_claveDelNombre) : Rango.Mando.ToString();

    /// <summary>Grupo al que pertenece, para poder agruparlos en la pantalla.</summary>
    public string Grupo => Textos.T(_claveDelGrupo);

    /// <summary>Con que control se acciona.</summary>
    public FormaDelMando Forma { get; }

    /// <summary>Nombres de las posiciones, cuando el mando las tiene, en el idioma en uso.</summary>
    public IReadOnlyList<string> Posiciones => [.. _etiquetas.Select(Traducir)];

    /// <summary>
    /// La posicion elegida tal y como la nombra el control del equipo (en espanol), para las
    /// cuentas que dependen del nombre (<see cref="VistaModeloEquipo.TextoCorto"/>).
    /// </summary>
    public string EtiquetaSinTraducir =>
        EsDePosiciones && Posicion >= 0 && Posicion < _etiquetas.Count ? _etiquetas[Posicion] : ValorTexto;

    /// <summary>Unidad que se escribe junto al valor.</summary>
    public string Unidad => Traducir(Rango.Unidad ?? string.Empty);

    /// <summary>
    /// Traduce el nombre de una posicion o de una unidad que da el control del equipo. Los
    /// controles las dan en espanol; lo que no esta en la lista (6 dB, LV1, SLOW1...) se queda tal cual.
    /// </summary>
    /// <param name="etiqueta">Lo que dice el control.</param>
    /// <returns>El texto en el idioma en uso.</returns>
    public static string Traducir(string etiqueta) =>
        ClavesDeLasPosiciones.TryGetValue(etiqueta, out var clave) ? Textos.T(clave) : etiqueta;

    /// <summary>Menor valor que admite.</summary>
    public double Minimo => Rango.Minimo;

    /// <summary>Mayor valor que admite.</summary>
    public double Maximo => Rango.Maximo;

    /// <summary>Salto entre valores consecutivos.</summary>
    public double Paso => Rango.Paso;

    /// <summary>El mando es una casilla de encendido y apagado.</summary>
    public bool EsInterruptor => Forma == FormaDelMando.Interruptor;

    /// <summary>El mando tiene posiciones con nombre.</summary>
    public bool EsDePosiciones => Forma == FormaDelMando.Posiciones;

    /// <summary>El mando es una escala continua.</summary>
    public bool EsEscala => Forma == FormaDelMando.Escala;

    /// <summary>
    /// Accionar este mando pone el equipo en antena.
    /// </summary>
    /// <remarks>
    /// El acoplador de antena es el caso tipico: sintonizar emite portadora. La interfaz avisa
    /// antes de tocarlo y lo acciona dentro de una transmision vigilada.
    /// </remarks>
    public bool TransmiteAlAccionar => Rango.TransmiteAlAccionar;

    /// <summary>El mando se puede leer pero no accionar.</summary>
    public bool SoloLectura => Rango.SoloLectura;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValorTexto))]
    [NotifyPropertyChangedFor(nameof(Encendido))]
    [NotifyPropertyChangedFor(nameof(Posicion))]
    private double _valor;

    [ObservableProperty]
    private bool _disponible = true;

    /// <summary>El mando esta encendido. Solo tiene sentido para los interruptores.</summary>
    public bool Encendido
    {
        get => Valor >= 0.5;
        set => Valor = value ? 1 : 0;
    }

    /// <summary>Posicion elegida. Solo tiene sentido para los mandos de posiciones.</summary>
    public int Posicion
    {
        get => (int)Math.Round(Math.Clamp(Valor, Rango.Minimo, Rango.Maximo) - Rango.Minimo);
        set => Valor = Rango.Minimo + value;
    }

    /// <summary>Valor tal y como se lee en la pantalla, con su unidad.</summary>
    public string ValorTexto
    {
        get
        {
            if (EsInterruptor) return Encendido ? Textos.T("Cabina.Mando.Encendido") : Textos.T("Cabina.Mando.Apagado");

            if (EsDePosiciones)
            {
                var indice = Posicion;
                return indice >= 0 && indice < Posiciones.Count ? Posiciones[indice] : string.Empty;
            }

            // Sin decimales cuando el paso es entero: «2400 Hz», no «2400,0 Hz».
            var numero = Paso >= 1
                ? Valor.ToString("N0", CultureInfo.CurrentCulture)
                : Valor.ToString("N1", CultureInfo.CurrentCulture);

            return Unidad.Length > 0 ? $"{numero} {Unidad}" : numero;
        }
    }

    /// <summary>Lee del equipo el valor que tiene ahora mismo.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task RecogerAsync(CancellationToken ct = default)
    {
        try
        {
            var leido = await _equipo.LeerMandoAsync(Mando, ct).ConfigureAwait(true);

            _recogiendo = true;
            Disponible = leido is not null;
            if (leido is { } valor) Valor = valor;
        }
        catch (OperationCanceledException)
        {
            // La ventana se esta cerrando.
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "No se ha podido leer el mando {Mando}.", Mando);
            Disponible = false;
        }
        finally
        {
            _recogiendo = false;
        }
    }

    /// <summary>
    /// Manda al equipo el valor que acaba de poner el operador.
    /// </summary>
    /// <remarks>
    /// No se envia lo que se lee del propio equipo: si no, cada lectura provocaria una
    /// escritura y el mando entraria en un bucle contra el dial fisico.
    /// </remarks>
    partial void OnValorChanged(double value)
    {
        if (_recogiendo) return;

        _ = EnviarAsync(value);
    }

    /// <summary>Cuando se toco por ultima vez desde el programa (Environment.TickCount64).</summary>
    private long _ultimoToque = long.MinValue / 2;

    private int _enviando;

    /// <summary>
    /// Tras tocarlo en el programa, cuanto se espera antes de volver a leerlo del equipo. Sin
    /// esto, el sondeo del frontal leeria el valor viejo entre dos muescas de la rueda y el
    /// mando daria saltos atras.
    /// </summary>
    public static TimeSpan CalmaTrasTocarlo { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Lee del equipo el valor, salvo que el operador lo este moviendo ahora mismo desde el
    /// programa. Es lo que usa el sondeo del frontal para reflejar lo que se toca en la radio.
    /// </summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    public Task RecogerSiNoSeEstaMoviendoAsync(CancellationToken ct = default)
    {
        if (Volatile.Read(ref _enviando) > 0
            || Environment.TickCount64 - Volatile.Read(ref _ultimoToque) < CalmaTrasTocarlo.TotalMilliseconds)
        {
            return Task.CompletedTask;
        }

        return RecogerAsync(ct);
    }

    private async Task EnviarAsync(double valor)
    {
        Volatile.Write(ref _ultimoToque, Environment.TickCount64);
        Interlocked.Increment(ref _enviando);
        try
        {
            await _equipo.EscribirMandoAsync(Mando, valor).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // Antes se apagaba el mando PARA SIEMPRE al primer fallo —un byte perdido bastaba— y
            // desde ahi la tecla del frontal no volvia a hacer nada. Ahora se vuelve a leer lo
            // que tiene de verdad el equipo: si contesta, el mando sigue vivo y ensena su valor.
            Log.Warning(ex, "No se ha podido accionar el mando {Mando}.", Mando);
            await RecogerAsync().ConfigureAwait(true);
        }
        finally
        {
            Interlocked.Decrement(ref _enviando);
        }
    }
}
