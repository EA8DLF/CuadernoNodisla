using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Modos.Cw;
using Nodisla.Cuaderno.Modos.Senal;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Digital;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una palabra del texto de telegrafía, ya clasificada.</summary>
/// <param name="Texto">La palabra.</param>
/// <param name="Tipo">Si es indicativo, llamada, abreviatura, prosigno...</param>
/// <param name="Ayuda">Lo que sale al pasar el ratón: el significado o el país; nulo si nada.</param>
public sealed record PalabraCw(string Texto, TipoDePalabraCw Tipo, string? Ayuda = null)
{
    /// <summary>Se puede pasar al contacto nuevo.</summary>
    public bool EsIndicativo => Tipo is TipoDePalabraCw.Indicativo;

    /// <summary>No es un indicativo de otro (se pinta como texto).</summary>
    public bool EsTexto => !EsIndicativo;

    /// <summary>Tiene significado o país que enseñar.</summary>
    public bool TieneAyuda => !string.IsNullOrEmpty(Ayuda);
}

/// <summary>El texto de una señal: el de la principal o el de una del «skimmer».</summary>
public sealed partial class LineaCw : ObservableObject
{
    private readonly int _palabrasMaximas;
    private readonly Func<string, PalabraCw> _clasificar;
    private string _enCurso = string.Empty;

    /// <summary>Monta la línea.</summary>
    /// <param name="canal">Canal del decodificador que la escribe.</param>
    /// <param name="palabrasMaximas">Palabras que se guardan; las más viejas se van.</param>
    /// <param name="clasificar">Convierte una palabra en su <see cref="PalabraCw"/> (tipo y ayuda).</param>
    public LineaCw(int canal, int palabrasMaximas, Func<string, PalabraCw> clasificar)
    {
        Canal = canal;
        _palabrasMaximas = palabrasMaximas;
        _clasificar = clasificar;
    }

    /// <summary>Canal del decodificador.</summary>
    public int Canal { get; }

    /// <summary>Las palabras, la última quizá a medias.</summary>
    public ObservableCollection<PalabraCw> Palabras { get; } = [];

    /// <summary>Tono de la señal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rotulo))]
    private double _tonoHz;

    /// <summary>Velocidad de la señal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rotulo))]
    private double _wpm;

    /// <summary>Tono y velocidad, para la cabecera de la línea.</summary>
    public string Rotulo => Textos.F("Cabina.Cw.RotuloSenal", Math.Round(TonoHz), Math.Round(Wpm));

    /// <summary>El texto entero, tal cual.</summary>
    public string Texto => string.Join(' ', Palabras.Select(p => p.Texto));

    /// <summary>Las últimas palabras, para la versión reducida de la cabina.</summary>
    public string Ultimo => string.Join(' ', Palabras.Skip(Math.Max(0, Palabras.Count - 14)).Select(p => p.Texto));

    /// <summary>
    /// La «traducción»: el significado de las abreviaturas, códigos Q y prosignos de las últimas
    /// palabras, en orden.
    /// </summary>
    public string Traduccion => string.Join("  ·  ", Palabras.Skip(Math.Max(0, Palabras.Count - 40))
        .Where(p => p.Tipo is TipoDePalabraCw.Abreviatura or TipoDePalabraCw.Llamada or TipoDePalabraCw.Prosigno && p.TieneAyuda)
        .Select(p => $"{p.Texto} = {p.Ayuda}")
        .Reverse().Distinct().Take(8).Reverse());

    /// <summary>Añade un carácter, un prosigno o un espacio.</summary>
    public void Anadir(string simbolo)
    {
        if (simbolo == " ")
        {
            _enCurso = string.Empty;
            return;
        }

        // Un prosigno es palabra propia.
        if (simbolo.StartsWith('<'))
        {
            _enCurso = string.Empty;
            Poner(simbolo, nueva: true);
            return;
        }

        var nueva = _enCurso.Length == 0;
        _enCurso += simbolo;
        Poner(_enCurso, nueva);
    }

    /// <summary>Lo borra todo.</summary>
    public void Borrar()
    {
        Palabras.Clear();
        _enCurso = string.Empty;
        Avisar();
    }

    private void Poner(string palabra, bool nueva)
    {
        var p = _clasificar(palabra);
        if (nueva || Palabras.Count == 0) Palabras.Add(p);
        else Palabras[^1] = p;
        while (Palabras.Count > _palabrasMaximas) Palabras.RemoveAt(0);
        Avisar();
    }

