using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Impresion.Modelo;

/// <summary>Tamano y colocacion de las tarjetas de QSL en la hoja.</summary>
/// <param name="Nombre">Como se elige en pantalla.</param>
/// <param name="Pagina">Tamano de la hoja.</param>
/// <param name="AnchoMm">Ancho de la tarjeta.</param>
/// <param name="AltoMm">Alto de la tarjeta.</param>
/// <param name="Columnas">Tarjetas por fila.</param>
/// <param name="Filas">Filas por hoja.</param>
/// <param name="MargenIzquierdoMm">Margen izquierdo del papel.</param>
/// <param name="MargenSuperiorMm">Margen superior del papel.</param>
/// <param name="SeparacionHorizontalMm">Hueco entre columnas.</param>
/// <param name="SeparacionVerticalMm">Hueco entre filas.</param>
/// <param name="ConMarcasDeCorte">Dibujar las marcas de corte.</param>
public sealed record PlantillaDeTarjetas(
    string Nombre,
    TamanoDePagina Pagina,
    double AnchoMm,
    double AltoMm,
    int Columnas,
    int Filas,
    double MargenIzquierdoMm,
    double MargenSuperiorMm,
    double SeparacionHorizontalMm,
    double SeparacionVerticalMm,
    bool ConMarcasDeCorte)
{
    /// <summary>Cuantas tarjetas entran en una hoja.</summary>
    public int PorHoja => Columnas * Filas;

    /// <summary>Esquina superior izquierda de una tarjeta dentro de la hoja.</summary>
    /// <param name="indice">Numero de tarjeta en la hoja, empezando en cero.</param>
    /// <returns>Distancias al borde izquierdo y al superior, en milimetros.</returns>
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

    /// <summary>
    /// Tarjeta de QSL de tamano internacional, 140 x 90 mm: dos por hoja A4.
    /// </summary>
    /// <remarks>
    /// Es la medida que aceptan los buros y la que cabe en las cajas de archivo de todo el
    /// mundo. Salirse de ella es garantizar que la tarjeta acabe doblada.
    /// </remarks>
    public static PlantillaDeTarjetas Internacional { get; } = new(
        "QSL internacional 140 × 90 mm, 2 por hoja A4",
        TamanoDePagina.A4, 140.0, 90.0, 1, 2, 35.0, 40.0, 0.0, 30.0, ConMarcasDeCorte: true);

    /// <summary>Media tarjeta, 105 x 74 mm: cuatro por hoja A4, para tiradas grandes.</summary>
    public static PlantillaDeTarjetas Media { get; } = new(
        "QSL pequeña 105 × 74 mm, 4 por hoja A4",
        TamanoDePagina.A4, 105.0, 74.0, 2, 2, 0.0, 74.5, 0.0, 0.0, ConMarcasDeCorte: true);

    /// <summary>Las plantillas de tarjeta que trae el programa.</summary>
    public static IReadOnlyList<PlantillaDeTarjetas> Conocidas { get; } = [Internacional, Media];
}

/// <summary>
/// Una tarjeta de QSL completa: la cara con los datos de la estacion y los del contacto.
/// </summary>
/// <param name="MiIndicativo">Indicativo que se anuncia en grande.</param>
/// <param name="Etiqueta">Los contactos que confirma y a quien van.</param>
/// <param name="MiNombre">Nombre del operador.</param>
/// <param name="MiQth">Poblacion e isla.</param>
/// <param name="MiEquipo">Equipo y potencia, si se quiere poner.</param>
/// <param name="MiAntena">Antena, si se quiere poner.</param>
/// <param name="Cq">Zona CQ.</param>
/// <param name="Itu">Zona ITU.</param>
/// <param name="Dxcc">Entidad DXCC, escrita.</param>
/// <remarks>
/// La tarjeta es la misma informacion que la etiqueta mas la cabecera de la estacion. Se
/// modela aparte y no como una etiqueta grande porque lo que decide si una tarjeta vale es la
/// cabecera: sin indicativo, entidad y zonas, no sirve para ningun diploma.
/// </remarks>
public sealed record TarjetaDeQsl(
    Indicativo MiIndicativo,
    EtiquetaDeQsl Etiqueta,
    string? MiNombre,
    string? MiQth,
    string? MiEquipo,
    string? MiAntena,
    int? Cq,
    int? Itu,
    string? Dxcc);
