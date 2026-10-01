using System.Text.RegularExpressions;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Que clase de mensaje es, a efectos de la secuencia del contacto.</summary>
public enum ClaseDeMensaje
{
    /// <summary>No se ha sacado nada util.</summary>
    Otro,

    /// <summary>Llamada general: <c>CQ EA8DLF IL18</c>, con o sin sufijo (DX, EU, POTA, TEST…).</summary>
    Cq,

    /// <summary>Alguien llama a alguien, con localizador o a secas: <c>EA8DLF IZ2ABC JN45</c>.</summary>
    Llamada,

    /// <summary>Informe sin R: <c>EA8DLF IZ2ABC -07</c>. En concurso, el intercambio sin R.</summary>
    Informe,

    /// <summary>Informe con R: <c>EA8DLF IZ2ABC R-07</c>. En concurso, <c>R</c> mas el intercambio.</summary>
    InformeConR,

    /// <summary><c>RR73</c>: recibido, y adios.</summary>
    Rr73,

    /// <summary><c>RRR</c>: recibido.</summary>
    Rrr,

    /// <summary><c>73</c>: adios.</summary>
    S73,
}

/// <summary>
/// Un mensaje de FT8 y compañia ya entendido: quien, a quien y que dice.
/// </summary>
/// <remarks>
/// El texto crudo se guarda siempre y esto es solo lo que se ha conseguido deducir. Nada de lo
/// que pase aqui puede tumbar una decodificacion.
/// </remarks>
public sealed record MensajeEstandar
{
    /// <summary>El texto tal y como llego.</summary>
    public string Texto { get; init; } = string.Empty;

    /// <summary>A quien va dirigido; vacio si es CQ o no se sabe.</summary>
    public string Llamado { get; init; } = string.Empty;

    /// <summary>Quien lo manda; vacio si no se sabe.</summary>
    public string Llamante { get; init; } = string.Empty;

    /// <summary>Sufijo de la llamada general: <c>DX</c>, <c>EU</c>, <c>POTA</c>, <c>TEST</c>…</summary>
    public string CqDirigido { get; init; } = string.Empty;

    /// <summary>Localizador de cuatro o seis caracteres que viaja en el mensaje.</summary>
    public string Locator { get; init; } = string.Empty;

    /// <summary>Informe en decibelios, si lo trae.</summary>
    public int? Informe { get; init; }

    /// <summary>El intercambio de concurso (clase y seccion, RST y estado, numero y localizador).</summary>
    public string Intercambio { get; init; } = string.Empty;

    /// <summary>Que es.</summary>
    public ClaseDeMensaje Clase { get; init; }

    /// <summary>
    /// La segunda mitad de un mensaje de fox: <c>K1ABC RR73; W9XYZ &lt;KH1/KH7Z&gt; -08</c>.
    /// </summary>
    public MensajeEstandar? Segundo { get; init; }

    /// <summary>Un mensaje del que no se sacó nada.</summary>
    public static MensajeEstandar Nada(string texto) => new() { Texto = texto, Clase = ClaseDeMensaje.Otro };

    /// <summary>Va dirigido a este indicativo.</summary>
    public bool VaDirigidoA(string indicativo) =>
        indicativo.Length > 0 && string.Equals(Llamado, indicativo, StringComparison.Ordinal);

    /// <summary>Lo manda este indicativo.</summary>
    public bool LoManda(string indicativo) =>
        indicativo.Length > 0 && string.Equals(Llamante, indicativo, StringComparison.Ordinal);
}

/// <summary>
/// Entiende los mensajes estandar de FT8, FT4 y los demas modos de 77 bits, incluidos los de
/// concurso y los de fox.
/// </summary>
/// <remarks>
/// <para>
/// La gramatica es minuscula pero tiene trampas: <c>RR73</c> tiene la forma de un localizador
/// valido; los indicativos compuestos van entre angulos; en concurso el informe se sustituye por
/// un intercambio y el «recibido» es una <c>R</c> suelta delante; y el fox manda dos mensajes en
/// uno separados por punto y coma.
/// </para>
/// <para>
/// No lanza nunca: ante la duda devuelve <see cref="ClaseDeMensaje.Otro"/>.
/// </para>
/// </remarks>
public static partial class InterpreteDeMensajes
{
    /// <summary>Sufijos de CQ que no son un indicativo.</summary>
    private static readonly HashSet<string> SufijosDeCq = new(StringComparer.Ordinal)
    {
        "DX", "EU", "NA", "SA", "AS", "AF", "OC", "AN", "POTA", "SOTA", "IOTA", "TEST", "FD", "RU", "WW",
        "QRP", "JA", "VK", "US", "EA", "F", "W", "K", "VE", "PY", "LU",
    };

    /// <summary>Analiza un mensaje. No lanza.</summary>
    public static MensajeEstandar Analizar(string? texto)
    {
        try
        {
            return AnalizarInterno(texto ?? string.Empty);
        }
        catch (Exception)
        {
            return MensajeEstandar.Nada(texto ?? string.Empty);
        }
    }

