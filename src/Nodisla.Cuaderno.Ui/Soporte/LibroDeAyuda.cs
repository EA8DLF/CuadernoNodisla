using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>Un capitulo de la ayuda: uno de los ficheros de <c>docs/ayuda</c>.</summary>
/// <param name="Clave">El nombre del fichero sin «.md» (p. ej. «02-operar»).</param>
/// <param name="Titulo">El titulo de primer nivel del capitulo.</param>
/// <param name="Texto">El Markdown entero.</param>
public sealed record CapituloDeAyuda(string Clave, string Titulo, string Texto)
{
    private string? _paraBuscar;

    /// <summary>Idioma en que esta escrito el texto («es», «en»...).</summary>
    public string Idioma { get; init; } = LibroDeAyuda.IdiomaOriginal;

    /// <summary>
    /// Cierto si se pidio en otro idioma y, a falta de traduccion, se ensena el espanol con un
    /// aviso al principio.
    /// </summary>
    public bool SinTraducir { get; init; }

    /// <summary>Titulo y texto en minusculas y sin tildes, para la busqueda.</summary>
    public string ParaBuscar => _paraBuscar ??= LibroDeAyuda.Normalizar(Titulo + "\n" + Texto);

    /// <inheritdoc />
    public override string ToString() => Titulo;
}

/// <summary>
/// Los capitulos de la ayuda y sus capturas, tal y como van incrustados en el ejecutable.
/// </summary>
/// <remarks>
/// <para>
/// Los <c>.md</c> de <c>docs/ayuda</c> y los <c>.png</c> de <c>docs/capturas/ayuda</c> se
/// incrustan al compilar (ver el <c>.csproj</c>): la ayuda que se lee dentro del programa es
/// siempre la de su misma version, y no hace falta red ni navegador.
/// </para>
/// <para>
/// <b>Idiomas.</b> El espanol es el original. Las traducciones viven en
/// <c>docs/ayuda/&lt;idioma&gt;/&lt;mismo-nombre&gt;.md</c> y se incrustan como
/// <c>AyudaIdioma.&lt;idioma&gt;.&lt;fichero&gt;</c>. <see cref="Capitulos"/> da los del idioma en
/// uso: cada capitulo traducido, y los que faltan en espanol con un aviso al principio. Los
/// enlaces de un capitulo traducido a uno que no lo esta llevan las anclas espanolas, que son las
/// que tiene el capitulo que se ensena. Las capturas son las mismas para todos.
/// </para>
/// </remarks>
public sealed class LibroDeAyuda
{
    /// <summary>Prefijo de los recursos de los capitulos.</summary>
    public const string PrefijoDeCapitulos = "Ayuda.";

    /// <summary>Prefijo de los recursos de las capturas.</summary>
    public const string PrefijoDeCapturas = "Ayuda.capturas.";

    /// <summary>Prefijo de los capitulos traducidos: <c>AyudaIdioma.en.01-primer-uso.md</c>.</summary>
    public const string PrefijoDeTraducciones = "AyudaIdioma.";

    /// <summary>Idioma en que se escribe la ayuda.</summary>
    public const string IdiomaOriginal = "es";

    private readonly Func<string, Stream?> _abrir;
    private readonly IReadOnlyList<CapituloDeAyuda> _originales;
    private readonly Dictionary<string, Dictionary<string, string>> _traducciones;
    private readonly Dictionary<string, IReadOnlyList<CapituloDeAyuda>> _porIdioma = new(StringComparer.Ordinal);

    /// <summary>Monta el libro con unos capitulos y una forma de abrir las imagenes.</summary>
    /// <param name="capitulos">Los capitulos en espanol, ya en orden.</param>
    /// <param name="abrirImagen">Abre una captura por su nombre de fichero, o devuelve nulo.</param>
    /// <param name="traducciones">
    /// Los capitulos traducidos: idioma → (clave del capitulo → Markdown). Solo cuentan los que
    /// tienen original en espanol.
    /// </param>
    public LibroDeAyuda(
        IReadOnlyList<CapituloDeAyuda> capitulos,
        Func<string, Stream?> abrirImagen,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? traducciones = null)
    {
        _originales = capitulos ?? throw new ArgumentNullException(nameof(capitulos));
        _abrir = abrirImagen ?? throw new ArgumentNullException(nameof(abrirImagen));
        _traducciones = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (traducciones is not null)
        {
            foreach (var (idioma, textos) in traducciones)
            {
                _traducciones[idioma] = new Dictionary<string, string>(textos, StringComparer.OrdinalIgnoreCase);
            }
        }

        _porIdioma[IdiomaOriginal] = _originales;
    }

