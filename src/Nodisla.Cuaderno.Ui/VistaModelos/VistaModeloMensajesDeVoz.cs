using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Audio.Procesado;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Ajustes;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Una ranura del voice keyer en pantalla.</summary>
public sealed partial class MensajeEnPantalla : ObservableObject
{
    /// <summary>Crea la ranura.</summary>
    public MensajeEnPantalla(int numero, string nombre, AudioEnMemoria? audio)
    {
        Numero = numero;
        _nombre = nombre;
        Audio = audio;
    }

    /// <summary>Ranura, de 1 a 6.</summary>
    public int Numero { get; }

    /// <summary>Tecla que la lanza.</summary>
    public string Tecla => string.Create(CultureInfo.InvariantCulture, $"F{Numero}");

    /// <summary>La grabacion, o nula.</summary>
    public AudioEnMemoria? Audio { get; private set; }

    /// <summary>Hay algo grabado.</summary>
    public bool Grabado => Audio is { Muestras.Length: > 0 };

    /// <summary>Lo que dura, escrito.</summary>
    public string Duracion => Audio is { } a && Grabado
        ? string.Create(CultureInfo.InvariantCulture, $"{a.Duracion.TotalSeconds:0.0} s")
        : Textos.T("Cabina.Fonia.Voz.Vacio");

    /// <summary>Lo que pone en el boton: tecla y nombre.</summary>
    public string Rotulo => $"{Tecla} {Nombre}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rotulo))]
    private string _nombre;

    [ObservableProperty]
    private bool _grabandoAhora;

    [ObservableProperty]
    private bool _sonandoAhora;

    /// <summary>Cambia la grabacion.</summary>
    public void PonerAudio(AudioEnMemoria? audio)
    {
        Audio = audio;
        OnPropertyChanged(nameof(Audio));
        OnPropertyChanged(nameof(Grabado));
        OnPropertyChanged(nameof(Duracion));
    }

    /// <summary>Vuelve a escribir lo que depende del idioma.</summary>
    public void Refrescar() => OnPropertyChanged(nameof(Duracion));
}

/// <summary>
/// El voice keyer: seis mensajes grabados con el microfono del PC que salen por la fonia con
/// las mismas protecciones que la voz.
/// </summary>
/// <remarks>
/// <para>
/// Para salir al aire pasa por las mismas puertas que la telegrafia, en este orden: el pestillo
/// «Permitir transmitir en esta sesion» (el mismo del modem y de la CW), que se pueda transmitir
/// en fonia (equipo conectado, modo de voz, dispositivos elegidos) y la pregunta de confirmacion.
/// Despues manda <see cref="ControlDeFonia"/>: vigilante, tope de tiempo y latido.
/// </para>
/// <para>
/// Grabar y escuchar son locales: el microfono a memoria y la memoria a los altavoces del PC.
/// Ninguna de las dos cosas va hacia el equipo.
/// </para>
/// </remarks>
public sealed partial class VistaModeloMensajesDeVoz : ObservableObject
{
    private readonly VistaModeloFonia _fonia;
    private readonly ControlDeFonia _control;
    private readonly AlmacenDeMensajesDeVoz _almacen;
    private readonly IGrabadorDeMicrofono _grabador;
    private readonly IReproductorLocal _reproductor;
    private readonly AjustesDelPrograma _ajustes;
    private readonly VistaModeloModemPropio? _modem;
    private bool _sincronizandoPestillo;
    private bool _cargando;

