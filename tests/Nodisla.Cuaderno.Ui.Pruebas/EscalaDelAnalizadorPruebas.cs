using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.Conversores;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La escala y el rótulo bajo el analizador del visor dibujado.
/// </summary>
public sealed class EscalaDelAnalizadorPruebas
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private static object Marca(double anchoHz, string fraccion) =>
        new EscalaDelAnalizador().Convert(
            [anchoHz, "21.0733", true, "18.1", "USB", "USB", 3000],
            typeof(string), fraccion, Es);

    [Theory]
    [InlineData("0.1", "-80k")]
    [InlineData("0.3", "-40k")]
    [InlineData("0.5", "21.073.300")]
    [InlineData("0.7", "+40k")]
    [InlineData("0.9", "+80k")]
    public void Con_el_espectro_de_la_radio_la_escala_es_la_del_equipo(string fraccion, string esperado) =>
        Marca(200_000, fraccion).Should().Be(esperado);

    [Fact]
    public void Sin_espectro_de_la_radio_la_escala_dice_la_frecuencia_del_audio() =>
        // Banda lateral superior: el centro de 3 kHz de audio está 1,5 kHz por encima del dial.
        Marca(0, "0.5").Should().Be((21_074_800 / 1000.0).ToString("N1", CultureInfo.CurrentCulture));

    [Fact]
    public void El_rotulo_es_el_que_pone_el_analizador_y_si_no_el_del_audio()
    {
        var rotulo = new RotuloDelAnalizador();
        rotulo.Convert(["CENTER FAST1 SPAN 200kHz", 3000], typeof(string), null, Es)
            .Should().Be("CENTER FAST1 SPAN 200kHz");
        ((string)rotulo.Convert([string.Empty, 3000], typeof(string), null, Es))
            .Should().StartWith("AUDIO RX").And.Contain("SPAN");
    }
}
