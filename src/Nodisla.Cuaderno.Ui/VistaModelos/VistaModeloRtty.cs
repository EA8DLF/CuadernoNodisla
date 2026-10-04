using System.Collections.Concurrent;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Modos.Rtty;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Conversores;
using Nodisla.Cuaderno.Ui.Digital;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// RTTY: el canal de recepción propio (<see cref="CanalRtty"/>) sobre el audio de recepción, y la
/// transmisión AFSK propia (<see cref="EmisorRtty"/>) sobre la salida de audio y el vigilante del
/// PTT.
/// </summary>
/// <remarks>
/// <para>
/// Escucha mientras se ve la página y no está en pausa — el mismo mecanismo exacto de
/// <see cref="VistaModeloCw"/> (<c>Visible</c>/<c>PaginaVisible</c>/<c>Pausado</c>) y por la misma
/// razón: la entrada de audio es compartida con CW, el módem propio y la fonía por recuento de
/// referencias (<c>EntradaDeAudioCompartida</c>), y aquí tiene que pedirse solo cuando hace falta
/// y soltarse de verdad cuando no.
/// </para>
/// <para>
/// RTTY aquí es una sola señal sintonizada a la vez, como un TU de verdad: no hay skimmer de
/// varias señales como en CW. El tono sirve igual para escuchar (<see cref="CanalRtty.Afinar"/>)
/// y para transmitir (<see cref="EmisorRtty.TonoDeMarcaHz"/>): es el mismo «TU» en los dos
/// sentidos.
/// </para>
/// <para>
/// La salida de audio (compartida también con el módem propio y la fonía) solo se abre mientras
/// dura una transmisión y se cierra justo al terminar: ni un segundo más de lo que hace falta.
/// </para>
/// </remarks>
public sealed partial class VistaModeloRtty : ObservableObject
{
    /// <summary>Tono más bajo que se puede escribir.</summary>
    public const int TonoMinimo = 300;

    /// <summary>Tono más alto que se puede escribir.</summary>
    public const int TonoMaximo = 3000;

    /// <summary>Palabras que se guardan en el texto recibido; las más viejas se van.</summary>
    public const int PalabrasGuardadas = 400;

    private readonly AjustesDelPrograma _ajustes;
    private readonly IEntradaDeAudio? _entrada;
    private readonly EmisorRtty? _emisor;
    private readonly ISalidaDeAudio? _salida;
    private readonly IResolutorDxcc? _dxcc;
    private readonly CanalRtty _canal;
    private readonly ConcurrentQueue<string> _cola = new();
    private readonly DispatcherTimer? _reloj;
    private bool _escuchando;
    private bool _audioAdquirida;
    private bool _usuarioQuiereEscuchar;

    /// <summary>Monta el modelo.</summary>
    /// <param name="ajustes">Ajustes del programa (frecuencia de muestreo y dispositivos).</param>
    /// <param name="entrada">Audio de recepción. Puede faltar.</param>
    /// <param name="emisor">El emisor de RTTY. Puede faltar: entonces no se puede transmitir.</param>
    /// <param name="salida">Salida de audio real, para la transmisión. Puede faltar.</param>
    /// <param name="conReloj">Volcar solo cada 100 ms. Las pruebas lo quitan y llaman a <see cref="Refrescar"/>.</param>
    /// <param name="dxcc">Para el país de los indicativos. Puede faltar.</param>
    public VistaModeloRtty(
        AjustesDelPrograma ajustes, IEntradaDeAudio? entrada = null, EmisorRtty? emisor = null,
        ISalidaDeAudio? salida = null, bool conReloj = true, IResolutorDxcc? dxcc = null)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        _ajustes = ajustes;
        _entrada = entrada;
        _emisor = emisor;
        _salida = salida;
        _dxcc = dxcc;

