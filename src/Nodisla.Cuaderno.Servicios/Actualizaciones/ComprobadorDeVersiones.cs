using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Servicios.Actualizaciones;

/// <summary>
/// Pregunta a GitHub cual es la ultima version publicada y si es mas nueva que la instalada.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nunca falla hacia fuera.</b> Sin red, con el repositorio todavia privado (la API responde
/// 404 igual que si no existiera), con el limite de 60 peticiones por hora agotado o con una
/// respuesta que no se entiende, el resultado es <see cref="EstadoDeComprobacion.NoDisponible"/>
/// y una linea de registro en nivel <c>Debug</c>. Enterarse de que hay version nueva es una
/// comodidad: no puede convertirse en un cartel de error cada vez que se abre el programa.
/// </para>
/// <para>
/// La peticion es anonima y lleva un <c>User-Agent</c> propio, que GitHub exige.
/// </para>
/// </remarks>
public sealed class ComprobadorDeVersiones
{
    private readonly IHttpClientFactory _fabrica;
    private readonly ILogger _log;

    /// <summary>Crea el comprobador.</summary>
    /// <param name="fabrica">Fabrica de clientes HTTP (cliente <see cref="Red.NombresDeClienteHttp.GitHub"/>).</param>
    /// <param name="instalada">Version que esta corriendo.</param>
    /// <param name="repositorio">Repositorio; por omision el de Cuaderno NODISLA.</param>
    /// <param name="log">Registro.</param>
    public ComprobadorDeVersiones(
        IHttpClientFactory fabrica,
        VersionSemantica instalada,
        RepositorioPublico? repositorio = null,
        ILogger<ComprobadorDeVersiones>? log = null)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        Instalada = instalada ?? throw new ArgumentNullException(nameof(instalada));
        Repositorio = repositorio ?? RepositorioPublico.CuadernoNodisla;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Version que esta corriendo.</summary>
    public VersionSemantica Instalada { get; }

    /// <summary>Repositorio que se consulta.</summary>
    public RepositorioPublico Repositorio { get; }

