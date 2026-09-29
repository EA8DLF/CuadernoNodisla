using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Valores;

/// <summary>Pruebas de distancias y rumbos de circulo maximo.</summary>
public sealed class GeodesiaPruebas
{
    private static readonly Coordenada Tenerife = new(28.4636, -16.2518);
    private static readonly Coordenada Madrid = new(40.4168, -3.7038);
    private static readonly Coordenada NuevaYork = new(40.7128, -74.0060);
    private static readonly Coordenada Tokio = new(35.6895, 139.6917);
    private static readonly Coordenada Sidney = new(-33.8688, 151.2093);

    [Fact]
    public void De_Tenerife_a_Madrid_hay_unos_mil_setecientos_cincuenta_kilometros()
    {
        Geodesia.DistanciaKm(Tenerife, Madrid).Should().BeApproximately(1754.3, 5.0);
    }

    [Theory]
    [InlineData(5360.6)]
    public void De_Tenerife_a_Nueva_York_hay_unos_cinco_mil_trescientos_sesenta(double km)
    {
        Geodesia.DistanciaKm(Tenerife, NuevaYork).Should().BeApproximately(km, 10.0);
    }

    [Fact]
    public void De_Tenerife_a_Tokio_y_a_Sidney_las_distancias_son_las_conocidas()
    {
        Geodesia.DistanciaKm(Tenerife, Tokio).Should().BeApproximately(12449.3, 20.0);
        Geodesia.DistanciaKm(Tenerife, Sidney).Should().BeApproximately(18680.6, 30.0);
    }

    [Fact]
    public void La_distancia_es_la_misma_en_los_dos_sentidos()
    {
        Geodesia.DistanciaKm(Tenerife, Madrid)
            .Should().BeApproximately(Geodesia.DistanciaKm(Madrid, Tenerife), 0.0001);
    }

    [Fact]
    public void Del_mismo_punto_al_mismo_punto_no_hay_distancia()
    {
        Geodesia.DistanciaKm(Tenerife, Tenerife).Should().Be(0.0);
    }

    [Fact]
    public void Entre_antipodas_esta_media_vuelta_al_mundo()
    {
        var antipoda = Geodesia.Antipoda(Tenerife);
        var mediaVuelta = Math.PI * Geodesia.RadioTerrestreKm;

        Geodesia.DistanciaKm(Tenerife, antipoda).Should().BeApproximately(mediaVuelta, 0.001);
    }

    [Fact]
    public void Ninguna_distancia_pasa_de_media_vuelta_al_mundo()
    {
        var mediaVuelta = Math.PI * Geodesia.RadioTerrestreKm;

        foreach (var a in new[] { Tenerife, Madrid, NuevaYork, Tokio, Sidney })
        {
            foreach (var b in new[] { Tenerife, Madrid, NuevaYork, Tokio, Sidney })
            {
                Geodesia.DistanciaKm(a, b).Should().BeLessThanOrEqualTo(mediaVuelta + 0.001);
            }
        }
    }

    [Fact]
    public void Cruzar_el_antimeridiano_son_dos_grados_no_trescientos_cincuenta_y_ocho()
    {
        var oeste = new Coordenada(0, 179);
        var este = new Coordenada(0, -179);

        Geodesia.DistanciaKm(oeste, este).Should().BeApproximately(222.4, 0.5);
        Geodesia.RumboGrados(oeste, este).Should().BeApproximately(90.0, 0.001);
    }

    [Fact]
    public void Los_polos_estan_a_la_distancia_que_les_toca()
    {
        var norte = new Coordenada(90, 0);
        var sur = new Coordenada(-90, 0);
        var ecuador = new Coordenada(0, 0);

        Geodesia.DistanciaKm(norte, sur)
            .Should().BeApproximately(Math.PI * Geodesia.RadioTerrestreKm, 0.001);
        Geodesia.DistanciaKm(ecuador, norte)
            .Should().BeApproximately(Math.PI * Geodesia.RadioTerrestreKm / 2, 0.001);
    }

    [Theory]
    [InlineData(0, 0, 10, 0, 0.0)]        // al norte
    [InlineData(0, 0, 0, 10, 90.0)]       // al este
    [InlineData(0, 0, -10, 0, 180.0)]     // al sur
    [InlineData(0, 0, 0, -10, 270.0)]     // al oeste
    public void El_rumbo_apunta_a_donde_debe(
        double lat1, double lon1, double lat2, double lon2, double esperado)
    {
        var rumbo = Geodesia.RumboGrados(new Coordenada(lat1, lon1), new Coordenada(lat2, lon2));

        rumbo.Should().BeApproximately(esperado, 0.001);
    }

    [Fact]
    public void De_Tenerife_a_Madrid_se_apunta_al_nordeste_y_a_la_vuelta_al_suroeste()
    {
        Geodesia.RumboGrados(Tenerife, Madrid).Should().BeApproximately(37.5, 0.5);
        Geodesia.RumboGrados(Madrid, Tenerife).Should().BeApproximately(224.6, 0.5);
    }

    [Fact]
    public void El_rumbo_siempre_cae_entre_cero_y_trescientos_sesenta()
    {
        foreach (var a in new[] { Tenerife, Madrid, NuevaYork, Tokio, Sidney })
        {
            foreach (var b in new[] { Tenerife, Madrid, NuevaYork, Tokio, Sidney })
            {
                var rumbo = Geodesia.RumboGrados(a, b);
                rumbo.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThan(360.0);
            }
        }
    }

    [Fact]
    public void El_rumbo_largo_es_el_contrario_del_corto()
    {
        var corto = Geodesia.RumboGrados(Tenerife, Tokio);
        var largo = Geodesia.RumboLargoGrados(Tenerife, Tokio);

        ((largo - corto + 360) % 360).Should().BeApproximately(180.0, 0.001);
        largo.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThan(360.0);
    }

    [Theory]
    [InlineData(28.4636, -16.2518, -28.4636, 163.7482)]
    [InlineData(0, 0, 0, 180)]
    [InlineData(0, 180, 0, 0)]
    [InlineData(-33.8688, 151.2093, 33.8688, -28.7907)]
    public void La_antipoda_esta_al_otro_lado_del_mundo(
        double lat, double lon, double latEsperada, double lonEsperada)
    {
        var a = Geodesia.Antipoda(new Coordenada(lat, lon));

        a.Latitud.Should().BeApproximately(latEsperada, 0.0001);
        a.Longitud.Should().BeApproximately(lonEsperada, 0.0001);
    }

    [Fact]
    public void La_antipoda_de_la_antipoda_es_el_punto_de_partida()
    {
        var vuelta = Geodesia.Antipoda(Geodesia.Antipoda(Tenerife));

        vuelta.Latitud.Should().BeApproximately(Tenerife.Latitud, 0.0001);
        vuelta.Longitud.Should().BeApproximately(Tenerife.Longitud, 0.0001);
    }

    [Fact]
    public void La_coordenada_va_y_viene_del_localizador()
    {
        var c = Coordenada.Desde(Locator.Parse("IL28HX"));

        c.Latitud.Should().BeApproximately(28.979, 0.01);
        c.Longitud.Should().BeApproximately(-15.375, 0.01);
        c.ALocator().Valor.Should().Be("IL28HX");
    }

    [Fact]
    public void La_distancia_entre_dos_localizadores_vecinos_es_pequena()
    {
        var a = Coordenada.Desde(Locator.Parse("IL28HX"));
        var b = Coordenada.Desde(Locator.Parse("IL28HW"));

        Geodesia.DistanciaKm(a, b).Should().BeLessThan(10.0).And.BeGreaterThan(0.0);
    }
}
