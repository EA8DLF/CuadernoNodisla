using System.Buffers.Binary;

namespace Nodisla.Cuaderno.Modos.Senal;

/// <summary>Audio leido de un fichero.</summary>
/// <param name="Muestras">Muestras en un solo canal, de -1 a 1.</param>
/// <param name="FrecuenciaDeMuestreo">Muestras por segundo.</param>
/// <param name="CanalesOriginales">Canales que traia el fichero antes de mezclarlos.</param>
public sealed record AudioDeFichero(float[] Muestras, int FrecuenciaDeMuestreo, int CanalesOriginales)
{
    /// <summary>Duracion del audio, en segundos.</summary>
    public double Segundos => FrecuenciaDeMuestreo == 0 ? 0 : (double)Muestras.Length / FrecuenciaDeMuestreo;
}

/// <summary>
/// Lee ficheros WAV para poder medir el decodificador sin radio.
/// </summary>
/// <remarks>
/// <para>
/// Es la puerta de entrada de las grabaciones de verdad. Las que circulan entre radioaficionados
/// para probar decodificadores suelen venir tal y como las graba el programa de referencia —doce
/// mil muestras por segundo, un canal, enteros de dieciseis bits— pero tambien aparecen
/// grabaciones caseras a 44100 o 48000, en estereo y en coma flotante. Se aceptan todas, porque
/// rechazar una grabacion por el formato del fichero seria perder una medida por una tonteria.
/// </para>
/// <para>
/// El estereo se mezcla a un canal sumando y dividiendo. En una grabacion de radio los dos
/// canales llevan lo mismo, asi que no se pierde nada; y si llevaran cosas distintas, mezclar es
/// lo que hace tambien el modem con la entrada de la tarjeta.
/// </para>
/// <para>
/// El lector recorre los trozos del fichero por su cabecera en vez de dar por hecho que el audio
/// empieza en el byte 44. Muchos programas meten trozos de metadatos antes; leerlos como si
/// fueran audio produce un chasquido al principio que se lleva por delante las decodificaciones
/// de esa ventana.
/// </para>
/// </remarks>
public static class LectorWav
{
    private const int FormatoEntero = 1;
    private const int FormatoComaFlotante = 3;
    private const int FormatoExtensible = 0xFFFE;

    /// <summary>Lee un fichero WAV entero.</summary>
    /// <param name="ruta">Camino del fichero.</param>
    /// <exception cref="InvalidDataException">Si el fichero no es un WAV que se pueda leer.</exception>
    public static AudioDeFichero Leer(string ruta) => Leer(File.ReadAllBytes(ruta));

