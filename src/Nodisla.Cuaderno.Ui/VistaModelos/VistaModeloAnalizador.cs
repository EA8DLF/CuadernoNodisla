using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Recursos;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El analizador de espectro del propio equipo en la pantalla del frontal dibujado.
/// </summary>
/// <remarks>
/// <para>
/// Lo que se ve es lo que manda la radio por su puente FT4222: la misma traza y el mismo
/// ancho que tiene en su pantalla, en cualquier modo (SSB, CW, FM, digitales). Nada de aqui
/// manda ordenes al equipo.
/// </para>
/// <para>
/// Como se ve (SPAN, CENTER/CURSOR/FIX, SPEED, 3DSS y EXPAND) lo dice cada trama, y ademas
/// llega por CAT (<see cref="Ajustar"/>) en cuanto se toca una tecla de la pantalla del
/// programa o el sondeo del frontal ve un cambio hecho en la radio. Lo del CAT manda durante
/// <see cref="PrioridadDelCat"/> tras cambiar, para que la trama que ya venia de camino no
/// deshaga el cambio recien pulsado; despues manda la trama, que es lo que pinta la radio.
/// </para>
/// <para>
/// Las trazas llegan de un hilo de fondo a unas diez por segundo. Se guarda solo la ultima y
/// se pinta cuando la interfaz tiene hueco: si la interfaz va lenta, se saltan trazas en vez
/// de acumular cola.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAnalizador : ObservableObject
{
    /// <summary>Lo que se tarda en volver a creer a la trama tras un cambio por CAT.</summary>
    public static readonly TimeSpan PrioridadDelCat = TimeSpan.FromMilliseconds(800);

    /// <summary>Nombres de SPEED por su numero (los del menu del FT-710, orden SS00).</summary>
    public static readonly IReadOnlyList<string> Velocidades = ["SLOW1", "SLOW2", "FAST1", "FAST2", "FAST3", "STOP"];

    private readonly IAnalizadorDeEspectro? _analizador;
    private readonly PintorDelAnalizador _pintor = new();
    private readonly Func<long> _milisegundos;
    private Dispatcher? _interfaz;
    private TrazaDeEspectro? _pendiente;
    private AjusteDelAnalizador? _delCat;
    private long _momentoDelCat;
    private int _usuarios;

    /// <summary>Se ve en el frontal.</summary>
    /// <param name="analizador">El analizador del equipo; nulo si en este arranque no lo hay.</param>
    /// <param name="milisegundos">Reloj en milisegundos; nulo, el del sistema (para pruebas).</param>
    /// <param name="audio">
    /// La entrada de audio del codec de la radio, para el osciloscopio y el AF-FFT de MULTI. Es
    /// la misma que usa el modem: solo se escuchan sus bloques, no se abre otro dispositivo.
    /// </param>
    public VistaModeloAnalizador(IAnalizadorDeEspectro? analizador, Func<long>? milisegundos = null, IEntradaDeAudio? audio = null)
    {
        _analizador = analizador;
        _audio = audio;
        _milisegundos = milisegundos ?? (() => Environment.TickCount64);
        if (_analizador is not null)
        {
            _analizador.TrazaRecibida += AlRecibirTraza;
            _analizador.EstadoCambiado += (_, _) => EnLaInterfaz(ActualizarEstado);
        }

        ActualizarEstado();
    }

    /// <summary>La traza, 850 × 110 puntos.</summary>
    public WriteableBitmap? ImagenDeLaTraza { get; private set; }

    /// <summary>La cascada, 850 × 120 puntos, lo nuevo arriba.</summary>
    public WriteableBitmap? ImagenDeLaCascada { get; private set; }

    /// <summary>La vista 3DSS, 850 × 230 puntos: las ultimas pasadas en perspectiva.</summary>
    public WriteableBitmap? ImagenTresD { get; private set; }

    /// <summary>Hay trazas frescas del equipo.</summary>
    [ObservableProperty]
    private bool _recibiendo;

    /// <summary>Titulo del aviso cuando no hay trazas.</summary>
    [ObservableProperty]
    private string _tituloDelAviso = string.Empty;

    /// <summary>Explicacion del aviso cuando no hay trazas.</summary>
    [ObservableProperty]
    private string _textoDelAviso = string.Empty;

    /// <summary>Linea de encima, como la del equipo: «CENTER  SPEED FAST1  SPAN 200kHz».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloEnPantalla))]
    private string _rotulo = string.Empty;

    /// <summary>Ancho del analizador en hercios, para la escala del frontal.</summary>
    [ObservableProperty]
    private double _spanHz = 200_000;

    /// <summary>Donde cae el VFO, de 0 (izquierda) a 1 (derecha).</summary>
    [ObservableProperty]
    private double _posicionDelVfo = 0.5;

    /// <summary>El VFO cae dentro de lo que se ve.</summary>
    [ObservableProperty]
    private bool _vfoVisible = true;

    /// <summary>La radio esta en 3DSS: se pinta la vista 3D en vez de traza y cascada.</summary>
    [ObservableProperty]
    private bool _enTresD;

    /// <summary>La radio tiene el analizador ampliado (EXPAND): el visor le da mas alto.</summary>
    [ObservableProperty]
    private bool _ampliado;

    /// <summary>SPEED, como SS00: 0 SLOW1 … 4 FAST3, 5 STOP.</summary>
    [ObservableProperty]
    private int _velocidad = 2;

    /// <summary>CENTER, CURSOR o FIX.</summary>
    [ObservableProperty]
    private ModoDelAnalizador _posicion = ModoDelAnalizador.Centro;

    /// <summary>En FIX, donde empieza la escala (hercios); 0 en CENTER y CURSOR.</summary>
    [ObservableProperty]
    private double _inicioFijoHz;

    /// <summary>Ultima traza pintada.</summary>
    public TrazaDeEspectro? UltimaTraza { get; private set; }

    /// <summary>Empieza a leer (la pantalla se ha mostrado). Se cuenta: dos pantallas, un analizador.</summary>
    public void Arrancar()
    {
        _interfaz ??= Dispatcher.CurrentDispatcher;
        if (_analizador is null) return;
        if (Interlocked.Increment(ref _usuarios) == 1) _analizador.Iniciar();
    }

    /// <summary>Deja de leer si ya nadie lo muestra.</summary>
    public void Parar()
    {
        if (_analizador is null) return;
        if (Interlocked.Decrement(ref _usuarios) == 0) _ = _analizador.DetenerAsync();
    }

    /// <summary>Pinta una traza (hilo de interfaz).</summary>
    /// <param name="traza">La traza.</param>
    public void Pintar(TrazaDeEspectro traza)
    {
        ArgumentNullException.ThrowIfNull(traza);
        UltimaTraza = traza;
        if (!MandaElCat()) AplicarLaTraza(traza);

        _pintor.Velocidad = Velocidad;
        _pintor.TresD = EnTresD;
        _pintor.Pintar(traza.Niveles);

        if (ImagenDeLaTraza is null || ImagenDeLaCascada is null || ImagenTresD is null)
        {
            ImagenDeLaTraza = new WriteableBitmap(
                PintorDelAnalizador.Ancho, PintorDelAnalizador.AltoDeTraza, 96, 96, PixelFormats.Bgra32, null);
            ImagenDeLaCascada = new WriteableBitmap(
                PintorDelAnalizador.Ancho, PintorDelAnalizador.AltoDeCascada, 96, 96, PixelFormats.Bgra32, null);
            ImagenTresD = new WriteableBitmap(
                PintorDelAnalizador.Ancho, PintorDelAnalizador.AltoTresD, 96, 96, PixelFormats.Bgra32, null);
            OnPropertyChanged(nameof(ImagenDeLaTraza));
            OnPropertyChanged(nameof(ImagenDeLaCascada));
            OnPropertyChanged(nameof(ImagenTresD));
        }

        var paso = PintorDelAnalizador.Ancho * 4;
        ImagenDeLaTraza.WritePixels(
            new Int32Rect(0, 0, PintorDelAnalizador.Ancho, PintorDelAnalizador.AltoDeTraza), _pintor.Traza, paso, 0);
        ImagenDeLaCascada.WritePixels(
            new Int32Rect(0, 0, PintorDelAnalizador.Ancho, PintorDelAnalizador.AltoDeCascada), _pintor.Cascada, paso, 0);
        if (EnTresD)
        {
            ImagenTresD.WritePixels(
                new Int32Rect(0, 0, PintorDelAnalizador.Ancho, PintorDelAnalizador.AltoTresD), _pintor.VistaTresD, paso, 0);
        }

        ColocarElVfo();
        Recibiendo = true;
        MirarElAudio();
    }

    /// <summary>
    /// Lo que el CAT dice del analizador de la radio: se ve al momento, sin esperar a la trama.
    /// </summary>
    /// <param name="ajuste">El ajuste leido o recien mandado; nulo si el equipo no lo da.</param>
    public void Ajustar(AjusteDelAnalizador? ajuste)
    {
        if (ajuste is null || ajuste == _delCat) return;
        _delCat = ajuste;
        _momentoDelCat = _milisegundos();

        if (ajuste.SpanHz is > 0 and { } span) SpanHz = span;
        if (ajuste.Velocidad is { } velocidad) Velocidad = velocidad;
        Posicion = ajuste.Posicion;
        EnTresD = ajuste.TresD;
        Ampliado = ajuste.Ampliado;

        // En FIX el comienzo de la escala solo lo trae la trama.
        InicioFijoHz = Posicion == ModoDelAnalizador.Fijo && UltimaTraza is { Modo: ModoDelAnalizador.Fijo } t ? t.InicioHz : 0;
        Rotulo = RotuloDe(Posicion, Velocidad, (int)SpanHz, InicioFijoHz);
        ColocarElVfo();
    }

    /// <summary>
    /// Sigue lo que el CAT del equipo dice del analizador: cada tecla de la pantalla y cada
    /// cambio que el sondeo ve en la radio se pinta al momento.
    /// </summary>
    /// <param name="equipo">El equipo del frontal.</param>
    public void Seguir(VistaModeloEquipo equipo)
    {
        ArgumentNullException.ThrowIfNull(equipo);
        Ajustar(equipo.AjusteDelAnalizador);
        equipo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(VistaModeloEquipo.AjusteDelAnalizador) or null or "")
            {
                EnLaInterfaz(() => Ajustar(equipo.AjusteDelAnalizador));
            }
        };
    }

    /// <summary>La linea del equipo encima del analizador.</summary>
    /// <param name="traza">La traza.</param>
    /// <returns>P. ej. «CENTER  SPEED FAST1  SPAN 200kHz».</returns>
    public static string RotuloDe(TrazaDeEspectro traza)
    {
        ArgumentNullException.ThrowIfNull(traza);
        return RotuloDe(traza.Modo, traza.Velocidad, traza.SpanHz, traza.Modo == ModoDelAnalizador.Fijo ? traza.InicioHz : 0);
    }

    /// <summary>La linea del equipo encima del analizador.</summary>
    /// <param name="posicion">CENTER, CURSOR o FIX.</param>
    /// <param name="velocidad">SPEED, como SS00.</param>
    /// <param name="spanHz">SPAN en hercios.</param>
    /// <param name="inicioFijoHz">En FIX, donde empieza la escala; 0 si no se sabe.</param>
    /// <returns>P. ej. «CENTER  SPEED FAST1  SPAN 200kHz».</returns>
    public static string RotuloDe(ModoDelAnalizador posicion, int velocidad, int spanHz, double inicioFijoHz)
    {
        var modo = posicion switch
        {
            ModoDelAnalizador.Centro => "CENTER",
            ModoDelAnalizador.Cursor => "CURSOR",
            ModoDelAnalizador.Fijo => "FIX",
            _ => "SCOPE",
        };

        var span = spanHz >= 1_000_000
            ? string.Create(CultureInfo.InvariantCulture, $"{spanHz / 1_000_000.0:0.#}MHz")
            : string.Create(CultureInfo.InvariantCulture, $"{spanHz / 1000.0:0.#}kHz");
        var rapidez = velocidad >= 0 && velocidad < Velocidades.Count
            ? Velocidades[velocidad]
            : velocidad.ToString(CultureInfo.InvariantCulture);

        return posicion == ModoDelAnalizador.Fijo && inicioFijoHz > 0
            ? string.Create(CultureInfo.InvariantCulture, $"FIX {inicioFijoHz / 1000.0:0}kHz  SPEED {rapidez}  SPAN {span}")
            : $"{modo}  SPEED {rapidez}  SPAN {span}";
    }

    private bool MandaElCat() =>
        _delCat is not null && _milisegundos() - _momentoDelCat < PrioridadDelCat.TotalMilliseconds;

    private void AplicarLaTraza(TrazaDeEspectro traza)
    {
        if (traza.SpanHz > 0) SpanHz = traza.SpanHz;
        Velocidad = traza.Velocidad;
        Posicion = traza.Modo;
        EnTresD = traza.TresD;
        Ampliado = traza.Ampliado;
        InicioFijoHz = traza.Modo == ModoDelAnalizador.Fijo ? traza.InicioHz : 0;
        Rotulo = RotuloDe(traza);
    }

    /// <summary>Coloca la marca del VFO con el ancho que se esta ensenando.</summary>
    private void ColocarElVfo()
    {
        if (UltimaTraza is not { } traza || SpanHz <= 0) return;
        var inicio = InicioFijoHz > 0 ? InicioFijoHz : traza.VfoHz - (SpanHz / 2);
        var posicion = (traza.VfoHz - inicio) / SpanHz;
        VfoVisible = posicion is >= 0 and <= 1;
        PosicionDelVfo = Math.Clamp(posicion, 0, 1);
    }

    private void AlRecibirTraza(object? remitente, TrazaDeEspectro traza)
    {
        // Solo se encola un pintado si no habia ya uno pendiente.
        if (Interlocked.Exchange(ref _pendiente, traza) is null)
        {
            EnLaInterfaz(() =>
            {
                var ultima = Interlocked.Exchange(ref _pendiente, null);
                if (ultima is not null) Pintar(ultima);
            });
        }
    }

    private void EnLaInterfaz(Action accion)
    {
        var interfaz = _interfaz ?? Application.Current?.Dispatcher;
        if (interfaz is null || interfaz.CheckAccess()) accion();
        else interfaz.BeginInvoke(accion, DispatcherPriority.Render);
    }

    private void ActualizarEstado()
    {
        if (_analizador is null)
        {
            Recibiendo = false;
            Rotulo = _delCat is null ? "SIN ANALIZADOR" : RotuloDe(Posicion, Velocidad, (int)SpanHz, InicioFijoHz);
            TituloDelAviso = "SIN ANALIZADOR DEL EQUIPO";
            TextoDelAviso = "Sin radio conectada no hay espectro.";
            return;
        }

        var estado = _analizador.Estado;
        if (estado == EstadoDelAnalizador.Recibiendo) return;

        Recibiendo = false;

        // Sin trazas, el rotulo sigue diciendo lo que el CAT sabe del analizador de la radio.
        Rotulo = _delCat is null ? "SCOPE  SIN DATOS" : RotuloDe(Posicion, Velocidad, (int)SpanHz, InicioFijoHz);
        (TituloDelAviso, TextoDelAviso) = estado switch
        {
            EstadoDelAnalizador.SinBiblioteca => (
                "FALTA LA BIBLIOTECA DE FTDI",
                $"{_analizador.Motivo} Sin ella no se puede leer el analizador de la radio."),
            EstadoDelAnalizador.SinDispositivo => (
                "LA RADIO NO ENTREGA SU ANALIZADOR",
                "No aparece su puente USB (FT4222). ¿Está la radio encendida y con el cable USB puesto?"),
            EstadoDelAnalizador.SinTramas => (
                "LA RADIO NO MANDA SU ESPECTRO",
                _analizador.Motivo),
            EstadoDelAnalizador.Fallo => ("ANALIZADOR CON FALLO", _analizador.Motivo),
            _ => ("ANALIZADOR PARADO", "Se pone en marcha al mostrar esta pantalla."),
        };
    }
}

/// <summary>Lo que dice el CAT del analizador de la radio (SS05, SS00 y SS06).</summary>
/// <param name="SpanHz">SPAN en hercios; nulo si no se sabe.</param>
/// <param name="Velocidad">SPEED como SS00 (0 SLOW1 … 5 STOP); nulo si no se sabe.</param>
/// <param name="Posicion">CENTER, CURSOR o FIX.</param>
/// <param name="TresD">Vista 3DSS; si no, cascada.</param>
/// <param name="Ampliado">EXPAND.</param>
public sealed record AjusteDelAnalizador(
    int? SpanHz,
    int? Velocidad,
    ModoDelAnalizador Posicion,
    bool TresD,
    bool Ampliado);