    private void Avisar()
    {
        OnPropertyChanged(nameof(Texto));
        OnPropertyChanged(nameof(Ultimo));
        OnPropertyChanged(nameof(Traduccion));
    }
}

/// <summary>Cómo llega el audio de la entrada, para el indicador del panel.</summary>
public enum NivelDeEntradaCw
{
    /// <summary>No llega audio.</summary>
    SinAudio,

    /// <summary>Muy bajo: por debajo de −50 dBFS de media.</summary>
    Bajo,

    /// <summary>Bien.</summary>
    Bien,

    /// <summary>Recortado: muestras a fondo de escala.</summary>
    Saturado,
}

/// <summary>
/// La telegrafía: el decodificador propio sobre el audio de recepción, para la página CW y la
/// versión reducida de la cabina.
/// </summary>
/// <remarks>
/// <para>
/// Escucha mientras se ve (la página CW, o la cabina con el equipo en CW o pedido a mano) y no
/// está en pausa. <b>No transmite nada</b> ni manda órdenes al equipo: del CAT solo lee el modo y
/// el tono de telegrafía (pitch).
/// </para>
/// <para>
/// El audio llega por el hilo de la tarjeta y ahí mismo se decodifica y se mide; el texto se
/// apunta en una cola y se vuelca en pantalla diez veces por segundo, en el hilo de la interfaz.
/// </para>
/// </remarks>
public sealed partial class VistaModeloCw : ObservableObject
{
    /// <summary>Palabras de la línea principal que se guardan.</summary>
    public const int PalabrasDeLaPrincipal = 400;

    /// <summary>Palabras de cada señal del skimmer.</summary>
    public const int PalabrasDeLasOtras = 10;

    /// <summary>Segundos que guarda «Grabar».</summary>
    public const int SegundosDeGrabacion = 60;

    /// <summary>Tono más bajo que se puede escribir.</summary>
    public const int TonoMinimo = 300;

    /// <summary>Tono más alto que se puede escribir.</summary>
    public const int TonoMaximo = 1200;

    private readonly AjustesDelPrograma _ajustes;
    private readonly IEntradaDeAudio? _entrada;
    private readonly IResolutorDxcc? _dxcc;
    private readonly DecodificadorCw _decodificador;
    private readonly ConcurrentQueue<TextoCw> _cola = new();
    private readonly DispatcherTimer? _reloj;
    private readonly object _cerrojoDeLaGrabacion = new();
    private List<float>? _grabacion;
    private int _frecuenciaDeLaGrabacion;
    private bool _escuchando;
    private int _canalPrincipal = -1;

    // Medida de la entrada (hilo del audio): energía y recortes de los últimos bloques.
    private double _energia;
    private long _muestras;
    private long _recortadas;
    private long _ultimoBloque;

