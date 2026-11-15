using FluentAssertions;
using Nodisla.Cuaderno.Ui.Soporte;
using Xunit;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Si el cuaderno no se puede abrir, el arranque se corta ahi: no se crea la ventana.
/// </summary>
/// <remarks>
/// Antes se pedia el cierre con <c>Shutdown(1)</c> pero <c>OnStartup</c> seguia adelante y
/// creaba y mostraba la ventana principal sobre un cuaderno que no se habia podido abrir.
/// </remarks>
public class ArranqueDelCuadernoPruebas
{
    [Fact]
    public void Si_el_cuaderno_se_prepara_bien_el_arranque_sigue()
    {
        var avisos = 0;

        var sigue = ArranqueDelCuaderno.Intentar(() => { }, _ => avisos++);

        sigue.Should().BeTrue();
        avisos.Should().Be(0);
    }

    [Fact]
    public void Si_el_cuaderno_no_se_puede_abrir_se_avisa_una_vez_y_el_arranque_no_sigue()
    {
        var fallo = new InvalidOperationException("database disk image is malformed");
        var avisados = new List<Exception>();

        var sigue = ArranqueDelCuaderno.Intentar(() => throw fallo, avisados.Add);

        sigue.Should().BeFalse();
        avisados.Should().ContainSingle().Which.Should().BeSameAs(fallo);
    }
}
