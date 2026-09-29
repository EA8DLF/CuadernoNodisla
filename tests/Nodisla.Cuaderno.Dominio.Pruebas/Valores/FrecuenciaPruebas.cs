using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Valores;

/// <summary>Pruebas de la frecuencia, que en ADIF viaja en megahercios.</summary>
public sealed class FrecuenciaPruebas
{
    [Fact]
    public void La_frecuencia_cero_es_la_de_por_defecto()
    {
        Frecuencia.Cero.EsCero.Should().BeTrue();
        Frecuencia.Cero.Megahercios.Should().Be(0m);
        default(Frecuencia).Should().Be(Frecuencia.Cero);
    }

    [Theory]
    [InlineData(14.074, 14074.0, 14074000L)]
    [InlineData(7.0, 7000.0, 7000000L)]
    [InlineData(0.136, 136.0, 136000L)]
    [InlineData(1296.2, 1296200.0, 1296200000L)]
    public void Las_unidades_se_convierten_sin_perder_nada(double mhz, double khz, long hz)
    {
        var f = Frecuencia.DesdeMegahercios((decimal)mhz);

        f.Megahercios.Should().Be((decimal)mhz);
        f.Kilohercios.Should().Be((decimal)khz);
        f.Hercios.Should().Be(hz);
    }

    [Fact]
    public void Se_puede_construir_desde_hercios_y_desde_kilohercios()
    {
        Frecuencia.DesdeHercios(14_074_000).Megahercios.Should().Be(14.074m);
        Frecuencia.DesdeKilohercios(14_074m).Megahercios.Should().Be(14.074m);
        Frecuencia.DesdeHercios(1).Megahercios.Should().Be(0.000001m);
    }

    [Fact]
    public void Una_frecuencia_negativa_no_existe()
    {
        var crear = () => Frecuencia.DesdeMegahercios(-1m);

        crear.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void La_frecuencia_se_redondea_al_hercio()
    {
        // Siete decimales de MHz son decimas de hercio: no aportan nada y ensucian el ADIF.
        Frecuencia.DesdeMegahercios(14.0740004m).Megahercios.Should().Be(14.074m);
    }

    [Theory]
    [InlineData("14.074", 14.074)]
    [InlineData("7", 7.0)]
    [InlineData(" 50.313 ", 50.313)]
    [InlineData("0.136", 0.136)]
    public void Se_lee_la_frecuencia_del_ADIF(string texto, double esperado)
    {
        Frecuencia.TryParseAdif(texto, out var f).Should().BeTrue();
        f.Megahercios.Should().Be((decimal)esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("catorce")]
    [InlineData("14,074")]   // la coma decimal no es de ADIF
    [InlineData("-14.074")]
    public void Se_rechaza_lo_que_no_es_una_frecuencia(string? texto)
    {
        Frecuencia.TryParseAdif(texto, out var f).Should().BeFalse();
        f.EsCero.Should().BeTrue();
    }

    [Theory]
    [InlineData(14.074, "14.074")]
    [InlineData(7.0, "7")]
    [InlineData(0.136, "0.136")]
    [InlineData(144.3, "144.3")]
    public void La_frecuencia_se_escribe_como_la_espera_ADIF(double mhz, string esperado)
    {
        Frecuencia.DesdeMegahercios((decimal)mhz).AAdif().Should().Be(esperado);
    }

    [Fact]
    public void Las_frecuencias_se_comparan_entre_si()
    {
        var baja = Frecuencia.DesdeMegahercios(7.1m);
        var alta = Frecuencia.DesdeMegahercios(14.2m);
        var otraBaja = Frecuencia.DesdeKilohercios(7100m);

        (baja < alta).Should().BeTrue();
        (alta > baja).Should().BeTrue();
        (baja <= otraBaja).Should().BeTrue();
        (baja >= otraBaja).Should().BeTrue();
        baja.CompareTo(alta).Should().BeNegative();
        alta.CompareTo(baja).Should().BePositive();
        baja.CompareTo(baja).Should().Be(0);
    }

    [Fact]
    public void Ida_y_vuelta_por_el_texto_ADIF_no_cambia_la_frecuencia()
    {
        var original = Frecuencia.DesdeMegahercios(50.313m);

        Frecuencia.TryParseAdif(original.AAdif(), out var vuelta).Should().BeTrue();
        vuelta.Should().Be(original);
    }

    [Fact]
    public void La_frecuencia_se_muestra_con_su_unidad()
    {
        Frecuencia.DesdeMegahercios(14.074m).ToString().Should().Be("14.074 MHz");
    }
}
