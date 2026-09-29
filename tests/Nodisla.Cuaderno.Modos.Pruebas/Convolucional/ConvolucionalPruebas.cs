using FluentAssertions;
using Nodisla.Cuaderno.Modos.Convolucional;

namespace Nodisla.Cuaderno.Modos.Pruebas.Convolucional;

/// <summary>
/// Pruebas del codigo convolucional K=32 y del decodificador de Fano, a solas, sin nada de WSPR.
/// </summary>
/// <remarks>
/// JT9 usa exactamente el mismo codigo, asi que lo que se comprueba aqui vale para los dos.
/// </remarks>
public class ConvolucionalPruebas
{
    private const int Datos = 50;
    private const int Total = 81;

    private static readonly CodigoConvolucional Codigo = CodigoConvolucional.DeWsprYJt9;

    private static byte[] BitsAlAzar(Random azar)
    {
        var bits = new byte[Total];
        for (var i = 0; i < Datos; i++) bits[i] = (byte)azar.Next(2);
        return bits;
    }

    /// <summary>Valores blandos con ruido gaussiano: 128 ± amplitud, mas ruido de desviacion sigma.</summary>
    private static byte[] Blandos(byte[] codificados, double amplitud, double sigma, Random azar)
    {
        var blandos = new byte[codificados.Length];
        for (var i = 0; i < blandos.Length; i++)
        {
            var u1 = 1.0 - azar.NextDouble();
            var u2 = azar.NextDouble();
            var g = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            var v = 128 + ((codificados[i] == 1 ? 1 : -1) * amplitud) + (sigma * g);
            blandos[i] = (byte)Math.Clamp((int)Math.Round(v), 0, 255);
        }
        return blandos;
    }

    [Fact]
    public void ElCodificadorSacaElDobleDeBitsYEsLineal()
    {
        var azar = new Random(1);
        var a = BitsAlAzar(azar);
        var b = BitsAlAzar(azar);
        var suma = new byte[Total];
        for (var i = 0; i < Total; i++) suma[i] = (byte)(a[i] ^ b[i]);

        var ca = Codigo.Codificar(a);
        var cb = Codigo.Codificar(b);
        var cs = Codigo.Codificar(suma);
        ca.Should().HaveCount(2 * Total);
        for (var i = 0; i < cs.Length; i++)
            cs[i].Should().Be((byte)(ca[i] ^ cb[i]), "un codigo convolucional es lineal");
    }

    [Fact]
    public void ElPrimerBitSoloDependeDeSiMismo()
    {
        // Con el registro vacio, el primer bit de entrada solo toca la posicion baja del
        // registro, y los dos polinomios tienen el bit bajo puesto: el primer par de salida
        // es el propio bit repetido.
        var unos = new byte[Total];
        unos[0] = 1;
        var c = Codigo.Codificar(unos);
        c[0].Should().Be(1);
        c[1].Should().Be(1);
        Codigo.Codificar(new byte[Total]).Should().OnlyContain(b => b == 0);
    }

    [Fact]
    public void SinRuidoFanoVuelveEnLinea()
    {
        var azar = new Random(7);
        var fano = new DecodificadorDeFano(Codigo);
        for (var intento = 0; intento < 50; intento++)
        {
            var bits = BitsAlAzar(azar);
            var blandos = Blandos(Codigo.Codificar(bits), 127, 0, azar);
            var r = fano.Decodificar(blandos, Datos, Total);
            r.Decodificado.Should().BeTrue();
            r.Bits.Should().Equal(bits);
            r.Esfuerzo.Should().BeLessThan(2 * Total, "sin ruido no hay que retroceder ni una vez");
        }
    }

    [Theory]
    [InlineData(40, 30, 100)]
    [InlineData(30, 30, 95)]
    public void ConRuidoFanoRecuperaCasiTodo(double amplitud, double sigma, int porcentajeMinimo)
    {
        var azar = new Random(11);
        var fano = new DecodificadorDeFano(Codigo);
        var aciertos = 0;
        const int Intentos = 100;
        for (var intento = 0; intento < Intentos; intento++)
        {
            var bits = BitsAlAzar(azar);
            var blandos = Blandos(Codigo.Codificar(bits), amplitud, sigma, azar);
            var r = fano.Decodificar(blandos, Datos, Total);
            if (r.Decodificado && r.Bits.AsSpan(0, Datos).SequenceEqual(bits.AsSpan(0, Datos))) aciertos++;
            else if (r.Decodificado) Assert.Fail("Fano devolvio un camino equivocado con metrica " + r.Metrica);
        }
        (100.0 * aciertos / Intentos).Should().BeGreaterThanOrEqualTo(porcentajeMinimo);
    }

    [Fact]
    public void ConRuidoPuroFanoSeAgotaOSacaMetricaHundida()
    {
        var azar = new Random(13);
        var fano = new DecodificadorDeFano(Codigo) { EsfuerzoPorBit = 2000 };
        var caminos = 0;
        for (var intento = 0; intento < 100; intento++)
        {
            var blandos = new byte[2 * Total];
            for (var i = 0; i < blandos.Length; i++) blandos[i] = (byte)Math.Clamp(128 + (int)Math.Round(40 * Gauss(azar)), 0, 255);
            var r = fano.Decodificar(blandos, Datos, Total);
            if (r.Decodificado)
            {
                caminos++;
                r.Metrica.Should().BeNegative("un camino por ruido puro tiene que tener metrica hundida");
            }
        }
        caminos.Should().BeLessThan(10, "el ruido puro casi nunca deberia atravesar la cola");
    }

    [Fact]
    public void LaColaSoloAdmiteCeros()
    {
        var azar = new Random(17);
        var fano = new DecodificadorDeFano(Codigo);
        var bits = BitsAlAzar(azar);
        var r = fano.Decodificar(Blandos(Codigo.Codificar(bits), 100, 20, azar), Datos, Total);
        r.Decodificado.Should().BeTrue();
        r.Bits.AsSpan(Datos).ToArray().Should().OnlyContain(b => b == 0);
    }

    private static double Gauss(Random azar)
    {
        var u1 = 1.0 - azar.NextDouble();
        var u2 = azar.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
