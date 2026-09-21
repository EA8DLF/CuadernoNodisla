using System.Text;

namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Decodificacion del texto de un fichero ADI.
/// </summary>
/// <remarks>
/// Los ficheros reales mezclan codificaciones dentro del mismo fichero: el programa que los
/// genero escribio casi todo en UTF-8 pero algun campo quedo en la pagina de codigos de
/// Windows. Se intenta UTF-8 estricto y, si la secuencia no es valida, se reinterpreta como
/// ANSI (Windows-1252), que nunca falla. Asi un acento mal codificado no tumba el registro.
/// </remarks>
internal static class TextoAdif
{
    private static readonly UTF8Encoding Utf8Estricto = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Marca de orden de bytes de UTF-8.</summary>
    internal static ReadOnlySpan<byte> MarcaUtf8 => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Tabla de los 32 codigos que Windows-1252 asigna de forma distinta a ISO-8859-1.
    /// El resto de valores altos coinciden con Unicode punto por punto.
    /// </summary>
    private static readonly char[] AltosDeWindows1252 =
    [
        '\u20AC', '\u0081', '\u201A', '\u0192', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u02C6', '\u2030', '\u0160', '\u2039', '\u0152', '\u008D', '\u017D', '\u008F',
        '\u0090', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\u02DC', '\u2122', '\u0161', '\u203A', '\u0153', '\u009D', '\u017E', '\u0178',
    ];

    /// <summary>Convierte bytes en texto probando UTF-8 y cayendo a ANSI si no es valido.</summary>
    public static string Decodificar(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return string.Empty;
        try
        {
            return Utf8Estricto.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return DesdeAnsi(bytes);
        }
    }

    /// <summary>Convierte bytes interpretandolos como Windows-1252.</summary>
    public static string DesdeAnsi(ReadOnlySpan<byte> bytes)
    {
        var destino = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            destino[i] = b < 0x80 ? (char)b
                : b < 0xA0 ? AltosDeWindows1252[b - 0x80]
                : (char)b;
        }
        return new string(destino);
    }

    /// <summary>Lee bytes ASCII como texto, sin tocar los valores altos.</summary>
    public static string Ascii(ReadOnlySpan<byte> bytes)
    {
        var destino = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++) destino[i] = (char)bytes[i];
        return new string(destino);
    }

    /// <summary>
    /// Numero de bytes que ocupan las primeras <paramref name="unidades"/> unidades UTF-16
    /// del texto codificado en UTF-8, o -1 si no hay suficientes.
    /// </summary>
    /// <remarks>
    /// Hace falta porque hay programas que declaran la longitud del campo en caracteres y no
    /// en bytes, como manda el estandar. Ver <see cref="AnalizadorAdi"/>.
    /// </remarks>
    public static int BytesDeUnidades(ReadOnlySpan<byte> datos, int unidades)
    {
        var i = 0;
        var u = 0;
        while (i < datos.Length && u < unidades)
        {
            var b = datos[i];
            var largo = b < 0x80 ? 1
                : (b & 0xE0) == 0xC0 ? 2
                : (b & 0xF0) == 0xE0 ? 3
                : (b & 0xF8) == 0xF0 ? 4
                : 1;
            if (i + largo > datos.Length) return -1;
            u += largo == 4 ? 2 : 1;
            i += largo;
        }
        return u == unidades ? i : -1;
    }

    /// <summary>Indica si el byte es espacio, tabulador, retorno o salto de linea.</summary>
    public static bool EsBlanco(byte b) => b is 0x20 or 0x09 or 0x0D or 0x0A;
}
