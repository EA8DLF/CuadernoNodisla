using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Integraciones.Bandplan;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Bandplan;

/// <summary>El bandplan: en que tramo cae una frecuencia y que se puede hacer en el.</summary>
public class BandplanPruebas
{
    private static readonly CatalogoBandplanes Catalogo = CatalogoBandplanes.Predeterminado;

    private static IBandplan Region1 => BandplanNodisla.Para(RegionIaru.Region1);

    private static IBandplan Region2 => BandplanNodisla.Para(RegionIaru.Region2);

    private static Modo Modo(string nombre)
    {
        Dominio.Valores.Modo.TryParse(nombre, null, out var m).Should().BeTrue();
        return m;
    }

    [Fact]
    public void El_recurso_trae_los_ocho_planes_de_log4om()
    {
        Catalogo.Todos.Should().HaveCount(8);
        Catalogo.Version.Should().Be(1);
        Catalogo.FechaDeLosDatos.Should().NotBeNull();
        Catalogo.PorRegion(1).Should().NotBeNull();
        Catalogo.PorRegion(2).Should().NotBeNull();
        Catalogo.PorRegion(3).Should().NotBeNull();
        Catalogo.PorDxcc(291).Should().NotBeNull();
    }

    [Fact]
    public void Ea8_es_region_1_y_no_region_2()
    {
        // 29 es Islas Canarias: no tiene plan nacional, asi que le toca el de la Region 1.
        var bandplan = BandplanNodisla.Para(RegionIaru.Region1, dxcc: 29);

        bandplan.Region.Should().Be(RegionIaru.Region1);
        bandplan.Plan.Region.Should().Be(1);
        bandplan.Plan.EsNacional.Should().BeFalse();
    }

    [Fact]
    public void Cuarenta_metros_termina_donde_manda_la_region()
    {
        var enR1 = Region1.Consultar(Frecuencia.DesdeKilohercios(7250));
        var enR2 = Region2.Consultar(Frecuencia.DesdeKilohercios(7250));

        // En la Region 1 la banda acaba en 7.200 kHz; en la Region 2 llega a 7.300.
        enR1.DentroDeBanda.Should().BeFalse();
        enR1.Tramo.Should().BeNull();
        enR1.Aviso.Should().Contain("fuera del plan de bandas");
        enR2.DentroDeBanda.Should().BeTrue();
    }

    [Fact]
    public void Ochenta_metros_tambien_cambia_con_la_region()
    {
        Region1.Consultar(Frecuencia.DesdeKilohercios(3900)).DentroDeBanda.Should().BeFalse();
        Region2.Consultar(Frecuencia.DesdeKilohercios(3900)).DentroDeBanda.Should().BeTrue();
    }

    [Theory]
    [InlineData(14010, UsoDelTramo.Cw)]
    [InlineData(14080, UsoDelTramo.DigitalEstrecho)]
    [InlineData(14200, UsoDelTramo.Fonia)]
    public void Cada_tramo_de_veinte_metros_tiene_su_uso(int khz, UsoDelTramo esperado)
    {
        var consulta = Region1.Consultar(Frecuencia.DesdeKilohercios(khz));

        consulta.DentroDeBanda.Should().BeTrue();
        consulta.Tramo!.Uso.Should().Be(esperado);
        consulta.Tramo.Banda.Nombre.Should().Be("20m");
        consulta.Tramo.Contiene(Frecuencia.DesdeKilohercios(khz)).Should().BeTrue();
    }

    [Fact]
    public void Transmitir_en_fonia_en_el_tramo_de_cw_avisa_pero_no_impide()
    {
        var consulta = Region1.Consultar(Frecuencia.DesdeKilohercios(14010), Modo("SSB"));

        consulta.DentroDeBanda.Should().BeTrue();
        consulta.ModoEncaja.Should().BeFalse();
        consulta.Aviso.Should().Contain("CW");
        // El aviso es eso, un aviso: el tramo se devuelve igual y nada impide operar.
        consulta.Tramo.Should().NotBeNull();
    }

    [Fact]
    public void Transmitir_en_el_tramo_que_toca_no_dice_nada()
    {
        var consulta = Region1.Consultar(Frecuencia.DesdeKilohercios(14010), Modo("CW"));

        consulta.ModoEncaja.Should().BeTrue();
        consulta.Aviso.Should().BeNull();
    }

    [Fact]
    public void Sin_modo_no_se_opina_sobre_el_modo()
    {
        var consulta = Region1.Consultar(Frecuencia.DesdeKilohercios(14010));

        consulta.ModoEncaja.Should().BeNull();
        consulta.Aviso.Should().BeNull();
    }

