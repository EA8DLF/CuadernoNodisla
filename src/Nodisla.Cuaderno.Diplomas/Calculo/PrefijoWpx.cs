using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Diplomas.Calculo;

/// <summary>
/// Calcula el prefijo WPX de un indicativo.
/// </summary>
/// <remarks>
/// <para>
/// Solo se usa para el aviso instantaneo: cuando el contacto ya esta en el cuaderno, el prefijo
/// bueno es el del campo <c>PFX</c> del propio contacto, que es lo que consulta el calculo del
/// progreso. Aqui hace falta adivinarlo a partir de lo que el operador acaba de teclear o de lo
/// que acaba de anunciar el cluster, cuando todavia no hay contacto.
/// </para>
/// <para>
/// Se aplican las reglas del reglamento de CQ: se descartan los sufijos de operacion, un sufijo
/// de una sola cifra sustituye a la ultima cifra del prefijo, y si el prefijo se queda sin
/// cifras se le anade un cero. Los casos raros se resuelven a la baja: si no se puede decidir,
/// se devuelve vacio y el aviso no dice nada, que es mejor que decir algo falso.
/// </para>
/// </remarks>
public static class PrefijoWpx
{
    /// <summary>Calcula el prefijo WPX de un indicativo.</summary>
    /// <param name="indicativo">Indicativo a analizar.</param>
    /// <returns>El prefijo, o cadena vacia si no se puede decidir.</returns>
    public static string De(Indicativo indicativo)
    {
        if (indicativo.EsVacio) return string.Empty;

        var partes = indicativo.Valor
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !Indicativo.EsSufijoDeOperacion(p))
            .ToList();

        if (partes.Count == 0) return string.Empty;
        if (partes.Count == 1) return Normalizar(partes[0]);

        // Sufijo de una sola cifra: cambia la cifra del prefijo (W1AW/2 -> W2).
        var cifra = partes.FirstOrDefault(p => p.Length == 1 && char.IsDigit(p[0]));
        if (cifra is not null)
        {
            var resto = partes.FirstOrDefault(p => p != cifra);
            if (resto is null) return string.Empty;
            var baseDelPrefijo = Normalizar(resto);
            if (baseDelPrefijo.Length == 0) return string.Empty;
            return baseDelPrefijo[..^1] + cifra;
        }

        // Prefijo ajeno delante o detras (EA8/DL1ABC): manda el que no sea un indicativo entero.
        var prefijos = partes.Where(p => !EsIndicativoEntero(p)).ToList();
        if (prefijos.Count == 1) return Normalizar(prefijos[0]);

        // Dos partes que podrian ser prefijo: se queda la mas corta, que es lo que hace el
        // reglamento con los indicativos portables.
        var corta = partes.OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal).First();
        return Normalizar(corta);
    }

    private static string Normalizar(string parte)
    {
        if (parte.Length == 0) return string.Empty;

        var ultimaCifra = -1;
        for (var i = 0; i < parte.Length; i++)
        {
            if (char.IsDigit(parte[i])) ultimaCifra = i;
        }

        // Sin ninguna cifra el reglamento manda anadir un cero: F/DL1ABC cuenta como F0.
        return ultimaCifra < 0 ? parte + "0" : parte[..(ultimaCifra + 1)];
    }

    /// <summary>Un indicativo entero tiene al menos una letra detras de la ultima cifra.</summary>
    private static bool EsIndicativoEntero(string parte)
    {
        var ultimaCifra = -1;
        for (var i = 0; i < parte.Length; i++)
        {
            if (char.IsDigit(parte[i])) ultimaCifra = i;
        }
        return ultimaCifra >= 0 && ultimaCifra < parte.Length - 1;
    }
}
