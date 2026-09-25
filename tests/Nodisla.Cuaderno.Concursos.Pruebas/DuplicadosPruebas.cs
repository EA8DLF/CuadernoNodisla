using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Concursos.Pruebas.Medidas;
using Nodisla.Cuaderno.Concursos.Sesion;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>El aviso de duplicado: lo que mas se mira y lo que mas rapido tiene que ir.</summary>
public sealed class DuplicadosPruebas
{
    private static readonly Banda Veinte = Banda.Parse("20m");
    private static readonly Banda Cuarenta = Banda.Parse("40m");
    private static readonly Modo Cw = Modo.Parse("CW");
    private static readonly Modo Ssb = Modo.Parse("SSB");

    [Fact]
    public void PorBandaSoloEsDuplicadoEnLaMismaBanda()
    {
        var indice = new IndiceDeDuplicados(AmbitoDeDuplicado.Banda);
        indice.Anadir("EA1ABC", Veinte, Cw);

        indice.Comprobar("EA1ABC", Veinte, Cw).Should().Be(EstadoDeDuplicado.Duplicado);
        indice.Comprobar("EA1ABC", Veinte, Ssb).Should().Be(EstadoDeDuplicado.Duplicado,
            "en un concurso de un solo modo la banda es lo unico que separa");
        indice.Comprobar("EA1ABC", Cuarenta, Cw).Should().Be(EstadoDeDuplicado.TrabajadaEnOtraBanda);
        indice.Comprobar("EA1XYZ", Veinte, Cw).Should().Be(EstadoDeDuplicado.Nuevo);
    }

    [Fact]
    public void PorBandaYModoElMismoCorresponsalValeDosVeces()
    {
        var indice = new IndiceDeDuplicados(AmbitoDeDuplicado.BandaYModo);
        indice.Anadir("EA1ABC", Veinte, Cw);

        indice.Comprobar("EA1ABC", Veinte, Cw).Should().Be(EstadoDeDuplicado.Duplicado);
        indice.Comprobar("EA1ABC", Veinte, Ssb).Should().Be(EstadoDeDuplicado.TrabajadaEnOtraBanda);
    }

    [Fact]
    public void ConAmbitoUnicoNoSeRepiteNiCambiandoDeBanda()
    {
        var indice = new IndiceDeDuplicados(AmbitoDeDuplicado.Unico);
        indice.Anadir("EA1ABC", Veinte, Cw);

        indice.Comprobar("EA1ABC", Cuarenta, Ssb).Should().Be(EstadoDeDuplicado.Duplicado);
    }

    [Fact]
    public void ElAvisoNoDistingueMayusculasNiEspera_AQueSeTermineDeEscribir()
    {
        var indice = new IndiceDeDuplicados(AmbitoDeDuplicado.Banda);
        indice.Anadir("ea1abc", Veinte, Cw);

        indice.Comprobar("EA1ABC", Veinte, Cw).Should().Be(EstadoDeDuplicado.Duplicado);
        indice.Comprobar("ea1abc", Veinte, Cw).Should().Be(EstadoDeDuplicado.Duplicado);
        indice.Comprobar("EA1AB", Veinte, Cw).Should().Be(EstadoDeDuplicado.Nuevo,
            "a medias todavia no es esa estacion");
    }

    [Fact]
    public void LosConocidosSeBuscanPorPrefijo()
    {
        var indice = new IndiceDeDuplicados(AmbitoDeDuplicado.Banda);
        foreach (var call in new[] { "EA1ABC", "EA1XYZ", "EA8DLF", "K1ABC" })
        {
            indice.Anadir(call, Veinte, Cw);
        }

        indice.PorPrefijo("EA1").Should().Equal("EA1ABC", "EA1XYZ");
        indice.PorPrefijo("EA").Should().HaveCount(3);
        indice.PorPrefijo("ZZ").Should().BeEmpty();
        indice.PorPrefijo("EA", maximo: 2).Should().HaveCount(2);
    }

    /// <summary>
    /// El presupuesto es de tiempo de procesador, no de reloj de pared: lo que se mide es lo
    /// que cuesta el codigo, no lo ocupada que este la maquina.
    /// </summary>
    [Fact]
    public void ElAvisoDeDuplicadoCuestaMenosDeLoQueTardaUnaTecla()
    {
        var indice = new IndiceDeDuplicados(AmbitoDeDuplicado.BandaYModo);
        // Un concurso grande de verdad: cinco mil contactos en seis bandas.
        for (var i = 0; i < 5000; i++)
        {
            indice.Anadir($"EA{i % 9}{(char)('A' + i % 26)}{(char)('A' + i / 26 % 26)}{i % 10}",
                Banda.Todas[8 + i % 6], i % 2 == 0 ? Cw : Ssb);
        }

        // Diez mil comprobaciones son las de un operador escribiendo durante todo el concurso.
        var coste = TiempoDeProceso.MejorDe(3, () =>
        {
            for (var i = 0; i < 10_000; i++)
            {
                indice.Comprobar("EA1ABC1", Veinte, Cw);
                indice.Comprobar("EA8DLF", Cuarenta, Ssb);
            }
        });

        // Veinte mil avisos en menos de 50 ms de procesador son 2,5 microsegundos por tecla.
        coste.Should().BeLessThan(50, "el aviso se dispara con cada tecla en mitad de un pileup");
    }

    [Fact]
    public void LaBusquedaPorPrefijoTambienEsBarataConElIndiceLleno()
    {
        var indice = new IndiceDeDuplicados(AmbitoDeDuplicado.Banda);
        for (var i = 0; i < 5000; i++)
        {
            indice.Anadir($"K{i % 9}{(char)('A' + i % 26)}{(char)('A' + i / 26 % 26)}", Veinte, Cw);
        }

        var coste = TiempoDeProceso.MejorDe(3, () =>
        {
            for (var i = 0; i < 2000; i++) indice.PorPrefijo("K1A");
        });

        coste.Should().BeLessThan(50);
    }
}
