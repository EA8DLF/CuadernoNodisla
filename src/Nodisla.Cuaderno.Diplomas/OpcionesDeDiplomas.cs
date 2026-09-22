namespace Nodisla.Cuaderno.Diplomas;

/// <summary>Como se configura el motor de diplomas.</summary>
public sealed class OpcionesDeDiplomas
{
    /// <summary>Nombre del fichero donde se compila el catalogo de diplomas.</summary>
    public const string NombreDelCatalogo = "diplomas.sqlite";

    /// <summary>
    /// Ruta del catalogo compilado. Si no se indica, se deja junto al cuaderno. El fichero se
    /// puede borrar sin perder nada: se reconstruye desde el recurso incrustado.
    /// </summary>
    public string? RutaDelCatalogo { get; set; }

    /// <summary>
    /// Diplomas que el operador tiene marcados como propios, en la forma
    /// <c>CODIGO/VARIANTE</c>. Si esta vacia, se usa una seleccion de partida con la variante
    /// menos restrictiva de cada diploma que se pueda resolver con solo el indicativo.
    /// </summary>
    public IList<string> MisDiplomas { get; } = [];

    /// <summary>
    /// Diplomas que se compilan en el catalogo. Vacio significa todos. Reducirlo acorta mucho
    /// la construccion, que es lo que hacen las pruebas.
    /// </summary>
    public HashSet<string> DiplomasIncluidos { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Cada cuanto se vuelve a mirar si el cuaderno ha cambiado por detras. Es la red de
    /// seguridad de la cache: lo normal es que la aplicacion avise al anadir un contacto o al
    /// bajar confirmaciones, pero si el cambio viene de otro proceso nadie avisa.
    /// </summary>
    public TimeSpan IntervaloDeComprobacion { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Ruta efectiva del catalogo compilado.</summary>
    /// <param name="rutaDelCuaderno">Fichero del cuaderno, junto al que se deja por omision.</param>
    /// <returns>La ruta completa del fichero.</returns>
    public string RutaDelCatalogoEfectiva(string? rutaDelCuaderno)
    {
        if (!string.IsNullOrWhiteSpace(RutaDelCatalogo)) return Path.GetFullPath(RutaDelCatalogo);

        var carpeta = string.IsNullOrWhiteSpace(rutaDelCuaderno)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(rutaDelCuaderno)) ?? Directory.GetCurrentDirectory();
        return Path.Combine(carpeta, NombreDelCatalogo);
    }
}