    /// <summary>Pregunta a GitHub. No lanza salvo cancelacion pedida.</summary>
    /// <param name="cancelacion">Cancelacion.</param>
    public async Task<ResultadoDeComprobacion> ComprobarAsync(CancellationToken cancelacion = default)
    {
        try
        {
            using var cliente = _fabrica.CreateClient(Red.NombresDeClienteHttp.GitHub);
            using var peticion = new HttpRequestMessage(HttpMethod.Get, Repositorio.UltimaPublicacion);
            peticion.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            peticion.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            if (cliente.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                peticion.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse(Red.NombresDeClienteHttp.AgenteDeUsuario));
            }

            using var respuesta = await cliente.SendAsync(peticion, cancelacion).ConfigureAwait(false);

            if (respuesta.StatusCode == HttpStatusCode.NotFound)
            {
                // Repositorio privado, o sin ninguna publicacion todavia. Lo normal hasta que
                // Jose lo haga publico: se calla.
                _log.LogDebug("Sin publicaciones visibles en {Repositorio} (404).", Repositorio.UltimaPublicacion);
                return ResultadoDeComprobacion.NoDisponible("Todavía no hay versiones publicadas.");
            }

            if (!respuesta.IsSuccessStatusCode)
            {
                _log.LogDebug("GitHub respondió {Codigo} al buscar versiones.", (int)respuesta.StatusCode);
                return ResultadoDeComprobacion.NoDisponible($"GitHub respondió {(int)respuesta.StatusCode}.");
            }

            var json = await respuesta.Content.ReadAsStringAsync(cancelacion).ConfigureAwait(false);
            var publicada = Interpretar(json, Repositorio);
            if (publicada is null)
            {
                _log.LogDebug("La última publicación de GitHub no trae un número de versión legible.");
                return ResultadoDeComprobacion.NoDisponible("La publicación no tiene un número de versión legible.");
            }

            if (publicada.Version > Instalada)
            {
                _log.LogInformation(
                    "Hay versión nueva: {Nueva} (instalada {Instalada}).", publicada.Version, Instalada);
                return new ResultadoDeComprobacion(EstadoDeComprobacion.HayVersionNueva, publicada);
            }

            _log.LogDebug("Al día: instalada {Instalada}, publicada {Publicada}.", Instalada, publicada.Version);
            return ResultadoDeComprobacion.AlDia;
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or IOException or InvalidOperationException)
        {
            // TaskCanceledException sin cancelacion pedida es el tiempo de espera del cliente.
            _log.LogDebug(ex, "No se ha podido consultar la última versión en GitHub.");
            return ResultadoDeComprobacion.NoDisponible("No se ha podido contactar con GitHub.");
        }
    }

    /// <summary>
    /// Lee la respuesta de <c>/releases/latest</c>. Nulo si no es una publicacion utilizable.
    /// </summary>
    /// <param name="json">Cuerpo de la respuesta.</param>
    /// <param name="repositorio">Repositorio, para validar las direcciones de descarga.</param>
    public static VersionPublicada? Interpretar(string json, RepositorioPublico repositorio)
    {
        ArgumentNullException.ThrowIfNull(repositorio);

        using var documento = JsonDocument.Parse(json);
        var raiz = documento.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object) return null;

        if (Booleano(raiz, "draft") || Booleano(raiz, "prerelease")) return null;

        var etiqueta = Cadena(raiz, "tag_name");
        if (!VersionSemantica.TryAnalizar(etiqueta, out var version)) return null;

        var pagina = Uri.TryCreate(Cadena(raiz, "html_url"), UriKind.Absolute, out var p)
            && p.Scheme == Uri.UriSchemeHttps
            ? p
            : repositorio.Publicaciones;

        DateTimeOffset? fecha = DateTimeOffset.TryParse(
            Cadena(raiz, "published_at"),
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var f) ? f : null;

        var ficheros = new List<FicheroPublicado>();
        if (raiz.TryGetProperty("assets", out var adjuntos) && adjuntos.ValueKind == JsonValueKind.Array)
        {
            foreach (var adjunto in adjuntos.EnumerateArray())
            {
                var nombre = Cadena(adjunto, "name");
                var direccion = Cadena(adjunto, "browser_download_url");
                if (string.IsNullOrWhiteSpace(nombre) || direccion is null) continue;

                // Solo descargas del propio repositorio y por HTTPS: si la respuesta apuntara a
                // otro sitio, no se baja nada.
                if (!direccion.StartsWith(repositorio.PrefijoDeDescargas, StringComparison.Ordinal)) continue;
                if (!Uri.TryCreate(direccion, UriKind.Absolute, out var uri)) continue;

                var bytes = adjunto.TryGetProperty("size", out var t) && t.TryGetInt64(out var b) ? b : 0;
                ficheros.Add(new FicheroPublicado(nombre, uri, bytes));
            }
        }

        var instalador = ElegirInstalador(ficheros);
        var suma = instalador is null
            ? null
            : ficheros.FirstOrDefault(x =>
                string.Equals(x.Nombre, instalador.Nombre + ".sha256", StringComparison.OrdinalIgnoreCase));

        return new VersionPublicada(
            version!,
            etiqueta!,
            Cadena(raiz, "name") is { Length: > 0 } titulo ? titulo : etiqueta!,
            Cadena(raiz, "body") ?? string.Empty,
            pagina,
            fecha,
            instalador,
            suma);
    }

    // El instalador de Inno Setup se llama CuadernoNodisla-Instalador-<version>.exe. Si alguna
    // vez se publica otro .exe (una herramienta suelta), gana el que se llama asi.
    private static FicheroPublicado? ElegirInstalador(List<FicheroPublicado> ficheros)
    {
        var exes = ficheros
            .Where(x => x.Nombre.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return exes.FirstOrDefault(x => x.Nombre.StartsWith("CuadernoNodisla-Instalador", StringComparison.OrdinalIgnoreCase))
            ?? (exes.Count == 1 ? exes[0] : null);
    }

    private static string? Cadena(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.String
            ? valor.GetString()
            : null;

    private static bool Booleano(JsonElement elemento, string propiedad) =>
        elemento.TryGetProperty(propiedad, out var valor) && valor.ValueKind == JsonValueKind.True;
}
