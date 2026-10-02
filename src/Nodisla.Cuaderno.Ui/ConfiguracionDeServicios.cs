using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Datos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Audio;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Modem;
using Nodisla.Cuaderno.Radio;
using Nodisla.Cuaderno.Satelites;
using Nodisla.Cuaderno.Impresion;
using Nodisla.Cuaderno.Servicios;
using Nodisla.Cuaderno.Ui.Datos;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui;

/// <summary>
/// Unico sitio del programa donde se ven todos los ensamblados a la vez. Aqui se decide que
/// implementacion concreta hay detras de cada puerto; el resto de la interfaz no lo sabe.
/// </summary>
/// <remarks>
/// Mientras la capa de datos, la de ADIF, la de radio y las integraciones no esten listas, los
/// puertos se cubren con las implementaciones de <c>Desarrollo</c>. Cuando lleguen las de
/// verdad, se cambian estas lineas y nada mas: ni un modelo de vista ni una ventana se enteran.
/// </remarks>
public static class ConfiguracionDeServicios
{
    /// <summary>Contactos de demostracion que se cargan mientras no hay base de datos.</summary>
    public const int ContactosDeDemostracion = 20_000;

    /// <summary>
    /// El cuaderno de demostracion, generado una sola vez.
    /// </summary>
    /// <remarks>
    /// Va aqui y no dentro de cada registro porque lo usan dos puertos —el repositorio y las
    /// consultas de informe— y generarlo dos veces serian cuarenta mil contactos en memoria y
    /// dos cuadernos distintos que no cuadrarian entre si.
    /// </remarks>
    private static readonly Lazy<IReadOnlyList<Qso>> Demostracion =
        new(() => CuadernoDeDemostracion.Generar(ContactosDeDemostracion));

    /// <summary>Registra los puertos, los casos de uso, los modelos de vista y las ventanas.</summary>
    public static IServiceCollection AnadirCuaderno(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        AnadirPuertosProvisionales(servicios);
        AnadirCasosDeUso(servicios);
        AnadirInterfaz(servicios);

        return servicios;
    }

    /// <summary>
    /// Arranca sin ningun perfil de estacion, para poder ver y probar el primer arranque.
    /// Se activa con la variable de entorno <c>CUADERNO_SIN_PERFILES</c>.
    /// </summary>
    private static bool SinPerfilesDeEjemplo =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUADERNO_SIN_PERFILES"));

    /// <summary>
    /// Se ha pedido arrancar con los puertos simulados.
    /// </summary>
    /// <remarks>
    /// Sirve para poder ver y capturar la aplicacion sin equipo, sin red y sin base de datos.
    /// <b>Con la variable puesta, la ventana lo dice en pantalla</b>: no hay nada peor que
    /// creer que se esta mirando el cuaderno de verdad.
    /// </remarks>
    public static bool ConPuertosSimulados =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUADERNO_SIMULADO"));

    /// <summary>Registra los puertos: los de verdad, o los simulados si se han pedido.</summary>
    private static void AnadirPuertosProvisionales(IServiceCollection servicios)
    {
        servicios.AddSingleton<IResolutorDxcc>(_ => ResolutorDxcc.Predeterminado);

        // Canarias es Region 1 de la IARU: en 40 metros se acaba en 7.200 y no en 7.300.
        servicios.AddSingleton<IBandplan>(_ => Integraciones.Bandplan.BandplanNodisla.Para(RegionIaru.Region1));

        // Los servicios de confirmacion traen su propio registro, y con el <b>el almacen de
        // credenciales cifrado con DPAPI</b>: los secretos solo los puede descifrar la cuenta
        // de Windows que los escribio. Registrarlos no abre ninguna conexion: los clientes
        // HTTP se crean perezosos y no salen a la red hasta que alguien pide subir o bajar.
        //
        // Con CUADERNO_CARPETA (verificaciones) los secretos tambien van a esa carpeta: una
        // sesion de pruebas no puede leer las credenciales de verdad del operador.
        if (Environment.GetEnvironmentVariable("CUADERNO_CARPETA") is { Length: > 0 })
        {
            servicios.AddSingleton<IAlmacenDeCredenciales>(_ =>
                new Servicios.Credenciales.AlmacenDeCredencialesDpapi(
                    Path.Combine(App.CarpetaDeDatos, "credenciales.dat")));
        }

        servicios.AnadirServiciosDeConfirmacion();

        servicios.AnadirLotw(new Servicios.Lotw.OpcionesLotw());
        servicios.AnadirEqsl(new Servicios.Eqsl.OpcionesEqsl());
        servicios.AnadirClubLog(new Servicios.ClubLog.OpcionesClubLog());
        servicios.AnadirQrz(new Servicios.Qrz.OpcionesQrz());

        // La propagacion es la de verdad en los dos modos: trae los indices del servicio
        // meteorologico espacial y guarda copia en disco.
        servicios.AddHttpClient();
        servicios.AddSingleton<IPropagacion>(
            proveedor => new Propagacion.ServicioDePropagacion(
                proveedor.GetRequiredService<System.Net.Http.IHttpClientFactory>()));


        // Satelites: catalogo y seguidor, sin abrir la red. La estacion desde la que se mira
        // se pone luego, con FijarEstacion, en cuanto se sepa el perfil activo; hasta entonces
        // se mide desde el origen de coordenadas, que no rompe nada mientras no haya satelite
        // elegido en pantalla.
        servicios.AnadirSatelites(_ => { });

        // Impresion de QSL: el generador de PDF, tambien sin tocar la red ni la impresora.
        servicios.AnadirImpresionDeQsl();

        // Lo que el operador dejo configurado. Se lee aqui, antes de la bifurcacion, porque
        // tambien hace falta con los puertos simulados: el apartado de audio y el modem se
        // ven igual, y ademas asi lo que se guarde al cerrar en modo simulado <b>conserva</b>
        // la configuracion de equipo y de cluster en vez de pisarla con la de fabrica.
        var ajustes = AjustesDelPrograma.Leer(App.CarpetaDeDatos);
        servicios.AddSingleton(ajustes);

        AnadirServiciosEnLinea(servicios, ajustes);

        if (ConPuertosSimulados)
        {
            AnadirPuertosSimulados(servicios);
            return;
        }

        AnadirPuertosReales(servicios, ajustes);
    }

