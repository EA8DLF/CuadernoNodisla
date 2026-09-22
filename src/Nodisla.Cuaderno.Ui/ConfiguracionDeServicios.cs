using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
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

    /// <summary>Puertos cubiertos con implementaciones en memoria hasta que lleguen las reales.</summary>
    private static void AnadirPuertosProvisionales(IServiceCollection servicios)
    {
        servicios.AddSingleton<IResolutorDxcc>(_ => ResolutorDxcc.Predeterminado);

        // Canarias es Region 1 de la IARU: en 40 metros se acaba en 7.200 y no en 7.300.
        servicios.AddSingleton<IBandplan>(_ => Integraciones.Bandplan.BandplanNodisla.Para(RegionIaru.Region1));

        servicios.AddSingleton<IRepositorioQso>(_ => new RepositorioQsoEnMemoria(Demostracion.Value));
        servicios.AddSingleton<IRepositorioEstacion>(
            _ => new RepositorioEstacionEnMemoria(conPerfilesDeEjemplo: !SinPerfilesDeEjemplo));
        servicios.AddSingleton<IConsultaIndicativo, ConsultaIndicativoNoDisponible>();

        // Los secretos van cifrados con la proteccion de datos de la cuenta de Windows. Esta
        // implementacion SI es la de verdad: no hay version de mentira de guardar una
        // contrasena.
        servicios.AddSingleton<IAlmacenDeCredenciales>(
            _ => new Servicios.Credenciales.AlmacenDeCredencialesDpapi());

        // El motor de diplomas de verdad necesita una conexion a la base del cuaderno, y la
        // interfaz todavia trabaja contra el repositorio en memoria. Mientras tanto, este
        // cuenta lo que SI se puede contar de los contactos que hay —entidades, continentes,
        // zonas y prefijos— y dice por que lo demas no sale.
        servicios.AddSingleton<IDiplomas>(
            proveedor => new DiplomasDeDesarrollo(
                Demostracion.Value,
                proveedor.GetRequiredService<IResolutorDxcc>()));

        // El modulo de propagacion SI es el de verdad: trae los indices del servicio
        // meteorologico espacial y guarda copia en disco. Sin red, arranca con la copia y lo
        // dice; la franja solar ensena ese aviso tal cual.
        servicios.AddHttpClient();
        servicios.AddSingleton<IPropagacion>(
            proveedor => new Propagacion.ServicioDePropagacion(
                proveedor.GetRequiredService<System.Net.Http.IHttpClientFactory>()));
        servicios.AddSingleton<ILectorAdif, LectorAdifNoDisponible>();
        servicios.AddSingleton<IEscritorAdif, EscritorAdifNoDisponible>();

        servicios.AddSingleton<IConsultasDeInforme>(
            proveedor => new ConsultasDeInformeEnMemoria(
                Demostracion.Value,
                proveedor.GetRequiredService<IResolutorDxcc>()));

        // ── Operacion ───────────────────────────────────────────────────────
        // Estas cuatro lineas son las que cambian el dia que lleguen el control CAT de verdad,
        // el cluster por Telnet y el lector de UDP de WSJT-X. Nada mas.
        servicios.AddSingleton<EquipoSimulado>();
        servicios.AddSingleton<IControlEquipo>(p => p.GetRequiredService<EquipoSimulado>());
        servicios.AddSingleton<IEquipoAvanzado>(p => p.GetRequiredService<EquipoSimulado>());
        servicios.AddSingleton<IVigilantePtt>(
            p => new VigilantePttDeDesarrollo(p.GetRequiredService<IControlEquipo>()));
        servicios.AddSingleton<IFuenteSpots>(_ => new FuenteSpotsSimulada());
        servicios.AddSingleton<IPuenteDigital, PuenteDigitalSimulado>();

        // Con los puertos simulados los paneles se conectan solos; con una radio de verdad
        // detras, esta linea pasa a ArranqueDeOperacion.ConPuertosReales.
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
        servicios.AddSingleton(_ => DiplomasElegidos.Leer(App.CarpetaDeDatos));

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
        servicios.AddTransient<VentanaDePrimerArranque>();
        servicios.AddSingleton<Func<VentanaDePrimerArranque>>(
            proveedor => proveedor.GetRequiredService<VentanaDePrimerArranque>);
    }
}
