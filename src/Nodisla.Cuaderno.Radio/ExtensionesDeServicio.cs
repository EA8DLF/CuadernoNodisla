using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio;

/// <summary>Registro del modulo de radio en el contenedor de servicios.</summary>
[SupportedOSPlatform("windows")]
public static class ExtensionesDeServicio
{
    /// <summary>
    /// Registra el control del equipo y el vigilante del PTT.
    /// </summary>
    /// <remarks>
    /// Se registra <see cref="IVigilantePtt"/> como el unico camino para transmitir. El
    /// <see cref="IControlEquipo"/> tambien se registra porque el resto de la aplicacion
    /// necesita leer frecuencia y modo, pero subir el PTT por el esta vetado: sus
    /// implementaciones lo rechazan.
    /// </remarks>
    /// <param name="servicios">Coleccion de servicios de la aplicacion.</param>
    /// <param name="configurar">Ajustes de radio.</param>
    /// <returns>La misma coleccion, para poder encadenar.</returns>
    public static IServiceCollection AnadirRadio(
        this IServiceCollection servicios,
        Action<OpcionesDeRadio>? configurar = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        var opciones = new OpcionesDeRadio();
        configurar?.Invoke(opciones);
        servicios.AddSingleton(opciones);

        // El contenedor registra ILogger<T>, no ILogger a secas: se pide la fabrica y se crean
        // con su categoria, para que en el registro se vea de donde sale cada apunte.
        servicios.AddSingleton<IControlEquipo>(proveedor =>
            FabricaDeControlEquipo.Crear(
                opciones,
                proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Radio.Equipo")));

        servicios.AddSingleton<IVigilantePtt>(proveedor => new VigilantePtt(
            proveedor.GetRequiredService<IControlEquipo>(),
            opciones.Vigilante,
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Radio.Ptt")));

        return servicios;
    }
}
