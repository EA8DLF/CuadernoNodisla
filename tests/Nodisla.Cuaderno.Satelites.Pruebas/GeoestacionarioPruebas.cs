using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Catalogo;
using Nodisla.Cuaderno.Satelites.Orbital;
using Nodisla.Cuaderno.Satelites.Seguimiento;

namespace Nodisla.Cuaderno.Satelites.Pruebas;

/// <summary>
/// Apuntamiento a satelites geoestacionarios, que es lo que hace falta para QO-100.
/// </summary>
public sealed class GeoestacionarioPruebas
{
    private static readonly Observador Canarias =
        new(new Coordenada(28.4636, -16.2518)); // Santa Cruz de Tenerife

    [Fact]
    public void Qo100DesdeCanariasSaleDondeTieneQueSalir()
    {
        var vista = Geoestacionario.Mirar(Geoestacionario.LongitudQo100, Canarias);

        // Contrastado con la formula cerrada de apuntamiento a geoestacionarios, la de los
        // manuales de television por satelite:
        //   az = 180 − atan( tan(Δlon) / sin(lat) )   con el satelite al este
        //   el = atan( (cos γ − Re/r) / sin γ ),  cos γ = cos(lat)·cos(Δlon)
        // Con Δlon = 42,15° y lat = 28,46° da 117,8° y 33,4°.
        vista.AzimutGrados.Should().BeApproximately(117.8, 0.3);
        vista.ElevacionGrados.Should().BeApproximately(33.4, 0.3);

        // La distancia a un geoestacionario desde latitudes medias ronda los 38.000 km.
        vista.DistanciaKm.Should().BeApproximately(38310, 100);
    }

    [Fact]
    public void DesdeElEcuadorYEnSuMismaLongitudCaeEnElCenit()
    {
        var debajo = new Observador(new Coordenada(0.0, 25.9));

        var vista = Geoestacionario.Mirar(25.9, debajo);

        vista.ElevacionGrados.Should().BeApproximately(90.0, 0.01);
        vista.DistanciaKm.Should().BeApproximately(
            Geoestacionario.RadioOrbitaKm - 6378.137, 1.0, "la distancia es la altura sobre el ecuador");
    }

    [Fact]
    public void DesdeLasAntipodasNoSeVe()
    {
        // Justo al otro lado del planeta: el satelite queda debajo del horizonte.
        var lejos = new Observador(new Coordenada(0.0, 25.9 - 180.0));

        Geoestacionario.Mirar(25.9, lejos).ElevacionGrados.Should().BeLessThan(0);
        Geoestacionario.SeVe(25.9, lejos).Should().BeFalse();
        Geoestacionario.SeVe(25.9, Canarias).Should().BeTrue();
    }

    [Fact]
    public void ElGeoestacionarioNoSeMuevePorMuchoQuePaseElTiempo()
    {
        var seguidor = new SeguidorDeSatelites(new OpcionesDeSatelites { Observador = Canarias });

        var ahora = seguidor.Donde("QO-100", new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero));
        var doceHorasDespues = seguidor.Donde("QO-100", new DateTimeOffset(2026, 9, 27, 0, 0, 0, TimeSpan.Zero));

        ahora.Should().NotBeNull("QO-100 tiene que poder situarse sin elementos orbitales");
        doceHorasDespues.Should().NotBeNull();
        doceHorasDespues!.Vista.AzimutGrados.Should().Be(ahora!.Vista.AzimutGrados);
        doceHorasDespues.Vista.ElevacionGrados.Should().Be(ahora.Vista.ElevacionGrados);
        doceHorasDespues.Vista.VelocidadRadialKmS.Should().Be(0.0, "en QO-100 no se corrige Doppler");
    }

    [Fact]
    public void QoCienEstaEnElCatalogoConSusDosTranspondedores()
    {
        var qo = CatalogoDeSatelites.Instancia.PorAbreviatura("QO-100");

        qo.Should().NotBeNull();
        qo!.Transpondedores.Should().HaveCountGreaterThanOrEqualTo(2);
        Geoestacionario.LongitudDe(qo.Abreviatura).Should().Be(25.9);
    }

    [Fact]
    public void UnSateliteQueNoEsGeoestacionarioNoSeCuelaPorAqui()
    {
        Geoestacionario.LongitudDe("SO-50").Should().BeNull();
        Geoestacionario.LongitudDe(null).Should().BeNull();

        var seguidor = new SeguidorDeSatelites(new OpcionesDeSatelites { Observador = Canarias });
        seguidor.Donde("SO-50", DateTimeOffset.UtcNow)
            .Should().BeNull("sin elementos cargados no hay forma de situarlo");
    }
}
