using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Ajustes;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// El apartado de audio y modos digitales de la pantalla de ajustes.
/// </summary>
/// <remarks>
/// <para>
/// Aqui se elige por donde entra y sale el sonido y <b>quien decodifica</b>: el modem propio
/// o el puente con WSJT-X y JTDX. El codec USB del FT-710 aparece en Windows como un
/// dispositivo de sonido mas entre veintitantos, asi que sale marcado y el primero de la
/// lista; pedirle al operador que lo adivine por el nombre es pedir un error, y ese error se
/// paga decodificando el escritorio o transmitiendo por los altavoces.
/// </para>
/// <para>
/// <b>Abrir la entrada es una accion, no un efecto secundario.</b> Esta pantalla no toca la
/// tarjeta de sonido al abrirse: solo cuando se pulsa «Probar el nivel», y se cierra al parar
/// o al salir. Abrirle el microfono a alguien por el mero hecho de entrar en Ajustes no se
/// hace.
/// </para>
/// <para>
/// Y el medidor <b>avisa</b>, no solo pinta: el nivel de esta estacion llegaba a 0,91–0,99,
/// al borde de recortar, y un codec saturado no decodifica mejor por ir mas fuerte sino peor,
/// porque el recorte se reparte por todo el ancho de banda y tapa a las señales debiles.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAjustesAudio : ObservableObject
{
    /// <summary>Cada cuanto se relee el nivel mientras se prueba.</summary>
    private static readonly TimeSpan RitmoDelMedidor = TimeSpan.FromMilliseconds(120);

    private readonly AjustesDelPrograma _ajustes;
    private readonly string _carpetaDeDatos;
    private readonly IEntradaDeAudio? _entrada;
    private readonly ISalidaDeAudio? _salida;
    private readonly DispatcherTimer _medidor;

    /// <summary>Monta el apartado.</summary>
    /// <param name="ajustes">Ajustes del programa, ya leidos del disco.</param>
    /// <param name="carpetaDeDatos">Carpeta donde se guardan.</param>
    /// <param name="entrada">Captura de audio. Puede faltar en pruebas.</param>
    /// <param name="salida">Reproduccion de audio. Puede faltar en pruebas.</param>
    public VistaModeloAjustesAudio(
        AjustesDelPrograma ajustes,
        string carpetaDeDatos,
        IEntradaDeAudio? entrada = null,
        ISalidaDeAudio? salida = null)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        ArgumentException.ThrowIfNullOrWhiteSpace(carpetaDeDatos);

        _ajustes = ajustes;
        _carpetaDeDatos = carpetaDeDatos;
        _entrada = entrada;
        _salida = salida;

        _medidor = new DispatcherTimer(DispatcherPriority.Background) { Interval = RitmoDelMedidor };
        _medidor.Tick += (_, _) => Nivel = _entrada?.Nivel ?? 0;

        Refrescar();
        RecogerDeLosAjustes();

        // El nivel en palabras y el aviso de los dispositivos se escriben en el idioma nuevo.
        Textos.AlCambiar(this, static vm =>
        {
            vm.OnPropertyChanged(nameof(NivelTexto));
            vm.OnPropertyChanged(nameof(AvisoDeDiscrepancia));
        });
    }

    /// <summary>Dispositivos de captura del sistema, con el del equipo el primero.</summary>
    public ObservableCollection<DispositivoDeAudio> Entradas { get; } = [];

    /// <summary>Dispositivos de reproduccion del sistema.</summary>
    public ObservableCollection<DispositivoDeAudio> Salidas { get; } = [];

    /// <summary>Muestras por segundo que se ofrecen.</summary>
    public IReadOnlyList<int> Frecuencias { get; } = [12000, 24000, 48000, 96000];

    // ── Telegrafía (CW): el decodificador de la cabina ───────────────────────

    /// <summary>Salta al guardar, para que el panel de telegrafía tome los ajustes nuevos.</summary>
    public event EventHandler? AjustesDeCwGuardados;

    /// <summary>Anchos de filtro que se ofrecen (Hz).</summary>
    public IReadOnlyList<int> AnchosDeFiltroCw { get; } = [50, 75, 100, 150, 200, 300, 500];

    /// <summary>Tono por omisión del decodificador (Hz).</summary>
    [ObservableProperty]
    private int _cwTonoHz = 700;

    /// <summary>Ancho del filtro (Hz).</summary>
    [ObservableProperty]
    private int _cwAnchoHz = 100;

    /// <summary>Sensibilidad, de 1 a 10.</summary>
    [ObservableProperty]
    private int _cwSensibilidad = 6;

    /// <summary>Velocidad mínima (WPM).</summary>
    [ObservableProperty]
    private int _cwWpmMinima = 5;

    /// <summary>Velocidad máxima (WPM).</summary>
    [ObservableProperty]
    private int _cwWpmMaxima = 60;

    /// <summary>Señales que se leen a la vez.</summary>
    [ObservableProperty]
    private int _cwSenales = 4;

    /// <summary>Segundos sin señal antes de volver a buscar (AUTO).</summary>
    [ObservableProperty]
    private int _cwSinSenal = 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayDiscrepanciaDeDispositivos))]
    [NotifyPropertyChangedFor(nameof(AvisoDeDiscrepancia))]
    private DispositivoDeAudio? _entradaElegida;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayDiscrepanciaDeDispositivos))]
    [NotifyPropertyChangedFor(nameof(AvisoDeDiscrepancia))]
    private DispositivoDeAudio? _salidaElegida;

    [ObservableProperty]
    private int _frecuenciaDeMuestreo = 48000;

    /// <summary>Preguntar antes de cada emision del modem.</summary>
    [ObservableProperty]
    private bool _pedirConfirmacionAlTransmitir = true;

    /// <summary>El pestillo «Permitir transmitir» arranca abierto.</summary>
    [ObservableProperty]
    private bool _recordarPermisoDeTransmitir;

    /// <summary>Apuntar solo el contacto que la secuencia da por completo.</summary>
    [ObservableProperty]
    private bool _registrarAlCompletar = true;


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NivelPorCiento))]
    [NotifyPropertyChangedFor(nameof(NivelSatura))]
    [NotifyPropertyChangedFor(nameof(NivelCorto))]
    [NotifyPropertyChangedFor(nameof(NivelTexto))]
    private double _nivel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NivelCorto))]
    [NotifyCanExecuteChangedFor(nameof(ProbarElNivelCommand))]
    [NotifyCanExecuteChangedFor(nameof(PararLaPruebaCommand))]
    private bool _probando;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HayParte))]
    private string _parte = string.Empty;

    [ObservableProperty]
    private bool _fallo;


    /// <summary>Hay módulo de audio detrás de esta pantalla.</summary>
    public bool HayAudio => _entrada is not null;

    /// <summary>El nivel de entrada en tanto por ciento, para el medidor.</summary>
    public double NivelPorCiento => Math.Clamp(Nivel, 0, 1) * 100;

    /// <summary>El nivel está al borde de recortar.</summary>
    public bool NivelSatura => Nivel >= AjustesDeDigital.NivelQueSatura;

    /// <summary>El nivel se queda corto.</summary>
    public bool NivelCorto => Probando && Nivel < AjustesDeDigital.NivelQueSeQuedaCorto;

    /// <summary>El nivel, en palabras, con el aviso si procede.</summary>
    public string NivelTexto => Textos.F(
        NivelSatura ? "Ajustes.Audio.Nivel.Satura" : NivelCorto ? "Ajustes.Audio.Nivel.Bajo" : "Ajustes.Audio.Nivel.Bien",
        Nivel);

    /// <summary>Hay un parte que enseñar.</summary>
    public bool HayParte => !string.IsNullOrEmpty(Parte);

    /// <summary>
    /// La entrada elegida es la del equipo de radio y la salida no, o al revés.
    /// </summary>
    /// <remarks>
    /// Es justo el fallo que tuvo Jose: la entrada acertó sola con el micrófono del FT-710
    /// —«EsDelEquipo» lo marcó bien— pero la salida se quedó apuntando al televisor, que
    /// Windows tenía como predeterminado. Un codec USB de un equipo de radio trae siempre la
    /// entrada y la salida juntas bajo el mismo contenedor USB; si una sale marcada «el equipo»
    /// y la otra no, hay algo elegido a mano que no encaja.
    /// </remarks>
    public bool HayDiscrepanciaDeDispositivos =>
        EntradaElegida is not null
        && SalidaElegida is not null
        && EntradaElegida.EsDelEquipo != SalidaElegida.EsDelEquipo;

    /// <summary>El aviso de la discrepancia, listo para pintar; vacío si no hay nada que decir.</summary>
    public string AvisoDeDiscrepancia => HayDiscrepanciaDeDispositivos
        ? Textos.F("Ajustes.Audio.Discrepancia", EntradaElegida?.Nombre, SalidaElegida?.Nombre)
        : string.Empty;

    /// <summary>Vuelve a preguntarle a Windows que dispositivos de sonido hay.</summary>
    [RelayCommand]
    public void Refrescar()
    {
        if (_entrada is Audio.Captura.EntradaDeAudioCompartida entradaReal) entradaReal.Refrescar();
        if (_salida is Audio.Reproduccion.SalidaDeAudioCompartida salidaReal) salidaReal.Refrescar();

        Rellenar(Entradas, _entrada?.Dispositivos, _ajustes.Digital.DispositivoDeEntrada, d => EntradaElegida = d);
        Rellenar(Salidas, _salida?.Dispositivos, _ajustes.Digital.DispositivoDeSalida, d => SalidaElegida = d);
        PedirConfirmacionAlTransmitir = _ajustes.Digital.PedirConfirmacionAlTransmitir;
    }

    /// <summary>
    /// Abre la entrada elegida y enseña el nivel.
    /// </summary>
    /// <remarks>
    /// <b>Esto abre el microfono.</b> Es lo unico de esta pantalla que toca la tarjeta de
    /// sonido, es una pulsacion del operador y se cierra al parar o al salir de la pantalla.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(SePuedeProbar))]
    public async Task ProbarElNivelAsync()
    {
        if (_entrada is null || EntradaElegida is null) return;

        try
        {
            await _entrada.AbrirAsync(EntradaElegida.Id, FrecuenciaDeMuestreo).ConfigureAwait(true);
            Probando = true;
            _medidor.Start();
            Fallo = false;
            Parte = Textos.F("Ajustes.Audio.Escuchando", EntradaElegida.Nombre);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido abrir la entrada de audio para probar el nivel.");
            Fallo = true;
            Parte = Textos.F("Ajustes.Audio.NoSeHaPodidoAbrir", EntradaElegida.Nombre, ex.Message);
            await PararLaPruebaAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Cierra la entrada y deja de medir.</summary>
    [RelayCommand(CanExecute = nameof(Probando))]
    public async Task PararLaPruebaAsync()
    {
        _medidor.Stop();
        Probando = false;
        Nivel = 0;

        try
        {
            if (_entrada is not null) await _entrada.CerrarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Fallo al cerrar la entrada de audio de la prueba.");
        }
    }

    /// <summary>Guarda lo elegido en el fichero de ajustes.</summary>
    [RelayCommand]
    public void Guardar()
    {
        try
        {
            var digital = _ajustes.Digital;

            digital.DispositivoDeEntrada = EntradaElegida?.Id;
            digital.NombreDeEntrada = EntradaElegida?.Nombre;
            digital.DispositivoDeSalida = SalidaElegida?.Id;
            digital.NombreDeSalida = SalidaElegida?.Nombre;
            digital.FrecuenciaDeMuestreo = FrecuenciaDeMuestreo;
            digital.Acotar();

            var cw = _ajustes.Cw;
            cw.TonoPorOmisionHz = CwTonoHz;
            cw.AnchoDelFiltroHz = CwAnchoHz;
            cw.Sensibilidad = CwSensibilidad;
            cw.WpmMinima = CwWpmMinima;
            cw.WpmMaxima = CwWpmMaxima;
            cw.Senales = CwSenales;
            cw.SegundosSinSenal = CwSinSenal;
            cw.Acotar();

            _ajustes.Guardar(_carpetaDeDatos);
            RecogerLosDeCw();
            AjustesDeCwGuardados?.Invoke(this, EventArgs.Empty);

            Fallo = false;
            Parte = Textos.T("Ajustes.Guardado");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido guardar los ajustes de audio.");
            Fallo = true;
            Parte = Textos.F("Ajustes.NoSeHanPodidoGuardar", ex.Message);
        }
    }

    /// <summary>Vuelve a poner en pantalla lo que hay guardado.</summary>
    [RelayCommand]
    public void Descartar()
    {
        RecogerDeLosAjustes();
        Parte = string.Empty;
        Fallo = false;
    }

    /// <summary>Cierra lo que haya quedado abierto. Lo llama la ventana al cerrarse.</summary>
    public void Detener()
    {
        _medidor.Stop();
        if (Probando) _ = PararLaPruebaAsync();
    }

    private bool SePuedeProbar() => !Probando && _entrada is not null && EntradaElegida is not null;

    private void RecogerDeLosAjustes()
    {
        var digital = _ajustes.Digital;

        FrecuenciaDeMuestreo = Frecuencias.Contains(digital.FrecuenciaDeMuestreo)
            ? digital.FrecuenciaDeMuestreo
            : 48000;

        PedirConfirmacionAlTransmitir = digital.PedirConfirmacionAlTransmitir;
        RecordarPermisoDeTransmitir = digital.RecordarPermisoDeTransmitir;
        RegistrarAlCompletar = digital.RegistrarAlCompletar;

        EntradaElegida = Escoger(Entradas, digital.DispositivoDeEntrada);
        SalidaElegida = Escoger(Salidas, digital.DispositivoDeSalida);
        RecogerLosDeCw();
    }

    private void RecogerLosDeCw()
    {
        var cw = _ajustes.Cw;
        CwTonoHz = cw.TonoPorOmisionHz;
        CwAnchoHz = cw.AnchoDelFiltroHz;
        CwSensibilidad = cw.Sensibilidad;
        CwWpmMinima = cw.WpmMinima;
        CwWpmMaxima = cw.WpmMaxima;
        CwSenales = cw.Senales;
        CwSinSenal = cw.SegundosSinSenal;
    }

    private static void Rellenar(
        ObservableCollection<DispositivoDeAudio> destino,
        IReadOnlyList<DispositivoDeAudio>? origen,
        string? guardado,
        Action<DispositivoDeAudio?> elegir)
    {
        destino.Clear();
        foreach (var dispositivo in origen ?? []) destino.Add(dispositivo);
        elegir(Escoger(destino, guardado));
    }

    /// <summary>
    /// El que estaba guardado, y si ya no esta, el del equipo.
    /// </summary>
    /// <remarks>
    /// Nunca «el primero de la lista» a ciegas: en esta maquina hay mas de veinte dispositivos
    /// de sonido y el primero puede ser cualquier cosa.
    /// </remarks>
    private static DispositivoDeAudio? Escoger(
        IReadOnlyCollection<DispositivoDeAudio> lista, string? guardado)
    {
        if (!string.IsNullOrEmpty(guardado))
        {
            var elegido = lista.FirstOrDefault(d => d.Id == guardado);
            if (elegido is not null) return elegido;
        }

        return lista.FirstOrDefault(d => d.EsDelEquipo);
    }

    // Las tres opciones de transmitir y apuntar valen en el acto (el modem las lee de los
    // ajustes); «Guardar» las deja en el fichero. Asi no pisan lo que se marque en el dialogo.
    partial void OnPedirConfirmacionAlTransmitirChanged(bool value) => _ajustes.Digital.PedirConfirmacionAlTransmitir = value;

    partial void OnRecordarPermisoDeTransmitirChanged(bool value) => _ajustes.Digital.RecordarPermisoDeTransmitir = value;

    partial void OnRegistrarAlCompletarChanged(bool value) => _ajustes.Digital.RegistrarAlCompletar = value;

    partial void OnEntradaElegidaChanged(DispositivoDeAudio? value) =>
        ProbarElNivelCommand.NotifyCanExecuteChanged();
}
