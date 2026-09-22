using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion.Geometria;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>Geometria del trayecto, paso gris y los casos que rompen las formulas ingenuas.</summary>
public class GeometriaDeTrayectoPruebas
{
    private static readonly Coordenada Tenerife = new(28.4636, -16.2518);
    private static readonly Coordenada Tokio = new(35.6895, 139.6917);

    [Fact]
    public void Canarias_japon_por_el_camino_corto_va_hacia_el_norte()
    {
        var trayecto = GeometriaDeTrayecto.Calcular(Tenerife, Tokio, Momento("2026-09-22T12:00:00Z"));

        trayecto.DistanciaKm.Should().BeApproximately(12449.3, 5.0);
        trayecto.RumboCorto.Should().BeApproximately(20.9, 0.5);
        trayecto.RumboLargo.Should().BeApproximately(200.9, 0.5);
    }

    [Fact]
    public void El_camino_corto_pasa_cerca_del_polo_y_el_largo_por_el_sur()
    {
        var mitadCorta = GeometriaDeTrayecto.PuntoIntermedio(Tenerife, Tokio, 0.5);
        var antipoda = Geodesia.Antipoda(GeometriaDeTrayecto.PuntoIntermedio(Tenerife, Tokio, 0.5));

        // El camino corto entre Canarias y Japon sube por encima del paralelo 60.
        mitadCorta.Latitud.Should().BeGreaterThan(60.0);
        // El camino largo es el opuesto: su punto medio esta en el hemisferio sur.
        antipoda.Latitud.Should().BeLessThan(-60.0);
    }

    [Fact]
    public void El_mismo_punto_como_origen_y_destino_no_revienta_nada()
    {
        var trayecto = GeometriaDeTrayecto.Calcular(Tenerife, Tenerife, Momento("2026-09-22T12:00:00Z"));

        trayecto.DistanciaKm.Should().BeApproximately(0.0, 1e-6);
        trayecto.RumboCorto.Should().Be(0.0);
        trayecto.RumboLargo.Should().Be(180.0);
        trayecto.EnPasoGris.Should().BeFalse();

        GeometriaDeTrayecto.PuntoIntermedio(Tenerife, Tenerife, 0.5).Should().Be(Tenerife);
    }

    [Fact]
    public void El_antimeridiano_no_es_un_caso_especial()
    {
        var oeste = new Coordenada(0.0, 170.0);
        var este = new Coordenada(0.0, -170.0);

        var medio = GeometriaDeTrayecto.PuntoIntermedio(oeste, este, 0.5);

        medio.Latitud.Should().BeApproximately(0.0, 1e-6);
        Math.Abs(medio.Longitud).Should().BeApproximately(180.0, 1e-6);
        Geodesia.DistanciaKm(oeste, este).Should().BeApproximately(2223.9, 5.0);
    }

    [Fact]
    public void Hay_paso_gris_cuando_el_terminador_pasa_por_los_dos_extremos()
    {
        // En el equinoccio la linea del dia y la noche es un meridiano: dos puntos en la misma
        // longitud amanecen a la misma hora, sea cual sea su latitud.
        var norte = new Coordenada(50.0, -16.2518);
        var amanecerEnCanarias = Momento("2026-03-21T07:06:00Z");

        var trayecto = GeometriaDeTrayecto.Calcular(Tenerife, norte, amanecerEnCanarias);

        trayecto.EnPasoGris.Should().BeTrue();
    }

    [Fact]
    public void A_mediodia_no_hay_paso_gris_aunque_el_camino_corte_el_terminador()
    {
        var analisis = GeometriaDeTrayecto.Analizar(Tenerife, Tokio, Momento("2026-03-21T13:12:00Z"));

        analisis.EnPasoGris.Should().BeFalse();
        // El camino si cruza la linea del dia y la noche: en Canarias es mediodia y en Japon, noche.
        analisis.CruzaElTerminador.Should().BeTrue();
        analisis.FraccionEnOscuridad.Should().BeInRange(0.05, 0.95);
    }

    [Fact]
    public void El_trayecto_lleva_el_orto_y_el_ocaso_del_destino()
    {
        var trayecto = GeometriaDeTrayecto.Calcular(Tenerife, Tokio, Momento("2026-03-21T12:00:00Z"));

        trayecto.AmaneceEnDestino.Should().NotBeNull();
        trayecto.AnocheceEnDestino.Should().NotBeNull();
        trayecto.AmaneceEnDestino!.Value.Should().BeCloseTo(
            Momento("2026-03-20T20:42:30Z"),
            TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void Con_el_destino_en_dia_polar_el_orto_y_el_ocaso_vienen_nulos_y_se_dice_por_que()
    {
        var polar = new Coordenada(80.0, 0.0);

        var trayecto = GeometriaDeTrayecto.Calcular(Tenerife, polar, Momento("2026-06-21T12:00:00Z"));

        trayecto.AmaneceEnDestino.Should().BeNull();
        trayecto.AnocheceEnDestino.Should().BeNull();
        trayecto.MotivoSinEventos.Should().Be(MotivoSinEventoSolar.DiaPolar);
        trayecto.DistanciaKm.Should().BeGreaterThan(5000.0);
    }

    [Fact]
    public void La_noche_polar_no_se_confunde_con_el_dia_polar()
    {
        var polar = new Coordenada(80.0, 0.0);

        var invierno = GeometriaDeTrayecto.Calcular(Tenerife, polar, Momento("2026-12-21T12:00:00Z"));

        invierno.AmaneceEnDestino.Should().BeNull();
        invierno.AnocheceEnDestino.Should().BeNull();
        invierno.MotivoSinEventos.Should().Be(MotivoSinEventoSolar.NochePolar);
    }

    [Fact]
    public void Cuando_hay_orto_y_ocaso_no_hay_motivo_que_dar()
    {
        GeometriaDeTrayecto.Calcular(Tenerife, Tokio, Momento("2026-03-21T12:00:00Z"))
            .MotivoSinEventos.Should().Be(MotivoSinEventoSolar.NoAplica);
    }

    [Fact]
    public void De_noche_cerrada_todo_el_camino_va_a_oscuras()
    {
        var cercano = new Coordenada(30.0, -16.0);

        var analisis = GeometriaDeTrayecto.Analizar(Tenerife, cercano, Momento("2026-03-21T02:00:00Z"));

        analisis.FraccionEnOscuridad.Should().Be(1.0);
        analisis.CruzaElTerminador.Should().BeFalse();
        analisis.EnPasoGris.Should().BeFalse();
    }

    [Fact]
    public void Los_puntos_de_control_reparten_el_camino_en_saltos()
    {
        var controles = GeometriaDeTrayecto.PuntosDeControl(Tenerife, Tokio, 4);

        controles.Should().HaveCount(4);
        controles[0].Should().NotBe(controles[3]);
    }

    [Fact]
    public void La_latitud_geomagnetica_sube_al_acercarse_al_polo_magnetico()
    {
        var canarias = GeometriaDeTrayecto.LatitudGeomagneticaGrados(Tenerife);
        var groenlandia = GeometriaDeTrayecto.LatitudGeomagneticaGrados(new Coordenada(75.0, -60.0));

        canarias.Should().BeInRange(25.0, 50.0);
        groenlandia.Should().BeGreaterThan(80.0);
    }

    private static DateTimeOffset Momento(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture);
}
