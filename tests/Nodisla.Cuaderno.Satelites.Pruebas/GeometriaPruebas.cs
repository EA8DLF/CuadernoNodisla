using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Doppler;
using Nodisla.Cuaderno.Satelites.Orbital;

namespace Nodisla.Cuaderno.Satelites.Pruebas;

/// <summary>Tiempo sidereo, geometria del observador y correccion Doppler.</summary>
public sealed class GeometriaPruebas
{
    [Fact]
    public void ElTiempoSidereoCuadraConElValorPublicadoParaJ2000()
    {
        // Contraste independiente: el tiempo sidéreo medio de Greenwich en J2000,0
        // (día juliano 2451545,0) vale 280,46061837°, que es la constante que publica la IAU
        // y de la que se derivan todas las tablas. Si esto se desvía, el azimut sale mal en
        // todos los pasos y no hay forma de darse cuenta mirando el programa.
        var radianes = Tiempos.TiempoSidereoGreenwich(2451545.0);
        var grados = radianes * Tiempos.RadianesAGrados;

        grados.Should().BeApproximately(280.46061837, 1e-6);
    }

    [Fact]
    public void ElDiaJulianoCuadraConLasFechasDeReferencia()
    {
        Tiempos.DiaJuliano(new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero))
            .Should().BeApproximately(2451545.0, 1e-9);
        Tiempos.DiaJuliano(new DateTimeOffset(1858, 11, 17, 0, 0, 0, TimeSpan.Zero))
            .Should().BeApproximately(2400000.5, 1e-9);
    }

    [Fact]
    public void ElDiaJulianoYSuInversaSeDeshacenMutuamente()
    {
        var instante = new DateTimeOffset(2026, 9, 25, 7, 42, 13, TimeSpan.Zero);
        var vuelta = Tiempos.DesdeDiaJuliano(Tiempos.DiaJuliano(instante));

        (vuelta - instante).Duration().Should().BeLessThan(TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public void ElObservadorSeSitueDondeDiceSuLocalizador()
    {
        // IL18: Gran Canaria. El elipsoide deja el radio geocéntrico por debajo del radio
        // ecuatorial, nunca por encima.
        var observador = Observador.DesdeLocator(Locator.Parse("IL18"), 100);
        var r = observador.PosicionFija();

        r.Modulo.Should().BeInRange(6350.0, 6379.0);
        observador.Coordenada.Latitud.Should().BeInRange(27.0, 29.0);
        observador.Coordenada.Longitud.Should().BeInRange(-17.0, -15.0);
    }

    [Fact]
    public void LaAlturaDelObservadorSeNotaEnSuRadio()
    {
        var suelo = Observador.DesdeLocator(Locator.Parse("IL18"));
        var alto = Observador.DesdeLocator(Locator.Parse("IL18"), 1949);

        (alto.PosicionFija().Modulo - suelo.PosicionFija().Modulo)
            .Should().BeApproximately(1.949, 1e-3);
    }

    [Fact]
    public void LaBajadaSubeDeFrecuenciaCuandoElSateliteSeAcerca()
    {
        var nominal = Frecuencia.DesdeMegahercios(436.795m);

        var acercandose = CorreccionDoppler.Bajada(nominal, -7.5);
        var alejandose = CorreccionDoppler.Bajada(nominal, 7.5);

        acercandose.Should().BeGreaterThan(nominal);
        alejandose.Should().BeLessThan(nominal);

        // 7,5 km/s sobre 436,795 MHz son casi once kilohercios, que es lo que se ve de verdad
        // en un paso de órbita baja en 70 cm.
        (acercandose.Hercios - nominal.Hercios).Should().BeCloseTo(10926, 5);
    }

    [Fact]
    public void LaSubidaVaAlReves()
    {
        // El error clásico: aplicar el mismo signo a las dos. Si el satélite se acerca hay que
        // transmitir MÁS ABAJO para que él reciba su frecuencia nominal.
        var nominal = Frecuencia.DesdeMegahercios(145.850m);

        CorreccionDoppler.Subida(nominal, -7.5).Should().BeLessThan(nominal);
        CorreccionDoppler.Subida(nominal, 7.5).Should().BeGreaterThan(nominal);
    }

    [Fact]
    public void SinVelocidadRadialNoHayCorreccion()
    {
        var nominal = Frecuencia.DesdeMegahercios(145.960m);

        CorreccionDoppler.Bajada(nominal, 0).Should().Be(nominal);
        CorreccionDoppler.Subida(nominal, 0).Should().Be(nominal);
    }

    [Fact]
    public void CorregirDevuelveLasDosFrecuenciasYLoQueSeHanMovido()
    {
        var vista = new VistaDesdeTierra(180, 30, 900, -6.0);
        var sintonia = CorreccionDoppler.Corregir(
            Frecuencia.DesdeMegahercios(435.250m),
            Frecuencia.DesdeMegahercios(145.960m),
            vista);

        sintonia.SeAcerca.Should().BeTrue();
        sintonia.DesplazamientoBajadaHz.Should().BePositive();
        sintonia.DesplazamientoSubidaHz.Should().BeNegative();

        // La bajada está en 2 m y la subida en 70 cm: el desplazamiento en 70 cm es tres veces
        // mayor en valor absoluto, porque el Doppler es proporcional a la frecuencia.
        Math.Abs(sintonia.DesplazamientoSubidaHz)
            .Should().BeGreaterThan(Math.Abs(sintonia.DesplazamientoBajadaHz) * 2.5);
    }
}
