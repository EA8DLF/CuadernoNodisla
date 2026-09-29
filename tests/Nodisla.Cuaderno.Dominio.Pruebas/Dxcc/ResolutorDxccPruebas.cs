using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Dxcc;

/// <summary>Comportamiento del resolutor DXCC: prefijos, excepciones, sufijos y fechas.</summary>
public sealed class ResolutorDxccPruebas
{
    private static readonly ResolutorDxcc Resolutor = ResolutorDxcc.Predeterminado;
    private static readonly DateOnly Hoy = new(2026, 1, 1);

    private static ResultadoDxcc Resolver(string indicativo, DateOnly? fecha = null) =>
        Resolutor.Resolver(Indicativo.Crudo(indicativo), fecha ?? Hoy);

    // ---------------------------------------------------------------- catalogo

    [Fact]
    public void El_catalogo_trae_las_entidades_vigentes_y_las_borradas()
    {
        Resolutor.Todas.Should().HaveCountGreaterThan(390);
        Resolutor.Todas.Should().Contain(e => e.Numero == 29 && !e.EstaBorrada);
        Resolutor.Todas.Should().Contain(e => e.Numero == 218 && e.EstaBorrada);
    }

    [Fact]
    public void El_catalogo_sabe_de_que_dia_son_sus_datos()
    {
        Resolutor.FechaDeLosDatos.Should().NotBeNull();
        Resolutor.FechaDeLosDatos!.Value.Year.Should().BeGreaterThan(2020);
    }

    [Fact]
    public void Las_entidades_se_buscan_por_numero()
    {
        Resolutor.PorNumero(29)!.Nombre.Should().Be("Canary Islands");
        Resolutor.PorNumero(29)!.NombreParaMostrar.Should().Be("Islas Canarias");
        Resolutor.PorNumero(99999).Should().BeNull();
    }

    // ---------------------------------------------------------------- prefijos

    [Theory]
    [InlineData("EA8DLF", 29, "AF")]     // Islas Canarias
    [InlineData("EA4ABC", 281, "EU")]    // Espana peninsular
    [InlineData("EA6ABC", 21, "EU")]     // Baleares
    [InlineData("EA9ABC", 32, "AF")]     // Ceuta y Melilla
    [InlineData("SV9ABC", 40, "EU")]     // Creta, no Grecia
    [InlineData("SV1ABC", 236, "EU")]    // Grecia
    [InlineData("W1AW", 291, "NA")]
    [InlineData("KL7RA", 6, "NA")]       // Alaska, no Estados Unidos
    [InlineData("KH6XX", 110, "OC")]     // Hawai
    [InlineData("JA1ABC", 339, "AS")]
    [InlineData("VK2ABC", 150, "OC")]
    [InlineData("ZS1ABC", 462, "AF")]
    public void Un_indicativo_normal_se_resuelve_por_su_prefijo(string indicativo, int dxcc, string continente)
    {
        var r = Resolver(indicativo);

        r.EsDesconocido.Should().BeFalse();
        r.Entidad!.Numero.Should().Be(dxcc);
        r.Continente.Should().Be(continente);
        r.NecesitaRevision.Should().BeFalse();
    }

    [Fact]
    public void Gana_siempre_el_prefijo_mas_largo()
    {
        // EA -> Espana, pero EA8 -> Canarias: no puede quedarse en el prefijo corto.
        Resolver("EA8DLF").PrefijoCoincidente.Should().Be("EA8");
        Resolver("EA4ABC").Entidad!.Numero.Should().Be(281);
    }

    [Fact]
    public void El_prefijo_puede_traer_zonas_distintas_de_las_de_la_entidad()
    {
        var vancouver = Resolver("VE7ABC");
        var terranova = Resolver("VO1ABC");

        vancouver.Entidad!.Numero.Should().Be(1);
        terranova.Entidad!.Numero.Should().Be(1);
        vancouver.ZonaCq.Should().NotBe(terranova.ZonaCq,
            "Canada ocupa varias zonas CQ y el prefijo es lo unico que las distingue");
    }

    [Fact]
    public void El_resultado_trae_la_coordenada_del_prefijo()
    {
        var r = Resolver("EA8DLF");

        r.Coordenada.Should().NotBeNull();
        r.Coordenada!.Value.Latitud.Should().BeApproximately(28.3, 1.0);
        r.Coordenada!.Value.Longitud.Should().BeApproximately(-15.9, 1.5);
    }

    // ---------------------------------------------------- sufijos y prefijos anadidos

    [Theory]
    [InlineData("EA8DLF/P", 29)]
    [InlineData("EA8DLF/M", 29)]
    [InlineData("EA8DLF/QRP", 29)]
    [InlineData("CT1ILT/P", 272)]
    [InlineData("EA8/DL1ABC", 29)]     // manda el prefijo anadido, no Alemania
    [InlineData("DL1ABC/EA8", 29)]     // da igual de que lado venga
    [InlineData("EA5/ON4ANV", 281)]
    [InlineData("SV9/J43ND", 40)]
    [InlineData("PJ5/SP9FIH", 519)]
    [InlineData("TA4/PE2M", 390)]
    [InlineData("F/ON4ABC/P", 227)]
    public void Los_anadidos_de_operacion_no_cambian_la_entidad(string indicativo, int dxcc)
    {
        Resolver(indicativo).Entidad!.Numero.Should().Be(dxcc);
    }