    /// <summary>Monta el voice keyer. No abre ningun dispositivo.</summary>
    public VistaModeloMensajesDeVoz(
        VistaModeloFonia fonia,
        ControlDeFonia control,
        AlmacenDeMensajesDeVoz almacen,
        IGrabadorDeMicrofono grabador,
        IReproductorLocal reproductor,
        AjustesDelPrograma ajustes,
        VistaModeloModemPropio? modem = null)
    {
        ArgumentNullException.ThrowIfNull(fonia);
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(almacen);
        ArgumentNullException.ThrowIfNull(grabador);
        ArgumentNullException.ThrowIfNull(reproductor);
        ArgumentNullException.ThrowIfNull(ajustes);

        _fonia = fonia;
        _control = control;
        _almacen = almacen;
        _grabador = grabador;
        _reproductor = reproductor;
        _ajustes = ajustes;
        _modem = modem;

        // El pestillo es el del modem: uno solo por sesion para todo lo que transmite solo.
        _permitirTransmitir = modem?.PermitirTransmitir ?? false;
        if (modem is not null) modem.PropertyChanged += AlCambiarElModem;

        Cargar();
        Textos.AlCambiar(this, static vm =>
        {
            foreach (var m in vm.Mensajes) m.Refrescar();
        });
    }

    /// <summary>Las seis ranuras.</summary>
    public ObservableCollection<MensajeEnPantalla> Mensajes { get; } = [];

    /// <summary>Pregunta antes de transmitir; la pone la vista. Sin ella, no se transmite si hay que preguntar.</summary>
    public Func<string, bool>? ConfirmarQueVaATransmitir { get; set; }

    /// <summary>El pestillo de la sesion.</summary>
    [ObservableProperty]
    private bool _permitirTransmitir;

    [ObservableProperty]
    private string _aviso = string.Empty;

    [ObservableProperty]
    private bool _grabando;

    [ObservableProperty]
    private double _nivelDeGrabacion;

    /// <summary>Hay un mensaje en el aire.</summary>
    public bool Enviando => _control.EnviandoMensaje;

    /// <summary>Lanza el mensaje de una tecla F (1 a 6). Devuelve si habia mensaje en esa tecla.</summary>
    public async Task<bool> PulsarTeclaAsync(int numero)
    {
        var mensaje = Mensajes.FirstOrDefault(m => m.Numero == numero);
        if (mensaje is not { Grabado: true }) return false;
        await EmitirAsync(mensaje).ConfigureAwait(true);
        return true;
    }

    /// <summary>Saca un mensaje al aire, si pasa todas las puertas.</summary>
    [RelayCommand]
    public async Task EmitirAsync(MensajeEnPantalla? mensaje)
    {
        if (mensaje is not { Audio: { } audio, Grabado: true }) return;

        if (_control.Transmitiendo)
        {
            Aviso = Textos.T("Cabina.Fonia.Voz.YaEnElAire");
            return;
        }

        if (!PermitirTransmitir)
        {
            Aviso = Textos.T("Cabina.TxCw.PestilloCerrado");
            return;
        }

        _fonia.Refrescar();
        if (!_fonia.PuedeTransmitir)
        {
            Aviso = _fonia.MotivoDeNoPoder;
            return;
        }

        if (_ajustes.Digital.PedirConfirmacionAlTransmitir
            && (ConfirmarQueVaATransmitir is not { } confirmar
                || !confirmar(Textos.F("Cabina.Fonia.Voz.Confirmar", mensaje.Tecla, mensaje.Nombre, mensaje.Duracion))))
        {
            Aviso = Textos.T("Cabina.TxCw.NoConfirmado");
            return;
        }

        try
        {
            Aviso = string.Empty;
            await _control.EmitirMensajeAsync(audio, _fonia.Ajustes.Microfono!.Id, _fonia.Ajustes.SalidaAlEquipo!.Id).ConfigureAwait(true);
            Log.Information("Mensaje de voz {Tecla} «{Nombre}» en el aire.", mensaje.Tecla, mensaje.Nombre);
        }
        catch (Exception fallo)
        {
            Log.Warning(fallo, "No se ha podido emitir el mensaje de voz.");
            Aviso = Textos.F("Cabina.Fonia.NoTransmite", fallo.Message);
        }
        finally
        {
            _fonia.Refrescar();
            OnPropertyChanged(nameof(Enviando));
        }
    }

    /// <summary>Corta el mensaje que este saliendo y lo que este sonando o grabando en local.</summary>
    [RelayCommand]
    public async Task PararAsync()
    {
        _reproductor.Parar();
        if (Grabando) await TerminarDeGrabarAsync().ConfigureAwait(true);
        if (_control.EnviandoMensaje)
        {
            await _control.TerminarTransmisionAsync(MotivoDeSuelta.Cancelado).ConfigureAwait(true);
        }

        OnPropertyChanged(nameof(Enviando));
    }

