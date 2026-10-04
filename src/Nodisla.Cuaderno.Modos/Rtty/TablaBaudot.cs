namespace Nodisla.Cuaderno.Modos.Rtty;

/// <summary>El juego de caracteres en que está el teletipo: letras o cifras (números y signos).</summary>
public enum JuegoBaudot
{
    /// <summary>A–Z.</summary>
    Letras,

    /// <summary>Dígitos y signos de puntuación.</summary>
    Cifras,
}

/// <summary>
/// La tabla ITA2 / CCITT n.º 2 (Baudot-Murray), de 5 bits, con sus dos juegos y los dos cambios
/// de juego.
/// </summary>
/// <remarks>
/// <para>
/// <b>Todo propio.</b> Es una tabla de un estándar público, igual que los vectores de sincronismo
/// de FT8: se documenta la procedencia, no se copia código de nadie (y desde luego no la de
/// fldigi, que es GPL).
/// </para>
/// <para>
/// <b>Procedencia, contrastada en dos fuentes independientes.</b> Hay dos variantes históricas de
/// ITA2 que difieren en cinco cifras (S, D, H, J y Z): la internacional CCITT n.º 2 y la «U.S.»,
/// que es la que se extendió en la posguerra con el material militar excedente y es la que usa
/// radioaficionado en la práctica (RTTY de HF). Esta tabla es la variante U.S.:
/// </para>
/// <list type="bullet">
/// <item>
/// George W. Henry Jr. (K9GWT), <i>ASCII, Baudot, and the Radio Amateur</i>, HAL Communications
/// Corp., 1980 — la Tabla 1 da letras, cifras U.S. y cifras CCITT n.º 2 en columnas separadas, y
/// explica explícitamente que «U.S. amateurs have generally adopted a version of the so-called
/// Military Standard code arrangement for punctuation».
/// </item>
/// <item>
/// dcode.fr, «Baudot Code» (tabla por omisión, rotulada ITA2), que coincide carácter a carácter
/// con la columna U.S. de K9GWT.
/// </item>
/// </list>
/// <para>
/// Como contraste se comprobó también la variante CCITT n.º 2 (S=', D=ENQ, H=£, J=BELL, Z=+) en
/// boxentriq.com y en el fichero <c>baudot.txt</c> de github.com/paulsmith/baudot, que coinciden
/// entre sí y con la columna CCITT de K9GWT: confirma que la discrepancia es una variante real y
/// documentada, no un error de transcripción.
/// </para>
/// <para>
/// <b>USOS (Unshift On Space).</b> Muchos terminales de verdad vuelven a LETRAS en cuanto ven un
/// espacio en CIFRAS, para que una ráfaga de ruido que se cuela como un cambio de juego no deje el
/// resto del mensaje ilegible. <see cref="Decodificar"/> lo hace así, y quien codifica
/// (<see cref="GeneradorRtty.Codigos"/>) lleva la cuenta igual, mandando un cambio a CIFRAS nuevo
/// si hace falta tras un espacio: así el código que se manda y el que se espera recibir coinciden
/// siempre, sin dar por hecho que el otro extremo tenga USOS y sin romperse si no lo tiene (un
/// cambio de juego a uno en el que ya se está no hace nada).
/// </para>
/// </remarks>
public static class TablaBaudot
{
    /// <summary>Código que cambia al juego de cifras.</summary>
    public const byte CambioACifras = 0b11011;

    /// <summary>Código que cambia al juego de letras.</summary>
    public const byte CambioALetras = 0b11111;

    /// <summary>Marca de «sin texto que emitir» (NUL, o un cambio de juego).</summary>
    public const char SinTexto = '\0';

