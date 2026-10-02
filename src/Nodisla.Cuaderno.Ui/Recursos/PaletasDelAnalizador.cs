namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>Los colores que puede llevar la cascada del analizador del equipo.</summary>
public enum PaletaDelAnalizador
{
    /// <summary>La de la pantalla del FT-710: negro granate, rojo, naranja y amarillo blanquecino.</summary>
    Radio,

    /// <summary>La de casa: negro, azul, verde, ámbar y blanco.</summary>
    Nodisla,

    /// <summary>Negro, azul, cian, verde, amarillo y rojo: la clásica de los SDR.</summary>
    Arcoiris,

    /// <summary>Negro, rojo, amarillo y blanco.</summary>
    Fuego,

    /// <summary>Negro, azul marino, cian y blanco.</summary>
    Hielo,

    /// <summary>Escala de grises.</summary>
    Gris,
}

/// <summary>
/// Las paletas de la cascada del analizador, ya hechas tabla: 256 colores Bgra32 cada una.
/// </summary>
/// <remarks>
/// Se calculan una vez y se reparten: pintar una fila es mirar 850 veces la tabla, sin cuentas.
/// Los tramos son propios (la idea de ofrecer varias es de Thetis, <c>ucGradientDefault.cs</c>,
/// pero no se ha copiado ningún dato de allí).
/// </remarks>
public static class PaletasDelAnalizador
{
    private static readonly int[][] Tablas = Enum.GetValues<PaletaDelAnalizador>()
        .Select(p => Crear(Tramos(p)))
        .ToArray();

    /// <summary>La tabla de 256 colores de una paleta.</summary>
    /// <param name="paleta">La paleta.</param>
    /// <returns>Colores Bgra32, del 0 (ruido) al 255 (lo más fuerte). No se debe modificar.</returns>
    public static int[] Tabla(PaletaDelAnalizador paleta)
    {
        var i = (int)paleta;
        return i >= 0 && i < Tablas.Length ? Tablas[i] : Tablas[0];
    }

    /// <summary>Lee el nombre guardado en los ajustes; si no se reconoce, la de la radio.</summary>
    /// <param name="nombre">Nombre guardado.</param>
    /// <returns>La paleta.</returns>
    public static PaletaDelAnalizador DesdeNombre(string? nombre) =>
        Enum.TryParse<PaletaDelAnalizador>(nombre, ignoreCase: true, out var p) && Enum.IsDefined(p) ? p : PaletaDelAnalizador.Radio;

    private static (double P, int R, int G, int B)[] Tramos(PaletaDelAnalizador paleta) => paleta switch
    {
        PaletaDelAnalizador.Nodisla =>
        [
            (0.00, 0x02, 0x04, 0x0C), (0.30, 0x08, 0x2A, 0x6E), (0.55, 0x14, 0x9C, 0x62),
            (0.80, 0xF2, 0xB0, 0x2E), (1.00, 0xFF, 0xFF, 0xF0),
        ],
        PaletaDelAnalizador.Arcoiris =>
        [
            (0.00, 0x00, 0x00, 0x08), (0.22, 0x00, 0x10, 0x9A), (0.42, 0x00, 0xB4, 0xE6),
            (0.60, 0x20, 0xD0, 0x40), (0.78, 0xF8, 0xE8, 0x10), (0.92, 0xFF, 0x50, 0x10), (1.00, 0xFF, 0xFF, 0xFF),
        ],
        PaletaDelAnalizador.Fuego =>
        [
            (0.00, 0x00, 0x00, 0x00), (0.40, 0xA0, 0x10, 0x00), (0.75, 0xFF, 0xC8, 0x00), (1.00, 0xFF, 0xFF, 0xFF),
        ],
        PaletaDelAnalizador.Hielo =>
        [
            (0.00, 0x00, 0x02, 0x08), (0.40, 0x06, 0x30, 0x8C), (0.75, 0x30, 0xD8, 0xF0), (1.00, 0xFF, 0xFF, 0xFF),
        ],
        PaletaDelAnalizador.Gris =>
        [
            (0.00, 0x00, 0x00, 0x00), (1.00, 0xFF, 0xFF, 0xFF),
        ],

        // Negro granate → granate → rojo → naranja → amarillo blanquecino, como la radio.
        _ =>
        [
            (0.00, 0x10, 0x02, 0x02), (0.30, 0x4A, 0x0A, 0x08), (0.60, 0xC8, 0x1E, 0x16),
            (0.82, 0xFF, 0x78, 0x2E), (1.00, 0xFF, 0xF4, 0xC8),
        ],
    };

    private static int[] Crear((double P, int R, int G, int B)[] tramos)
    {
        var tabla = new int[256];
        for (var i = 0; i < 256; i++)
        {
            var t = i / 255.0;
            var k = 0;
            while (k < tramos.Length - 2 && t > tramos[k + 1].P) k++;
            var (p0, r0, g0, b0) = tramos[k];
            var (p1, r1, g1, b1) = tramos[k + 1];
            var u = Math.Clamp((t - p0) / (p1 - p0), 0, 1);
            var r = (int)(r0 + ((r1 - r0) * u));
            var g = (int)(g0 + ((g1 - g0) * u));
            var b = (int)(b0 + ((b1 - b0) * u));
            tabla[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }

        return tabla;
    }
}
