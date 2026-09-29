using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Diplomas.Calculo;

/// <summary>Un patron de un diploma por indicativo y la referencia a la que pertenece.</summary>
/// <param name="Referencia">Codigo de la referencia.</param>
/// <param name="Patron">Patron con el que casa, con <c>*</c> y <c>?</c> como comodines.</param>
public sealed record PatronDeIndicativo(string Referencia, string Patron);

/// <summary>
/// Decide a que referencia de un diploma por indicativo corresponde un indicativo.
/// </summary>
/// <remarks>
/// Los diplomas por indicativo listan unas veces indicativos completos (<c>II0MMI</c>) y otras
/// patrones con asterisco (<c>3B8*</c>). Lo mismo que hace el <c>GLOB</c> de la consulta se
/// resuelve aqui en memoria, que es lo que permite que el aviso al teclear no toque la base.
/// </remarks>
public static class CoincidenciaDeIndicativo
{
    /// <summary>Busca la referencia que casa con el indicativo.</summary>
    /// <param name="patrones">Referencias del diploma.</param>
    /// <param name="indicativo">Indicativo a comprobar.</param>
    /// <returns>El codigo de la referencia, o nulo si ninguna casa.</returns>
    public static string? Buscar(IReadOnlyList<PatronDeIndicativo> patrones, Indicativo indicativo)
    {
        ArgumentNullException.ThrowIfNull(patrones);
        if (indicativo.EsVacio) return null;

        var valor = indicativo.Valor;
        foreach (var patron in patrones)
        {
            if (Casa(patron.Patron, valor)) return patron.Referencia;
        }
        return null;
    }

    /// <summary>Dice si un patron del catalogo casa con un indicativo.</summary>
    /// <param name="patron">Patron, con <c>*</c> y <c>?</c> como comodines.</param>
    /// <param name="indicativo">Indicativo normalizado.</param>
    /// <returns>Cierto si casa.</returns>
    public static bool Casa(string? patron, string indicativo)
    {
        if (string.IsNullOrEmpty(patron)) return false;
        if (patron.IndexOf('*', StringComparison.Ordinal) < 0 &&
            patron.IndexOf('?', StringComparison.Ordinal) < 0)
        {
            return string.Equals(patron, indicativo, StringComparison.OrdinalIgnoreCase);
        }

        return Comparar(patron, 0, indicativo, 0);
    }

    private static bool Comparar(string patron, int p, string texto, int t)
    {
        while (p < patron.Length)
        {
            var c = patron[p];
            if (c == '*')
            {
                // El asterisco final se come el resto, que es el caso normal de estos catalogos.
                if (p == patron.Length - 1) return true;
                for (var salto = t; salto <= texto.Length; salto++)
                {
                    if (Comparar(patron, p + 1, texto, salto)) return true;
                }
                return false;
            }

            if (t >= texto.Length) return false;
            if (c != '?' && char.ToUpperInvariant(c) != char.ToUpperInvariant(texto[t])) return false;
            p++;
            t++;
        }

        return t == texto.Length;
    }
}