    /// <summary>
    /// Los capitulos en el idioma en uso (<see cref="Textos.Codigo"/>): primero la presentacion
    /// (README) y luego por su numero.
    /// </summary>
    public IReadOnlyList<CapituloDeAyuda> Capitulos => CapitulosEn(Textos.Codigo);

    /// <summary>Idiomas que tienen al menos un capitulo traducido.</summary>
    public IReadOnlyCollection<string> IdiomasTraducidos => _traducciones.Keys;

    /// <summary>Claves de los capitulos traducidos a un idioma (solo los que tienen original).</summary>
    /// <param name="idioma">Codigo de dos letras.</param>
    /// <returns>Las claves (vacio si no hay ninguno).</returns>
    public IReadOnlyList<string> TraducidosA(string idioma) =>
        _traducciones.TryGetValue(idioma, out var textos)
            ? _originales.Where(c => textos.ContainsKey(c.Clave)).Select(c => c.Clave).ToList()
            : [];

    /// <summary>Los capitulos en un idioma: los traducidos y, los que faltan, en espanol con aviso.</summary>
    /// <param name="idioma">Codigo de dos letras.</param>
    /// <returns>Los capitulos, en el orden de los originales; los mismos objetos cada vez para el mismo idioma.</returns>
    public IReadOnlyList<CapituloDeAyuda> CapitulosEn(string idioma)
    {
        ArgumentNullException.ThrowIfNull(idioma);
        lock (_porIdioma)
        {
            if (_porIdioma.TryGetValue(idioma, out var hechos)) return hechos;

            _traducciones.TryGetValue(idioma, out var textos);
            var aviso = Textos.Buscar("Ayuda.CapituloSinTraducir", CultureInfo.GetCultureInfo(idioma))
                ?? "Este capítulo aún no está traducido a su idioma; se muestra en español.";
            var capitulos = _originales
                .Select(original => textos is not null && textos.TryGetValue(original.Clave, out var traducido)
                    ? Capitulo(original.Clave, traducido) with { Idioma = idioma }
                    : new CapituloDeAyuda(original.Clave, original.Titulo, ConAviso(original.Texto, aviso)) { SinTraducir = true })
                .ToList();
            _porIdioma[idioma] = capitulos;
            return capitulos;
        }
    }

    /// <summary>Pone un aviso, como cita en cursiva, justo debajo del titulo principal.</summary>
    /// <param name="texto">Markdown del capitulo.</param>
    /// <param name="aviso">Texto del aviso.</param>
    /// <returns>El Markdown con el aviso.</returns>
    public static string ConAviso(string texto, string aviso)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var cita = "> *" + aviso + "*\n\n";
        var desde = 0;
        foreach (var linea in texto.Split('\n'))
        {
            var fin = desde + linea.Length + 1;
            if (linea.StartsWith("# ", StringComparison.Ordinal))
            {
                return fin >= texto.Length
                    ? texto + "\n\n" + cita
                    : texto[..fin] + "\n" + cita + texto[fin..];
            }

            desde = fin;
        }

