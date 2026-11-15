using FluentAssertions;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>Los avisos de dispositivos de fonia mal elegidos.</summary>
public sealed partial class FoniaDispositivosYRegistroPruebas
{
    // ── Punto 8: avisos de dispositivos mal elegidos ─────────────────────

    /// <summary>
    /// Tal y como estaba guardado la manana del 2026-11-15, y sin que el programa supiera cual
    /// era el codec (ninguno marcado «del equipo»): no avisaba de nada.
    /// </summary>
    [Fact]
    public async Task Avisa_si_la_entrada_del_equipo_y_el_microfono_son_el_mismo_dispositivo()
    {
        var m = await MontarAsync(
            entradas: [CodecIn with { EsDelEquipo = false }, MicroPc],
            salidas: [CodecOut with { EsDelEquipo = false }, CascosPc]);

        m.Fonia.Ajustes.EntradaDelEquipo = MicroPc;
        m.Fonia.Ajustes.Microfono = MicroPc;

        m.Fonia.Ajustes.AvisoDeDispositivos.Should().Contain("La entrada del equipo y el micrófono del PC son el mismo dispositivo");
    }

    [Fact]
    public async Task Avisa_si_los_altavoces_y_la_salida_al_equipo_son_el_mismo_dispositivo()
    {
        var m = await MontarAsync(
            entradas: [CodecIn with { EsDelEquipo = false }, MicroPc],
            salidas: [CodecOut with { EsDelEquipo = false }, CascosPc]);

        m.Fonia.Ajustes.Altavoces = CascosPc;
        m.Fonia.Ajustes.SalidaAlEquipo = CascosPc;

        m.Fonia.Ajustes.AvisoDeDispositivos.Should().Contain("Los altavoces del PC y la salida al equipo son el mismo dispositivo");
    }

    [Fact]
    public async Task Avisa_si_la_entrada_del_equipo_no_es_la_del_codec()
    {
        var m = await MontarAsync(entradas: [CodecIn, MicroPc, OtroMicro]);

        m.Fonia.Ajustes.EntradaDelEquipo = OtroMicro;

        m.Fonia.Ajustes.Microfono.Should().Be(MicroPc);
        m.Fonia.Ajustes.AvisoDeDispositivos.Should().Contain("La entrada del equipo no es la del codec del equipo");
    }

    [Fact]
    public async Task Con_cada_dispositivo_en_su_sitio_no_avisa_de_nada()
    {
        var m = await MontarAsync();

        m.Fonia.Ajustes.EntradaDelEquipo.Should().Be(CodecIn);
        m.Fonia.Ajustes.SalidaAlEquipo.Should().Be(CodecOut);
        m.Fonia.Ajustes.Microfono.Should().Be(MicroPc);
        m.Fonia.Ajustes.Altavoces.Should().Be(CascosPc);
        m.Fonia.Ajustes.AvisoDeDispositivos.Should().BeEmpty();
    }
}
