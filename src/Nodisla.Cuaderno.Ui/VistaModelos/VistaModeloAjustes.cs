using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Un secreto de un servicio, tal y como se maneja en la pantalla de ajustes.
/// </summary>
/// <remarks>
/// <b>Lo escrito no se vuelve a ensenar.</b> El almacen cifra con la proteccion de datos del
/// usuario de Windows y solo sabe decir si hay algo guardado, no que es. La pantalla ensena
/// «guardada» o «sin guardar» y el campo siempre sale vacio: un secreto que se vuelve a pintar
/// en pantalla es un secreto que alguien puede leer por encima del hombro.
/// </remarks>
public sealed partial class SecretoDeServicio : ObservableObject
{
    private readonly IAlmacenDeCredenciales _almacen;
    private readonly string _titulo;
    private readonly string _explicacion;

    /// <summary>Monta la fila del secreto.</summary>
    /// <param name="almacen">Almacen cifrado.</param>
    /// <param name="clave">Clave con la que se guarda.</param>
    /// <param name="servicio">Servicio al que pertenece.</param>
    /// <param name="titulo">Como se llama en pantalla (texto o clave de los recursos).</param>
    /// <param name="explicacion">Que es, para el operador (texto o clave de los recursos).</param>
    public SecretoDeServicio(
        IAlmacenDeCredenciales almacen,
        string clave,
        string servicio,
        string titulo,
        string explicacion)
    {
        _almacen = almacen ?? throw new ArgumentNullException(nameof(almacen));
        Clave = clave;
        Servicio = servicio;
        _titulo = titulo;
        _explicacion = explicacion;

        Refrescar();

        Textos.AlCambiar(this, static s =>
        {
            s.OnPropertyChanged(nameof(Titulo));
            s.OnPropertyChanged(nameof(Explicacion));
            s.OnPropertyChanged(nameof(Estado));
        });
    }

    /// <summary>Clave con la que se guarda.</summary>
    public string Clave { get; }

    /// <summary>Servicio al que pertenece.</summary>
    public string Servicio { get; }

    /// <summary>Como se llama en pantalla.</summary>
    public string Titulo => TextoOClave.Resolver(_titulo);

    /// <summary>Que es, para el operador.</summary>
    public string Explicacion => TextoOClave.Resolver(_explicacion);

    /// <summary>Lo que el operador acaba de teclear. Se vacia al guardar.</summary>
    /// <remarks>
    /// La casilla de la pantalla escucha este valor: cuando se vacia al guardar o al borrar,
    /// se vacian tambien sus puntos. Antes los puntos se quedaban y parecia que no se habia
    /// guardado nada.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nuevo = string.Empty;

    /// <summary>Hay un secreto guardado con esa clave.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Estado))]
    [NotifyCanExecuteChangedFor(nameof(BorrarCommand))]
    private bool _guardado;

    /// <summary>Estado, escrito para el operador.</summary>
    public string Estado => Textos.T(Guardado ? "Ajustes.Secreto.Guardada" : "Ajustes.Secreto.SinGuardar");

    /// <summary>Guarda lo tecleado y vacia el campo.</summary>
    [RelayCommand(CanExecute = nameof(HayAlgoTecleado))]
    public void Guardar()
    {
        if (string.IsNullOrWhiteSpace(Nuevo)) return;

        _almacen.Guardar(Clave, Nuevo);
        Nuevo = string.Empty;
        Refrescar();
    }

    /// <summary>Borra el secreto guardado.</summary>
    [RelayCommand(CanExecute = nameof(Guardado))]
    public void Borrar()
    {
        _almacen.Borrar(Clave);
        Nuevo = string.Empty;
        Refrescar();
    }

    private bool HayAlgoTecleado() => !string.IsNullOrWhiteSpace(Nuevo);

    private void Refrescar() => Guardado = _almacen.Existe(Clave);
}

