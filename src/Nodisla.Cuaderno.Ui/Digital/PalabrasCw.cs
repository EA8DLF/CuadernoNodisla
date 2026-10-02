using System.Text.RegularExpressions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Qué es una palabra del texto de telegrafía, para pintarla.</summary>
public enum TipoDePalabraCw
{
    /// <summary>Texto normal.</summary>
    Normal,

    /// <summary>Un indicativo: se puede pasar al contacto nuevo con un clic.</summary>
    Indicativo,

    /// <summary>El indicativo de la propia estación.</summary>
    Propio,

    /// <summary>«CQ» (y «QRZ?»): alguien está llamando.</summary>
    Llamada,

    /// <summary>Un prosigno («&lt;AR&gt;», «&lt;SK&gt;»...).</summary>
    Prosigno,

    /// <summary>Una abreviatura o código Q del glosario (con su significado).</summary>
    Abreviatura,
}

/// <summary>
/// Reconoce indicativos y llamadas en el texto que sale del decodificador de telegrafía.
/// </summary>
/// <remarks>
/// <para>
/// La forma del indicativo es la de la UIT: prefijo de una o dos letras (o letra y cifra, o cifra
/// y letra), una cifra y un sufijo de una a cuatro letras, con un prefijo o un sufijo de portable
/// separados por barra («EA8/DL1ABC», «EA8DLF/P»). Además tiene que pasar la regla del cuaderno
/// (<see cref="Indicativo.TryParse"/>). Así «5NN», «599», «FT710» o «RST» no salen como indicativo.
/// </para>
/// <para>
/// Las abreviaturas de telegrafía que encajan en la forma (pocas: ninguna de las corrientes
/// lleva cifra en medio) se descartan con una lista corta.
/// </para>
/// </remarks>
public static partial class PalabrasCw
{
    private static readonly HashSet<string> NoSonIndicativos = new(StringComparer.Ordinal)
    {
        "73", "88", "5NN", "599", "559", "579", "589", "TU", "R", "K",
    };

    /// <summary>Qué es una palabra.</summary>
    /// <param name="palabra">Palabra tal como sale (mayúsculas).</param>
    /// <param name="miIndicativo">Indicativo de la estación, o vacío.</param>
    public static TipoDePalabraCw Clasificar(string palabra, string? miIndicativo)
    {
        ArgumentNullException.ThrowIfNull(palabra);
        if (palabra.Length == 0) return TipoDePalabraCw.Normal;
        if (palabra.StartsWith('<') && palabra.EndsWith('>')) return TipoDePalabraCw.Prosigno;
        if (palabra is "CQ" or "QRZ?" or "QRZ") return TipoDePalabraCw.Llamada;
        if (!EsIndicativo(palabra)) return TipoDePalabraCw.Normal;

        if (!string.IsNullOrEmpty(miIndicativo))
        {
            var propio = Indicativo.Normalizar(miIndicativo);
            var limpio = Indicativo.Normalizar(palabra);
            if (limpio == propio || limpio.Split('/').Contains(propio, StringComparer.Ordinal))
                return TipoDePalabraCw.Propio;
        }

        return TipoDePalabraCw.Indicativo;
    }

    /// <summary>La palabra tiene forma de indicativo y la acepta el cuaderno.</summary>
    public static bool EsIndicativo(string palabra)
    {
        ArgumentNullException.ThrowIfNull(palabra);
        if (NoSonIndicativos.Contains(palabra)) return false;
        return FormaUit().IsMatch(palabra) && Indicativo.TryParse(palabra, out _);
    }

    /// <summary>Parte una línea en palabras, con cada prosigno como palabra propia.</summary>
    public static IEnumerable<string> Partir(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        foreach (var trozo in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var resto = trozo;
            while (resto.Length > 0)
            {
                var abre = resto.IndexOf('<', StringComparison.Ordinal);
                var cierra = abre >= 0 ? resto.IndexOf('>', abre) : -1;
                if (abre < 0 || cierra < 0)
                {
                    yield return resto;
                    break;
                }

                if (abre > 0) yield return resto[..abre];
                yield return resto[abre..(cierra + 1)];
                resto = resto[(cierra + 1)..];
            }
        }
    }

    [GeneratedRegex(@"^(?:[A-Z0-9]{1,4}/)?(?:[A-Z]{1,2}|[A-Z][0-9]|[0-9][A-Z])[0-9]{1,2}[A-Z]{1,4}(?:/[A-Z0-9]{1,4})?$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaUit();
}
