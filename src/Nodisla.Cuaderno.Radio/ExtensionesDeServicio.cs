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
        //
        // Y NO SE REGISTRA EL CONTROL A PELO, sino el intermediario que lo lleva dentro: asi
        // cambiar la via en los ajustes no obliga a cerrar el programa. Todo lo que hay delante
        // —el panel, el frontal, la barra, el vigilante del PTT— se engancha a este objeto, que
        // no cambia nunca; lo que cambia es lo que tiene detras.
        servicios.AddSingleton(proveedor => new Control.ControlEquipoConmutable(
            FabricaDeControlEquipo.Crear(
                opciones,
                proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Radio.Equipo")),
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Radio.Equipo")));

        servicios.AddSingleton<IControlEquipo>(
            proveedor => proveedor.GetRequiredService<Control.ControlEquipoConmutable>());
        servicios.AddSingleton<IControlEquipoConmutable>(
            proveedor => proveedor.GetRequiredService<Control.ControlEquipoConmutable>());

        servicios.AddSingleton<IVigilantePtt>(proveedor => new VigilantePtt(
            proveedor.GetRequiredService<IControlEquipo>(),
            opciones.Vigilante,
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Radio.Ptt"),
            proveedor.GetService<IBandplan>()));

        // El analizador de espectro del propio FT-710, por su puente FT4222 interno. Solo lee;
        // registrarlo no abre nada: se arranca cuando la pantalla del frontal lo pide.
        servicios.AddSingleton<IAnalizadorDeEspectro>(proveedor => new Espectro.AnalizadorFt710(
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Radio.Espectro")));

        return servicios;
    }
}
