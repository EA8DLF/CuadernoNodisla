using System.Text;

namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>
/// El código Morse: de puntos y rayas a texto y de texto a puntos y rayas.
/// </summary>
/// <remarks>
/// <para>
/// Letras, números, la puntuación de la Recomendación UIT-R M.1677, los prosignos de uso en
/// radioafición y los caracteres europeos más corrientes (Ñ, CH, À, Ä, É, È, Ö, Ü, Ç...).
/// </para>
/// <para>
/// <b>Prosignos.</b> Algunos comparten código con un signo de puntuación: <c>-...-</c> es «=» y
/// también <c>&lt;BT&gt;</c>, <c>.-.-.</c> es «+» y <c>&lt;AR&gt;</c>, <c>-.--.</c> es «(» y
/// <c>&lt;KN&gt;</c>, <c>.-...</c> es «&amp;» y <c>&lt;AS&gt;</c>. En el aire de radioaficionado se
/// usan como prosignos, y así se escriben al decodificar. Al codificar valen las dos formas.
/// </para>
/// <para>
/// Un código que no está en la tabla no se inventa: <see cref="TryDecodificar"/> dice que no, y
/// el decodificador no escribe nada en su lugar.
/// </para>
/// </remarks>
public static class TablaMorse
{
    /// <summary>Cada código con su texto. El primero de cada código es el que sale al decodificar.</summary>
    private static readonly (string Codigo, string Texto)[] Entradas =
    [
        // Letras
        (".-", "A"), ("-...", "B"), ("-.-.", "C"), ("-..", "D"), (".", "E"), ("..-.", "F"),
        ("--.", "G"), ("....", "H"), ("..", "I"), (".---", "J"), ("-.-", "K"), (".-..", "L"),
        ("--", "M"), ("-.", "N"), ("---", "O"), (".--.", "P"), ("--.-", "Q"), (".-.", "R"),
        ("...", "S"), ("-", "T"), ("..-", "U"), ("...-", "V"), (".--", "W"), ("-..-", "X"),
        ("-.--", "Y"), ("--..", "Z"),

        // Números
        ("-----", "0"), (".----", "1"), ("..---", "2"), ("...--", "3"), ("....-", "4"),
        (".....", "5"), ("-....", "6"), ("--...", "7"), ("---..", "8"), ("----.", "9"),

        // Puntuación
        (".-.-.-", "."), ("--..--", ","), ("..--..", "?"), (".----.", "'"), ("-.-.--", "!"),
        ("-..-.", "/"), ("-.--.-", ")"), ("---...", ":"), ("-.-.-.", ";"), ("-....-", "-"),
        ("..--.-", "_"), (".-..-.", "\""), ("...-..-", "$"), (".--.-.", "@"),
        ("..-.-", "¿"), ("--...-", "¡"),

        // Prosignos (los que comparten código con puntuación salen como prosigno)
        (".-.-.", "<AR>"), ("...-.-", "<SK>"), ("-...-", "<BT>"), ("-.--.", "<KN>"), (".-...", "<AS>"),
        ("-.-.-", "<KA>"), ("...-.", "<SN>"), ("........", "<HH>"), ("...---...", "<SOS>"),

        // Caracteres europeos
        ("--.--", "Ñ"), ("----", "CH"), (".--.-", "À"), (".-.-", "Ä"), ("..-..", "É"), (".-..-", "È"),
        ("---.", "Ö"), ("..--", "Ü"), ("-.-..", "Ç"), ("..--.", "Ð"), (".--..", "Þ"), ("--..-.", "Ź"),
        ("--..-", "Ż"), ("...-...", "Ś"), ("--.-.", "Ĝ"), (".---.", "Ĵ"),
    ];

    /// <summary>Formas que solo valen al codificar: alias de un código que ya está arriba.</summary>
    private static readonly (string Texto, string Codigo)[] Alias =
    [
        ("=", "-...-"), ("+", ".-.-."), ("(", "-.--."), ("&", ".-..."), ("Å", ".--.-"), ("Æ", ".-.-"),
        ("Ø", "---."), ("Ł", ".-..-"), ("<BK>", "-...-.-"), ("<CT>", "-.-.-"), ("<VE>", "...-."),
        ("Á", ".--.-"), ("Ó", "---."), ("Ú", "..--"), ("Í", ".."), ("<CL>", "-.-..-.."),
    ];

