using FluentAssertions;
using Serilog.Events;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>Lo que la fonia deja escrito en el registro cuando no puede escuchar o transmitir.</summary>
public sealed partial class FoniaDispositivosYRegistroPruebas
{
    // ── Punto 6: el motivo queda en el registro ──────────────────────────

    [Fact]
    public async Task Los_dispositivos_que_hay_y_los_elegidos_quedan_anotados_una_vez()
    {
        var m = await MontarAsync();

        var linea = m.Anotado.Textos.Should().ContainSingle(t => t.Contains("dispositivos", StringComparison.OrdinalIgnoreCase)).Subject;
        linea.Should().Contain("Micrófono (USB Audio Device)").And.Contain("codec-in")
            .And.Contain("Altavoces (Lync USB Headset)").And.Contain("cascos-pc")
            .And.Contain("del equipo");

        // Buscar otra vez y encontrar lo mismo no repite la linea.
        m.Fonia.Ajustes.RefrescarDispositivos();
        m.Fonia.Ajustes.RefrescarDispositivos();
        m.Anotado.Textos.Count(t => t.Contains("dispositivos", StringComparison.OrdinalIgnoreCase)).Should().Be(1);

        // Elegir otro si se anota.
        m.Fonia.Ajustes.Altavoces = AltavocesPc;
        m.Anotado.Textos.Count(t => t.Contains("dispositivos", StringComparison.OrdinalIgnoreCase)).Should().Be(2);
        m.Anotado.Textos.Last(t => t.Contains("dispositivos", StringComparison.OrdinalIgnoreCase)).Should().Contain("altavoces-pc");
    }

    [Fact]
    public async Task Un_dispositivo_guardado_que_ya_no_esta_queda_anotado_como_aviso()
    {
        var m = await MontarAsync(
            entradas: [MicroPc],
            preparar: f => f.EntradaDelEquipo = "{0.0.1.00000000}.{codec-de-ayer}");

        m.Fonia.Ajustes.EntradaDelEquipo.Should().BeNull();
        var aviso = m.Anotado.Lineas.Should().ContainSingle(l => l.Level == LogEventLevel.Warning).Subject.RenderMessage();
        aviso.Should().Contain("{codec-de-ayer}", "se dice que identificador se pidio");
        aviso.Should().Contain("Micrófono (Lync USB Headset)", "y cuales hay");
    }

    [Fact]
    public async Task Si_al_arrancar_no_se_abre_la_escucha_el_registro_dice_por_que()
    {
        var sinPedir = await MontarAsync(escucharAlArrancar: false);
        await sinPedir.Fonia.EscucharSiSePidioAsync();
        sinPedir.Anotado.Textos.Should().Contain(t => t.Contains("no se abre la escucha al arrancar") && t.Contains("no está pedida"));

        var sinCodec = await MontarAsync(entradas: [MicroPc], escucharAlArrancar: true);
        await sinCodec.Fonia.EscucharSiSePidioAsync();
        sinCodec.Anotado.Lineas.Where(l => l.Level == LogEventLevel.Warning).Select(l => l.RenderMessage())
            .Should().Contain(t => t.Contains("no se abre la escucha al arrancar") && t.Contains("entrada del equipo"));
    }

    [Fact]
    public async Task Al_abrir_la_escucha_queda_anotado_de_donde_a_donde()
    {
        var m = await MontarAsync();

        await m.Fonia.AlternarEscuchaAsync();

        m.Anotado.Textos.Should().Contain(t =>
            t.Contains("Micrófono (USB Audio Device)") && t.Contains("Altavoces (Lync USB Headset)") && t.Contains("escucha", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Un_ptt_rechazado_queda_anotado_con_su_motivo()
    {
        var m = await MontarAsync("PKTUSB");

        await m.Fonia.PttAbajoAsync();

        m.Radio.Ptt.Should().BeFalse();
        m.Anotado.Textos.Should().Contain(t => t.Contains("PTT") && t.Contains("rechazado") && t.Contains(m.Fonia.MotivoDeNoPoder));
    }

    [Fact]
    public async Task El_motivo_de_no_poder_transmitir_se_anota_al_cambiar_y_no_en_cada_refresco()
    {
        var m = await MontarAsync();
        int Lineas() => m.Anotado.Textos.Count(t => t.Contains("PTT de fonía"));
        var alMontar = Lineas();

        // El refresco de los medidores (cada 100 ms) recalcula si se puede: no anota nada.
        for (var i = 0; i < 50; i++) m.Fonia.Refrescar();
        Lineas().Should().Be(alMontar);

        m.Radio.PonerModo("PKTUSB");
        await EsperaALaVentana.DrenarAsync();
        for (var i = 0; i < 50; i++) m.Fonia.Refrescar();

        Lineas().Should().Be(alMontar + 1, "una linea por cambio de estado");
        m.Anotado.Textos.Last(t => t.Contains("PTT de fonía")).Should().Contain(m.Fonia.MotivoDeNoPoder);

        m.Radio.PonerModo("USB");
        await EsperaALaVentana.DrenarAsync();
        Lineas().Should().Be(alMontar + 2);
        m.Anotado.Textos.Last(t => t.Contains("PTT de fonía")).Should().Contain("disponible");
    }
}
