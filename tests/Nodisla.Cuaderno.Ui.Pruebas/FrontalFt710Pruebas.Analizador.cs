using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Las teclas de la pantalla (CENTER, 3DSS, EXPAND, SPAN, SPEED) cambian el DIBUJO del programa
/// igual que cambian la radio: rotulo, escala, vista 3DSS, ampliado y velocidad.
/// </summary>
public sealed partial class FrontalFt710Pruebas
{
    [Theory]
    [InlineData("CENTER", "CURSOR  SPEED FAST1  SPAN 200kHz", 200_000, false, false, 2)]
    [InlineData("3DSS", "CENTER  SPEED FAST1  SPAN 200kHz", 200_000, true, false, 2)]
    [InlineData("EXPAND", "CENTER  SPEED FAST1  SPAN 200kHz", 200_000, false, true, 2)]
    [InlineData("SPAN+", "CENTER  SPEED FAST1  SPAN 500kHz", 500_000, false, false, 2)]
    [InlineData("SPAN-", "CENTER  SPEED FAST1  SPAN 100kHz", 100_000, false, false, 2)]
    [InlineData("SPEED+", "CENTER  SPEED FAST2  SPAN 200kHz", 200_000, false, false, 3)]
    [InlineData("SPEED-", "CENTER  SPEED SLOW2  SPAN 200kHz", 200_000, false, false, 1)]
    public async Task Cada_tecla_de_la_pantalla_cambia_tambien_el_dibujo(
        string tecla, string rotulo, double span, bool tresD, bool ampliado, int velocidad)
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        var dibujo = new VistaModeloAnalizador(null);
        dibujo.Seguir(equipo);
        dibujo.Rotulo.Should().Be("CENTER  SPEED FAST1  SPAN 200kHz", "es lo que tenia la radio al conectar");

        equipo.TeclaDelAnalizadorCommand.Execute(tecla);

        // Al momento, sin esperar a la radio ni a la trama.
        dibujo.Rotulo.Should().Be(rotulo);
        dibujo.SpanHz.Should().Be(span);
        dibujo.EnTresD.Should().Be(tresD);
        dibujo.Ampliado.Should().Be(ampliado);
        dibujo.Velocidad.Should().Be(velocidad);
        canal.Mandadas.Should().NotContain(o => o.StartsWith("TX1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lo_que_se_cambia_en_la_radio_tambien_cambia_el_dibujo()
    {
        var (canal, equipo, conmutable) = await MontarAsync();
        await using var _ = conmutable;
        var dibujo = new VistaModeloAnalizador(null);
        dibujo.Seguir(equipo);

        // Como si el operador tocara la pantalla de la radio: SPAN 100 kHz, SPEED SLOW1 y FIX ampliado.
        canal.Poner("SS05;", "SS0560000;");
        canal.Poner("SS00;", "SS0000000;");
        canal.Poner("SS06;", "SS06B0000;");
        await equipo.RefrescarElFrontalAsync();
        await EsperaALaVentana.DrenarAsync();

        dibujo.Posicion.Should().Be(ModoDelAnalizador.Fijo);
        dibujo.Ampliado.Should().BeTrue("SS06B es W/F FIX EXPAND");
        dibujo.SpanHz.Should().Be(100_000);
        dibujo.Velocidad.Should().Be(0);
        dibujo.Rotulo.Should().Be("FIX  SPEED SLOW1  SPAN 100kHz", "sin trama de FIX no se sabe donde empieza");
    }
}
