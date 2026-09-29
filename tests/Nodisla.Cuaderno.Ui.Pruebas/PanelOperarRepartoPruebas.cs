using FluentAssertions;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>Reparto del alto en Operar: contacto + cluster primero, el frontal con lo que sobre.</summary>
public class PanelOperarRepartoPruebas
{
    [Theory]
    [InlineData(620, 50, 0)]     // 1366×768: no cabe legible, se pliega solo
    [InlineData(850, 50, 356)]   // 1920×1000: encoge para dejar sitio abajo
    [InlineData(1100, 50, 370)]  // pantalla alta: su tope
    public void ElFrontalCedeElAlto(double panel, double arriba, double esperado) =>
        PanelOperar.AltoDelFrontal(panel, arriba).Should().BeApproximately(esperado, 0.5);

    [Fact]
    public void LoDeAbajoConservaSuMinimo()
    {
        for (var panel = 400.0; panel < 1400; panel += 7)
        {
            var frontal = PanelOperar.AltoDelFrontal(panel, 50);
            if (frontal > 0)
            {
                frontal.Should().BeGreaterThanOrEqualTo(PanelOperar.AltoMinimoDelFrontal);
                (panel - 50 - frontal).Should().BeGreaterThanOrEqualTo(PanelOperar.AltoMinimoDeAbajo);
            }
        }
    }
}