    /// <summary>Graba (o deja de grabar) un mensaje con el microfono del PC, en local.</summary>
    [RelayCommand]
    public async Task GrabarAsync(MensajeEnPantalla? mensaje)
    {
        if (mensaje is null) return;

        if (Grabando)
        {
            var estaba = Mensajes.FirstOrDefault(m => m.GrabandoAhora);
            await TerminarDeGrabarAsync().ConfigureAwait(true);
            if (ReferenceEquals(estaba, mensaje)) return;
        }

        if (_control.Transmitiendo)
        {
            Aviso = Textos.T("Cabina.Fonia.Voz.NoGrabarEnElAire");
            return;
        }

        if (_fonia.Ajustes.Microfono is not { } micro)
        {
            Aviso = Textos.T("Cabina.Fonia.ElijaTransmision");
            return;
        }

        try
        {
            _reproductor.Parar();
            await _grabador.EmpezarAsync(micro.Id).ConfigureAwait(true);
            mensaje.GrabandoAhora = true;
            Grabando = true;
            Aviso = Textos.F("Cabina.Fonia.Voz.Grabando", mensaje.Tecla);
        }
        catch (Exception fallo)
        {
            Log.Warning(fallo, "No se ha podido grabar del micrófono.");
            Aviso = Textos.F("Cabina.Fonia.Voz.NoGraba", fallo.Message);
        }
    }

    /// <summary>Suena un mensaje por los altavoces del PC para revisarlo.</summary>
    [RelayCommand]
    public async Task EscucharAsync(MensajeEnPantalla? mensaje)
    {
        if (mensaje is not { Audio: { } audio, Grabado: true }) return;
        if (mensaje.SonandoAhora)
        {
            _reproductor.Parar();
            return;
        }

        if (_fonia.Ajustes.Altavoces is not { } altavoces)
        {
            Aviso = Textos.T("Cabina.Fonia.ElijaEscucha");
            return;
        }

        try
        {
            mensaje.SonandoAhora = true;
            await _reproductor.ReproducirAsync(audio, altavoces.Id).ConfigureAwait(true);
        }
        catch (Exception fallo)
        {
            Log.Warning(fallo, "No se ha podido escuchar el mensaje de voz.");
            Aviso = Textos.F("Cabina.Fonia.Voz.NoSuena", fallo.Message);
        }
        finally
        {
            mensaje.SonandoAhora = false;
        }
    }

    /// <summary>Borra la grabacion de una ranura.</summary>
    [RelayCommand]
    public void Borrar(MensajeEnPantalla? mensaje)
    {
        if (mensaje is null || !mensaje.Grabado) return;
        try
        {
            _almacen.GuardarAudio(mensaje.Numero, null);
            mensaje.PonerAudio(null);
            Aviso = string.Empty;
        }
        catch (Exception fallo) when (fallo is IOException or UnauthorizedAccessException)
        {
            Aviso = Textos.F("Cabina.Fonia.Voz.NoGuarda", fallo.Message);
        }
    }

    /// <summary>«No volver a preguntar» del dialogo: el mismo ajuste que el del modem y la CW.</summary>
    public void NoVolverAPreguntarAlTransmitir()
    {
        _ajustes.Digital.PedirConfirmacionAlTransmitir = false;
        Log.Warning("El operador ha pedido no volver a confirmar las transmisiones (mensajes de voz).");
        _modem?.NoVolverAPreguntarAlTransmitir();
    }

    /// <summary>Medidor de la grabacion; lo llama el refresco del panel.</summary>
    public void Refrescar()
    {
        NivelDeGrabacion = Grabando ? _grabador.Nivel : 0;
        OnPropertyChanged(nameof(Enviando));
    }

