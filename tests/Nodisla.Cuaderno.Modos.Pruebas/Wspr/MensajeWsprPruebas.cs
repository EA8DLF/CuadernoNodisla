using FluentAssertions;
using Nodisla.Cuaderno.Modos.Tablas;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Pruebas.Wspr;

/// <summary>La gramatica y el empaquetado de los mensajes de WSPR.</summary>
public class MensajeWsprPruebas
{
    [Theory]
    [InlineData("EA8DLF IL18 37", 1)]
    [InlineData("K1ABC FN42 37", 1)]
    [InlineData("W1AW FN31 0", 1)]
    [InlineData("4X1AB KM72 60", 1)]
    [InlineData("PJ4/K1ABC 37", 2)]
    [InlineData("EA8DLF/P 20", 2)]
    [InlineData("K1ABC/7 10", 2)]
    [InlineData("VK3XYZ/23 33", 2)]
    [InlineData("<PJ4/K1ABC> FK52UD 37", 3)]
    [InlineData("<EA8DLF/P> IL18TX 23", 3)]
    public void IdaYVueltaPorLos50Bits(string texto, int tipo)
    {
        MensajeWspr.TryAnalizar(texto, out var mensaje, out var motivo).Should().BeTrue(motivo);
        mensaje.Tipo.Should().Be(tipo);
        mensaje.Texto.Should().Be(texto);

        var bits = mensaje.Empaquetar();
        bits.Should().HaveCount(50);

        // El tipo 3 solo se resuelve si el resumen se conoce.
        Func<int, string?> resolver = h => h == HashDeIndicativo.Calcular(mensaje.Indicativo) ? mensaje.Indicativo : null;
        MensajeWspr.TryDesempaquetar(bits, resolver, out var vuelta).Should().BeTrue();
        vuelta.Texto.Should().Be(texto);
        vuelta.Tipo.Should().Be(tipo);
        vuelta.PotenciaDbm.Should().Be(mensaje.PotenciaDbm);
    }

    [Fact]
    public void UnTipo3SinResumenConocidoSaleConPuntosSuspensivos()
    {
        MensajeWspr.TryAnalizar("<PJ4/K1ABC> FK52UD 37", out var mensaje, out _).Should().BeTrue();
        MensajeWspr.TryDesempaquetar(mensaje.Empaquetar(), null, out var vuelta).Should().BeTrue();
        vuelta.Texto.Should().Be("<...> FK52UD 37");
        vuelta.IndicativoConocido.Should().BeFalse();
        vuelta.Resumen.Should().Be(HashDeIndicativo.Calcular("PJ4/K1ABC"));
    }

    [Theory]
    [InlineData("EA8DLF IL18 38")]     // potencia que no acaba en 0, 3 ni 7
    [InlineData("EA8DLF IL18 63")]
    [InlineData("EA8DLF SS18 37")]     // fuera del mapa
    [InlineData("EA8DLF IL18")]
    [InlineData("3DA0AB KG44 37")]     // la cifra no cae en la tercera casilla
    [InlineData("EA8DLFX IL18 37")]    // siete caracteres
    [InlineData("VP2E/K1ABC 37")]      // prefijo de cuatro
    [InlineData("K1ABC/QRP 37")]       // sufijo de tres letras
    [InlineData("CQ EA8DLF IL18")]
    [InlineData("")]
    public void LoQueNoCabeSeRechaza(string texto)
    {
        MensajeWspr.TryAnalizar(texto, out _, out var motivo).Should().BeFalse();
        motivo.Should().NotBeEmpty();
    }

    [Fact]
    public void ElLocalizadorSeComprimeComoDiceElProtocolo()
    {
        // Los ejemplos de la especificacion: AA00 es la esquina del mapa y RR99 la contraria.
        MensajeWspr.TryComprimirLocalizador("AA00", out var aa00).Should().BeTrue();
        aa00.Should().Be(179u * 180);
        MensajeWspr.TryComprimirLocalizador("RR99", out var rr99).Should().BeTrue();
        rr99.Should().Be(179u);
        MensajeWspr.TryDescomprimirLocalizador(32400, out _).Should().BeFalse("fuera del mapa");
    }

