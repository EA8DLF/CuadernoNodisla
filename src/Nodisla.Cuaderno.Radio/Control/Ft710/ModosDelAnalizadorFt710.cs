namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>Un modo del analizador del FT-710 (orden <c>SS06</c>).</summary>
/// <param name="Nombre">Como lo rotula el equipo.</param>
/// <param name="EsTresD">Vista 3DSS; si no, cascada (W/F).</param>
/// <param name="Posicion">0 CENTER, 1 CURSOR, 2 FIX.</param>
/// <param name="Ampliado">Cascada ampliada (EXPAND). En 3DSS no existe.</param>
/// <param name="AlEscribir">Caracter que se manda en <c>SS06</c>.</param>
/// <param name="AlLeer">Caracter que contesta el equipo con ese modo puesto.</param>
public sealed record ModoDelAnalizadorFt710(
    string Nombre,
    bool EsTresD,
    int Posicion,
    bool Ampliado,
    char AlEscribir,
    char AlLeer);

/// <summary>
/// Los modos del analizador del FT-710 y como se escriben y se leen.
/// </summary>
/// <remarks>
/// <b>El equipo no contesta lo mismo que se le manda en los modos ampliados.</b> Comprobado en la
/// radio de Jose el 28-09-2026: mandar <c>SS0630000</c> (W/F CENTER EXPAND, segun el manual) se
/// lee de vuelta como <c>SS0650000</c>; <c>SS0660000</c> como <c>SS0680000</c> y
/// <c>SS0690000</c> como <c>SS06B0000</c>. Y mandar directamente 5, 8 o B lo rechaza con
/// <c>?;</c>. Los normales (4, 7, A) y los 3DSS (0, 1, 2) se leen igual que se escriben.
/// </remarks>
public static class ModosDelAnalizadorFt710
{
    /// <summary>Los nueve modos, en el orden del manual CAT.</summary>
    public static IReadOnlyList<ModoDelAnalizadorFt710> Todos { get; } =
    [
        new("3DSS CENTER", true, 0, false, '0', '0'),
        new("3DSS CURSOR", true, 1, false, '1', '1'),
        new("3DSS FIX", true, 2, false, '2', '2'),
        new("W/F CENTER EXPAND", false, 0, true, '3', '5'),
        new("W/F CENTER", false, 0, false, '4', '4'),
        new("W/F CURSOR EXPAND", false, 1, true, '6', '8'),
        new("W/F CURSOR", false, 1, false, '7', '7'),
        new("W/F FIX EXPAND", false, 2, true, '9', 'B'),
        new("W/F FIX", false, 2, false, 'A', 'A'),
    ];

    /// <summary>Nombres de los modos, por indice.</summary>
    /// <returns>Los nombres.</returns>
    public static IReadOnlyList<string> Etiquetas() => [.. Todos.Select(m => m.Nombre)];

    /// <summary>Indice del modo a partir de lo que contesta el equipo.</summary>
    /// <param name="leido">Caracter tras <c>SS06</c>.</param>
    /// <returns>El indice, o nulo si no se conoce.</returns>
    public static double? DesdeLoLeido(char leido)
    {
        var c = char.ToUpperInvariant(leido);
        for (var i = 0; i < Todos.Count; i++)
        {
            if (Todos[i].AlLeer == c)
            {
                return i;
            }
        }

        // Por si otro firmware contesta con el mismo caracter que se escribe.
        for (var i = 0; i < Todos.Count; i++)
        {
            if (Todos[i].AlEscribir == c)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>Indice del modo con esas tres caracteristicas.</summary>
    /// <param name="tresD">Vista 3DSS.</param>
    /// <param name="posicion">0 CENTER, 1 CURSOR, 2 FIX.</param>
    /// <param name="ampliado">Ampliado (solo en cascada).</param>
    /// <returns>El indice.</returns>
    public static int Indice(bool tresD, int posicion, bool ampliado)
    {
        for (var i = 0; i < Todos.Count; i++)
        {
            var m = Todos[i];
            if (m.EsTresD == tresD && m.Posicion == posicion && (tresD || m.Ampliado == ampliado))
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>La tecla CENTER: CENTER → CURSOR → FIX, sin cambiar de vista.</summary>
    /// <param name="indice">Modo actual.</param>
    /// <returns>Modo siguiente.</returns>
    public static int TrasCenter(int indice)
    {
        var m = Todos[Math.Clamp(indice, 0, Todos.Count - 1)];
        return Indice(m.EsTresD, (m.Posicion + 1) % 3, m.Ampliado);
    }

    /// <summary>La tecla 3DSS: pasa de 3DSS a cascada y al reves, en la misma posicion.</summary>
    /// <param name="indice">Modo actual.</param>
    /// <returns>Modo siguiente.</returns>
    public static int TrasTresD(int indice)
    {
        var m = Todos[Math.Clamp(indice, 0, Todos.Count - 1)];
        return Indice(!m.EsTresD, m.Posicion, ampliado: false);
    }

    /// <summary>La tecla EXPAND: amplia o no la cascada. En 3DSS no hay ampliado.</summary>
    /// <param name="indice">Modo actual.</param>
    /// <returns>Modo siguiente, o el mismo si esta en 3DSS.</returns>
    public static int TrasExpand(int indice)
    {
        var m = Todos[Math.Clamp(indice, 0, Todos.Count - 1)];
        return m.EsTresD ? indice : Indice(false, m.Posicion, !m.Ampliado);
    }
}