/// <summary>
/// La pantalla de ajustes: credenciales, LoTW, equipo y cuaderno.
/// </summary>
/// <remarks>
/// <para>
/// Aqui va lo que se toca una vez y se olvida, y lo que hace falta para diagnosticar. Lo que
/// se mira operando —el estado de las conexiones— se queda en la pantalla de operar.
/// </para>
/// <para>
/// Las credenciales <b>se guardan cifradas y no se vuelven a ensenar</b>. El
/// <c>config.ini</c> del programa original guarda la clave de API de Club Log en claro; aqui
/// el almacen solo sabe decir si hay algo guardado.
/// </para>
/// </remarks>
public sealed partial class VistaModeloAjustes : ObservableObject
{
    private readonly ImportarAdif _importar;
    private readonly IRepositorioQso _cuaderno;
    private readonly Func<string?> _motivoDeNoPoderSubir;
    private readonly IEscritorAdif? _escritor;

    /// <summary>El aviso de la frase de paso tal y como llegó: el texto o, mejor, su clave, que sigue al idioma.</summary>
    private readonly string _avisoDeLaFraseDePaso;

    /// <summary>Contactos que se piden de una vez al exportar.</summary>
    private const int PaginaDeExportacion = 1000;

    /// <summary>Monta la pantalla.</summary>
    /// <param name="credenciales">Almacen cifrado de secretos.</param>
    /// <param name="importar">Caso de uso de importacion de ADIF.</param>
    /// <param name="cuaderno">Repositorio, para las cifras del cuaderno.</param>
    /// <param name="avisoDeLaFraseDePaso">
    /// Aviso que hay que ensenar <b>antes</b> de pedir la frase de paso del certificado.
    /// </param>
    /// <param name="motivoDeNoPoderSubir">
    /// Por que no se puede subir a LoTW ahora mismo, o nulo si si se puede.
    /// </param>
    /// <param name="cat">
    /// El apartado del control del equipo. Nulo con los puertos simulados, donde no hay equipo
    /// de verdad que configurar.
    /// </param>
    /// <param name="cluster">El apartado de la conexion al cluster. Nulo con los simulados.</param>
    /// <param name="audio">
    /// El apartado de audio y modos digitales: por donde entra el sonido y quien decodifica.
    /// </param>
    /// <param name="escritor">
    /// Escritor de ADIF para exportar el cuaderno. Sin el, el boton sale apagado y lo dice.
    /// </param>
    public VistaModeloAjustes(
        IAlmacenDeCredenciales credenciales,
        ImportarAdif importar,
        IRepositorioQso cuaderno,
        string avisoDeLaFraseDePaso,
        Func<string?> motivoDeNoPoderSubir,
        VistaModeloAjustesCat? cat = null,
        VistaModeloAjustesCluster? cluster = null,
        VistaModeloAjustesAudio? audio = null,
        IEscritorAdif? escritor = null)
    {
        _escritor = escritor;
        Cat = cat;
        Cluster = cluster;
        Audio = audio;

        ArgumentNullException.ThrowIfNull(credenciales);

        _importar = importar ?? throw new ArgumentNullException(nameof(importar));
        _cuaderno = cuaderno ?? throw new ArgumentNullException(nameof(cuaderno));
        _motivoDeNoPoderSubir = motivoDeNoPoderSubir ?? throw new ArgumentNullException(nameof(motivoDeNoPoderSubir));

        _avisoDeLaFraseDePaso = avisoDeLaFraseDePaso;

        Secretos =
        [
            new(credenciales, ClavesDeCredencial.LotwContrasena, "LoTW", "Ajustes.Secreto.LotwContrasena",
                "Ajustes.Secreto.LotwContrasena.Ayuda"),
            new(credenciales, ClavesDeCredencial.TqslFraseDePaso, "LoTW", "Ajustes.Secreto.TqslFrase",
                "Ajustes.Secreto.TqslFrase.Ayuda"),
            new(credenciales, ClavesDeCredencial.EqslContrasena, "eQSL", "Ajustes.Secreto.EqslContrasena",
                "Ajustes.Secreto.EqslContrasena.Ayuda"),
            new(credenciales, ClavesDeCredencial.ClubLogContrasena, "Club Log", "Ajustes.Secreto.ClubLogContrasena",
                "Ajustes.Secreto.ClubLogContrasena.Ayuda"),
            new(credenciales, ClavesDeCredencial.ClubLogApi, "Club Log", "Ajustes.Secreto.ClubLogApi",
                "Ajustes.Secreto.ClubLogApi.Ayuda"),
            new(credenciales, ClavesDeCredencial.QrzContrasena, "QRZ.com", "Ajustes.Secreto.QrzContrasena",
                "Ajustes.Secreto.QrzContrasena.Ayuda"),
            new(credenciales, ClavesDeCredencial.QrzClaveDeCuaderno, "QRZ.com", "Ajustes.Secreto.QrzClaveDeCuaderno",
                "Ajustes.Secreto.QrzClaveDeCuaderno.Ayuda"),
            new(credenciales, ClavesDeCredencial.HamQthContrasena, "HamQTH", "Ajustes.Secreto.HamQthContrasena",
                "Ajustes.Secreto.HamQthContrasena.Ayuda"),
        ];

        // El aviso de la frase de paso sigue al idioma.
        Textos.AlCambiar(this, static vm => vm.OnPropertyChanged(nameof(AvisoDeLaFraseDePaso)));

        RefrescarLotw();

        // Para capturar cada apartado sin tocar la ventana del operador.
        if (Environment.GetEnvironmentVariable("CUADERNO_APARTADO") is { Length: > 0 } apartado
            && int.TryParse(apartado, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cual))
        {
            IndiceDelApartado = Math.Clamp(cual, 0, Apartados.Count - 1);
        }
    }