    [Fact]
    public void ElIndicativoSeComprimeComoDiceElProtocolo()
    {
        // Con seis casillas de 37, 36, 10, 27, 27 y 27 valores, el mayor numero posible cabe en 28 bits.
        MensajeWspr.TryComprimirIndicativo("ZZ9ZZZ", out var tope).Should().BeTrue();
        tope.Should().BeLessThan(1u << 28);
        // Un indicativo corto con la cifra en segunda posicion se corre con un espacio delante.
        MensajeWspr.TryComprimirIndicativo("K1ABC", out var k1abc).Should().BeTrue();
        MensajeWspr.TryComprimirIndicativo(" K1ABC", out var conEspacio).Should().BeTrue();
        k1abc.Should().Be(conEspacio);
    }

    [Fact]
    public void BitsDeBasuraNoFormanMensajes()
    {
        // Cincuenta bits al azar forman un mensaje con gramatica valida una de cada tres veces
        // (medido: 34,5 %). Es lo que da de si el formato: el campo de indicativo casi siempre
        // descomprime a algo con forma de indicativo, y el de potencia admite el 59 % de sus
        // valores entre los tres tipos. Por eso la gramatica no es la unica puerta ni la
        // principal: la cola de ceros, el limite de esfuerzo y la coincidencia hacen el resto,
        // y el banco mide que entre todas no se cuela nada. Aqui se comprueba que lo que sale
        // es siempre valido y que la tasa no empeora sin que alguien se entere.
        var azar = new Random(5);
        var aceptados = 0;
        const int Intentos = 20000;
        for (var i = 0; i < Intentos; i++)
        {
            var bits = new byte[50];
            for (var b = 0; b < 50; b++) bits[b] = (byte)azar.Next(2);
            if (MensajeWspr.TryDesempaquetar(bits, null, out var m))
            {
                aceptados++;
                m.Texto.Should().NotBeEmpty();
                TablasWspr.EsPotenciaValida(m.PotenciaDbm).Should().BeTrue();
            }
        }
        (100.0 * aceptados / Intentos).Should().BeLessThan(40, "la gramática tiene que tirar al menos lo que tiraba cuando se midió");
    }

    [Fact]
    public void ElResumenDeLookup3EsElConocido()
    {
        // Vectores publicados con lookup3 por su autor: hashlittle("", 0) = 0xdeadbeef y
        // hashlittle("Four score and seven years ago", 0) = 0x17770551.
        HashDeIndicativo.Lookup3([], 0).Should().Be(0xdeadbeef);
        var texto = System.Text.Encoding.ASCII.GetBytes("Four score and seven years ago");
        HashDeIndicativo.Lookup3(texto, 0).Should().Be(0x17770551u);
        HashDeIndicativo.Lookup3(texto, 1).Should().Be(0xcd628161u);
        HashDeIndicativo.Calcular("PJ4/K1ABC").Should().BeInRange(0, 32767);
    }

    [Fact]
    public void ElEntrelazadoEsUnaPermutacion()
    {
        var posiciones = ParametrosWspr.PosicionDeCadaBit.ToArray();
        posiciones.Should().HaveCount(162);
        posiciones.Distinct().Should().HaveCount(162);
        posiciones.Max().Should().Be(161);
        // Los primeros valores de la inversion de bits: 0, 128, 64, 192 → 192 sobra, luego 32, 160, 96...
        posiciones.Take(6).Should().Equal(new byte[] { 0, 128, 64, 32, 160, 96 });
    }

    [Fact]
    public void LasTablasSonLasPublicadas()
    {
        TablasWspr.VectorDeSincronismo.Length.Should().Be(162);
        var unos = 0;
        foreach (var b in TablasWspr.VectorDeSincronismo) unos += b;
        unos.Should().Be(63);
        TablasWspr.PotenciasValidasDbm.Length.Should().Be(19);
        TablasWspr.EsPotenciaValida(37).Should().BeTrue();
        TablasWspr.EsPotenciaValida(38).Should().BeFalse();
    }
}