    /// <summary>Lee un WAV que ya esta en memoria.</summary>
    /// <param name="bytes">Contenido del fichero.</param>
    /// <exception cref="InvalidDataException">Si el contenido no es un WAV que se pueda leer.</exception>
    public static AudioDeFichero Leer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12 || !EsTexto(bytes[..4], "RIFF") || !EsTexto(bytes.Slice(8, 4), "WAVE"))
            throw new InvalidDataException("El fichero no es un WAV: no empieza por RIFF/WAVE.");

        int formato = 0, canales = 0, frecuencia = 0, bitsPorMuestra = 0;
        ReadOnlySpan<byte> datos = default;
        var encontroFormato = false;

        var posicion = 12;
        while (posicion + 8 <= bytes.Length)
        {
            var nombre = bytes.Slice(posicion, 4);
            var tamano = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(posicion + 4, 4));
            var contenido = posicion + 8;
            // Un tamano mentiroso no puede hacer que se lea fuera del fichero.
            if (tamano < 0 || contenido + tamano > bytes.Length) tamano = bytes.Length - contenido;

            if (EsTexto(nombre, "fmt "))
            {
                if (tamano < 16) throw new InvalidDataException("El trozo de formato del WAV está incompleto.");
                var f = bytes.Slice(contenido, tamano);
                formato = BinaryPrimitives.ReadUInt16LittleEndian(f[..2]);
                canales = BinaryPrimitives.ReadUInt16LittleEndian(f.Slice(2, 2));
                frecuencia = (int)BinaryPrimitives.ReadUInt32LittleEndian(f.Slice(4, 4));
                bitsPorMuestra = BinaryPrimitives.ReadUInt16LittleEndian(f.Slice(14, 2));
                // En el formato extensible, el que manda es el subformato del final.
                if (formato == FormatoExtensible && tamano >= 40)
                    formato = BinaryPrimitives.ReadUInt16LittleEndian(f.Slice(24, 2));
                encontroFormato = true;
            }
            else if (EsTexto(nombre, "data"))
            {
                datos = bytes.Slice(contenido, tamano);
            }

            // Los trozos van alineados a par: si el tamano es impar, hay un byte de relleno.
            posicion = contenido + tamano + (tamano & 1);
        }

        if (!encontroFormato) throw new InvalidDataException("El WAV no trae trozo de formato.");
        if (datos.IsEmpty) throw new InvalidDataException("El WAV no trae audio.");
        if (canales is < 1 or > 8) throw new InvalidDataException($"El WAV dice tener {canales} canales.");
        if (frecuencia <= 0) throw new InvalidDataException("El WAV no dice su frecuencia de muestreo.");

        var entrelazadas = Convertir(datos, formato, bitsPorMuestra);
        return new AudioDeFichero(AUnCanal(entrelazadas, canales), frecuencia, canales);
    }

    private static float[] Convertir(ReadOnlySpan<byte> datos, int formato, int bits)
    {
        switch (formato, bits)
        {
            case (FormatoEntero, 8):
            {
                // Los WAV de ocho bits son sin signo y con el cero en 128.
                var salida = new float[datos.Length];
                for (var i = 0; i < salida.Length; i++) salida[i] = (datos[i] - 128) / 128f;
                return salida;
            }
            case (FormatoEntero, 16):
            {
                var n = datos.Length / 2;
                var salida = new float[n];
                for (var i = 0; i < n; i++)
                    salida[i] = BinaryPrimitives.ReadInt16LittleEndian(datos.Slice(i * 2, 2)) / 32768f;
                return salida;
            }
            case (FormatoEntero, 24):
            {
                var n = datos.Length / 3;
                var salida = new float[n];
                for (var i = 0; i < n; i++)
                {
                    var b = datos.Slice(i * 3, 3);
                    // Se recompone a 32 bits con signo desplazando y luego se escala.
                    var v = (b[0] << 8) | (b[1] << 16) | (b[2] << 24);
                    salida[i] = v / 2147483648f;
                }
                return salida;
            }
            case (FormatoEntero, 32):
            {
                var n = datos.Length / 4;
                var salida = new float[n];
                for (var i = 0; i < n; i++)
                    salida[i] = BinaryPrimitives.ReadInt32LittleEndian(datos.Slice(i * 4, 4)) / 2147483648f;
                return salida;
            }
            case (FormatoComaFlotante, 32):
            {
                var n = datos.Length / 4;
                var salida = new float[n];
                for (var i = 0; i < n; i++)
                    salida[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(datos.Slice(i * 4, 4)));
                return salida;
            }
            case (FormatoComaFlotante, 64):
            {
                var n = datos.Length / 8;
                var salida = new float[n];
                for (var i = 0; i < n; i++)
                    salida[i] = (float)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(datos.Slice(i * 8, 8)));
                return salida;
            }
            default:
                throw new InvalidDataException($"El WAV viene en un formato que no se lee: código {formato}, {bits} bits por muestra.");
        }
    }

    private static float[] AUnCanal(float[] entrelazadas, int canales)
    {
        if (canales == 1) return entrelazadas;
        var marcos = entrelazadas.Length / canales;
        var salida = new float[marcos];
        for (var m = 0; m < marcos; m++)
        {
            float suma = 0;
            for (var c = 0; c < canales; c++) suma += entrelazadas[(m * canales) + c];
            salida[m] = suma / canales;
        }
        return salida;
    }

    private static bool EsTexto(ReadOnlySpan<byte> bytes, string texto)
    {
        if (bytes.Length != texto.Length) return false;
        for (var i = 0; i < texto.Length; i++)
            if (bytes[i] != texto[i]) return false;
        return true;
    }

    /// <summary>
    /// Escribe un WAV de un canal en enteros de dieciseis bits.
    /// </summary>
    /// <param name="ruta">Camino del fichero a crear.</param>
    /// <param name="muestras">Muestras de -1 a 1.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    /// <remarks>
    /// Existe para poder guardar lo que el modem <i>habria</i> emitido y mirarlo con un programa
    /// de audio, o volver a metérselo al decodificador. Es la unica forma de probar la
    /// transmision sin poner el equipo en antena.
    /// </remarks>
    public static void Escribir(string ruta, ReadOnlySpan<float> muestras, int frecuenciaDeMuestreo)
    {
        var bytesDeAudio = muestras.Length * 2;
        using var fichero = File.Create(ruta);
        using var escritor = new BinaryWriter(fichero);

        escritor.Write("RIFF"u8);
        escritor.Write(36 + bytesDeAudio);
        escritor.Write("WAVE"u8);
        escritor.Write("fmt "u8);
        escritor.Write(16);
        escritor.Write((short)FormatoEntero);
        escritor.Write((short)1);
        escritor.Write(frecuenciaDeMuestreo);
        escritor.Write(frecuenciaDeMuestreo * 2);
        escritor.Write((short)2);
        escritor.Write((short)16);
        escritor.Write("data"u8);
        escritor.Write(bytesDeAudio);

        foreach (var m in muestras)
            escritor.Write((short)Math.Clamp(Math.Round(m * 32767f), short.MinValue, short.MaxValue));
    }
}
