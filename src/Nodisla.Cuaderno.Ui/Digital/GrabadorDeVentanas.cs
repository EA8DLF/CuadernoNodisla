using System.Buffers.Binary;
using System.IO;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>
/// Guarda en un WAV el audio de cada ventana, como el «Save all» de WSJT-X.
/// </summary>
/// <remarks>
/// <para>
/// Se queda con los ultimos bloques que entrega la entrada de audio y, cuando el modem da por
/// cerrada una ventana, recorta lo que cae dentro de ella y lo escribe en PCM de 16 bits mono.
/// El nombre del fichero es el de WSJT-X —<c>AAMMDD_HHMMSS.wav</c>— para que las grabaciones
/// sirvan tambien para comparar con otros decodificadores.
/// </para>
/// <para>
/// Escribe en el hilo que lo llame y no abre nada hasta que hay una ventana que guardar.
/// </para>
/// </remarks>
public sealed class GrabadorDeVentanas
{
    private readonly object _cerrojo = new();
    private readonly LinkedList<BloqueDeAudio> _bloques = new();

    /// <summary>Cuanto audio se guarda en memoria, como maximo.</summary>
    public TimeSpan Memoria { get; set; } = TimeSpan.FromSeconds(40);

    /// <summary>Muestras guardadas ahora mismo en memoria.</summary>
    public long MuestrasEnMemoria
    {
        get
        {
            lock (_cerrojo) return _bloques.Sum(b => (long)b.Muestras.Length);
        }
    }

    /// <summary>Mete un bloque recien capturado y tira los que ya no hacen falta.</summary>
    public void Anadir(BloqueDeAudio bloque)
    {
        ArgumentNullException.ThrowIfNull(bloque);

        lock (_cerrojo)
        {
            _bloques.AddLast(bloque);
            var limite = bloque.InstanteUtc - Memoria;
            while (_bloques.First is { } primero && FinDe(primero.Value) < limite)
            {
                _bloques.RemoveFirst();
            }
        }
    }

    /// <summary>
    /// Recorta el audio de una ventana. Devuelve las muestras, o vacio si no hay nada de ella.
    /// </summary>
    public float[] Recortar(DateTimeOffset inicio, TimeSpan duracion, out int frecuenciaDeMuestreo)
    {
        frecuenciaDeMuestreo = 0;
        var fin = inicio + duracion;
        var salida = new List<float>();

        lock (_cerrojo)
        {
            foreach (var bloque in _bloques)
            {
                if (bloque.Muestras.Length == 0) continue;
                frecuenciaDeMuestreo = bloque.FrecuenciaDeMuestreo;

                var finDelBloque = FinDe(bloque);
                if (finDelBloque <= inicio || bloque.InstanteUtc >= fin) continue;

                var desde = Math.Max(0, (int)Math.Round((inicio - bloque.InstanteUtc).TotalSeconds * bloque.FrecuenciaDeMuestreo));
                var hasta = Math.Min(bloque.Muestras.Length, (int)Math.Round((fin - bloque.InstanteUtc).TotalSeconds * bloque.FrecuenciaDeMuestreo));
                if (hasta > desde) salida.AddRange(bloque.Muestras.Span[desde..hasta].ToArray());
            }
        }

        return salida.ToArray();
    }

    /// <summary>Guarda la ventana en un WAV dentro de la carpeta. Devuelve la ruta, o nulo si no habia audio.</summary>
    public string? Guardar(string carpeta, DateTimeOffset inicio, TimeSpan duracion)
    {
        var muestras = Recortar(inicio, duracion, out var frecuencia);
        if (muestras.Length == 0 || frecuencia <= 0) return null;

        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, NombreDe(inicio));
        File.WriteAllBytes(ruta, AWav(muestras, frecuencia));
        return ruta;
    }

    /// <summary>El nombre que usa WSJT-X: <c>AAMMDD_HHMMSS.wav</c>.</summary>
    public static string NombreDe(DateTimeOffset inicio) =>
        inicio.UtcDateTime.ToString("yyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".wav";

    /// <summary>Un WAV PCM de 16 bits mono con estas muestras.</summary>
    public static byte[] AWav(ReadOnlySpan<float> muestras, int frecuencia)
    {
        var datos = muestras.Length * 2;
        var bytes = new byte[44 + datos];
        var s = bytes.AsSpan();

        "RIFF"u8.CopyTo(s);
        BinaryPrimitives.WriteInt32LittleEndian(s[4..], 36 + datos);
        "WAVE"u8.CopyTo(s[8..]);
        "fmt "u8.CopyTo(s[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(s[20..], 1);           // PCM
        BinaryPrimitives.WriteInt16LittleEndian(s[22..], 1);           // mono
        BinaryPrimitives.WriteInt32LittleEndian(s[24..], frecuencia);
        BinaryPrimitives.WriteInt32LittleEndian(s[28..], frecuencia * 2);
        BinaryPrimitives.WriteInt16LittleEndian(s[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(s[34..], 16);
        "data"u8.CopyTo(s[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(s[40..], datos);

        var p = 44;
        foreach (var m in muestras)
        {
            var v = (short)Math.Clamp(Math.Round(m * 32767f), short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(s[p..], v);
            p += 2;
        }

        return bytes;
    }

    private static DateTimeOffset FinDe(BloqueDeAudio b) =>
        b.InstanteUtc + TimeSpan.FromSeconds((double)b.Muestras.Length / Math.Max(1, b.FrecuenciaDeMuestreo));
}
