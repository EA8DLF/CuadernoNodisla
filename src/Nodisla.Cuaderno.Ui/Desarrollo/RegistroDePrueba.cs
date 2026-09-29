using System.Windows;
using System.Windows.Threading;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Teclea un indicativo en el formulario de Operar (y lo registra, si se pide) sin tocar el
/// teclado de nadie, para comprobar la ficha de QRZ y la subida automatica con captura.
/// </summary>
/// <remarks>
/// <para>
/// Solo con los puertos simulados: la ficha y los servicios son de mentira y la cola no se
/// guarda, asi que no sale nada a la red ni se sube ningun contacto de verdad.
/// </para>
/// <para>
/// <c>CUADERNO_TECLEAR=DL1ABC</c> solo escribe el indicativo; <c>CUADERNO_REGISTRAR=DL1ABC</c>
/// ademas lo registra un momento despues.
/// </para>
/// </remarks>
public static class RegistroDePrueba
{
    /// <summary>Programa el tecleo, si se ha pedido.</summary>
    /// <param name="ventana">Ventana principal.</param>
    /// <param name="antes">Cuanto esperar antes de teclear.</param>
    public static void ProgramarSiSePide(Window ventana, TimeSpan antes)
    {
        ArgumentNullException.ThrowIfNull(ventana);
        if (!ConfiguracionDeServicios.ConPuertosSimulados) return;

        var registrar = Environment.GetEnvironmentVariable("CUADERNO_REGISTRAR");
        var indicativo = registrar is { Length: > 0 } ? registrar : Environment.GetEnvironmentVariable("CUADERNO_TECLEAR");
        if (indicativo is not { Length: > 0 }) return;

        var paso = 0;
        var reloj = new DispatcherTimer { Interval = antes };
        reloj.Tick += async (_, _) =>
        {
            if (ventana.DataContext is not VistaModeloPrincipal principal)
            {
                reloj.Stop();
                return;
            }

            try
            {
                if (paso == 0)
                {
                    principal.Entrada.Indicativo = indicativo;
                    Log.Information("Registro de prueba: tecleado {Indicativo}.", indicativo);
                    paso++;
                    reloj.Interval = TimeSpan.FromSeconds(1);
                    if (registrar is not { Length: > 0 }) reloj.Stop();
                    return;
                }

                reloj.Stop();
                await principal.Entrada.GuardarCommand.ExecuteAsync(null);
                Log.Information("Registro de prueba: {Mensaje}", principal.Entrada.Mensaje);
            }
            catch (Exception ex)
            {
                reloj.Stop();
                Log.Error(ex, "Registro de prueba fallido.");
            }
        };
        reloj.Start();
    }
}
