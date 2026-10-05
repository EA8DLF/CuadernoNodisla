using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Radio.Cw;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Conversores;
using Nodisla.Cuaderno.Ui.Telegrafia;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Un paso de la secuencia, para pintarlo en la tira CQ → … → 73.</summary>
public sealed partial class PasoEnPantalla : ObservableObject
{
    /// <summary>Crea el paso.</summary>
    /// <param name="paso">Cual es.</param>
    public PasoEnPantalla(PasoCw paso) => Paso = paso;

    /// <summary>Cual es.</summary>
    public PasoCw Paso { get; }

    /// <summary>Como se llama, en el idioma en uso.</summary>
    public string Nombre => Paso switch
    {
        PasoCw.Cq => Textos.T("Cabina.TxCw.Paso.Cq"),
        PasoCw.Respuesta => Textos.T("Cabina.TxCw.Paso.Respuesta"),
        PasoCw.Informe => Textos.T("Cabina.TxCw.Paso.Informe"),
        PasoCw.Confirmacion => Textos.T("Cabina.TxCw.Paso.Confirmacion"),
        PasoCw.SetentaYTres => Textos.T("Cabina.TxCw.Paso.SetentaYTres"),
        _ => string.Empty,
    };

    /// <summary>Es lo ultimo que se mando.</summary>
    [ObservableProperty]
    private bool _hecho;

    /// <summary>Es lo que toca mandar.</summary>
    [ObservableProperty]
    private bool _toca;

    /// <summary>Avisa de que el nombre ha cambiado de idioma.</summary>
    public void Refrescar() => OnPropertyChanged(nameof(Nombre));
}

/// <summary>
/// La zona de transmision de telegrafia: macros F1–F12, escritura libre y la secuencia
/// automatica del contacto, como la del FT8 del modem propio.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nada sale al aire sin pasar por cinco puertas</b>, y en este orden: el pestillo
/// «Permitir transmitir en esta sesion» (el mismo del modem; no se guarda), que el equipo sepa
/// manipular por CAT, que este en CW (si no, se pregunta antes de cambiarlo), la pregunta de
/// transmitir (con su «no volver a preguntar», el mismo de siempre) y el vigilante del PTT, que
/// es quien sube y baja la antena. La telegrafia la genera el manipulador de la radio: el PC no
/// produce audio.
/// </para>
/// <para>
/// <b>Nunca se transmite al arrancar ni al conectar</b>: solo con una tecla o un boton del
/// operador, o con la secuencia automatica que el operador ha encendido en esta sesion y que
/// reacciona a lo que lee el decodificador en un contacto que el operador ha empezado.
/// </para>
/// <para>Esc y «PARAR CW» cortan en el acto: orden de parar al manipulador y PTT abajo.</para>
/// </remarks>
public sealed partial class VistaModeloTransmisionCw : ObservableObject, IDisposable
{
    /// <summary>Silencio tras el que se da por terminada la pasada del otro.</summary>
    public static readonly TimeSpan SilencioDeFinDePasada = TimeSpan.FromSeconds(1.6);

    private readonly AjustesDelPrograma _ajustes;
    private readonly EmisorCw _emisor;
    private readonly IControlEquipo _control;
    private readonly VistaModeloEntradaQso? _entrada;
    private readonly VistaModeloModemPropio? _modem;
    private readonly VistaModeloCw? _cw;
    private readonly string? _carpeta;
    private readonly SecuenciadorCw _secuencia = new();
    private readonly LectorDePalabrasCw? _lector;
    private readonly DispatcherTimer? _reloj;
    private readonly List<PasoCw> _pasosEnCurso = [];
    private AjustesDeTransmisionCw _guardado;
    private DateTimeOffset _finDeLaUltimaTx = DateTimeOffset.MinValue;
    private DateTimeOffset _ultimaNovedadVista = DateTimeOffset.MinValue;
    private bool _sincronizandoPestillo;

    /// <summary>Monta la zona de transmision.</summary>
    /// <param name="ajustes">Ajustes del programa (la pregunta de transmitir y el registro al completar).</param>
    /// <param name="emisor">El emisor de telegrafia sobre el equipo y el vigilante.</param>
    /// <param name="control">El equipo, para su modo.</param>
    /// <param name="entrada">El contacto nuevo, que se rellena y se registra. Puede faltar.</param>
    /// <param name="cw">El decodificador de la cabina: de su texto avanza la secuencia. Puede faltar.</param>
    /// <param name="modem">El modem propio: su pestillo es el mismo, y de el sale el indicativo propio. Puede faltar.</param>
    /// <param name="carpeta">Carpeta de datos, donde van las macros. Nula: no se guarda nada.</param>
    /// <param name="conReloj">Mirar el silencio cada cuarto de segundo. Las pruebas lo quitan y llaman a <see cref="Latir"/>.</param>
    public VistaModeloTransmisionCw(
        AjustesDelPrograma ajustes,
        EmisorCw emisor,
        IControlEquipo control,
        VistaModeloEntradaQso? entrada = null,
        VistaModeloCw? cw = null,
        VistaModeloModemPropio? modem = null,
        string? carpeta = null,
        bool conReloj = true)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        ArgumentNullException.ThrowIfNull(emisor);
        ArgumentNullException.ThrowIfNull(control);
        _ajustes = ajustes;
        _emisor = emisor;
        _control = control;
        _entrada = entrada;
        _cw = cw;
        _modem = modem;
        _carpeta = carpeta;
        _guardado = AjustesDeTransmisionCw.Leer(carpeta);

