using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Modem;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Con los puertos simulados, el modem de verdad escuchando el codec de verdad: SOLO RECEPCION.
/// </summary>
/// <remarks>
/// <para>
/// Se activa con <c>CUADERNO_AUDIO_REAL</c> junto a <c>CUADERNO_SIMULADO</c>. Sirve para
/// comprobar la pestaña Digital con el aire real (decodificaciones, cascada, colores) sin abrir
/// el puerto CAT de la radio ni el cuaderno de verdad.
/// </para>
/// <para>
/// <b>No puede transmitir, por construccion</b>: no se registra ninguna salida de audio (la
/// pantalla no abre ninguna al escuchar) y el modem se monta sin salida y sin vigilante de PTT,
/// con lo que se niega a emitir aunque se lo pidan.
/// </para>
/// </remarks>
public static class RecepcionReal
{
    /// <summary>Se ha pedido la recepcion real.</summary>
    public static bool Pedida =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUADERNO_AUDIO_REAL"));

    /// <summary>Sustituye el audio y el modem simulados por los de verdad, sin salida.</summary>
    public static void AnadirSiSePide(IServiceCollection servicios)
    {
        if (!Pedida) return;

        servicios.AnadirAudio(opciones => opciones.Reloj.SeguimientoAutomatico = false);
        servicios.RemoveAll<ISalidaDeAudio>();

        servicios.AddSingleton(proveedor => TablasDelProtocolo.Cargar(
            null,
            proveedor.GetService<ILoggerFactory>()?.CreateLogger("Nodisla.Cuaderno.Modos.Tablas")));

        servicios.RemoveAll<IModemPropio>();
        servicios.AddSingleton<IModemPropio>(proveedor => new ModemPropio(
            proveedor.GetRequiredService<TablasDelProtocolo>(),
            proveedor.GetRequiredService<IRelojDelModem>(),
            proveedor.GetRequiredService<IEntradaDeAudio>(),
            salida: null,
            vigilante: null,
            proveedor.GetService<ILoggerFactory>()?.CreateLogger<ModemPropio>()));
    }
}