    /// <summary>Las claves de los nombres de los apartados, en el orden de la columna de la izquierda.</summary>
    public static IReadOnlyList<string> ClavesDeLosApartados { get; } =
    [
        "Ajustes.Apartado.Cuentas", "Ajustes.Apartado.Subidas", "Ajustes.Apartado.Equipo", "Ajustes.Apartado.Audio",
        "Ajustes.Apartado.Fonia", "Ajustes.Apartado.Cluster", "Ajustes.Apartado.Correo", "Ajustes.Apartado.Libro",
        "Ajustes.Apartado.Actualizaciones", "Ajustes.Apartado.Idioma",
    ];

    /// <summary>Los apartados de la configuración, en el idioma en uso y en el orden de la columna de la izquierda.</summary>
    public static IReadOnlyList<string> Apartados => ClavesDeLosApartados.Select(Textos.T).ToList();

    /// <summary>Índices de los apartados.</summary>
    public const int ApartadoCuentas = 0, ApartadoSubidas = 1, ApartadoEquipo = 2, ApartadoAudio = 3,
        ApartadoFonia = 4, ApartadoCluster = 5, ApartadoCorreo = 6, ApartadoLibro = 7, ApartadoActualizaciones = 8, ApartadoIdioma = 9;

    /// <summary>Apartado que se está viendo.</summary>
    [ObservableProperty]
    private int _indiceDelApartado;

    private IReadOnlyList<TarjetaDeServicio>? _tarjetas;

    /// <summary>
    /// Una tarjeta por servicio con su cuenta, sus secretos y su estado.
    /// </summary>
    /// <remarks>
    /// Se monta la primera vez que se pide y no en el constructor: las subidas, el correo y la
    /// fonía llegan después, por inicializador.
    /// </remarks>
    public IReadOnlyList<TarjetaDeServicio> Tarjetas => _tarjetas ??= MontarTarjetas();

