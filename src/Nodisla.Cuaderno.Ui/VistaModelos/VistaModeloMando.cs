using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.Puertos;
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
    private static readonly IReadOnlyDictionary<MandoDeEquipo, (string Nombre, string Grupo)> Nombres =
        new Dictionary<MandoDeEquipo, (string, string)>
        {
            [MandoDeEquipo.Potencia] = ("Potencia", "Nivel"),
            [MandoDeEquipo.GananciaRf] = ("Ganancia de RF", "Nivel"),
            [MandoDeEquipo.GananciaMicrofono] = ("Ganancia de micrófono", "Nivel"),
            [MandoDeEquipo.Volumen] = ("Volumen", "Nivel"),
            [MandoDeEquipo.Monitor] = ("Monitor", "Nivel"),
            [MandoDeEquipo.Compresor] = ("Compresor de voz", "Nivel"),

            [MandoDeEquipo.Atenuador] = ("Atenuador", "Recepción"),
            [MandoDeEquipo.Preamplificador] = ("Preamplificador", "Recepción"),
            [MandoDeEquipo.Agc] = ("Control automático de ganancia", "Recepción"),
            [MandoDeEquipo.SupresorDeRuido] = ("Supresor de ruido", "Recepción"),
            [MandoDeEquipo.NivelSupresorDeRuido] = ("Nivel del supresor", "Recepción"),
            [MandoDeEquipo.ReductorDeRuido] = ("Reductor de ruido", "Recepción"),
            [MandoDeEquipo.NivelReductorDeRuido] = ("Nivel del reductor", "Recepción"),
            [MandoDeEquipo.MuescaAutomatica] = ("Muesca automática", "Recepción"),
            [MandoDeEquipo.MuescaManual] = ("Muesca manual", "Recepción"),
            [MandoDeEquipo.FrecuenciaDeMuesca] = ("Frecuencia de la muesca", "Recepción"),
            [MandoDeEquipo.Contorno] = ("Contorno", "Recepción"),
            [MandoDeEquipo.FrecuenciaDeContorno] = ("Frecuencia del contorno", "Recepción"),
            [MandoDeEquipo.DesplazamientoFi] = ("Desplazamiento de FI", "Recepción"),
            [MandoDeEquipo.AnchoDeFiltro] = ("Ancho del filtro", "Recepción"),
            [MandoDeEquipo.FiltroDeTejado] = ("Filtro de tejado", "Recepción"),
            [MandoDeEquipo.Silenciador] = ("Silenciador", "Recepción"),

            [MandoDeEquipo.TonoCw] = ("Tono de telegrafía", "Telegrafía"),
            [MandoDeEquipo.VelocidadKeyer] = ("Velocidad del manipulador", "Telegrafía"),
            [MandoDeEquipo.BreakIn] = ("Escucha entre caracteres", "Telegrafía"),
            [MandoDeEquipo.RetardoBreakIn] = ("Retardo de la escucha", "Telegrafía"),

            [MandoDeEquipo.Vox] = ("Paso a transmisión por voz", "Transmisión"),
            [MandoDeEquipo.GananciaVox] = ("Ganancia del circuito de voz", "Transmisión"),
            [MandoDeEquipo.RetardoVox] = ("Retardo del circuito de voz", "Transmisión"),
            [MandoDeEquipo.Sintonizador] = ("Acoplador de antena", "Transmisión"),
            [MandoDeEquipo.Antena] = ("Antena", "Transmisión"),

            [MandoDeEquipo.Split] = ("Trabajo en dos frecuencias", "Frecuencia"),
            [MandoDeEquipo.Rit] = ("Desplazamiento de recepción", "Frecuencia"),
            [MandoDeEquipo.DesplazamientoRit] = ("Valor del desplazamiento de recepción", "Frecuencia"),
            [MandoDeEquipo.Xit] = ("Desplazamiento de transmisión", "Frecuencia"),
            [MandoDeEquipo.DesplazamientoXit] = ("Valor del desplazamiento de transmisión", "Frecuencia"),

            [MandoDeEquipo.FiltroEstrecho] = ("Filtro estrecho (NAR)", "Recepción"),
            [MandoDeEquipo.Apf] = ("Filtro de pico de audio (APF)", "Recepción"),
            [MandoDeEquipo.FrecuenciaApf] = ("Frecuencia del APF", "Recepción"),
            [MandoDeEquipo.Bloqueo] = ("Bloqueo del dial (LOCK)", "Frecuencia"),
            [MandoDeEquipo.SintoniaFinaRapida] = ("Sintonía fina o rápida (FINE/FAST)", "Frecuencia"),
            [MandoDeEquipo.TonoDeReferenciaCw] = ("Tono de referencia (SPOT)", "Telegrafía"),
            [MandoDeEquipo.NivelAmc] = ("Nivel del AMC", "Transmisión"),
            [MandoDeEquipo.AntiVox] = ("Antivox", "Transmisión"),
            [MandoDeEquipo.ContrastePantalla] = ("Contraste de la pantalla", "Pantalla"),
            [MandoDeEquipo.BrilloPantalla] = ("Brillo de la pantalla", "Pantalla"),
            [MandoDeEquipo.EspectroVelocidad] = ("Velocidad del analizador (SPEED)", "Pantalla"),
            [MandoDeEquipo.EspectroAncho] = ("Ancho del analizador (SPAN)", "Pantalla"),
            [MandoDeEquipo.EspectroModo] = ("Modo del analizador (CENTER/3DSS/EXPAND)", "Pantalla"),
            [MandoDeEquipo.EspectroNivel] = ("Nivel del analizador", "Pantalla"),
            [MandoDeEquipo.EspectroPicos] = ("Picos del analizador", "Pantalla"),
            [MandoDeEquipo.EspectroColor] = ("Color del analizador", "Pantalla"),
            [MandoDeEquipo.FuncionDelMandoFunc] = ("Función del mando FUNC", "Frontal"),
            [MandoDeEquipo.FuncionDelMandoDsp] = ("Función del mando DSP", "Frontal"),
        };

    private readonly IEquipoAvanzado _equipo;
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
            : (rango.Mando.ToString(), "Otros");

        Nombre = nombre;
        Grupo = grupo;

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
        Posiciones = rango.TransmiteAlAccionar && etiquetas.Count > 1
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
    public string Nombre { get; }

    /// <summary>Grupo al que pertenece, para poder agruparlos en la pantalla.</summary>
    public string Grupo { get; }

    /// <summary>Con que control se acciona.</summary>
    public FormaDelMando Forma { get; }

    /// <summary>Nombres de las posiciones, cuando el mando las tiene.</summary>
    public IReadOnlyList<string> Posiciones { get; }

    /// <summary>Unidad que se escribe junto al valor.</summary>
    public string Unidad => Rango.Unidad ?? string.Empty;

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
            if (EsInterruptor) return Encendido ? "Encendido" : "Apagado";

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
