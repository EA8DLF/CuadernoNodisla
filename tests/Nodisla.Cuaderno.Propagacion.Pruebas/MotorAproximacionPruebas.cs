using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion;
using Nodisla.Cuaderno.Propagacion.Prediccion;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>
/// Comportamiento del motor de aproximacion visto desde fuera: que bandas saca y como las ordena.
/// </summary>
public class MotorAproximacionPruebas
{
    private static readonly Coordenada Tenerife = new(28.4636, -16.2518);
    private static readonly Coordenada Madrid = new(40.4168, -3.7038);
    private static readonly Coordenada Tokio = new(35.6895, 139.6917);

    private static readonly DateTimeOffset Mediodia = Momento("2026-09-22T13:00:00Z");
    private static readonly DateTimeOffset Madrugada = Momento("2026-09-22T02:00:00Z");

    private static readonly MotorAproximacionNodisla Motor = new();

    [Fact]
    public void El_motor_dice_que_es_una_aproximacion_y_que_no_es_voacap()
    {
        Motor.EsAproximacion.Should().BeTrue();
        Motor.Nombre.Should().Contain("VOACAP");
        Motor.Nombre.Should().Contain("Aproximación");
    }

    [Fact]
    public void Cada_prediccion_va_firmada_por_su_motor()
    {
        // La firma va en cada banda, no solo en el motor: el dia que haya un motor externo,
        // las bandas que caigan de vuelta a la estimacion tienen que poder distinguirse.
        var prediccion = Predecir(Tenerife, Madrid, Mediodia);

        prediccion.Should().OnlyContain(p => p.EsAproximacion);
        prediccion.Should().OnlyContain(p => p.Motor == MotorAproximacionNodisla.NombreDelMotor);
    }

    [Fact]
    public void Una_prediccion_recien_hecha_nunca_se_da_por_calculada_por_descuido()
    {
        // El contrato deja EsAproximacion en cierto por omision: si alguien escribe un motor y
        // se olvida de firmar, lo peor que pasa es que se enseñe como estimacion algo que no lo
        // era, y no al reves.
        new PrediccionDeBanda(Banda.Parse("20m"), Mediodia, 0.5, null, null, 1)
            .EsAproximacion.Should().BeTrue();
    }

    [Fact]
    public void Se_predicen_todas_las_bandas_de_hf_y_las_probabilidades_son_probabilidades()
    {
        var prediccion = Predecir(Tenerife, Madrid, Mediodia);

        prediccion.Should().HaveCount(11);
        prediccion.Select(p => p.Banda.Nombre).Should().ContainInOrder("160m", "80m", "40m", "20m", "10m", "6m");
        prediccion.Should().OnlyContain(p => p.Fiabilidad >= 0.0 && p.Fiabilidad <= 1.0);
        prediccion.Should().OnlyContain(p => p.HoraUtc == Mediodia);
    }

    [Fact]
    public void A_mediodia_en_un_trayecto_corto_mandan_las_bandas_altas()
    {
        var prediccion = Predecir(Tenerife, Madrid, Mediodia).ToDictionary(p => p.Banda.Nombre);

        prediccion["20m"].Fiabilidad.Should().BeGreaterThan(0.6);
        prediccion["17m"].Fiabilidad.Should().BeGreaterThan(0.6);
        prediccion["80m"].Fiabilidad.Should().BeLessThan(0.05);
        prediccion["160m"].Fiabilidad.Should().BeLessThan(0.01);
    }

    [Fact]
    public void De_madrugada_se_invierte_y_mandan_las_bandas_bajas()
    {
        var prediccion = Predecir(Tenerife, Madrid, Madrugada).ToDictionary(p => p.Banda.Nombre);

        prediccion["80m"].Fiabilidad.Should().BeGreaterThan(0.8);
        prediccion["160m"].Fiabilidad.Should().BeGreaterThan(0.8);
        prediccion["20m"].Fiabilidad.Should().BeLessThan(0.1);
        prediccion["10m"].Fiabilidad.Should().BeLessThan(0.01);
    }

    [Fact]
    public void Ochenta_metros_siempre_va_mejor_de_noche_que_de_dia()
    {
        var dia = Predecir(Tenerife, Madrid, Mediodia).Single(p => p.Banda.Nombre == "80m");
        var noche = Predecir(Tenerife, Madrid, Madrugada).Single(p => p.Banda.Nombre == "80m");

        noche.Fiabilidad.Should().BeGreaterThan(dia.Fiabilidad);
        noche.RelacionSenalRuido.Should().BeGreaterThan(dia.RelacionSenalRuido ?? -999.0);
    }

    [Fact]
    public void Un_trayecto_de_doce_mil_kilometros_va_en_cuatro_saltos()
    {
        Predecir(Tenerife, Tokio, Mediodia).Should().OnlyContain(p => p.Saltos == 4);
        Predecir(Tenerife, Madrid, Mediodia).Should().OnlyContain(p => p.Saltos == 1);
    }

