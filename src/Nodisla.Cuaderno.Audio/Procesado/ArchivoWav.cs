using NAudio.Wave;
using Nodisla.Cuaderno.Audio.Captura;

namespace Nodisla.Cuaderno.Audio.Procesado;

/// <summary>Guarda y lee audio de un canal en WAV PCM de 16 bits.</summary>
public static class ArchivoWav
{
    /// <summary>Escribe el audio en un WAV PCM 16 bits mono. Crea la carpeta si hace falta.</summary>
    public static void Guardar(string ruta, AudioEnMemoria audio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        ArgumentNullException.ThrowIfNull(audio);

        var carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));
        if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

        // Primero a un temporal y luego se cambia el nombre: un corte a medias no deja un WAV roto.
        var temporal = ruta + ".tmp";
        using (var escritor = new WaveFileWriter(temporal, new WaveFormat(audio.Frecuencia, 16, 1)))
        {
            var bytes = new byte[audio.Muestras.Length * 2];
            for (var i = 0; i < audio.Muestras.Length; i++)
            {
                var v = float.IsFinite(audio.Muestras[i]) ? Math.Clamp(audio.Muestras[i], -1f, 1f) : 0f;
                var s = (short)Math.Round(v * short.MaxValue);
                bytes[2 * i] = (byte)(s & 0xFF);
                bytes[(2 * i) + 1] = (byte)((s >> 8) & 0xFF);
            }

            escritor.Write(bytes, 0, bytes.Length);
        }

        File.Move(temporal, ruta, overwrite: true);
    }

    /// <summary>Lee un WAV y lo deja en un canal.</summary>
    public static AudioEnMemoria Leer(string ruta)
    {
        using var lector = new WaveFileReader(ruta);
        var formato = lector.WaveFormat;
        var flotante = formato.Encoding == WaveFormatEncoding.IeeeFloat
            || (formato is WaveFormatExtensible && formato.BitsPerSample == 32);
        var bytes = new byte[lector.Length];
        var leidos = 0;
        int n;
        while (leidos < bytes.Length && (n = lector.Read(bytes, leidos, bytes.Length - leidos)) > 0) leidos += n;

        var cuadros = ConversorDeMuestras.CuantasMuestras(leidos, formato.Channels, formato.BitsPerSample);
        var mono = new float[Math.Max(0, cuadros)];
        if (cuadros > 0)
        {
            ConversorDeMuestras.AMono(bytes.AsSpan(0, leidos), formato.Channels, formato.BitsPerSample, flotante, mono);
        }

        return new AudioEnMemoria(mono, formato.SampleRate);
    }
}