    private static readonly Dictionary<string, string> DeCodigoATexto = Construir();

    private static readonly Dictionary<string, string> DeTextoACodigo = ConstruirInversa();

    /// <summary>Longitud del código más largo de la tabla, en elementos.</summary>
    public static int ElementosMaximos { get; } = Entradas.Max(e => e.Codigo.Length);

    /// <summary>Todos los códigos que se saben decodificar, con su texto.</summary>
    public static IReadOnlyDictionary<string, string> Codigos => DeCodigoATexto;

    /// <summary>Traduce un código de puntos y rayas («.-»).</summary>
    /// <param name="codigo">Puntos (<c>.</c>) y rayas (<c>-</c>).</param>
    /// <param name="texto">El carácter o el prosigno; vacío si no está en la tabla.</param>
    public static bool TryDecodificar(string codigo, out string texto)
    {
        if (DeCodigoATexto.TryGetValue(codigo, out var t))
        {
            texto = t;
            return true;
        }

        texto = string.Empty;
        return false;
    }

    /// <summary>El código de un carácter o prosigno («A», «&lt;AR&gt;», «Ñ»), o nulo si no lo tiene.</summary>
    public static string? Codificar(string simbolo)
    {
        ArgumentNullException.ThrowIfNull(simbolo);
        return DeTextoACodigo.TryGetValue(simbolo.ToUpperInvariant(), out var c) ? c : null;
    }

    /// <summary>
    /// Parte un texto en los símbolos que se manipulan: cada carácter, cada prosigno entre
    /// ángulos («&lt;AR&gt;») y cada espacio de palabra. Lo que no tiene código se omite.
    /// </summary>
    public static IReadOnlyList<string> Simbolos(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var lista = new List<string>();
        var mayus = texto.ToUpperInvariant();
        for (var i = 0; i < mayus.Length; i++)
        {
            var c = mayus[i];
            if (char.IsWhiteSpace(c))
            {
                if (lista.Count > 0 && lista[^1] != " ") lista.Add(" ");
                continue;
            }

            if (c == '<')
            {
                var cierre = mayus.IndexOf('>', i + 1);
                if (cierre > i)
                {
                    var prosigno = mayus.Substring(i, cierre - i + 1);
                    if (DeTextoACodigo.ContainsKey(prosigno))
                    {
                        lista.Add(prosigno);
                        i = cierre;
                        continue;
                    }
                }
            }

            // «CH» se manda como letra propia solo si se pide entre ángulos; suelto son C y H.
            var uno = c.ToString();
            if (DeTextoACodigo.ContainsKey(uno)) lista.Add(uno);
        }

        while (lista.Count > 0 && lista[^1] == " ") lista.RemoveAt(lista.Count - 1);
        return lista;
    }

    /// <summary>El texto entero en puntos y rayas, con un espacio entre caracteres y « / » entre palabras.</summary>
    public static string ATexto(string texto)
    {
        var sb = new StringBuilder();
        foreach (var s in Simbolos(texto))
        {
            if (s == " ")
            {
                sb.Append("/ ");
                continue;
            }

            sb.Append(Codificar(s)).Append(' ');
        }

        return sb.ToString().TrimEnd();
    }

    private static Dictionary<string, string> Construir()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (codigo, texto) in Entradas)
        {
            if (!d.TryAdd(codigo, texto))
                throw new InvalidOperationException($"El código {codigo} está dos veces en la tabla Morse.");
        }

        return d;
    }

    private static Dictionary<string, string> ConstruirInversa()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (codigo, texto) in Entradas) d.TryAdd(texto, codigo);
        foreach (var (texto, codigo) in Alias) d.TryAdd(texto, codigo);
        d["<CH>"] = "----";
        return d;
    }
}
