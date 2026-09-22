using System.IO;
using System.Text.Json;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>
/// Los diplomas que el operador sigue, recordados de una sesion a la siguiente.
/// </summary>
/// <remarks>
/// <para>
/// Vive aqui, en la interfaz, y no en el motor de diplomas, porque el puerto
/// <c>IDiplomas</c> <b>no tiene manera de cambiar la seleccion</b>: hoy es una opcion con la
/// que se construye el motor. La pantalla guarda la lista y pide el progreso de cada uno con
/// <c>ProgresoAsync</c>, que si esta en el puerto. Si mas adelante el puerto gana un
/// <c>FijarMisDiplomasAsync</c>, esta clase pasa a alimentarlo y la pantalla no cambia.
/// </para>
/// <para>
/// Se guarda aparte del resto de ajustes de paneles porque es una lista que crece: mezclarla
/// con los interruptores de la ventana obligaria a versionar aquel fichero cada vez que
/// alguien sigue un diploma mas.
/// </para>
/// </remarks>
public sealed class DiplomasElegidos
{
    /// <summary>Nombre del fichero donde se guarda.</summary>
    public const string Fichero = "diplomas-elegidos.json";

    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = true };

    private readonly HashSet<string> _claves = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Claves elegidas, en la forma <c>CODIGO/VARIANTE</c>.</summary>
    public IReadOnlyCollection<string> Claves => _claves;

    /// <summary>Esa variante esta elegida.</summary>
    /// <param name="clave">Clave <c>CODIGO/VARIANTE</c>.</param>
    /// <returns>Cierto si el operador la sigue.</returns>
    public bool Contiene(string clave) => _claves.Contains(clave);

    /// <summary>Sustituye la seleccion entera.</summary>
    /// <param name="claves">Claves que quedan elegidas.</param>
    public void Poner(IEnumerable<string> claves)
    {
        ArgumentNullException.ThrowIfNull(claves);

        _claves.Clear();
        foreach (var clave in claves) _claves.Add(clave);
    }

    /// <summary>Lee la seleccion guardada. Si no hay fichero, no hay nada elegido.</summary>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    /// <returns>La seleccion.</returns>
    public static DiplomasElegidos Leer(string carpeta)
    {
        var elegidos = new DiplomasElegidos();

        try
        {
            var ruta = Path.Combine(carpeta, Fichero);
            if (!File.Exists(ruta)) return elegidos;

            var claves = JsonSerializer.Deserialize<string[]>(File.ReadAllText(ruta));
            if (claves is not null) elegidos.Poner(claves);
        }
        catch (Exception ex)
        {
            // Un fichero de ajustes roto no puede impedir abrir el cuaderno: se arranca sin
            // ninguno elegido, que es el estado de partida.
            Log.Warning(ex, "No se ha podido leer la selección de diplomas.");
        }

        return elegidos;
    }

    /// <summary>Guarda la seleccion.</summary>
    /// <param name="carpeta">Carpeta de datos del programa.</param>
    public void Escribir(string carpeta)
    {
        try
        {
            Directory.CreateDirectory(carpeta);
            File.WriteAllText(
                Path.Combine(carpeta, Fichero),
                JsonSerializer.Serialize(_claves.ToArray(), Formato));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido guardar la selección de diplomas.");
        }
    }
}
