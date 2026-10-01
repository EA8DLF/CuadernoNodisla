using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>Un capitulo de la ayuda: uno de los ficheros de <c>docs/ayuda</c>.</summary>
/// <param name="Clave">El nombre del fichero sin «.md» (p. ej. «02-operar»).</param>
/// <param name="Titulo">El titulo de primer nivel del capitulo.</param>
/// <param name="Texto">El Markdown entero.</param>
public sealed record CapituloDeAyuda(string Clave, string Titulo, string Texto)
{
    private string? _paraBuscar;

    /// <summary>Titulo y texto en minusculas y sin tildes, para la busqueda.</summary>
    public string ParaBuscar => _paraBuscar ??= LibroDeAyuda.Normalizar(Titulo + "\n" + Texto);

    /// <inheritdoc />
    public override string ToString() => Titulo;
}

/// <summary>
/// Los capitulos de la ayuda y sus capturas, tal y como van incrustados en el ejecutable.
/// </summary>
/// <remarks>
/// Los <c>.md</c> de <c>docs/ayuda</c> y los <c>.png</c> de <c>docs/capturas/ayuda</c> se
/// incrustan al compilar (ver el <c>.csproj</c>): la ayuda que se lee dentro del programa es
/// siempre la de su misma version, y no hace falta red ni navegador.
/// </remarks>
public sealed class LibroDeAyuda
{
    /// <summary>Prefijo de los recursos de los capitulos.</summary>
    public const string PrefijoDeCapitulos = "Ayuda.";

    /// <summary>Prefijo de los recursos de las capturas.</summary>
    public const string PrefijoDeCapturas = "Ayuda.capturas.";

    private readonly Func<string, Stream?> _abrir;

    /// <summary>Monta el libro con unos capitulos y una forma de abrir las imagenes.</summary>
    /// <param name="capitulos">Los capitulos, ya en orden.</param>
    /// <param name="abrirImagen">Abre una captura por su nombre de fichero, o devuelve nulo.</param>
    public LibroDeAyuda(IReadOnlyList<CapituloDeAyuda> capitulos, Func<string, Stream?> abrirImagen)
    {
        Capitulos = capitulos ?? throw new ArgumentNullException(nameof(capitulos));
        _abrir = abrirImagen ?? throw new ArgumentNullException(nameof(abrirImagen));
    }

    /// <summary>Los capitulos: primero la presentacion (README) y luego por su numero.</summary>
    public IReadOnlyList<CapituloDeAyuda> Capitulos { get; }

    /// <summary>Lee los capitulos incrustados en un ensamblado (por omision, el del programa).</summary>
    /// <param name="ensamblado">Ensamblado con los recursos.</param>
    /// <returns>El libro.</returns>
    public static LibroDeAyuda DelEnsamblado(Assembly? ensamblado = null)
    {
        ensamblado ??= typeof(LibroDeAyuda).Assembly;
        var capitulos = new List<CapituloDeAyuda>();
        foreach (var recurso in ensamblado.GetManifestResourceNames())
        {
            if (!recurso.StartsWith(PrefijoDeCapitulos, StringComparison.Ordinal)
                || recurso.StartsWith(PrefijoDeCapturas, StringComparison.Ordinal)
                || !recurso.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var flujo = ensamblado.GetManifestResourceStream(recurso)!;
            using var lector = new StreamReader(flujo, Encoding.UTF8);
            capitulos.Add(Capitulo(recurso[PrefijoDeCapitulos.Length..^3], lector.ReadToEnd()));
        }

        return new LibroDeAyuda(Ordenar(capitulos), nombre =>
            ensamblado.GetManifestResourceStream(PrefijoDeCapturas + nombre));
    }

    /// <summary>Monta un capitulo a partir de su clave y su texto.</summary>
    /// <param name="clave">Nombre del fichero sin extension.</param>
    /// <param name="texto">El Markdown.</param>
    /// <returns>El capitulo, con su titulo sacado del primer «# ».</returns>
    public static CapituloDeAyuda Capitulo(string clave, string texto) =>
        new(clave, DocumentoMarkdown.TituloPrincipal(DocumentoMarkdown.Leer(texto)) ?? clave, texto);

    /// <summary>El README primero; los demas por su nombre, que empieza por el numero.</summary>
    /// <param name="capitulos">Capitulos sin ordenar.</param>
    /// <returns>Los capitulos en orden.</returns>
    public static IReadOnlyList<CapituloDeAyuda> Ordenar(IEnumerable<CapituloDeAyuda> capitulos) =>
        capitulos
            .OrderBy(c => c.Clave.Equals("README", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Clave, StringComparer.Ordinal)
            .ToList();

    /// <summary>Busca un capitulo por su clave o por el nombre de su fichero.</summary>
    /// <param name="claveOFichero">«02-operar», «02-operar.md» o «../ayuda/02-operar.md».</param>
    /// <returns>El capitulo, o nulo.</returns>
    public CapituloDeAyuda? Buscar(string? claveOFichero)
    {
        if (string.IsNullOrWhiteSpace(claveOFichero)) return null;
        var clave = Path.GetFileName(claveOFichero.Replace('\\', '/'));
        if (clave.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) clave = clave[..^3];
        return Capitulos.FirstOrDefault(c => string.Equals(c.Clave, clave, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Abre una captura por la ruta con que la cita el capitulo.</summary>
    /// <param name="ruta">Ruta del Markdown (p. ej. «../capturas/ayuda/operar.png»).</param>
    /// <returns>El flujo de la imagen, o nulo si no va incrustada.</returns>
    public Stream? AbrirImagen(string ruta) =>
        string.IsNullOrWhiteSpace(ruta) ? null : _abrir(Path.GetFileName(ruta.Replace('\\', '/')));

    /// <summary>Capitulos cuyo titulo o texto contienen todas las palabras buscadas.</summary>
    /// <param name="busqueda">Lo tecleado; sin distinguir mayusculas ni tildes.</param>
    /// <returns>Los capitulos que casan, los que lo tienen en el titulo primero.</returns>
    public IReadOnlyList<CapituloDeAyuda> Filtrar(string? busqueda)
    {
        var palabras = Palabras(busqueda);
        if (palabras.Length == 0) return Capitulos;

        return Capitulos
            .Where(c => palabras.All(p => c.ParaBuscar.Contains(p, StringComparison.Ordinal)))
            .OrderBy(c => palabras.All(p => Normalizar(c.Titulo).Contains(p, StringComparison.Ordinal)) ? 0 : 1)
            .ToList();
    }

    /// <summary>Las palabras de una busqueda, ya normalizadas.</summary>
    /// <param name="busqueda">Lo tecleado.</param>
    /// <returns>Las palabras.</returns>
    public static string[] Palabras(string? busqueda) =>
        string.IsNullOrWhiteSpace(busqueda)
            ? []
            : Normalizar(busqueda).Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Minusculas y sin tildes: «Fonía» y «fonia» son lo mismo al buscar.</summary>
    /// <param name="texto">Texto.</param>
    /// <returns>Texto normalizado, de la misma longitud que el original.</returns>
    public static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
        {
            var sinTilde = c.ToString().Normalize(NormalizationForm.FormD);
            var basica = sinTilde.Length > 0 && CharUnicodeInfo.GetUnicodeCategory(sinTilde[0]) != UnicodeCategory.NonSpacingMark
                ? sinTilde[0]
                : c;
            sb.Append(char.ToLowerInvariant(basica));
        }

        return sb.ToString();
    }
}
