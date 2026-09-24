using FluentAssertions;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>Pruebas de la transformada de Fourier propia.</summary>
public class FftPruebas
{
    [Fact]
    public void CoincideConLaTransformadaCalculadaALoBruto()
    {
        // La FFT es un atajo para calcular la misma suma que la definicion. Si no dieran lo
        // mismo, el atajo estaria mal, y todo lo que hay encima seria basura.
        const int N = 64;
        var azar = new Random(3);
        var real = new float[N];
        var imaginaria = new float[N];
        for (var i = 0; i < N; i++) real[i] = (float)(azar.NextDouble() - 0.5);
        var original = real.ToArray();

        Fft.Transformar(real, imaginaria);

        for (var k = 0; k < N; k++)
        {
            double sr = 0, si = 0;
            for (var n = 0; n < N; n++)
            {
                var angulo = -2 * Math.PI * k * n / N;
                sr += original[n] * Math.Cos(angulo);
                si += original[n] * Math.Sin(angulo);
            }
            real[k].Should().BeApproximately((float)sr, 1e-3f);
            imaginaria[k].Should().BeApproximately((float)si, 1e-3f);
        }
    }

    [Fact]
    public void LaInversaDeshaceLaDirecta()
    {
        const int N = 256;
        var azar = new Random(11);
        var real = new float[N];
        var imaginaria = new float[N];
        for (var i = 0; i < N; i++) real[i] = (float)(azar.NextDouble() - 0.5);
        var original = real.ToArray();

        Fft.Transformar(real, imaginaria);
        Fft.TransformarInversa(real, imaginaria);

        for (var i = 0; i < N; i++)
        {
            real[i].Should().BeApproximately(original[i], 1e-4f);
            imaginaria[i].Should().BeApproximately(0f, 1e-4f);
        }
    }

    [Fact]
    public void UnTonoPuroCaeEnSuCasilla()
    {
        const int N = 1024;
        const int Casilla = 37;
        var muestras = new float[N];
        for (var i = 0; i < N; i++) muestras[i] = MathF.Sin(2 * MathF.PI * Casilla * i / N);

        var magnitudes = new float[(N / 2) + 1];
        Fft.MagnitudesDeSenalReal(muestras, N, magnitudes);

        var mayor = 0;
        for (var i = 1; i < magnitudes.Length; i++) if (magnitudes[i] > magnitudes[mayor]) mayor = i;
        mayor.Should().Be(Casilla);
    }

    [Fact]
    public void SeNiegaSiLaLongitudNoEsPotenciaDeDos()
    {
        var real = new float[100];
        var imaginaria = new float[100];
        var accion = () => Fft.Transformar(real, imaginaria);
        accion.Should().Throw<ArgumentException>();
    }
}

/// <summary>Pruebas del cambio de frecuencia de muestreo.</summary>
public class RemuestreadorPruebas
{
    [Theory]
    [InlineData(48000, 12800)]
    [InlineData(48000, 64000.0 / 3)]
    [InlineData(12000, 12800)]
    [InlineData(44100, 12800)]
    public void UnTonoSobreviveAlCambioDeFrecuencia(double de, double a)
    {
        const double TonoHz = 1500;
        const double Segundos = 0.5;
        var entrada = new float[(int)(de * Segundos)];
        for (var i = 0; i < entrada.Length; i++) entrada[i] = (float)Math.Sin(2 * Math.PI * TonoHz * i / de);

        var salida = Remuestreador.Remuestrear(entrada, de, a);

        salida.Length.Should().BeCloseTo((int)(a * Segundos), 2);

        // Se compara en la zona de dentro, lejos de los bordes, donde el nucleo cabe entero.
        var desde = salida.Length / 4;
        var hasta = salida.Length * 3 / 4;
        for (var n = desde; n < hasta; n++)
        {
            var esperado = (float)Math.Sin(2 * Math.PI * TonoHz * n / a);
            salida[n].Should().BeApproximately(esperado, 0.02f,
                "el remuestreo no puede cambiar la señal, solo los instantes en que se mide");
        }
    }

