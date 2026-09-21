using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// El trayecto que se pinta en el mapa tiene que ser el que sigue la señal, no una recta entre
/// dos puntos. Estas pruebas fijan lo que distingue una cosa de la otra: que el camino corto
/// mida lo que mide la distancia de círculo máximo, que el largo sea el resto de la vuelta al
/// mundo, y que ninguno de los dos se convierta en una raya que cruce el mapa al pasar por la
/// línea de cambio de fecha.
/// </summary>
public sealed class TrazadoDeCirculoMaximoPruebas
{
    /// <summary>Santa Cruz de Tenerife, en IL18. Es la estación desde la que se opera.</summary>
    private static readonly Coordenada Canarias = new(28.47, -16.25);

    /// <summary>Tokio: el caso donde el camino corto y el largo se parecen poco.</summary>
    private static readonly Coordenada Tokio = new(35.68, 139.77);

    /// <summary>Buenos Aires, casi en las antípodas por el otro hemisferio.</summary>
    private static readonly Coordenada BuenosAires = new(-34.60, -58.38);

    [Fact]
    public void El_trayecto_empieza_en_el_origen_y_acaba_en_el_destino()
    {
        var trazo = TrazadoDeCirculoMaximo.Trazar(Canarias, Tokio);

        trazo[0].Should().Be(Canarias);
        trazo[^1].Should().Be(Tokio);
    }

    [Fact]
    public void El_camino_corto_mide_lo_mismo_que_la_distancia_de_circulo_maximo()
    {
        var trazo = TrazadoDeCirculoMaximo.Trazar(Canarias, Tokio, CaminoDelTrayecto.Corto, 512);

        var recorrido = LargoDelTrazo(trazo);
        var directa = Geodesia.DistanciaKm(Canarias, Tokio);

        // Con 512 puntos la poligonal se pega a la curva: menos de una milésima de diferencia.
        recorrido.Should().BeApproximately(directa, directa * 0.001);
    }

    [Fact]
    public void El_camino_largo_es_el_resto_de_la_vuelta_al_mundo()
    {
        var corto = Geodesia.DistanciaKm(Canarias, Tokio);
        var vuelta = 2 * Math.PI * Geodesia.RadioTerrestreKm;

        var largo = LargoDelTrazo(
            TrazadoDeCirculoMaximo.Trazar(Canarias, Tokio, CaminoDelTrayecto.Largo, 512));

        largo.Should().BeApproximately(vuelta - corto, (vuelta - corto) * 0.001);
    }

    [Fact]
    public void El_camino_largo_sale_de_casa_en_el_rumbo_contrario_al_corto()
    {
        var corto = TrazadoDeCirculoMaximo.Trazar(Canarias, Tokio, CaminoDelTrayecto.Corto, 512);
        var largo = TrazadoDeCirculoMaximo.Trazar(Canarias, Tokio, CaminoDelTrayecto.Largo, 512);

        var rumboCorto = Geodesia.RumboGrados(Canarias, corto[1]);
        var rumboLargo = Geodesia.RumboGrados(Canarias, largo[1]);

        var separacion = Math.Abs(rumboCorto - rumboLargo);
        if (separacion > 180) separacion = 360 - separacion;

        // Es justo lo que el operador hace con el rotor: girar la antena media vuelta.
        separacion.Should().BeApproximately(180, 1.0);
    }

    [Fact]
    public void El_camino_a_Japon_pasa_por_el_norte_y_no_por_el_ecuador()
    {
        var trazo = TrazadoDeCirculoMaximo.Trazar(Canarias, Tokio, CaminoDelTrayecto.Corto, 128);

        var latitudMaxima = trazo.Max(p => p.Latitud);

        // Interpolando latitudes y longitudes en línea recta el máximo saldría en 35°, el de
        // Tokio. El círculo máximo sube mucho más: por eso las antenas apuntan al nordeste.
        latitudMaxima.Should().BeGreaterThan(60);
    }

    [Fact]
    public void Un_trayecto_que_cruza_la_linea_de_cambio_de_fecha_se_parte_en_dos()
    {
        var trazo = TrazadoDeCirculoMaximo.Trazar(Tokio, new Coordenada(21.31, -157.86), CaminoDelTrayecto.Corto, 128);

        var tramos = TrazadoDeCirculoMaximo.PartirPorElAntimeridiano(trazo);

        tramos.Should().HaveCount(2);
        foreach (var tramo in tramos)
        {
            for (var i = 1; i < tramo.Count; i++)
            {
                Math.Abs(tramo[i].Longitud - tramo[i - 1].Longitud).Should().BeLessThan(180);
            }
        }
    }

    [Fact]
    public void Un_trayecto_que_no_cruza_el_antimeridiano_se_queda_entero()
    {
        var trazo = TrazadoDeCirculoMaximo.Trazar(Canarias, BuenosAires, CaminoDelTrayecto.Corto, 64);

        var tramos = TrazadoDeCirculoMaximo.PartirPorElAntimeridiano(trazo);

        tramos.Should().HaveCount(1);
        tramos[0].Should().HaveCount(trazo.Count);
    }

    [Fact]
    public void Hacia_las_antipodas_el_trayecto_no_se_rompe()
    {
        var antipoda = Geodesia.Antipoda(Canarias);

        var trazo = TrazadoDeCirculoMaximo.Trazar(Canarias, antipoda, CaminoDelTrayecto.Corto, 64);

        // El plano del círculo no está determinado, pero el trazo tiene que salir válido:
        // ni coordenadas imposibles ni saltos.
        trazo.Should().HaveCount(64);
        trazo.Should().OnlyContain(p => !double.IsNaN(p.Latitud) && !double.IsNaN(p.Longitud));
        trazo.Should().OnlyContain(p => p.Latitud >= -90 && p.Latitud <= 90);
    }

    [Fact]
    public void Con_origen_y_destino_iguales_el_camino_largo_da_la_vuelta_al_mundo()
    {
        var vuelta = 2 * Math.PI * Geodesia.RadioTerrestreKm;

        var largo = LargoDelTrazo(
            TrazadoDeCirculoMaximo.Trazar(Canarias, Canarias, CaminoDelTrayecto.Largo, 512));

        largo.Should().BeApproximately(vuelta, vuelta * 0.001);
    }

    [Fact]
    public void Nunca_se_traza_con_menos_puntos_de_los_que_hacen_falta_para_ver_una_curva()
    {
        var trazo = TrazadoDeCirculoMaximo.Trazar(Canarias, Tokio, CaminoDelTrayecto.Corto, 2);

        trazo.Should().HaveCount(TrazadoDeCirculoMaximo.PuntosMinimos);
    }

    /// <summary>Suma de las distancias entre puntos seguidos del trazo.</summary>
    private static double LargoDelTrazo(IReadOnlyList<Coordenada> trazo)
    {
        double total = 0;
        for (var i = 1; i < trazo.Count; i++) total += Geodesia.DistanciaKm(trazo[i - 1], trazo[i]);
        return total;
    }
}
