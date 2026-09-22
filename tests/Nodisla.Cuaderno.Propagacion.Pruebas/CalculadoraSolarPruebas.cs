using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion.Geometria;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>
/// Orto y ocaso contra los valores publicados, y los dias polares en los que no hay ninguno.
/// </summary>
/// <remarks>
/// Las horas de referencia se contrastaron el 22 de septiembre de 2026 con dos calculadoras
/// publicas, sunrise-sunset.org y sunrisesunset.io, y con timeanddate. Entre ellas no coinciden:
/// se separan un par de minutos a latitudes medias y hasta siete en Reikiavik, porque cada una
/// trata la refraccion a su manera. Por eso la tolerancia es de tres minutos y las referencias
/// son el punto medio de lo que publican. Para un cuaderno de radio eso sobra: lo que importa es
/// saber si falta media hora para el paso gris, no el segundo exacto.
/// </remarks>
public class CalculadoraSolarPruebas
{
    private static readonly Coordenada Tenerife = new(28.4636, -16.2518);
    private static readonly Coordenada Madrid = new(40.4168, -3.7038);
    private static readonly Coordenada Reikiavik = new(64.1466, -21.9426);
    private static readonly Coordenada PuntoPolar = new(80.0, 0.0);

    private const int ToleranciaMinutos = 3;

    /// <summary>Latitud, longitud, dia UTC, orto publicado y ocaso publicado.</summary>
    public static TheoryData<double, double, string, string, string> HorasPublicadas() => new()
    {
        { 28.4636, -16.2518, "2026-03-21", "2026-03-21T07:07:00Z", "2026-03-21T19:17:50Z" },
        { 28.4636, -16.2518, "2026-06-21", "2026-06-21T06:07:00Z", "2026-06-21T20:06:40Z" },
        { 40.4168, -3.7038, "2026-03-21", "2026-03-21T06:15:00Z", "2026-03-21T18:29:00Z" },
        { 64.1466, -21.9426, "2026-06-21", "2026-06-21T02:54:30Z", "2026-06-22T00:05:00Z" },
        { 64.1466, -21.9426, "2026-12-21", "2026-12-21T11:22:00Z", "2026-12-21T15:30:00Z" },
    };