    /// <summary>Monta el modelo.</summary>
    /// <param name="ajustes">Ajustes del programa (los de CW se leen de ahí).</param>
    /// <param name="entrada">Audio de recepción. Puede faltar.</param>
    /// <param name="conReloj">Volcar solo cada 100 ms. Las pruebas lo quitan y llaman a <see cref="Refrescar"/>.</param>
    /// <param name="dxcc">Para el país de los indicativos. Puede faltar.</param>
    public VistaModeloCw(AjustesDelPrograma ajustes, IEntradaDeAudio? entrada, bool conReloj = true, IResolutorDxcc? dxcc = null)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        _ajustes = ajustes;
        _entrada = entrada;
        _dxcc = dxcc;
        _decodificador = new DecodificadorCw(ajustes.Cw.Opciones());
        _decodificador.TextoDecodificado += (_, t) => _cola.Enqueue(t);
        Principal = new LineaCw(-1, PalabrasDeLaPrincipal, Clasificar);
        Principal.Palabras.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SinTexto));
        TonoHz = ajustes.Cw.TonoPorOmisionHz;
        TonoEscrito = ajustes.Cw.TonoPorOmisionHz.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (conReloj)
        {
            _reloj = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            _reloj.Tick += (_, _) => Refrescar();
        }
    }

    /// <summary>Alguien ha pulsado un indicativo del texto.</summary>
    public event EventHandler<string>? IndicativoElegido;

    /// <summary>Se ha pedido abrir la página CW (desde la versión reducida de la cabina).</summary>
    public event EventHandler? AbrirPaginaPedido;

    /// <summary>El motor (para las pruebas y el audio simulado).</summary>
    public DecodificadorCw Decodificador => _decodificador;

    /// <summary>El texto de la señal principal.</summary>
    public LineaCw Principal { get; }

    /// <summary>Las demás señales de la banda de paso.</summary>
    public ObservableCollection<LineaCw> Otras { get; } = [];

    /// <summary>El glosario de telegrafía, para la página.</summary>
    public IReadOnlyList<EntradaDelGlosarioCw> Glosario => GlosarioCw.Entradas;

    /// <summary>El indicativo de la estación, para resaltarlo.</summary>
    public string? MiIndicativo { get; set; }

    /// <summary>Carpeta de datos del programa, donde van las grabaciones.</summary>
    public string? CarpetaDeDatos { get; set; }

    /// <summary>El equipo está en CW (lo dice el CAT).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    private bool _equipoEnCw;

    /// <summary>El operador ha pedido verlo en la cabina aunque el equipo no esté en CW.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    private bool _mostrarAMano;

    /// <summary>Se está viendo la página CW.</summary>
    [ObservableProperty]
    private bool _paginaVisible;

    /// <summary>En pausa: no se escucha el audio.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDePausa))]
    private bool _pausado;

    /// <summary>El tono está fijado (a mano, en el espectro o al pitch); si no, AUTO.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDeFijar))]
    [NotifyPropertyChangedFor(nameof(RotuloDelModo))]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaBusqueda))]
    private bool _fijo;

    /// <summary>Tono de la señal principal (Hz).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TonoTexto))]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaBusqueda))]
    private double _tonoHz;

    /// <summary>El tono escrito a mano en la casilla (se aplica con Intro).</summary>
    [ObservableProperty]
    private string _tonoEscrito = "700";

    /// <summary>Velocidad de la señal principal (WPM); cero si aún no se sabe.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WpmTexto))]
    private double _wpm;

    /// <summary>Lo que destaca la señal sobre el ruido (dB).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NivelPorCiento))]
    [NotifyPropertyChangedFor(nameof(NivelTexto))]
    private double _nivelDb;

    /// <summary>Confianza en lo que se lee, de 0 a 1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConfianzaTexto))]
    private double _confianza;

    /// <summary>La principal está enganchada a una señal de telegrafía.</summary>
    [ObservableProperty]
    private bool _enganchado;

    /// <summary>En AUTO, la principal está anclada a una señal y no busca otra.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaBusqueda))]
    private bool _anclado;

    /// <summary>Segundos que lleva la principal sin señal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaBusqueda))]
    private double _segundosSinSenal;

    /// <summary>La llave de la principal está abajo ahora mismo.</summary>
    [ObservableProperty]
    private bool _marcando;

    /// <summary>La foto del decodificador para el espectro.</summary>
    [ObservableProperty]
    private EstadoCw _estado = EstadoCw.Vacio;

    /// <summary>El tono de telegrafía del equipo (pitch), si el CAT lo da.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDeFijar))]
    private double? _pitchDelEquipoHz;

    /// <summary>La entrada de audio está abierta.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinAudio))]
    private bool _hayAudio;

    /// <summary>Cómo llega el audio de la entrada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntradaTexto))]
    [NotifyPropertyChangedFor(nameof(EntradaBaja))]
    [NotifyPropertyChangedFor(nameof(EntradaSaturada))]
    [NotifyPropertyChangedFor(nameof(EntradaBien))]
    private NivelDeEntradaCw _nivelDeEntrada;

    /// <summary>Nivel medio de la entrada (dBFS).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EntradaTexto))]
    private double _entradaDbfs = -120;

    /// <summary>Hay una grabación en marcha.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDeGrabar))]
    private bool _grabando;

    /// <summary>Segundos grabados.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDeGrabar))]
    private int _segundosGrabados;

    /// <summary>Se enseña la línea de traducción de las abreviaturas.</summary>
    [ObservableProperty]
    private bool _mostrarTraduccion = true;

    /// <summary>Un aviso para el operador, o vacío.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAviso))]
    private string _aviso = string.Empty;

    /// <summary>La entrada de audio está cerrada: se ofrece «Escuchar».</summary>
    public bool SinAudio => !HayAudio;

    /// <summary>Hay aviso que enseñar.</summary>
    public bool HayAviso => Aviso.Length > 0;

    /// <summary>Todavía no hay texto: se enseña qué se está esperando.</summary>
    public bool SinTexto => Principal.Palabras.Count == 0;

    /// <summary>Se ve la versión reducida en la cabina.</summary>
    public bool Visible => EquipoEnCw || MostrarAMano;

    /// <summary>Se está escuchando el audio ahora mismo.</summary>
    public bool Escuchando => _escuchando;

    /// <summary>Tono para leer.</summary>
    public string TonoTexto => Textos.F("Cabina.Cw.Tono", Math.Round(TonoHz));

    /// <summary>Velocidad para leer.</summary>
    public string WpmTexto => Wpm > 0 ? Textos.F("Cabina.Cw.Wpm", Math.Round(Wpm)) : Textos.T("Cabina.Cw.WpmSinDato");

    /// <summary>Nivel para leer.</summary>
    public string NivelTexto => Textos.F("Cabina.Cw.Nivel", Math.Round(NivelDb));

    /// <summary>Confianza para leer.</summary>
    public string ConfianzaTexto => Textos.F("Cabina.Cw.Confianza", Math.Round(Confianza * 100));

    /// <summary>Barra de nivel: de 0 a 30 dB sobre el ruido.</summary>
    public double NivelPorCiento => Math.Clamp(NivelDb / 30 * 100, 0, 100);

    /// <summary>Buscando, enganchado en N Hz o fijo, con el tiempo sin señal.</summary>
    public string EstadoDeLaBusqueda
    {
        get
        {
            var tono = Math.Round(TonoHz);
            var principal = Fijo ? Textos.F("Cabina.Cw.EstadoFijo", tono)
                : Anclado ? Textos.F("Cabina.Cw.EstadoEnganchado", tono)
                : Textos.T("Cabina.Cw.EstadoBuscando");
            return SegundosSinSenal >= 2 && (Fijo || Anclado)
                ? $"{principal} · {Textos.F("Cabina.Cw.SinSenal", Math.Floor(SegundosSinSenal))}"
                : principal;
        }
    }

    /// <summary>El indicador de la entrada, para leer.</summary>
    public string EntradaTexto => NivelDeEntrada switch
    {
        NivelDeEntradaCw.SinAudio => Textos.T("Cabina.Cw.EntradaSinAudio"),
        NivelDeEntradaCw.Bajo => Textos.F("Cabina.Cw.EntradaBaja", Math.Round(EntradaDbfs)),
        NivelDeEntradaCw.Saturado => Textos.T("Cabina.Cw.EntradaSaturada"),
        _ => Textos.F("Cabina.Cw.EntradaBien", Math.Round(EntradaDbfs)),
    };

    /// <summary>La entrada llega baja (o no llega).</summary>
    public bool EntradaBaja => NivelDeEntrada is NivelDeEntradaCw.Bajo or NivelDeEntradaCw.SinAudio;

    /// <summary>La entrada llega recortada.</summary>
    public bool EntradaSaturada => NivelDeEntrada is NivelDeEntradaCw.Saturado;

    /// <summary>La entrada llega bien.</summary>
    public bool EntradaBien => NivelDeEntrada is NivelDeEntradaCw.Bien;

    /// <summary>«Grabar 60 s» o los segundos que van.</summary>
    public string RotuloDeGrabar => Grabando ? Textos.F("Cabina.Cw.Grabando", SegundosGrabados) : Textos.T("Cabina.Cw.Grabar");

    /// <summary>«Pausa» o «Seguir».</summary>
    public string RotuloDePausa => Pausado ? Textos.T("Cabina.Cw.Seguir") : Textos.T("Cabina.Cw.Pausa");

    /// <summary>«Fijar» (al pitch o al tono actual) o «Auto».</summary>
    public string RotuloDeFijar => Fijo
        ? Textos.T("Cabina.Cw.Auto")
        : PitchDelEquipoHz is { } p ? Textos.F("Cabina.Cw.FijarAlPitch", Math.Round(p)) : Textos.T("Cabina.Cw.Fijar");

    /// <summary>«AUTO» o «FIJO», junto al tono.</summary>
    public string RotuloDelModo => Fijo ? Textos.T("Cabina.Cw.ModoFijo") : Textos.T("Cabina.Cw.ModoAuto");

    /// <summary>Lo que dice el CAT: modo del equipo y tono de telegrafía. Solo se lee.</summary>
    /// <param name="modo">Modo del equipo («CW», «USB»...).</param>
    /// <param name="pitchHz">Pitch, si lo da.</param>
    public void SeguirAlEquipo(string? modo, double? pitchHz)
    {
        EquipoEnCw = modo is { } m && m.StartsWith("CW", StringComparison.OrdinalIgnoreCase);
        PitchDelEquipoHz = pitchHz is > 0 ? pitchHz : null;
    }

    /// <summary>Vuelve a leer los ajustes de CW (tras guardarlos).</summary>
    public void AplicarAjustes()
    {
        _decodificador.Configurar(_ajustes.Cw.Opciones());
        if (!Fijo) TonoHz = _ajustes.Cw.TonoPorOmisionHz;
    }

    /// <summary>Clasifica una palabra y le pone su ayuda (significado o país).</summary>
    public PalabraCw Clasificar(string palabra)
    {
        var tipo = PalabrasCw.Clasificar(palabra, MiIndicativo);
        string? ayuda = null;
        if (tipo is TipoDePalabraCw.Indicativo or TipoDePalabraCw.Propio)
        {
            ayuda = Pais(palabra);
        }
        else if (GlosarioCw.Buscar(palabra) is { } entrada)
        {
            ayuda = entrada.Significado;
            if (tipo == TipoDePalabraCw.Normal) tipo = TipoDePalabraCw.Abreviatura;
        }

        return new PalabraCw(palabra, tipo, ayuda);
    }

    /// <summary>Borra el texto.</summary>
    [RelayCommand]
    public void Borrar()
    {
        Principal.Borrar();
        Otras.Clear();
    }

    /// <summary>Pausa o sigue.</summary>
    [RelayCommand]
    public void AlternarPausa() => Pausado = !Pausado;

    /// <summary>Muestra u oculta la versión reducida de la cabina (cuando el equipo no está en CW).</summary>
    [RelayCommand]
    public void AlternarMostrar() => MostrarAMano = !MostrarAMano;

    /// <summary>Pide abrir la página CW.</summary>
    [RelayCommand]
    public void AbrirPagina() => AbrirPaginaPedido?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Fija el tono (al pitch del equipo si lo hay; si no, al que tenga ahora) o lo vuelve a
    /// automático.
    /// </summary>
    [RelayCommand]
    public void FijarOAuto()
    {
        if (Fijo)
        {
            _decodificador.FijarTono(null);
            Fijo = false;
            return;
        }

        FijarEn(PitchDelEquipoHz ?? TonoHz);
    }

    /// <summary>En AUTO, suelta la señal enganchada y busca otra.</summary>
    [RelayCommand]
    public void Buscar()
    {
        if (Fijo)
        {
            _decodificador.FijarTono(null);
            Fijo = false;
        }

        _decodificador.Buscar();
    }

    /// <summary>Fija el tono a un valor (clic en el espectro).</summary>
    /// <param name="hz">Tono.</param>
    [RelayCommand]
    public void FijarEn(double hz)
    {
        hz = Math.Clamp(hz, 100, 3000);
        _decodificador.FijarTono(hz);
        TonoHz = hz;
        TonoEscrito = Math.Round(hz).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Fijo = true;
    }

    /// <summary>Fija el tono escrito en la casilla, acotado a 300–1200 Hz.</summary>
    [RelayCommand]
    public void AplicarTonoEscrito()
    {
        if (!double.TryParse(TonoEscrito.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz))
        {
            TonoEscrito = Math.Round(TonoHz).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        var acotado = Math.Clamp(Math.Round(hz), TonoMinimo, TonoMaximo);
        Aviso = acotado != Math.Round(hz) ? Textos.F("Cabina.Cw.TonoFueraDeRango", acotado) : string.Empty;
        FijarEn(acotado);
    }

    /// <summary>Sube el tono escrito 10 Hz y lo aplica.</summary>
    [RelayCommand]
    public void SubirTono() => Mover(10);

    /// <summary>Baja el tono escrito 10 Hz y lo aplica.</summary>
    [RelayCommand]
    public void BajarTono() => Mover(-10);

    /// <summary>Pasa un indicativo del texto al contacto nuevo.</summary>
    [RelayCommand]
    public void PasarIndicativo(string? indicativo)
    {
        if (string.IsNullOrWhiteSpace(indicativo)) return;
        IndicativoElegido?.Invoke(this, indicativo);
    }

    /// <summary>
    /// Graba 60 s del audio tal como llega de la entrada, en la carpeta de datos
    /// (<c>grabaciones-cw</c>). Para mandar casos difíciles. No transmite nada.
    /// </summary>
    [RelayCommand]
    public void Grabar()
    {
        if (Grabando) return;
        lock (_cerrojoDeLaGrabacion)
        {
            _grabacion = [];
            _frecuenciaDeLaGrabacion = 0;
        }

        SegundosGrabados = 0;
        Grabando = true;
        Aviso = string.Empty;
    }

    /// <summary>Abre la entrada de audio del equipo si estaba cerrada.</summary>
    [RelayCommand]
    public async Task EscucharAsync()
    {
        if (_entrada is null) return;
        try
        {
            if (_entrada.Abierto is null)
            {
                var guardado = _ajustes.Digital.DispositivoDeEntrada;
                var dispositivo = _entrada.Dispositivos.FirstOrDefault(d => d.Id == guardado)
                    ?? _entrada.Dispositivos.FirstOrDefault(d => d.EsDelEquipo);
                if (dispositivo is null)
                {
                    Aviso = Textos.T("Cabina.Cw.SinEntrada");
                    return;
                }

                await _entrada.AbrirAsync(dispositivo.Id, _ajustes.Digital.FrecuenciaDeMuestreo).ConfigureAwait(true);
            }

            Aviso = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido abrir la entrada de audio para la telegrafía.");
            Aviso = Textos.F("Cabina.Cw.NoSeAbre", ex.Message);
        }

        MirarLaEscucha();
    }

    /// <summary>Vuelca en pantalla lo decodificado y el estado (hilo de la interfaz).</summary>
    public void Refrescar()
    {
        HayAudio = _entrada?.Abierto is not null;
        var estado = _decodificador.Estado;
        Estado = estado;
        Anclado = estado.Anclado;
        SegundosSinSenal = estado.SegundosSinSenal;

        if (estado.Principal is { } p)
        {
            if (p.Id != _canalPrincipal)
            {
                // Cambió de señal: lo que se escriba ahora es de otra, se separa.
                if (_canalPrincipal >= 0) Principal.Anadir(" ");
                _canalPrincipal = p.Id;
            }

            TonoHz = p.TonoHz;
            Wpm = p.Enganchado || p.Confianza > 0.5 ? p.Wpm : 0;
            NivelDb = p.SeparacionDb;
            Confianza = p.Confianza;
            Enganchado = p.Enganchado;
            Marcando = p.Marcando;
        }

        MedirLaEntrada();
        TerminarLaGrabacionSiToca();

        while (_cola.TryDequeue(out var t))
        {
            if (t.EsPrincipal)
            {
                Principal.Anadir(t.Texto);
                continue;
            }

            var linea = Otras.FirstOrDefault(l => l.Canal == t.Canal);
            if (linea is null)
            {
                if (t.Texto == " ") continue;
                linea = new LineaCw(t.Canal, PalabrasDeLasOtras, Clasificar);
                Otras.Add(linea);
            }

            linea.Anadir(t.Texto);
        }

        // Las señales del skimmer que ya no están se quitan; las demás, con su tono y velocidad.
        foreach (var linea in Otras.ToList())
        {
            var vivo = estado.Canales.FirstOrDefault(c => c.Id == linea.Canal && !c.EsPrincipal);
            if (vivo is null)
            {
                Otras.Remove(linea);
                continue;
            }

            linea.TonoHz = vivo.TonoHz;
            linea.Wpm = vivo.Wpm;
        }
    }

    partial void OnEquipoEnCwChanged(bool value) => MirarLaEscucha();

    partial void OnMostrarAManoChanged(bool value) => MirarLaEscucha();

    partial void OnPaginaVisibleChanged(bool value) => MirarLaEscucha();

    partial void OnPausadoChanged(bool value) => MirarLaEscucha();

    private void Mover(int hz)
    {
        var actual = double.TryParse(TonoEscrito, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : TonoHz;
        TonoEscrito = Math.Clamp(Math.Round(actual) + hz, TonoMinimo, TonoMaximo).ToString(System.Globalization.CultureInfo.InvariantCulture);
        AplicarTonoEscrito();
    }

    private string? Pais(string palabra)
    {
        if (_dxcc is null || !Indicativo.TryParse(palabra, out var indicativo)) return null;
        try
        {
            var r = _dxcc.Resolver(indicativo, DateOnly.FromDateTime(DateTime.UtcNow));
            if (r.Entidad is not { } e) return null;
            var nombre = Textos.Codigo == "es" && !string.IsNullOrEmpty(e.NombreEspanol) ? e.NombreEspanol : e.Nombre;
            return Textos.F("Cabina.Cw.Pais", nombre);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "No se ha podido resolver el país de {Indicativo}.", palabra);
            return null;
        }
    }

    /// <summary>Se escucha mientras se ve (cabina o página) y no está en pausa.</summary>
    private void MirarLaEscucha()
    {
        var quiere = (Visible || PaginaVisible) && !Pausado && _entrada is not null;
        if (quiere == _escuchando)
        {
            HayAudio = _entrada?.Abierto is not null;
            return;
        }

        if (quiere)
        {
            _decodificador.Reiniciar();
            if (Fijo) _decodificador.FijarTono(TonoHz);
            _entrada!.BloqueCapturado += AlCapturar;
            _reloj?.Start();
        }
        else
        {
            _entrada!.BloqueCapturado -= AlCapturar;
            _reloj?.Stop();
        }

        _escuchando = quiere;
        HayAudio = _entrada?.Abierto is not null;
        OnPropertyChanged(nameof(Escuchando));
    }

    private void MedirLaEntrada()
    {
        double energia;
        long muestras, recortadas, ultimo;
        lock (_cerrojoDeLaGrabacion)
        {
            energia = _energia;
            muestras = _muestras;
            recortadas = _recortadas;
            ultimo = _ultimoBloque;
            _energia = 0;
            _muestras = 0;
            _recortadas = 0;
        }

        if (muestras == 0)
        {
            if (Environment.TickCount64 - ultimo > 1000) NivelDeEntrada = NivelDeEntradaCw.SinAudio;
            return;
        }

        EntradaDbfs = 10 * Math.Log10((energia / muestras) + 1e-12);
        NivelDeEntrada = recortadas > muestras / 2000 ? NivelDeEntradaCw.Saturado
            : EntradaDbfs < -50 ? NivelDeEntradaCw.Bajo
            : NivelDeEntradaCw.Bien;
    }

    private void TerminarLaGrabacionSiToca()
    {
        if (!Grabando) return;
        float[]? lista = null;
        int fs;
        lock (_cerrojoDeLaGrabacion)
        {
            fs = _frecuenciaDeLaGrabacion;
            if (_grabacion is { } g && fs > 0)
            {
                SegundosGrabados = g.Count / fs;
                if (g.Count >= SegundosDeGrabacion * fs)
                {
                    lista = [.. g];
                    _grabacion = null;
                }
            }
        }

        if (lista is null) return;
        Grabando = false;
        try
        {
            var carpeta = Path.Combine(CarpetaDeDatos ?? Path.GetTempPath(), "grabaciones-cw");
            Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, $"cw-{DateTime.Now:yyyyMMdd-HHmmss}-{fs}Hz.wav");
            LectorWav.Escribir(ruta, lista, fs);
            Aviso = Textos.F("Cabina.Cw.Grabado", ruta);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido guardar la grabación de telegrafía.");
            Aviso = Textos.F("Cabina.Cw.NoSeGraba", ex.Message);
        }
    }

    private void AlCapturar(object? origen, BloqueDeAudio bloque)
    {
        var muestras = bloque.Muestras.Span;
        double energia = 0;
        var recortadas = 0;
        foreach (var x in muestras)
        {
            energia += (double)x * x;
            if (Math.Abs(x) >= 0.985f) recortadas++;
        }

        lock (_cerrojoDeLaGrabacion)
        {
            _energia += energia;
            _muestras += muestras.Length;
            _recortadas += recortadas;
            _ultimoBloque = Environment.TickCount64;
            if (_grabacion is { } g)
            {
                _frecuenciaDeLaGrabacion = bloque.FrecuenciaDeMuestreo;
                if (g.Count < SegundosDeGrabacion * bloque.FrecuenciaDeMuestreo)
                {
                    foreach (var x in muestras) g.Add(x);
                }
            }
        }

        try
        {
            _decodificador.Alimentar(muestras, bloque.FrecuenciaDeMuestreo);
        }
        catch (Exception ex)
        {
            // Un fallo del decodificador no puede tumbar la captura, que comparten el módem y la fonía.
            Log.Error(ex, "Fallo del decodificador de telegrafía con un bloque de audio.");
        }
    }
}
