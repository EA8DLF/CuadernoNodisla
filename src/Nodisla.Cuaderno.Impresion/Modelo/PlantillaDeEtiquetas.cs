namespace Nodisla.Cuaderno.Impresion.Modelo;

/// <summary>
/// Geometria de una hoja de etiquetas adhesivas: cuantas caben y donde esta cada una.
/// </summary>
/// <param name="Nombre">Como se conoce el papel, para elegirlo en pantalla.</param>
/// <param name="Referencia">Referencia del fabricante, la que va impresa en la caja.</param>
/// <param name="Pagina">Tamano de la hoja.</param>
/// <param name="MargenIzquierdoMm">Del borde izquierdo del papel al borde de la primera columna.</param>
/// <param name="MargenSuperiorMm">Del borde superior del papel al borde de la primera fila.</param>
/// <param name="AnchoMm">Ancho de una etiqueta.</param>
/// <param name="AltoMm">Alto de una etiqueta.</param>
/// <param name="SeparacionHorizontalMm">Hueco entre columnas.</param>
/// <param name="SeparacionVerticalMm">Hueco entre filas.</param>
/// <param name="Columnas">Numero de columnas.</param>
/// <param name="Filas">Numero de filas.</param>
/// <remarks>
/// <para>
/// <b>Las medidas son las del fabricante y no se retocan.</b> La tentacion de «ajustar un
/// milimetro para que quede bonito» es exactamente lo que hace que la tinta caiga en el borde
/// de la etiqueta y se quede pegada al rodillo de la impresora. Si algo sale corrido, lo que
/// esta mal es el escalado de la impresora, no la plantilla: hay que imprimir <b>al 100 %</b>,
/// sin «ajustar a la pagina».
/// </para>
/// <para>
/// El margen interior de cada etiqueta —el aire entre el borde del adhesivo y el texto— no va
/// aqui sino en el dibujo, porque depende de lo que se imprima y no del papel.
/// </para>
/// </remarks>
public sealed record PlantillaDeEtiquetas(
    string Nombre,
    string Referencia,
    TamanoDePagina Pagina,
    double MargenIzquierdoMm,
    double MargenSuperiorMm,
    double AnchoMm,
    double AltoMm,
    double SeparacionHorizontalMm,
    double SeparacionVerticalMm,
    int Columnas,
    int Filas)
{
    /// <summary>Cuantas etiquetas entran en una hoja.</summary>
    public int PorHoja => Columnas * Filas;

    /// <summary>Esquina superior izquierda de una etiqueta por su numero dentro de la hoja.</summary>
    /// <param name="indice">Numero de la etiqueta en la hoja, empezando en cero.</param>
    /// <returns>Distancias al borde izquierdo y al borde superior del papel, en milimetros.</returns>
    /// <remarks>
    /// Se rellena por filas, de izquierda a derecha y de arriba abajo, que es como salen las
    /// hojas de la impresora y como espera verlo quien va a despegarlas.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">El indice no cae en la hoja.</exception>
    public (double XMm, double YMm) Esquina(int indice)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(indice);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(indice, PorHoja);

        var fila = indice / Columnas;
        var columna = indice % Columnas;

        return (
            MargenIzquierdoMm + (columna * (AnchoMm + SeparacionHorizontalMm)),
            MargenSuperiorMm + (fila * (AltoMm + SeparacionVerticalMm)));
    }

    /// <summary>La plantilla cabe en su hoja de papel.</summary>
    /// <remarks>
    /// Se comprueba porque una plantilla a mano mal medida no falla al dibujar: dibuja fuera
    /// del papel y el error no aparece hasta que sale la hoja de la impresora.
    /// </remarks>
    public bool CabeEnElPapel
    {
        get
        {
            var ancho = MargenIzquierdoMm
                        + (Columnas * AnchoMm)
                        + ((Columnas - 1) * SeparacionHorizontalMm);
            var alto = MargenSuperiorMm
                       + (Filas * AltoMm)
                       + ((Filas - 1) * SeparacionVerticalMm);
            return ancho <= Pagina.AnchoMm + 0.01 && alto <= Pagina.AltoMm + 0.01;
        }
    }

    /// <summary>Avery L7160 en A4: 21 etiquetas de 63,5 x 38,1 mm. La de toda la vida para QSL.</summary>
    public static PlantillaDeEtiquetas AveryL7160 { get; } = new(
        "Avery 21 por hoja (63,5 × 38,1 mm)", "L7160 / 3652",
        TamanoDePagina.A4, 7.2, 15.1, 63.5, 38.1, 2.5, 0.0, 3, 7);

    /// <summary>Avery L7163 en A4: 14 etiquetas de 99,1 x 38,1 mm, mas anchas.</summary>
    public static PlantillaDeEtiquetas AveryL7163 { get; } = new(
        "Avery 14 por hoja (99,1 × 38,1 mm)", "L7163 / 3652",
        TamanoDePagina.A4, 4.65, 15.1, 99.1, 38.1, 2.5, 0.0, 2, 7);

    /// <summary>Avery L7165 en A4: 8 etiquetas grandes de 99,1 x 67,7 mm.</summary>
    /// <remarks>
    /// Es la que hay que usar cuando en una etiqueta van varios contactos con el mismo
    /// corresponsal: en la de 38 mm de alto caben tres lineas y se queda corta.
    /// </remarks>
    public static PlantillaDeEtiquetas AveryL7165 { get; } = new(
        "Avery 8 por hoja (99,1 × 67,7 mm)", "L7165 / 3427",
        TamanoDePagina.A4, 4.65, 13.1, 99.1, 67.7, 2.5, 0.0, 2, 4);

    /// <summary>Avery 5160 en papel carta: 30 etiquetas de 66,7 x 25,4 mm.</summary>
    /// <remarks>
    /// Papel estadounidense. Se incluye porque las hojas que vienen de fuera lo traen y porque
    /// el original lo ofrece; en Canarias lo normal es A4.
    /// </remarks>
    public static PlantillaDeEtiquetas Avery5160 { get; } = new(
        "Avery 30 por hoja, carta (66,7 × 25,4 mm)", "5160",
        TamanoDePagina.Carta, 4.8, 12.7, 66.7, 25.4, 3.2, 0.0, 3, 10);

    /// <summary>Las plantillas que trae el programa.</summary>
    public static IReadOnlyList<PlantillaDeEtiquetas> Conocidas { get; } =
        [AveryL7160, AveryL7163, AveryL7165, Avery5160];
}
