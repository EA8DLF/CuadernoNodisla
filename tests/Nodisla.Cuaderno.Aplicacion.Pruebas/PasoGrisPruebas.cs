using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// El paso gris es lo que más se mira de un mapa de radioaficionado, así que tiene que caer
/// donde de verdad cae. Se comprueba contra hechos del calendario que no se discuten: en los
/// solsticios el Sol está sobre los trópicos, en los equinoccios sobre el ecuador, al mediodía
/// de Greenwich el Sol está sobre Greenwich, y el polo de invierno está a oscuras.
/// </summary>
public sealed class PasoGrisPruebas
{
    private static readonly Coordenada Canarias = new(28.47, -16.25);

    [Fact]
    public void En_el_solsticio_de_junio_el_Sol_esta_sobre_el_tropico_de_Cancer()
    {
        var sol = PasoGris.SubpuntoSolar(new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

        sol.Latitud.Should().BeApproximately(23.44, 0.15);
    }

    [Fact]
    public void En_el_solsticio_de_diciembre_el_Sol_esta_sobre_el_tropico_de_Capricornio()
    {
        var sol = PasoGris.SubpuntoSolar(new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.Zero));

        sol.Latitud.Should().BeApproximately(-23.44, 0.15);
    }

    [Theory]
    [InlineData(2026, 3, 20)]
    [InlineData(2026, 9, 22)]
    public void En_los_equinoccios_el_Sol_esta_casi_sobre_el_ecuador(int ano, int mes, int dia)
    {
        var sol = PasoGris.SubpuntoSolar(new DateTimeOffset(ano, mes, dia, 12, 0, 0, TimeSpan.Zero));

        Math.Abs(sol.Latitud).Should().BeLessThan(1.0);
    }

    [Fact]
    public void Al_mediodia_de_Greenwich_el_Sol_cae_sobre_el_meridiano_cero()
    {
        var sol = PasoGris.SubpuntoSolar(new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero));

        // No es exacto: la ecuación del tiempo desplaza el mediodía solar hasta un cuarto de
        // hora, que son unos cuatro grados de longitud.
        Math.Abs(sol.Longitud).Should().BeLessThan(5.0);
    }

    [Fact]
    public void El_subpunto_solar_da_una_vuelta_entera_al_dia()
    {
        var manana = PasoGris.SubpuntoSolar(new DateTimeOffset(2026, 6, 21, 6, 0, 0, TimeSpan.Zero));
        var tarde = PasoGris.SubpuntoSolar(new DateTimeOffset(2026, 6, 21, 18, 0, 0, TimeSpan.Zero));

        var giro = Math.Abs(manana.Longitud - tarde.Longitud);
        if (giro > 180) giro = 360 - giro;

        // Doce horas son media vuelta: 180 grados de longitud.
        giro.Should().BeApproximately(180, 2.0);
    }

    [Fact]
    public void En_Canarias_es_de_dia_al_mediodia_y_de_noche_a_medianoche()
    {
        PasoGris.EsDeDia(Canarias, new DateTimeOffset(2026, 6, 21, 13, 0, 0, TimeSpan.Zero)).Should().BeTrue();
        PasoGris.EsDeDia(Canarias, new DateTimeOffset(2026, 6, 22, 1, 0, 0, TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void En_el_solsticio_de_junio_el_polo_norte_tiene_dia_y_el_sur_noche()
    {
        var instante = new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

        PasoGris.EsDeDia(new Coordenada(89, 0), instante).Should().BeTrue();
        PasoGris.EsDeDia(new Coordenada(-89, 0), instante).Should().BeFalse();
    }

    [Fact]
    public void La_linea_de_dia_y_noche_recorre_el_mundo_de_lado_a_lado()
    {
        var linea = PasoGris.Linea(new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));

        linea[0].Longitud.Should().BeApproximately(-180, 0.001);
        linea[^1].Longitud.Should().BeApproximately(180, 0.001);
        linea.Should().OnlyContain(p => p.Latitud > -90 && p.Latitud < 90);
    }

    [Fact]
    public void Sobre_la_linea_el_Sol_esta_en_el_horizonte()
    {
        var instante = new DateTimeOffset(2026, 4, 15, 9, 30, 0, TimeSpan.Zero);

        var linea = PasoGris.Linea(instante, 73);

        foreach (var punto in linea)
        {
            // Los polos se recortan a 89,999° para que quepan en el mapa: ahí el error crece.
            if (Math.Abs(punto.Latitud) > 89) continue;
            Math.Abs(PasoGris.AlturaDelSol(punto, instante)).Should().BeLessThan(0.5);
        }
    }

    [Fact]
    public void La_linea_de_crepusculo_deja_el_Sol_seis_grados_bajo_el_horizonte()
    {
        var instante = new DateTimeOffset(2026, 4, 15, 9, 30, 0, TimeSpan.Zero);

        var linea = PasoGris.Linea(instante, 73, PasoGris.GradosDeCrepusculo);

        foreach (var punto in linea)
        {
            if (Math.Abs(punto.Latitud) > 89) continue;
            PasoGris.AlturaDelSol(punto, instante)
                .Should().BeApproximately(-PasoGris.GradosDeCrepusculo, 0.5);
        }
    }

    [Fact]
    public void La_zona_nocturna_se_cierra_por_el_polo_que_esta_a_oscuras()
    {
        var junio = PasoGris.ZonaNocturna(new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));
        var diciembre = PasoGris.ZonaNocturna(new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.Zero));

        // En junio la noche permanente está en el sur; en diciembre, en el norte.
        junio.Min(p => p.Latitud).Should().BeApproximately(-90, 0.001);
        diciembre.Max(p => p.Latitud).Should().BeApproximately(90, 0.001);

        junio[0].Should().Be(junio[^1]);
        diciembre[0].Should().Be(diciembre[^1]);
    }

    [Fact]
    public void Amanecer_y_anochecer_cuentan_como_paso_gris()
    {
        var instante = new DateTimeOffset(2026, 4, 15, 9, 30, 0, TimeSpan.Zero);
        var enLaLinea = PasoGris.Linea(instante, 73).First(p => Math.Abs(p.Latitud) < 60);

        PasoGris.EstaEnPasoGris(enLaLinea, instante).Should().BeTrue();

        // El punto que tiene el Sol encima no está en paso gris ni de lejos.
        PasoGris.EstaEnPasoGris(PasoGris.SubpuntoSolar(instante), instante).Should().BeFalse();
    }
}