    [Fact]
    public void AlBajarDeFrecuenciaSeQuitaLoQueNoCabe()
    {
        // Un tono de 5000 Hz no cabe en una frecuencia de 12800 muestras por segundo: si no se
        // filtrara antes, aparecería doblado como un tono falso en mitad de la banda de FT8.
        const double De = 48000, A = 12800, TonoHz = 9000;
        var entrada = new float[(int)De];
        for (var i = 0; i < entrada.Length; i++) entrada[i] = (float)Math.Sin(2 * Math.PI * TonoHz * i / De);

        var salida = Remuestreador.Remuestrear(entrada, De, A);

        var potencia = 0.0;
        for (var i = salida.Length / 4; i < salida.Length * 3 / 4; i++) potencia += (double)salida[i] * salida[i];
        potencia /= salida.Length / 2;
        potencia.Should().BeLessThan(1e-4, "lo que está por encima de la nueva media banda tiene que desaparecer");
    }
}

/// <summary>Pruebas del lector de ficheros WAV.</summary>
public class LectorWavPruebas
{
    [Fact]
    public void LoQueSeEscribeSeVuelveALeerIgual()
    {
        var muestras = new float[4800];
        for (var i = 0; i < muestras.Length; i++) muestras[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 48000));

        var ruta = Path.Combine(Path.GetTempPath(), $"nodisla-{Guid.NewGuid():N}.wav");
        try
        {
            LectorWav.Escribir(ruta, muestras, 48000);
            var leido = LectorWav.Leer(ruta);

            leido.FrecuenciaDeMuestreo.Should().Be(48000);
            leido.CanalesOriginales.Should().Be(1);
            leido.Muestras.Length.Should().Be(muestras.Length);
            for (var i = 0; i < muestras.Length; i++)
                leido.Muestras[i].Should().BeApproximately(muestras[i], 1e-4f, "solo se pierde la cuantificación a 16 bits");
            leido.Segundos.Should().BeApproximately(0.1, 1e-6);
        }
        finally
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }

    [Fact]
    public void UnFicheroQueNoEsWavSeRechazaConClaridad()
    {
        var accion = () => LectorWav.Leer("no es un fichero de audio"u8.ToArray());
        accion.Should().Throw<InvalidDataException>().WithMessage("*RIFF*");
    }

    [Fact]
    public void SeSaltaLosTrozosDeMetadatosQueVienenAntesDelAudio()
    {
        // Muchos programas meten un trozo LIST antes del audio. Leerlo como si fuera sonido
        // produce un chasquido que se lleva por delante las decodificaciones de esa ventana.
        var conMetadatos = new List<byte>();
        conMetadatos.AddRange("RIFF"u8.ToArray());
        conMetadatos.AddRange(BitConverter.GetBytes(0));
        conMetadatos.AddRange("WAVE"u8.ToArray());
        conMetadatos.AddRange("LIST"u8.ToArray());
        conMetadatos.AddRange(BitConverter.GetBytes(4));
        conMetadatos.AddRange("INFO"u8.ToArray());
        conMetadatos.AddRange("fmt "u8.ToArray());
        conMetadatos.AddRange(BitConverter.GetBytes(16));
        conMetadatos.AddRange(BitConverter.GetBytes((short)1));
        conMetadatos.AddRange(BitConverter.GetBytes((short)1));
        conMetadatos.AddRange(BitConverter.GetBytes(12000));
        conMetadatos.AddRange(BitConverter.GetBytes(24000));
        conMetadatos.AddRange(BitConverter.GetBytes((short)2));
        conMetadatos.AddRange(BitConverter.GetBytes((short)16));
        conMetadatos.AddRange("data"u8.ToArray());
        conMetadatos.AddRange(BitConverter.GetBytes(4));
        conMetadatos.AddRange(BitConverter.GetBytes((short)16384));
        conMetadatos.AddRange(BitConverter.GetBytes((short)-16384));

        var leido = LectorWav.Leer(conMetadatos.ToArray());

        leido.FrecuenciaDeMuestreo.Should().Be(12000);
        leido.Muestras.Should().HaveCount(2);
        leido.Muestras[0].Should().BeApproximately(0.5f, 1e-4f);
        leido.Muestras[1].Should().BeApproximately(-0.5f, 1e-4f);
    }
}
