namespace Nodisla.Cuaderno.Impresion.Modelo;

/// <summary>Tamano de pagina en milimetros.</summary>
/// <param name="AnchoMm">Ancho.</param>
/// <param name="AltoMm">Alto.</param>
public readonly record struct TamanoDePagina(double AnchoMm, double AltoMm)
{
    /// <summary>A4 vertical, 210 x 297 mm. Lo normal en Europa.</summary>
    public static TamanoDePagina A4 => new(210.0, 297.0);

    /// <summary>A4 apaisado.</summary>
    public static TamanoDePagina A4Apaisado => new(297.0, 210.0);

    /// <summary>Carta estadounidense, 215,9 x 279,4 mm. La usan las etiquetas Avery de EE. UU.</summary>
    public static TamanoDePagina Carta => new(215.9, 279.4);

    /// <summary>Carta apaisada.</summary>
    public static TamanoDePagina CartaApaisada => new(279.4, 215.9);
}