    private IReadOnlyList<TarjetaDeServicio> MontarTarjetas()
    {
        SecretoDeServicio S(string clave) => Secretos.Single(s => s.Clave == clave);
        var subidas = Subidas;
        var guardarCuenta = subidas?.GuardarCuentasCommand;

        IReadOnlyList<CampoDeCuenta> Campos(params (string Rotulo, Func<VistaModeloSubidas, string> Leer, Action<VistaModeloSubidas, string> Escribir, Func<string?>? Nota)[] campos) =>
            subidas is null
                ? []
                : campos.Select(c => new CampoDeCuenta(c.Rotulo, () => c.Leer(subidas), v => c.Escribir(subidas, v), c.Nota)).ToList();

        Func<string?> Nota(string clave) => () => clave;

        var qrz = new[] { S(ClavesDeCredencial.QrzContrasena), S(ClavesDeCredencial.QrzClaveDeCuaderno) };
        var lotw = new[] { S(ClavesDeCredencial.LotwContrasena), S(ClavesDeCredencial.TqslFraseDePaso) };
        var eqsl = new[] { S(ClavesDeCredencial.EqslContrasena) };
        var clubLog = new[] { S(ClavesDeCredencial.ClubLogContrasena), S(ClavesDeCredencial.ClubLogApi) };
        var hamQth = new[] { S(ClavesDeCredencial.HamQthContrasena) };
        // El texto de «usuario vacío» lo da el apartado de subidas con el indicativo del perfil;
        // se pide cada vez que se enseña, para que siga al perfil y al idioma.
        Func<string?> usuarioVacio = () => subidas?.UsuarioPorOmision ?? Textos.T("Ajustes.Cuenta.UsuarioVacio");

        var tarjetas = new List<TarjetaDeServicio>
        {
            new("QRZ.com", "Q", "Ajustes.Tarjeta.Qrz.Descripcion",
                () => TarjetaDeServicio.PorSecretos([qrz[0]], qrz), qrz,
                Campos(("Ajustes.Cuenta.Usuario", s => s.UsuarioQrz, (s, v) => s.UsuarioQrz = v, usuarioVacio)))
            { GuardarCuenta = guardarCuenta },

            new("LoTW", "L", "Ajustes.Tarjeta.Lotw.Descripcion",
                () =>
                {
                    if (SePuedeSubirALotw) return (EstadoDeServicio.Configurado, Textos.T("Ajustes.Estado.ListaParaSubir"));
                    var (estado, texto) = TarjetaDeServicio.PorSecretos([lotw[0]], lotw);

                    // Con la contraseña puesta pero sin poder firmar, lo que falta es TQSL.
                    return estado == EstadoDeServicio.Configurado ? (EstadoDeServicio.AMedias, Textos.T("Ajustes.Estado.FaltaTqsl")) : (estado, texto);
                },
                lotw,
                Campos(
                    ("Ajustes.Cuenta.Usuario", s => s.UsuarioLotw, (s, v) => s.UsuarioLotw = v, usuarioVacio),
                    ("Ajustes.Cuenta.UbicacionTqsl", s => s.UbicacionTqsl, (s, v) => s.UbicacionTqsl = v, Nota("Ajustes.Cuenta.UbicacionTqsl.Nota")),
                    ("Ajustes.Cuenta.RutaTqsl", s => s.RutaTqsl, (s, v) => s.RutaTqsl = v, Nota("Ajustes.Cuenta.RutaTqsl.Nota"))),
                () => MotivoDeNoPoderSubirALotw,
                this)
            {
                GuardarCuenta = guardarCuenta,
                Probar = RefrescarCommand,
                TextoDeProbar = "Ajustes.Tarjeta.VolverAComprobar",
                Nota = _avisoDeLaFraseDePaso,
            },

            new("eQSL.cc", "E", "Ajustes.Tarjeta.Eqsl.Descripcion",
                () => TarjetaDeServicio.PorSecretos(eqsl, eqsl), eqsl,
                Campos(
                    ("Ajustes.Cuenta.Usuario", s => s.UsuarioEqsl, (s, v) => s.UsuarioEqsl = v, usuarioVacio),
                    ("Ajustes.Cuenta.ApodoQth", s => s.ApodoEqsl, (s, v) => s.ApodoEqsl = v, Nota("Ajustes.Cuenta.ApodoQth.Nota"))))
            { GuardarCuenta = guardarCuenta },

            new("Club Log", "C", "Ajustes.Tarjeta.ClubLog.Descripcion",
                () => TarjetaDeServicio.PorSecretos(clubLog, clubLog), clubLog,
                Campos(
                    ("Ajustes.Cuenta.CorreoClubLog", s => s.CorreoClubLog, (s, v) => s.CorreoClubLog = v, null),
                    ("Ajustes.Cuenta.IndicativoClubLog", s => s.IndicativoClubLog, (s, v) => s.IndicativoClubLog = v, null)))
            { GuardarCuenta = guardarCuenta },

            new("HamQTH", "H", "Ajustes.Tarjeta.HamQth.Descripcion",
                () => TarjetaDeServicio.PorSecretos(hamQth, hamQth), hamQth,
                Campos(("Ajustes.Cuenta.Usuario", s => s.UsuarioHamQth, (s, v) => s.UsuarioHamQth = v, usuarioVacio)))
            { GuardarCuenta = guardarCuenta },
        };

        tarjetas.Add(new TarjetaDeServicio("Ajustes.Tarjeta.Cluster.Nombre", "D", "Ajustes.Tarjeta.Cluster.Descripcion",
            () => Cluster is null
                ? (EstadoDeServicio.SinConfigurar, Textos.T("Ajustes.Estado.SinNodoDeVerdad"))
                : string.IsNullOrWhiteSpace(Cluster.Servidor)
                    ? (EstadoDeServicio.SinConfigurar, Textos.T("Ajustes.Estado.SinNodo"))
                    : (EstadoDeServicio.Configurado, Textos.T(Cluster.ContrasenaGuardada ? "Ajustes.Estado.ConfiguradoConContrasena" : "Ajustes.Estado.Configurado")),
            aviso: () => Cluster is null
                ? Textos.T("Ajustes.Tarjeta.Cluster.Simulado")
                : string.IsNullOrWhiteSpace(Cluster.Servidor)
                    ? string.Empty
                    : Textos.F("Ajustes.Tarjeta.Cluster.Aviso", Cluster.Nombre, Cluster.Servidor, Cluster.Puerto, Cluster.IndicativoDeAcceso),
            origenes: Cluster)
        {
            Configurar = new RelayCommand(() => IndiceDelApartado = ApartadoCluster),
        });

        if (CorreoQsl is { } correo)
        {
            tarjetas.Add(new TarjetaDeServicio("Ajustes.Tarjeta.Correo.Nombre", "@", "Ajustes.Tarjeta.Correo.Descripcion",
                () => string.IsNullOrWhiteSpace(correo.Servidor)
                    ? (correo.ContrasenaGuardada ? EstadoDeServicio.AMedias : EstadoDeServicio.SinConfigurar,
                       Textos.T(correo.ContrasenaGuardada ? "Ajustes.Estado.Incompleta" : "Ajustes.Estado.SinConfigurar"))
                    : correo.ContrasenaGuardada || string.IsNullOrWhiteSpace(correo.Usuario)
                        ? (EstadoDeServicio.Configurado, Textos.T("Ajustes.Estado.Configurada"))
                        : (EstadoDeServicio.AMedias, Textos.T("Ajustes.Estado.FaltaContrasena")),
                aviso: () => correo.Aviso is { Length: > 0 } a
                    ? a
                    : string.IsNullOrWhiteSpace(correo.Servidor) ? string.Empty : $"{correo.Servidor}:{correo.Puerto} · {correo.Seguridad}",
                origenes: correo)
            {
                Probar = correo.ProbarCommand,
                Configurar = new RelayCommand(() => IndiceDelApartado = ApartadoCorreo),
            });
        }

        return tarjetas;
    }

