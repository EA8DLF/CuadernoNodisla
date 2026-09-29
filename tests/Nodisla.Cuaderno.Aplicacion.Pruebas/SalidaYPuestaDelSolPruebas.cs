using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// El orto y el ocaso que se ensenan en la franja solar.
/// </summary>
/// <remarks>
/// Se comprueban contra los almanaques con un margen de unos minutos: la cuenta del paso gris
/// es la del Sol medio, no la de efemerides finas, y esa diferencia no cambia ninguna decision
/// de operacion. Lo que si importa es que los casos raros —noche polar, sol de medianoche— no
/// devuelvan una hora inventada.
/// </remarks>
public sealed class SalidaYPuestaDelSolPruebas
{
    /// <summary>Margen que se admite frente al almanaque.</summary>
    private static readonly TimeSpan Margen = TimeSpan.FromMinutes(12);

    [Fact]
    public void En_el_equinoccio_el_dia_dura_unas_doce_horas()
    {
        // Equinoccio de marzo: en el ecuador el dia y la noche se reparten el reloj.
        var (orto, ocaso) = SalidaYPuestaDelSol.Calcular(
            new Coordenada(0, 0),
            new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));

        orto.Should().NotBeNull();
        ocaso.Should().NotBeNull();

        var duracion = ocaso!.Value - orto!.Value;
        duracion.Should().BeCloseTo(TimeSpan.FromHours(12), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void En_las_canarias_las_horas_son_las_del_almanaque()
    {
        // Las Palmas, 28,1 N 15,4 O, el 22 de septiembre de 2026. Dos dias despues del
        // equinoccio, alli el Sol sale sobre las 07:50 y se pone sobre las 19:55 hora local,
        // que en septiembre es UTC+1: 06:50 y 18:55 en UTC, que es como se ensena en la franja.
        var (orto, ocaso) = SalidaYPuestaDelSol.Calcular(
            new Coordenada(28.1, -15.4),
            new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));

        orto.Should().NotBeNull();
        ocaso.Should().NotBeNull();

        orto!.Value.UtcDateTime.TimeOfDay.Should().BeCloseTo(new TimeSpan(6, 50, 0), Margen);
        ocaso!.Value.UtcDateTime.TimeOfDay.Should().BeCloseTo(new TimeSpan(18, 55, 0), Margen);
    }

    [Fact]
    public void El_sol_sale_antes_por_el_este()
    {
        var momento = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);

        var (ortoEste, _) = SalidaYPuestaDelSol.Calcular(new Coordenada(40, 20), momento);
        var (ortoOeste, _) = SalidaYPuestaDelSol.Calcular(new Coordenada(40, 0), momento);

        ortoEste.Should().NotBeNull();
        ortoOeste.Should().NotBeNull();

        // Veinte grados de longitud son ochenta minutos de reloj.
        (ortoOeste!.Value - ortoEste!.Value).Should().BeCloseTo(TimeSpan.FromMinutes(80), TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void En_la_noche_polar_no_hay_orto_ni_ocaso()
    {
        // Longyearbyen en diciembre: el Sol no asoma en todo el dia. Inventarse una hora seria
        // peor que no dar ninguna.
        var (orto, ocaso) = SalidaYPuestaDelSol.Calcular(
            new Coordenada(78.2, 15.6),
            new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.Zero));

        orto.Should().BeNull();
        ocaso.Should().BeNull();
    }

    [Fact]
    public void Con_sol_de_medianoche_tampoco_hay_horas()
    {
        var (orto, ocaso) = SalidaYPuestaDelSol.Calcular(
            new Coordenada(78.2, 15.6),
            new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

        orto.Should().BeNull();
        ocaso.Should().BeNull();
    }

    [Fact]
    public void Las_horas_que_devuelve_son_del_dia_pedido()
    {
        var dia = new DateTimeOffset(2026, 9, 22, 23, 50, 0, TimeSpan.Zero);

        var (orto, ocaso) = SalidaYPuestaDelSol.Calcular(new Coordenada(28.1, -15.4), dia);

        orto!.Value.UtcDateTime.Date.Should().Be(dia.UtcDateTime.Date);
        ocaso!.Value.UtcDateTime.Date.Should().Be(dia.UtcDateTime.Date);
    }
}