    [Fact]
    public void Cuando_no_hay_circuito_no_se_da_un_nivel_de_senal_inventado()
    {
        // Ciento sesenta metros a mediodia en un trayecto largo: la senal calculada seria de
        // cientos de decibelios negativos, un numero que no significa nada. Se devuelve nulo.
        var banda = Predecir(Tenerife, Tokio, Mediodia).Single(p => p.Banda.Nombre == "160m");

        banda.Fiabilidad.Should().BeApproximately(0.0, 1e-6);
        banda.SenalDbw.Should().BeNull();
        banda.RelacionSenalRuido.Should().BeNull();
    }

    [Fact]
    public void Por_encima_de_la_muf_no_se_da_un_nivel_de_senal_que_no_llega()
    {
        // Comparando con la P.533 se vio que casi una de cada tres filas publicaba una senal de
        // una banda que estaba muy por encima de la MUF: el presupuesto de perdidas sigue dando
        // un numero grande, pero por encima de la MUF la onda no vuelve y ese numero no existe.
        var prediccion = Predecir(Tenerife, Madrid, Madrugada).ToDictionary(p => p.Banda.Nombre);

        // De madrugada la MUF esta baja; diez metros esta muy por encima.
        prediccion["10m"].Fiabilidad.Should().BeLessThan(ModeloMufLuf.ProbabilidadMinimaDeModo);
        prediccion["10m"].SenalDbw.Should().BeNull();
        prediccion["10m"].RelacionSenalRuido.Should().BeNull();

        // Y las que si estan por debajo de la MUF siguen dando su cifra.
        prediccion["80m"].SenalDbw.Should().NotBeNull();
        prediccion["80m"].RelacionSenalRuido.Should().NotBeNull();
    }

    [Fact]
    public void Mas_potencia_nunca_empeora_la_prediccion()
    {
        var pocos = Motor.Predecir(Solicitud(Tenerife, Madrid, Mediodia, 5));
        var muchos = Motor.Predecir(Solicitud(Tenerife, Madrid, Mediodia, 1000));

        for (var i = 0; i < pocos.Count; i++)
        {
            muchos[i].Fiabilidad.Should().BeGreaterThanOrEqualTo(pocos[i].Fiabilidad);
        }
    }

    [Fact]
    public void Sin_indices_solares_se_usan_condiciones_medias_y_no_revienta()
    {
        var sinIndices = Motor.Predecir(
            new SolicitudDePrediccion(Tenerife, Madrid, 100, Mediodia, null));

        sinIndices.Should().HaveCount(11);
        sinIndices.Should().OnlyContain(p => p.SinDatosSolares);
        MotorAproximacionNodisla.CondicionesDesde(null).Should().Be(CondicionesIonosfericas.Supuestas);
    }

    [Fact]
    public void Con_indices_solares_la_prediccion_no_se_marca_como_hecha_a_ciegas()
    {
        Predecir(Tenerife, Madrid, Mediodia).Should().OnlyContain(p => !p.SinDatosSolares);
    }

    [Fact]
    public void Si_faltan_las_manchas_se_deducen_del_flujo_solar()
    {
        var indices = new IndicesSolares(104, 3, 1, null, null, null, false, Mediodia);

        var condiciones = MotorAproximacionNodisla.CondicionesDesde(indices);

        condiciones.ManchasSolares.Should().BeApproximately(52.0, 1.0);
        condiciones.IndiceK.Should().Be(1.0);
    }

    [Fact]
    public void Si_solo_se_sabe_que_hay_tormenta_se_supone_un_indice_k_de_tormenta()
    {
        var indices = new IndicesSolares(104, null, null, 52, null, null, true, Mediodia);

        MotorAproximacionNodisla.CondicionesDesde(indices).IndiceK.Should().Be(5.0);
    }

    [Fact]
    public async Task La_prediccion_asincrona_atiende_la_cancelacion()
    {
        using var testigo = new CancellationTokenSource();
        await testigo.CancelAsync();

        var accion = async () => await Motor.PredecirAsync(Solicitud(Tenerife, Madrid, Mediodia, 100), testigo.Token);

        await accion.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IReadOnlyList<PrediccionDeBanda> Predecir(
        Coordenada origen,
        Coordenada destino,
        DateTimeOffset momento) => Motor.Predecir(Solicitud(origen, destino, momento, 100));

    private static SolicitudDePrediccion Solicitud(
        Coordenada origen,
        Coordenada destino,
        DateTimeOffset momento,
        double potencia) => new(
            origen,
            destino,
            potencia,
            momento,
            new IndicesSolares(104, 3, 1, 52, 400, -2, false, momento));

    private static DateTimeOffset Momento(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture);
}