    [Fact]
    public void La_banda_ciudadana_esta_fuera_del_plan_pero_se_puede_consultar()
    {
        // El dial de esta estacion estaba en 27,555 MHz cuando se interrogo al equipo.
        var consulta = Region1.Consultar(Frecuencia.DesdeMegahercios(27.555m));

        consulta.DentroDeBanda.Should().BeFalse();
        consulta.Tramo.Should().BeNull();
        consulta.Aviso.Should().Contain("fuera del plan de bandas");
        consulta.Frecuencia.Megahercios.Should().Be(27.555m);
    }

    [Fact]
    public void Los_tramos_de_una_banda_salen_ordenados()
    {
        var tramos = Region1.TramosDe(Banda.Parse("20m"));

        tramos.Should().HaveCountGreaterThan(1);
        tramos.Should().BeInAscendingOrder(t => t.Inferior);
        tramos[0].Descripcion.Should().Contain("20m");
    }

    [Fact]
    public void Las_frecuencias_senaladas_dicen_el_modo()
    {
        var senaladas = Region1.FrecuenciasSenaladas(Banda.Parse("20m"));

        senaladas.Should().Contain(s =>
            s.Frecuencia == Frecuencia.DesdeKilohercios(14074) && s.Descripcion == "FT8");

        var bandplan = BandplanNodisla.Para(RegionIaru.Region1);
        bandplan.ModoSugerido(Frecuencia.DesdeKilohercios(14074)).Should().Be("FT8");
        bandplan.ModoSugerido(Frecuencia.DesdeKilohercios(7047.5m)).Should().Be("FT4");
        bandplan.ModoSugerido(Frecuencia.DesdeKilohercios(14010)).Should().Be("CW");
    }

    [Fact]
    public void El_limite_de_un_tramo_es_del_tramo_que_empieza_ahi()
    {
        var plan = Catalogo.PorRegion(1)!;

        // 14.070 es donde acaba CW y empieza digital: la frecuencia es del segundo.
        plan.Buscar(Frecuencia.DesdeKilohercios(14070))!.Uso.Should().Be(UsoDelTramo.DigitalEstrecho);

        // 14.350 es el final de la banda y no empieza nada despues: sigue siendo de fonia.
        plan.Buscar(Frecuencia.DesdeKilohercios(14350))!.Uso.Should().Be(UsoDelTramo.Fonia);

        // Un hercio mas arriba ya no hay banda.
        plan.Buscar(Frecuencia.DesdeKilohercios(14350.001m)).Should().BeNull();
    }

    [Fact]
    public void Un_plan_nacional_gana_al_de_la_region()
    {
        var bandplan = BandplanNodisla.Para(RegionIaru.Region2, dxcc: 291);

        bandplan.Plan.EsNacional.Should().BeTrue();
        bandplan.Plan.Dxcc.Should().Be(291);
    }

    [Fact]
    public void Se_puede_cargar_un_plan_escrito_a_mano_con_clases_de_licencia()
    {
        const string recurso =
            "# prueba\n" +
            "V\t1\t2026-01-01\t2026-01-02\n" +
            "P\tpruebas\t1\t0\tPlan de prueba\n" +
            "S\tpruebas\t20m\t14000\t14070\tCW\tUSB\tAvanzada\n" +
            "S\tpruebas\t20m\t14070\t14350\tPHONE\tUSB\t\n" +
            "F\tpruebas\t14074\tFT8\t\n";

        var catalogo = CatalogoBandplanes.Cargar(recurso);
        var plan = catalogo.PorIdentificador("pruebas");

        plan.Should().NotBeNull();
        plan!.Segmentos.Should().HaveCount(2);
        plan.Segmentos[0].Clases.Should().ContainSingle().Which.Should().Be("Avanzada");
        plan.Senaladas.Should().ContainSingle();

        var novato = new BandplanNodisla(plan, "Novato");
        novato.TieneAccesoALaFrecuencia(Frecuencia.DesdeKilohercios(14010)).Should().BeFalse();
        novato.Consultar(Frecuencia.DesdeKilohercios(14010)).Aviso.Should().Contain("licencia");

        var avanzada = new BandplanNodisla(plan, "Avanzada");
        avanzada.TieneAccesoALaFrecuencia(Frecuencia.DesdeKilohercios(14010)).Should().BeTrue();
        avanzada.Consultar(Frecuencia.DesdeKilohercios(14010)).Aviso.Should().BeNull();
    }

    [Fact]
    public void Todos_los_tramos_del_recurso_van_de_menos_a_mas()
    {
        foreach (var plan in Catalogo.Todos)
        {
            plan.Segmentos.Should().NotBeEmpty();
            foreach (var s in plan.Segmentos)
            {
                s.Hasta.Should().BeGreaterThan(s.Desde, $"el tramo {s} de {plan.Identificador} esta al reves");
            }
        }
    }
}
