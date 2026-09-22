using System.Diagnostics;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// Lo que se pinta en el mapa cuando el cluster escupe anuncios sin parar.
/// </summary>
/// <remarks>
/// <para>
/// El mapa se quedó en negro una vez y costó encontrarlo porque solo pasaba con la aplicación
/// llevando un rato abierta. La causa resultó ser otra —el panel soltaba el control del mapa al
/// cambiar de pestaña—, pero de aquel susto sale esta prueba: <b>el reparto de marcas nunca
/// puede devolver una lista vacía si hay contactos</b>, por muchas veces seguidas que se le
/// pida y por muchos anuncios que se le echen encima.
/// </para>
/// <para>
/// Un mapa con los datos de hace dos segundos es aceptable. Un mapa en blanco no lo es, porque
/// el operador no distingue «no hay nada» de «se ha roto».
/// </para>
/// </remarks>
public sealed class MarcasEnRafagaPruebas
{
    /// <summary>Tamaño del cuaderno de demostración, que es el caso peor que se ve en pantalla.</summary>
    private const int Contactos = 20_000;

    /// <summary>Anuncios que llegan en la ráfaga.</summary>
    private const int AnunciosDeLaRafaga = 500;

    /// <summary>Tope de marcas que admite la vista.</summary>
    private const int Tope = 1_200;

    [Fact]
    public void Una_rafaga_de_anuncios_nunca_deja_el_mapa_sin_marcas()
    {
        var cuaderno = Puntos(Contactos, semilla: 7);
        var anuncios = Puntos(AnunciosDeLaRafaga, semilla: 99);

        // Cada anuncio que llega rehace la lista entera: contactos más los spots que haya
        // hasta ese momento. Es exactamente lo que hace la vista con cada anuncio.
        for (var llegados = 1; llegados <= AnunciosDeLaRafaga; llegados++)
        {
            var marcas = cuaderno.Concat(anuncios.Take(llegados)).ToList();

            var grupos = AgrupacionDePuntos.AgruparHasta(marcas, p => p, Tope);

            grupos.Should().NotBeEmpty("con {0} marcas encima el mapa no puede quedarse vacío", marcas.Count);
            grupos.Count.Should().BeLessThanOrEqualTo(Tope);
            grupos.Sum(g => g.Cuantos).Should().Be(marcas.Count, "no se puede perder ninguna marca por el camino");
        }
    }

    [Fact]
    public void El_reparto_del_caso_peor_no_tarda_lo_que_tarda_un_anuncio_en_llegar()
    {
        // Si un reparto tardara más de lo que tarda en llegar el anuncio siguiente, cada
        // cálculo cancelaría al anterior y la capa no se refrescaría nunca. El cluster real
        // manda unos pocos anuncios por segundo en una apertura buena.
        var marcas = Puntos(Contactos + AnunciosDeLaRafaga, semilla: 3);

        var reloj = Stopwatch.StartNew();
        var grupos = AgrupacionDePuntos.AgruparHasta(marcas, p => p, Tope);
        reloj.Stop();

        grupos.Should().NotBeEmpty();
        reloj.ElapsedMilliseconds.Should().BeLessThan(500);
    }

    [Fact]
    public void Agrupar_por_casilla_de_pantalla_tampoco_devuelve_nada_vacio()
    {
        // Es el reparto que usa la vista cuando sabe cuántos metros mide un punto de pantalla.
        // Se prueban desde la vista de mundo hasta el zoom más cerrado.
        var marcas = Puntos(Contactos, semilla: 11);

        foreach (var lado in (double[])[90, 45, 20, 10, 5, 1, 0.5, 0.1, 0.05])
        {
            var grupos = AgrupacionDePuntos.Agrupar(marcas, p => p, lado);

            grupos.Should().NotBeEmpty("con casillas de {0} grados sigue habiendo veinte mil contactos", lado);
            grupos.Sum(g => g.Cuantos).Should().Be(marcas.Count);
        }
    }

    [Fact]
    public void Un_cuaderno_vacio_si_puede_dar_cero_marcas()
    {
        // La única lista vacía legítima: no hay nada que pintar porque no hay nada.
        AgrupacionDePuntos.AgruparHasta(Array.Empty<Coordenada>(), p => p, Tope).Should().BeEmpty();
    }

    /// <summary>Puntos repartidos por el mundo, siempre los mismos para la misma semilla.</summary>
    private static List<Coordenada> Puntos(int cuantos, int semilla)
    {
        var azar = new Random(semilla);
        var puntos = new List<Coordenada>(cuantos);

        for (var i = 0; i < cuantos; i++)
        {
            puntos.Add(new Coordenada(
                (azar.NextDouble() * 160) - 80,
                (azar.NextDouble() * 360) - 180));
        }

        return puntos;
    }
}
