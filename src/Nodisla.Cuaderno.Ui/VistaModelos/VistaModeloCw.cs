using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Modos.Cw;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Digital;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una palabra del texto de telegrafía, ya clasificada.</summary>
/// <param name="Texto">La palabra.</param>
/// <param name="Tipo">Si es indicativo, llamada, prosigno...</param>
public sealed record PalabraCw(string Texto, TipoDePalabraCw Tipo)
{
    /// <summary>Se puede pasar al contacto nuevo.</summary>
    public bool EsIndicativo => Tipo is TipoDePalabraCw.Indicativo;

    /// <summary>No es un indicativo de otro (se pinta como texto).</summary>
    public bool EsTexto => !EsIndicativo;
}

/// <summary>El texto de una señal: el de la principal o el de una del «skimmer».</summary>
public sealed partial class LineaCw : ObservableObject
{
    private readonly int _palabrasMaximas;
    private readonly Func<string?> _miIndicativo;
    private string _enCurso = string.Empty;

    /// <summary>Monta la línea.</summary>
    /// <param name="canal">Canal del decodificador que la escribe.</param>
    /// <param name="palabrasMaximas">Palabras que se guardan; las más viejas se van.</param>
    /// <param name="miIndicativo">El indicativo propio, para resaltarlo.</param>
    public LineaCw(int canal, int palabrasMaximas, Func<string?> miIndicativo)
    {
        Canal = canal;
        _palabrasMaximas = palabrasMaximas;
        _miIndicativo = miIndicativo;
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
        OnPropertyChanged(nameof(Texto));
    }

    private void Poner(string palabra, bool nueva)
    {
        var p = new PalabraCw(palabra, PalabrasCw.Clasificar(palabra, _miIndicativo()));
        if (nueva || Palabras.Count == 0) Palabras.Add(p);
        else Palabras[^1] = p;
        while (Palabras.Count > _palabrasMaximas) Palabras.RemoveAt(0);
        OnPropertyChanged(nameof(Texto));
    }
}

/// <summary>
/// El panel de telegrafía de la cabina: el decodificador propio sobre el audio de recepción.
/// </summary>
/// <remarks>
/// <para>
/// Se ve cuando el equipo está en CW o cuando el operador lo pide, y solo escucha el audio
/// mientras se ve y no está en pausa. <b>No transmite nada</b> ni manda órdenes al equipo: del CAT
/// solo lee el modo y el tono de telegrafía (pitch), que es a lo que se fija con «Fijar».
/// </para>
/// <para>
/// El audio llega por el hilo de la tarjeta y ahí mismo se decodifica; el texto se apunta en una
/// cola y se vuelca en pantalla diez veces por segundo, en el hilo de la interfaz.
/// </para>
/// </remarks>
public sealed partial class VistaModeloCw : ObservableObject
{
    /// <summary>Palabras de la línea principal que se guardan.</summary>
    public const int PalabrasDeLaPrincipal = 400;

    /// <summary>Palabras de cada señal del skimmer.</summary>
    public const int PalabrasDeLasOtras = 10;

    private readonly AjustesDelPrograma _ajustes;
    private readonly IEntradaDeAudio? _entrada;
    private readonly DecodificadorCw _decodificador;
    private readonly ConcurrentQueue<TextoCw> _cola = new();
    private readonly DispatcherTimer? _reloj;
    private bool _escuchando;
    private int _canalPrincipal = -1;

    /// <summary>Monta el panel.</summary>
    /// <param name="ajustes">Ajustes del programa (los de CW se leen de ahí).</param>
    /// <param name="entrada">Audio de recepción. Puede faltar.</param>
    /// <param name="conReloj">Volcar solo cada 100 ms. Las pruebas lo quitan y llaman a <see cref="Refrescar"/>.</param>
    public VistaModeloCw(AjustesDelPrograma ajustes, IEntradaDeAudio? entrada, bool conReloj = true)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        _ajustes = ajustes;
        _entrada = entrada;
        _decodificador = new DecodificadorCw(ajustes.Cw.Opciones());
        _decodificador.TextoDecodificado += (_, t) => _cola.Enqueue(t);
        Principal = new LineaCw(-1, PalabrasDeLaPrincipal, () => MiIndicativo);
        Principal.Palabras.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SinTexto));
        TonoHz = ajustes.Cw.TonoPorOmisionHz;

        if (conReloj)
        {
            _reloj = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            _reloj.Tick += (_, _) => Refrescar();
        }
    }

    /// <summary>Alguien ha pulsado un indicativo del texto.</summary>
    public event EventHandler<string>? IndicativoElegido;

    /// <summary>El motor (para las pruebas y el audio simulado).</summary>
    public DecodificadorCw Decodificador => _decodificador;

    /// <summary>El texto de la señal principal.</summary>
    public LineaCw Principal { get; }

    /// <summary>Las demás señales de la banda de paso.</summary>
    public ObservableCollection<LineaCw> Otras { get; } = [];

    /// <summary>El indicativo de la estación, para resaltarlo.</summary>
    public string? MiIndicativo { get; set; }

    /// <summary>El equipo está en CW (lo dice el CAT).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    private bool _equipoEnCw;

    /// <summary>El operador ha pedido verlo aunque el equipo no esté en CW.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Visible))]
    private bool _mostrarAMano;

    /// <summary>En pausa: no se escucha el audio.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDePausa))]
    private bool _pausado;

    /// <summary>El tono está fijado (a mano o al pitch del equipo); si no, se busca solo.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDeFijar))]
    [NotifyPropertyChangedFor(nameof(RotuloDelModo))]
    private bool _fijo;

    /// <summary>Tono de la señal principal (Hz).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TonoTexto))]
    private double _tonoHz;

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

    /// <summary>Se ve el panel.</summary>
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

    /// <summary>Muestra u oculta el panel (cuando el equipo no está en CW).</summary>
    [RelayCommand]
    public void AlternarMostrar() => MostrarAMano = !MostrarAMano;

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

    /// <summary>Fija el tono a un valor (clic en el espectro).</summary>
    /// <param name="hz">Tono.</param>
    [RelayCommand]
    public void FijarEn(double hz)
    {
        hz = Math.Clamp(hz, 100, 3000);
        _decodificador.FijarTono(hz);
        TonoHz = hz;
        Fijo = true;
    }

    /// <summary>Pasa un indicativo del texto al contacto nuevo.</summary>
    [RelayCommand]
    public void PasarIndicativo(string? indicativo)
    {
        if (string.IsNullOrWhiteSpace(indicativo)) return;
        IndicativoElegido?.Invoke(this, indicativo);
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
                linea = new LineaCw(t.Canal, PalabrasDeLasOtras, () => MiIndicativo);
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

    partial void OnPausadoChanged(bool value) => MirarLaEscucha();

    /// <summary>Se escucha mientras se ve y no está en pausa.</summary>
    private void MirarLaEscucha()
    {
        var quiere = Visible && !Pausado && _entrada is not null;
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

    private void AlCapturar(object? origen, BloqueDeAudio bloque)
    {
        try
        {
            _decodificador.Alimentar(bloque.Muestras.Span, bloque.FrecuenciaDeMuestreo);
        }
        catch (Exception ex)
        {
            // Un fallo del decodificador no puede tumbar la captura, que comparten el módem y la fonía.
            Log.Error(ex, "Fallo del decodificador de telegrafía con un bloque de audio.");
        }
    }
}
