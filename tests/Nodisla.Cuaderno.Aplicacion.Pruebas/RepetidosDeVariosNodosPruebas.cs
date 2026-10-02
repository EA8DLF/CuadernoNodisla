using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>
/// Con varios nodos conectados, el mismo anuncio llega varias veces. Estas pruebas fijan como
/// se funde: una sola fila por estación, con los nodos por los que llegó y los anunciantes
/// distintos sumados.
/// </summary>
public sealed class RepetidosDeVariosNodosPruebas
{
    private static readonly DateTimeOffset Partida = new(2026, 10, 2, 18, 0, 0, TimeSpan.Zero);

    private static Spot Anuncio(
        string nodo,
        string anunciante = "EA1ABC",
        double mhz = 14.025,
        double segundos = 0,
        string indicativo = "3Y0J") =>
        new(
            Indicativo.Parse(indicativo),
            Frecuencia.DesdeMegahercios((decimal)mhz),
            Indicativo.Crudo(anunciante),
            null,
            Partida.AddSeconds(segundos),
            nodo)
        {
            ModoAnunciado = Modo.Crudo("CW"),
            Continente = "EU",
        };

    [Fact]
    public void El_mismo_anuncio_por_dos_nodos_es_una_fila_con_dos_origenes()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio("EA4RCH"));
        var fundido = junta.Juntar(Anuncio("DXFun", segundos: 2));

        junta.Siguiendo.Should().Be(1, "una sola fila");
        fundido.Nodos.Should().Equal("EA4RCH", "DXFun");
        fundido.NumeroDeNodos.Should().Be(2);
        fundido.Veces.Should().Be(1, "es el mismo anunciante: no son dos que lo oyen");
    }

    [Fact]
    public void Oyen_suma_los_anunciantes_distintos_de_todos_los_nodos()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio("EA4RCH", anunciante: "EA1ABC"));
        junta.Juntar(Anuncio("DXFun", anunciante: "F5XYZ", segundos: 20));
        junta.Juntar(Anuncio("VE7CC", anunciante: "EA1ABC", segundos: 40));
        var fundido = junta.Juntar(Anuncio("VE7CC", anunciante: "K1ABC", segundos: 60));

        fundido.Veces.Should().Be(3, "EA1ABC, F5XYZ y K1ABC: el repetido de EA1ABC no cuenta dos veces");
        fundido.Nodos.Should().Equal("EA4RCH", "DXFun", "VE7CC");
    }

    [Fact]
    public void Un_nodo_que_repite_no_se_apunta_dos_veces()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio("DXFun"));
        var fundido = junta.Juntar(Anuncio("dxfun", anunciante: "F5XYZ", segundos: 5));

        fundido.Nodos.Should().ContainSingle();
    }

    [Fact]
    public void Por_omision_un_kilohercio_de_diferencia_es_la_misma_estacion()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio("EA4RCH", mhz: 14.0250));
        var dentro = junta.Juntar(Anuncio("DXFun", anunciante: "F5XYZ", mhz: 14.0259, segundos: 5));
        var fuera = junta.Juntar(Anuncio("VE7CC", anunciante: "K1ABC", mhz: 14.0275, segundos: 10));

        junta.Tolerancia.Should().Be(1.0m);
        dentro.Veces.Should().Be(2);
        fuera.Veces.Should().Be(1, "dos kilohercios y medio ya no es la misma estación");
    }

    [Fact]
    public void La_tolerancia_y_la_ventana_se_pueden_cambiar()
    {
        var ancha = new JuntaDeRepetidos(TimeSpan.FromMinutes(2), 3.0m);

        ancha.Juntar(Anuncio("EA4RCH", mhz: 14.0250));
        ancha.Juntar(Anuncio("DXFun", anunciante: "F5XYZ", mhz: 14.0275, segundos: 30))
            .Veces.Should().Be(2, "con tres kilohercios de tolerancia sí es la misma");

        ancha.Juntar(Anuncio("VE7CC", anunciante: "K1ABC", mhz: 14.0275, segundos: 30 + 180))
            .Veces.Should().Be(1, "pasada la ventana de dos minutos empieza de cero");
    }

    [Fact]
    public void Otra_estacion_en_otro_nodo_es_otra_fila()
    {
        var junta = new JuntaDeRepetidos();

        junta.Juntar(Anuncio("EA4RCH"));
        junta.Juntar(Anuncio("DXFun", indicativo: "VP8LP", segundos: 3));

        junta.Siguiendo.Should().Be(2);
    }
}
