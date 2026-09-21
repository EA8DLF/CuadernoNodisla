using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui;

/// <summary>
/// Unico sitio del programa donde se ven todos los ensamblados a la vez. Aqui se decide que
/// implementacion concreta hay detras de cada puerto; el resto de la interfaz no lo sabe.
/// </summary>
/// <remarks>
/// Mientras la capa de datos y la de ADIF no esten listas, los puertos se cubren con las
/// implementaciones de <c>Desarrollo</c>. Cuando lleguen las de verdad, se cambian estas
/// lineas y nada mas: ni un modelo de vista ni una ventana se enteran.
/// </remarks>
public static class ConfiguracionDeServicios
{
    /// <summary>Contactos de demostracion que se cargan mientras no hay base de datos.</summary>
    public const int ContactosDeDemostracion = 20_000;

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
        servicios.AddSingleton<IRepositorioQso>(
            _ => new RepositorioQsoEnMemoria(CuadernoDeDemostracion.Generar(ContactosDeDemostracion)));
        servicios.AddSingleton<IRepositorioEstacion>(
            _ => new RepositorioEstacionEnMemoria(conPerfilesDeEjemplo: !SinPerfilesDeEjemplo));
        servicios.AddSingleton<IConsultaIndicativo, ConsultaIndicativoNoDisponible>();
        servicios.AddSingleton<ILectorAdif, LectorAdifNoDisponible>();
        servicios.AddSingleton<IEscritorAdif, EscritorAdifNoDisponible>();
    }

    private static void AnadirCasosDeUso(IServiceCollection servicios)
    {
        servicios.AddSingleton<RegistrarQso>();
        servicios.AddSingleton<EditarQso>();
        servicios.AddSingleton<EliminarQso>();
        servicios.AddSingleton<BuscarEnCuaderno>();
        servicios.AddSingleton<ConsultarTrabajadoAntes>();
        servicios.AddSingleton<CrearPerfilDeEstacion>();
    }

    private static void AnadirInterfaz(IServiceCollection servicios)
    {
        servicios.AddSingleton<VistaModeloEntradaQso>();
        servicios.AddSingleton<VistaModeloCuaderno>();
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
