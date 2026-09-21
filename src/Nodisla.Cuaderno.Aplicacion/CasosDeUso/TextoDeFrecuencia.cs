using System.Globalization;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>
/// Lectura y escritura de la frecuencia tal y como la teclea y la lee el operador.
/// </summary>
/// <remarks>
/// REGLA FIJA, NO LA CAMBIE NADIE POR «ESPANOLIZAR» EL PROGRAMA: la frecuencia se escribe y se
/// muestra SIEMPRE con punto decimal y sin separador de millares. Es la convencion del campo
/// <c>FREQ</c> de ADIF, la del dial de cualquier equipo y la de todos los cuadernos del mundo.
/// En espanol <c>14.200</c> sigue siendo catorce megahercios y pico, nunca catorce mil doscientos.
/// Por eso todo esto va con <see cref="CultureInfo.InvariantCulture"/> de forma explicita y no
/// con la cultura del hilo, que en el resto del programa es espanola.
/// Al teclear si se admite la coma, porque es la tecla que tiene el teclado numerico espanol,
/// y se interpreta como punto decimal.
/// </remarks>
public static class TextoDeFrecuencia
{
    /// <summary>Formato con el que se escribe: hasta seis decimales, sin ceros de relleno.</summary>
    private const string Formato = "0.######";

    /// <summary>Interpreta lo que ha escrito el operador. Admite coma o punto decimal.</summary>
    public static bool TryLeer(string? texto, out Frecuencia frecuencia)
    {
        frecuencia = Frecuencia.Cero;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        var limpio = Normalizar(texto);
        if (limpio.Length == 0) return false;

        return Frecuencia.TryParseAdif(limpio, out frecuencia);
    }

    /// <summary>Interpreta lo tecleado y devuelve cero si no se entiende.</summary>
    public static Frecuencia Leer(string? texto) => TryLeer(texto, out var f) ? f : Frecuencia.Cero;

    /// <summary>Escribe la frecuencia para el operador. Una frecuencia cero se escribe vacia.</summary>
    public static string Escribir(Frecuencia frecuencia) =>
        frecuencia.EsCero ? string.Empty : EscribirMegahercios(frecuencia.Megahercios);

    /// <summary>Escribe un valor en megahercios con la misma regla.</summary>
    public static string EscribirMegahercios(decimal megahercios) =>
        megahercios.ToString(Formato, CultureInfo.InvariantCulture);

    /// <summary>
    /// Quita los espacios y unifica la coma con el punto. Si vienen las dos, o mas de un
    /// separador, se rechaza: es mas seguro que adivinar lo que quiso decir el operador.
    /// </summary>
    private static string Normalizar(string texto)
    {
        Span<char> destino = stackalloc char[texto.Length];
        var n = 0;
        var separadores = 0;

        foreach (var c in texto)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (c is ',' or '.')
            {
                separadores++;
                destino[n++] = '.';
                continue;
            }
            destino[n++] = c;
        }

        return separadores > 1 ? string.Empty : new string(destino[..n]);
    }
}
