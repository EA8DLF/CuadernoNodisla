using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion;
using Nodisla.Cuaderno.Propagacion.Prediccion;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>
/// Bateria que fija el comportamiento de la aproximacion propia.
/// </summary>
/// <remarks>
/// El modelo es una estimacion y puede afinarse, pero no puede cambiar de comportamiento sin que
/// alguien se entere: estas pruebas clavan las relaciones que tienen que seguir cumpliendose
/// -que de dia las bandas bajas se cierran, que la MUF sube con el Sol, que una tormenta hunde
/// los trayectos polares- y los pocos numeros que estan anclados en formulas publicadas.
/// </remarks>
public class ModeloMufLufPruebas
{
    private static readonly Coordenada Tenerife = new(28.4636, -16.2518);
    private static readonly Coordenada Madrid = new(40.4168, -3.7038);
    private static readonly Coordenada Tokio = new(35.6895, 139.6917);
    private static readonly Coordenada Laponia = new(70.0, 20.0);

    private static readonly CondicionesIonosfericas CicloMedio = new(52.0, 1.0, false);

    private static readonly DateTimeOffset Mediodia = Momento("2026-09-22T13:00:00Z");
    private static readonly DateTimeOffset Madrugada = Momento("2026-09-22T02:00:00Z");

    [Theory]
    [InlineData(63.75, 0.0)]
    [InlineData(70.0, 8.5)]
    [InlineData(104.0, 52.0)]
    [InlineData(200.0, 157.0)]
    public void Las_manchas_salen_de_invertir_la_relacion_de_covington(double flujo, double esperado)
    {
        ModeloMufLuf.ManchasDesdeFlujo(flujo).Should().BeApproximately(esperado, 1.0);
    }

    [Fact]
    public void Un_flujo_por_debajo_del_minimo_no_da_manchas_negativas()
    {
        ModeloMufLuf.ManchasDesdeFlujo(50.0).Should().Be(0.0);
    }

    [Theory]
    [InlineData(500.0, 1)]
    [InlineData(4000.0, 1)]
    [InlineData(4001.0, 2)]
    [InlineData(12449.0, 4)]
    public void Los_saltos_se_reparten_en_tramos_de_cuatro_mil_kilometros(double distancia, int saltos)
    {
        ModeloMufLuf.SaltosNecesarios(distancia).Should().Be(saltos);
    }

    [Fact]
    public void Un_salto_de_tres_mil_kilometros_da_el_factor_que_miden_las_ionosondas()
    {
        // El M(3000)F2 de las ionosondas ronda 3,2-3,4: si esto cambia, la MUF entera cambia.
        var elevacion = ModeloMufLuf.ElevacionGrados(3000.0, ModeloMufLuf.AlturaCapaF2Km);
        var factor = ModeloMufLuf.FactorOblicuidad(elevacion, ModeloMufLuf.AlturaCapaF2Km);

        elevacion.Should().BeApproximately(4.26, 0.05);
        factor.Should().BeApproximately(3.28, 0.02);
    }

    [Fact]
    public void Con_los_dos_extremos_en_el_mismo_sitio_la_muf_es_la_frecuencia_critica()
    {
        var geometria = ModeloMufLuf.Calcular(Tenerife, Tenerife, Mediodia, CicloMedio);
        var critica = ModeloMufLuf.FrecuenciaCriticaF2Mhz(Tenerife, Mediodia, CicloMedio);

        geometria.Saltos.Should().Be(1);
        geometria.ElevacionGrados.Should().BeApproximately(90.0, 0.5);
        geometria.MufMhz.Should().BeApproximately(critica, 0.05);
    }

    [Fact]
    public void La_ionosfera_de_dia_aguanta_mas_frecuencia_que_la_de_noche()
    {
        var dia = ModeloMufLuf.FrecuenciaCriticaF2Mhz(Tenerife, Mediodia, CicloMedio);
        var noche = ModeloMufLuf.FrecuenciaCriticaF2Mhz(Tenerife, Madrugada, CicloMedio);

        dia.Should().BeInRange(8.0, 12.0);
        noche.Should().BeInRange(2.5, 5.0);
        dia.Should().BeGreaterThan(noche * 2.0);
    }

    [Fact]
    public void Mas_manchas_significa_mas_frecuencia_critica()
    {
        var minimo = ModeloMufLuf.FrecuenciaCriticaF2Mhz(Tenerife, Mediodia, new CondicionesIonosfericas(5, 1, false));
        var maximo = ModeloMufLuf.FrecuenciaCriticaF2Mhz(Tenerife, Mediodia, new CondicionesIonosfericas(150, 1, false));

        maximo.Should().BeGreaterThan(minimo * 1.5);
    }

