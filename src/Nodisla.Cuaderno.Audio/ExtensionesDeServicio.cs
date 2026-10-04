using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Captura;
using Nodisla.Cuaderno.Audio.Reloj;
using Nodisla.Cuaderno.Audio.Reproduccion;

namespace Nodisla.Cuaderno.Audio;

/// <summary>Registro del modulo de audio en el contenedor de servicios.</summary>
[SupportedOSPlatform("windows")]
public static class ExtensionesDeServicio
{
    /// <summary>
    /// Registra el reloj del modem, la entrada y la salida de audio.
    /// </summary>
    /// <remarks>
    /// El reloj se registra como el mismo objeto para todos: la hora corregida tiene que ser
    /// una sola en todo el programa, y ademas asi la medida del desvio se hace una vez. No se
    /// arranca su seguimiento aqui; eso lo decide la aplicacion cuando quiera empezar a hablar
    /// con la red.
    /// </remarks>
    /// <param name="servicios">Coleccion de servicios de la aplicacion.</param>
    /// <param name="configurar">Ajustes de audio.</param>
    /// <returns>La misma coleccion, para poder encadenar.</returns>
    /// <exception cref="ArgumentNullException">Si no se da coleccion de servicios.</exception>
    public static IServiceCollection AnadirAudio(
        this IServiceCollection servicios,
        Action<OpcionesDeAudio>? configurar = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        var opciones = new OpcionesDeAudio();
        configurar?.Invoke(opciones);
        servicios.AddSingleton(opciones);

        // Como en el modulo de radio: se pide la fabrica de registros y se crea uno con su
        // categoria, para que en el registro se vea de donde sale cada apunte.
        servicios.AddSingleton<IRelojDelSistema, RelojDelSistemaDeWindows>();

        servicios.AddSingleton<IRelojDelModem>(proveedor => new RelojDelModem(
            opciones.Reloj,
            fuentes: null,
            relojDelSistema: null,
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Audio.Reloj")));

        servicios.AddSingleton<ISincronizadorDeHora>(proveedor => new SincronizadorDeHora(
            proveedor.GetRequiredService<IRelojDelModem>(),
            proveedor.GetRequiredService<IRelojDelSistema>(),
            opciones.Reloj,
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Audio.Reloj")));

        // Envueltas en un recuento de referencias: CW, el módem propio y la fonía comparten la
        // misma tarjeta (un AddSingleton) sin conocerse entre sí. Sin esto, quien la cierra se la
        // quita a los demás, y quien deja de necesitarla no puede cerrarla por si acaso otro la
        // sigue usando — con lo que, en la práctica, nadie la cerraba nunca. Ver
        // EntradaDeAudioCompartida para el porqué completo.
        servicios.AddSingleton<IEntradaDeAudio>(proveedor => new EntradaDeAudioCompartida(new EntradaDeAudioWasapi(
            proveedor.GetRequiredService<IRelojDelModem>(),
            opciones,
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Audio.Entrada"))));

        servicios.AddSingleton<ISalidaDeAudio>(proveedor => new SalidaDeAudioCompartida(new SalidaDeAudioWasapi(
            opciones,
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Audio.Salida"))));

        return servicios;
    }
}
