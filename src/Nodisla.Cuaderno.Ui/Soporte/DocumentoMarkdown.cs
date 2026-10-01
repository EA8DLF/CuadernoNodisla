using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>Un bloque de un documento Markdown: titulo, parrafo, lista, tabla…</summary>
public abstract record BloqueMd;

/// <summary>Titulo de nivel 1 a 6, con el ancla que le daria GitHub.</summary>
public sealed record TituloMd(int Nivel, IReadOnlyList<TrozoMd> Trozos, string Ancla) : BloqueMd;

/// <summary>Parrafo de texto corrido.</summary>
public sealed record ParrafoMd(IReadOnlyList<TrozoMd> Trozos) : BloqueMd;

/// <summary>Lista con viñetas o numerada.</summary>
public sealed record ListaMd(bool Numerada, IReadOnlyList<ElementoDeListaMd> Elementos) : BloqueMd;

/// <summary>Un elemento de una lista, con sus sublistas.</summary>
public sealed record ElementoDeListaMd(IReadOnlyList<TrozoMd> Trozos, IReadOnlyList<BloqueMd> Hijos);

/// <summary>Cita (lineas que empiezan por «&gt;»). Se usa para las notas y los avisos.</summary>
public sealed record CitaMd(IReadOnlyList<BloqueMd> Bloques) : BloqueMd;

/// <summary>Bloque de codigo entre tres comillas invertidas.</summary>
public sealed record CodigoMd(string Texto) : BloqueMd;

/// <summary>Tabla con cabecera.</summary>
public sealed record TablaMd(IReadOnlyList<IReadOnlyList<TrozoMd>> Cabecera, IReadOnlyList<IReadOnlyList<IReadOnlyList<TrozoMd>>> Filas) : BloqueMd;

/// <summary>Imagen sola en su linea: una captura con su pie.</summary>
public sealed record ImagenMd(string Ruta, string Texto) : BloqueMd;

/// <summary>Raya horizontal.</summary>
public sealed record RayaMd : BloqueMd;

/// <summary>Un trozo de texto con su formato.</summary>
/// <param name="Texto">El texto, ya sin marcas.</param>
/// <param name="Negrita">Va en negrita.</param>
/// <param name="Cursiva">Va en cursiva.</param>
/// <param name="Codigo">Va en letra de maquina (teclas, nombres de fichero…).</param>
/// <param name="Enlace">Destino del enlace, o nulo.</param>
public sealed record TrozoMd(string Texto, bool Negrita = false, bool Cursiva = false, bool Codigo = false, string? Enlace = null);

/// <summary>
/// Lector de Markdown sencillo, el justo para la ayuda del programa.
/// </summary>
/// <remarks>
/// <para>
/// No pretende ser CommonMark: entiende lo que usan los capitulos de <c>docs/ayuda</c> —titulos,
/// parrafos, listas anidadas por sangria, citas, tablas, bloques de codigo, imagenes solas en su
/// linea, negrita, cursiva, codigo y enlaces— y nada mas. Lo que no entiende sale como texto.
/// </para>
/// <para>
/// No sabe nada de WPF: la misma lectura la pinta la ventana de ayuda en un FlowDocument y la
/// herramienta de la ayuda web en HTML. Asi las dos dicen exactamente lo mismo.
/// </para>
/// </remarks>
public static partial class DocumentoMarkdown
{
    [GeneratedRegex(@"^(?<sangria> *)(?<marca>[-*+]|\d+[.)])\s+(?<texto>.*)$")]
    private static partial Regex ElementoDeLista();

    [GeneratedRegex(@"^!\[(?<texto>[^\]]*)\]\((?<ruta>[^)\s]+)(?:\s+""[^""]*"")?\)\s*$")]
    private static partial Regex ImagenSola();

    [GeneratedRegex(@"^(?<almohadillas>#{1,6})\s+(?<texto>.*?)\s*#*\s*$")]
    private static partial Regex Titulo();

