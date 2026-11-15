using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Subidas;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Con que color se dice algo en pantalla.</summary>
public enum TonoDeAviso
{
    /// <summary>No hay nada que decir.</summary>
    Ninguno,

    /// <summary>Se esta preguntando.</summary>
    Trabajando,

    /// <summary>Ha ido bien.</summary>
    Correcto,

    /// <summary>Funciona, pero conviene leerlo.</summary>
    Aviso,

    /// <summary>No funciona.</summary>
    Error,
}

/// <summary>
/// Todo lo de QRZ.com en un solo sitio de la configuracion, ordenado por lo que el operador
/// quiere hacer y no por la clase de dato que hay que escribir.
/// </summary>
/// <remarks>
/// <para>
/// QRZ.com son dos servicios con credenciales distintas: el libro en linea va con una clave de
/// API y la consulta de fichas con el usuario y la contrasena de la web. Antes las dos
/// casillas estaban juntas en una tarjeta («Contraseña de QRZ.com» y «Clave del cuaderno de
/// QRZ») y las casillas de activar en otro apartado: el operador no sabia donde iba su clave,
/// y en los registros quedaron decenas de «Username/password incorrect».
/// </para>
/// <para>
/// Aqui hay dos recuadros, uno por tarea. Cada uno tiene su dato, de donde se saca, su
/// casilla de activar y su boton de probar, que contesta con lo que QRZ dice de verdad.
/// </para>
/// <para>
/// Los secretos se guardan con las mismas claves de siempre en el mismo almacen cifrado, y
/// las casillas en los mismos campos de <c>ajustes.json</c>: lo que ya estaba guardado sigue
/// valiendo.
/// </para>
/// </remarks>
public sealed partial class VistaModeloQrz : ObservableObject
{
    private readonly VistaModeloSubidas _subidas;
    private readonly Func<CancellationToken, Task<PruebaDelCuadernoQrz>> _probarCuaderno;
    private readonly Func<CancellationToken, Task<PruebaDeLaFichaQrz>> _probarFicha;