    [Theory]
    [InlineData("HC7AE/1", 120)]       // cambio de distrito dentro de Ecuador
    [InlineData("KB1EFS/2", 291)]
    [InlineData("I4RHP/7", 248)]
    public void El_digito_suelto_cambia_de_distrito_pero_no_de_pais(string indicativo, int dxcc)
    {
        Resolver(indicativo).Entidad!.Numero.Should().Be(dxcc);
    }

    [Theory]
    [InlineData("EA8DLF/MM")]
    [InlineData("EA8DLF/AM")]
    [InlineData("DL1ABC/MM")]
    public void El_movil_maritimo_y_el_aeronautico_no_tienen_entidad(string indicativo)
    {
        var r = Resolver(indicativo);

        r.EsDesconocido.Should().BeTrue();
        r.NecesitaRevision.Should().BeTrue();
    }

    [Fact]
    public void Dos_indicativos_completos_pegados_piden_revision()
    {
        var r = Resolver("EA8DLF/DL1ABC");

        r.NecesitaRevision.Should().BeTrue();
    }

    [Fact]
    public void Un_indicativo_vacio_es_desconocido()
    {
        Resolutor.Resolver(Indicativo.Vacio, Hoy).EsDesconocido.Should().BeTrue();
    }

    [Fact]
    public void Un_prefijo_que_no_existe_es_desconocido()
    {
        Resolver("QQ1ZZZ").EsDesconocido.Should().BeTrue();
    }

    // ---------------------------------------------------------------- excepciones

    [Fact]
    public void La_excepcion_nominal_manda_sobre_el_prefijo()
    {
        // VK0 vale para Heard y para la Antartida; la tabla nombra este indicativo.
        var r = Resolver("VK0TBC");

        r.EsCoincidenciaExacta.Should().BeTrue();
        r.Entidad!.Numero.Should().Be(13);
        r.ZonaCq.Should().Be(29);
        r.ZonaItu.Should().Be(70);
    }

    [Fact]
    public void La_excepcion_nominal_tambien_vale_llevando_barra_P()
    {
        var r = Resolver("EA8RM/P");

        r.EsCoincidenciaExacta.Should().BeTrue();
        r.Entidad!.Numero.Should().Be(29);
    }

    [Fact]
    public void Un_indicativo_normal_no_se_marca_como_coincidencia_exacta()
    {
        Resolver("EA8DLF").EsCoincidenciaExacta.Should().BeFalse();
    }

    // ---------------------------------------------------------------- la fecha manda

    [Fact]
    public void Checoslovaquia_antes_del_noventa_y_tres_y_Chequia_despues()
    {
        Resolver("OK1ABC", new DateOnly(1990, 5, 1)).Entidad!.Numero.Should().Be(218);
        Resolver("OK1ABC", new DateOnly(2020, 5, 1)).Entidad!.Numero.Should().Be(503);
    }

    [Fact]
    public void La_Alemania_del_Este_existio_solo_en_su_epoca()
    {
        Resolver("Y21ABC", new DateOnly(1985, 6, 1)).Entidad!.Numero.Should().Be(229);
        Resolver("Y21ABC", new DateOnly(2020, 6, 1)).Entidad!.Numero.Should().Be(230);
    }

    [Fact]
    public void Un_contacto_anterior_a_la_division_de_Alemania_va_a_la_entidad_vieja()
    {
        Resolver("DL1ABC", new DateOnly(1960, 1, 1)).Entidad!.Numero.Should().Be(81);
        Resolver("DL1ABC", new DateOnly(2020, 1, 1)).Entidad!.Numero.Should().Be(230);
    }

    [Fact]
    public void Una_entidad_borrada_no_se_asigna_a_un_contacto_posterior_a_su_baja()
    {
        var checoslovaquia = Resolutor.PorNumero(218)!;

        checoslovaquia.EstaBorrada.Should().BeTrue();
        checoslovaquia.EraValidaEn(new DateOnly(1990, 1, 1)).Should().BeTrue();
        checoslovaquia.EraValidaEn(new DateOnly(2000, 1, 1)).Should().BeFalse();
    }

    [Fact]
    public void Libia_sigue_contando_aunque_el_fichero_de_Log4OM_la_marque_inactiva()
    {
        Resolver("5A1AL").Entidad!.Numero.Should().Be(436);
    }

    [Fact]
    public void Resolver_sin_fecha_usa_el_dia_de_hoy()
    {
        Resolutor.Resolver(Indicativo.Parse("EA8DLF")).Entidad!.Numero.Should().Be(29);
    }

    // ---------------------------------------------------------------- estructura

    [Fact]
    public void El_arbol_de_prefijos_se_construye_entero()
    {
        var catalogo = CatalogoDxcc.Predeterminado;

        catalogo.NodosDelArbol.Should().BeGreaterThan(20000);
        catalogo.NumeroDeExcepciones.Should().BeGreaterThan(2000);
        catalogo.Version.Should().Be(1);
    }

    [Fact]
    public void Un_catalogo_vacio_no_revienta()
    {
        var catalogo = CatalogoDxcc.Cargar("V\t1\t2026-01-01\t2026-01-01\n");
        var resolutor = new ResolutorDxcc(catalogo);

        resolutor.Todas.Should().BeEmpty();
        resolutor.Resolver(Indicativo.Crudo("EA8DLF"), Hoy).EsDesconocido.Should().BeTrue();
    }

    [Fact]
    public void Construir_el_resolutor_sin_catalogo_no_esta_permitido()
    {
        var crear = () => new ResolutorDxcc(null!);

        crear.Should().Throw<ArgumentNullException>();
    }
}