    /// <summary>Hay apartado de fonía que enseñar.</summary>
    public bool HayFonia => Fonia is not null;

    /// <summary>Hay apartado de correo que enseñar.</summary>
    public bool HayCorreo => CorreoQsl is not null;

    /// <summary>Hay apartado de subidas que enseñar.</summary>
    public bool HaySubidas => Subidas is not null;

    /// <summary>
    /// La subida automatica y el completado con QRZ: cuentas, casillas y cola. Nulo si no se
    /// ha montado (pruebas).
    /// </summary>
    public VistaModeloSubidas? Subidas { get; init; }

    /// <summary>Los secretos de los servicios, por orden.</summary>
    public IReadOnlyList<SecretoDeServicio> Secretos { get; }

    /// <summary>
    /// El apartado CAT: por donde se habla con el equipo.
    /// </summary>
    /// <remarks>
    /// Es lo que faltaba y por lo que el boton Conectar no conectaba: la via de control estaba
    /// escrita en el codigo y venia en «Ninguna».
    /// </remarks>
    public VistaModeloAjustesCat? Cat { get; }

    /// <summary>El apartado del cluster: a que nodo se entra y con que indicativo.</summary>
    public VistaModeloAjustesCluster? Cluster { get; }

    /// <summary>
    /// El apartado de audio y modos digitales: por donde entra el sonido y quien decodifica.
    /// </summary>
    /// <remarks>
    /// Es donde se elige entre el modem propio y el puente con WSJT-X, y donde se ve el nivel
    /// de entrada con su aviso de saturacion.
    /// </remarks>
    public VistaModeloAjustesAudio? Audio { get; }

