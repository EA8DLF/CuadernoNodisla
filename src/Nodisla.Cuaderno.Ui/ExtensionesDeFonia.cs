using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Dispositivos;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui;

/// <summary>Registro de la fonia por el PC.</summary>
internal static class ExtensionesDeFonia
{
    /// <summary>
    /// Registra la fonia: dos caminos de audio, su control y los modelos de pantalla.
    /// </summary>
    /// <remarks>
    /// Registrar no abre nada: ni el microfono ni la tarjeta del equipo. La escucha se abre
    /// cuando el operador la pide y el microfono solo mientras se transmite. Con los puertos
    /// simulados, los caminos son de mentira.
    /// </remarks>
    public static IServiceCollection AnadirFonia(this IServiceCollection servicios, bool simulado, string carpeta)
    {
        servicios.AddSingleton(proveedor =>
        {
            var fabrica = proveedor.GetService<ILoggerFactory>();
            IPuenteDeAudio recepcion = simulado
                ? new PuenteDeAudioSimulado(0)
                : new PuenteDeAudioWasapi(registro: fabrica?.CreateLogger("Nodisla.Cuaderno.Audio.Fonia.Escucha"));
            IPuenteDeAudio transmision = simulado
                ? new PuenteDeAudioSimulado(1.7)
                : new PuenteDeAudioWasapi(registro: fabrica?.CreateLogger("Nodisla.Cuaderno.Audio.Fonia.Micro"));

            var ajustes = proveedor.GetRequiredService<AjustesDelPrograma>().Fonia;
            return new ControlDeFonia(
                proveedor.GetRequiredService<IVigilantePtt>(),
                recepcion,
                transmision,
                new OpcionesDeFonia { TiempoMaximo = TimeSpan.FromSeconds(Math.Clamp(ajustes.TiempoMaximoSegundos, 10, 600)) },
                TimeProvider.System,
                fabrica?.CreateLogger<ControlDeFonia>());
        });

        servicios.AddSingleton(proveedor =>
        {
            Func<IReadOnlyList<DispositivoDeAudio>> entradas;
            Func<IReadOnlyList<DispositivoDeAudio>> salidas;
            Func<bool, string?>? porOmision = null;

            if (simulado)
            {
                entradas = () => proveedor.GetService<IEntradaDeAudio>()?.Dispositivos ?? [];
                salidas = () => proveedor.GetService<ISalidaDeAudio>()?.Dispositivos ?? [];
            }
            else
            {
                entradas = () => CatalogoDeDispositivos.Entradas();
                salidas = () => CatalogoDeDispositivos.Salidas();
                porOmision = DispositivosPorOmision.Id;
            }

            return new VistaModeloAjustesFonia(
                proveedor.GetRequiredService<AjustesDelPrograma>(),
                carpeta,
                entradas,
                salidas,
                porOmision);
        });

        servicios.AddSingleton(proveedor => new VistaModeloFonia(
            proveedor.GetRequiredService<VistaModeloAjustesFonia>(),
            proveedor.GetRequiredService<ControlDeFonia>(),
            proveedor.GetRequiredService<VistaModeloEquipo>(),
            proveedor.GetService<IControlEquipo>()));

        return servicios;
    }
}