        _wpm = _guardado.Wpm;
        _perfil = _guardado.Perfil;
        _perfilAnterior = _perfil;
        _escribirMientrasSeEnvia = _guardado.EscribirMientrasSeEnvia;
        _miNombre = _guardado.MiNombre;
        _miQth = _guardado.MiQth;
        _numero = _guardado.SiguienteNumero;
        _tiempoMaximoSegundos = _guardado.TiempoMaximoSegundos;

        // El pestillo es el del modem: uno solo por sesion para todo lo que transmite solo.
        _permitirTransmitir = modem?.PermitirTransmitir ?? false;
        if (modem is not null) modem.PropertyChanged += AlCambiarElModem;

        foreach (var paso in new[] { PasoCw.Cq, PasoCw.Respuesta, PasoCw.Informe, PasoCw.Confirmacion, PasoCw.SetentaYTres })
        {
            Pasos.Add(new PasoEnPantalla(paso));
        }

        CargarJuego();

        _emisor.EstadoCambiado += AlCambiarElEmisor;
        _control.EstadoCambiado += AlCambiarElEquipo;

        _secuencia.CorresponsalEncontrado += AlEncontrarCorresponsal;
        _secuencia.DatosRecibidos += AlRecibirDatos;
        _secuencia.ContactoCompleto += AlCompletarse;

        if (_entrada is not null) _entrada.PropertyChanged += AlCambiarLaEntrada;
        if (_cw is not null)
        {
            _lector = new LectorDePalabrasCw(_cw.Principal.Palabras);
            _lector.PalabraLeida += AlLeerPalabra;
        }

        if (conReloj)
        {
            _reloj = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
            _reloj.Tick += (_, _) => Latir();
            _reloj.Start();
        }