    /// <summary>Monta el apartado.</summary>
    /// <param name="claveDeApi">El secreto de la clave de API del libro.</param>
    /// <param name="contrasena">El secreto de la contrasena de la web.</param>
    /// <param name="subidas">La subida automatica: casillas, usuario, cola y recuento.</param>
    /// <param name="probarCuaderno">Como se prueba la clave; nulo para lo que diga <paramref name="subidas"/>.</param>
    /// <param name="probarFicha">Como se prueba el acceso; nulo para lo que diga <paramref name="subidas"/>.</param>
    public VistaModeloQrz(
        SecretoDeServicio claveDeApi,
        SecretoDeServicio contrasena,
        VistaModeloSubidas subidas,
        Func<CancellationToken, Task<PruebaDelCuadernoQrz>>? probarCuaderno = null,
        Func<CancellationToken, Task<PruebaDeLaFichaQrz>>? probarFicha = null)
    {
        ClaveDeApi = claveDeApi ?? throw new ArgumentNullException(nameof(claveDeApi));
        Contrasena = contrasena ?? throw new ArgumentNullException(nameof(contrasena));
        _subidas = subidas ?? throw new ArgumentNullException(nameof(subidas));
        _probarCuaderno = probarCuaderno ?? subidas.ProbarCuadernoDeQrzAsync;
        _probarFicha = probarFicha ?? subidas.ProbarFichaDeQrzAsync;
        Subida = subidas.Servicios.Single(s => s.Medio == Dominio.Entidades.MedioDeConfirmacion.QrzCom);
        _usuario = subidas.UsuarioQrz;

        // El error tipico: pegar la clave de API en la casilla de la contrasena, o al reves.
        ClaveDeApi.Avisar = texto => FormatoDeClaveQrz.PareceClaveDeApi(texto)
            ? null
            : Textos.T("Ajustes.Qrz.Clave.AvisoForma");
        ClaveDeApi.Normalizar = texto => texto.Trim();
        Contrasena.Avisar = texto => FormatoDeClaveQrz.PareceClaveDeApi(texto)
            ? Textos.T("Ajustes.Qrz.Contrasena.AvisoForma")
            : null;

        ClaveDeApi.SecretoCambiado += (_, _) => AlCambiarLaClave();
        Contrasena.SecretoCambiado += (_, _) => AlCambiarLaContrasena();
        ClaveDeApi.PropertyChanged += (_, _) => AvisarDelEstado();
        Contrasena.PropertyChanged += (_, _) => AvisarDelEstado();
        Subida.PropertyChanged += (_, _) => AvisarDelEstado();
        subidas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(VistaModeloSubidas.CompletarConQrz) or nameof(VistaModeloSubidas.UsuarioPorOmision))
            {
                AvisarDelEstado();
            }
        };
        subidas.Pastillas[^1].PropertyChanged += (_, _) => AvisarDelEstado();

        Textos.AlCambiar(this, static vm => vm.OnPropertyChanged(string.Empty));
    }

    // ── Subir mis contactos ──────────────────────────────────────────────────

    /// <summary>La clave de API del libro.</summary>
    public SecretoDeServicio ClaveDeApi { get; }

    /// <summary>La subida a QRZ: su casilla, su estado, el recuento y los contactos anteriores.</summary>
    public FilaDeServicioDeSubida Subida { get; }

    /// <summary>Como esta la subida, para el color de la chapa.</summary>
    public EstadoDeServicio EstadoDeLaSubida =>
        !ClaveDeApi.Guardado ? EstadoDeServicio.SinConfigurar
        : Subida.Activado && Subida.Semaforo != SemaforoDeSubida.Rojo ? EstadoDeServicio.Configurado
        : EstadoDeServicio.AMedias;

    /// <summary>Como esta la subida, en dos palabras.</summary>
    public string EstadoDeLaSubidaTexto => Textos.T(
        !ClaveDeApi.Guardado ? "Ajustes.Qrz.Estado.SinClave"
        : !Subida.Activado ? "Ajustes.Qrz.Estado.SinActivar"
        : Subida.Semaforo == SemaforoDeSubida.Rojo ? "Ajustes.Qrz.Estado.ConProblemas"
        : "Ajustes.Qrz.Estado.Activada");

    /// <summary>Lo que ha dicho QRZ al probar la clave.</summary>
    [ObservableProperty]
    private string _pruebaDeLaClave = string.Empty;

    /// <summary>Con que color se dice.</summary>
    [ObservableProperty]
    private TonoDeAviso _tonoDeLaClave;

    /// <summary>Se esta preguntando a QRZ por la clave.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProbarLaClaveCommand))]
    private bool _probandoLaClave;

    /// <summary>Pregunta a QRZ si la clave vale y de que libro es.</summary>
    [RelayCommand(CanExecute = nameof(PuedeProbarLaClave))]
    public async Task ProbarLaClaveAsync()
    {
        // Lo tecleado y sin guardar se guarda antes: se prueba lo que se ve en la casilla.
        if (ClaveDeApi.GuardarCommand.CanExecute(null)) ClaveDeApi.Guardar();

        ProbandoLaClave = true;
        TonoDeLaClave = TonoDeAviso.Trabajando;
        PruebaDeLaClave = Textos.T("Ajustes.Qrz.Probando");
        try
        {
            var prueba = await _probarCuaderno(CancellationToken.None).ConfigureAwait(true);
            TonoDeLaClave = Tono(prueba.Veredicto);
            PruebaDeLaClave = prueba.Mensaje;
            if (prueba.Veredicto != VeredictoDeQrz.Correcto) return;

            // La clave vale: lo que estuviera parado puede salir ya.
            _subidas.ReanudarQrz();
            await _subidas.RecuentoEnCurso.ConfigureAwait(true);
            if (!Subida.Activado)
            {
                TonoDeLaClave = TonoDeAviso.Aviso;
                PruebaDeLaClave += " " + Textos.T("Ajustes.Qrz.Subir.ProbadaSinActivar");
            }
            else if (Subida.SinSubir > 0)
            {
                PruebaDeLaClave += " " + Textos.F("Ajustes.Qrz.Subir.HaySinSubir", Subida.SinSubir);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falló la prueba de la clave de API de QRZ.");
            TonoDeLaClave = TonoDeAviso.Error;
            PruebaDeLaClave = ex.Message;
        }
        finally
        {
            ProbandoLaClave = false;
        }
    }

    private bool PuedeProbarLaClave() => !ProbandoLaClave;

    // ── Rellenar la ficha ────────────────────────────────────────────────────

    /// <summary>La contrasena de la web de QRZ.com.</summary>
    public SecretoDeServicio Contrasena { get; }

    /// <summary>El usuario de la web de QRZ.com. Vacio: el indicativo del perfil.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarElUsuarioCommand))]
    private string _usuario;

    /// <summary>Lo que vale un usuario vacio.</summary>
    public string UsuarioPorOmision => _subidas.UsuarioPorOmision;

    /// <summary>Rellenar los contactos con la ficha de QRZ.</summary>
    public bool Completar
    {
        get => _subidas.CompletarConQrz;
        set
        {
            if (_subidas.CompletarConQrz == value) return;
            _subidas.CompletarConQrz = value;
            AvisarDelEstado();
        }
    }

    /// <summary>Como esta la consulta de fichas, para el color de la chapa.</summary>
    public EstadoDeServicio EstadoDeLaFicha =>
        !Contrasena.Guardado ? EstadoDeServicio.SinConfigurar
        : Completar && _subidas.Pastillas[^1].Semaforo != SemaforoDeSubida.Ambar ? EstadoDeServicio.Configurado
        : EstadoDeServicio.AMedias;

    /// <summary>Como esta la consulta de fichas, en dos palabras.</summary>
    public string EstadoDeLaFichaTexto => Textos.T(
        !Contrasena.Guardado ? "Ajustes.Qrz.Estado.SinContrasena"
        : !Completar ? "Ajustes.Qrz.Estado.SinActivar"
        : _subidas.Pastillas[^1].Semaforo == SemaforoDeSubida.Ambar ? "Ajustes.Qrz.Estado.ConProblemas"
        : "Ajustes.Qrz.Estado.Activada");

    /// <summary>El ultimo problema de la consulta de fichas mientras se opera, o vacio.</summary>
    public string ProblemaDeLaFicha =>
        Completar && Contrasena.Guardado && _subidas.Pastillas[^1].Semaforo == SemaforoDeSubida.Ambar
            ? _subidas.Pastillas[^1].Texto
            : string.Empty;

    /// <summary>Lo que ha dicho QRZ al probar el usuario y la contrasena.</summary>
    [ObservableProperty]
    private string _pruebaDelAcceso = string.Empty;

    /// <summary>Con que color se dice.</summary>
    [ObservableProperty]
    private TonoDeAviso _tonoDelAcceso;

    /// <summary>Se esta preguntando a QRZ por el acceso.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ProbarElAccesoCommand))]
    private bool _probandoElAcceso;

    /// <summary>Guarda el usuario escrito. Vale ya, sin reiniciar.</summary>
    [RelayCommand(CanExecute = nameof(ElUsuarioHaCambiado))]
    public void GuardarElUsuario()
    {
        _subidas.UsuarioQrz = Usuario;
        _subidas.GuardarCuentas();
        Usuario = _subidas.UsuarioQrz;
        GuardarElUsuarioCommand.NotifyCanExecuteChanged();
        AlCambiarLaContrasena();
    }

    private bool ElUsuarioHaCambiado() =>
        !string.Equals((Usuario ?? string.Empty).Trim(), (_subidas.UsuarioQrz ?? string.Empty).Trim(), StringComparison.Ordinal);

    /// <summary>Abre una sesion en QRZ con el usuario y la contrasena, sin consultar a nadie.</summary>
    [RelayCommand(CanExecute = nameof(PuedeProbarElAcceso))]
    public async Task ProbarElAccesoAsync()
    {
        if (ElUsuarioHaCambiado()) GuardarElUsuario();
        if (Contrasena.GuardarCommand.CanExecute(null)) Contrasena.Guardar();

        ProbandoElAcceso = true;
        TonoDelAcceso = TonoDeAviso.Trabajando;
        PruebaDelAcceso = Textos.T("Ajustes.Qrz.Probando");
        try
        {
            var prueba = await _probarFicha(CancellationToken.None).ConfigureAwait(true);
            TonoDelAcceso = Tono(prueba.Veredicto);
            PruebaDelAcceso = prueba.Mensaje;
            if (prueba.Veredicto != VeredictoDeQrz.Correcto) return;

            if (prueba.Aviso is { Length: > 0 } aviso)
            {
                TonoDelAcceso = TonoDeAviso.Aviso;
                PruebaDelAcceso += " " + Textos.F("Ajustes.Qrz.Ficha.AvisoDeQrz", aviso);
            }
            if (!Completar)
            {
                TonoDelAcceso = TonoDeAviso.Aviso;
                PruebaDelAcceso += " " + Textos.T("Ajustes.Qrz.Ficha.ProbadaSinActivar");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Falló la prueba del acceso a QRZ.com.");
            TonoDelAcceso = TonoDeAviso.Error;
            PruebaDelAcceso = ex.Message;
        }
        finally
        {
            ProbandoElAcceso = false;
        }
    }

    private bool PuedeProbarElAcceso() => !ProbandoElAcceso;

    private void AlCambiarLaClave()
    {
        // Clave nueva (o borrada): lo que dijo la prueba anterior ya no vale, y la cola puede
        // volver a intentarlo con la que hay ahora.
        TonoDeLaClave = ClaveDeApi.Guardado ? TonoDeAviso.Aviso : TonoDeAviso.Ninguno;
        PruebaDeLaClave = ClaveDeApi.Guardado ? Textos.T("Ajustes.Qrz.Clave.GuardadaSinProbar") : string.Empty;
        _subidas.ReanudarQrz();
    }

    private void AlCambiarLaContrasena()
    {
        TonoDelAcceso = Contrasena.Guardado ? TonoDeAviso.Aviso : TonoDeAviso.Ninguno;
        PruebaDelAcceso = Contrasena.Guardado ? Textos.T("Ajustes.Qrz.Contrasena.GuardadaSinProbar") : string.Empty;
        _subidas.Refrescar();
    }

    private void AvisarDelEstado()
    {
        OnPropertyChanged(nameof(EstadoDeLaSubida));
        OnPropertyChanged(nameof(EstadoDeLaSubidaTexto));
        OnPropertyChanged(nameof(EstadoDeLaFicha));
        OnPropertyChanged(nameof(EstadoDeLaFichaTexto));
        OnPropertyChanged(nameof(ProblemaDeLaFicha));
        OnPropertyChanged(nameof(Completar));
        OnPropertyChanged(nameof(UsuarioPorOmision));
    }

    private static TonoDeAviso Tono(VeredictoDeQrz veredicto) => veredicto switch
    {
        VeredictoDeQrz.Correcto => TonoDeAviso.Correcto,
        VeredictoDeQrz.SinRed or VeredictoDeQrz.SinDato => TonoDeAviso.Aviso,
        _ => TonoDeAviso.Error,
    };
}
