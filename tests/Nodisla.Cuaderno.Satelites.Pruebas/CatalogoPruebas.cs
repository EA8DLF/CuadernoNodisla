using FluentAssertions;
using Nodisla.Cuaderno.Satelites.Catalogo;
using Nodisla.Cuaderno.Satelites.Fuentes;

namespace Nodisla.Cuaderno.Satelites.Pruebas;

/// <summary>El catalogo de satelites incrustado y las fuentes de elementos.</summary>
public sealed class CatalogoPruebas
{
    [Fact]
    public void ElCatalogoSeLeeDelRecursoIncrustado()
    {
        var catalogo = CatalogoDeSatelites.Instancia;

        catalogo.Satelites.Should().NotBeEmpty();
        catalogo.EnServicio.Should().NotBeEmpty();
        catalogo.Satelites.Should().OnlyHaveUniqueItems(s => s.Abreviatura);
    }

    [Fact]
    public void SO50TieneSuRepetidorConSubtono()
    {
        var so50 = CatalogoDeSatelites.Instancia.PorAbreviatura("so-50");

        so50.Should().NotBeNull();
        so50!.Estado.Should().Be(EstadoDelSatelite.Activo);

        var repetidor = so50.Transpondedores.Single(t => t.Clase == ClaseDeTranspondedor.Fm);
        repetidor.Modo.Should().Be("VU");
        repetidor.SubidaCentral!.Value.Megahercios.Should().Be(145.850m);
        repetidor.BajadaCentral!.Value.Megahercios.Should().Be(436.795m);
        repetidor.SubtonoHz.Should().Be(67.0);
    }

    [Fact]
    public void UnTranspondedorLinealTraeElMargenEnteroYSiEstaInvertido()
    {
        var rs44 = CatalogoDeSatelites.Instancia.PorAbreviatura("RS-44");
        var lineal = rs44!.Transpondedores.Single(t => t.Clase == ClaseDeTranspondedor.Lineal);

        lineal.Invertido.Should().BeTrue();
        lineal.SubidaInicio!.Value.Megahercios.Should().BeLessThan(lineal.SubidaFin!.Value.Megahercios);
        lineal.BajadaInicio!.Value.Megahercios.Should().BeLessThan(lineal.BajadaFin!.Value.Megahercios);

        // El centro del margen es por donde se empieza a llamar.
        lineal.BajadaCentral!.Value.Megahercios.Should().Be(435.640m);
    }

    [Fact]
    public void SeBuscaPorNumeroNoradPorqueEsLoQueTraeElFicheroDeElementos()
    {
        CatalogoDeSatelites.Instancia.PorNumeroDeCatalogo(25544)!.Abreviatura.Should().Be("ISS");
        CatalogoDeSatelites.Instancia.PorNumeroDeCatalogo(999999).Should().BeNull();
    }

    [Fact]
    public void LosSatelitesApagadosSiguenEnElCatalogoParaLosContactosAntiguos()
    {
        var ao13 = CatalogoDeSatelites.Instancia.PorAbreviatura("AO-13");

        ao13.Should().NotBeNull();
        ao13!.Estado.Should().Be(EstadoDelSatelite.Inactivo);
        CatalogoDeSatelites.Instancia.EnServicio.Should().NotContain(ao13);
    }

    [Fact]
    public void LasFuentesDeElementosApuntanAlPuntoDeAccesoQueSigueVivo()
    {
        // El fichero clásico amateur.txt de Celestrak devuelve 404 desde que todo pasa por
        // gp.php. Se deja fijado con una prueba para que nadie lo «arregle» al revés.
        FuentesDeElementos.Todas.Should().HaveCount(3);
        FuentesDeElementos.CelestrakAficionado.Url.Should().Contain("gp.php");
        FuentesDeElementos.CelestrakAficionado.Url.Should().NotContain("amateur.txt");
        FuentesDeElementos.Todas.Should().OnlyContain(f => f.Url.StartsWith("https://"));
    }

    [Fact]
    public void LaLecturaDeElementosSabeDecirDeCuandoEs()
    {
        var elementos = Orbital.LectorDeElementos.Leer(
            "ISS (ZARYA)\n"
            + "1 25544U 98067A   24015.54791667  .00016717  00000-0  30177-3 0  9002\n"
            + "2 25544  51.6416 247.4627 0006703 130.5360 325.0288 15.49682159 33601\n");

        var obtenido = new DateTimeOffset(2024, 1, 15, 18, 0, 0, TimeSpan.Zero);
        var lectura = new LecturaDeElementos(
            elementos, obtenido, OrigenDeLosElementos.Red, "Celestrak");

        lectura.Describir(obtenido, TimeSpan.FromDays(3))
            .Should().Contain("1 satélite").And.NotContain("otra vez");
        lectura.Describir(obtenido.AddDays(10), TimeSpan.FromDays(3))
            .Should().Contain("otra vez");
    }
}
