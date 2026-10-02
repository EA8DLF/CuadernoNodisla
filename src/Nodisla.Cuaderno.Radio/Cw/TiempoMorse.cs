namespace Nodisla.Cuaderno.Radio.Cw;

/// <summary>
/// Lo que tarda en salir un texto en telegrafia, con la temporizacion de PARIS.
/// </summary>
/// <remarks>
/// <para>
/// Ningun equipo dice por CAT cuando ha terminado de manipular (en el FT-710, <c>KY</c> es solo
/// «Set»), asi que el programa lo calcula: un punto dura <c>1200 / WPM</c> milisegundos, una
/// raya tres puntos, el hueco dentro de un caracter un punto, entre caracteres tres y entre
/// palabras siete. Es lo que hace que el siguiente trozo no pise al anterior.
/// </para>
/// <para>
/// Los prosignos entre angulos (<c>&lt;SK&gt;</c>) van seguidos, sin hueco entre sus letras.
/// </para>
/// </remarks>
public static class TiempoMorse
{
    private static readonly Dictionary<char, string> Codigos = new()
    {
        ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".", ['F'] = "..-.",
        ['G'] = "--.", ['H'] = "....", ['I'] = "..", ['J'] = ".---", ['K'] = "-.-", ['L'] = ".-..",
        ['M'] = "--", ['N'] = "-.", ['O'] = "---", ['P'] = ".--.", ['Q'] = "--.-", ['R'] = ".-.",
        ['S'] = "...", ['T'] = "-", ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-",
        ['Y'] = "-.--", ['Z'] = "--..",
        ['0'] = "-----", ['1'] = ".----", ['2'] = "..---", ['3'] = "...--", ['4'] = "....-",
        ['5'] = ".....", ['6'] = "-....", ['7'] = "--...", ['8'] = "---..", ['9'] = "----.",
        ['/'] = "-..-.", ['?'] = "..--..", ['.'] = ".-.-.-", [','] = "--..--", ['='] = "-...-",
        ['+'] = ".-.-.", ['-'] = "-....-", [':'] = "---...", ['\''] = ".----.", ['('] = "-.--.",
        [')'] = "-.--.-", ['"'] = ".-..-.", ['@'] = ".--.-.",
    };

    /// <summary>Lo que dura un punto a esa velocidad.</summary>
    /// <param name="wpm">Palabras por minuto (PARIS).</param>
    /// <returns>La duracion de un punto.</returns>
    public static TimeSpan Punto(int wpm) => TimeSpan.FromMilliseconds(1200.0 / Math.Max(1, wpm));

    /// <summary>Unidades (puntos) que ocupa un texto, sin el hueco final.</summary>
    /// <param name="texto">Texto con prosignos entre angulos.</param>
    /// <returns>Las unidades.</returns>
    public static int Unidades(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var total = 0;
        var primeraPalabra = true;
        foreach (var palabra in texto.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var unidades = UnidadesDePalabra(palabra);
            if (unidades == 0) continue;
            if (!primeraPalabra) total += 7;
            total += unidades;
            primeraPalabra = false;
        }

        return total;
    }

    /// <summary>Lo que tarda en salir un texto a esa velocidad.</summary>
    /// <param name="texto">Texto con prosignos entre angulos.</param>
    /// <param name="wpm">Palabras por minuto.</param>
    /// <returns>La duracion.</returns>
    public static TimeSpan Duracion(string texto, int wpm) => Punto(wpm) * Unidades(texto);

    private static int UnidadesDePalabra(string palabra)
    {
        var total = 0;
        var caracteres = 0;
        var i = 0;
        while (i < palabra.Length)
        {
            string codigo;
            if (palabra[i] == '<' && palabra.IndexOf('>', i) is var cierra and > 0)
            {
                // Prosigno: sus letras seguidas, como un solo caracter.
                codigo = string.Concat(palabra[(i + 1)..cierra].Select(c => Codigos.GetValueOrDefault(c, string.Empty)));
                i = cierra + 1;
            }
            else
            {
                codigo = Codigos.GetValueOrDefault(palabra[i], string.Empty);
                i++;
            }

            if (codigo.Length == 0) continue;
            if (caracteres > 0) total += 3;
            total += codigo.Sum(s => s == '.' ? 1 : 3) + (codigo.Length - 1);
            caracteres++;
        }

        return total;
    }
}