    /// <summary>
    /// Apartado del idioma del programa. Nunca nulo: sin registrar (pruebas) cambia el idioma
    /// sin guardarlo, y la pestaña no se queda con enlaces rotos.
    /// </summary>
    public VistaModeloIdioma Idioma { get; init; } = new(new Ajustes.AjustesDelPrograma(), null);

    /// <summary>Apartado de fonía por el PC, o nulo si no se registró.</summary>
    public VistaModeloAjustesFonia? Fonia { get; init; }

    /// <summary>Apartado del correo con el que se mandan las QSL, o nulo si no se registró.</summary>
    public VistaModeloCorreoQsl? CorreoQsl { get; init; }

    /// <summary>
    /// Apartado «Actualizaciones»: el MISMO objeto que la barra del aviso de versión, para que
    /// buscar a mano desde aquí encienda también la barra. Nulo si no se registró.
    /// </summary>
    public VistaModeloActualizaciones? Actualizaciones { get; init; }

    /// <summary>Hay apartado de actualizaciones que enseñar.</summary>
    public bool HayActualizaciones => Actualizaciones is not null;

    /// <summary>Hay apartado de audio que enseñar.</summary>
    public bool HayAudio => Audio is not null;

    /// <summary>Hay apartado de equipo que enseñar.</summary>
    public bool HayCat => Cat is not null;

    /// <summary>Hay apartado de cluster que enseñar.</summary>
    public bool HayCluster => Cluster is not null;

    /// <summary>
    /// Aviso sobre la frase de paso, que se ensena <b>antes</b> de pedirla.
    /// </summary>
    /// <remarks>
    /// TQSL solo admite la frase por linea de ordenes, donde queda visible en la lista de
    /// procesos mientras dura la subida. No se puede evitar; lo que si se puede es decirlo
    /// antes y no despues.
    /// </remarks>
    public string AvisoDeLaFraseDePaso => TextoOClave.Resolver(_avisoDeLaFraseDePaso);

    /// <summary>Por que no se puede subir a LoTW. Vacio cuando si se puede.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SePuedeSubirALotw))]
    private string _motivoDeNoPoderSubirALotw = string.Empty;

    /// <summary>Se puede subir a LoTW ahora mismo.</summary>
    public bool SePuedeSubirALotw => MotivoDeNoPoderSubirALotw.Length == 0;

    /// <summary>Contactos que hay en el cuaderno.</summary>
    [ObservableProperty]
    private string _contactosDelCuaderno = "—";

    /// <summary>Donde esta el cuaderno.</summary>
    [ObservableProperty]
    private string _rutaDelCuaderno = string.Empty;

    /// <summary>Lo que ha pasado con la ultima importacion o exportacion.</summary>
    [ObservableProperty]
    private string _parteDeLaImportacion = string.Empty;

    /// <summary>La ultima importacion dejo choques que conviene revisar.</summary>
    [ObservableProperty]
    private bool _hayChoques;

    /// <summary>Choques de la ultima importacion, escritos.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> Choques { get; } = [];

