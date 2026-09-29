using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>El catalogo incrustado se lee y dice lo que tiene que decir.</summary>
public sealed class CatalogoPruebas
{
    private static readonly CatalogoDeConcursos Catalogo = CatalogoDeConcursos.Predeterminado;

    [Fact]
    public void ElRecursoIncrustadoSeLee()
    {
        Catalogo.Cabecera.Version.Should().Be(1);
        Catalogo.Cabecera.Concursos.Should().Be(Catalogo.Concursos.Count);
        Catalogo.Concursos.Should().HaveCountGreaterThan(200);
    }

    [Fact]
    public void LosConcursosDeLog4OmSiguenEstando()
    {
        // Si se pierde un identificador, los contactos importados de Log4OM dejan de casar.
        Catalogo.Buscar("CQ-WW-SSB").Should().NotBeNull();
        Catalogo.Buscar("DARC-WAEDC-CW").Should().NotBeNull();
        Catalogo.Buscar("EA-MAJESTAD-CW").Should().NotBeNull();
        Catalogo.Buscar("7QP").Should().NotBeNull();
    }

    [Fact]
    public void LosConcursosSinReglasEntranComoSoloCatalogo()
    {
        var sinReglas = Catalogo.Buscar("7QP")!;
        sinReglas.Estado.Should().Be(EstadoDeLasReglas.SoloCatalogo);
        sinReglas.SabePuntuar.Should().BeFalse();
        sinReglas.NecesitaAviso.Should().BeTrue();
    }

    [Fact]
    public void CqWwCwTraeSusReglas()
    {
        var cqww = Catalogo.Buscar("CQ-WW-CW")!;
        cqww.Estado.Should().Be(EstadoDeLasReglas.Aproximado);
        cqww.Modos.Should().ContainSingle().Which.Should().Be("CW");
        cqww.Bandas.Should().HaveCount(6).And.NotContain(Banda.Parse("30m"));
        cqww.Enviado.Should().Equal(TipoDeIntercambio.Informe, TipoDeIntercambio.ZonaCq);
        cqww.Puntuacion.Should().HaveCount(4);
        cqww.Multiplicadores.Should().HaveCount(2);
        cqww.Multiplicadores.Should().OnlyContain(m => m.Alcance == AlcanceDeMultiplicador.PorBanda);
        cqww.Duplicado.Should().Be(AmbitoDeDuplicado.Banda);
        cqww.UsaNumeroDeSerie.Should().BeFalse();
    }

    [Fact]
    public void WpxCuentaLosPrefijosUnaSolaVez()
    {
        var wpx = Catalogo.Buscar("CQ-WPX-CW")!;
        wpx.UsaNumeroDeSerie.Should().BeTrue();
        wpx.Multiplicadores.Should().ContainSingle()
            .Which.Should().Be(new ReglaDeMultiplicador(TipoDeMultiplicador.PrefijoWpx, AlcanceDeMultiplicador.Global));
    }

    [Fact]
    public void ElOrdenDeLasReglasDePuntosSeConserva()
    {
        // Es lo que hace que Norteamerica se mire antes que el mismo continente.
        var cqww = Catalogo.Buscar("CQ-WW-CW")!;
        cqww.Puntuacion.Select(p => p.Ambito).Should().Equal(
            AmbitoDePuntos.MismoPais,
            AmbitoDePuntos.MismoContinenteNa,
            AmbitoDePuntos.MismoContinente,
            AmbitoDePuntos.OtroContinente);
    }

    [Fact]
    public void LasBandasBajasDelWpxVanEnSuPropiaRegla()
    {
        var wpx = Catalogo.Buscar("CQ-WPX-SSB")!;
        var bajas = wpx.Puntuacion.Where(p => p.Bandas.Count > 0).ToList();
        bajas.Should().NotBeEmpty();
        bajas.Should().OnlyContain(p => p.Bandas.Contains(Banda.Parse("40m")));
        bajas.Should().OnlyContain(p => !p.Bandas.Contains(Banda.Parse("20m")));
    }

    [Theory]
    [InlineData("CQ-WW", "CQ-WW-CW")]
    [InlineData("wpx", "CQ-WPX-CW")]
    [InlineData("Rey", "EA-MAJESTAD-CW")]
    public void LaBusquedaEncuentraLoQueElOperadorEscribe(string escrito, string esperado)
    {
        Catalogo.Buscar(escrito, 20).Select(c => c.Codigo).Should().Contain(esperado);
    }

    [Fact]
    public void UnConcursoDesconocidoNoRompeNada()
    {
        var inventado = Catalogo.BuscarOMinimo("mi-concurso-del-pueblo");
        inventado.Codigo.Should().Be("MI-CONCURSO-DEL-PUEBLO");
        inventado.Estado.Should().Be(EstadoDeLasReglas.SoloCatalogo);
        inventado.AdmiteBanda(Banda.Parse("20m")).Should().BeTrue();
        inventado.AdmiteModo(Modo.Parse("CW")).Should().BeTrue();
    }

    [Fact]
    public void LasBandasYModosSeFiltranSegunLasBases()
    {
        var cqww = Catalogo.Buscar("CQ-WW-CW")!;
        cqww.AdmiteBanda(Banda.Parse("20m")).Should().BeTrue();
        cqww.AdmiteBanda(Banda.Parse("30m")).Should().BeFalse("las WARC no se usan en concursos");
        cqww.AdmiteModo(Modo.Parse("CW")).Should().BeTrue();
        cqww.AdmiteModo(Modo.Parse("SSB")).Should().BeFalse();
    }

    [Fact]
    public void LaFmCuentaComoFoniaEnUnConcursoDeSsb()
    {
        var cqww = Catalogo.Buscar("CQ-WW-SSB")!;
        cqww.AdmiteModo(Modo.Parse("FM")).Should().BeTrue();
    }
}
