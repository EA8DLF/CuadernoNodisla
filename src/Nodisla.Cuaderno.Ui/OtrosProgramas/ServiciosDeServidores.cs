using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Servidores;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.OtrosProgramas;

/// <summary>Alta del servidor para otros programas (rigctld y TCI) en el contenedor de servicios.</summary>
public static class ServiciosDeServidores
{
    /// <summary>
    /// Registra la radio compartida, los dos servidores y su apartado de Configuracion. No arranca
    /// nada: eso lo hace <see cref="Arrancar"/>, y solo lo que el operador haya encendido.
    /// </summary>
    /// <param name="servicios">El contenedor.</param>
    /// <param name="carpetaDeDatos">Donde se guardan los ajustes.</param>
    /// <returns>El mismo contenedor.</returns>
    public static IServiceCollection AnadirServidoresParaOtrosProgramas(this IServiceCollection servicios, string? carpetaDeDatos)
    {
        servicios.AddSingleton(p =>
        {
            var vigilante = p.GetRequiredService<IVigilantePtt>();
            return new RadioCompartida(
                p.GetRequiredService<IControlEquipo>(),
                vigilante,

                // El mismo pestillo que el modem y la telegrafia: «Permitir transmitir en esta sesion».
                () => p.GetService<VistaModeloModemPropio>()?.PermitirTransmitir == true,
                f => vigilante is VigilantePtt v && v.Seguridad.PotenciaMaximaPorBanda.TryGetValue(OpcionesDeSeguridadDeTx.ClaveDeBanda(f), out var w) ? w : null,
                p.GetService<ILoggerFactory>()?.CreateLogger<RadioCompartida>());
        });
        servicios.AddSingleton(p => new ServidoresParaOtrosProgramas(
            p.GetRequiredService<RadioCompartida>(),
            p.GetService<IFuenteSpots>(),
            p.GetService<ILoggerFactory>()));
        servicios.AddSingleton(p => new VistaModeloServidores(p.GetRequiredService<ServidoresParaOtrosProgramas>(), carpetaDeDatos));
        return servicios;
    }

    /// <summary>
    /// Arranca lo que el operador dejo encendido (de fabrica, nada) y engancha los spots que mandan
    /// los clientes TCI al bandmap.
    /// </summary>
    /// <param name="proveedor">El contenedor.</param>
    public static void Arrancar(IServiceProvider proveedor)
    {
        ArgumentNullException.ThrowIfNull(proveedor);
        if (proveedor.GetService<ServidoresParaOtrosProgramas>() is not { } servidores) return;
        var vm = proveedor.GetService<VistaModeloServidores>();

        // Los spots de los clientes TCI entran por la fuente fusionada del cluster, como un nodo mas.
        if (proveedor.GetService<Integraciones.Cluster.FuenteDeVariosNodos>() is { } cluster)
        {
            servidores.SpotDeCliente += (_, spot) => cluster.Inyectar(spot);
        }

        var guardadas = vm?.Guardadas() ?? new OpcionesDeServidores();
        if (!guardadas.RigctldActivo && !guardadas.TciActivo && !guardadas.PermitirTx) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await servidores.AplicarAsync(guardadas).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error(ex, "No se ha podido arrancar el servidor para otros programas.");
            }
        });
    }
}