    private static MensajeEstandar AnalizarInterno(string texto)
    {
        var limpio = texto.Trim();
        if (limpio.Length == 0) return MensajeEstandar.Nada(texto);

        // El fox mete dos mensajes en uno: «K1ABC RR73; W9XYZ <KH1/KH7Z> -08».
        var puntoYComa = limpio.IndexOf(';', StringComparison.Ordinal);
        if (puntoYComa > 0)
        {
            var primero = AnalizarInterno(limpio[..puntoYComa]);
            var segundo = AnalizarInterno(limpio[(puntoYComa + 1)..]);
            return primero with { Texto = texto, Segundo = segundo };
        }

        var partes = limpio.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (partes.Length == 0) return MensajeEstandar.Nada(texto);

        // «CQ» y sus variantes: «CQ DX EA8DLF IL18», «CQ POTA K1ABC FN42», «CQ 135 …».
        if (partes[0] is "CQ" or "QRZ" or "DE")
        {
            var i = 1;
            var dirigido = string.Empty;
            if (partes.Length > 2 && (SufijosDeCq.Contains(partes[1]) || EsNumeroCorto(partes[1])))
            {
                dirigido = partes[1];
                i = 2;
            }

            if (partes.Length <= i) return MensajeEstandar.Nada(texto);

            var quien = SinAngulos(partes[i]);
            var grid = partes.Length > i + 1 && EsLocalizador(partes[i + 1]) ? partes[i + 1] : string.Empty;

            return new MensajeEstandar
            {
                Texto = texto,
                Llamante = quien,
                CqDirigido = dirigido,
                Locator = grid,
                Clase = ClaseDeMensaje.Cq,
            };
        }

        if (partes.Length < 2) return MensajeEstandar.Nada(texto);

        // La primera mitad del mensaje de un fox: «K1ABC RR73», sin el indicativo del fox.
        if (partes.Length == 2 && partes[1] is "RR73" or "RRR" or "73" && PareceIndicativo(SinAngulos(partes[0])))
        {
            return new MensajeEstandar
            {
                Texto = texto,
                Llamado = SinAngulos(partes[0]),
                Clase = partes[1] switch { "RR73" => ClaseDeMensaje.Rr73, "RRR" => ClaseDeMensaje.Rrr, _ => ClaseDeMensaje.S73 },
            };
        }

        var llamado = SinAngulos(partes[0]);
        var llamante = SinAngulos(partes[1]);
        if (!PareceIndicativo(llamado) || !PareceIndicativo(llamante)) return MensajeEstandar.Nada(texto);

        var resto = partes.AsSpan(2).ToArray();
        var mensaje = new MensajeEstandar { Texto = texto, Llamado = llamado, Llamante = llamante };

        if (resto.Length == 0) return mensaje with { Clase = ClaseDeMensaje.Llamada };

        switch (resto[0])
        {
            case "RR73": return mensaje with { Clase = ClaseDeMensaje.Rr73 };
            case "RRR": return mensaje with { Clase = ClaseDeMensaje.Rrr };
            case "73": return mensaje with { Clase = ClaseDeMensaje.S73 };
        }

        var conR = false;
        if (resto[0] == "R" && resto.Length > 1)
        {
            conR = true;
            resto = resto[1..];
        }

        // Informe: «-07», «+05», «R-07».
        var informe = InformeRegex().Match(resto[0]);
        if (resto.Length == 1 && informe.Success)
        {
            var db = int.Parse(informe.Groups["db"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var esR = conR || informe.Groups["r"].Success;
            return mensaje with
            {
                Informe = db,
                Clase = esR ? ClaseDeMensaje.InformeConR : ClaseDeMensaje.Informe,
            };
        }

        // Solo un localizador: «EA8DLF IZ2ABC JN45» (llamada) o «… R JN45» (concurso VHF).
        if (resto.Length == 1 && EsLocalizador(resto[0]))
        {
            return mensaje with
            {
                Locator = resto[0],
                Clase = conR ? ClaseDeMensaje.InformeConR : ClaseDeMensaje.Llamada,
            };
        }

        // Lo demas es intercambio de concurso: «2A EMA», «579 MA», «570123 IO91NP».
        var intercambio = string.Join(' ', resto);
        var localizadorDelIntercambio = resto.FirstOrDefault(EsLocalizador) ?? string.Empty;
        return mensaje with
        {
            Intercambio = intercambio,
            Locator = localizadorDelIntercambio,
            Clase = conR ? ClaseDeMensaje.InformeConR : ClaseDeMensaje.Informe,
        };
    }

    /// <summary>Un localizador Maidenhead de 4 o 6 caracteres. <c>RR73</c> no lo es.</summary>
    public static bool EsLocalizador(string palabra) =>
        palabra != "RR73" && LocalizadorRegex().IsMatch(palabra);

    private static bool EsNumeroCorto(string palabra) => palabra.Length is >= 1 and <= 3 && palabra.All(char.IsAsciiDigit);

    private static bool PareceIndicativo(string palabra) =>
        palabra.Length >= 3 && palabra.Any(char.IsAsciiDigit) && palabra.Any(char.IsAsciiLetter)
        && palabra.All(c => char.IsAsciiLetterOrDigit(c) || c == '/');

    private static string SinAngulos(string palabra) => palabra.Trim('<', '>');

    [GeneratedRegex(@"^(?<r>R)?(?<db>[+-]\d\d)$")]
    private static partial Regex InformeRegex();

    [GeneratedRegex(@"^[A-R]{2}[0-9]{2}([A-X]{2})?$")]
    private static partial Regex LocalizadorRegex();
}
