using FluentAssertions;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Apagar con la pulsación larga de LOCK (autorizado por Jose el 29-09-2026): PS0 sale SOLO por
/// <see cref="ControlFt710.ApagarAsync"/>; en todo lo demás sigue prohibida.
/// </summary>
public class EncendidoFt710Pruebas
{
    private static async Task<(CanalCatDeCaptura Canal, ControlFt710 Control)> MontarAsync()
    {
        var canal = new CanalCatDeCaptura();
        var control = new ControlFt710(canal, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });
        await control.ConectarAsync();
        return (canal, control);
    }

    [Fact]
    public async Task Apagar_baja_el_ptt_manda_ps0_y_desconecta()
    {
        var (canal, control) = await MontarAsync();
        await using var _ = control;
        var desde = canal.Recibidas.Count;

        await control.ApagarAsync();

        var enviadas = canal.Recibidas.Skip(desde).ToList();
        enviadas.Should().Contain("PS0;");
        enviadas.IndexOf("TX0;").Should().BeLessThan(enviadas.IndexOf("PS0;"), "el PTT se baja antes de apagar");
        control.Estado.Conectado.Should().BeFalse();
    }

    [Fact]
    public async Task Fuera_del_apagado_ps0_sigue_prohibida()
    {
        var (_, control) = await MontarAsync();
        await using var _ = control;

        var enCrudo = () => control.OrdenEnCrudoAsync("PS0;");
        await enCrudo.Should().ThrowAsync<OrdenPeligrosaException>();

        var comprobar = () => OrdenesFt710.ComprobarQueEsSegura("PS0;");
        comprobar.Should().Throw<OrdenPeligrosaException>();
    }

    [Fact]
    public async Task Tras_apagar_ps0_vuelve_a_estar_prohibida()
    {
        var (_, control) = await MontarAsync();
        await using var _ = control;

        await control.ApagarAsync();

        var comprobar = () => OrdenesFt710.ComprobarQueEsSegura("PS0;");
        comprobar.Should().Throw<OrdenPeligrosaException>();
    }

    [Fact]
    public async Task Apagar_sin_conectar_no_manda_nada()
    {
        var canal = new CanalCatDeCaptura();
        await using var control = new ControlFt710(canal, new OpcionesFt710 { IntervaloDeSondeo = TimeSpan.FromHours(1) });

        var apagar = () => control.ApagarAsync();
        await apagar.Should().ThrowAsync<InvalidOperationException>();
        canal.Recibidas.Should().NotContain("PS0;");
    }
}