    [GeneratedRegex(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)*\|?\s*$")]
    private static partial Regex SeparadorDeTabla();

    [GeneratedRegex(@"^\s*([-*_])\s*(\1\s*){2,}$")]
    private static partial Regex Raya();

    [GeneratedRegex(@"\G<(?<d>https?://[^>\s]+|mailto:[^>\s]+|[^@\s<>]+@[^>\s]+\.[A-Za-z]{2,})>")]
    private static partial Regex AutoEnlace();

    /// <summary>Lee un documento entero.</summary>
    /// <param name="texto">El Markdown.</param>
    /// <returns>Los bloques, en orden.</returns>
    public static IReadOnlyList<BloqueMd> Leer(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var lineas = QuitarComentarios(texto.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')).Split('\n');
        var i = 0;
        return LeerBloques(lineas, ref i, lineas.Length);
    }

    /// <summary>El titulo de primer nivel del documento, o nulo si no tiene.</summary>
    /// <param name="bloques">El documento ya leido.</param>
    /// <returns>El texto del primer titulo de nivel 1.</returns>
    public static string? TituloPrincipal(IReadOnlyList<BloqueMd> bloques) =>
        bloques.OfType<TituloMd>().FirstOrDefault(t => t.Nivel == 1) is { } titulo ? TextoPlano(titulo.Trozos) : null;

    /// <summary>El texto sin formato de unos trozos.</summary>
    /// <param name="trozos">Los trozos.</param>
    /// <returns>El texto seguido.</returns>
    public static string TextoPlano(IEnumerable<TrozoMd> trozos) => string.Concat(trozos.Select(t => t.Texto));

    /// <summary>
    /// El ancla de un titulo como la calcula GitHub: minusculas, sin signos y con guiones en
    /// vez de espacios. Asi los enlaces «capitulo.md#apartado» valen igual en la web y aqui.
    /// </summary>
    /// <param name="texto">Texto del titulo.</param>
    /// <returns>El ancla.</returns>
    public static string Ancla(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Trim().ToLower(CultureInfo.InvariantCulture))
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') sb.Append(c);
            else if (c == ' ') sb.Append('-');
        }

        return sb.ToString();
    }

