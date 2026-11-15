using FluentAssertions;
using Nodisla.Cuaderno.Ui.VistaModelos;
using static Nodisla.Cuaderno.Ui.Pruebas.MontajeDeLaPestanaDigital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Cancelar una emision del modem (Detener, panico, cambio de modo) se trataba como un fallo:
/// error en el registro y «A task was canceled.» en ingles a la vista. Modem, salida y reloj de
/// mentira: aqui no sale nada al aire.
/// </summary>
public sealed class CancelarLaEmisionPruebas
{
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    private static DateTimeOffset T(int segundos) => Mediodia.AddSeconds(segundos);

    [Fact]
    public async Task CancelarUnaEmisionNoEsUnFalloYSeDiceEnElIdiomaDelOperador()
    {
        var modelo = Montar(out var modem, out var reloj, out _);
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;

        reloj.Ahora = T(5);
        await modelo.LlamarCqCommand.ExecuteAsync(null);
        modelo.TxHabilitado.Should().BeTrue();

        // La emision se cancela a medias (Detener, panico, cambio de modo): el modem lo cuenta asi.
        modem.FalloAlEmitir = new TaskCanceledException();
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0)));

        modem.Emisiones.Should().BeEmpty();
        modelo.Aviso.Should().Be("Emisión cancelada.", "ni «A task was canceled.» ni «No se ha podido emitir»");
        modelo.TxHabilitado.Should().BeFalse("el estado queda igual de limpio que con un fallo de verdad");
        modelo.Secuenciador.Activo.Should().BeFalse();

        // Y no se ha quedado atascado: la siguiente orden sale con normalidad.
        reloj.Ahora = T(35);
        await modelo.LlamarCqCommand.ExecuteAsync(null);
        reloj.Ahora = T(46);
        await Ventana(modelo, () => modem.Soltar(T(30)));
        modem.Emisiones.Should().ContainSingle().Which.Texto.Should().StartWith("CQ EA8DLF");
    }

    [Fact]
    public async Task UnFalloDeVerdadAlEmitirSeSigueDiciendoComoFallo()
    {
        var modelo = Montar(out var modem, out var reloj, out _);
        await modelo.EscucharCommand.ExecuteAsync(null);
        modelo.PermitirTransmitir = true;
        modelo.ConfirmarQueVaATransmitir = _ => true;

        reloj.Ahora = T(5);
        await modelo.LlamarCqCommand.ExecuteAsync(null);
        modem.FalloAlEmitir = new InvalidOperationException("la salida de audio no responde");
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(T(0)));

        modelo.Aviso.Should().Contain("la salida de audio no responde");
        modelo.TxHabilitado.Should().BeFalse();
    }

    private static async Task Ventana(VistaModeloModemPropio modelo, Action soltar)
    {
        var aviso = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void AlProcesar(object? _, DateTimeOffset __) => aviso.TrySetResult();

        modelo.VentanaProcesada += AlProcesar;
        try
        {
            soltar();
            await aviso.Task.WaitAsync(Paciencia);
        }
        finally
        {
            modelo.VentanaProcesada -= AlProcesar;
        }
    }
}
