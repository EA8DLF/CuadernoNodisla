using System.IO;
using System.Text.Json;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>
/// El aviso de versiones nuevas: si se comprueba al arrancar, cuando se comprobo por ultima vez
/// y que version decidio el operador no instalar.
/// </summary>
/// <remarks>
/// Va en su propio fichero (<c>actualizaciones.json</c>) y no dentro de <c>ajustes.json</c>: la
/// fecha de la ultima comprobacion cambia cada dia, y reescribir la configuracion del equipo y
/// del cluster cada dia para apuntar una fecha es buscarse un fichero a medias el dia que se
/// vaya la luz.
/// </remarks>
public sealed class AjustesDeActualizaciones
{
    /// <summary>Nombre del fichero dentro de la carpeta de datos.</summary>
    public const string NombreDelFichero = "actualizaciones.json";

    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = true };

    /// <summary>Buscar versiones nuevas al arrancar (como mucho una vez al dia).</summary>
    public bool ComprobarAlArrancar { get; set; } = true;

    /// <summary>Ultima comprobacion automatica, en UTC.</summary>
    public DateTimeOffset? UltimaComprobacion { get; set; }

    /// <summary>Version cuyo aviso cerro el operador; no se vuelve a avisar de ella.</summary>
    public string? VersionDescartada { get; set; }

    /// <summary>Lee los ajustes, o los de fabrica si no hay o no se entienden.</summary>
    /// <param name="carpeta">Carpeta de datos.</param>
    public static AjustesDeActualizaciones Leer(string carpeta)
    {
        try
        {
            var ruta = Path.Combine(carpeta, NombreDelFichero);
            if (!File.Exists(ruta)) return new AjustesDeActualizaciones();
            return JsonSerializer.Deserialize<AjustesDeActualizaciones>(File.ReadAllText(ruta), Formato)
                ?? new AjustesDeActualizaciones();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "No se han podido leer los ajustes de actualizaciones; se usan los de fábrica.");
            return new AjustesDeActualizaciones();
        }
    }

    /// <summary>Guarda los ajustes (fichero temporal y reemplazo, como <see cref="AjustesDelPrograma"/>).</summary>
    /// <param name="carpeta">Carpeta de datos.</param>
    public void Guardar(string carpeta)
    {
        try
        {
            Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, NombreDelFichero);
            var temporal = ruta + ".nuevo";
            File.WriteAllText(temporal, JsonSerializer.Serialize(this, Formato));
            if (File.Exists(ruta)) File.Replace(temporal, ruta, null);
            else File.Move(temporal, ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "No se han podido guardar los ajustes de actualizaciones.");
        }
    }
}
