using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Servicios.Correo;
using Nodisla.Cuaderno.Ui.Qsl;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Ajustes del correo con el que se mandan las QSL: servidor, cuenta, remitente, asunto y texto.
/// </summary>
/// <remarks>
/// Todo se guarda al momento en <c>qsl\correo.json</c>, salvo la contraseña, que va al almacen
/// cifrado con «Guardar» y no se vuelve a enseñar.
/// </remarks>
public sealed partial class VistaModeloCorreoQsl : ObservableObject
{
    private readonly ServicioDeQsl _servicio;

    /// <summary>Crea el modelo.</summary>
    /// <param name="servicio">Las QSL.</param>
    public VistaModeloCorreoQsl(ServicioDeQsl servicio)
    {
        _servicio = servicio ?? throw new ArgumentNullException(nameof(servicio));

        // El estado de la contraseña se escribe en el idioma nuevo.
        Textos.AlCambiar(this, static vm =>
        {
            vm.OnPropertyChanged(nameof(EstadoDeLaContrasena));
            vm.OnPropertyChanged(nameof(Texto));
        });
    }

    /// <summary>Formas de cifrar.</summary>
    public IReadOnlyList<SeguridadSmtp> Seguridades { get; } = Enum.GetValues<SeguridadSmtp>();

    /// <summary>Formatos de la tarjeta adjunta.</summary>
    public IReadOnlyList<FormatoDeImagen> Formatos { get; } = Enum.GetValues<FormatoDeImagen>();

    /// <summary>Servidor.</summary>
    public string Servidor { get => _servicio.Ajustes.Smtp.Servidor; set => Poner(value?.Trim() ?? string.Empty, v => _servicio.Ajustes.Smtp.Servidor = v); }

    /// <summary>Puerto.</summary>
    public int Puerto { get => _servicio.Ajustes.Smtp.Puerto; set => Poner(Math.Clamp(value, 1, 65535), v => _servicio.Ajustes.Smtp.Puerto = v); }

    /// <summary>Cifrado.</summary>
    public SeguridadSmtp Seguridad
    {
        get => _servicio.Ajustes.Smtp.Seguridad;
        set
        {
            var anterior = _servicio.Ajustes.Smtp.Seguridad;
            Poner(value, v => _servicio.Ajustes.Smtp.Seguridad = v);

            // Al cambiar el cifrado se propone su puerto de siempre, si seguia el del otro.
            if (anterior != value && Puerto is 587 or 465 or 25)
            {
                Puerto = value switch { SeguridadSmtp.SslDirecto => 465, SeguridadSmtp.StartTls => 587, _ => 25 };
            }
        }
    }

    /// <summary>Usuario.</summary>
    public string Usuario { get => _servicio.Ajustes.Smtp.Usuario ?? string.Empty; set => Poner(value?.Trim() ?? string.Empty, v => _servicio.Ajustes.Smtp.Usuario = v.Length == 0 ? null : v); }

    /// <summary>Direccion del remitente.</summary>
    public string Remitente { get => _servicio.Ajustes.Smtp.Remitente; set => Poner(value?.Trim() ?? string.Empty, v => _servicio.Ajustes.Smtp.Remitente = v); }

    /// <summary>Nombre visible del remitente.</summary>
    public string NombreDelRemitente { get => _servicio.Ajustes.Smtp.NombreDelRemitente ?? string.Empty; set => Poner(value ?? string.Empty, v => _servicio.Ajustes.Smtp.NombreDelRemitente = v.Length == 0 ? null : v); }

    /// <summary>Asunto con variables.</summary>
    public string Asunto { get => _servicio.Ajustes.Asunto; set => Poner(value ?? string.Empty, v => _servicio.Ajustes.Asunto = v); }

    /// <summary>Texto con variables.</summary>
    /// <remarks>
    /// El de fábrica (el que el operador no ha tocado) se enseña en el idioma del programa, igual
    /// que en la ventana de envío, y se sigue guardando como el de fábrica para que siga al idioma.
    /// </remarks>
    public string Texto
    {
        get => _servicio.Ajustes.Texto == Ajustes.AjustesDeCorreoQsl.TextoPorOmision ? TextoDeFabricaTraducido : _servicio.Ajustes.Texto;
        set => Poner(
            value == TextoDeFabricaTraducido ? Ajustes.AjustesDeCorreoQsl.TextoPorOmision : value ?? string.Empty,
            v => _servicio.Ajustes.Texto = v);
    }

