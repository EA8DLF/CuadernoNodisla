using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>Lo que se ha podido sacar en claro de un mensaje decodificado.</summary>
/// <param name="Llamante">Quien transmite, cuando el mensaje permite saberlo.</param>
/// <param name="Llamado">A quien se dirige. <c>CQ</c> no cuenta como indicativo.</param>
/// <param name="Locator">Localizador que viaja en el mensaje.</param>
/// <param name="EsCq">El mensaje es una llamada general.</param>
public readonly record struct AnalisisDeMensaje(
    Indicativo Llamante,
    Indicativo Llamado,
    Locator Locator,
    bool EsCq)
{
    /// <summary>No se saco nada del mensaje.</summary>
    public static AnalisisDeMensaje Nada => default;

    /// <summary>Se reconocio al menos un indicativo.</summary>
    public bool HayAlgo => !Llamante.EsVacio || !Llamado.EsVacio || EsCq;
}

/// <summary>
/// Entiende los mensajes de FT8 y compania.
/// </summary>
/// <remarks>
/// El lenguaje de estos modos es minusculo —caben setenta y siete bits— pero tiene trampas:
/// la llamada general puede ir dirigida (<c>CQ DX</c>, <c>CQ POTA</c>, <c>CQ 135</c>), los
/// indicativos compuestos viajan entre angulos, los que no caben se sustituyen por
/// <c>&lt;...&gt;</c>, y <c>RR73</c> tiene exactamente la forma de un localizador valido.
/// Nada de lo que pase aqui puede tirar la decodificacion: el texto crudo se guarda siempre y
/// esto es solo lo que se ha conseguido deducir.
/// </remarks>
public static class AnalizadorMensajeDigital
{
    /// <summary>
    /// Palabras de cuatro o menos caracteres que aparecen donde iria un localizador y no lo
    /// son. <c>RR73</c> es la importante: pasa la validacion de Maidenhead sin despeinarse.
    /// </summary>
    private static readonly HashSet<string> NoSonLocalizador = new(StringComparer.Ordinal)
    {
        "RR73", "RRR", "R73", "73", "TU", "NIL", "TNX", "QSL", "AGN", "NO", "SRI",
        "DE", "CQ", "QRZ", "OOO", "RO", "RRRR",
    };

    /// <summary>Analiza el texto de una decodificacion. No lanza nunca.</summary>
    /// <param name="texto">Mensaje tal y como lo envio el programa.</param>
    public static AnalisisDeMensaje Analizar(string? texto)
    {
        try
        {
            return AnalizarInterno(texto);
        }
        catch (Exception)
        {
            // Que el analisis falle no puede costar la decodificacion.
            return AnalisisDeMensaje.Nada;
        }
    }

    private static AnalisisDeMensaje AnalizarInterno(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return AnalisisDeMensaje.Nada;

        var t = texto.Trim();

        // Los mensajes con indicativo no estandar llegan partidos por un punto y coma:
        // «K1ABC RR73; W9XYZ <PJ4/K1ABC> -11». Quien transmite esta en la segunda mitad.
        var puntoYComa = t.IndexOf(';');
        if (puntoYComa >= 0 && puntoYComa + 1 < t.Length) t = t[(puntoYComa + 1)..].Trim();

        var piezas = Trocear(t);
        if (piezas.Count == 0) return AnalisisDeMensaje.Nada;

        var esCq = false;
        var i = 0;

        if (piezas[0] is "CQ" or "QRZ")
        {
            esCq = true;
            i = 1;
            // Llamada dirigida: la palabra que sigue no es un indicativo y detras aun queda algo.
            if (i < piezas.Count && piezas.Count - i > 1 && !EsIndicativo(piezas[i], out _)) i++;
        }
        else if (piezas[0] == "DE" && piezas.Count > 1)
        {
            i = 1;
        }

        var llamante = Indicativo.Vacio;
        var llamado = Indicativo.Vacio;

        if (esCq)
        {
            if (i < piezas.Count && EsIndicativo(piezas[i], out var quien)) llamante = quien;
            i++;
        }
        else
        {
            if (i < piezas.Count && EsIndicativo(piezas[i], out var aQuien)) llamado = aQuien;
            i++;
            if (i < piezas.Count && EsIndicativo(piezas[i], out var quien)) llamante = quien;
            i++;
        }

        var locator = BuscarLocalizador(piezas, i);

        return llamante.EsVacio && llamado.EsVacio && !esCq
            ? AnalisisDeMensaje.Nada
            : new AnalisisDeMensaje(llamante, llamado, locator, esCq);
    }

    /// <summary>Parte el mensaje en palabras y les quita los angulos y los signos sueltos.</summary>
    private static List<string> Trocear(string texto)
    {
        var piezas = new List<string>(6);
        foreach (var bruta in texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var pieza = bruta.Trim('<', '>').ToUpperInvariant();
            if (pieza.Length == 0 || pieza == "?") continue;
            piezas.Add(pieza);
        }
        return piezas;
    }

    private static bool EsIndicativo(string pieza, out Indicativo indicativo)
    {
        indicativo = Indicativo.Vacio;
        if (pieza is "CQ" or "QRZ" or "DE") return false;
        return Indicativo.TryParse(pieza, out indicativo);
    }

    /// <summary>
    /// Busca el primer localizador de verdad a partir de la posicion dada. Se recorre lo que
    /// queda en vez de mirar solo la palabra siguiente porque entre medias puede haber una
    /// <c>R</c> suelta o un informe.
    /// </summary>
    private static Locator BuscarLocalizador(List<string> piezas, int desde)
    {
        for (var j = Math.Max(0, desde); j < piezas.Count; j++)
        {
            var pieza = piezas[j];
            if (pieza.Length is not (4 or 6)) continue;
            if (NoSonLocalizador.Contains(pieza)) continue;
            if (Locator.TryParse(pieza, out var locator)) return locator;
        }
        return Locator.Vacio;
    }
}
