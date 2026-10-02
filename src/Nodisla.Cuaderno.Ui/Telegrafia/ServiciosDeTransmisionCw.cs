using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Cw;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Telegrafia;

/// <summary>Alta de la transmision de telegrafia en el contenedor de servicios.</summary>
public static class ServiciosDeTransmisionCw
{
    /// <summary>
    /// Registra el emisor de telegrafia (sobre el control del equipo y el vigilante del PTT) y la
    /// zona de transmision. Se llama desde <c>ConfiguracionDeServicios</c>, despues de dar de alta
    /// el equipo, el vigilante, el contacto nuevo, el modem propio y el decodificador de CW.
    /// </summary>
    /// <param name="servicios">El contenedor.</param>
    /// <param name="carpetaDeDatos">Donde se guardan las macros.</param>
    /// <returns>El mismo contenedor.</returns>
    public static IServiceCollection AnadirTransmisionCw(this IServiceCollection servicios, string? carpetaDeDatos)
    {
        servicios.AddSingleton(p => new EmisorCw(
            p.GetRequiredService<IControlEquipo>(),
            p.GetRequiredService<IVigilantePtt>(),
            registro: p.GetService<ILoggerFactory>()?.CreateLogger<EmisorCw>()));
        servicios.AddSingleton(p => new VistaModeloTransmisionCw(
            p.GetRequiredService<AjustesDelPrograma>(),
            p.GetRequiredService<EmisorCw>(),
            p.GetRequiredService<IControlEquipo>(),
            p.GetService<VistaModeloEntradaQso>(),
            p.GetService<VistaModeloCw>(),
            p.GetService<VistaModeloModemPropio>(),
            carpetaDeDatos));

        // Las salvaguardas de transmision (plan de banda, ROE, potencia por banda).
        servicios.AddSingleton(p => new VistaModeloSeguridadTx(p.GetRequiredService<IVigilantePtt>(), carpetaDeDatos));
        return servicios;
    }
}
