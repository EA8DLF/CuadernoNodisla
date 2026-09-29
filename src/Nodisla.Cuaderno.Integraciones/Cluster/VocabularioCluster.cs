using System.Text.RegularExpressions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>Lo que se puede sacar del comentario de un anuncio de cluster.</summary>
/// <param name="Modo">Modo que nombra el comentario. Vacio si no lo dice.</param>
/// <param name="Decibelios">Relacion senal-ruido que da una estacion de escucha automatica.</param>
/// <param name="PalabrasPorMinuto">Velocidad en CW que da una estacion de escucha automatica.</param>
/// <param name="Referencias">Referencias de POTA, SOTA, IOTA o WWFF que aparezcan.</param>
public sealed record LecturaDeComentario(
    Modo Modo,
    int? Decibelios,
    int? PalabrasPorMinuto,
    IReadOnlyList<ReferenciaAnunciada> Referencias)
{
    /// <summary>Comentario del que no se saca nada.</summary>
    public static LecturaDeComentario Vacia { get; } = new(Modo.Vacio, null, null, []);
}

/// <summary>
/// Vocabulario que se reconoce en el comentario de un anuncio: modos, decibelios, velocidad
/// y referencias de programas de activaciones.
/// </summary>
/// <remarks>
/// La lista de palabras corrientes es la misma que Log4OM guarda en
/// <c>%APPDATA%\Log4OM2\clusterknownwords.txt</c>: son las que aparecen una y otra vez en
/// los comentarios (<c>TNX</c>, <c>CQ</c>, <c>UP</c>, <c>SPLIT</c>, <c>73</c>) y que por
/// tanto no hay que confundir con un modo ni con una referencia.
/// </remarks>
public static partial class VocabularioCluster
{
    /// <summary>
    /// Palabras corrientes de los comentarios, copiadas de <c>clusterknownwords.txt</c>.
    /// </summary>
    public static IReadOnlyList<string> PalabrasCorrientes { get; } =
    [
        "TNX", "QSO", "CQ", "DX", "FROM", "CONTEST", "TEST", "UP", "DN", "SPECIAL",
        "CALL", "TU", "FOR", "NEW", "MODE", "TKS", "73", "73!", "SPLIT", "WPM",
        "BOOMING", "NICE",
    ];

    private static readonly HashSet<string> Corrientes =
        new(PalabrasCorrientes, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Alias que usan los operadores y que no son nombres ADIF de modo.
    /// </summary>
    /// <remarks>
    /// Aqui solo entra lo que se puede traducir a un modo ADIF de verdad. Palabras como
    /// <c>DIGI</c> o <c>DIGITAL</c> no dicen que modo es, y <see cref="Modo"/> no tiene forma
    /// de representarlas: se dejan en el comentario, que es donde el operador las lee.
    /// </remarks>
    private static readonly Dictionary<string, string> AliasDeModo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PHONE"] = "SSB",
        ["FONE"] = "SSB",
        ["PSK"] = "PSK31",
        ["BPSK"] = "PSK31",
        ["BPSK31"] = "PSK31",
        ["BPSK63"] = "PSK63",
        ["JT65A"] = "JT65",
    };

    /// <summary>Indica si una palabra es de las corrientes en los comentarios.</summary>
    public static bool EsPalabraCorriente(string palabra) => Corrientes.Contains(palabra);

    /// <summary>Lee todo lo que el comentario de un anuncio deja sacar.</summary>
    public static LecturaDeComentario Leer(string? comentario)
    {
        if (string.IsNullOrWhiteSpace(comentario)) return LecturaDeComentario.Vacia;

        var decibelios = PrimerNumero(Decibelios().Match(comentario));
        var velocidad = PrimerNumero(Velocidad().Match(comentario));
        var modo = BuscarModo(comentario);
        var referencias = BuscarReferencias(comentario);

        return referencias.Count == 0 && modo.EsVacio && decibelios is null && velocidad is null
            ? LecturaDeComentario.Vacia
            : new LecturaDeComentario(modo, decibelios, velocidad, referencias);
    }

