using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.VistaModelos;
using static Nodisla.Cuaderno.Ui.Pruebas.MontajeDeLaPestanaDigital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La señal propia, oida por el receptor mientras se transmite, salia en la lista marcada como
/// «Nuevo» (visto el 15-11-2026). No es una estacion oida. Modem y reloj de mentira.
/// </summary>
public sealed class SenalPropiaPruebas
{
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    private static DateTimeOffset T(int segundos) => Mediodia.AddSeconds(segundos);

    [Theory]
    [InlineData("CQ EA8DLF IL27")]
    [InlineData("CQ EA8DLF/P IL27")]
    public async Task LaSenalPropiaNoCuentaComoEstacionOida(string propio)
    {
        var modelo = Montar(out var modem, out var reloj, out _);
        modelo.PonerElDial(Frecuencia.DesdeMegahercios(14.074m));
        modelo.PskReporterActivo = true;

        // La captura del 15-11-2026: «13:30:00 -19 -0,1 1.788 CQ EA8DLF IL27», con «Nuevo».
        reloj.Ahora = T(16);
        await Ventana(modelo, () => modem.Soltar(
            T(0),
            Oido(propio, -19, 1788, T(0)),
            Oido("CQ EA5XYZ IM98", -7, 800, T(0))));

        var mia = modelo.Decodificaciones.Single(f => f.Texto == propio);
        mia.EsNuevo.Should().BeFalse("uno no es una estacion nueva para si mismo");
        mia.EntidadNueva.Should().BeFalse();
        mia.NuevaEnBanda.Should().BeFalse();
        mia.CuadriculaNueva.Should().BeFalse();
        mia.TrabajadoAntes.Should().BeFalse();
        mia.Novedad.HayAlgo.Should().BeFalse();

        var otra = modelo.Decodificaciones.Single(f => f.Texto == "CQ EA5XYZ IM98");
        otra.EsNuevo.Should().BeTrue("a los demas se les sigue marcando igual");

        modelo.PskReporter.Pendientes.Should().Be(1, "solo EA5XYZ es una recepcion que informar");

        // «Solo nuevos» no la deja pasar.
        modelo.SoloNuevos = true;
        modelo.Decodificaciones.Should().ContainSingle().Which.Texto.Should().Be("CQ EA5XYZ IM98");
        modelo.SoloNuevos = false;

        // Y el doble clic no la convierte en corresponsal.
        modelo.DecodificacionElegida = modelo.Decodificaciones.Single(f => f.Texto == propio);
        await modelo.PrepararRespuestaCommand.ExecuteAsync(null);
        modelo.Corresponsal.Should().BeEmpty();
        modelo.Secuenciador.Activo.Should().BeFalse();
        modelo.Secuenciador.DxCall.Should().BeEmpty();
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
