using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Diplomas.Calculo;

/// <summary>
/// Reparte los modos ADIF en las familias de las que hablan los reglamentos: telegrafia,
/// fonia y digitales.
/// </summary>
/// <remarks>
/// Los reglamentos no nombran modos ADIF sueltos sino familias, y por eso una variante de fonia
/// tiene que aceptar SSB, AM y FM, no solo uno de los tres. La clasificacion se hace sobre el
/// <b>modo principal</b> de ADIF: el submodo no cambia la familia. Los modos de imagen (SSTV,
/// ATV, FAX) caen del lado digital, que es donde los pone tambien el programa original, porque
/// los catalogos de diplomas solo distinguen tres familias.
/// </remarks>
public static class ClasesDeModo
{
    /// <summary>Modos principales de ADIF que cuentan como telegrafia.</summary>
    public static IReadOnlyList<string> Telegrafia { get; } = ["CW"];

    /// <summary>Modos principales de ADIF que cuentan como fonia.</summary>
    public static IReadOnlyList<string> Fonia { get; } = ["AM", "FM", "SSB", "DIGITALVOICE", "VOI"];

    /// <summary>Familia a la que pertenece un modo principal de ADIF.</summary>
    /// <param name="modoPrincipal">Modo principal, por ejemplo <c>SSB</c>.</param>
    /// <returns>La familia del modo.</returns>
    public static ClaseDeModo De(string? modoPrincipal)
    {
        if (string.IsNullOrWhiteSpace(modoPrincipal)) return ClaseDeModo.Digital;
        foreach (var m in Telegrafia)
        {
            if (m.Equals(modoPrincipal, StringComparison.OrdinalIgnoreCase)) return ClaseDeModo.Telegrafia;
        }
        foreach (var m in Fonia)
        {
            if (m.Equals(modoPrincipal, StringComparison.OrdinalIgnoreCase)) return ClaseDeModo.Fonia;
        }
        return ClaseDeModo.Digital;
    }

    /// <summary>Condicion SQL que deja pasar solo los contactos de una familia.</summary>
    /// <param name="clase">Familia de modos.</param>
    /// <returns>La condicion sobre <c>q.mode</c>, o nulo si la familia no filtra nada.</returns>
    public static string? Condicion(ClaseDeModo clase)
    {
        switch (clase)
        {
            case ClaseDeModo.Telegrafia:
                return $"q.mode IN ({ReglasDeVariante.Lista(Telegrafia)})";
            case ClaseDeModo.Fonia:
                return $"q.mode IN ({ReglasDeVariante.Lista(Fonia)})";
            case ClaseDeModo.Digital:
                var noDigitales = new List<string>(Telegrafia);
                noDigitales.AddRange(Fonia);
                return $"q.mode NOT IN ({ReglasDeVariante.Lista(noDigitales)})";
            default:
                return null;
        }
    }
}
