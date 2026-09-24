namespace Nodisla.Cuaderno.Diplomas;

/// <summary>Como se configura el motor de diplomas.</summary>
public sealed class OpcionesDeDiplomas
{
    /// <summary>Nombre del fichero donde se compila el catalogo de diplomas.</summary>
    public const string NombreDelCatalogo = "diplomas.sqlite";

    /// <summary>Nombre del fichero donde se guarda que diplomas sigue el operador.</summary>
    public const string NombreDeMisDiplomas = "mis-diplomas.txt";

    /// <summary>
    /// Ruta del catalogo compilado. Si no se indica, se deja junto al cuaderno. El fichero se
    /// puede borrar sin perder nada: se reconstruye desde el recurso incrustado.
    /// </summary>
    public string? RutaDelCatalogo { get; set; }

    /// <summary>
    /// Seleccion <b>de partida</b> de diplomas, en la forma <c>CODIGO/VARIANTE</c>.
    /// </summary>
    /// <remarks>
    /// Es el valor con el que arranca un cuaderno que todavia no tiene seleccion guardada, no
    /// la fuente de verdad: en cuanto el operador elige, manda lo guardado y esta lista deja de
    /// mirarse. Vacia significa que no se sigue ningun diploma, y entonces no se calcula nada.
    /// </remarks>
    public IList<string> MisDiplomas { get; } = [];

    /// <summary>
    /// Fichero donde se guarda la seleccion del operador. Si no se indica, se deja junto al
    /// <b>cuaderno</b>.
    /// </summary>
    /// <remarks>
    /// Junto al cuaderno y no junto al catalogo a proposito, por dos razones: el catalogo se
    /// borra y se reconstruye cuando cambia el recurso, y ademas puede compartirse entre varios
    /// cuadernos. Los diplomas que sigue el operador son de su cuaderno, no del catalogo.
    /// </remarks>
    public string? RutaDeMisDiplomas { get; set; }

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

    /// <summary>Ruta efectiva del fichero con la seleccion del operador.</summary>
    /// <param name="rutaDelCuaderno">Fichero del cuaderno, junto al que se deja por omision.</param>
    /// <returns>La ruta completa del fichero.</returns>
    public string RutaDeMisDiplomasEfectiva(string? rutaDelCuaderno)
    {
        if (!string.IsNullOrWhiteSpace(RutaDeMisDiplomas)) return Path.GetFullPath(RutaDeMisDiplomas);

        var carpeta = string.IsNullOrWhiteSpace(rutaDelCuaderno)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(rutaDelCuaderno)) ?? Directory.GetCurrentDirectory();
        return Path.Combine(carpeta, NombreDeMisDiplomas);
    }
}