    [Theory]
    [MemberData(nameof(HorasPublicadas))]
    public void El_orto_y_el_ocaso_coinciden_con_los_publicados(
        double latitud,
        double longitud,
        string dia,
        string ortoEsperado,
        string ocasoEsperado)
    {
        var punto = new Coordenada(latitud, longitud);
        var momento = DateTimeOffset.Parse(dia + "T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        var sucesos = CalculadoraSolar.Calcular(punto, momento);

        sucesos.Motivo.Should().Be(MotivoSinEventoSolar.NoAplica);
        sucesos.AmaneceUtc.Should().NotBeNull();
        sucesos.AnocheceUtc.Should().NotBeNull();
        sucesos.AmaneceUtc!.Value.Should().BeCloseTo(
            DateTimeOffset.Parse(ortoEsperado, System.Globalization.CultureInfo.InvariantCulture),
            TimeSpan.FromMinutes(ToleranciaMinutos));
        sucesos.AnocheceUtc!.Value.Should().BeCloseTo(
            DateTimeOffset.Parse(ocasoEsperado, System.Globalization.CultureInfo.InvariantCulture),
            TimeSpan.FromMinutes(ToleranciaMinutos));
    }

    [Fact]
    public void En_reikiavik_el_dia_de_verano_acaba_pasada_la_medianoche_utc()
    {
        var sucesos = CalculadoraSolar.Calcular(Reikiavik, Momento("2026-06-21T12:00:00Z"));

        sucesos.AnocheceUtc!.Value.UtcDateTime.Date.Should().Be(new DateTime(2026, 6, 22));
        (sucesos.AnocheceUtc.Value - sucesos.AmaneceUtc!.Value).Should().BeGreaterThan(TimeSpan.FromHours(21));
    }

    [Fact]
    public void En_el_solsticio_de_verano_un_punto_polar_no_tiene_ni_orto_ni_ocaso()
    {
        var sucesos = CalculadoraSolar.Calcular(PuntoPolar, Momento("2026-06-21T12:00:00Z"));

        sucesos.Motivo.Should().Be(MotivoSinEventoSolar.DiaPolar);
        sucesos.AmaneceUtc.Should().BeNull();
        sucesos.AnocheceUtc.Should().BeNull();
        CalculadoraSolar.EsDeDia(PuntoPolar, Momento("2026-06-21T02:00:00Z")).Should().BeTrue();
    }

    [Fact]
    public void En_el_solsticio_de_invierno_un_punto_polar_tampoco_los_tiene()
    {
        var sucesos = CalculadoraSolar.Calcular(PuntoPolar, Momento("2026-12-21T12:00:00Z"));

        sucesos.Motivo.Should().Be(MotivoSinEventoSolar.NochePolar);
        sucesos.AmaneceUtc.Should().BeNull();
        sucesos.AnocheceUtc.Should().BeNull();
        CalculadoraSolar.EsDeDia(PuntoPolar, Momento("2026-12-21T12:00:00Z")).Should().BeFalse();
    }

    [Fact]
    public void El_mediodia_solar_cae_donde_lo_pone_el_noaa()
    {
        var sucesos = CalculadoraSolar.Calcular(Tenerife, Momento("2026-03-21T12:00:00Z"));

        sucesos.MediodiaSolarUtc.Should().BeCloseTo(
            Momento("2026-03-21T13:12:09Z"),
            TimeSpan.FromMinutes(ToleranciaMinutos));
    }

    [Fact]
    public void El_sol_al_mediodia_del_solsticio_esta_donde_dice_la_geometria()
    {
        // Altura maxima = 90 - latitud + declinacion, y en el solsticio la declinacion vale 23,44.
        var mediodia = CalculadoraSolar.Calcular(Tenerife, Momento("2026-06-21T12:00:00Z")).MediodiaSolarUtc;

        CalculadoraSolar.AlturaSolarGrados(Tenerife, mediodia)
            .Should().BeApproximately(90.0 - 28.4636 + 23.44, 0.3);
    }

    [Fact]
    public void La_declinacion_cambia_de_signo_en_los_equinoccios()
    {
        CalculadoraSolar.DeclinacionGrados(Momento("2026-03-20T12:00:00Z")).Should().BeApproximately(0.0, 0.5);
        CalculadoraSolar.DeclinacionGrados(Momento("2026-06-21T12:00:00Z")).Should().BeApproximately(23.44, 0.1);
        CalculadoraSolar.DeclinacionGrados(Momento("2026-12-21T12:00:00Z")).Should().BeApproximately(-23.44, 0.1);
    }

    [Fact]
    public void La_ecuacion_del_tiempo_se_mueve_en_su_margen_de_siempre()
    {
        // Entre unos -14 y unos +16 minutos a lo largo del ano.
        CalculadoraSolar.EcuacionDelTiempoMinutos(Momento("2026-02-11T12:00:00Z"))
            .Should().BeInRange(-15.0, -13.0);
        CalculadoraSolar.EcuacionDelTiempoMinutos(Momento("2026-11-03T12:00:00Z"))
            .Should().BeInRange(15.0, 17.0);
    }

    [Fact]
    public void De_noche_en_madrid_el_sol_esta_bajo_el_horizonte()
    {
        CalculadoraSolar.EsDeDia(Madrid, Momento("2026-03-21T02:00:00Z")).Should().BeFalse();
        CalculadoraSolar.EsDeDia(Madrid, Momento("2026-03-21T12:00:00Z")).Should().BeTrue();
    }

    private static DateTimeOffset Momento(string texto) =>
        DateTimeOffset.Parse(texto, System.Globalization.CultureInfo.InvariantCulture);
}