    /// <summary>Se esta importando o exportando.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportarAdifCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportarAdifCommand))]
    private bool _ocupado;

    /// <summary>
    /// Pregunta que fichero importar. La pone la pantalla (es un dialogo de Windows); las
    /// pruebas ponen otra que no abre nada. Nula, o respuesta nula: no se hace nada.
    /// </summary>
    public Func<string?>? ElegirFicheroParaImportar { get; set; }

    /// <summary>Pregunta donde exportar. Igual que <see cref="ElegirFicheroParaImportar"/>.</summary>
    public Func<string?>? ElegirFicheroParaExportar { get; set; }

    /// <summary>Hay escritor de ADIF con el que exportar.</summary>
    public bool SePuedeExportar => _escritor is not null;

    /// <summary>Pide el fichero e importa.</summary>
    [RelayCommand(CanExecute = nameof(EstaLibre))]
    private async Task ImportarAdifAsync()
    {
        if (ElegirFicheroParaImportar?.Invoke() is not { Length: > 0 } ruta) return;
        await ImportarAsync(ruta).ConfigureAwait(true);
    }

    /// <summary>Pide donde y exporta el cuaderno entero.</summary>
    [RelayCommand(CanExecute = nameof(SePuedeExportarAhora))]
    private async Task ExportarAdifAsync()
    {
        if (ElegirFicheroParaExportar?.Invoke() is not { Length: > 0 } ruta) return;
        await ExportarAsync(ruta).ConfigureAwait(true);
    }

    private bool EstaLibre() => !Ocupado;

    private bool SePuedeExportarAhora() => !Ocupado && _escritor is not null;

    /// <summary>
    /// Exporta el cuaderno entero a un fichero ADIF, del contacto mas antiguo al mas nuevo.
    /// </summary>
    /// <param name="ruta">Fichero de destino.</param>
    /// <remarks>
    /// Se escribe primero en un fichero de al lado y solo al terminar bien se pone en su sitio:
    /// una exportacion que falla a medias no puede dejar destrozado un respaldo que ya
    /// existiera con ese nombre. Antes este boton decia «no se ha escrito» y no hacia nada.
    /// </remarks>
    /// <returns>La tarea de la exportacion.</returns>
    public async Task ExportarAsync(string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return;

        if (_escritor is null)
        {
            ParteDeLaImportacion = Textos.T("Ajustes.Libro.SinEscritorAlExportar");
            return;
        }

        Ocupado = true;
        Choques.Clear();
        HayChoques = false;
        ParteDeLaImportacion = Textos.T("Ajustes.Libro.Exportando");

        var temporal = ruta + ".escribiendo";
        var escritos = 0;

        async IAsyncEnumerable<Dominio.Entidades.Qso> Todos()
        {
            var desplazamiento = 0;
            while (true)
            {
                var pagina = await _cuaderno
                    .BuscarAsync(new CriterioQso { Descendente = false }, desplazamiento, PaginaDeExportacion)
                    .ConfigureAwait(false);

                foreach (var qso in pagina.Elementos)
                {
                    escritos++;
                    yield return qso;
                }

                if (pagina.Elementos.Count < PaginaDeExportacion) yield break;
                desplazamiento += pagina.Elementos.Count;
            }
        }

        try
        {
            await using (var fichero = File.Create(temporal))
            {
                await _escritor.EscribirAsync(Todos(), fichero).ConfigureAwait(true);
            }

            File.Move(temporal, ruta, overwrite: true);
            ParteDeLaImportacion = Textos.F("Ajustes.Libro.Exportados", Path.GetFileName(ruta), escritos);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido exportar el cuaderno a {Ruta}.", ruta);
            ParteDeLaImportacion = Textos.F("Ajustes.Libro.ErrorAlExportar", ex.Message);
            try
            {
                if (File.Exists(temporal)) File.Delete(temporal);
            }
            catch (IOException)
            {
                // Un temporal que no se deja borrar no tapa el fallo de verdad.
            }
        }
        finally
        {
            Ocupado = false;
        }
    }