    private static string QuitarComentarios(string texto) =>
        Regex.Replace(texto, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static List<BloqueMd> LeerBloques(string[] lineas, ref int i, int fin)
    {
        var bloques = new List<BloqueMd>();
        while (i < fin)
        {
            var linea = lineas[i];
            if (string.IsNullOrWhiteSpace(linea))
            {
                i++;
                continue;
            }

            if (linea.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                bloques.Add(LeerCodigo(lineas, ref i, fin));
                continue;
            }

            if (Titulo().Match(linea) is { Success: true } titulo)
            {
                var trozos = LeerEnLinea(titulo.Groups["texto"].Value);
                bloques.Add(new TituloMd(titulo.Groups["almohadillas"].Length, trozos, Ancla(TextoPlano(trozos))));
                i++;
                continue;
            }

            if (Raya().IsMatch(linea))
            {
                bloques.Add(new RayaMd());
                i++;
                continue;
            }

            if (ImagenSola().Match(linea.Trim()) is { Success: true } imagen)
            {
                bloques.Add(new ImagenMd(imagen.Groups["ruta"].Value, imagen.Groups["texto"].Value));
                i++;
                continue;
            }

            if (linea.TrimStart().StartsWith('>'))
            {
                var dentro = new List<string>();
                while (i < fin && lineas[i].TrimStart().StartsWith('>'))
                {
                    var resto = lineas[i].TrimStart()[1..];
                    dentro.Add(resto.StartsWith(' ') ? resto[1..] : resto);
                    i++;
                }

                var arr = dentro.ToArray();
                var j = 0;
                bloques.Add(new CitaMd(LeerBloques(arr, ref j, arr.Length)));
                continue;
            }

            if (linea.Contains('|', StringComparison.Ordinal) && i + 1 < fin && SeparadorDeTabla().IsMatch(lineas[i + 1]))
            {
                bloques.Add(LeerTabla(lineas, ref i, fin));
                continue;
            }

            if (ElementoDeLista().IsMatch(linea))
            {
                bloques.Add(LeerLista(lineas, ref i, fin, Sangria(linea)));
                continue;
            }

            bloques.Add(LeerParrafo(lineas, ref i, fin));
        }

        return bloques;
    }

    private static bool EsNumerada(string linea) => char.IsDigit(linea.TrimStart()[0]);

    private static int Sangria(string linea) => linea.Length - linea.TrimStart(' ').Length;

    private static bool EmpiezaBloque(string linea) =>
        string.IsNullOrWhiteSpace(linea)
        || linea.TrimStart().StartsWith("```", StringComparison.Ordinal)
        || linea.TrimStart().StartsWith('>')
        || Titulo().IsMatch(linea)
        || ImagenSola().IsMatch(linea.Trim())
        || ElementoDeLista().IsMatch(linea)
        || Raya().IsMatch(linea);

    private static CodigoMd LeerCodigo(string[] lineas, ref int i, int fin)
    {
        var sangria = Sangria(lineas[i]);
        i++;
        var sb = new StringBuilder();
        while (i < fin && !lineas[i].TrimStart().StartsWith("```", StringComparison.Ordinal))
        {
            var l = lineas[i];
            sb.AppendLine(l.Length >= sangria && string.IsNullOrWhiteSpace(l[..sangria]) ? l[sangria..] : l.TrimStart());
            i++;
        }

        i++; // la valla de cierre
        return new CodigoMd(sb.ToString().TrimEnd('\r', '\n'));
    }

    private static ParrafoMd LeerParrafo(string[] lineas, ref int i, int fin)
    {
        var sb = new StringBuilder(lineas[i].Trim());
        i++;
        while (i < fin && !EmpiezaBloque(lineas[i])
               && !(lineas[i].Contains('|', StringComparison.Ordinal) && i + 1 < fin && SeparadorDeTabla().IsMatch(lineas[i + 1])))
        {
            sb.Append(' ').Append(lineas[i].Trim());
            i++;
        }

        return new ParrafoMd(LeerEnLinea(sb.ToString()));
    }

    private static ListaMd LeerLista(string[] lineas, ref int i, int fin, int sangria)
    {
        var numerada = EsNumerada(lineas[i]);
        var elementos = new List<ElementoDeListaMd>();

        while (i < fin)
        {
            var linea = lineas[i];
            if (string.IsNullOrWhiteSpace(linea))
            {
                // Una linea en blanco no corta la lista si lo siguiente sigue siendo de ella.
                var k = i + 1;
                while (k < fin && string.IsNullOrWhiteSpace(lineas[k])) k++;
                if (k < fin && ElementoDeLista().IsMatch(lineas[k]) && Sangria(lineas[k]) >= sangria
                    && (Sangria(lineas[k]) > sangria || EsNumerada(lineas[k]) == numerada))
                {
                    i = k;
                    continue;
                }

                break;
            }

            if (ElementoDeLista().Match(linea) is not { Success: true } marca
                || Sangria(linea) != sangria
                || EsNumerada(linea) != numerada)
            {
                break;
            }

            var texto = new StringBuilder(marca.Groups["texto"].Value.Trim());
            var hijos = new List<BloqueMd>();
            i++;

            while (i < fin && !string.IsNullOrWhiteSpace(lineas[i]))
            {
                var siguiente = lineas[i];
                var s = Sangria(siguiente);
                if (ElementoDeLista().IsMatch(siguiente))
                {
                    if (s <= sangria) break;
                    hijos.Add(LeerLista(lineas, ref i, fin, s));
                    continue;
                }

                if (s <= sangria && EmpiezaBloque(siguiente)) break;

                // Continuacion del mismo elemento (o, tras una sublista, un parrafo suyo).
                if (hijos.Count == 0) texto.Append(' ').Append(siguiente.Trim());
                else hijos.Add(new ParrafoMd(LeerEnLinea(siguiente.Trim())));
                i++;
            }

            elementos.Add(new ElementoDeListaMd(LeerEnLinea(texto.ToString()), hijos));
        }

        return new ListaMd(numerada, elementos);
    }

    private static TablaMd LeerTabla(string[] lineas, ref int i, int fin)
    {
        var cabecera = Celdas(lineas[i]).Select(LeerEnLinea).ToList();
        i += 2;
        var filas = new List<IReadOnlyList<IReadOnlyList<TrozoMd>>>();
        while (i < fin && lineas[i].Contains('|', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(lineas[i]))
        {
            var celdas = Celdas(lineas[i]).Select(LeerEnLinea).ToList();
            while (celdas.Count < cabecera.Count) celdas.Add([]);
            filas.Add(celdas);
            i++;
        }

        return new TablaMd(cabecera, filas);
    }

    private static List<string> Celdas(string linea)
    {
        var t = linea.Trim();
        if (t.StartsWith('|')) t = t[1..];
        if (t.EndsWith('|') && !t.EndsWith("\\|", StringComparison.Ordinal)) t = t[..^1];

        var celdas = new List<string>();
        var sb = new StringBuilder();
        var enCodigo = false;
        for (var k = 0; k < t.Length; k++)
        {
            var c = t[k];
            if (c == '\\' && k + 1 < t.Length && t[k + 1] == '|')
            {
                sb.Append('|');
                k++;
            }
            else if (c == '`')
            {
                enCodigo = !enCodigo;
                sb.Append(c);
            }
            else if (c == '|' && !enCodigo)
            {
                celdas.Add(sb.ToString().Trim());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        celdas.Add(sb.ToString().Trim());
        return celdas;
    }

    /// <summary>Lee el formato dentro de una linea: negrita, cursiva, codigo y enlaces.</summary>
    /// <param name="texto">El texto con sus marcas.</param>
    /// <returns>Los trozos.</returns>
    public static IReadOnlyList<TrozoMd> LeerEnLinea(string texto)
    {
        var trozos = new List<TrozoMd>();
        LeerEnLinea(texto, false, false, null, trozos);
        return Juntar(trozos);
    }

    private static void LeerEnLinea(string t, bool negrita, bool cursiva, string? enlace, List<TrozoMd> trozos)
    {
        var sb = new StringBuilder();
        void Soltar()
        {
            if (sb.Length == 0) return;
            trozos.Add(new TrozoMd(sb.ToString(), negrita, cursiva, false, enlace));
            sb.Clear();
        }

        var k = 0;
        while (k < t.Length)
        {
            var c = t[k];

            if (c == '\\' && k + 1 < t.Length && char.IsPunctuation(t[k + 1]) || c == '\\' && k + 1 < t.Length && char.IsSymbol(t[k + 1]))
            {
                sb.Append(t[k + 1]);
                k += 2;
                continue;
            }

            if (c == '`')
            {
                var cierre = t.IndexOf('`', k + 1);
                if (cierre > k)
                {
                    Soltar();
                    trozos.Add(new TrozoMd(t[(k + 1)..cierre], negrita, cursiva, true, enlace));
                    k = cierre + 1;
                    continue;
                }
            }

            if (c == '<' && enlace is null && AutoEnlace().Match(t, k) is { Success: true } auto && auto.Index == k)
            {
                Soltar();
                var destino = auto.Groups["d"].Value;
                var correo = !destino.Contains("://", StringComparison.Ordinal) && !destino.StartsWith("mailto:", StringComparison.Ordinal);
                trozos.Add(new TrozoMd(destino, negrita, cursiva, false, correo ? "mailto:" + destino : destino));
                k += auto.Length;
                continue;
            }

            if ((c == '[' || c == '!' && k + 1 < t.Length && t[k + 1] == '[') && enlace is null)
            {
                var abre = c == '!' ? k + 1 : k;
                var cierraTexto = BuscarCierre(t, abre, '[', ']');
                if (cierraTexto > 0 && cierraTexto + 1 < t.Length && t[cierraTexto + 1] == '(')
                {
                    var cierraRuta = t.IndexOf(')', cierraTexto + 2);
                    if (cierraRuta > 0)
                    {
                        Soltar();
                        var destino = t[(cierraTexto + 2)..cierraRuta].Trim().Split(' ')[0];
                        LeerEnLinea(t[(abre + 1)..cierraTexto], negrita, cursiva, destino, trozos);
                        k = cierraRuta + 1;
                        continue;
                    }
                }
            }

            if ((c == '*' || c == '_') && k + 1 < t.Length && t[k + 1] == c)
            {
                var marca = new string(c, 2);
                var cierre = t.IndexOf(marca, k + 2, StringComparison.Ordinal);
                if (cierre > k + 2)
                {
                    Soltar();
                    LeerEnLinea(t[(k + 2)..cierre], true, cursiva, enlace, trozos);
                    k = cierre + 2;
                    continue;
                }
            }

            if (c == '*' || c == '_' && (k == 0 || !char.IsLetterOrDigit(t[k - 1])))
            {
                var cierre = BuscarCierreDeCursiva(t, k, c);
                if (cierre > k + 1)
                {
                    Soltar();
                    LeerEnLinea(t[(k + 1)..cierre], negrita, true, enlace, trozos);
                    k = cierre + 1;
                    continue;
                }
            }

            sb.Append(c);
            k++;
        }

        Soltar();
    }

    private static int BuscarCierre(string t, int abre, char a, char b)
    {
        var nivel = 0;
        for (var k = abre; k < t.Length; k++)
        {
            if (t[k] == a) nivel++;
            else if (t[k] == b && --nivel == 0) return k;
        }

        return -1;
    }

    private static int BuscarCierreDeCursiva(string t, int k, char c)
    {
        if (k + 1 >= t.Length || char.IsWhiteSpace(t[k + 1])) return -1;
        for (var j = k + 1; j < t.Length; j++)
        {
            if (t[j] != c) continue;
            if (j + 1 < t.Length && t[j + 1] == c) { j++; continue; }
            if (char.IsWhiteSpace(t[j - 1])) continue;
            if (c == '_' && j + 1 < t.Length && char.IsLetterOrDigit(t[j + 1])) continue;
            return j;
        }

        return -1;
    }

    private static List<TrozoMd> Juntar(List<TrozoMd> trozos)
    {
        var juntos = new List<TrozoMd>(trozos.Count);
        foreach (var t in trozos)
        {
            if (juntos.Count > 0 && juntos[^1] is var u
                && u.Negrita == t.Negrita && u.Cursiva == t.Cursiva && u.Codigo == t.Codigo && !u.Codigo
                && u.Enlace == t.Enlace)
            {
                juntos[^1] = u with { Texto = u.Texto + t.Texto };
            }
            else
            {
                juntos.Add(t);
            }
        }

        return juntos;
    }
}