    private async Task TerminarDeGrabarAsync()
    {
        var mensaje = Mensajes.FirstOrDefault(m => m.GrabandoAhora);
        AudioEnMemoria? audio = null;
        try
        {
            audio = await _grabador.PararAsync().ConfigureAwait(true);
        }
        catch (Exception fallo)
        {
            Log.Warning(fallo, "Fallo al parar la grabación del micrófono.");
        }
        finally
        {
            Grabando = false;
            NivelDeGrabacion = 0;
            if (mensaje is not null) mensaje.GrabandoAhora = false;
        }

        if (mensaje is null) return;
        audio = Recortar(audio);
        if (audio is null || audio.Duracion < TimeSpan.FromMilliseconds(300))
        {
            Aviso = Textos.T("Cabina.Fonia.Voz.Corto");
            return;
        }

        try
        {
            _almacen.GuardarAudio(mensaje.Numero, audio);
            mensaje.PonerAudio(audio);
            Aviso = Textos.F("Cabina.Fonia.Voz.Guardado", mensaje.Tecla, mensaje.Duracion);
        }
        catch (Exception fallo) when (fallo is IOException or UnauthorizedAccessException)
        {
            Aviso = Textos.F("Cabina.Fonia.Voz.NoGuarda", fallo.Message);
        }
    }

    /// <summary>Quita el silencio del principio y del final, con 150 ms de margen.</summary>
    internal static AudioEnMemoria? Recortar(AudioEnMemoria? audio)
    {
        if (audio is null || audio.Muestras.Length == 0) return null;
        const float Umbral = 0.01f; // -40 dBFS
        var m = audio.Muestras;
        var primero = Array.FindIndex(m, x => Math.Abs(x) > Umbral);
        if (primero < 0) return null;
        var ultimo = Array.FindLastIndex(m, x => Math.Abs(x) > Umbral);
        var margen = (int)(0.150 * audio.Frecuencia);
        var desde = Math.Max(0, primero - margen);
        var hasta = Math.Min(m.Length - 1, ultimo + margen);
        return new AudioEnMemoria(m[desde..(hasta + 1)], audio.Frecuencia);
    }

    private void Cargar()
    {
        _cargando = true;
        try
        {
            IReadOnlyList<MensajeDeVoz> guardados;
            try
            {
                guardados = _almacen.Leer(PorOmision);
            }
            catch (Exception fallo) when (fallo is IOException or UnauthorizedAccessException)
            {
                Log.Warning(fallo, "No se han podido leer los mensajes de voz.");
                guardados = Enumerable.Range(1, AlmacenDeMensajesDeVoz.Ranuras).Select(n => new MensajeDeVoz(n, PorOmision(n), null)).ToList();
            }

            foreach (var g in guardados)
            {
                var m = new MensajeEnPantalla(g.Numero, g.Nombre, g.Audio);
                m.PropertyChanged += AlCambiarUnMensaje;
                Mensajes.Add(m);
            }
        }
        finally
        {
            _cargando = false;
        }
    }

    private static string PorOmision(int numero) => numero switch
    {
        1 => "CQ",
        2 => Textos.T("Cabina.Fonia.Voz.Indicativo"),
        3 => Textos.T("Cabina.Fonia.Voz.Informe"),
        4 => Textos.T("Cabina.Fonia.Voz.Gracias"),
        5 => Textos.T("Cabina.Fonia.Voz.Repetir"),
        _ => "73",
    };

    private void AlCambiarUnMensaje(object? origen, PropertyChangedEventArgs e)
    {
        if (_cargando || e.PropertyName != nameof(MensajeEnPantalla.Nombre) || origen is not MensajeEnPantalla m) return;
        try
        {
            _almacen.GuardarNombre(m.Numero, m.Nombre);
        }
        catch (Exception fallo) when (fallo is IOException or UnauthorizedAccessException)
        {
            Aviso = Textos.F("Cabina.Fonia.Voz.NoGuarda", fallo.Message);
        }
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

    partial void OnPermitirTransmitirChanged(bool value)
    {
        // Cerrar el pestillo corta el mensaje que este saliendo.
        if (!value && _control.EnviandoMensaje) _ = PararAsync();

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
}