    /// <summary>
    /// El cuaderno de verdad: base de datos, diplomas, radio, cluster y ADIF.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Nada de esto toca la radio ni la red al arrancar.</b> El equipo y el cluster se
    /// registran, pero conectar es una accion del operador: abrir el programa no puede ponerse
    /// a mandar ordenes CAT a una radio que puede estar haciendo otra cosa, ni a abrir una
    /// conexion a un nodo.
    /// </para>
    /// <para>
    /// La base vive en <c>%AppData%\CuadernoNodisla\cuaderno.sqlite</c> y se puede copiar
    /// tal cual: es un solo fichero.
    /// </para>
    /// </remarks>
    private static void AnadirPuertosReales(IServiceCollection servicios, AjustesDelPrograma ajustes)
    {
        // ── El cuaderno ────────────────────────────────────────────────────
        servicios.AnadirDatosDelCuaderno(opciones =>
        {
            opciones.Ruta = Path.Combine(App.CarpetaDeDatos, OpcionesCuaderno.NombreDelFichero);
            opciones.CarpetaDeCopias = Path.Combine(App.CarpetaDeDatos, "copias");
        });

        // Los repositorios de la capa de datos van por ambito y los modelos de vista son
        // unicos: este puente abre un ambito por llamada. Ver UnAmbitoPorLlamada.
        servicios.AnadirPuentesDelCuaderno();

        // ── Los diplomas, ya con el cuaderno de verdad detras ───────────────
        servicios.AddSingleton<IDiplomas>(proveedor => new Diplomas.MotorDeDiplomas(
            new Diplomas.OpcionesDeDiplomas
            {
                RutaDelCatalogo = Path.Combine(App.CarpetaDeDatos, Diplomas.OpcionesDeDiplomas.NombreDelCatalogo),
            },
            proveedor.GetRequiredService<IFabricaDeConexion>(),
            proveedor.GetRequiredService<IResolutorDxcc>()));

        // ── ADIF de verdad: importar el respaldo de Log4OM y exportar ───────
        servicios.AddSingleton<ILectorAdif, Adif.LectorAdif>();
        servicios.AddSingleton<IEscritorAdif, Adif.EscritorAdif>();

        // ── La radio ───────────────────────────────────────────────────────
        // Registrada, pero SIN abrir el puerto ni buscar por los puertos serie: arrancar el
        // programa no puede ponerse a abrir COM uno por uno. Se monta la via guardada con el
        // puerto guardado, y conectar sigue siendo cosa del operador. Si el equipo se ha
        // cambiado de puerto, en Ajustes esta el boton que lo busca.
        var deLaRadio = ajustes.Equipo.AOpcionesDeRadio();
        servicios.AnadirRadio(opciones =>
        {
            opciones.Via = deLaRadio.Via;
            opciones.Ft710 = deLaRadio.Ft710;
            opciones.Rigctld = deLaRadio.Rigctld;
            opciones.OmniRig = deLaRadio.OmniRig;
            opciones.Vigilante = deLaRadio.Vigilante;

            // Salvaguardas de TX (plan de banda, ROE, potencia por banda): su propio fichero.
            opciones.Vigilante.Seguridad = Ajustes.AjustesDeSeguridadTx.Leer(App.CarpetaDeDatos).AOpciones();
        });

        // OJO: aqui NO se registra un IEquipoAvanzado de mentira. El modelo de vista mira si
        // el control que hay ES avanzado, asi que colar un equipo simulado detras de ese puerto
        // pintaria el frontal del FT-710 con mandos inventados encima de una radio que no esta
        // conectada. Mientras la via de control sea Ninguna, el control es generico y la cabina
        // lo dice.

        // ── El cluster: varios nodos por Telnet a la vez, sin conectar solos ──
        // Los nodos, el indicativo y los tiempos salen de los ajustes, y cada contrasena del
        // almacen cifrado. El indicativo vacio quiere decir «el del perfil de estacion
        // activo», que es lo que hay que poner cuando alguien opera como EA8DLF/P.
        servicios.AddSingleton(proveedor => new Integraciones.Cluster.FuenteDeVariosNodos(
            opciones => new Integraciones.Cluster.ClusterTelnet(opciones, proveedor.GetRequiredService<IResolutorDxcc>()),
            OpcionesDeLosNodos(proveedor, ajustes)));

        servicios.AddSingleton<IFuenteSpots>(
            proveedor => proveedor.GetRequiredService<Integraciones.Cluster.FuenteDeVariosNodos>());


        // ── El audio del modem propio ──────────────────────────────────────
        // Registrado, pero SIN ABRIR NINGUN DISPOSITIVO: el modulo deja claro que no arranca
        // el seguimiento del reloj ni toca la tarjeta de sonido hasta que se le pide. Abrir el
        // microfono de alguien al arrancar un programa no se hace.
        //
        // El seguimiento del reloj tampoco arranca solo, y por la misma razon: abrir el
        // programa no tiene por que ponerse a hablar con servidores de hora. Se pone en marcha
        // al entrar en la pestana Digital, que es cuando el desvio importa.
        servicios.AnadirAudio(opciones =>
        {
            opciones.FrecuenciaDeMuestreo = ajustes.Digital.FrecuenciaDeMuestreo;
            opciones.Reloj.SeguimientoAutomatico = false;
        });

        // ── EL MODEM PROPIO DE FT8 Y FT4 ───────────────────────────────────
        // Los modos digitales los hace esta aplicacion y no un programa de fuera: no hay
        // puente con WSJT-X ni con JTDX.
        //
        // Las tablas del protocolo se cargan una vez y se comparten. Si el fichero no esta,
        // el modulo se queda con un codigo de pruebas y LO DICE: el modem funciona entero
        // pero solo se entiende consigo mismo, y la pestana Digital lo enseña con un cartel
        // que no se puede pasar por alto.
        servicios.AddSingleton(proveedor => TablasDelProtocolo.Cargar(
            null,
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Modos.Tablas")));

        // Se le entregan HECHOS la salida de audio y el vigilante de PTT. El modem no sabe
        // abrir una tarjeta ni accionar un PTT por su cuenta: si no se los dan, se niega a
        // emitir en vez de apanarselas. Registrarlo no abre nada ni transmite nada.
        servicios.AddSingleton<IModemPropio>(proveedor => new ModemPropio(
            proveedor.GetRequiredService<TablasDelProtocolo>(),
            proveedor.GetRequiredService<IRelojDelModem>(),
            proveedor.GetRequiredService<IEntradaDeAudio>(),
            proveedor.GetRequiredService<ISalidaDeAudio>(),
            proveedor.GetRequiredService<IVigilantePtt>(),
            proveedor.GetService<ILoggerFactory>()?.CreateLogger<ModemPropio>()));

        // Con puertos de verdad NO se conecta nada solo.
        servicios.AddSingleton(ArranqueDeOperacion.ConPuertosReales);
    }

    /// <summary>
    /// Los simulados, para poder ver y probar la aplicacion sin equipo, sin red y sin base.
    /// </summary>
    /// <remarks>
    /// No se borran cuando llega lo real: siguen haciendo falta para capturar pantallas, para
    /// enseñar la aplicacion y para trabajar en la interfaz sin tener la radio delante. Lo que
    /// si hace falta es que <b>se note</b>, y de eso se encarga el aviso de la ventana.
    /// </remarks>
    private static void AnadirPuertosSimulados(IServiceCollection servicios)
    {
        servicios.AddSingleton<IRepositorioQso>(_ => new RepositorioQsoEnMemoria(Demostracion.Value));
        servicios.AddSingleton<IRepositorioEstacion>(
            _ => new RepositorioEstacionEnMemoria(conPerfilesDeEjemplo: !SinPerfilesDeEjemplo));
        servicios.AddSingleton<IRepositorioRondas, RepositorioRondasEnMemoria>();
        servicios.AddSingleton<IRepositorioDiplomasEmitidos, RepositorioDiplomasEmitidosEnMemoria>();

        servicios.AddSingleton<IConsultasDeInforme>(
            proveedor => new ConsultasDeInformeEnMemoria(
                Demostracion.Value,
                proveedor.GetRequiredService<IResolutorDxcc>()));

        servicios.AddSingleton<IDiplomas>(
            proveedor => new DiplomasDeDesarrollo(
                Demostracion.Value,
                proveedor.GetRequiredService<IResolutorDxcc>(),
                App.CarpetaDeDatos));

        // El ADIF de verdad tambien aqui: leer y escribir ficheros no toca ni la red ni el
        // equipo, y el cuaderno de detras es el de memoria. Con los provisionales, Importar y
        // Exportar de Ajustes no se podian probar nunca con los puertos simulados.
        servicios.AddSingleton<ILectorAdif, Adif.LectorAdif>();
        servicios.AddSingleton<IEscritorAdif, Adif.EscritorAdif>();

        servicios.AddSingleton<EquipoSimulado>();
        servicios.AddSingleton<IControlEquipo>(p => p.GetRequiredService<EquipoSimulado>());
        servicios.AddSingleton<IEquipoAvanzado>(p => p.GetRequiredService<EquipoSimulado>());
        servicios.AddSingleton<IAnalizadorDeEspectro>(p => new AnalizadorSimulado(p.GetRequiredService<EquipoSimulado>()));
        servicios.AddSingleton<IVigilantePtt>(
            p => new VigilantePttDeDesarrollo(p.GetRequiredService<IControlEquipo>()));
        // El cluster de mentira tiene VARIOS nodos, como el de verdad: cada uno con sus
        // anuncios, que se solapan para que se vea la fusion, uno de escucha automatica y uno
        // caido para que se vea el error. Ninguno sale a la red.
        // Los nodos de muestra van en una COPIA de los ajustes: los de verdad no se tocan, ni
        // en memoria ni en disco.
        servicios.AddSingleton(proveedor => new AjustesDelClusterDeMuestra(
            FuenteSpotsSimulada.ConNodosDeMuestra(proveedor.GetRequiredService<AjustesDelPrograma>())));
        servicios.AddSingleton(proveedor => new Integraciones.Cluster.FuenteDeVariosNodos(
            opciones => new FuenteSpotsSimulada(opciones),
            OpcionesDeLosNodos(proveedor, proveedor.GetRequiredService<AjustesDelClusterDeMuestra>().Ajustes)));
        servicios.AddSingleton<IFuenteSpots>(
            proveedor => proveedor.GetRequiredService<Integraciones.Cluster.FuenteDeVariosNodos>());

        // ── Los modos digitales, de mentira ────────────────────────────────
        // Ni tarjeta de sonido ni servidores de hora: la cascada y las decodificaciones se
        // inventan aqui y recorren el mismo camino que las de verdad. El reloj simulado
        // arranca DESVIADO a proposito, con el desvio que llego a tener esta maquina, para
        // que lo que se vea y se capture sea el aviso y no el caso bonito.
        servicios.AddSingleton<RelojSimulado>();
        servicios.AddSingleton<IRelojDelModem>(p => p.GetRequiredService<RelojSimulado>());
        servicios.AddSingleton<ISincronizadorDeHora>(p => p.GetRequiredService<RelojSimulado>());
        servicios.AddSingleton<IEntradaDeAudio, EntradaDeAudioSimulada>();
        servicios.AddSingleton<ISalidaDeAudio, SalidaDeAudioSimulada>();
        servicios.AddSingleton<IModemPropio, ModemPropioSimulado>();
        RecepcionReal.AnadirSiSePide(servicios);

        // Con los puertos simulados los paneles se conectan solos: asi la pantalla de
        // operacion se ve funcionando desde el primer arranque.
        servicios.AddSingleton(ArranqueDeOperacion.ConPuertosSimulados);
    }

    /// <summary>
    /// Indicativo con el que se entra al cluster: el escrito en los ajustes o, si no hay, el
    /// del perfil de estacion activo.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La consulta al cuaderno se lanza en <see cref="Task.Run(Func{Task})"/> y no se espera a
    /// pelo: esto se resuelve montando la ventana, en el hilo de la interfaz, y esperar ahi a
    /// una tarea que quiera volver a ese mismo hilo es la receta del programa colgado al
    /// arrancar.
    /// </para>
    /// <para>
    /// Si no hay perfil ni indicativo escrito se devuelve el vacio. El cluster no conectara, y
    /// la pantalla de ajustes lo dice con todas las letras; inventarse un indicativo seria
    /// entrar en un nodo publico con el de otro.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Los nodos de los ajustes, ya listos para la integracion: indicativo resuelto y
    /// contrasena de cada uno sacada del almacen cifrado.
    /// </summary>
    /// <param name="proveedor">Proveedor de servicios.</param>
    /// <param name="ajustes">Ajustes del programa.</param>
    /// <returns>Las opciones de cada nodo con servidor, en orden.</returns>
    internal static IReadOnlyList<Integraciones.Cluster.OpcionesCluster> OpcionesDeLosNodos(
        IServiceProvider proveedor,
        AjustesDelPrograma ajustes)
    {
        var comun = IndicativoDeAcceso(proveedor, ajustes);
        var almacen = proveedor.GetRequiredService<IAlmacenDeCredenciales>();

        return
        [
            .. ajustes.Cluster.Nodos
                .Where(n => !string.IsNullOrWhiteSpace(n.Servidor))
                .Select(n => ajustes.Cluster.AOpcionesDeNodo(n, comun, almacen.Leer(n.ClaveDeContrasena))),
        ];
    }

    private static Indicativo IndicativoDeAcceso(IServiceProvider proveedor, AjustesDelPrograma ajustes)
    {
        if (Indicativo.TryParse(ajustes.Cluster.Indicativo, out var escrito)) return escrito;

        try
        {
            var estaciones = proveedor.GetRequiredService<IRepositorioEstacion>();
            var perfil = Task.Run(() => estaciones.PredeterminadaAsync()).GetAwaiter().GetResult();
            return perfil?.StationCallsign ?? Indicativo.Vacio;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se ha podido leer el perfil de estación para el cluster.");
            return Indicativo.Vacio;
        }
    }

    /// <summary>
    /// QRZ.com para completar contactos y la subida automatica a LoTW, eQSL, Club Log y QRZ.
    /// </summary>
    /// <remarks>
    /// <b>Con los puertos simulados no se sale a la red:</b> el cuaderno es de demostracion y
    /// subirlo a las cuentas del operador, o consultar QRZ con su contrasena desde una sesion de
    /// pruebas, no se puede consentir. Ahi la consulta y los servicios son de mentira y la cola
    /// no se guarda en disco, para que un contacto de demostracion no se cuele en la cola de
    /// verdad.
    /// </remarks>
    private static void AnadirServiciosEnLinea(IServiceCollection servicios, AjustesDelPrograma ajustes)
    {
        servicios.AddSingleton<AvisosDeQsos>();

        if (ConPuertosSimulados)
        {
            servicios.AddSingleton<IConsultaIndicativo>(_ => new Servicios.Consulta.ConsultaConCache(
                () => [new ConsultaIndicativoSimulada()], carpeta: null));
            servicios.AddSingleton(proveedor => Arrancada(proveedor, new Servicios.Subidas.ColaDeSubidas(
                proveedor.GetRequiredService<IRepositorioQso>(),
                () => ServicioQslSimulado.Todos,
                medio => CuentasDeServicios.Activado(ajustes.Servicios, medio),
                ruta: null)));
        }
        else
        {
            servicios.AddSingleton(proveedor => new CuentasDeServicios(
                ajustes,
                proveedor.GetRequiredService<IAlmacenDeCredenciales>(),
                proveedor.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
                proveedor.GetRequiredService<Servicios.Red.PoliticaDeReintentos>(),
                IndicativoDelPerfil(proveedor)));

            servicios.AddSingleton<IConsultaIndicativo>(proveedor =>
            {
                var cuentas = proveedor.GetRequiredService<CuentasDeServicios>();
                return new Servicios.Consulta.ConsultaConCache(
                    cuentas.Consultas,
                    Path.Combine(App.CarpetaDeDatos, "cache-indicativos"),
                    log: proveedor.GetService<ILogger<Servicios.Consulta.ConsultaConCache>>());
            });

            servicios.AddSingleton(proveedor =>
            {
                var cuentas = proveedor.GetRequiredService<CuentasDeServicios>();
                return Arrancada(proveedor, new Servicios.Subidas.ColaDeSubidas(
                    proveedor.GetRequiredService<IRepositorioQso>(),
                    cuentas.ServiciosQsl,
                    cuentas.Activado,
                    Path.Combine(App.CarpetaDeDatos, Servicios.Subidas.ColaDeSubidas.NombreDelFichero),
                    log: proveedor.GetService<ILogger<Servicios.Subidas.ColaDeSubidas>>()));
            });
        }

        servicios.AddSingleton(proveedor => new CompletadorDeQso(
            proveedor.GetRequiredService<IConsultaIndicativo>(),
            activo: () => ajustes.Servicios.CompletarConQrz));

        servicios.AddSingleton(proveedor => new VistaModeloSubidas(
            proveedor.GetRequiredService<Servicios.Subidas.ColaDeSubidas>(),
            proveedor.GetRequiredService<CompletadorDeQso>(),
            ajustes,
            App.CarpetaDeDatos,
            proveedor.GetService<CuentasDeServicios>(),
            proveedor.GetRequiredService<AvisosDeQsos>()));
    }

    /// <summary>La cola escucha los contactos que se guardan y se pone a trabajar.</summary>
    /// <summary>
    /// El editor de la tarjeta QSL y su envío por correo.
    /// </summary>
    /// <remarks>
    /// Con <c>CUADERNO_SIMULADO</c> no sale ni un correo: el enviador es un buzón en disco que
    /// deja cada mensaje como <c>.eml</c> en <c>qsl\enviados-simulados</c> de la carpeta de
    /// datos. Al mandar, el cuaderno se refresca y el contacto que se esté modificando en Operar
    /// recibe la misma marca, para que guardar la ficha no la pise.
    /// </remarks>
    private static void AnadirQsl(IServiceCollection servicios)
    {
        servicios.AddSingleton<Servicios.Correo.IEnviadorDeCorreo>(proveedor => ConPuertosSimulados
            ? new Servicios.Correo.BuzonDeSalidaEnDisco(Path.Combine(App.CarpetaDeDatos, "qsl", "enviados-simulados"))
            : new Servicios.Correo.ClienteSmtp(proveedor.GetService<ILogger<Servicios.Correo.ClienteSmtp>>()));

        servicios.AddSingleton(proveedor =>
        {
            var servicio = new Qsl.ServicioDeQsl(
                App.CarpetaDeDatos,
                proveedor.GetRequiredService<Servicios.Correo.IEnviadorDeCorreo>(),
                proveedor.GetRequiredService<IAlmacenDeCredenciales>(),
                proveedor.GetService<IRepositorioQso>(),
                proveedor.GetService<IRepositorioEstacion>(),
                proveedor.GetService<CompletadorDeQso>())
            {
                // Perezoso: la impresion se crea despues y ella misma apunta a este editor.
                EstacionActiva = () => proveedor.GetRequiredService<VistaModeloImpresion>().EstacionId,
            };

            servicio.QslEnviadas += (_, ids) =>
            {
                proveedor.GetService<VistaModeloEntradaQso>()?.AnotarQslEnviadas(ids);
                if (proveedor.GetService<VistaModeloCuaderno>() is { } cuaderno) _ = cuaderno.RefrescarAsync();
            };
            return servicio;
        });

        servicios.AddSingleton(proveedor => new VistaModeloQsl(proveedor.GetRequiredService<Qsl.ServicioDeQsl>()));
        servicios.AddSingleton(proveedor => new VistaModeloCorreoQsl(proveedor.GetRequiredService<Qsl.ServicioDeQsl>()));

        // ── Diseñador de diplomas: mismo motor de plantillas y mismo correo que las QSL ──
        // Subpestaña «Diplomas» de la pestaña QSL: Vistas.Qsl.DisenadorDeDiplomas con
        // DataContext = VistaModeloDisenadorDeDiplomas. El historial y la numeración van en el
        // cuaderno (tabla diploma_emitido); con CUADERNO_SIMULADO, en memoria.
        servicios.AddSingleton(proveedor => new Qsl.ServicioDeDiplomas(
            App.CarpetaDeDatos,
            proveedor.GetRequiredService<Qsl.ServicioDeQsl>(),
            proveedor.GetRequiredService<IRepositorioDiplomasEmitidos>(),
            proveedor.GetService<IRepositorioQso>(),
            proveedor.GetService<IDiplomas>()));
        servicios.AddSingleton(proveedor => new VistaModeloDisenadorDeDiplomas(proveedor.GetRequiredService<Qsl.ServicioDeDiplomas>()));
    }

    private static Servicios.Subidas.ColaDeSubidas Arrancada(
        IServiceProvider proveedor, Servicios.Subidas.ColaDeSubidas cola)
    {
        cola.Escuchar(proveedor.GetRequiredService<AvisosDeQsos>());
        cola.Arrancar();
        return cola;
    }

    /// <summary>
    /// El indicativo del perfil de estacion activo, releido como mucho una vez por minuto.
    /// </summary>
    /// <remarks>
    /// Es el usuario de QRZ.com, LoTW, eQSL y HamQTH cuando en Ajustes se deja vacio. Se lee en
    /// un hilo aparte por lo mismo que <see cref="IndicativoDeAcceso"/>.
    /// </remarks>
    private static Func<Indicativo> IndicativoDelPerfil(IServiceProvider proveedor)
    {
        var cerrojo = new object();
        var valor = Indicativo.Vacio;
        var leido = DateTime.MinValue;
        return () =>
        {
            lock (cerrojo)
            {
                if (DateTime.UtcNow - leido < TimeSpan.FromMinutes(1)) return valor;
                try
                {
                    var estaciones = proveedor.GetRequiredService<IRepositorioEstacion>();
                    var perfil = Task.Run(() => estaciones.PredeterminadaAsync()).GetAwaiter().GetResult();
                    valor = perfil?.StationCallsign ?? Indicativo.Vacio;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "No se ha podido leer el perfil de estación para los servicios.");
                }
                leido = DateTime.UtcNow;
                return valor;
            }
        };
    }

    private static void AnadirCasosDeUso(IServiceCollection servicios)
    {
        servicios.AddSingleton<RegistrarQso>();
        servicios.AddSingleton<EditarQso>();
        servicios.AddSingleton<EliminarQso>();
        servicios.AddSingleton<BuscarEnCuaderno>();
        servicios.AddSingleton<ConsultarTrabajadoAntes>();
        servicios.AddSingleton<CrearPerfilDeEstacion>();
        servicios.AddSingleton<PuntosDelCuaderno>();
        // Con varios nodos, el mismo anuncio llega varias veces: la ventana y la tolerancia
        // con las que se juntan salen de los ajustes del cluster.
        servicios.AddSingleton(proveedor =>
        {
            var cluster = proveedor.GetRequiredService<AjustesDelPrograma>().Cluster;
            return new SeguirElCluster(
                proveedor.GetRequiredService<IFuenteSpots>(),
                proveedor.GetRequiredService<IConsultasDeInforme>(),
                proveedor.GetRequiredService<IResolutorDxcc>(),
                cluster.VentanaAcotada,
                cluster.ToleranciaAcotada);
        });
        servicios.AddSingleton<RetratoDelIndicativo>();
        servicios.AddSingleton<ImportarAdif>();
        servicios.AddSingleton<GestionarRonda>();
    }

    /// <summary>
    /// Devuelve, cada vez que se le pregunta, por que no se puede subir a LoTW.
    /// </summary>
    /// <remarks>
    /// Se pregunta cada vez y no una sola: el operador puede instalar TQSL con la aplicacion
    /// abierta, y entonces la respuesta cambia sin reiniciar nada.
    /// </remarks>
    private static Func<string?> MotivoDeNoPoderSubirALotw(IServiceProvider proveedor) => () =>
    {
        try
        {
            var lotw = new Servicios.Lotw.ServicioLotw(
                proveedor.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
                proveedor.GetRequiredService<IAlmacenDeCredenciales>(),
                new Servicios.Lotw.OpcionesLotw());

            return lotw.MotivoDeNoPoderSubir;
        }
        catch (Exception ex)
        {
            return global::Nodisla.Cuaderno.Idiomas.Textos.F("Ajustes.Tarjeta.Lotw.ErrorAlComprobar", ex.Message);
        }
    };

    /// <summary>
    /// Monta el apartado CAT de los ajustes, o nulo si se esta con los puertos simulados.
    /// </summary>
    /// <remarks>
    /// Con los puertos simulados no hay intermediario que conmutar —el equipo es de mentira—,
    /// asi que el apartado no se enseña en vez de enseñarse sin funcionar.
    /// </remarks>
    private static VistaModeloAjustesCat? AjustesDelEquipo(IServiceProvider proveedor)
    {
        var conmutable = proveedor.GetService<IControlEquipoConmutable>();

        // Solo para las capturas de la ayuda (simulado + CUADERNO_CAPTURA: ventana apartada que
        // se cierra sola y que nadie toca): el apartado se monta sobre el equipo simulado para
        // poder retratarlo. En uso normal con los simulados sigue sin apartado CAT.
        if (conmutable is null
            && ConPuertosSimulados
            && Environment.GetEnvironmentVariable("CUADERNO_CAPTURA") is { Length: > 0 }
            && proveedor.GetService<EquipoSimulado>() is { } simulado)
        {
            conmutable = new Radio.Control.ControlEquipoConmutable(simulado);
        }

        if (conmutable is null) return null;

        return new VistaModeloAjustesCat(
            proveedor.GetRequiredService<AjustesDelPrograma>(),
            App.CarpetaDeDatos,
            conmutable,
            proveedor.GetRequiredService<IVigilantePtt>(),

            // Con su propia categoria: cuando algo no conecta, lo primero que se mira es el
            // registro, y hasta ahora la busqueda de equipos no dejaba ahi ni una linea.
            proveedor.GetService<Microsoft.Extensions.Logging.ILoggerFactory>()
                ?.CreateLogger("Nodisla.Cuaderno.Radio.Equipo"));
    }

    /// <summary>
    /// Monta el apartado de audio y modos digitales.
    /// </summary>
    /// <remarks>
    /// Se monta siempre, con los puertos de verdad y con los simulados: en los dos casos hay
    /// entrada y salida detras —la de verdad o la de mentira— y el operador tiene que poder
    /// ver por donde entraria el sonido y elegir quien decodifica.
    /// </remarks>
    private static VistaModeloAjustesAudio AjustesDeAudio(IServiceProvider proveedor) => new(
        proveedor.GetRequiredService<AjustesDelPrograma>(),
        App.CarpetaDeDatos,
        proveedor.GetService<IEntradaDeAudio>(),
        proveedor.GetService<ISalidaDeAudio>());

    /// <summary>Monta el apartado del cluster.</summary>
    /// <remarks>
    /// Con los puertos simulados tambien se monta: los nodos son de mentira y no salen a la
    /// red, «Probar» contesta sin abrir ningun puerto y no se escribe en los ajustes de verdad.
    /// </remarks>
    private static VistaModeloAjustesCluster? AjustesDelCluster(IServiceProvider proveedor)
    {
        var varios = proveedor.GetService<Integraciones.Cluster.FuenteDeVariosNodos>();
        if (varios is null) return null;

        return new VistaModeloAjustesCluster(
            proveedor.GetService<AjustesDelClusterDeMuestra>()?.Ajustes ?? proveedor.GetRequiredService<AjustesDelPrograma>(),
            ConPuertosSimulados ? null : App.CarpetaDeDatos,
            proveedor.GetRequiredService<IAlmacenDeCredenciales>(),
            proveedor.GetRequiredService<IRepositorioEstacion>(),
            varios,
            proveedor.GetRequiredService<SeguirElCluster>(),
            ConPuertosSimulados ? FuenteSpotsSimulada.ProbarAsync : null,

            // Cambiar los nodos cambia el nombre que se lee en el panel del cluster, y ese
            // nombre no es una propiedad observable: hay que avisarlo a mano.
            () => proveedor.GetRequiredService<VistaModeloCluster>().AvisarDeCambioDeFuente());
    }

    /// <summary>
    /// El aviso de versiones nuevas y «Reportar un fallo».
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registrar no sale a la red. La comprobacion del arranque la lanza la barra
    /// <see cref="AvisoDeVersion"/> al cargarse, en segundo plano y como mucho una vez al dia;
    /// con <c>CUADERNO_SIMULADO</c> no se lanza.
    /// </para>
    /// <para>
    /// Un solo <see cref="VistaModeloActualizaciones"/> para la barra y para el apartado de
    /// Ajustes: buscar a mano desde Ajustes enciende la barra.
    /// </para>
    /// </remarks>
    private static void AnadirActualizacionesYFallos(IServiceCollection servicios)
    {
        servicios.AnadirActualizaciones(Soporte.VersionInstalada.Actual);
        servicios.AddSingleton(_ => new Soporte.AccionesDelSistema());

        servicios.AddSingleton(proveedor =>
        {
            var actualizaciones = new VistaModeloActualizaciones(
                proveedor.GetRequiredService<Servicios.Actualizaciones.ComprobadorDeVersiones>(),
                proveedor.GetRequiredService<Servicios.Actualizaciones.DescargadorDeInstalador>(),
                Ajustes.AjustesDeActualizaciones.Leer(App.CarpetaDeDatos),
                App.CarpetaDeDatos,
                TimeProvider.System,
                proveedor.GetRequiredService<Soporte.AccionesDelSistema>(),
                proveedor.GetService<ILogger<VistaModeloActualizaciones>>())
            {
                SinComprobacionAlArrancar = ConPuertosSimulados,
            };

            // Solo para las capturas de la ayuda, con los simulados: la barra encendida con una
            // version inventada (CUADERNO_VERSION_DE_PRUEBA=9.9.9), sin salir a GitHub.
            if (ConPuertosSimulados
                && Environment.GetEnvironmentVariable("CUADERNO_VERSION_DE_PRUEBA") is { Length: > 0 } inventada
                && Servicios.Actualizaciones.VersionSemantica.TryAnalizar(inventada, out var version))
            {
                actualizaciones.Nueva = new Servicios.Actualizaciones.VersionPublicada(
                    version!, "v" + inventada, inventada,
                    "- Ayuda integrada con el capítulo de primer uso.\n- Diseñador de diplomas en QSL › Diplomas.",
                    new Uri("https://github.com/EA8DLF/CuadernoNodisla/releases"), DateTimeOffset.UtcNow,
                    new Servicios.Actualizaciones.FicheroPublicado("CuadernoNodisla-Instalador.exe", new Uri("https://github.com/EA8DLF/CuadernoNodisla/releases"), 1),
                    new Servicios.Actualizaciones.FicheroPublicado("CuadernoNodisla-Instalador.exe.sha256", new Uri("https://github.com/EA8DLF/CuadernoNodisla/releases"), 1));
                actualizaciones.AvisoVisible = true;
            }

            return actualizaciones;
        });

        // Transitorio: cada vez que se abre el apartado, un formulario en blanco con el entorno
        // de ese momento (el operador puede haber cambiado de radio desde que arranco).
        servicios.AddTransient(proveedor =>
        {
            var ajustes = proveedor.GetRequiredService<AjustesDelPrograma>();
            return new VistaModeloReportarFallo(
                () => Soporte.DatosDelEntorno.Describir(Soporte.VersionInstalada.Actual.ToString(), ajustes),
                () => Soporte.DatosDelEntorno.UltimasLineasDelRegistro(Path.Combine(App.CarpetaDeDatos, "registros")),
                proveedor.GetRequiredService<Soporte.AccionesDelSistema>(),
                log: proveedor.GetService<ILogger<VistaModeloReportarFallo>>());
        });
        servicios.AddSingleton<Func<VistaModeloReportarFallo>>(
            proveedor => proveedor.GetRequiredService<VistaModeloReportarFallo>);

        // La ayuda: los capitulos de docs/ayuda incrustados en el ejecutable. Desde ella se
        // reporta un fallo (formulario nuevo cada vez) y se buscan actualizaciones con el
        // MISMO aviso de la barra.
        servicios.AddSingleton(proveedor => new VistaModeloAyuda(
            Soporte.LibroDeAyuda.DelEnsamblado(),
            proveedor.GetRequiredService<VistaModeloActualizaciones>(),
            proveedor.GetRequiredService<Func<VistaModeloReportarFallo>>(),
            proveedor.GetRequiredService<Soporte.AccionesDelSistema>()));
    }

    private static void AnadirInterfaz(IServiceCollection servicios)
    {
        servicios.AddSingleton(_ => EstadoDeLosPaneles.Leer(App.CarpetaDeDatos));
        servicios.AddSingleton<VistaModeloBandmap>();
        servicios.AddSingleton<VistaModeloDiplomas>();

        // LoTW se monta aqui solo para poder DECIR por que no se puede subir: si falta TQSL o
        // falta la ubicacion de estacion, la pantalla lo explica en vez de dejar un boton gris.
        servicios.AddSingleton(proveedor => new VistaModeloAjustes(
            proveedor.GetRequiredService<IAlmacenDeCredenciales>(),
            proveedor.GetRequiredService<ImportarAdif>(),
            proveedor.GetRequiredService<IRepositorioQso>(),
            Servicios.Lotw.ServicioLotw.ClaveDelAvisoDeLaFraseDePaso,
            MotivoDeNoPoderSubirALotw(proveedor),
            AjustesDelEquipo(proveedor),
            AjustesDelCluster(proveedor),
            AjustesDeAudio(proveedor),
            proveedor.GetService<IEscritorAdif>())
        {
            Subidas = proveedor.GetService<VistaModeloSubidas>(),
            Fonia = proveedor.GetService<VistaModeloAjustesFonia>(),
            Analizador = proveedor.GetService<VistaModeloAjustesAnalizador>(),
            CorreoQsl = proveedor.GetService<VistaModeloCorreoQsl>(),
            Actualizaciones = proveedor.GetService<VistaModeloActualizaciones>(),

            // Con lo simulado no se escribe en los ajustes de verdad: se cambia y ya.
            Idioma = new VistaModeloIdioma(
                proveedor.GetRequiredService<AjustesDelPrograma>(),
                ConPuertosSimulados ? null : App.CarpetaDeDatos),
        });

        servicios.AddSingleton<VistaModeloSolar>();
        servicios.AddSingleton<VistaModeloRetrato>();
        servicios.AddSingleton<VistaModeloEntradaQso>();
        servicios.AddSingleton<VistaModeloCuaderno>();
        servicios.AddSingleton<VistaModeloEquipo>();
        servicios.AddSingleton<VistaModeloCluster>();

        // ── El modem propio en pantalla ────────────────────────────────────
        // El reloj va dentro de la pestana Digital y no escondido en Ajustes: es lo primero
        // que hay que mirar cuando el modem no saca nada, y con el reloj mal no solo se
        // pierden decodificaciones, se transmite fuera de ventana.
        servicios.AddSingleton(proveedor => new VistaModeloRelojDigital(
            proveedor.GetRequiredService<IRelojDelModem>(),
            proveedor.GetService<ISincronizadorDeHora>()));

        // Si el modem arranco con el codigo de pruebas, la pantalla lo dice. No se le pasa el
        // objeto de las tablas: solo los dos datos que la pantalla necesita saber.
        servicios.AddSingleton(proveedor =>
        {
            var tablas = proveedor.GetService<TablasDelProtocolo>();
            return tablas is null
                ? EstadoDelCorrector.NoProcede
                : new EstadoDelCorrector(tablas.EsElCodigoReal, tablas.Procedencia);
        });

        // Los colores de la lista salen de lo que ya calcula el retrato del indicativo y los
        // diplomas; el equipo entra para poder ir a la frecuencia del modo por CAT.
        servicios.AddSingleton(proveedor => new VistaModeloModemPropio(
            proveedor.GetRequiredService<VistaModeloRelojDigital>(),
            proveedor.GetRequiredService<AjustesDelPrograma>(),
            proveedor.GetRequiredService<ConsultarTrabajadoAntes>(),
            proveedor.GetRequiredService<RegistrarQso>(),
            proveedor.GetRequiredService<EstadoDelCorrector>(),
            proveedor.GetService<IModemPropio>(),
            proveedor.GetService<IEntradaDeAudio>(),
            proveedor.GetService<ISalidaDeAudio>(),
            proveedor.GetService<IControlEquipo>(),
            new Digital.EvaluadorDeNovedad(
                proveedor.GetRequiredService<ConsultarTrabajadoAntes>(),
                proveedor.GetService<RetratoDelIndicativo>(),
                proveedor.GetService<IResolutorDxcc>(),
                proveedor.GetService<IRepositorioQso>(),
                proveedor.GetService<IDiplomas>()))
        {
            CarpetaDeDatos = App.CarpetaDeDatos,
            Completador = proveedor.GetService<CompletadorDeQso>(),
        });
        servicios.AddSingleton<VistaModeloMapa>();

        // ── Satelites: catalogo, pasos, seguimiento en vivo y Doppler ───────
        servicios.AddSingleton(proveedor => new VistaModeloSatelites(
            proveedor.GetRequiredService<Satelites.Catalogo.CatalogoDeSatelites>(),
            proveedor.GetRequiredService<Satelites.Seguimiento.SeguidorDeSatelites>(),
            proveedor.GetRequiredService<Satelites.Seguimiento.OpcionesDeSatelites>(),
            proveedor.GetRequiredService<IControlEquipo>(),
            proveedor.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
            proveedor.GetRequiredService<AjustesDelPrograma>(),
            App.CarpetaDeDatos,
            proveedor.GetRequiredService<VistaModeloMapa>()));

        // ── Impresion de etiquetas de QSL ────────────────────────────────────
        servicios.AddSingleton(proveedor => new VistaModeloImpresion(
            proveedor.GetRequiredService<BuscarEnCuaderno>(),
            proveedor.GetRequiredService<Impresion.IGeneradorDeImpresos>(),
            proveedor.GetRequiredService<AjustesDelPrograma>(),
            App.CarpetaDeDatos)
        {
            Qsl = proveedor.GetService<VistaModeloQsl>(),
        });

        // ── Tarjeta QSL propia: editor, correo y envío ──────────────────────
        AnadirQsl(servicios);

        // ── La ronda de control (NET Control) ────────────────────────────────
        servicios.AddSingleton(proveedor => new VistaModeloRonda(
            proveedor.GetRequiredService<GestionarRonda>(),
            proveedor.GetRequiredService<IRepositorioEstacion>()));

        // ── Fonía por el PC: altavoces, micrófono y PTT de fonía ─────────────
        servicios.AnadirFonia(ConPuertosSimulados, App.CarpetaDeDatos);

        // El analizador de la propia radio. Con los puertos simulados, AnalizadorSimulado.
        // Sus ajustes (spots, clic, suelo de ruido, paleta) van en Configuración → Equipo y se
        // aplican al momento. Con los simulados no se escriben en disco.
        servicios.AddSingleton(proveedor => new VistaModeloAjustesAnalizador(
            proveedor.GetRequiredService<AjustesDelPrograma>(),
            ConPuertosSimulados ? null : App.CarpetaDeDatos));
        servicios.AddSingleton(proveedor =>
        {
            var analizador = new VistaModeloAnalizador(
                proveedor.GetService<IAnalizadorDeEspectro>(), audio: proveedor.GetService<IEntradaDeAudio>());
            var ajustes = proveedor.GetRequiredService<VistaModeloAjustesAnalizador>();
            analizador.Aplicar(ajustes.Guardado);
            ajustes.Cambiado += (_, _) => analizador.Aplicar(ajustes.Guardado);
            return analizador;
        });

        // ── Telegrafía: el decodificador de CW propio, sobre el audio de recepción ──
        // Solo escucha: ni transmite ni manda órdenes al equipo.
        servicios.AddSingleton(proveedor => new VistaModeloCw(
            proveedor.GetRequiredService<AjustesDelPrograma>(),
            proveedor.GetService<IEntradaDeAudio>(),
            dxcc: proveedor.GetService<Dominio.Dxcc.IResolutorDxcc>())
        {
            CarpetaDeDatos = App.CarpetaDeDatos,
        });

        // ── Transmitir en CW (macros y secuencia) y las salvaguardas de TX ──
        Telegrafia.ServiciosDeTransmisionCw.AnadirTransmisionCw(servicios, App.CarpetaDeDatos);
        AnadirActualizacionesYFallos(servicios);

        // ── Servidor para otros programas (rigctld y TCI): apagado de fábrica ──
        OtrosProgramas.ServiciosDeServidores.AnadirServidoresParaOtrosProgramas(servicios, App.CarpetaDeDatos);

        // Al montar la ventana se dan de alta las fuentes de PTT en el vigilante: tras un corte
        // de seguridad no se vuelve a transmitir hasta que todas esten sueltas.
        servicios.AddSingleton(proveedor =>
        {
            var principal = ActivatorUtilities.CreateInstance<VistaModeloPrincipal>(proveedor);
            Telegrafia.FuentesDePtt.Conectar(proveedor);
            OtrosProgramas.ServiciosDeServidores.Arrancar(proveedor);
            return principal;
        });
        servicios.AddSingleton<VentanaPrincipal>();

        // El primer arranque se pide una sola vez, pero se crea al vuelo para que la ventana
        // principal no dependa del contenedor mas alla de esta fabrica.
        servicios.AddTransient<VistaModeloPrimerArranque>();

        // La bienvenida del cuaderno vacio: se pide una sola vez, el primer arranque con la
        // base recien creada.
        servicios.AddTransient(proveedor => new VistaModeloCuadernoVacio(
            proveedor.GetRequiredService<ImportarAdif>(),
            Path.Combine(App.CarpetaDeDatos, OpcionesCuaderno.NombreDelFichero)));
        servicios.AddTransient<VentanaDeCuadernoVacio>();
        servicios.AddSingleton<Func<VentanaDeCuadernoVacio>>(
            proveedor => proveedor.GetRequiredService<VentanaDeCuadernoVacio>);
        servicios.AddTransient<VentanaDePrimerArranque>();
        servicios.AddSingleton<Func<VentanaDePrimerArranque>>(
            proveedor => proveedor.GetRequiredService<VentanaDePrimerArranque>);
    }
}