    [Fact]
    public void La_muf_del_trayecto_la_manda_el_salto_peor()
    {
        // A las 13 UTC en Canarias es mediodia y en Japon es de noche: el extremo japones hunde
        // la MUF de todo el circuito por debajo de la que habria si todo fuera de dia.
        var largo = ModeloMufLuf.Calcular(Tenerife, Tokio, Mediodia, CicloMedio);
        var corto = ModeloMufLuf.Calcular(Tenerife, Madrid, Mediodia, CicloMedio);

        largo.Saltos.Should().Be(4);
        largo.MufMhz.Should().BeLessThan(corto.MufMhz);
        corto.MufMhz.Should().BeInRange(18.0, 30.0);
    }

    [Fact]
    public void Una_tormenta_geomagnetica_hunde_el_trayecto_polar()
    {
        var tranquilo = ModeloMufLuf.Calcular(Tenerife, Laponia, Mediodia, new CondicionesIonosfericas(52, 1, false));
        var revuelto = ModeloMufLuf.Calcular(Tenerife, Laponia, Mediodia, new CondicionesIonosfericas(52, 8, true));

        revuelto.MufMhz.Should().BeLessThan(tranquilo.MufMhz * 0.85);
    }

    [Fact]
    public void La_tormenta_no_toca_los_trayectos_ecuatoriales()
    {
        var ecuador = new Coordenada(0.0, -16.0);

        ModeloMufLuf.FactorDeTormenta(
            Math.Abs(Propagacion.Geometria.GeometriaDeTrayecto.LatitudGeomagneticaGrados(ecuador)),
            9.0).Should().BeGreaterThan(0.85);
        ModeloMufLuf.FactorDeTormenta(75.0, 9.0).Should().BeLessThan(0.75);
        ModeloMufLuf.FactorDeTormenta(75.0, 0.0).Should().Be(1.0);
    }

    [Fact]
    public void De_dia_la_capa_d_se_come_las_bandas_bajas_y_de_noche_desaparece()
    {
        var geometriaDia = ModeloMufLuf.Calcular(Tenerife, Madrid, Mediodia, CicloMedio);
        var geometriaNoche = ModeloMufLuf.Calcular(Tenerife, Madrid, Madrugada, CicloMedio);

        var ochentaDia = ModeloMufLuf.AbsorcionDb(Tenerife, Madrid, Mediodia, 3.65, CicloMedio, geometriaDia);
        var veinteDia = ModeloMufLuf.AbsorcionDb(Tenerife, Madrid, Mediodia, 14.15, CicloMedio, geometriaDia);
        var ochentaNoche = ModeloMufLuf.AbsorcionDb(Tenerife, Madrid, Madrugada, 3.65, CicloMedio, geometriaNoche);

        ochentaDia.Should().BeGreaterThan(veinteDia * 3.0);
        veinteDia.Should().BeInRange(4.0, 20.0);
        ochentaNoche.Should().Be(0.0);
    }

    [Fact]
    public void La_absorcion_tiene_tope_para_no_dar_cifras_absurdas()
    {
        var geometria = ModeloMufLuf.Calcular(Tenerife, Tokio, Mediodia, CicloMedio);

        ModeloMufLuf.AbsorcionDb(Tenerife, Tokio, Mediodia, 0.5, CicloMedio, geometria)
            .Should().BeLessThanOrEqualTo(ModeloMufLuf.AbsorcionMaximaDb);
    }

    [Fact]
    public void El_ruido_sigue_las_cifras_de_la_p372()
    {
        ModeloMufLuf.RuidoDbw(14.15, 2500, AmbienteDeRuido.Residencial).Should().BeApproximately(-129.4, 0.5);
        ModeloMufLuf.RuidoDbw(1.84, 2500, AmbienteDeRuido.Residencial).Should().BeApproximately(-104.9, 0.5);

        // Cuanto mas tranquilo el emplazamiento, menos ruido; y nunca por debajo del galactico.
        ModeloMufLuf.RuidoDbw(14.15, 2500, AmbienteDeRuido.Rural)
            .Should().BeLessThan(ModeloMufLuf.RuidoDbw(14.15, 2500, AmbienteDeRuido.Industrial));
    }

    [Fact]
    public void La_normal_acumulada_esta_donde_tiene_que_estar()
    {
        ModeloMufLuf.NormalAcumulada(0.0).Should().BeApproximately(0.5, 1e-4);
        ModeloMufLuf.NormalAcumulada(1.645).Should().BeApproximately(0.95, 1e-3);
        ModeloMufLuf.NormalAcumulada(-1.645).Should().BeApproximately(0.05, 1e-3);
    }

    [Fact]
    public void Trabajar_en_la_muf_es_jugarsela_a_cara_o_cruz()
    {
        ModeloMufLuf.ProbabilidadDeMuf(14.0, 14.0).Should().BeApproximately(0.5, 1e-3);
        // La FOT clasica, el 85 % de la MUF, sale cerca del 90 % de los dias.
        ModeloMufLuf.ProbabilidadDeMuf(14.0, 0.85 * 14.0).Should().BeInRange(0.87, 0.93);
        ModeloMufLuf.ProbabilidadDeMuf(14.0, 20.0).Should().BeLessThan(0.02);
    }

    private static DateTimeOffset Momento(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture);
}
