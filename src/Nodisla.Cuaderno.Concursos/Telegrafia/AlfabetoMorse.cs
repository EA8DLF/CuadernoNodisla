using System.Text;

namespace Nodisla.Cuaderno.Concursos.Telegrafia;

/// <summary>
/// El alfabeto Morse internacional, con lo que anade el castellano.
/// </summary>
/// <remarks>
/// <para>
/// La parte internacional es la de la recomendacion de la UIT. Lo que se anade es lo que hace
/// falta para escribir en castellano sin que el manipulador se coma letras: la <b>enie</b>
/// tiene codigo propio (<c>--.--</c>) y no es una ene; la <b>e acentuada</b> tambien
/// (<c>..-..</c>); las demas vocales acentuadas <b>no</b> tienen codigo y se envian como la
/// vocal sin acento, que es lo que hace todo el mundo en el aire. La <b>ce cedilla</b> se
/// incluye porque aparece en indicativos y nombres de la peninsula iberica.
/// </para>
/// <para>
/// Los signos de apertura de interrogacion y admiracion no existen en Morse. Se descartan al
/// manipular en vez de convertirlos en otra cosa: mandar un signo que no es el que se escribio
/// confunde mas que no mandar nada.
/// </para>
/// <para>
/// Las senales de procedimiento se escriben entre angulos —<c>&lt;AR&gt;</c>, <c>&lt;SK&gt;</c>,
/// <c>&lt;BT&gt;</c>, <c>&lt;KN&gt;</c>, <c>&lt;AS&gt;</c>, <c>&lt;HH&gt;</c>— porque son dos o
/// mas letras pegadas sin separacion entre ellas, y eso no se puede escribir de otra forma.
/// </para>
/// </remarks>
public static class AlfabetoMorse
{
    private static readonly Dictionary<char, string> Codigos = new()
    {
        ['A'] = ".-", ['B'] = "-...", ['C'] = "-.-.", ['D'] = "-..", ['E'] = ".",
        ['F'] = "..-.", ['G'] = "--.", ['H'] = "....", ['I'] = "..", ['J'] = ".---",
        ['K'] = "-.-", ['L'] = ".-..", ['M'] = "--", ['N'] = "-.", ['O'] = "---",
        ['P'] = ".--.", ['Q'] = "--.-", ['R'] = ".-.", ['S'] = "...", ['T'] = "-",
        ['U'] = "..-", ['V'] = "...-", ['W'] = ".--", ['X'] = "-..-", ['Y'] = "-.--",
        ['Z'] = "--..",
        ['0'] = "-----", ['1'] = ".----", ['2'] = "..---", ['3'] = "...--", ['4'] = "....-",
        ['5'] = ".....", ['6'] = "-....", ['7'] = "--...", ['8'] = "---..", ['9'] = "----.",
        ['.'] = ".-.-.-", [','] = "--..--", ['?'] = "..--..", ['\''] = ".----.",
        ['!'] = "-.-.--", ['/'] = "-..-.", ['('] = "-.--.", [')'] = "-.--.-",
        ['&'] = ".-...", [':'] = "---...", [';'] = "-.-.-.", ['='] = "-...-",
        ['+'] = ".-.-.", ['-'] = "-....-", ['_'] = "..--.-", ['"'] = ".-..-.",
        ['$'] = "...-..-", ['@'] = ".--.-.",
        // Lo que anade el castellano.
        ['Ñ'] = "--.--", ['É'] = "..-..", ['Ç'] = "-.-..", ['Ü'] = "..--",
    };

    private static readonly Dictionary<char, char> SinTilde = new()
    {
        ['Á'] = 'A', ['À'] = 'A', ['Í'] = 'I', ['Ó'] = 'O', ['Ú'] = 'U',
    };

    private static readonly Dictionary<string, string> Procedimiento = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AR"] = ".-.-.",     // fin de mensaje
        ["SK"] = "...-.-",    // fin de contacto
        ["VA"] = "...-.-",    // el mismo, con el otro nombre
        ["BT"] = "-...-",     // separacion de parrafo
        ["KN"] = "-.--.",     // adelante solo tu
        ["AS"] = ".-...",     // espera
        ["SN"] = "...-.",     // entendido
        ["VE"] = "...-.",     // el mismo, con el otro nombre
        ["HH"] = "........",  // error
        ["CT"] = "-.-.-",     // atencion, empieza la transmision
        ["SOS"] = "...---...",
    };

    /// <summary>Codigo Morse de un caracter.</summary>
    /// <param name="letra">Caracter a traducir; no distingue mayusculas.</param>
    /// <param name="codigo">Puntos y rayas, si existe.</param>
    /// <returns><see langword="true"/> si ese caracter se puede manipular.</returns>
    public static bool TryCodigo(char letra, out string codigo)
    {
        var mayuscula = char.ToUpperInvariant(letra);
        if (SinTilde.TryGetValue(mayuscula, out var plana)) mayuscula = plana;
        return Codigos.TryGetValue(mayuscula, out codigo!);
    }

    /// <summary>Codigo Morse de una senal de procedimiento.</summary>
    /// <param name="nombre">Nombre de la senal, sin los angulos: <c>AR</c>, <c>SK</c>…</param>
    /// <param name="codigo">Puntos y rayas, si existe.</param>
    /// <returns><see langword="true"/> si esa senal existe.</returns>
    public static bool TryProcedimiento(string nombre, out string codigo)
    {
        if (Procedimiento.TryGetValue(nombre, out codigo!)) return true;

        // Una senal que no este en la lista pero sean letras validas se manda pegada, que es
        // exactamente lo que significa escribirla entre angulos.
        var construido = new StringBuilder();
        foreach (var letra in nombre)
        {
            if (!TryCodigo(letra, out var trozo)) { codigo = string.Empty; return false; }
            construido.Append(trozo);
        }
        codigo = construido.ToString();
        return codigo.Length > 0;
    }

    /// <summary>Todos los caracteres que se saben manipular, en orden alfabetico.</summary>
    public static IReadOnlyCollection<char> Caracteres { get; } =
        Codigos.Keys.OrderBy(c => c).ToArray();

    /// <summary>Todas las senales de procedimiento con nombre propio.</summary>
    public static IReadOnlyCollection<string> Procedimientos { get; } =
        Procedimiento.Keys.OrderBy(n => n, StringComparer.Ordinal).ToArray();

    /// <summary>Traduce un texto a puntos y rayas, para ensenarlo en pantalla.</summary>
    /// <remarks>
    /// Separa los caracteres con un espacio y las palabras con una barra, que es como se
    /// escribe el Morse cuando se lee con los ojos. No sirve para manipular: para eso esta
    /// <see cref="Manipulador"/>, que ademas reparte los tiempos.
    /// </remarks>
    /// <param name="texto">Texto a traducir.</param>
    /// <returns>Los puntos y rayas.</returns>
    public static string Escribir(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return string.Empty;
        var salida = new StringBuilder();
        foreach (var letra in texto)
        {
            if (letra == ' ')
            {
                if (salida.Length > 0) salida.Append("/ ");
                continue;
            }
            if (!TryCodigo(letra, out var codigo)) continue;
            salida.Append(codigo).Append(' ');
        }
        return salida.ToString().TrimEnd();
    }
}