        MirarLaSecuencia();
        Textos.AlCambiar(this, static vm =>
        {
            foreach (var p in vm.Pasos) p.Refrescar();
            vm.OnPropertyChanged(string.Empty);
        });
    }

    // ── Lo que pone la ventana ──────────────────────────────────────────────────────────────

    /// <summary>
    /// La pregunta de transmitir. Sin ella puesta y con la pregunta pedida en los ajustes,
    /// <b>no se transmite</b> (al reves que el modem: aqui, en duda, no).
    /// </summary>
    public Func<string, bool>? ConfirmarQueVaATransmitir { get; set; }

    /// <summary>La pregunta de cambiar el equipo a CW. Sin ella, no se cambia y no se transmite.</summary>
    public Func<string, bool>? PreguntarSiCambiaACw { get; set; }

    /// <summary>Elige el fichero donde exportar las macros; nulo si se cancela.</summary>
    public Func<string?>? ElegirFicheroParaExportar { get; set; }

    /// <summary>Elige el fichero de macros que importar; nulo si se cancela.</summary>
    public Func<string?>? ElegirFicheroParaImportar { get; set; }

    /// <summary>
    /// Indicativo propio fijo. Vacio (lo normal): el del perfil de estacion activo, que sale del
    /// modem propio o del decodificador.
    /// </summary>
    public string? MiIndicativo { get; set; }

    /// <summary>La hora, para las pruebas.</summary>
    public Func<DateTimeOffset> Ahora { get; init; } = () => DateTimeOffset.UtcNow;

    // ── Lo que se ve ────────────────────────────────────────────────────────────────────────

    /// <summary>Las doce macros del juego en uso.</summary>
    public ObservableCollection<MacroCw> Macros { get; } = [];

    /// <summary>La tira de la secuencia.</summary>
    public ObservableCollection<PasoEnPantalla> Pasos { get; } = [];

    /// <summary>El secuenciador, para las pruebas.</summary>
    public SecuenciadorCw Secuencia => _secuencia;

    /// <summary>El emisor, para las pruebas.</summary>
    public EmisorCw Emisor => _emisor;

    /// <summary>Las variables que se pueden usar en una macro, para la ayuda del editor.</summary>
    public string VariablesTexto => string.Join("  ", ExpansorDeMacrosCw.Variables) + "  <+5> <-5> <WPM 25>  <AR> <SK> <BT> <KN>";

    /// <summary>El pestillo de la sesion (el mismo del modem propio). Empieza cerrado.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SePuedeTransmitir))]
    [NotifyPropertyChangedFor(nameof(Motivo))]
    private bool _permitirTransmitir;

    /// <summary>Secuencia automatica: contesta sola a lo que lee el decodificador. Empieza apagada siempre.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaSecuencia))]
    private bool _automatico;

    /// <summary>Juego de macros en uso: conversacion, contactos o concurso.</summary>
    [ObservableProperty]
    private PerfilDeMacrosCw _perfil;

    /// <summary>El perfil que estaba puesto antes del cambio, para volcar sus macros al salir de el.</summary>
    private PerfilDeMacrosCw _perfilAnterior;

    /// <summary>Busco y contesto (S&amp;P) en vez de llamar CQ.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoDeLaSecuencia))]
    private bool _busco;

    /// <summary>Velocidad (WPM).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WpmTexto))]
    private int _wpm;

    /// <summary>Texto libre que escribe el operador.</summary>
    [ObservableProperty]
    private string _textoLibre = string.Empty;

    /// <summary>Lo escrito sale palabra a palabra mientras se teclea.</summary>
    [ObservableProperty]
    private bool _escribirMientrasSeEnvia;

    /// <summary>Se ve el editor de macros.</summary>
    [ObservableProperty]
    private bool _editandoMacros;

    /// <summary>Nombre del operador ({MINOMBRE}).</summary>
    [ObservableProperty]
    private string _miNombre;

    /// <summary>QTH propio ({MIQTH}).</summary>
    [ObservableProperty]
    private string _miQth;

    /// <summary>Siguiente numero de serie ({NR}).</summary>
    [ObservableProperty]
    private int _numero;

    /// <summary>Tope de una transmision (s).</summary>
    [ObservableProperty]
    private int _tiempoMaximoSegundos;

    /// <summary>El PTT esta arriba por la telegrafia.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTx))]
    private bool _enAntena;

    /// <summary>Lo que esta manipulando el equipo ahora.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTx))]
    private string _enviando = string.Empty;

    /// <summary>Lo que falta por salir.</summary>
    [ObservableProperty]
    private string _pendiente = string.Empty;

    /// <summary>Velocidad del trozo en curso.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstadoTx))]
    private int _wpmEnCurso;

    /// <summary>Un aviso para el operador.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayAviso))]
    private string _aviso = string.Empty;

    /// <summary>El corresponsal del contacto en curso.</summary>
    [ObservableProperty]
    private string _corresponsal = string.Empty;

    /// <summary>El RST que me ha dado.</summary>
    [ObservableProperty]
    private string _rstRecibido = string.Empty;

    /// <summary>Hay aviso.</summary>
    public bool HayAviso => Aviso.Length > 0;

    /// <summary>Por que no se puede transmitir ahora (equipo o pestillo), o vacio.</summary>
    public string Motivo => _emisor.PorQueNoPuede
        ?? (PermitirTransmitir ? string.Empty : Textos.T("Cabina.TxCw.PestilloCerrado"));

    /// <summary>Se puede pulsar una macro.</summary>
    public bool SePuedeTransmitir => PermitirTransmitir && _emisor.PorQueNoPuede is null;

    /// <summary>«22 WPM».</summary>
    public string WpmTexto => Textos.F("Cabina.TxCw.Wpm", Wpm);

    /// <summary>«EN ANTENA · «CQ CQ…» · 22 WPM» o «En escucha».</summary>
    public string EstadoTx => EnAntena
        ? Textos.F("Cabina.TxCw.EnAntena", Enviando, WpmEnCurso)
        : Textos.T("Cabina.TxCw.EnEscucha");

    /// <summary>En que va la secuencia.</summary>
    public string EstadoDeLaSecuencia => !Automatico
        ? Textos.T("Cabina.TxCw.Secuencia.Manual")
        : Busco ? Textos.T("Cabina.TxCw.Secuencia.AutoBusco") : Textos.T("Cabina.TxCw.Secuencia.AutoLlamo");

    // ── Ordenes ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Manda una macro (clic en su boton o su tecla F).</summary>
    /// <param name="macro">La macro.</param>
    /// <returns>La tarea.</returns>
    [RelayCommand]
    public Task EnviarMacroAsync(MacroCw? macro)
    {
        if (macro is null) return Task.CompletedTask;
        var paso = PasoDeLaTecla(macro.Numero);
        return EnviarAsync([macro], paso);
    }

    /// <summary>La tecla F<paramref name="numero"/>.</summary>
    /// <param name="numero">1 a 12.</param>
    /// <returns>La tarea.</returns>
    public Task PulsarTeclaAsync(int numero) =>
        numero is >= 1 and <= 12 ? EnviarMacroAsync(Macros[numero - 1]) : Task.CompletedTask;

    /// <summary>Intro: manda lo que toca segun la secuencia (el ESM de N1MM).</summary>
    /// <returns>La tarea.</returns>
    [RelayCommand]
    public Task IntroAsync()
    {
        var paso = _secuencia.Siguiente;
        if (paso == PasoCw.Libre) paso = Busco ? PasoCw.Respuesta : PasoCw.Cq;
        var macros = MacrosDelPaso(paso);
        if (macros.Count == 0)
        {
            Aviso = Textos.T("Cabina.TxCw.PasoSinMacro");
            return Task.CompletedTask;
        }

        return EnviarAsync(macros, paso);
    }

    /// <summary>Manda el texto libre.</summary>
    /// <returns>La tarea.</returns>
    [RelayCommand]
    public async Task EnviarTextoLibreAsync()
    {
        var texto = TextoLibre.Trim();
        if (texto.Length == 0) return;
        TextoLibre = string.Empty;
        await EnviarPlantillaAsync(texto, null).ConfigureAwait(true);
    }

    /// <summary>PARAR CW (y Esc): para el manipulador y baja el PTT en el acto. Apaga el automatico.</summary>
    /// <returns>La tarea.</returns>
    [RelayCommand]
    public async Task PararAsync()
    {
        Automatico = false;
        _pasosEnCurso.Clear();
        try
        {
            await _emisor.PararAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al parar la telegrafía.");
            Aviso = Textos.F("Cabina.TxCw.FalloAlParar", ex.Message);
        }
    }

    /// <summary>Sube la velocidad 2 WPM.</summary>
    [RelayCommand]
    public void MasRapido() => Wpm = Math.Min(_emisor.Velocidades.Maxima, Wpm + 2);

    /// <summary>Baja la velocidad 2 WPM.</summary>
    [RelayCommand]
    public void MasDespacio() => Wpm = Math.Max(_emisor.Velocidades.Minima, Wpm - 2);

    /// <summary>Contacto nuevo: olvida el de la secuencia (no toca el formulario).</summary>
    [RelayCommand]
    public void NuevoContacto()
    {
        _secuencia.Empezar(Busco ? PapelCw.Busco : PapelCw.Llamo, _entrada?.Indicativo);
        Corresponsal = _secuencia.Corresponsal;
        RstRecibido = string.Empty;
        MirarLaSecuencia();
    }

    /// <summary>Quita el aviso.</summary>
    [RelayCommand]
    public void CerrarAviso() => Aviso = string.Empty;

    /// <summary>Abre o cierra el editor de macros.</summary>
    [RelayCommand]
    public void AlternarEditor() => EditandoMacros = !EditandoMacros;

    /// <summary>Guarda las macros y los ajustes.</summary>
    [RelayCommand]
    public void GuardarMacros()
    {
        VolcarAlGuardado();
        Guardar();
        Aviso = Textos.T("Cabina.TxCw.Guardado");
    }

    /// <summary>Cambia el juego de macros en uso.</summary>
    [RelayCommand]
    public void ElegirPerfil(PerfilDeMacrosCw perfil) => Perfil = perfil;

    /// <summary>Vuelve a las macros de fabrica del juego en uso.</summary>
    [RelayCommand]
    public void RestaurarFabrica()
    {
        switch (Perfil)
        {
            case PerfilDeMacrosCw.Contactos: _guardado.Contactos = JuegosDeFabrica.Contactos(); break;
            case PerfilDeMacrosCw.Concurso: _guardado.Concurso = JuegosDeFabrica.Concurso(); break;
            default: _guardado.Conversacion = JuegosDeFabrica.Conversacion(); break;
        }

        CargarJuego();
        Guardar();
    }

    /// <summary>Exporta las macros (los dos juegos) a un fichero.</summary>
    [RelayCommand]
    public void Exportar()
    {
        if (ElegirFicheroParaExportar?.Invoke() is not { Length: > 0 } ruta) return;
        try
        {
            VolcarAlGuardado();
            File.WriteAllText(ruta, _guardado.AJson(), new UTF8Encoding(false));
            Aviso = Textos.F("Cabina.TxCw.Exportado", Path.GetFileName(ruta));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Aviso = Textos.F("Cabina.TxCw.NoExporta", ex.Message);
        }
    }

    /// <summary>Importa las macros de un fichero (los dos juegos).</summary>
    [RelayCommand]
    public void Importar()
    {
        if (ElegirFicheroParaImportar?.Invoke() is not { Length: > 0 } ruta) return;
        try
        {
            var leidos = AjustesDeTransmisionCw.Desde(File.ReadAllText(ruta, Encoding.UTF8));
            _guardado.Conversacion = leidos.Conversacion;
            _guardado.Contactos = leidos.Contactos;
            _guardado.Concurso = leidos.Concurso;
            CargarJuego();
            Guardar();
            Aviso = Textos.F("Cabina.TxCw.Importado", Path.GetFileName(ruta));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Aviso = Textos.F("Cabina.TxCw.NoImporta", ex.Message);
        }
    }

    // ── El reloj ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Mira el silencio: cierra la pasada del otro si lleva callado un rato y, en automatico,
    /// repite lo ultimo si nadie contesta. Lo llama el reloj cada cuarto de segundo.
    /// </summary>
    public void Latir()
    {
        OnPropertyChanged(nameof(Motivo));
        OnPropertyChanged(nameof(SePuedeTransmitir));
        if (_emisor.Enviando) return;

        var ahora = Ahora();
        if (_lector is not null && _lector.UltimaNovedad > _ultimaNovedadVista)
        {
            _ultimaNovedadVista = _lector.UltimaNovedad;
        }

        // Fin de pasada por silencio.
        var hayPasada = _lector?.HayPendiente == true || _secuencia.PasadaEnCurso.Length > 0;
        if (hayPasada && _ultimaNovedadVista != DateTimeOffset.MinValue && ahora - _ultimaNovedadVista >= SilencioDeFinDePasada)
        {
            _lector?.Cerrar();
            TerminarPasada();
            return;
        }

        // Nadie contesta: repetir lo ultimo, o rendirse.
        if (!Automatico || !SePuedeTransmitir || _finDeLaUltimaTx == DateTimeOffset.MinValue) return;
        var ultimaActividad = _ultimaNovedadVista > _finDeLaUltimaTx ? _ultimaNovedadVista : _finDeLaUltimaTx;
        if (ahora - ultimaActividad < TimeSpan.FromSeconds(_guardado.SegundosSinRespuesta)) return;

        _finDeLaUltimaTx = ahora;
        if (_secuencia.Silencio(_guardado.RepeticionesMaximas))
        {
            _ = EnviarLoQueTocaAsync();
        }
        else if (_secuencia.Paso != PasoCw.Libre && !_secuencia.Completo)
        {
            Automatico = false;
            Aviso = Textos.T("Cabina.TxCw.SinRespuesta");
        }
    }

    /// <summary>Lo que ha leido el decodificador, palabra a palabra (lo usan tambien las pruebas).</summary>
    /// <param name="palabra">Una palabra terminada.</param>
    public void Oir(string palabra)
    {
        if (_emisor.Enviando) return;
        _ultimaNovedadVista = Ahora();
        if (_secuencia.Oir(palabra)) TerminarPasada();
    }

    /// <summary>Se acaba la pasada del otro: la secuencia decide y, en automatico, se contesta.</summary>
    public void TerminarPasada()
    {
        if (_secuencia.TerminarPasada())
        {
            MirarLaSecuencia();
            if (Automatico) _ = EnviarLoQueTocaAsync();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _reloj?.Stop();
        _emisor.EstadoCambiado -= AlCambiarElEmisor;
        _control.EstadoCambiado -= AlCambiarElEquipo;
        if (_modem is not null) _modem.PropertyChanged -= AlCambiarElModem;
        if (_entrada is not null) _entrada.PropertyChanged -= AlCambiarLaEntrada;
        if (_lector is not null)
        {
            _lector.PalabraLeida -= AlLeerPalabra;
            _lector.Dispose();
        }
    }

    // ── El envio, con todas sus puertas ─────────────────────────────────────────────────────

    private Task EnviarLoQueTocaAsync()
    {
        var paso = _secuencia.Siguiente;
        var macros = MacrosDelPaso(paso);
        return macros.Count == 0 ? Task.CompletedTask : EnviarAsync(macros, paso);
    }

    private Task EnviarAsync(IReadOnlyList<MacroCw> macros, PasoCw? paso) =>
        EnviarPlantillaAsync(string.Join(' ', macros.Select(m => m.Texto)), paso);

    private async Task EnviarPlantillaAsync(string plantilla, PasoCw? paso)
    {
        var (minima, maxima) = _emisor.Velocidades;
        var resultado = ExpansorDeMacrosCw.Expandir(plantilla, Contexto(), Wpm, minima, maxima);
        if (resultado.FaltaMiIndicativo)
        {
            Aviso = Textos.T("Cabina.TxCw.FaltaMiIndicativo");
            return;
        }

        if (resultado.FaltaIndicativo)
        {
            Aviso = Textos.T("Cabina.TxCw.FaltaIndicativo");
            return;
        }

        if (resultado.Vacio) return;

        // Con una transmision en marcha, se añade detras: ya paso por todas las puertas.
        if (_emisor.Enviando)
        {
            if (_emisor.Encolar(resultado.Trozos))
            {
                if (paso is { } p) _pasosEnCurso.Add(p);
                return;
            }
        }

        if (!await PasarLasPuertasAsync(resultado.Texto).ConfigureAwait(true)) return;

        _pasosEnCurso.Clear();
        if (paso is { } primero) _pasosEnCurso.Add(primero);
        _emisor.Opciones.TiempoMaximo = TimeSpan.FromSeconds(TiempoMaximoSegundos);
        _lector?.Saltar();
        Aviso = string.Empty;

        FinDelEnvioCw fin;
        try
        {
            fin = await _emisor.TransmitirAsync(resultado.Trozos, "Telegrafía: " + Resumen(resultado.Texto)).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido transmitir la telegrafía.");
            Aviso = Textos.F("Cabina.TxCw.Fallo", ex.Message);
            _pasosEnCurso.Clear();
            return;
        }

        // Lo que leyo el decodificador mientras tanto es el propio tono de escucha.
        _lector?.Saltar();
        _finDeLaUltimaTx = Ahora();
        var pasos = _pasosEnCurso.ToList();
        _pasosEnCurso.Clear();

        switch (fin)
        {
            case FinDelEnvioCw.Completo:
                foreach (var p in pasos) _secuencia.AlEnviar(p);
                MirarLaSecuencia();
                break;
            case FinDelEnvioCw.TiempoAgotado:
                Automatico = false;
                Aviso = Textos.F("Cabina.TxCw.TiempoAgotado", TiempoMaximoSegundos);
                break;
            case FinDelEnvioCw.Parado:
                Automatico = false;
                Aviso = Textos.T("Cabina.TxCw.Parado");
                break;
            default:
                Automatico = false;
                Aviso = Textos.F("Cabina.TxCw.Fallo", _emisor.UltimoFallo?.Message ?? string.Empty);
                break;
        }
    }

    /// <summary>Pestillo, equipo, modo CW y pregunta, en este orden.</summary>
    private async Task<bool> PasarLasPuertasAsync(string texto)
    {
        if (!PermitirTransmitir)
        {
            Aviso = Textos.T("Cabina.TxCw.PestilloCerrado");
            return false;
        }

        if (_emisor.PorQueNoPuede is { } porque)
        {
            Aviso = porque;
            return false;
        }

        if (!EstaEnCw(_control.Estado.Modo))
        {
            var pregunta = Textos.F("Cabina.TxCw.CambiarACw", _control.Estado.Modo.NombreUsual ?? "?");
            if (PreguntarSiCambiaACw is not { } preguntar || !preguntar(pregunta))
            {
                Automatico = false;
                Aviso = Textos.T("Cabina.TxCw.NoEstaEnCw");
                return false;
            }

            try
            {
                await _control.PonerModoAsync(Modo.Parse("CW")).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Aviso = Textos.F("Cabina.TxCw.NoCambiaACw", ex.Message);
                return false;
            }
        }

        if (_ajustes.Digital.PedirConfirmacionAlTransmitir
            && (ConfirmarQueVaATransmitir is not { } confirmar || !confirmar(Resumen(texto))))
        {
            Automatico = false;
            Aviso = Textos.T("Cabina.TxCw.NoConfirmado");
            return false;
        }

        return true;
    }

    /// <summary>«No volver a preguntar» del dialogo: el mismo ajuste que el del modem.</summary>
    public void NoVolverAPreguntarAlTransmitir()
    {
        _ajustes.Digital.PedirConfirmacionAlTransmitir = false;
        Log.Warning("El operador ha pedido no volver a confirmar las transmisiones (telegrafía).");
        if (_modem is not null) _modem.NoVolverAPreguntarAlTransmitir();
    }

    private static bool EstaEnCw(Modo modo) =>
        (modo.NombreUsual ?? string.Empty).StartsWith("CW", StringComparison.OrdinalIgnoreCase)
        || (modo.Principal ?? string.Empty).StartsWith("CW", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// El RST de telegrafia: tres cifras. El formulario puede traer el «59» de fonia si el equipo
    /// no estaba en CW al abrirlo; entonces se da 599.
    /// </summary>
    private static string RstDeCw(string? rst) =>
        rst is { Length: 3 } r && r.All(char.IsAsciiDigit) ? r : "599";

    private static string Resumen(string texto) => texto.Length <= 60 ? texto : texto[..57] + "…";

    private ContextoDeMacroCw Contexto()
    {
        var mio = MiIndicativo is { Length: > 0 } fijo ? fijo
            : _modem is { MiIndicativo.EsVacio: false } m ? m.MiIndicativo.Valor
            : _cw?.MiIndicativo ?? string.Empty;
        var miLoc = _modem is { MiLocalizador.EsVacio: false } ml ? ml.MiLocalizador.Valor : string.Empty;
        var call = _entrada?.Indicativo is { Length: > 0 } i ? i : _secuencia.Corresponsal;
        return new ContextoDeMacroCw(
            mio,
            call.Trim().ToUpperInvariant(),
            RstDeCw(_entrada?.InformeEnviado),
            Numero,
            _entrada?.Nombre ?? _secuencia.Nombre,
            _entrada?.Qth ?? _secuencia.Qth,
            _entrada?.Localizador ?? _secuencia.Localizador,
            MiNombre,
            MiQth,
            miLoc,
            Perfil == PerfilDeMacrosCw.Concurso);
    }

    // ── Macros y secuencia ──────────────────────────────────────────────────────────────────

    private JuegoDeMacrosGuardado JuegoDe(PerfilDeMacrosCw perfil) => perfil switch
    {
        PerfilDeMacrosCw.Contactos => _guardado.Contactos,
        PerfilDeMacrosCw.Concurso => _guardado.Concurso,
        _ => _guardado.Conversacion,
    };

    private JuegoDeMacrosGuardado JuegoEnUso => JuegoDe(Perfil);

    private void CargarJuego()
    {
        Macros.Clear();
        var juego = JuegoEnUso;
        for (var i = 0; i < juego.Macros.Count; i++) Macros.Add(new MacroCw(i + 1, juego.Macros[i].Rotulo, juego.Macros[i].Texto));
        _secuencia.Concurso = juego.Concurso;
        _secuencia.CierraConElInforme = juego.Pasos.SetentaYTres.Length == 0;
        MirarLaSecuencia();
    }

    private void VolcarAlGuardado()
    {
        JuegoEnUso.Macros = Macros.Select(m => new MacroGuardada(m.Rotulo, m.Texto)).ToList();
        _guardado.Wpm = Wpm;
        _guardado.Perfil = Perfil;
        _guardado.EscribirMientrasSeEnvia = EscribirMientrasSeEnvia;
        _guardado.MiNombre = MiNombre;
        _guardado.MiQth = MiQth;
        _guardado.SiguienteNumero = Numero;
        _guardado.TiempoMaximoSegundos = TiempoMaximoSegundos;
        _guardado.Acotar();
    }

    private void Guardar()
    {
        if (string.IsNullOrEmpty(_carpeta)) return;
        try
        {
            _guardado.Guardar(_carpeta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "No se han podido guardar las macros de telegrafía.");
            Aviso = Textos.F("Cabina.TxCw.NoGuarda", ex.Message);
        }
    }

    private List<MacroCw> MacrosDelPaso(PasoCw paso)
    {
        var p = JuegoEnUso.Pasos;
        var teclas = paso switch
        {
            PasoCw.Cq => p.Cq,
            PasoCw.Respuesta => p.Respuesta,
            PasoCw.Informe => _secuencia.Papel == PapelCw.Busco || (Busco && _secuencia.Paso == PasoCw.Libre) ? p.InformeBuscando : p.InformeLlamando,
            PasoCw.Confirmacion => p.Confirmacion,
            PasoCw.SetentaYTres => p.SetentaYTres,
            _ => [],
        };
        return teclas.Select(t => Macros[t - 1]).ToList();
    }

    private PasoCw? PasoDeLaTecla(int tecla)
    {
        var p = JuegoEnUso.Pasos;
        if (p.Cq.Contains(tecla)) return PasoCw.Cq;
        if (p.Respuesta.Contains(tecla)) return PasoCw.Respuesta;
        if (p.InformeLlamando.Contains(tecla) || p.InformeBuscando.Contains(tecla)) return PasoCw.Informe;
        if (p.Confirmacion.Contains(tecla)) return PasoCw.Confirmacion;
        if (p.SetentaYTres.Contains(tecla)) return PasoCw.SetentaYTres;
        return null;
    }

    private void MirarLaSecuencia()
    {
        _secuencia.MiIndicativo = Contexto().MiIndicativo;
        var siguiente = _secuencia.Siguiente;
        foreach (var p in Pasos)
        {
            p.Hecho = p.Paso == _secuencia.Paso;
            p.Toca = p.Paso == siguiente;
        }

        var toca = MacrosDelPaso(siguiente == PasoCw.Libre ? (Busco ? PasoCw.Respuesta : PasoCw.Cq) : siguiente).Select(m => m.Numero).ToHashSet();
        foreach (var m in Macros) m.EsLaQueToca = toca.Contains(m.Numero);
        Corresponsal = _secuencia.Corresponsal;
        RstRecibido = _secuencia.RstRecibido;
        OnPropertyChanged(nameof(EstadoDeLaSecuencia));
    }

    // ── Eventos ─────────────────────────────────────────────────────────────────────────────

    private void AlLeerPalabra(object? origen, string palabra) => Oir(palabra);

    private void AlEncontrarCorresponsal(object? origen, string indicativo)
    {
        if (_entrada is not null && !string.Equals(_entrada.Indicativo, indicativo, StringComparison.OrdinalIgnoreCase))
        {
            _entrada.Indicativo = indicativo;
        }

        Corresponsal = indicativo;
    }

    private void AlRecibirDatos(object? origen, EventArgs e)
    {
        RstRecibido = _secuencia.RstRecibido;
        if (_entrada is null) return;
        if (_secuencia.RstRecibido.Length > 0) _entrada.InformeRecibido = _secuencia.RstRecibido;
        if (_secuencia.Nombre.Length > 0 && _entrada.Nombre.Length == 0) _entrada.Nombre = _secuencia.Nombre;
        if (_secuencia.Qth.Length > 0 && _entrada.Qth.Length == 0) _entrada.Qth = _secuencia.Qth;
        if (_secuencia.Localizador.Length > 0 && _entrada.Localizador.Length == 0) _entrada.Localizador = _secuencia.Localizador;
    }

    private async void AlCompletarse(object? origen, DatosDelQsoCw datos)
    {
        try
        {
            AlRecibirDatos(origen, EventArgs.Empty);
            var numeroEnviado = Numero;
            if (Perfil == PerfilDeMacrosCw.Concurso)
            {
                Numero = Math.Min(9999, Numero + 1);
                VolcarAlGuardado();
                Guardar();
            }

            if (_entrada is null) return;
            _entrada.InformeEnviado = RstDeCw(_entrada.InformeEnviado);
            if (Perfil == PerfilDeMacrosCw.Concurso)
            {
                var nota = Textos.F("Cabina.TxCw.NotaConcurso", numeroEnviado.ToString("000", System.Globalization.CultureInfo.InvariantCulture), datos.NumeroRecibido);
                _entrada.Comentario = string.IsNullOrWhiteSpace(_entrada.Comentario) ? nota : _entrada.Comentario + " · " + nota;
            }

            // La misma regla que el FT8: con «registrar al completar» puesto, se apunta solo.
            if (_ajustes.Digital.RegistrarAlCompletar)
            {
                await _entrada.GuardarCommand.ExecuteAsync(null).ConfigureAwait(true);
                Aviso = Textos.F("Cabina.TxCw.Registrado", datos.Indicativo);
            }
            else
            {
                Aviso = Textos.F("Cabina.TxCw.ListoParaRegistrar", datos.Indicativo);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido apuntar el contacto de telegrafía.");
            Aviso = Textos.F("Cabina.TxCw.NoRegistra", ex.Message);
        }
    }

    private void AlCambiarLaEntrada(object? origen, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VistaModeloEntradaQso.Indicativo) || _entrada is null) return;
        _secuencia.PonerCorresponsal(_entrada.Indicativo);
        MirarLaSecuencia();
    }

    private void AlCambiarElModem(object? origen, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VistaModeloModemPropio.PermitirTransmitir) || _modem is null || _sincronizandoPestillo) return;
        _sincronizandoPestillo = true;
        try
        {
            PermitirTransmitir = _modem.PermitirTransmitir;
        }
        finally
        {
            _sincronizandoPestillo = false;
        }
    }

    private void AlCambiarElEmisor(object? origen, EstadoDelEmisorCw estado) => Hilo.EnLaVentana(() =>
    {
        EnAntena = estado.EnAntena;
        Enviando = estado.Enviando;
        Pendiente = estado.Pendiente;
        WpmEnCurso = estado.Wpm;
    });

    private void AlCambiarElEquipo(object? origen, EstadoDelEquipo estado) => Hilo.EnLaVentana(() =>
    {
        OnPropertyChanged(nameof(Motivo));
        OnPropertyChanged(nameof(SePuedeTransmitir));
    });

    partial void OnPermitirTransmitirChanged(bool value)
    {
        if (!value)
        {
            // Cerrar el pestillo corta lo que este saliendo.
            Automatico = false;
            if (_emisor.Enviando) _ = PararAsync();
        }

        if (_modem is not null && !_sincronizandoPestillo && _modem.PermitirTransmitir != value)
        {
            _sincronizandoPestillo = true;
            try
            {
                _modem.PermitirTransmitir = value;
            }
            finally
            {
                _sincronizandoPestillo = false;
            }
        }
    }

    partial void OnAutomaticoChanged(bool value)
    {
        if (value && !PermitirTransmitir)
        {
            Automatico = false;
            Aviso = Textos.T("Cabina.TxCw.PestilloCerrado");
        }
    }

    partial void OnPerfilChanged(PerfilDeMacrosCw value)
    {
        VolcarJuegoAnterior(_perfilAnterior);
        _perfilAnterior = value;
        CargarJuego();
        _guardado.Perfil = value;
        Guardar();
    }

    private void VolcarJuegoAnterior(PerfilDeMacrosCw perfilAnterior)
    {
        if (Macros.Count == 0) return;
        JuegoDe(perfilAnterior).Macros = Macros.Select(m => new MacroGuardada(m.Rotulo, m.Texto)).ToList();
    }

    partial void OnBuscoChanged(bool value)
    {
        if (_secuencia.Paso == PasoCw.Libre || _secuencia.Completo) _secuencia.Empezar(value ? PapelCw.Busco : PapelCw.Llamo, _entrada?.Indicativo);
        MirarLaSecuencia();
    }

    partial void OnWpmChanged(int value)
    {
        var (minima, maxima) = _emisor.Velocidades;
        if (value < minima || value > maxima)
        {
            Wpm = Math.Clamp(value, minima, maxima);
            return;
        }

        _guardado.Wpm = value;
    }

    partial void OnTiempoMaximoSegundosChanged(int value) => _guardado.TiempoMaximoSegundos = Math.Clamp(value, 10, 180);

    partial void OnEscribirMientrasSeEnviaChanged(bool value) => _guardado.EscribirMientrasSeEnvia = value;

    partial void OnTextoLibreChanged(string value)
    {
        // «Escribir mientras se envia»: cada palabra terminada (con su espacio) sale ya.
        if (!EscribirMientrasSeEnvia || value.Length == 0 || !char.IsWhiteSpace(value[^1])) return;
        var palabras = value.Trim();
        if (palabras.Length == 0) return;
        TextoLibre = string.Empty;
        _ = EnviarPlantillaAsync(palabras, null);
    }
}
