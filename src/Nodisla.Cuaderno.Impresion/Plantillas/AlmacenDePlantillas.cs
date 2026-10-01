using System.IO.Compression;
using System.Text.Json;

namespace Nodisla.Cuaderno.Impresion.Plantillas;

/// <summary>
/// Lo que un almacen de plantillas necesita saber de una plantilla: como se llama y que imagenes
/// usa (fondo, logo, firma…).
/// </summary>
public interface IPlantillaConImagenes
{
    /// <summary>Identificador estable: el nombre de su JSON en disco.</summary>
    string Id { get; set; }

    /// <summary>Como se elige en pantalla.</summary>
    string Nombre { get; set; }

    /// <summary>Los nombres de fichero de las imagenes que usa, sin ruta.</summary>
    /// <returns>Los nombres; vacio si no usa ninguna.</returns>
    IEnumerable<string> Imagenes();

    /// <summary>Cambia una imagen por otra en todos los sitios donde se use.</summary>
    /// <param name="viejo">Nombre de fichero anterior.</param>
    /// <param name="nuevo">Nombre de fichero nuevo.</param>
    void RenombrarImagen(string viejo, string nuevo);
}

/// <summary>
/// Plantillas guardadas en disco como JSON, cada una con sus imagenes al lado. Es el motor comun
/// de las tarjetas QSL y de los diplomas.
/// </summary>
/// <typeparam name="T">El tipo de plantilla.</typeparam>
/// <remarks>
/// <para>
/// Cada plantilla es <c>&lt;id&gt;.json</c> y sus imagenes <c>&lt;id&gt;-&lt;uso&gt;-&lt;hora&gt;.&lt;ext&gt;</c>
/// en la misma carpeta. <b>Las imagenes se copian</b> al elegirlas: si se leyeran de donde las
/// eligio el operador, borrar o mover la foto dejaria la plantilla sin ella sin avisar.
/// </para>
/// <para>
/// Las plantillas de fabrica no se escriben en disco: se ofrecen mientras no se haya guardado otra
/// con su mismo identificador. El disco no se toca hasta que el operador guarda algo suyo.
/// </para>
/// <para>
/// Exportar e importar usan un ZIP con <c>plantilla.json</c> y las imagenes: se puede pasar una
/// plantilla a otro equipo o a otro operador con un solo fichero.
/// </para>
/// </remarks>
public abstract class AlmacenDePlantillas<T>
    where T : class, IPlantillaConImagenes
{
    private const string FicheroDeOmision = "omision.txt";
    private const string JsonDelPaquete = "plantilla.json";

    /// <summary>Extensiones de imagen que se aceptan.</summary>
    public static readonly IReadOnlyList<string> ExtensionesDeImagen = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];

    /// <summary>Opciones del JSON: indentado, para que se pueda leer y retocar a mano.</summary>
    protected static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    /// <summary>Crea el almacen sobre una carpeta.</summary>
    /// <param name="carpeta">Donde se guardan las plantillas.</param>
    protected AlmacenDePlantillas(string carpeta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carpeta);
        Carpeta = carpeta;
    }

    /// <summary>Donde se guardan.</summary>
    public string Carpeta { get; }

    /// <summary>Las plantillas que trae el programa, la primera es la de omision.</summary>
    /// <returns>Plantillas nuevas cada vez (se pueden modificar sin miedo).</returns>
    protected abstract IReadOnlyList<T> DeFabrica();

    /// <summary>Copia independiente de una plantilla, con el mismo identificador.</summary>
    /// <param name="plantilla">La plantilla.</param>
    /// <returns>La copia.</returns>
    protected abstract T Clonar(T plantilla);

    /// <summary>Arregla lo que un JSON viejo o escrito a mano pueda traer a nulo.</summary>
    /// <param name="plantilla">La plantilla recien leida.</param>
    protected virtual void Normalizar(T plantilla)
    {
    }

    /// <summary>Todas las plantillas: las guardadas y las de fabrica que no se han sobrescrito.</summary>
    /// <returns>Las plantillas, por nombre.</returns>
    public IReadOnlyList<T> Listar()
    {
        var plantillas = new List<T>();
        if (Directory.Exists(Carpeta))
        {
            foreach (var fichero in Directory.EnumerateFiles(Carpeta, "*.json"))
            {
                if (Leer(fichero) is { } plantilla)
                {
                    plantilla.Id = Path.GetFileNameWithoutExtension(fichero);
                    plantillas.Add(plantilla);
                }
            }
        }

        foreach (var deFabrica in DeFabrica())
        {
            if (!plantillas.Any(p => p.Id == deFabrica.Id)) plantillas.Add(deFabrica);
        }

        return plantillas.OrderBy(p => p.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>La plantilla de un identificador.</summary>
    /// <param name="id">El identificador.</param>
    /// <returns>La plantilla o nula.</returns>
    public T? Obtener(string id) => Listar().FirstOrDefault(p => p.Id == id);

    /// <summary>Identificador de la plantilla por omision.</summary>
    /// <returns>El guardado o, si no hay, el de la primera de fabrica.</returns>
    public string IdPorOmision()
    {
        var fabrica = DeFabrica();
        var ruta = Path.Combine(Carpeta, FicheroDeOmision);
        if (File.Exists(ruta))
        {
            var id = File.ReadAllText(ruta).Trim();
            if (id.Length > 0 && (fabrica.Any(p => p.Id == id) || File.Exists(RutaDelJson(id)))) return id;
        }

        return fabrica[0].Id;
    }

    /// <summary>La plantilla por omision.</summary>
    /// <returns>La plantilla.</returns>
    public T PorOmision() => Obtener(IdPorOmision()) ?? DeFabrica()[0];

    /// <summary>Marca una plantilla como la de omision.</summary>
    /// <param name="id">Su identificador.</param>
    public void PonerPorOmision(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Directory.CreateDirectory(Carpeta);
        File.WriteAllText(Path.Combine(Carpeta, FicheroDeOmision), id);
    }

    /// <summary>Es una de las del programa (sin guardar o sobrescrita con su identificador).</summary>
    /// <param name="id">El identificador.</param>
    /// <returns>Si lo es.</returns>
    public bool EsDeFabrica(string id) => DeFabrica().Any(p => p.Id == id);

    /// <summary>Guarda una plantilla.</summary>
    /// <param name="plantilla">La plantilla.</param>
    public void Guardar(T plantilla)
    {
        ArgumentNullException.ThrowIfNull(plantilla);
        ValidarId(plantilla.Id);
        Directory.CreateDirectory(Carpeta);

        // Se escribe a un temporal y se cambia de nombre: un corte a medias no deja un JSON roto.
        var ruta = RutaDelJson(plantilla.Id);
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(plantilla, Opciones));
        File.Move(temporal, ruta, overwrite: true);
    }

    /// <summary>Copia una imagen a la carpeta de la plantilla.</summary>
    /// <param name="plantilla">La plantilla que la va a usar.</param>
    /// <param name="rutaDeLaImagen">La imagen elegida por el operador.</param>
    /// <param name="uso">Para que es (fondo, logo, firma…): va en el nombre del fichero.</param>
    /// <returns>El nombre del fichero copiado, para apuntarlo en la plantilla.</returns>
    /// <exception cref="ArgumentException">No es una imagen que se sepa leer.</exception>
    public string CopiarImagen(T plantilla, string rutaDeLaImagen, string uso)
    {
        ArgumentNullException.ThrowIfNull(plantilla);
        ArgumentException.ThrowIfNullOrWhiteSpace(rutaDeLaImagen);
        ValidarId(plantilla.Id);
        var extension = Path.GetExtension(rutaDeLaImagen).ToLowerInvariant();
        if (!ExtensionesDeImagen.Contains(extension))
        {
            throw new ArgumentException("La imagen tiene que ser PNG, JPG, BMP, GIF o TIFF.", nameof(rutaDeLaImagen));
        }

        Directory.CreateDirectory(Carpeta);

        // El nombre lleva la hora: el visor de WPF guarda en cache las imagenes por ruta, y con
        // el mismo nombre seguiria enseñando la foto vieja despues de cambiarla.
        var nombre = $"{plantilla.Id}-{Limpio(uso)}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
        File.Copy(rutaDeLaImagen, Path.Combine(Carpeta, nombre), overwrite: true);
        return nombre;
    }

    /// <summary>Ruta completa de una imagen de plantilla, si existe.</summary>
    /// <param name="nombre">El nombre apuntado en la plantilla.</param>
    /// <returns>La ruta o nulo.</returns>
    public string? RutaDeImagen(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        var ruta = Path.Combine(Carpeta, Path.GetFileName(nombre));
        return File.Exists(ruta) ? ruta : null;
    }

    /// <summary>Borra una imagen que ya no usa ninguna plantilla guardada, sin fallar.</summary>
    /// <param name="nombre">Su nombre.</param>
    public void OlvidarImagen(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return;
        var enUso = Listar().SelectMany(p => p.Imagenes()).Contains(nombre, StringComparer.OrdinalIgnoreCase);
        if (!enUso) BorrarSinFallar(Path.Combine(Carpeta, Path.GetFileName(nombre)));
    }

    /// <summary>Copia de una plantilla con otro identificador y sus imagenes duplicadas.</summary>
    /// <param name="original">La plantilla.</param>
    /// <param name="nombre">Nombre de la copia.</param>
    /// <returns>La copia, sin guardar.</returns>
    public T Duplicar(T original, string nombre)
    {
        ArgumentNullException.ThrowIfNull(original);
        var copia = Clonar(original);
        copia.Id = IdNuevo();
        copia.Nombre = nombre;
        foreach (var imagen in original.Imagenes().Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (RutaDeImagen(imagen) is { } ruta) copia.RenombrarImagen(imagen, CopiarImagen(copia, ruta, UsoDe(imagen)));
        }

        return copia;
    }

    /// <summary>Borra una plantilla y sus imagenes.</summary>
    /// <param name="plantilla">La plantilla.</param>
    public void Borrar(T plantilla)
    {
        ArgumentNullException.ThrowIfNull(plantilla);
        ValidarId(plantilla.Id);
        BorrarSinFallar(RutaDelJson(plantilla.Id));
        foreach (var imagen in plantilla.Imagenes())
        {
            if (RutaDeImagen(imagen) is { } ruta) BorrarSinFallar(ruta);
        }

        if (IdPorOmision() == plantilla.Id) BorrarSinFallar(Path.Combine(Carpeta, FicheroDeOmision));
    }

    /// <summary>Guarda la plantilla y sus imagenes en un ZIP para llevarla a otro sitio.</summary>
    /// <param name="plantilla">La plantilla.</param>
    /// <param name="rutaDelZip">Donde dejar el fichero.</param>
    public void Exportar(T plantilla, string rutaDelZip)
    {
        ArgumentNullException.ThrowIfNull(plantilla);
        ArgumentException.ThrowIfNullOrWhiteSpace(rutaDelZip);
        var temporal = rutaDelZip + ".tmp";
        using (var zip = ZipFile.Open(temporal, ZipArchiveMode.Create))
        {
            var entrada = zip.CreateEntry(JsonDelPaquete, CompressionLevel.Optimal);
            using (var flujo = entrada.Open())
            {
                JsonSerializer.Serialize(flujo, plantilla, Opciones);
            }

            foreach (var imagen in plantilla.Imagenes().Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (RutaDeImagen(imagen) is { } ruta) zip.CreateEntryFromFile(ruta, Path.GetFileName(imagen), CompressionLevel.Optimal);
            }
        }

        File.Move(temporal, rutaDelZip, overwrite: true);
    }

    /// <summary>Trae una plantilla exportada y la guarda con un identificador nuevo.</summary>
    /// <param name="rutaDelZip">El fichero exportado.</param>
    /// <returns>La plantilla ya guardada.</returns>
    /// <exception cref="InvalidDataException">El fichero no es una plantilla.</exception>
    public T Importar(string rutaDelZip)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rutaDelZip);
        using var zip = ZipFile.OpenRead(rutaDelZip);
        var json = zip.GetEntry(JsonDelPaquete) ?? throw new InvalidDataException("El fichero no es una plantilla exportada: le falta plantilla.json.");
        T? plantilla;
        try
        {
            using var flujo = json.Open();
            plantilla = JsonSerializer.Deserialize<T>(flujo, Opciones);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("La plantilla del fichero está dañada.", ex);
        }

        if (plantilla is null) throw new InvalidDataException("La plantilla del fichero está vacía.");
        Normalizar(plantilla);
        plantilla.Id = IdNuevo();
        if (string.IsNullOrWhiteSpace(plantilla.Nombre)) plantilla.Nombre = "Plantilla importada";
        Directory.CreateDirectory(Carpeta);

        foreach (var imagen in plantilla.Imagenes().Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            // Solo el nombre: una entrada con «..\» no puede escribir fuera de la carpeta.
            var entrada = zip.GetEntry(Path.GetFileName(imagen));
            var extension = Path.GetExtension(imagen).ToLowerInvariant();
            if (entrada is null || !ExtensionesDeImagen.Contains(extension))
            {
                plantilla.RenombrarImagen(imagen, string.Empty);
                continue;
            }

            var nuevo = $"{plantilla.Id}-{Limpio(UsoDe(imagen))}-{DateTime.UtcNow:yyyyMMddHHmmssfff}{extension}";
            entrada.ExtractToFile(Path.Combine(Carpeta, nuevo), overwrite: true);
            plantilla.RenombrarImagen(imagen, nuevo);
        }

        Guardar(plantilla);
        return plantilla;
    }

    /// <summary>Un identificador nuevo que no esta en uso.</summary>
    /// <returns>Doce caracteres hexadecimales.</returns>
    public string IdNuevo()
    {
        string id;
        do
        {
            id = Guid.NewGuid().ToString("N")[..12];
        }
        while (File.Exists(RutaDelJson(id)));
        return id;
    }

    /// <summary>Comprueba que un identificador no se salga de la carpeta.</summary>
    /// <param name="id">El identificador.</param>
    /// <exception cref="ArgumentException">Tiene caracteres de ruta.</exception>
    protected static void ValidarId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Identificador de plantilla no válido: «{id}».", nameof(id));
        }
    }

    /// <summary>Borra un fichero; si esta abierto por otro programa se queda.</summary>
    /// <param name="ruta">El fichero.</param>
    protected static void BorrarSinFallar(string ruta)
    {
        try
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private T? Leer(string fichero)
    {
        try
        {
            var plantilla = JsonSerializer.Deserialize<T>(File.ReadAllText(fichero), Opciones);
            if (plantilla is not null) Normalizar(plantilla);
            return plantilla;
        }
        catch (JsonException)
        {
            // Un JSON roto no puede tumbar el editor: se salta y las demas se ven.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private string RutaDelJson(string id) => Path.Combine(Carpeta, id + ".json");

    private static string UsoDe(string imagen)
    {
        // <id>-<uso>-<hora>.<ext>: el uso es el penultimo trozo. Si el nombre no sigue la pauta, «imagen».
        var partes = Path.GetFileNameWithoutExtension(imagen).Split('-');
        return partes.Length >= 3 ? partes[^2] : "imagen";
    }

    private static string Limpio(string uso)
    {
        var limpio = new string(uso.Where(char.IsAsciiLetterOrDigit).ToArray());
        return limpio.Length == 0 ? "imagen" : limpio.ToLowerInvariant();
    }
}
