using Microsoft.Data.Sqlite;

namespace Nodisla.Cuaderno.Datos;

/// <summary>Donde vive el cuaderno y como se protege.</summary>
public sealed class OpcionesCuaderno
{
    /// <summary>Nombre de la carpeta del programa dentro de los datos del usuario.</summary>
    public const string CarpetaDelPrograma = "CuadernoNodisla";

    /// <summary>Nombre del fichero de la base de datos.</summary>
    public const string NombreDelFichero = "cuaderno.sqlite";

    /// <summary>Ruta completa del fichero de la base de datos.</summary>
    public string Ruta { get; set; } = RutaPorOmision();

    /// <summary>
    /// Carpeta donde se dejan las copias de seguridad. Si no se indica, se usa una carpeta
    /// <c>copias</c> junto al fichero del cuaderno.
    /// </summary>
    public string? CarpetaDeCopias { get; set; }

    /// <summary>Cuantas copias de seguridad se conservan antes de ir borrando las mas viejas.</summary>
    public int CopiasAConservar { get; set; } = 10;

    /// <summary>Carpeta efectiva de las copias de seguridad.</summary>
    public string CarpetaDeCopiasEfectiva =>
        CarpetaDeCopias ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Ruta)) ?? ".", "copias");

    /// <summary>Cadena de conexion de SQLite para el fichero configurado.</summary>
    public string CadenaDeConexion => new SqliteConnectionStringBuilder
    {
        DataSource = Ruta,
        ForeignKeys = true,
    }.ToString();

    /// <summary>Ruta habitual del cuaderno: <c>%AppData%\CuadernoNodisla\cuaderno.sqlite</c>.</summary>
    /// <returns>La ruta completa del fichero.</returns>
    public static string RutaPorOmision() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        CarpetaDelPrograma,
        NombreDelFichero);
}
