using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion.Prediccion;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>
/// «Mi propagacion hacia ellos» en el cluster: la prevision fila a fila, con indices fijos y
/// una hora fija. Nada de red ni de reloj de pared.
/// </summary>
public class PropagacionHaciaEstacionPruebas
{
    private static readonly Coordenada Tenerife = new(28.4636, -16.2518);
    private static readonly Coordenada Berlin = Coordenada.Desde(Locator.Parse("JO62QM"));
    private static readonly Coordenada Tokio = Coordenada.Desde(Locator.Parse("PM95VQ"));

    // Mediodia en Canarias y en Europa; madrugada en los dos sitios.
    private static readonly DateTimeOffset Mediodia = new(2026, 9, 22, 13, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Madrugada = new(2026, 9, 22, 2, 5, 0, TimeSpan.Zero);

    private static readonly IndicesSolares SolDeHoy = new(
        FlujoSolar: 150, IndiceA: 8, IndiceK: 2, ManchasSolares: 110,
        VientoSolarKmS: 400, CampoBz: 0, Tormenta: false,
        MedidoUtc: new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void De_dia_hacia_Europa_una_banda_baja_sale_peor_que_una_alta()
    {
        var calculo = new PropagacionHaciaEstacion();

        var en80 = calculo.Prever(Tenerife, Berlin, 3.650, "SSB", Mediodia, SolDeHoy)!;
        var en40 = calculo.Prever(Tenerife, Berlin, 7.100, "SSB", Mediodia, SolDeHoy)!;
        var en20 = calculo.Prever(Tenerife, Berlin, 14.200, "SSB", Mediodia, SolDeHoy)!;
        var en15 = calculo.Prever(Tenerife, Berlin, 21.200, "SSB", Mediodia, SolDeHoy)!;

        // La capa D se come las bandas bajas de dia; las altas pasan.
        en80.Fiabilidad.Should().BeLessThan(en20.Fiabilidad);
        en40.Fiabilidad.Should().BeLessThan(en15.Fiabilidad);
        en20.Fiabilidad.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void De_madrugada_una_banda_alta_se_cierra_y_la_baja_abre()
    {
        var calculo = new PropagacionHaciaEstacion();

        var en40 = calculo.Prever(Tenerife, Berlin, 7.100, "CW", Madrugada, SolDeHoy)!;
        var en10 = calculo.Prever(Tenerife, Berlin, 28.020, "CW", Madrugada, SolDeHoy)!;

        en10.Fiabilidad.Should().BeLessThan(en40.Fiabilidad);
        en10.Fiabilidad.Should().BeLessThan(0.2);
        en10.MufMhz.Should().BeLessThan(28.0);
    }

    [Fact]
    public void El_ft8_se_da_mejor_que_la_fonia_en_la_misma_frecuencia()
    {
        var calculo = new PropagacionHaciaEstacion();

        var fonia = calculo.Prever(Tenerife, Tokio, 14.200, "SSB", Mediodia, SolDeHoy)!;
        var ft8 = calculo.Prever(Tenerife, Tokio, 14.074, "FT8", Mediodia, SolDeHoy)!;

        ft8.Fiabilidad.Should().BeGreaterThanOrEqualTo(fonia.Fiabilidad);
    }

    [Fact]
    public void Lleva_distancia_rumbo_mufa_y_relacion_senal_ruido()
    {
        var prevision = new PropagacionHaciaEstacion()
            .Prever(Tenerife, Berlin, 14.200, "SSB", Mediodia, SolDeHoy)!;

        prevision.DistanciaKm.Should().BeInRange(3200, 3700);
        prevision.RumboGrados.Should().BeInRange(20, 40);
        prevision.MufMhz.Should().BeGreaterThan(14.2);
        prevision.RelacionSenalRuido.Should().NotBeNull();
        prevision.Saltos.Should().BeGreaterThanOrEqualTo(1);
        prevision.Motor.Should().Contain("Aproximación");
    }

    [Fact]
    public void Sin_indices_sin_posicion_o_fuera_de_hf_no_hay_numero()
    {
        var calculo = new PropagacionHaciaEstacion();

        calculo.Prever(Tenerife, Berlin, 14.2, "SSB", Mediodia, indices: null).Should().BeNull();
        calculo.Prever(null, Berlin, 14.2, "SSB", Mediodia, SolDeHoy).Should().BeNull();
        calculo.Prever(Tenerife, null, 14.2, "SSB", Mediodia, SolDeHoy).Should().BeNull();
        calculo.Prever(Tenerife, Berlin, 144.300, "SSB", Mediodia, SolDeHoy).Should().BeNull();
        calculo.Prever(Tenerife, Berlin, 0.475, "CW", Mediodia, SolDeHoy).Should().BeNull();
    }

    [Fact]
    public void Dentro_del_mismo_cuarto_de_hora_se_reutiliza_y_al_pasar_se_recalcula()
    {
        var calculo = new PropagacionHaciaEstacion();

        var primera = calculo.Prever(Tenerife, Berlin, 14.2, "SSB", Mediodia, SolDeHoy);
        var mismoTramo = calculo.Prever(Tenerife, Berlin, 14.2, "SSB", Mediodia.AddMinutes(3), SolDeHoy);
        mismoTramo.Should().BeSameAs(primera);

        var otroTramo = calculo.Prever(Tenerife, Berlin, 14.2, "SSB", Mediodia.AddMinutes(20), SolDeHoy);
        otroTramo.Should().NotBeSameAs(primera);
        otroTramo!.CalculadaUtc.Should().BeAfter(primera!.CalculadaUtc);

        // Indices nuevos: se rehace aunque sea el mismo tramo.
        var conTormenta = calculo.Prever(
            Tenerife, Berlin, 14.2, "SSB", Mediodia.AddMinutes(20), SolDeHoy with { IndiceK = 7, Tormenta = true });
        conTormenta.Should().NotBeSameAs(otroTramo);
    }
}