    /// <summary>Las 32 combinaciones, en el orden de la tabla: (Letras, Cifras).</summary>
    private static readonly (char Letra, char Cifra)[] Tabla =
    [
        /* 00000 */ ('\0', '\0'),   // NUL / blanco
        /* 00001 */ ('E', '3'),
        /* 00010 */ ('\n', '\n'),  // LF
        /* 00011 */ ('A', '-'),
        /* 00100 */ (' ', ' '),    // SPACE
        /* 00101 */ ('S', '\a'),  // BELL en cifras
        /* 00110 */ ('I', '8'),
        /* 00111 */ ('U', '7'),
        /* 01000 */ ('\r', '\r'),  // CR
        /* 01001 */ ('D', '$'),
        /* 01010 */ ('R', '4'),
        /* 01011 */ ('J', '\''),
        /* 01100 */ ('N', ','),
        /* 01101 */ ('F', '!'),
        /* 01110 */ ('C', ':'),
        /* 01111 */ ('K', '('),
        /* 10000 */ ('T', '5'),
        /* 10001 */ ('Z', '"'),
        /* 10010 */ ('L', ')'),
        /* 10011 */ ('W', '2'),
        /* 10100 */ ('H', '#'),
        /* 10101 */ ('Y', '6'),
        /* 10110 */ ('P', '0'),
        /* 10111 */ ('Q', '1'),
        /* 11000 */ ('O', '9'),
        /* 11001 */ ('B', '?'),
        /* 11010 */ ('G', '&'),
        /* 11011 */ ('\0', '\0'),  // FIGS (cambio a cifras): se trata aparte
        /* 11100 */ ('M', '.'),
        /* 11101 */ ('X', '/'),
        /* 11110 */ ('V', ';'),
        /* 11111 */ ('\0', '\0'),  // LTRS (cambio a letras): se trata aparte
    ];

    private static readonly Dictionary<char, byte> CodigoDeLetra = Construir(JuegoBaudot.Letras);
    private static readonly Dictionary<char, byte> CodigoDeCifra = Construir(JuegoBaudot.Cifras);

    /// <summary>Código Baudot (0–31) de un carácter en el juego dado, o nulo si no está en él.</summary>
    public static byte? Codigo(char c, JuegoBaudot juego) =>
        (juego == JuegoBaudot.Letras ? CodigoDeLetra : CodigoDeCifra).TryGetValue(c, out var codigo) ? codigo : null;

    /// <summary>
    /// El juego en que hay que estar para mandar <paramref name="c"/>: el que ya se tenga si vale
    /// en los dos (espacio, CR, LF), o el que haga falta si no.
    /// </summary>
    /// <returns>Nulo si el carácter no es representable en Baudot.</returns>
    public static JuegoBaudot? JuegoRequerido(char c, JuegoBaudot juegoActual)
    {
        if (EsComun(c)) return juegoActual;
        if (CodigoDeLetra.ContainsKey(c)) return JuegoBaudot.Letras;
        if (CodigoDeCifra.ContainsKey(c)) return JuegoBaudot.Cifras;
        return null;
    }

    /// <summary>El carácter vale igual en los dos juegos (espacio, retorno de carro, salto de línea).</summary>
    public static bool EsComun(char c) => c is ' ' or '\r' or '\n';

    /// <summary>
    /// Decodifica un código de 5 bits: cambia de juego (y no emite nada) o da el carácter que
    /// toca en el juego actual. Aplica USOS: un espacio en CIFRAS deja <paramref name="juego"/> en
    /// LETRAS.
    /// </summary>
    /// <param name="codigo">El código, de 0 a 31.</param>
    /// <param name="juego">Juego actual; se actualiza con los cambios y con USOS.</param>
    /// <returns>El texto a emitir: un carácter, o vacío si era NUL o un cambio de juego.</returns>
    public static string Decodificar(byte codigo, ref JuegoBaudot juego)
    {
        if (codigo == CambioACifras)
        {
            juego = JuegoBaudot.Cifras;
            return string.Empty;
        }

        if (codigo == CambioALetras)
        {
            juego = JuegoBaudot.Letras;
            return string.Empty;
        }

        var (letra, cifra) = Tabla[codigo];
        var c = juego == JuegoBaudot.Letras ? letra : cifra;
        if (c == ' ' && juego == JuegoBaudot.Cifras) juego = JuegoBaudot.Letras; // USOS
        return c == SinTexto ? string.Empty : c.ToString();
    }

    private static Dictionary<char, byte> Construir(JuegoBaudot juego)
    {
        var mapa = new Dictionary<char, byte>();
        for (var codigo = 0; codigo < Tabla.Length; codigo++)
        {
            if (codigo is CambioACifras or CambioALetras) continue;
            var c = juego == JuegoBaudot.Letras ? Tabla[codigo].Letra : Tabla[codigo].Cifra;
            if (c != SinTexto) mapa[c] = (byte)codigo;
        }

        return mapa;
    }
}
