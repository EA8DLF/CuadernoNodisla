namespace Nodisla.Cuaderno.Dominio.Valores;

/// <summary>
/// Modo de emision segun ADIF. En ADIF el par correcto es <c>MODE</c> + <c>SUBMODE</c>:
/// FT8 no es un modo, es un submodo de MFSK. Aqui se guarda el par y se resuelve
/// automaticamente cuando el usuario escribe solo el submodo.
/// </summary>
public readonly record struct Modo
{
    private Modo(string principal, string? submodo)
    {
        Principal = principal;
        Submodo = string.IsNullOrEmpty(submodo) ? null : submodo;
    }

    /// <summary>Campo <c>MODE</c> de ADIF, por ejemplo <c>MFSK</c>.</summary>
    public string Principal { get; }

    /// <summary>Campo <c>SUBMODE</c> de ADIF, por ejemplo <c>FT8</c>. Puede ser nulo.</summary>
    public string? Submodo { get; }

    public static Modo Vacio => default;

    public bool EsVacio => string.IsNullOrEmpty(Principal);

    /// <summary>Nombre con el que el operador conoce el modo: el submodo si lo hay.</summary>
    public string NombreUsual => Submodo ?? Principal;

    /// <summary>Catalogo de modos ADIF con sus submodos. Fuente: ADIF 3.1.5, tabla Mode.</summary>
    private static readonly Dictionary<string, string[]> Catalogo = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AM"] = [],
        ["ARDOP"] = [],
        ["ATV"] = [],
        ["CHIP"] = ["CHIP64", "CHIP128"],
        ["CLO"] = [],
        ["CONTESTI"] = [],
        ["CW"] = ["PCW"],
        ["DIGITALVOICE"] = ["C4FM", "DMR", "DSTAR", "FREEDV", "M17"],
        ["DOMINO"] = ["DOM-M", "DOM4", "DOM5", "DOM8", "DOM11", "DOM16", "DOM22", "DOM44", "DOM88", "DOMINOEX", "DOMINOF"],
        ["DYNAMIC"] = ["VARA HF", "VARA SATELLITE", "VARA FM 1200", "VARA FM 9600"],
        ["FAX"] = [],
        ["FM"] = [],
        ["FSK441"] = [],
        ["FT8"] = [],
        ["HELL"] = ["FMHELL", "FSKH105", "FSKH245", "FSKHELL", "HELL80", "HELLX5", "HELLX9", "HFSK", "PSKHELL", "SLOWHELL"],
        ["ISCAT"] = ["ISCAT-A", "ISCAT-B"],
        ["JT4"] = ["JT4A", "JT4B", "JT4C", "JT4D", "JT4E", "JT4F", "JT4G"],
        ["JT6M"] = [],
        ["JT9"] = ["JT9-1", "JT9-2", "JT9-5", "JT9-10", "JT9-30", "JT9A", "JT9B", "JT9C", "JT9D", "JT9E", "JT9E FAST", "JT9F", "JT9F FAST", "JT9G", "JT9G FAST", "JT9H", "JT9H FAST"],
        ["JT44"] = [],
        ["JT65"] = ["JT65A", "JT65B", "JT65B2", "JT65C", "JT65C2"],
        ["MFSK"] = ["FSQCALL", "FST4", "FST4W", "FT4", "JS8", "JTMS", "MFSK4", "MFSK8", "MFSK11", "MFSK16", "MFSK22", "MFSK31", "MFSK32", "MFSK64", "MFSK64L", "MFSK128", "MFSK128L", "Q65"],
        ["MSK144"] = [],
        ["MT63"] = [],
        ["OLIVIA"] = ["OLIVIA 4/125", "OLIVIA 4/250", "OLIVIA 8/250", "OLIVIA 8/500", "OLIVIA 16/500", "OLIVIA 16/1000", "OLIVIA 32/1000"],
        ["OPERA"] = ["OPERA-BEACON", "OPERA-QSO"],
        ["PAC"] = ["PAC2", "PAC3", "PAC4"],
        ["PAX"] = ["PAX2"],
        ["PKT"] = [],
        ["PSK"] = ["8PSK125", "8PSK125F", "8PSK125FL", "8PSK250", "8PSK250F", "8PSK250FL", "8PSK500", "8PSK500F", "8PSK1000", "8PSK1000F", "8PSK1200F", "FSK31", "PSK10", "PSK31", "PSK63", "PSK63F", "PSK63RC10", "PSK63RC20", "PSK63RC32", "PSK63RC4", "PSK63RC5", "PSK125", "PSK125C12", "PSK125R", "PSK125RC10", "PSK125RC12", "PSK125RC16", "PSK125RC4", "PSK125RC5", "PSK250", "PSK250C6", "PSK250R", "PSK250RC2", "PSK250RC3", "PSK250RC5", "PSK250RC6", "PSK250RC7", "PSK500", "PSK500C2", "PSK500C4", "PSK500R", "PSK500RC2", "PSK500RC3", "PSK500RC4", "PSK800C2", "PSK800RC2", "PSK1000", "PSK1000C2", "PSK1000R", "PSK1000RC2", "PSKAM10", "PSKAM31", "PSKAM50", "PSKFEC31", "QPSK31", "QPSK63", "QPSK125", "QPSK250", "QPSK500", "SIM31"],
        ["PSK2K"] = [],
        ["Q15"] = [],
        ["QRA64"] = ["QRA64A", "QRA64B", "QRA64C", "QRA64D", "QRA64E"],
        ["ROS"] = ["ROS-EME", "ROS-HF", "ROS-MF"],
        ["RTTY"] = ["ASCI"],
        ["RTTYM"] = [],
        ["SSB"] = ["LSB", "USB"],
        ["SSTV"] = [],
        ["T10"] = [],
        ["THOR"] = ["THOR-M", "THOR4", "THOR5", "THOR8", "THOR11", "THOR16", "THOR22", "THOR25X4", "THOR50X1", "THOR50X2", "THOR100"],
        ["THRB"] = ["THRBX", "THRBX1", "THRBX2", "THRBX4", "THROB1", "THROB2", "THROB4"],
        ["TOR"] = ["AMTORFEC", "GTOR", "NAVTEX", "SITORB"],
        ["V4"] = [],
        ["VOI"] = [],
        ["WINMOR"] = [],
        ["WSPR"] = [],
    };

    /// <summary>Indice inverso submodo -> modo principal, para resolver lo que teclea el usuario.</summary>
    private static readonly Dictionary<string, string> PorSubmodo = ConstruirIndiceInverso();

    private static Dictionary<string, string> ConstruirIndiceInverso()
    {
        var indice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (principal, submodos) in Catalogo)
        {
            foreach (var s in submodos) indice[s] = principal;
        }
        return indice;
    }

    /// <summary>Todos los modos principales de ADIF.</summary>
    public static IReadOnlyCollection<string> ModosPrincipales => Catalogo.Keys;

    /// <summary>Submodos declarados para un modo principal.</summary>
    public static IReadOnlyList<string> SubmodosDe(string principal) =>
        Catalogo.TryGetValue(principal, out var s) ? s : [];

    /// <summary>
    /// Interpreta lo que escribe el operador. Acepta <c>FT8</c>, <c>MFSK/FT8</c> o el par
    /// separado, y resuelve el modo principal cuando solo se da el submodo.
    /// </summary>
    public static bool TryParse(string? modo, string? submodo, out Modo resultado)
    {
        resultado = Vacio;
        var m = modo?.Trim().ToUpperInvariant();
        var s = submodo?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(m) && string.IsNullOrEmpty(s)) return false;

        // "MFSK/FT8" en un solo campo.
        if (!string.IsNullOrEmpty(m) && string.IsNullOrEmpty(s) && m.Contains('/'))
        {
            var partes = m.Split('/', 2);
            if (Catalogo.ContainsKey(partes[0])) { m = partes[0]; s = partes[1]; }
        }

        // Solo se dio el submodo: se busca su modo principal.
        if (string.IsNullOrEmpty(m) && !string.IsNullOrEmpty(s))
        {
            if (PorSubmodo.TryGetValue(s, out var principalDeducido))
            {
                resultado = new Modo(principalDeducido, s);
                return true;
            }
            if (Catalogo.ContainsKey(s)) { resultado = new Modo(s, null); return true; }
            return false;
        }

        // Se dio un modo que en realidad es un submodo: se corrige.
        if (!string.IsNullOrEmpty(m) && !Catalogo.ContainsKey(m) && PorSubmodo.TryGetValue(m, out var principal))
        {
            resultado = new Modo(principal, m);
            return true;
        }

        if (string.IsNullOrEmpty(m) || !Catalogo.ContainsKey(m)) return false;

        resultado = new Modo(m, s);
        return true;
    }

    public static Modo Parse(string? modo, string? submodo = null) =>
        TryParse(modo, submodo, out var r) ? r : throw new FormatException($"Modo no reconocido: {modo}/{submodo}");

    /// <summary>
    /// Crea el modo sin validarlo contra el catalogo. Necesario al importar ADIF ajeno:
    /// perder un QSO por un modo que ADIF aun no recoge seria peor que aceptarlo.
    /// </summary>
    public static Modo Crudo(string? modo, string? submodo = null) =>
        new((modo ?? string.Empty).Trim().ToUpperInvariant(), submodo?.Trim().ToUpperInvariant());

    /// <summary>Indica si el modo se opera por tarjeta de sonido con informes en dB.</summary>
    public bool UsaInformeEnDecibelios => Principal is "MFSK" or "FT8" or "JT65" or "JT9" or "JT4"
        or "QRA64" or "MSK144" or "FSK441" or "ISCAT" or "WSPR" or "Q15"
        || Submodo is "FT4" or "FT8" or "Q65" or "FST4" or "FST4W" or "JS8";

    public override string ToString() => Submodo is null ? Principal : $"{Principal}/{Submodo}";
}
