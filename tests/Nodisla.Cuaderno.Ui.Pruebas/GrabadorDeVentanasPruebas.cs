using System.Buffers.Binary;
using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Digital;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>El WAV de cada ventana: recorte y formato.</summary>
public sealed class GrabadorDeVentanasPruebas : IDisposable
{
    private static readonly DateTimeOffset Inicio = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "CuadernoNodislaPruebas", Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        try { if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true); }
        catch (IOException) { /* una carpeta temporal que no se borra no hace fallar la prueba */ }
    }

    [Fact]
    public void RecortaLoQueCaeDentroDeLaVentana()
    {
        var g = new GrabadorDeVentanas();
        const int fs = 1000;

        // Tres bloques de un segundo: el segundo y el tercero caen en la ventana [1 s, 3 s).
        for (var i = 0; i < 4; i++)
        {
            var muestras = new float[fs];
            Array.Fill(muestras, i * 0.1f);
            g.Anadir(new BloqueDeAudio(muestras, fs, Inicio + TimeSpan.FromSeconds(i)));
        }

        var recorte = g.Recortar(Inicio + TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), out var frecuencia);

        frecuencia.Should().Be(fs);
        recorte.Should().HaveCount(2 * fs);
        recorte[0].Should().BeApproximately(0.1f, 0.0001f);
        recorte[^1].Should().BeApproximately(0.2f, 0.0001f);
    }

    [Fact]
    public void LoViejoSeTira()
    {
        var g = new GrabadorDeVentanas { Memoria = TimeSpan.FromSeconds(5) };
        for (var i = 0; i < 10; i++)
        {
            g.Anadir(new BloqueDeAudio(new float[100], 100, Inicio + TimeSpan.FromSeconds(i)));
        }

        g.MuestrasEnMemoria.Should().BeLessThanOrEqualTo(700);
    }

    [Fact]
    public void ElWavEsPcmDe16BitsMonoConElNombreDeWsjtx()
    {
        var g = new GrabadorDeVentanas();
        var muestras = new float[48000];
        muestras[0] = 1f;
        muestras[1] = -1f;
        g.Anadir(new BloqueDeAudio(muestras, 48000, Inicio));

        var ruta = g.Guardar(_carpeta, Inicio, TimeSpan.FromSeconds(1));

        ruta.Should().NotBeNull();
        Path.GetFileName(ruta).Should().Be("260926_120000.wav");

        var bytes = File.ReadAllBytes(ruta!);
        bytes.Length.Should().Be(44 + (48000 * 2));
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(bytes, 8, 4).Should().Be("WAVE");
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(22)).Should().Be(1, "mono");
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(24)).Should().Be(48000);
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(34)).Should().Be(16);
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(44)).Should().Be(32767);
        BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(46)).Should().Be(-32767);
    }

    [Fact]
    public void SinAudioNoSeEscribeNada()
    {
        var g = new GrabadorDeVentanas();
        g.Guardar(_carpeta, Inicio, TimeSpan.FromSeconds(15)).Should().BeNull();
        Directory.Exists(_carpeta).Should().BeFalse();
    }
}