        Principal = new LineaCw(-1, PalabrasGuardadas, Clasificar);
        Principal.Palabras.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SinTexto));
            OnPropertyChanged(nameof(TextoRecibido));
        };

        // Lo que quedó guardado la última vez (o lo de fábrica): se construye el canal con ello
        // directamente y LUEGO se refleja en las propiedades observables, para que al escribirlas
        // el canal ya exista (ver los OnXxxChanged, más abajo).
        var guardado = ajustes.Rtty;
        _canal = new CanalRtty(
            ajustes.Digital.FrecuenciaDeMuestreo, guardado.TonoHz,
            new ParametrosRtty(DesplazamientoHz: guardado.DesplazamientoHz, BitsDeParada: guardado.BitsDeParada, Invertido: guardado.Invertido));
        _canal.Texto += t => _cola.Enqueue(t);

        if (_emisor is not null) _emisor.EstadoCambiado += AlCambiarElEmisor;

        _tonoHz = guardado.TonoHz;
        _tonoEscrito = Math.Round(guardado.TonoHz).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _desplazamientoHz = guardado.DesplazamientoHz;
        _bitsDeParada = guardado.BitsDeParada;
        _invertido = guardado.Invertido;

        if (conReloj)
        {
            _reloj = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
            _reloj.Tick += (_, _) => Refrescar();
        }
    }

    /// <summary>Los desplazamientos que se pueden elegir.</summary>
    public static IReadOnlyList<int> DesplazamientosDisponibles { get; } = [170, 850];

    /// <summary>Los bits de parada que se pueden elegir.</summary>
    public static IReadOnlyList<double> BitsDeParadaDisponibles { get; } = [1, 1.5, 2];

    /// <summary>El emisor, para las pruebas y para las fuentes de PTT.</summary>
    public EmisorRtty? Emisor => _emisor;

    /// <summary>El canal de recepción, para las pruebas.</summary>
    public CanalRtty Canal => _canal;

    /// <summary>El texto recibido, con los indicativos y llamadas reconocidos (como en CW).</summary>
    public LineaCw Principal { get; }

    /// <summary>Salta cuando se pulsa un indicativo del texto recibido.</summary>
    public event EventHandler<string>? IndicativoElegido;

    /// <summary>El indicativo de la propia estación, para resaltarlo. Lo pone la ventana principal.</summary>
    public string? MiIndicativo { get; set; }

    /// <summary>Carpeta de datos del programa: donde se guarda «no volver a preguntar».</summary>
    public string? CarpetaDeDatos { get; set; }

    /// <summary>
    /// La pregunta de transmitir. Sin ella puesta y con la pregunta pedida en los ajustes, no se
    /// transmite. La pone la vista (<c>PanelRtty</c>), con el mismo diálogo que CW y la fonía.
    /// </summary>
    public Func<string, bool>? ConfirmarQueVaATransmitir { get; set; }

    /// <summary>
    /// Se pregunta antes de emitir. Es el MISMO ajuste que comparten el módem, CW y los mensajes
    /// de voz (<see cref="AjustesDeDigital.PedirConfirmacionAlTransmitir"/>): «no volver a
    /// preguntar» en cualquiera de ellos lo apaga para todos.
    /// </summary>
    public bool PedirConfirmacionAlTransmitir
    {
        get => _ajustes.Digital.PedirConfirmacionAlTransmitir;
        set
        {
            if (_ajustes.Digital.PedirConfirmacionAlTransmitir == value) return;
            _ajustes.Digital.PedirConfirmacionAlTransmitir = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Se está viendo la página RTTY.</summary>
    [ObservableProperty]
    private bool _paginaVisible;

    /// <summary>En pausa: no se escucha el audio.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloDePausa))]
    private bool _pausado;

    /// <summary>Tono de marca, para escuchar y para transmitir (Hz).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TonoTexto))]
    private double _tonoHz = 1500;

    /// <summary>El tono escrito a mano en la casilla (se aplica con Intro).</summary>
    [ObservableProperty]
    private string _tonoEscrito = "1500";

    /// <summary>Desplazamiento entre marca y espacio (Hz): 170 o 850.</summary>
    [ObservableProperty]
    private int _desplazamientoHz = 170;

    /// <summary>Bits de parada: 1, 1,5 o 2.</summary>
    [ObservableProperty]
    private double _bitsDeParada = 1.5;

    /// <summary>El tono de marca es el más bajo de los dos («reversa»).</summary>
    [ObservableProperty]
    private bool _invertido;

    /// <summary>Lo que destaca la señal sobre el ruido (dB).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NivelPorCiento))]
    [NotifyPropertyChangedFor(nameof(NivelTexto))]
    private double _nivelDb;

    /// <summary>El canal está enganchado a una señal.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoDeEscucha))]
    private bool _enganchado;

    /// <summary>La entrada de audio está abierta.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SinAudio))]
    private bool _hayAudio;

    /// <summary>Se enseña la línea de traducción de las abreviaturas.</summary>
    [ObservableProperty]
    private bool _mostrarTraduccion = true;

    /// <summary>Lo que escribe el operador para emitir.</summary>
    [ObservableProperty]
    private string _textoAEmitir = string.Empty;

    /// <summary>El PTT está arriba por esta transmisión.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTx))]
    private bool _enAntena;

    /// <summary>El trozo que se está generando y reproduciendo ahora.</summary>
    [ObservableProperty]
    private string _enviando = string.Empty;

    /// <summary>Lo que falta por mandar, encolado detrás.</summary>
    [ObservableProperty]
    private string _pendiente = string.Empty;

    /// <summary>Un aviso para el operador, o vacío.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAviso))]
    private string _aviso = string.Empty;

    /// <summary>La entrada de audio está cerrada: se ofrece «Escuchar».</summary>
    public bool SinAudio => !HayAudio;

    /// <summary>Hay aviso que enseñar.</summary>
    public bool HayAviso => Aviso.Length > 0;

    /// <summary>El texto recibido tal cual, para quien no necesite la clasificación por palabras.</summary>
    public string TextoRecibido => Principal.Texto;

    /// <summary>Todavía no hay texto recibido.</summary>
    public bool SinTexto => Principal.Palabras.Count == 0;

    /// <summary>Se está escuchando el audio ahora mismo.</summary>
    public bool Escuchando => _escuchando;

    /// <summary>Hay una transmisión en marcha.</summary>
    public bool Transmitiendo => _emisor?.Enviando ?? false;

    /// <summary>Se puede transmitir: hay emisor y salida de audio.</summary>
    public bool SePuedeTransmitir => _emisor is not null && _salida is not null;

    /// <summary>Tono para leer.</summary>
    public string TonoTexto => Textos.F("Cabina.Rtty.Tono", Math.Round(TonoHz));

    /// <summary>Nivel para leer.</summary>
    public string NivelTexto => Textos.F("Cabina.Rtty.Nivel", Math.Round(NivelDb));

    /// <summary>Barra de nivel: de 0 a 30 dB sobre el ruido.</summary>
    public double NivelPorCiento => Math.Clamp(NivelDb / 30 * 100, 0, 100);

    /// <summary>«Pausa» o «Seguir».</summary>
    public string RotuloDePausa => Pausado ? Textos.T("Cabina.Rtty.Seguir") : Textos.T("Cabina.Rtty.Pausa");

    /// <summary>«EN ANTENA» o «En escucha».</summary>
    public string EstadoTx => EnAntena ? Textos.T("Cabina.Rtty.EnAntena") : Textos.T("Cabina.Rtty.EnEscucha");

    /// <summary>«Enganchado» o «Buscando…».</summary>
    public string EstadoDeEscucha => Enganchado ? Textos.T("Cabina.Rtty.Enganchado") : Textos.T("Cabina.Rtty.Buscando");

    /// <summary>Borra el texto recibido.</summary>
    [RelayCommand]
    public void Borrar() => Principal.Borrar();

    /// <summary>Pasa un indicativo del texto al contacto nuevo.</summary>
    [RelayCommand]
    public void PasarIndicativo(string? indicativo)
    {
        if (string.IsNullOrWhiteSpace(indicativo)) return;
        IndicativoElegido?.Invoke(this, indicativo);
    }

    /// <summary>Clasifica una palabra y le pone su ayuda (significado o país), igual que en CW.</summary>
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

    /// <summary>
    /// Mete texto en el recibido como si llegara del canal, sin audio real: solo para las
    /// capturas de la ayuda y las pruebas que necesitan algo escrito en la terminal.
    /// </summary>
    public void Simular(string texto)
    {
        foreach (var c in texto) Principal.Anadir(c is '\n' or '\r' ? " " : c.ToString());
    }

    /// <summary>Pausa o sigue.</summary>
    [RelayCommand]
    public void AlternarPausa() => Pausado = !Pausado;

    /// <summary>Sube el tono escrito 10 Hz y lo aplica.</summary>
    [RelayCommand]
    public void SubirTono() => Mover(10);

    /// <summary>Baja el tono escrito 10 Hz y lo aplica.</summary>
    [RelayCommand]
    public void BajarTono() => Mover(-10);

    /// <summary>Fija el tono escrito en la casilla, acotado al rango admitido.</summary>
    [RelayCommand]
    public void AplicarTonoEscrito()
    {
        if (!double.TryParse(TonoEscrito.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hz))
        {
            TonoEscrito = Math.Round(TonoHz).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        var acotado = Math.Clamp(Math.Round(hz), TonoMinimo, TonoMaximo);
        TonoHz = acotado;
        TonoEscrito = acotado.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Abre la entrada de audio si estaba cerrada.</summary>
    [RelayCommand]
    public async Task EscucharAsync()
    {
        if (_entrada is null) return;
        _usuarioQuiereEscuchar = true;
        await AdquirirEntradaAsync().ConfigureAwait(true);
        MirarLaEscucha();
    }

    /// <summary>
    /// Manda el texto escrito: si ya hay una transmisión en marcha, lo encola; si no, abre la
    /// salida de audio (compartida, por recuento de referencias) y empieza una nueva.
    /// </summary>
    [RelayCommand]
    public async Task EmitirAsync()
    {
        var texto = TextoAEmitir.Trim();
        if (texto.Length == 0 || _emisor is null) return;

        if (_emisor.Enviando)
        {
            TextoAEmitir = string.Empty;
            _emisor.Encolar(texto);
            return;
        }

        if (_salida is null)
        {
            Aviso = Textos.T("Cabina.Rtty.SinSalida");
            return;
        }

        var dispositivo = ElegirLaSalida();
        if (dispositivo is null)
        {
            Aviso = Textos.T("Cabina.Rtty.SinDispositivoDeSalida");
            return;
        }

        // La misma pregunta que CW, el módem y la fonía, con el mismo ajuste de «no volver a
        // preguntar»: se pregunta antes de abrir la salida, para no tocar la tarjeta si se dice
        // que no.
        if (PedirConfirmacionAlTransmitir
            && (ConfirmarQueVaATransmitir is not { } confirmar || !confirmar(Resumen(texto))))
        {
            Aviso = Textos.T("Cabina.TxCw.NoConfirmado");
            return;
        }

        try
        {
            await _salida.AbrirAsync(dispositivo.Id, _ajustes.Digital.FrecuenciaDeMuestreo).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido abrir la salida de audio para RTTY.");
            Aviso = Textos.F("Cabina.Rtty.NoSeAbreLaSalida", ex.Message);
            return;
        }

        TextoAEmitir = string.Empty;
        Aviso = string.Empty;
        try
        {
            _emisor.TonoDeMarcaHz = TonoHz;
            _emisor.Parametros = ParametrosDeLinea();
            var fin = await _emisor.TransmitirAsync(texto, Textos.F("Cabina.Rtty.Motivo", Resumen(texto))).ConfigureAwait(true);
            Aviso = fin switch
            {
                FinDelEnvioRtty.Completo => string.Empty,
                FinDelEnvioRtty.Parado => Textos.T("Cabina.Rtty.Parado"),
                FinDelEnvioRtty.TiempoAgotado => Textos.T("Cabina.Rtty.TiempoAgotado"),
                _ => Textos.F("Cabina.Rtty.Fallo", _emisor.UltimoFallo?.Message ?? string.Empty),
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido transmitir RTTY.");
            Aviso = Textos.F("Cabina.Rtty.Fallo", ex.Message);
        }
        finally
        {
            try
            {
                await _salida.CerrarAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se ha podido cerrar la salida de audio de RTTY.");
            }
        }
    }

    /// <summary>
    /// «No volver a preguntar» del diálogo: el mismo ajuste que CW, el módem y la fonía.
    /// </summary>
    public void NoVolverAPreguntarAlTransmitir()
    {
        PedirConfirmacionAlTransmitir = false;
        Log.Warning("El operador ha pedido no volver a confirmar las transmisiones (RTTY).");
        if (CarpetaDeDatos is not { Length: > 0 } carpeta) return;

        try
        {
            _ajustes.Guardar(carpeta);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido guardar «no volver a preguntar».");
        }
    }

    /// <summary>
    /// Guarda el tono, el desplazamiento, la parada y la inversión en los ajustes. Lo llama la
    /// ventana al cerrarse, igual que con el modo y el tono del módem propio.
    /// </summary>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    public void GuardarLoElegido(string carpeta)
    {
        var r = _ajustes.Rtty;
        r.TonoHz = TonoHz;
        r.DesplazamientoHz = DesplazamientoHz;
        r.BitsDeParada = BitsDeParada;
        r.Invertido = Invertido;
        r.Acotar();
        _ajustes.Guardar(carpeta);
    }

    /// <summary>Para la transmisión en el acto (corta audio y baja el PTT).</summary>
    [RelayCommand]
    public async Task PararAsync()
    {
        if (_emisor is null) return;
        try
        {
            await _emisor.PararAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido parar RTTY.");
            Aviso = Textos.F("Cabina.Rtty.Fallo", ex.Message);
        }
    }

    /// <summary>Vuelca en pantalla lo decodificado y el estado (hilo de la interfaz).</summary>
    public void Refrescar()
    {
        HayAudio = _entrada?.Abierto is not null;
        NivelDb = _canal.SeparacionDb;
        Enganchado = _canal.Enganchado;

        // CR y LF viajan de verdad por Baudot (TablaBaudot.EsComun): para la línea de texto son
        // un espacio, no un carácter de la palabra.
        while (_cola.TryDequeue(out var t)) Principal.Anadir(t is "\r" or "\n" ? " " : t);
    }

    partial void OnPaginaVisibleChanged(bool value) => MirarLaEscucha();

    partial void OnPausadoChanged(bool value) => MirarLaEscucha();

    partial void OnTonoHzChanged(double value) => _canal.Afinar(value);

    partial void OnDesplazamientoHzChanged(int value) => _canal.Configurar(ParametrosDeLinea());

    partial void OnBitsDeParadaChanged(double value) => _canal.Configurar(ParametrosDeLinea());

    partial void OnInvertidoChanged(bool value) => _canal.Configurar(ParametrosDeLinea());

    private ParametrosRtty ParametrosDeLinea() =>
        new(DesplazamientoHz: DesplazamientoHz, BitsDeParada: BitsDeParada, Invertido: Invertido);

    private void Mover(int hz)
    {
        var actual = double.TryParse(TonoEscrito, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : TonoHz;
        TonoEscrito = Math.Clamp(Math.Round(actual) + hz, TonoMinimo, TonoMaximo).ToString(System.Globalization.CultureInfo.InvariantCulture);
        AplicarTonoEscrito();
    }

    private static string Resumen(string texto) => texto.Length <= 40 ? texto : texto[..37] + "…";

    private string? Pais(string palabra)
    {
        if (_dxcc is null || !Indicativo.TryParse(palabra, out var indicativo)) return null;
        try
        {
            var r = _dxcc.Resolver(indicativo, DateOnly.FromDateTime(DateTime.UtcNow));
            if (r.Entidad is not { } e) return null;
            var nombre = Textos.Codigo == "es" && !string.IsNullOrEmpty(e.NombreEspanol) ? e.NombreEspanol : e.Nombre;
            return Textos.F("Cabina.Rtty.Pais", nombre);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "No se ha podido resolver el país de {Indicativo}.", palabra);
            return null;
        }
    }

    private DispositivoDeAudio? ElegirLaSalida()
    {
        if (_salida is null) return null;
        var guardado = _ajustes.Digital.DispositivoDeSalida;
        if (!string.IsNullOrEmpty(guardado))
        {
            var elegido = _salida.Dispositivos.FirstOrDefault(d => d.Id == guardado);
            if (elegido is not null) return elegido;
        }

        return _salida.Dispositivos.FirstOrDefault(d => d.EsDelEquipo);
    }

    /// <summary>
    /// Pide la entrada compartida (si no se tenía ya pedida) y la abre si hiciera falta. Único
    /// camino de entrada a <see cref="IEntradaDeAudio.AbrirAsync"/> desde aquí, igual que en
    /// <see cref="VistaModeloCw"/>.
    /// </summary>
    private async Task AdquirirEntradaAsync()
    {
        if (_entrada is null || _audioAdquirida) return;
        _audioAdquirida = true;

        try
        {
            var guardado = _ajustes.Digital.DispositivoDeEntrada;
            var dispositivo = _entrada.Dispositivos.FirstOrDefault(d => d.Id == guardado)
                ?? _entrada.Dispositivos.FirstOrDefault(d => d.EsDelEquipo);
            if (dispositivo is null)
            {
                Aviso = Textos.T("Cabina.Rtty.SinEntrada");
                _audioAdquirida = false;
                return;
            }

            await _entrada.AbrirAsync(dispositivo.Id, _ajustes.Digital.FrecuenciaDeMuestreo).ConfigureAwait(true);
            Aviso = string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido abrir la entrada de audio para RTTY.");
            Aviso = Textos.F("Cabina.Rtty.NoSeAbre", ex.Message);
            _audioAdquirida = false;
        }
    }

    /// <summary>Suelta la entrada compartida, si se tenía pedida.</summary>
    private async Task SoltarEntradaAsync()
    {
        if (_entrada is null || !_audioAdquirida) return;
        _audioAdquirida = false;

        try
        {
            await _entrada.CerrarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido cerrar la entrada de audio de RTTY.");
        }
    }

    /// <summary>
    /// Se escucha mientras se ve la página y no está en pausa. Al entrar, pide la entrada
    /// compartida; al salir —incluida la pausa—, la suelta de verdad.
    /// </summary>
    private void MirarLaEscucha()
    {
        var quiere = PaginaVisible && !Pausado && _entrada is not null;
        if (quiere == _escuchando)
        {
            HayAudio = _entrada?.Abierto is not null;
            return;
        }

        if (quiere)
        {
            _canal.Reiniciar();
            _entrada!.BloqueCapturado += AlCapturar;
            _reloj?.Start();
            if (_usuarioQuiereEscuchar) _ = AdquirirEntradaAsync();
        }
        else
        {
            _entrada!.BloqueCapturado -= AlCapturar;
            _reloj?.Stop();
            if (_usuarioQuiereEscuchar) _ = SoltarEntradaAsync();
        }

        _escuchando = quiere;
        HayAudio = _entrada?.Abierto is not null;
        OnPropertyChanged(nameof(Escuchando));
    }

    private void AlCapturar(object? origen, BloqueDeAudio bloque)
    {
        try
        {
            _canal.Anadir(bloque.Muestras.Span);
        }
        catch (Exception ex)
        {
            // Un fallo del canal no puede tumbar la captura, que comparten el módem y la fonía.
            Log.Error(ex, "Fallo del canal de RTTY con un bloque de audio.");
        }
    }

    private void AlCambiarElEmisor(object? origen, EstadoDelEmisorRtty estado) => Hilo.EnLaVentana(() =>
    {
        EnAntena = estado.EnAntena;
        Enviando = estado.Enviando;
        Pendiente = estado.Pendiente;
        OnPropertyChanged(nameof(Transmitiendo));
    });
}
