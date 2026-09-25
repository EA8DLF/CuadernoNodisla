using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Calculo;
using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Concursos.Sesion;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>La puntuacion, vista desde Canarias, que es desde donde se opera.</summary>
public sealed class PuntuacionPruebas
{
    /// <summary>EA8DLF: entidad 29, continente africano, zona CQ 33, zona ITU 36.</summary>
    private static readonly DatosDeMiEstacion Ea8 =
        new(Indicativo.Parse("EA8DLF"), 29, "AF", 33, 36, Locator.Parse("IL18"));

    private static ApunteDeConcurso Contacto(string call, string banda, string modo, int dxcc, string continente) =>
        new(Indicativo.Parse(call), Banda.Parse(banda), Modo.Parse(modo))
        {
            Dxcc = dxcc,
            Continente = continente,
        };

    [Theory]
    // Desde Canarias, la peninsula esta en otro continente: tres puntos, no uno.
    [InlineData("EA1ABC", 281, "EU", 3)]
    // Otra estacion de Canarias es el propio pais: cero puntos, pero cuenta de multiplicador.
    [InlineData("EA8ABC", 29, "AF", 0)]
    // Marruecos es Africa como nosotros y otro pais: un punto.
    [InlineData("CN8ABC", 446, "AF", 1)]
    [InlineData("K1ABC", 291, "NA", 3)]
    public void ElCqWwPuntuaDesdeCanarias(string call, int dxcc, string continente, int esperado)
    {
        var cqww = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WW-CW")!;
        CalculadoraDePuntos.De(cqww, Ea8, Contacto(call, "20m", "CW", dxcc, continente))
            .Should().Be(esperado);
    }

    [Fact]
    public void ElCasoDeNorteamericaSeMiraAntesQueElDelMismoContinente()
    {
        var cqww = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WW-CW")!;
        var desdeEeuu = new DatosDeMiEstacion(Indicativo.Parse("K1ABC"), 291, "NA", 5, 8);

        CalculadoraDePuntos.De(cqww, desdeEeuu, Contacto("VE3ABC", "20m", "CW", 1, "NA"))
            .Should().Be(2, "dentro de Norteamerica el CQ WW da dos puntos, no uno");
    }

    [Theory]
    // Distinto continente: tres puntos arriba, seis en las bandas bajas.
    [InlineData("20m", "EA1ABC", 281, "EU", 3)]
    [InlineData("40m", "EA1ABC", 281, "EU", 6)]
    // Mismo continente y otro pais: uno arriba, dos en bandas bajas.
    [InlineData("20m", "CN8ABC", 446, "AF", 1)]
    [InlineData("80m", "CN8ABC", 446, "AF", 2)]
    // Mismo pais: un punto en cualquier banda, tambien en las bajas.
    [InlineData("160m", "EA8ABC", 29, "AF", 1)]
    public void ElWpxDobleLaPuntuacionEnLasBandasBajas(
        string banda, string call, int dxcc, string continente, int esperado)
    {
        var wpx = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WPX-CW")!;
        CalculadoraDePuntos.De(wpx, Ea8, Contacto(call, banda, "CW", dxcc, continente))
            .Should().Be(esperado);
    }

    [Fact]
    public void ElArrl10PuntuaSegunElModo()
    {
        var arrl = CatalogoDeConcursos.Predeterminado.Buscar("ARRL-10")!;
        CalculadoraDePuntos.De(arrl, Ea8, Contacto("K1ABC", "10m", "CW", 291, "NA")).Should().Be(4);
        CalculadoraDePuntos.De(arrl, Ea8, Contacto("K1ABC", "10m", "SSB", 291, "NA")).Should().Be(2);
    }

    [Fact]
    public void ElIaruPuntuaPorZonaItu()
    {
        var iaru = CatalogoDeConcursos.Predeterminado.Buscar("IARU-HF")!;
        var mismaZona = Contacto("EA8ABC", "20m", "CW", 29, "AF") with { ZonaItu = 36 };
        var otraZonaMismoContinente = Contacto("CN8ABC", "20m", "CW", 446, "AF") with { ZonaItu = 37 };
        var otroContinente = Contacto("EA1ABC", "20m", "CW", 281, "EU") with { ZonaItu = 37 };

        CalculadoraDePuntos.De(iaru, Ea8, mismaZona).Should().Be(1);
        CalculadoraDePuntos.De(iaru, Ea8, otraZonaMismoContinente).Should().Be(3);
        CalculadoraDePuntos.De(iaru, Ea8, otroContinente).Should().Be(5);
    }

    [Fact]
    public void SinContinenteNoSeInventanPuntosDeOtroContinente()
    {
        var cqww = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WW-CW")!;
        var aCiegas = new ApunteDeConcurso(Indicativo.Parse("XX1ABC"), Banda.Parse("20m"), Modo.Parse("CW"));

        CalculadoraDePuntos.De(cqww, Ea8, aCiegas).Should().Be(0);
    }

    [Fact]
    public void UnConcursoSinReglasNoPuntua()
    {
        var sinReglas = CatalogoDeConcursos.Predeterminado.Buscar("7QP")!;
        CalculadoraDePuntos.De(sinReglas, Ea8, Contacto("K1ABC", "20m", "CW", 291, "NA")).Should().Be(0);
    }

    [Fact]
    public void LosMultiplicadoresPorBandaSeRepitenEnCadaBanda()
    {
        var cqww = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WW-CW")!;
        var contador = new ContadorDeMultiplicadores(cqww.Multiplicadores);
        var en20 = Contacto("EA1ABC", "20m", "CW", 281, "EU") with { ZonaCq = 14 };
        var en40 = en20 with { Banda = Banda.Parse("40m") };

        contador.Registrar(en20).Should().HaveCount(2, "zona y pais");
        contador.Registrar(en20).Should().BeEmpty("ya estaban en esa banda");
        contador.Registrar(en40).Should().HaveCount(2, "en otra banda vuelven a contar");
        contador.Total.Should().Be(4);
    }

    [Fact]
    public void LosPrefijosDelWpxSoloCuentanUnaVez()
    {
        var wpx = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WPX-CW")!;
        var contador = new ContadorDeMultiplicadores(wpx.Multiplicadores);
        var en20 = Contacto("EA1ABC", "20m", "CW", 281, "EU");
        var otroConMismoPrefijo = Contacto("EA1XYZ", "40m", "CW", 281, "EU");

        contador.Registrar(en20).Should().ContainSingle();
        contador.Registrar(otroConMismoPrefijo).Should().BeEmpty("EA1 ya estaba, y el prefijo es global");
        contador.Total.Should().Be(1);
    }

    [Fact]
    public void PrevisualizarNoApuntaNada()
    {
        var cqww = CatalogoDeConcursos.Predeterminado.Buscar("CQ-WW-CW")!;
        var contador = new ContadorDeMultiplicadores(cqww.Multiplicadores);
        var contacto = Contacto("EA1ABC", "20m", "CW", 281, "EU") with { ZonaCq = 14 };

        contador.Previsualizar(contacto).Should().HaveCount(2);
        contador.Total.Should().Be(0);
        contador.Registrar(contacto).Should().HaveCount(2);
    }
}
