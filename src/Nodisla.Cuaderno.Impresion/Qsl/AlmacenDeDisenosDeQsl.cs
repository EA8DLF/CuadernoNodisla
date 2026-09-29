using System.Text.Json;

namespace Nodisla.Cuaderno.Impresion.Qsl;

/// <summary>
/// Las plantillas de tarjeta guardadas: un JSON y, si la tiene, su imagen de fondo.
/// </summary>
/// <remarks>
/// <para>
/// Viven en <c>&lt;carpeta de datos&gt;\qsl\disenos\</c>: <c>&lt;id&gt;.json</c> con la
/// plantilla y <c>&lt;id&gt;-fondo.&lt;ext&gt;</c> con la foto. <b>La foto se copia</b> a esa
/// carpeta al elegirla: si se leyera de donde la eligio el operador, borrar o mover la foto del
/// escritorio dejaria la tarjeta sin fondo sin avisar.
/// </para>
/// <para>
/// La plantilla por omision se apunta en <c>omision.txt</c>. Si no hay ninguna guardada se
/// ofrece la del programa (<see cref="DisenoDeQsl.PorOmision"/>) sin escribirla: el disco no se
/// toca hasta que el operador guarda algo suyo.
/// </para>
/// </remarks>
public sealed class AlmacenDeDisenosDeQsl
{
    private const string FicheroDeOmision = "omision.txt";

    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    /// <summary>Crea el almacen sobre la carpeta de datos del programa.</summary>
    /// <param name="carpetaDeDatos">La carpeta de datos (la de <c>CUADERNO_CARPETA</c> en pruebas).</param>
    public AlmacenDeDisenosDeQsl(string carpetaDeDatos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carpetaDeDatos);
        Carpeta = Path.Combine(carpetaDeDatos, "qsl", "disenos");
    }

    /// <summary>Donde se guardan.</summary>
    public string Carpeta { get; }

    /// <summary>Todas las plantillas, la del programa incluida si no se ha guardado otra con su nombre.</summary>
    /// <returns>Las plantillas, por nombre.</returns>
    public IReadOnlyList<DisenoDeQsl> Listar()
    {
        var disenos = new List<DisenoDeQsl>();
        if (Directory.Exists(Carpeta))
        {
            foreach (var fichero in Directory.EnumerateFiles(Carpeta, "*.json"))
            {
                try
                {
                    var diseno = JsonSerializer.Deserialize<DisenoDeQsl>(File.ReadAllText(fichero), Opciones);
                    if (diseno is null) continue;
                    diseno.Id = Path.GetFileNameWithoutExtension(fichero);
                    diseno.Campos ??= [];
                    disenos.Add(diseno);
                }
                catch (JsonException)
                {
                    // Un JSON roto no puede tumbar el editor: se salta y las demas se ven.
                }
            }
        }

        if (!disenos.Any(d => d.Id == "nodisla")) disenos.Add(DisenoDeQsl.PorOmision());
        return disenos.OrderBy(d => d.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Identificador de la plantilla por omision.</summary>
    /// <returns>El guardado o, si no hay, el de la del programa.</returns>
    public string IdPorOmision()
    {
        var ruta = Path.Combine(Carpeta, FicheroDeOmision);
        if (File.Exists(ruta))
        {
            var id = File.ReadAllText(ruta).Trim();
            if (id.Length > 0 && (id == "nodisla" || File.Exists(RutaDelJson(id)))) return id;
        }

        return "nodisla";
    }

    /// <summary>La plantilla por omision.</summary>
    /// <returns>La plantilla.</returns>
    public DisenoDeQsl PorOmision()
    {
        var id = IdPorOmision();
        return Listar().FirstOrDefault(d => d.Id == id) ?? DisenoDeQsl.PorOmision();
    }

    /// <summary>Marca una plantilla como la de omision.</summary>
    /// <param name="id">Su identificador.</param>
    public void PonerPorOmision(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Directory.CreateDirectory(Carpeta);
        File.WriteAllText(Path.Combine(Carpeta, FicheroDeOmision), id);
    }

    /// <summary>Guarda una plantilla.</summary>
    /// <param name="diseno">La plantilla.</param>
    public void Guardar(DisenoDeQsl diseno)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ValidarId(diseno.Id);
        Directory.CreateDirectory(Carpeta);

        // Se escribe a un temporal y se cambia de nombre: un corte a medias no deja un JSON roto.
        var ruta = RutaDelJson(diseno.Id);
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(diseno, Opciones));
        File.Move(temporal, ruta, overwrite: true);
    }

    /// <summary>Copia una imagen como fondo de la plantilla y la apunta en ella.</summary>
    /// <param name="diseno">La plantilla (se modifica su <see cref="DisenoDeQsl.ImagenDeFondo"/>).</param>
    /// <param name="rutaDeLaImagen">La foto elegida por el operador.</param>
    public void PonerFondo(DisenoDeQsl diseno, string rutaDeLaImagen)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ArgumentException.ThrowIfNullOrWhiteSpace(rutaDeLaImagen);
        ValidarId(diseno.Id);
        Directory.CreateDirectory(Carpeta);

        var extension = Path.GetExtension(rutaDeLaImagen).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff"))
        {
            throw new ArgumentException("La imagen tiene que ser PNG, JPG, BMP, GIF o TIFF.", nameof(rutaDeLaImagen));
        }

        // El nombre lleva la hora: el visor de WPF guarda en cache las imagenes por ruta, y con
        // el mismo nombre seguiria enseñando la foto vieja despues de cambiarla.
        var nombre = $"{diseno.Id}-fondo-{DateTime.UtcNow:yyyyMMddHHmmss}{extension}";
        File.Copy(rutaDeLaImagen, Path.Combine(Carpeta, nombre), overwrite: true);
        var anterior = diseno.ImagenDeFondo;
        diseno.ImagenDeFondo = nombre;
        if (!string.IsNullOrEmpty(anterior) && anterior != nombre) BorrarSinFallar(Path.Combine(Carpeta, anterior));
    }

    /// <summary>Ruta completa de la imagen de fondo, si la tiene y existe.</summary>
    /// <param name="diseno">La plantilla.</param>
    /// <returns>La ruta o nulo.</returns>
    public string? RutaDelFondo(DisenoDeQsl diseno)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        if (string.IsNullOrWhiteSpace(diseno.ImagenDeFondo)) return null;
        var ruta = Path.Combine(Carpeta, Path.GetFileName(diseno.ImagenDeFondo));
        return File.Exists(ruta) ? ruta : null;
    }

    /// <summary>Borra una plantilla y su imagen.</summary>
    /// <param name="diseno">La plantilla.</param>
    public void Borrar(DisenoDeQsl diseno)
    {
        ArgumentNullException.ThrowIfNull(diseno);
        ValidarId(diseno.Id);
        BorrarSinFallar(RutaDelJson(diseno.Id));
        if (RutaDelFondo(diseno) is { } fondo) BorrarSinFallar(fondo);
        if (IdPorOmision() == diseno.Id) BorrarSinFallar(Path.Combine(Carpeta, FicheroDeOmision));
    }

    private string RutaDelJson(string id) => Path.Combine(Carpeta, id + ".json");

    private static void ValidarId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Identificador de plantilla no válido: «{id}».", nameof(id));
        }
    }

    private static void BorrarSinFallar(string ruta)
    {
        try
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
        catch (IOException)
        {
            // Si la imagen esta abierta por otro programa se queda; no es motivo para fallar.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