    /// <summary>Trae las cifras del cuaderno y vuelve a mirar si LoTW esta listo.</summary>
    [RelayCommand]
    public async Task RefrescarAsync()
    {
        RefrescarLotw();

        // El indicativo de partida del cluster sale del perfil de estacion activo, y el perfil
        // se puede haber cambiado desde que se monto la pantalla.
        if (Cluster is not null) await Cluster.CargarElPerfilAsync().ConfigureAwait(true);
        Cat?.RefrescarViaPuesta();

        try
        {
            var total = await _cuaderno.ContarAsync().ConfigureAwait(true);
            ContactosDelCuaderno = total.ToString("N0", Textos.Cultura);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se han podido contar los contactos del cuaderno.");
            ContactosDelCuaderno = Textos.T("Ajustes.Libro.NoSeHaPodidoContar");
        }
    }

    /// <summary>
    /// Importa un fichero ADIF y ensena el parte completo.
    /// </summary>
    /// <param name="ruta">Fichero que se importa.</param>
    /// <remarks>
    /// El parte no es un adorno: dice cuantos registros se fundieron y <b>cuantas
    /// confirmaciones se han rescatado</b> al fundir en vez de saltar la copia. Esa cifra es la
    /// que justifica todo el trabajo de fusion, y es lo primero que Jose querra mirar al meter
    /// su respaldo.
    /// </remarks>
    public async Task ImportarAsync(string ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return;

        Ocupado = true;
        Choques.Clear();
        HayChoques = false;
        ParteDeLaImportacion = Textos.T("Ajustes.Libro.Importando");

        try
        {
            await using var fichero = File.OpenRead(ruta);
            var parte = await _importar.EjecutarAsync(fichero).ConfigureAwait(true);

            ParteDeLaImportacion = Escribir(parte, ruta);

            foreach (var choque in parte.Choques.Take(50))
            {
                Choques.Add(choque.ToString() ?? string.Empty);
            }

            HayChoques = Choques.Count > 0;

            await RefrescarAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido importar el ADIF {Ruta}.", ruta);
            ParteDeLaImportacion = Textos.F("Ajustes.Libro.ErrorAlImportar", ex.Message);
        }
        finally
        {
            Ocupado = false;
        }
    }

    private static string Escribir(ResultadoDeImportacion parte, string ruta)
    {
        var texto = new System.Text.StringBuilder();

        texto.Append(Textos.F(
            "Ajustes.Libro.Parte.Resumen",
            Path.GetFileName(ruta),
            parte.RegistrosLeidos,
            parte.Anadidos,
            parte.Fundidos,
            parte.FundidosEnElFichero,
            parte.FundidosConElCuaderno,
            parte.YaEstaban));

        if (parte.ConfirmacionesRecuperadas > 0)
        {
            texto.Append(' ').Append(Textos.F("Ajustes.Libro.Parte.Rescatadas", parte.ConfirmacionesRecuperadas));
        }

        if (parte.ProgramaOrigen is { Length: > 0 } programa)
        {
            texto.Append(' ').Append(Textos.F("Ajustes.Libro.Parte.Programa", programa));
        }

        texto.Append(' ').Append(Textos.F("Ajustes.Libro.Parte.Duracion", parte.Duracion.TotalSeconds));

        if (!parte.NoSePierdeNada)
        {
            texto.Append(' ').Append(Textos.T("Ajustes.Libro.Parte.NoCuadra"));
        }

        if (parte.Avisos.Count > 0)
        {
            texto.Append(' ').Append(Textos.F("Ajustes.Libro.Parte.Avisos", parte.Avisos.Count));
        }

        return texto.ToString();
    }

    private void RefrescarLotw()
    {
        try
        {
            MotivoDeNoPoderSubirALotw = _motivoDeNoPoderSubir() ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se ha podido comprobar si LoTW está listo.");
            MotivoDeNoPoderSubirALotw = Textos.F("Ajustes.Tarjeta.Lotw.ErrorAlComprobar", ex.Message);
        }
    }
}
