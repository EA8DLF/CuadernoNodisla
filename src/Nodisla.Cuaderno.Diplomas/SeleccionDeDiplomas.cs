using System.Text;

namespace Nodisla.Cuaderno.Diplomas;

/// <summary>
/// Guarda y lee que diplomas sigue el operador.
/// </summary>
/// <remarks>
/// <para>
/// Un fichero de texto, una clave <c>CODIGO/VARIANTE</c> por linea. No va dentro del catalogo
/// compilado a proposito: ese fichero se borra y se rehace cada vez que cambia el recurso de
/// diplomas, y la eleccion del operador no puede perderse por una actualizacion del catalogo.
/// En texto, ademas, se puede mirar y arreglar a mano si algo se tuerce.
/// </para>
/// <para>
/// Se escribe entero a un fichero temporal y luego se mueve encima, para que un corte de luz a
/// mitad no deje una seleccion a medias.
/// </para>
/// </remarks>
public static class SeleccionDeDiplomas
{
    private const string Cabecera =
        "# Diplomas que sigue el operador, uno por linea, en la forma CODIGO/VARIANTE.\n" +
        "# Lo escribe el motor de diplomas; se puede editar a mano con el programa cerrado.\n";

    /// <summary>Lee la seleccion guardada.</summary>
    /// <param name="ruta">Fichero de la seleccion.</param>
    /// <returns>Las claves guardadas, o nulo si nunca se ha guardado ninguna.</returns>
    public static IReadOnlyList<string>? Leer(string ruta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        if (!File.Exists(ruta)) return null;

        var claves = new List<string>();
        foreach (var linea in File.ReadLines(ruta, Encoding.UTF8))
        {
            var texto = linea.Trim();
            if (texto.Length == 0 || texto[0] == '#') continue;
            if (!claves.Contains(texto, StringComparer.OrdinalIgnoreCase)) claves.Add(texto);
        }
        return claves;
    }

    /// <summary>Guarda la seleccion.</summary>
    /// <param name="ruta">Fichero de la seleccion.</param>
    /// <param name="claves">Claves elegidas; lista vacia significa que no sigue ninguno.</param>
    public static void Escribir(string ruta, IReadOnlyList<string> claves)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);
        ArgumentNullException.ThrowIfNull(claves);

        var carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));
        if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

        var texto = new StringBuilder(Cabecera);
        foreach (var clave in claves) texto.Append(clave).Append('\n');

        // Una seleccion vacia tambien se guarda: «ninguno» es una eleccion del operador y no
        // puede confundirse con «todavia no ha elegido», que es lo que dice que no haya fichero.
        var temporal = ruta + ".nuevo";
        File.WriteAllText(temporal, texto.ToString(), Encoding.UTF8);
        File.Move(temporal, ruta, overwrite: true);
    }
}
