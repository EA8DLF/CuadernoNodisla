using FluentAssertions;
using Nodisla.Cuaderno.Audio.Captura;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// La aduana entre los bytes de Windows y las muestras del modem.
/// </summary>
public class ConversionYRemuestreoPruebas
{
    [Fact]
    public void LosDosCanalesSePromedianEnVezDeTirarUno()
    {
        // Dos cuadros de dos canales en coma flotante: (1, 0) y (0,5, -0,5).
        var crudo = new byte[4 * 4];
        BitConverter.GetBytes(1.0f).CopyTo(crudo, 0);
        BitConverter.GetBytes(0.0f).CopyTo(crudo, 4);
        BitConverter.GetBytes(0.5f).CopyTo(crudo, 8);
        BitConverter.GetBytes(-0.5f).CopyTo(crudo, 12);

        var mono = new float[2];
        var cuantas = ConversorDeMuestras.AMono(crudo, canales: 2, bitsPorMuestra: 32, esComaFlotante: true, mono);

        cuantas.Should().Be(2);
        mono[0].Should().BeApproximately(0.5f, 1e-6f);

        // Si se hubiera cogido solo el canal izquierdo, esto seria 0,5 y no cero: quedarse con
        // el canal que no es deja el modem sordo cuando el equipo solo trae uno.
        mono[1].Should().BeApproximately(0f, 1e-6f);
    }

    [Fact]
    public void LosEnterosDeDieciseisBitsVanYVuelven()
    {
        var original = new[] { 0f, 0.5f, -0.5f, 1f, -1f };
        var bytes = new byte[original.Length * 2];

        ConversorDeMuestras.DesdeMono(original, canales: 1, bitsPorMuestra: 16, esComaFlotante: false, bytes);

        var vuelta = new float[original.Length];
        ConversorDeMuestras.AMono(bytes, canales: 1, bitsPorMuestra: 16, esComaFlotante: false, vuelta);

        for (var i = 0; i < original.Length; i++)
        {
            vuelta[i].Should().BeApproximately(original[i], 1e-4f);
        }
    }

    [Fact]
    public void AlSalirSeRepiteLaMismaMuestraEnTodosLosCanales()
    {
        var bytes = new byte[2 * 2 * 4];
        ConversorDeMuestras.DesdeMono(
            new[] { 0.25f, -0.25f },
            canales: 2,
            bitsPorMuestra: 32,
            esComaFlotante: true,
            bytes);

        BitConverter.ToSingle(bytes, 0).Should().BeApproximately(0.25f, 1e-6f);
        BitConverter.ToSingle(bytes, 4).Should().BeApproximately(0.25f, 1e-6f);
        BitConverter.ToSingle(bytes, 8).Should().BeApproximately(-0.25f, 1e-6f);
        BitConverter.ToSingle(bytes, 12).Should().BeApproximately(-0.25f, 1e-6f);
    }

    [Fact]
    public void LoQueSePasaDeLaEscalaSeRecortaYNoDaLaVuelta()
    {
        var bytes = new byte[2 * 2];
        ConversorDeMuestras.DesdeMono(
            new[] { 2f, -2f },
            canales: 1,
            bitsPorMuestra: 16,
            esComaFlotante: false,
            bytes);

        BitConverter.ToInt16(bytes, 0).Should().Be(32767);
        BitConverter.ToInt16(bytes, 2).Should().Be(-32767);
    }

    [Fact]
    public void ConLaMismaFrecuenciaNoSeToca()
    {
        var remuestreador = new Remuestreador(48000, 48000);
        remuestreador.EsPasoDirecto.Should().BeTrue();

        var entrada = new[] { 1f, 2f, 3f, 4f };
        var salida = new float[4];

        remuestreador.Convertir(entrada, salida).Should().Be(4);
        salida.Should().Equal(entrada);
    }

    [Fact]
    public void AlBajarDeFrecuenciaSaleLaMitadDeMuestrasYLaRampaSigueSiendoRecta()
    {
        var remuestreador = new Remuestreador(96000, 48000);
        var entrada = new float[100];
        for (var i = 0; i < entrada.Length; i++)
        {
            entrada[i] = i;
        }

        var salida = new float[remuestreador.MuestrasMaximas(entrada.Length)];
        var cuantas = remuestreador.Convertir(entrada, salida);

        cuantas.Should().Be(50);
        for (var i = 0; i < cuantas; i++)
        {
            salida[i].Should().BeApproximately(i * 2f, 1e-3f);
        }
    }

    [Fact]
    public void LaContinuidadEntreBloquesSeMantiene()
    {
        var remuestreador = new Remuestreador(48000, 44100);
        var todo = new List<float>();

        // Una rampa partida en dos: si el remuestreador perdiera la fase entre bloques, en la
        // costura apareceria un escalon.
        for (var bloque = 0; bloque < 2; bloque++)
        {
            var entrada = new float[480];
            for (var i = 0; i < entrada.Length; i++)
            {
                entrada[i] = (bloque * 480) + i;
            }

            var salida = new float[remuestreador.MuestrasMaximas(entrada.Length)];
            var cuantas = remuestreador.Convertir(entrada, salida);
            todo.AddRange(salida.Take(cuantas));
        }

        var paso = 48000.0 / 44100.0;
        for (var i = 1; i < todo.Count; i++)
        {
            (todo[i] - todo[i - 1]).Should().BeApproximately((float)paso, 1e-2f);
        }
    }
}