    private static string TextoDeFabricaTraducido => Textos.F("Qsl.Envio.TextoPorOmision");

    /// <summary>Formato de la tarjeta adjunta.</summary>
    public FormatoDeImagen Formato { get => _servicio.Ajustes.Formato; set => Poner(value, v => _servicio.Ajustes.Formato = v); }

    /// <summary>La contraseña tecleada, aun sin guardar. Nunca se escribe en disco ni en el registro.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarLaContrasenaCommand))]
    private string _contrasenaNueva = string.Empty;

    /// <summary>Lo ultimo que ha pasado.</summary>
    [ObservableProperty]
    private string _aviso = string.Empty;

    /// <summary>Se esta probando la conexion.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProbarCommand))]
    private bool _probando;

    /// <summary>Si hay contraseña guardada.</summary>
    public string EstadoDeLaContrasena => Textos.T(_servicio.HayContrasena ? "Ajustes.Secreto.Guardada" : "Ajustes.Secreto.SinGuardar");

    /// <summary>Hay contraseña guardada.</summary>
    public bool ContrasenaGuardada => _servicio.HayContrasena;

    /// <summary>Guarda cifrada la contraseña tecleada.</summary>
    [RelayCommand(CanExecute = nameof(HayContrasenaTecleada))]
    public void GuardarLaContrasena()
    {
        if (string.IsNullOrEmpty(ContrasenaNueva)) return;
        _servicio.GuardarContrasena(ContrasenaNueva);
        ContrasenaNueva = string.Empty;
        AvisarDeLaContrasena();
        Aviso = Textos.T("Ajustes.Correo.ContrasenaGuardada");
    }

    /// <summary>Borra la contraseña guardada.</summary>
    [RelayCommand(CanExecute = nameof(ContrasenaGuardada))]
    public void BorrarLaContrasena()
    {
        _servicio.BorrarContrasena();
        AvisarDeLaContrasena();
        Aviso = Textos.T("Ajustes.Correo.ContrasenaBorrada");
    }

    /// <summary>Conecta con el servidor y se identifica, sin mandar ningun correo.</summary>
    /// <returns>Tarea.</returns>
    [RelayCommand(CanExecute = nameof(SePuedeProbar))]
    public async Task ProbarAsync()
    {
        Probando = true;
        Aviso = Textos.T("Ajustes.Equipo.Probando");
        try
        {
            await _servicio.ProbarAsync().ConfigureAwait(true);
            Aviso = Textos.T("Ajustes.Correo.ConexionCorrecta");
        }
        catch (ErrorDeCorreo ex)
        {
            Aviso = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            Aviso = Textos.F("Ajustes.Correo.NoSeHaPodidoProbar", ex.Message);
        }
        finally
        {
            Probando = false;
        }
    }

    /// <summary>Vuelve a poner el asunto y el texto de omision.</summary>
    [RelayCommand]
    public void TextoPorOmision()
    {
        Asunto = Ajustes.AjustesDeCorreoQsl.AsuntoPorOmision;
        Texto = TextoDeFabricaTraducido;
        OnPropertyChanged(nameof(Asunto));
        OnPropertyChanged(nameof(Texto));
    }

    private bool HayContrasenaTecleada() => !string.IsNullOrEmpty(ContrasenaNueva);

    private bool SePuedeProbar() => !Probando;

    private void AvisarDeLaContrasena()
    {
        OnPropertyChanged(nameof(EstadoDeLaContrasena));
        OnPropertyChanged(nameof(ContrasenaGuardada));
        BorrarLaContrasenaCommand.NotifyCanExecuteChanged();
    }

    private void Poner<T>(T valor, Action<T> escribir, [System.Runtime.CompilerServices.CallerMemberName] string? propiedad = null)
    {
        escribir(valor);
        OnPropertyChanged(propiedad);
        try
        {
            _servicio.GuardarAjustes();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "No se han podido guardar los ajustes de correo de las QSL.");
            Aviso = Textos.F("Ajustes.NoSeHanPodidoGuardar", ex.Message);
        }
    }
}
