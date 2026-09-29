using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// Un nodo bien conectado repite el mismo anuncio desde varias rutas, y las redes de escucha
/// automática anuncian la misma estación decenas de veces por minuto. Estas pruebas fijan la
/// regla que hace legible la lista: el repetido no se tira, se junta con el anterior; y si la
/// estación se ha movido de frecuencia o de modo, ya no es el mismo anuncio.
/// </summary>
public sealed class JuntaDeRepetidosPruebas
{
    private static readonly DateTimeOffset Partida = new(2026, 9, 21, 19, 0, 0, TimeSpan.Zero);

    private static Spot Anuncio(
        string indicativo = "3Y0J",
        double mhz = 14.025,
        string anunciante = "EA1ABC",
        string? continente = "EU",
        string modo = "CW",
        bool automatico = false,
        int minutos = 0) =>
        new(
            Indicativo.Parse(indicativo),
            Frecuencia.DesdeMegahercios((decimal)mhz),
            Indicativo.Crudo(anunciante),
            null,
            Partida.AddMinutes(minutos),
            "Cluster de pruebas")
        {
            ModoAnunciado = Modo.Crudo(modo),
            Continente = continente,
            EsDeEscuchaAutomatica = automatico,
        };

    [Fact]
    public void El_primer_anuncio_no_esta_repetido()
    {
        var junta = new JuntaDeRepetidos();

        var anuncio = junta.Juntar(Anuncio());

        anuncio.Veces.Should().Be(1);
        anuncio.EsRepetido.Should().BeFalse();
    }

    [Fact]
    public void El_mismo_indicativo_en_la_misma_banda_se_junta()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio(anunciante: "EA1ABC"));
        var segundo = junta.Juntar(Anuncio(anunciante: "F5XYZ", minutos: 2));

        segundo.Veces.Should().Be(2);
        segundo.EsRepetido.Should().BeTrue();
        junta.Siguiendo.Should().Be(1);
    }

    [Fact]
    public void Se_ve_quien_lo_oye_y_desde_que_continentes()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio(anunciante: "EA1ABC", continente: "EU"));
        junta.Juntar(Anuncio(anunciante: "K1ABC", continente: "NA", minutos: 1));
        var tercero = junta.Juntar(Anuncio(anunciante: "JA1NUT", continente: "AS", minutos: 2));

        tercero.Veces.Should().Be(3);
        tercero.Continentes.Should().BeEquivalentTo(["EU", "NA", "AS"]);
    }

    [Fact]
    public void El_mismo_anunciante_repitiendo_no_cuenta_dos_veces()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio(anunciante: "EA1ABC"));
        var segundo = junta.Juntar(Anuncio(anunciante: "EA1ABC", minutos: 1));

        // Lo que interesa es cuántas estaciones distintas la están oyendo, no cuántos
        // mensajes ha mandado el nodo.
        segundo.Veces.Should().Be(1);
    }

    [Fact]
    public void Pasada_la_ventana_ya_no_es_repetido()
    {
        var junta = new JuntaDeRepetidos(TimeSpan.FromMinutes(10));

        junta.Juntar(Anuncio());
        var tarde = junta.Juntar(Anuncio(anunciante: "F5XYZ", minutos: 11));

        tarde.Veces.Should().Be(1);
        tarde.EsRepetido.Should().BeFalse();
    }

    [Fact]
    public void Una_frecuencia_distinta_es_un_anuncio_nuevo()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio(mhz: 14.025));
        var movida = junta.Juntar(Anuncio(mhz: 14.030, anunciante: "F5XYZ", minutos: 1));

        // La estación se ha movido cinco kilohercios: no es la misma frecuencia.
        movida.Veces.Should().Be(1);
    }

    [Fact]
    public void Unos_pocos_hercios_de_diferencia_siguen_siendo_la_misma_estacion()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio(mhz: 14.0250));
        var casi = junta.Juntar(Anuncio(mhz: 14.0253, anunciante: "F5XYZ", minutos: 1));

        // Dos receptores que oyen a la misma estación no leen el mismo dial al hercio.
        casi.Veces.Should().Be(2);
    }

    [Fact]
    public void Un_modo_distinto_es_un_anuncio_nuevo()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio(modo: "CW"));
        var otroModo = junta.Juntar(Anuncio(modo: "SSB", anunciante: "F5XYZ", minutos: 1));

        otroModo.Veces.Should().Be(1);
        junta.Siguiendo.Should().Be(2);
    }

    [Fact]
    public void Una_banda_distinta_es_un_anuncio_nuevo()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio(mhz: 14.025));
        var otraBanda = junta.Juntar(Anuncio(mhz: 7.025, anunciante: "F5XYZ", minutos: 1));

        otraBanda.Veces.Should().Be(1);
        junta.Siguiendo.Should().Be(2);
    }

    [Fact]
    public void Se_distingue_lo_que_solo_han_oido_maquinas_de_lo_que_ha_oido_alguien()
    {
        var junta = new JuntaDeRepetidos();

        var soloMaquinas = junta.Juntar(Anuncio(anunciante: "EA8URL-#", automatico: true));
        soloMaquinas.SoloEscuchaAutomatica.Should().BeTrue();
        soloMaquinas.AnunciadoPorPersona.Should().BeFalse();

        var conPersona = junta.Juntar(Anuncio(anunciante: "EA1ABC", minutos: 1));
        conPersona.SoloEscuchaAutomatica.Should().BeFalse();
        conPersona.AnunciadoPorPersona.Should().BeTrue();

        // Y se sabe si el último en anunciarla fue una máquina, que es lo que decide si la
        // fila sube en la lista o solo se actualiza.
        var luegoMaquina = junta.Juntar(Anuncio(anunciante: "OK1DX-#", automatico: true, minutos: 2));
        luegoMaquina.ElUltimoEsAutomatico.Should().BeTrue();
        luegoMaquina.AnunciadoPorPersona.Should().BeTrue();
    }

    [Fact]
    public void Dos_estaciones_distintas_no_se_juntan()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio(indicativo: "3Y0J"));
        var otra = junta.Juntar(Anuncio(indicativo: "VK0EK", minutos: 1));

        otra.Veces.Should().Be(1);
        junta.Siguiendo.Should().Be(2);
    }

    [Fact]
    public void Lo_que_se_sale_de_la_ventana_se_olvida()
    {
        var junta = new JuntaDeRepetidos(TimeSpan.FromMinutes(10));

        junta.Juntar(Anuncio(indicativo: "3Y0J"));
        junta.Juntar(Anuncio(indicativo: "VK0EK", minutos: 1));
        junta.Siguiendo.Should().Be(2);

        junta.Juntar(Anuncio(indicativo: "PJ2T", minutos: 30));

        // Los dos primeros caducaron al llegar el tercero.
        junta.Siguiendo.Should().Be(1);
    }

    [Fact]
    public void Vaciar_olvida_todo()
    {
        var junta = new JuntaDeRepetidos();
        junta.Juntar(Anuncio());

        junta.Vaciar();

        junta.Siguiendo.Should().Be(0);
        junta.Juntar(Anuncio(anunciante: "F5XYZ", minutos: 1)).Veces.Should().Be(1);
    }

    [Fact]
    public void La_ventana_por_omision_son_diez_minutos()
    {
        new JuntaDeRepetidos().Ventana.Should().Be(TimeSpan.FromMinutes(10));
        new JuntaDeRepetidos(TimeSpan.Zero).Ventana.Should().Be(JuntaDeRepetidos.VentanaPorOmision);
    }
}
