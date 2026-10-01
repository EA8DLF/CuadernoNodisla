using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Datos.Informes;
using Nodisla.Cuaderno.Datos.Repositorios;

namespace Nodisla.Cuaderno.Datos;

/// <summary>Registro de la capa de datos en el contenedor de servicios.</summary>
public static class ExtensionesDeServicio
{
    /// <summary>
    /// Registra el contexto del cuaderno, los repositorios, las consultas de informe y el
    /// migrador.
    /// </summary>
    /// <param name="servicios">Coleccion de servicios de la aplicacion.</param>
    /// <param name="configurar">Ajustes opcionales de rutas y copias de seguridad.</param>
    /// <returns>La misma coleccion, para poder encadenar.</returns>
    public static IServiceCollection AnadirDatosDelCuaderno(
        this IServiceCollection servicios,
        Action<OpcionesCuaderno>? configurar = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        var opciones = new OpcionesCuaderno();
        configurar?.Invoke(opciones);
        servicios.AddSingleton(opciones);

        servicios.AddDbContext<ContextoCuaderno>(constructor =>
            constructor.UseSqlite(
                opciones.CadenaDeConexion,
                sqlite => sqlite.MigrationsAssembly(typeof(ContextoCuaderno).Assembly.FullName)));

        servicios.AddSingleton<IFabricaDeConexion, FabricaDeConexion>();

        // El notificador de diplomas es opcional: se pide por si alguien lo ha registrado.
        servicios.AddScoped<IRepositorioQso>(sp => new RepositorioQso(
            sp.GetRequiredService<ContextoCuaderno>(),
            sp.GetService<INotificadorDeDiplomas>()));
        servicios.AddScoped<IRepositorioEstacion, RepositorioEstacion>();
        servicios.AddScoped<IConsultasDeInforme, ConsultasDeInforme>();
        servicios.AddScoped<IRepositorioRondas, RepositorioRondas>();
        servicios.AddScoped<IRepositorioDiplomasEmitidos, RepositorioDiplomasEmitidos>();
        servicios.AddScoped<MigradorDeCuaderno>();

        return servicios;
    }
}