        return cita + texto;
    }

    /// <summary>Lee los capitulos incrustados en un ensamblado (por omision, el del programa).</summary>
    /// <param name="ensamblado">Ensamblado con los recursos.</param>
    /// <returns>El libro.</returns>
    public static LibroDeAyuda DelEnsamblado(Assembly? ensamblado = null)
    {
        ensamblado ??= typeof(LibroDeAyuda).Assembly;
        var capitulos = new List<CapituloDeAyuda>();
        var traducciones = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var recurso in ensamblado.GetManifestResourceNames())
        {
            if (!recurso.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) continue;

            if (recurso.StartsWith(PrefijoDeTraducciones, StringComparison.Ordinal))
            {
                // AyudaIdioma.<idioma>.<clave>.md
                var resto = recurso[PrefijoDeTraducciones.Length..^3];
                var punto = resto.IndexOf('.', StringComparison.Ordinal);
                if (punto <= 0 || punto == resto.Length - 1) continue;
                var idioma = resto[..punto];
                if (!traducciones.TryGetValue(idioma, out var deEse))
                {
                    deEse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    traducciones[idioma] = deEse;
                }

                deEse[resto[(punto + 1)..]] = Leer(ensamblado, recurso);
                continue;
            }

            if (!recurso.StartsWith(PrefijoDeCapitulos, StringComparison.Ordinal)
                || recurso.StartsWith(PrefijoDeCapturas, StringComparison.Ordinal))
            {
                continue;
            }

            capitulos.Add(Capitulo(recurso[PrefijoDeCapitulos.Length..^3], Leer(ensamblado, recurso)));
        }

        return new LibroDeAyuda(
            Ordenar(capitulos),
            nombre => ensamblado.GetManifestResourceStream(PrefijoDeCapturas + nombre),
            traducciones.ToDictionary(t => t.Key, t => (IReadOnlyDictionary<string, string>)t.Value, StringComparer.Ordinal));
    }

    private static string Leer(Assembly ensamblado, string recurso)
    {
        using var flujo = ensamblado.GetManifestResourceStream(recurso)!;
        using var lector = new StreamReader(flujo, Encoding.UTF8);
        return lector.ReadToEnd();
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

    /// <summary>Busca un capitulo, en el idioma en uso, por su clave o por el nombre de su fichero.</summary>
    /// <param name="claveOFichero">«02-operar», «02-operar.md» o «../ayuda/02-operar.md».</param>
    /// <returns>El capitulo, o nulo.</returns>
    public CapituloDeAyuda? Buscar(string? claveOFichero) => Buscar(claveOFichero, Textos.Codigo);

    /// <summary>Busca un capitulo en un idioma por su clave o por el nombre de su fichero.</summary>
    /// <param name="claveOFichero">«02-operar», «02-operar.md» o «../ayuda/02-operar.md».</param>
    /// <param name="idioma">Codigo de dos letras.</param>
    /// <returns>El capitulo, o nulo.</returns>
    public CapituloDeAyuda? Buscar(string? claveOFichero, string idioma)
    {
        if (string.IsNullOrWhiteSpace(claveOFichero)) return null;
        var clave = Path.GetFileName(claveOFichero.Replace('\\', '/'));
        if (clave.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) clave = clave[..^3];
        return CapitulosEn(idioma).FirstOrDefault(c => string.Equals(c.Clave, clave, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// El ancla de un apartado tal y como existe en el capitulo que se ensena.
    /// </summary>
    /// <remarks>
    /// Un capitulo sin traducir enlaza a los demas con las anclas espanolas. Si el destino esta
    /// traducido, su apartado tiene otra ancla: se busca el titulo que ocupa el mismo lugar en el
    /// original, que para eso las traducciones conservan los mismos titulos en el mismo orden.
    /// </remarks>
    /// <param name="capitulo">El capitulo de destino, en el idioma en que se ensena.</param>
    /// <param name="ancla">El ancla pedida (puede ser la del original espanol).</param>
    /// <returns>El ancla que existe en el capitulo, o la pedida si no hay equivalencia.</returns>
    public string? Ancla(CapituloDeAyuda capitulo, string? ancla)
    {
        ArgumentNullException.ThrowIfNull(capitulo);
        if (string.IsNullOrEmpty(ancla) || capitulo.Idioma == IdiomaOriginal) return ancla;

        var propios = Titulos(capitulo.Texto);
        if (propios.Any(t => t.Ancla == ancla)) return ancla;

        var original = _originales.FirstOrDefault(c => string.Equals(c.Clave, capitulo.Clave, StringComparison.OrdinalIgnoreCase));
        if (original is null) return ancla;
        var delOriginal = Titulos(original.Texto);
        var lugar = delOriginal.FindIndex(t => t.Ancla == ancla);
        return lugar >= 0 && delOriginal.Count == propios.Count ? propios[lugar].Ancla : ancla;
    }

    private static List<TituloMd> Titulos(string texto) =>
        DocumentoMarkdown.Leer(texto).OfType<TituloMd>().ToList();

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
        var capitulos = Capitulos;
        if (palabras.Length == 0) return capitulos;

        return capitulos
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
