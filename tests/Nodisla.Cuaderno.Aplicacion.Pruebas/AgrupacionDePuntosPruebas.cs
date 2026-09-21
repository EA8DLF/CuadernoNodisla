using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// El mapa tiene que ir fluido con el cuaderno entero encima, y el cuaderno de EA8DLF pasa de
/// veinte mil contactos con la mitad apretados en Europa. Estas pruebas fijan lo que hace que
/// eso sea posible: que nunca se pinten más marcas de las pedidas, que no se pierda ni un
/// contacto por el camino, y que un cuaderno pequeño no se agrupe sin necesidad.
/// </summary>
public sealed class AgrupacionDePuntosPruebas
{
    /// <summary>Puntos repartidos por el mundo entero, con semilla fija para que no varíen.</summary>
    private static IReadOnlyList<Coordenada> Sembrar(int cuantos, int semilla = 8_1968)
    {
        var azar = new Random(semilla);
        var lista = new List<Coordenada>(cuantos);
        for (var i = 0; i < cuantos; i++)
        {
            lista.Add(new Coordenada(azar.Next(-80, 80) + azar.NextDouble(), azar.Next(-179, 179) + azar.NextDouble()));
        }

        return lista;
    }

    [Fact]
    public void Con_pocos_puntos_cada_uno_se_pinta_por_su_cuenta()
    {
        var puntos = Sembrar(50);

        var grupos = AgrupacionDePuntos.AgruparHasta(puntos, p => p, 1200);

        grupos.Should().HaveCount(50);
        grupos.Should().OnlyContain(g => g.EsSuelto);
    }

    [Fact]
    public void Con_veinte_mil_contactos_nunca_se_pintan_mas_marcas_de_las_pedidas()
    {
        var puntos = Sembrar(20_000);

        var grupos = AgrupacionDePuntos.AgruparHasta(puntos, p => p, 800);

        grupos.Count.Should().BeLessThanOrEqualTo(800);
    }

    [Fact]
    public void Agrupar_no_pierde_ningun_contacto()
    {
        var puntos = Sembrar(5_000);

        var grupos = AgrupacionDePuntos.Agrupar(puntos, p => p, 5.0);

        grupos.Sum(g => g.Cuantos).Should().Be(5_000);
    }

    [Fact]
    public void Los_grupos_salen_de_mas_a_menos_poblados()
    {
        var puntos = Sembrar(5_000);

        var grupos = AgrupacionDePuntos.Agrupar(puntos, p => p, 10.0);

        grupos.Select(g => g.Cuantos).Should().BeInDescendingOrder();
    }

    [Fact]
    public void Los_puntos_muy_juntos_acaban_en_la_misma_marca()
    {
        // Ocho contactos dentro del mismo cuadrado de un grado, como pasa con un club local.
        var apretados = new List<Coordenada>();
        for (var i = 0; i < 8; i++) apretados.Add(new Coordenada(40.1 + (i * 0.01), -3.7 + (i * 0.01)));

        var grupos = AgrupacionDePuntos.Agrupar(apretados, p => p, 1.0);

        grupos.Should().HaveCount(1);
        grupos[0].Cuantos.Should().Be(8);
        grupos[0].Centro.Latitud.Should().BeApproximately(40.135, 0.01);
    }

    [Fact]
    public void Un_punto_sin_coordenada_valida_no_rompe_la_agrupacion()
    {
        var puntos = new List<Coordenada>
        {
            new(40, -3),
            new(double.NaN, double.NaN),
            new(41, -4),
        };

        var grupos = AgrupacionDePuntos.Agrupar(puntos, p => p, 1.0);

        grupos.Sum(g => g.Cuantos).Should().Be(2);
    }

    [Fact]
    public void El_lado_de_la_casilla_nunca_se_sale_de_los_limites()
    {
        var puntos = Sembrar(100);

        var minusculo = AgrupacionDePuntos.Agrupar(puntos, p => p, 0.000001);
        var enorme = AgrupacionDePuntos.Agrupar(puntos, p => p, 5000);

        minusculo.Should().HaveCount(100);
        enorme.Should().NotBeEmpty();
        enorme.Count.Should().BeLessThan(100);
    }

    [Fact]
    public void Con_todo_el_cuaderno_en_un_solo_sitio_queda_una_sola_marca()
    {
        var mismoPunto = Enumerable.Repeat(new Coordenada(28.47, -16.25), 3_000).ToList();

        var grupos = AgrupacionDePuntos.AgruparHasta(mismoPunto, p => p, 500);

        grupos.Should().HaveCount(1);
        grupos[0].Cuantos.Should().Be(3_000);
    }
}
