using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Valores;

/// <summary>Pruebas del localizador Maidenhead.</summary>
public sealed class LocatorPruebas
{
    [Theory]
    [InlineData("IL", 1)]
    [InlineData("IL28", 2)]
    [InlineData("IL28HX", 3)]
    [InlineData("IL28HX12", 4)]
    [InlineData("IL28HX12AB", 5)]
    public void Se_aceptan_los_localizadores_de_dos_a_diez_caracteres(string texto, int precision)
    {
        Locator.TryParse(texto, out var l).Should().BeTrue();
        l.Valor.Should().Be(texto);
        l.Precision.Should().Be(precision);
    }

    [Theory]
    [InlineData("il28hx", "IL28HX")]
    [InlineData("  IN80  ", "IN80")]
    public void El_localizador_se_normaliza_a_mayusculas(string entrada, string esperado)
    {
        Locator.Parse(entrada).Valor.Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I")]            // longitud impar
    [InlineData("IL2")]          // longitud impar
    [InlineData("IL289")]        // longitud impar
    [InlineData("IL28HX12AB34")] // demasiado largo
    [InlineData("SL28")]         // el primer par no pasa de la R
    [InlineData("IS28")]         // el primer par no pasa de la R
    [InlineData("1L28")]         // el primer par es de letras
    [InlineData("ILAB")]         // el segundo par es de digitos
    [InlineData("IL28YX")]       // el tercer par no pasa de la X
    [InlineData("IL28H8")]       // el tercer par es de letras
    public void Se_rechaza_lo_que_no_es_un_localizador(string? texto)
    {
        Locator.TryParse(texto, out var l).Should().BeFalse();
        l.EsVacio.Should().BeTrue();
    }

    [Fact]
    public void Parse_se_queja_cuando_el_localizador_no_vale()
    {
        var leer = () => Locator.Parse("ZZ99");

        leer.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("IL", 25.0, -10.0)]          // campo entero
    [InlineData("IL28", 28.5, -15.0)]        // Gran Canaria
    [InlineData("IN80", 40.5, -3.0)]         // Madrid
    [InlineData("AA00", -89.5, -179.0)]        // esquina suroeste
    [InlineData("RR99", 89.5, 179.0)]        // esquina noreste
    [InlineData("JJ00", 0.5, 1.0)]           // junto al cruce del meridiano
    public void El_localizador_da_el_centro_de_su_cuadro(string texto, double lat, double lon)
    {
        var (latitud, longitud) = Locator.Parse(texto).ACoordenadas();

        latitud.Should().BeApproximately(lat, 0.001);
        longitud.Should().BeApproximately(lon, 0.001);
    }

    [Fact]
    public void Un_localizador_de_seis_afina_el_cuadro_de_cuatro()
    {
        var (lat4, lon4) = Locator.Parse("IL28").ACoordenadas();
        var (lat6, lon6) = Locator.Parse("IL28HX").ACoordenadas();

        // El cuadro de cuatro mide 2 grados de longitud por 1 de latitud.
        Math.Abs(lon6 - lon4).Should().BeLessThan(1.0);
        Math.Abs(lat6 - lat4).Should().BeLessThan(0.5);
    }

    [Fact]
    public void El_localizador_vacio_no_tiene_coordenadas()
    {
        var pedir = () => Locator.Vacio.ACoordenadas();

        pedir.Should().Throw<InvalidOperationException>();
        Locator.Vacio.Precision.Should().Be(0);
    }

    [Theory]
    [InlineData("IL28HX")]
    [InlineData("IN80DJ")]
    [InlineData("AA00AA")]
    [InlineData("RR99XX")]
    [InlineData("JJ00AA")]
    public void Ida_y_vuelta_a_coordenadas_devuelve_el_mismo_localizador(string texto)
    {
        var l = Locator.Parse(texto);
        var (lat, lon) = l.ACoordenadas();

        Locator.DesdeCoordenadas(lat, lon).Valor.Should().Be(texto);
    }

    [Theory]
    [InlineData("IL", 1)]
    [InlineData("IL28", 2)]
    [InlineData("IL28HX", 3)]
    [InlineData("IL28HX12", 4)]
    [InlineData("IL28HX12AB", 5)]
    [InlineData("AA00AA00AA", 5)]
    [InlineData("RR99XX99XX", 5)]
    public void La_ida_y_vuelta_aguanta_en_todas_las_precisiones(string texto, int precision)
    {
        var l = Locator.Parse(texto);
        var (lat, lon) = l.ACoordenadas();

        Locator.DesdeCoordenadas(lat, lon, precision).Valor.Should().Be(texto);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 6)]
    [InlineData(4, 8)]
    [InlineData(5, 10)]
    public void La_precision_pedida_fija_la_longitud_del_localizador(int precision, int caracteres)
    {
        var l = Locator.DesdeCoordenadas(28.0, -15.5, precision);

        l.Valor.Should().HaveLength(caracteres);
        l.Precision.Should().Be(precision);
        Locator.TryParse(l.Valor, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Una_precision_imposible_se_rechaza(int precision)
    {
        var pedir = () => Locator.DesdeCoordenadas(28.0, -15.5, precision);

        pedir.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(28.4636, -16.2518, "IL18")]   // Tenerife
    [InlineData(40.4168, -3.7038, "IN80")]    // Madrid
    [InlineData(0.0, 0.0, "JJ00")]            // cruce de ecuador y meridiano
    [InlineData(-33.8688, 151.2093, "QF56")]  // Sidney
    public void Las_coordenadas_conocidas_caen_en_su_localizador(double lat, double lon, string esperado)
    {
        Locator.DesdeCoordenadas(lat, lon, 2).Valor.Should().Be(esperado);
    }

    [Theory]
    [InlineData(95.0, 200.0)]
    [InlineData(-95.0, -200.0)]
    [InlineData(90.0, 180.0)]
    [InlineData(-90.0, -180.0)]
    public void Las_coordenadas_fuera_de_rango_se_recortan_sin_reventar(double lat, double lon)
    {
        var l = Locator.DesdeCoordenadas(lat, lon);

        Locator.TryParse(l.Valor, out _).Should().BeTrue();
    }

    [Fact]
    public void Dos_localizadores_con_el_mismo_texto_son_iguales()
    {
        Locator.Parse("il28hx").Should().Be(Locator.Parse("IL28HX"));
    }
}
