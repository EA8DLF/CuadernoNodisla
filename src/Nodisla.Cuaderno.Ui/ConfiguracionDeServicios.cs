using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Datos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Audio;
using Nodisla.Cuaderno.Radio;
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

        servicios.AddSingleton<IConsultaIndicativo, ConsultaIndicativoNoDisponible>();

        if (ConPuertosSimulados)
        {
            AnadirPuertosSimulados(servicios);
            return;
        }

        AnadirPuertosReales(servicios);
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
    private static void AnadirPuertosReales(IServiceCollection servicios)
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
        // Registrada, pero SIN abrir el puerto: la via arranca en Ninguna y la elige el
        // operador en los ajustes. El PTT solo se sube por el vigilante.
        servicios.AnadirRadio(opciones =>
        {
            opciones.Via = ViaDeControl.Ninguna;
        });

        // OJO: aqui NO se registra un IEquipoAvanzado de mentira. El modelo de vista mira si
        // el control que hay ES avanzado (`_equipo is IEquipoAvanzado`), asi que colar un
        // equipo simulado detras de ese puerto pintaria el frontal del FT-710 con mandos
        // inventados encima de una radio que no esta conectada. Mientras la via de control sea
        // Ninguna, el control es generico y la cabina lo dice.

        // ── El cluster, por Telnet y sin conectar solo ──────────────────────
        // PENDIENTE: el nodo y el indicativo con el que se entra estan fijos aqui porque
        // todavia no hay pantalla donde elegirlos, y el registro de servicios se monta antes
        // de que haya perfil de estacion cargado. Cuando Ajustes tenga la configuracion del
        // cluster, esto pasa a leerse de ahi y el indicativo, del perfil activo.
        servicios.AddSingleton<IFuenteSpots>(proveedor => new Integraciones.Cluster.ClusterTelnet(
            new Integraciones.Cluster.OpcionesCluster
            {
                Nombre = "Cluster de DX",
                Servidor = "cluster.ea4rch.es",
                Puerto = 7300,
                Indicativo = Indicativo.Parse("EA8DLF"),
            },
            proveedor.GetRequiredService<IResolutorDxcc>()));

        // ── Los modos digitales, escuchando solo cuando se pida ─────────────
        servicios.AddSingleton<IPuenteDigital>(_ => new Integraciones.Digital.PuenteDigitalUdp());

        // ── El audio del modem propio ──────────────────────────────────────
        // Registrado, pero SIN ABRIR NINGUN DISPOSITIVO: el modulo deja claro que no arranca
        // el seguimiento del reloj ni toca la tarjeta de sonido hasta que se le pide. Abrir el
        // microfono de alguien al arrancar un programa no se hace.
        servicios.AnadirAudio();

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

        servicios.AddSingleton<IConsultasDeInforme>(
            proveedor => new ConsultasDeInformeEnMemoria(
                Demostracion.Value,
                proveedor.GetRequiredService<IResolutorDxcc>()));

        servicios.AddSingleton<IDiplomas>(
            proveedor => new DiplomasDeDesarrollo(
                Demostracion.Value,
                proveedor.GetRequiredService<IResolutorDxcc>(),
                App.CarpetaDeDatos));

        servicios.AddSingleton<ILectorAdif, LectorAdifNoDisponible>();
        servicios.AddSingleton<IEscritorAdif, EscritorAdifNoDisponible>();

        servicios.AddSingleton<EquipoSimulado>();
        servicios.AddSingleton<IControlEquipo>(p => p.GetRequiredService<EquipoSimulado>());
        servicios.AddSingleton<IEquipoAvanzado>(p => p.GetRequiredService<EquipoSimulado>());
        servicios.AddSingleton<IVigilantePtt>(
            p => new VigilantePttDeDesarrollo(p.GetRequiredService<IControlEquipo>()));
        servicios.AddSingleton<IFuenteSpots>(_ => new FuenteSpotsSimulada());
        servicios.AddSingleton<IPuenteDigital, PuenteDigitalSimulado>();

        // Con los puertos simulados los paneles se conectan solos: asi la pantalla de
        // operacion se ve funcionando desde el primer arranque.
        servicios.AddSingleton(ArranqueDeOperacion.ConPuertosSimulados);
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
        servicios.AddSingleton<SeguirElCluster>();
        servicios.AddSingleton<RetratoDelIndicativo>();
        servicios.AddSingleton<ImportarAdif>();
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
            return $"No se ha podido comprobar el estado de LoTW: {ex.Message}";
        }
    };

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
            Servicios.Lotw.ServicioLotw.AvisoDeLaFraseDePaso,
            MotivoDeNoPoderSubirALotw(proveedor)));

        servicios.AddSingleton<VistaModeloSolar>();
        servicios.AddSingleton<VistaModeloRetrato>();
        servicios.AddSingleton<VistaModeloEntradaQso>();
        servicios.AddSingleton<VistaModeloCuaderno>();
        servicios.AddSingleton<VistaModeloEquipo>();
        servicios.AddSingleton<VistaModeloCluster>();
        servicios.AddSingleton<VistaModeloDigital>();
        servicios.AddSingleton<VistaModeloMapa>();
        servicios.AddSingleton<VistaModeloPrincipal>();
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