    /// <summary>Modo que nombra el comentario. Vacio si no nombra ninguno.</summary>
    /// <remarks>
    /// Se devuelve el primero que aparece, que es el que ponen las estaciones de escucha
    /// automatica justo detras del indicativo. Las palabras corrientes no se miran, para que
    /// un <c>TEST</c> o un <c>NEW</c> no acaben convertidos en modo.
    /// </remarks>
    public static Modo BuscarModo(string? comentario)
    {
        if (string.IsNullOrWhiteSpace(comentario)) return Modo.Vacio;

        foreach (var trozo in comentario.Split(Separadores, StringSplitOptions.RemoveEmptyEntries))
        {
            var palabra = trozo.Trim('.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '"', '\'');
            if (palabra.Length is < 2 or > 16) continue;
            if (EsPalabraCorriente(palabra)) continue;

            if (AliasDeModo.TryGetValue(palabra, out var alias) &&
                Modo.TryParse(alias, null, out var traducido))
            {
                return traducido;
            }
            if (Modo.TryParse(palabra, null, out var modo)) return modo;
        }
        return Modo.Vacio;
    }

    /// <summary>Referencias de programas de activaciones que aparezcan en el comentario.</summary>
    /// <remarks>
    /// El orden importa: <c>EAFF-0123</c> es WWFF y no POTA, y <c>EA8/GC-001</c> es SOTA y no
    /// IOTA, asi que se prueban los patrones mas especificos primero.
    /// </remarks>
    public static IReadOnlyList<ReferenciaAnunciada> BuscarReferencias(string? comentario)
    {
        if (string.IsNullOrWhiteSpace(comentario)) return [];

        string texto = comentario;
        var encontradas = new List<ReferenciaAnunciada>();
        var ocupado = new List<(int Inicio, int Fin)>();

        void Recoger(Regex patron, TipoDeReferencia tipo)
        {
            foreach (Match m in patron.Matches(texto))
            {
                var solapa = false;
                foreach (var (inicio, fin) in ocupado)
                {
                    if (m.Index < fin && inicio < m.Index + m.Length) { solapa = true; break; }
                }
                if (solapa) continue;
                ocupado.Add((m.Index, m.Index + m.Length));
                encontradas.Add(new ReferenciaAnunciada(tipo, m.Value.ToUpperInvariant()));
            }
        }

        Recoger(Wwff(), TipoDeReferencia.Wwff);
        Recoger(Sota(), TipoDeReferencia.Sota);
        Recoger(Iota(), TipoDeReferencia.Iota);
        Recoger(Pota(), TipoDeReferencia.Pota);
        return encontradas;
    }

    /// <summary>
    /// Localizador que algunos nodos anaden al final de la linea.
    /// </summary>
    /// <remarks>
    /// Se exigen cuatro caracteres por lo menos: un localizador de dos se confundiria con el
    /// continente, que es lo otro que ponen ahi, y <c>NA</c>, <c>AF</c> y <c>OC</c> son
    /// localizadores validos ademas de continentes.
    /// </remarks>
    public static Locator BuscarLocator(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Locator.Vacio;
        var v = texto.Trim();
        if (v.Length is not (4 or 6 or 8 or 10)) return Locator.Vacio;
        return Locator.TryParse(v, out var locator) ? locator : Locator.Vacio;
    }

    private static readonly char[] Separadores = [' ', '\t'];

    private static int? PrimerNumero(Match m) =>
        m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : null;

    /// <summary>Decibelios que anuncia una estacion de escucha automatica, por ejemplo <c>25 dB</c>.</summary>
    [GeneratedRegex(@"(-?\d{1,3})\s*dB\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Decibelios();

    /// <summary>Velocidad en CW, por ejemplo <c>28 WPM</c>.</summary>
    [GeneratedRegex(@"(\d{1,3})\s*WPM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Velocidad();

    /// <summary>Referencia de parque, por ejemplo <c>US-1234</c> o <c>EA8-0025</c>.</summary>
    [GeneratedRegex(@"\b[A-Z0-9]{1,3}-\d{4,5}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pota();

    /// <summary>Referencia de cumbre, por ejemplo <c>EA8/GC-001</c>.</summary>
    [GeneratedRegex(@"\b[A-Z0-9]{1,3}/[A-Z]{2}-\d{3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Sota();

    /// <summary>Referencia de isla, por ejemplo <c>AF-004</c>.</summary>
    [GeneratedRegex(@"\b(?:AF|AN|AS|EU|NA|OC|SA)-\d{3}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Iota();

    /// <summary>Referencia de espacio natural, por ejemplo <c>EAFF-0123</c>.</summary>
    [GeneratedRegex(@"\b[A-Z0-9]{1,3}FF-\d{4}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Wwff();
}
