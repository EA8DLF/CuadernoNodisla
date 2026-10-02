using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>De qué tipo es una entrada del glosario de telegrafía.</summary>
public enum GrupoDelGlosarioCw
{
    /// <summary>Código Q (QRZ, QSL, QTH...).</summary>
    CodigoQ,

    /// <summary>Abreviatura de telegrafía (TU, FB, OM, 73...).</summary>
    Abreviatura,

    /// <summary>Prosigno (&lt;AR&gt;, &lt;SK&gt;...).</summary>
    Prosigno,

    /// <summary>Número corto de concurso (N = 9, T = 0...). Solo en el glosario: suelto es ambiguo.</summary>
    NumeroCorto,
}

/// <summary>Una entrada del glosario: lo que se lee en el aire y lo que significa.</summary>
/// <param name="Texto">Como sale en el texto decodificado («QRZ», «&lt;AR&gt;», «5NN»).</param>
/// <param name="Clave">Clave del significado en los textos del programa.</param>
/// <param name="Grupo">Código Q, abreviatura, prosigno o número corto.</param>
public sealed record EntradaDelGlosarioCw(string Texto, string Clave, GrupoDelGlosarioCw Grupo)
{
    /// <summary>El significado, en el idioma del programa.</summary>
    public string Significado => Textos.T(Clave);

    /// <summary>El grupo, para leer.</summary>
    public string GrupoTexto => Textos.T($"Digital.CwGlosario.Grupo{Grupo}");
}

/// <summary>
/// El glosario propio de telegrafía: códigos Q, abreviaturas, prosignos y números cortos de
/// concurso, con su significado en los seis idiomas del programa (apartado Digital,
/// <c>Digital.CwAbrev.*</c>).
/// </summary>
public static class GlosarioCw
{
    private static readonly string[] CodigosQ =
    [
        "QRA", "QRG", "QRK", "QRL", "QRM", "QRN", "QRO", "QRP", "QRQ", "QRS", "QRT", "QRU", "QRV", "QRX",
        "QRZ", "QSB", "QSK", "QSL", "QSO", "QSP", "QSY", "QTH", "QTR",
    ];

    private static readonly string[] Abreviaturas =
    [
        "CQ", "DE", "K", "R", "BK", "TU", "TNX", "TKS", "FB", "OM", "YL", "XYL", "ES", "HR", "UR", "RST",
        "5NN", "ENN", "599", "AGN", "PSE", "GM", "GA", "GE", "GN", "73", "88", "72", "WX", "ANT", "RIG",
        "PWR", "DX", "CFM", "NR", "?", "CL", "CUL", "GL", "HW", "OP", "SRI", "TEST", "WPM", "ABT", "FER",
        "VY", "NW", "SIG", "RPT", "INFO", "NAME", "QRZ?", "TT", "B4", "PSE",
    ];

    private static readonly string[] Prosignos = ["<AR>", "<SK>", "<BT>", "<KN>", "<AS>", "<SN>", "<HH>", "<KA>", "<SOS>"];

    private static readonly (string Letra, string Cifra)[] Cortos =
    [
        ("T", "0"), ("A", "1"), ("U", "2"), ("V", "3"), ("E", "5"), ("B", "7"), ("D", "8"), ("N", "9"),
    ];

    /// <summary>Todas las entradas, en el orden del glosario.</summary>
    public static IReadOnlyList<EntradaDelGlosarioCw> Entradas { get; } = Construir();

    private static readonly Dictionary<string, EntradaDelGlosarioCw> PorTexto = Entradas
        .Where(e => e.Grupo != GrupoDelGlosarioCw.NumeroCorto)
        .GroupBy(e => e.Texto, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    /// <summary>La entrada de una palabra del texto, o nula si no es ni código ni abreviatura.</summary>
    public static EntradaDelGlosarioCw? Buscar(string palabra)
    {
        ArgumentNullException.ThrowIfNull(palabra);
        return PorTexto.TryGetValue(palabra, out var e) ? e : null;
    }

    /// <summary>La clave del significado de un texto («?» → «Interrogacion», «&lt;AR&gt;» → «Prosigno_AR»).</summary>
    public static string ClaveDe(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var limpio = texto switch
        {
            "?" => "Interrogacion",
            "QRZ?" => "QRZ_Pregunta",
            _ when texto.StartsWith('<') => "Prosigno_" + texto.Trim('<', '>'),
            _ => texto,
        };
        return "Digital.CwAbrev." + limpio;
    }

    private static List<EntradaDelGlosarioCw> Construir()
    {
        var lista = new List<EntradaDelGlosarioCw>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var q in CodigosQ) Anadir(q, GrupoDelGlosarioCw.CodigoQ);
        foreach (var a in Abreviaturas) Anadir(a, GrupoDelGlosarioCw.Abreviatura);
        foreach (var p in Prosignos) Anadir(p, GrupoDelGlosarioCw.Prosigno);
        foreach (var (letra, cifra) in Cortos)
            lista.Add(new EntradaDelGlosarioCw(letra, "Digital.CwAbrev.Corto" + cifra, GrupoDelGlosarioCw.NumeroCorto));
        return lista;

        void Anadir(string texto, GrupoDelGlosarioCw grupo)
        {
            if (vistos.Add(texto)) lista.Add(new EntradaDelGlosarioCw(texto, ClaveDe(texto), grupo));
        }
    }
}
