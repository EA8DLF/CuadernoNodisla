using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Valores;

/// <summary>Pruebas de la banda ADIF y de su deduccion a partir de la frecuencia.</summary>
public sealed class BandaPruebas
{
    /// <summary>Las 33 bandas de la tabla <c>Band</c> de ADIF 3.1.5, de la mas baja a la mas alta.</summary>
    private static readonly string[] BandasAdif =
    [
        "2190m", "630m", "560m", "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m",
        "12m", "10m", "8m", "6m", "5m", "4m", "2m", "1.25m", "70cm", "33cm", "23cm", "13cm",
        "9cm", "6cm", "3cm", "1.25cm", "6mm", "4mm", "2.5mm", "2mm", "1mm", "submm",
    ];

    [Fact]
    public void Estan_todas_las_bandas_de_ADIF_y_en_orden()
    {
        Banda.Todas.Select(b => b.Nombre).Should().Equal(BandasAdif);
    }

    [Fact]
    public void Cada_banda_de_ADIF_se_reconoce_por_su_nombre()
    {
        foreach (var nombre in BandasAdif)
        {
            Banda.TryParse(nombre, out var b).Should().BeTrue($"{nombre} es una banda de ADIF");
            b.Nombre.Should().Be(nombre);
        }
    }

    [Theory]
    [InlineData("20M", "20m")]
    [InlineData("  40m  ", "40m")]
    [InlineData("70CM", "70cm")]
    public void El_nombre_de_la_banda_no_distingue_mayusculas(string entrada, string esperado)
    {
        Banda.Parse(entrada).Nombre.Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("21m")]
    [InlineData("11m")]      // la banda ciudadana no es de radioaficionado
    [InlineData("20 m")]
    public void Se_rechaza_lo_que_no_es_una_banda(string? texto)
    {
        Banda.TryParse(texto, out var b).Should().BeFalse();
        b.EsVacia.Should().BeTrue();
    }

    [Fact]
    public void Parse_se_queja_cuando_la_banda_no_existe()
    {
        var leer = () => Banda.Parse("21m");

        leer.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData(1.840, "160m")]
    [InlineData(3.573, "80m")]
    [InlineData(5.357, "60m")]
    [InlineData(7.074, "40m")]
    [InlineData(10.136, "30m")]
    [InlineData(14.074, "20m")]
    [InlineData(18.100, "17m")]
    [InlineData(21.074, "15m")]
    [InlineData(24.915, "12m")]
    [InlineData(28.074, "10m")]
    [InlineData(50.313, "6m")]
    [InlineData(70.200, "4m")]
    [InlineData(144.174, "2m")]
    [InlineData(432.200, "70cm")]
    [InlineData(1296.200, "23cm")]
    [InlineData(10368.100, "3cm")]
    public void La_frecuencia_dice_en_que_banda_se_esta(double mhz, string banda)
    {
        Banda.DesdeFrecuencia(Frecuencia.DesdeMegahercios((decimal)mhz)).Nombre.Should().Be(banda);
    }

    [Fact]
    public void Los_extremos_de_cada_banda_pertenecen_a_la_banda()
    {
        foreach (var banda in Banda.Todas)
        {
            var (inferior, superior) = banda.Limite;

            banda.Contiene(Frecuencia.DesdeMegahercios(inferior)).Should().BeTrue($"{banda} empieza ahi");
            banda.Contiene(Frecuencia.DesdeMegahercios(superior)).Should().BeTrue($"{banda} acaba ahi");
        }
    }

    [Theory]
    [InlineData(14.0, "20m")]
    [InlineData(14.35, "20m")]
    [InlineData(7.0, "40m")]
    [InlineData(7.3, "40m")]
    public void Justo_en_el_borde_se_sigue_estando_dentro(double mhz, string banda)
    {
        Banda.DesdeFrecuencia(Frecuencia.DesdeMegahercios((decimal)mhz)).Nombre.Should().Be(banda);
    }

    [Theory]
    [InlineData(13.999)]
    [InlineData(14.351)]
    [InlineData(0.1)]
    [InlineData(100.0)]
    [InlineData(200.0)]
    public void Una_frecuencia_fuera_de_bandas_no_tiene_banda(double mhz)
    {
        Banda.DesdeFrecuencia(Frecuencia.DesdeMegahercios((decimal)mhz)).EsVacia.Should().BeTrue();
    }

    [Fact]
    public void La_frecuencia_cero_no_tiene_banda()
    {
        Banda.DesdeFrecuencia(Frecuencia.Cero).EsVacia.Should().BeTrue();
    }

    [Fact]
    public void Las_bandas_no_se_pisan_entre_si()
    {
        // Si dos bandas compartieran frecuencias, deducir la banda seria una loteria.
        var limites = Banda.Todas.Select(b => (b.Nombre, b.Limite)).ToArray();

        for (var i = 1; i < limites.Length; i++)
        {
            limites[i].Limite.Inferior.Should().BeGreaterThan(limites[i - 1].Limite.Superior,
                $"{limites[i].Nombre} empieza despues de que acabe {limites[i - 1].Nombre}");
        }
    }

    [Fact]
    public void La_banda_vacia_no_contiene_nada_ni_tiene_limites()
    {
        Banda.Vacia.EsVacia.Should().BeTrue();
        Banda.Vacia.Contiene(Frecuencia.DesdeMegahercios(14.074m)).Should().BeFalse();

        var pedir = () => Banda.Vacia.Limite;
        pedir.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Una_banda_no_contiene_la_frecuencia_cero()
    {
        Banda.Parse("20m").Contiene(Frecuencia.Cero).Should().BeFalse();
    }

    [Fact]
    public void La_banda_se_convierte_a_texto_solo()
    {
        var b = Banda.Parse("20m");

        string texto = b;
        texto.Should().Be("20m");
        b.ToString().Should().Be("20m");
    }
}
