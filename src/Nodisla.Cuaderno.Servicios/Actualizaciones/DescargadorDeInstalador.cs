using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Servicios.Actualizaciones;

/// <summary>Que salio de descargar el instalador.</summary>
public enum EstadoDeDescarga
{
    /// <summary>Descargado y con la suma SHA-256 correcta: se puede instalar.</summary>
    Verificado,

    /// <summary>
    /// Descargado, pero la suma no casa con la publicada. El fichero ya se ha borrado y
    /// <b>no se instala</b>.
    /// </summary>
    SumaNoCasa,

    /// <summary>No se pudo descargar (red, falta la suma, fichero raro...).</summary>
    Fallida,
}

/// <summary>Resultado de la descarga.</summary>
/// <param name="Estado">Que salio.</param>
/// <param name="Ruta">Ruta del instalador verificado; nula si no se verifico.</param>
/// <param name="Motivo">Explicacion para el operador cuando no se verifico.</param>
public sealed record ResultadoDeDescarga(EstadoDeDescarga Estado, string? Ruta = null, string? Motivo = null);

/// <summary>
/// Baja el instalador de una version publicada y comprueba su SHA-256 contra el fichero
/// <c>.sha256</c> publicado a su lado.
/// </summary>
/// <remarks>
/// <para>
/// La suma se baja <b>antes</b> que el instalador: sin suma no se baja nada. Un instalador que
/// no se puede comprobar no se ejecuta, por mucho que venga de la pagina buena.
/// </para>
/// <para>
/// El instalador se escribe con extension <c>.parcial</c> y solo se renombra a <c>.exe</c>
/// cuando la suma casa. Si no casa, se borra: no queda en disco un ejecutable dudoso que
/// alguien pueda abrir con dos clics.
/// </para>
/// </remarks>
public sealed partial class DescargadorDeInstalador
{
    /// <summary>Tamano maximo que se acepta. El instalador de verdad ronda los 70 MB.</summary>
    public const long TamanoMaximo = 1024L * 1024 * 1024;

    private readonly IHttpClientFactory _fabrica;
    private readonly string _carpeta;
    private readonly ILogger _log;

    /// <summary>Crea el descargador.</summary>
    /// <param name="fabrica">Fabrica de clientes HTTP (cliente <see cref="Red.NombresDeClienteHttp.GitHubDescargas"/>).</param>
    /// <param name="carpeta">Carpeta temporal; por omision <c>%TEMP%\CuadernoNodisla-actualizacion</c>.</param>
    /// <param name="log">Registro.</param>
    public DescargadorDeInstalador(
        IHttpClientFactory fabrica,
        string? carpeta = null,
        ILogger<DescargadorDeInstalador>? log = null)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _carpeta = carpeta ?? Path.Combine(Path.GetTempPath(), "CuadernoNodisla-actualizacion");
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    /// <summary>Descarga y verifica. Solo lanza si se cancela.</summary>
    /// <param name="version">Version a descargar.</param>
    /// <param name="progreso">Fraccion descargada, de 0 a 1 (si se conoce el tamano).</param>
    /// <param name="cancelacion">Cancelacion.</param>
    public async Task<ResultadoDeDescarga> DescargarAsync(
        VersionPublicada version,
        IProgress<double>? progreso = null,
        CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        if (version.Instalador is null)
        {
            return new ResultadoDeDescarga(EstadoDeDescarga.Fallida,
                Motivo: "La versión publicada no trae instalador. Descárguela desde la página de GitHub.");
        }

        if (version.Suma is null)
        {
            return new ResultadoDeDescarga(EstadoDeDescarga.Fallida,
                Motivo: "La versión publicada no trae el fichero .sha256; sin él no se puede comprobar el instalador y no se instala.");
        }

        string? parcial = null;
        try
        {
            using var cliente = _fabrica.CreateClient(Red.NombresDeClienteHttp.GitHubDescargas);

            var textoDeSuma = await cliente.GetStringAsync(version.Suma.Direccion, cancelacion).ConfigureAwait(false);
            var esperada = LeerSuma(textoDeSuma);
            if (esperada is null)
            {
                return new ResultadoDeDescarga(EstadoDeDescarga.Fallida,
                    Motivo: "El fichero .sha256 publicado no contiene una suma SHA-256 legible.");
            }

            var destino = Path.Combine(_carpeta, NombreSeguro(version.Etiqueta));
            Directory.CreateDirectory(destino);
            var nombre = NombreSeguro(version.Instalador.Nombre);
            var final = Path.Combine(destino, nombre);
            parcial = final + ".parcial";

            using var respuesta = await cliente.GetAsync(
                version.Instalador.Direccion, HttpCompletionOption.ResponseHeadersRead, cancelacion).ConfigureAwait(false);
            respuesta.EnsureSuccessStatusCode();

            var total = respuesta.Content.Headers.ContentLength ?? version.Instalador.Bytes;
            if (total > TamanoMaximo)
            {
                return new ResultadoDeDescarga(EstadoDeDescarga.Fallida, Motivo: "El instalador anunciado es demasiado grande.");
            }

            string obtenida;
            await using (var origen = await respuesta.Content.ReadAsStreamAsync(cancelacion).ConfigureAwait(false))
            await using (var fichero = new FileStream(parcial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var bufer = new byte[81920];
                long leidos = 0;
                int n;
                while ((n = await origen.ReadAsync(bufer, cancelacion).ConfigureAwait(false)) > 0)
                {
                    leidos += n;
                    if (leidos > TamanoMaximo) throw new IOException("La descarga supera el tamaño máximo.");
                    sha.AppendData(bufer, 0, n);
                    await fichero.WriteAsync(bufer.AsMemory(0, n), cancelacion).ConfigureAwait(false);
                    if (total > 0) progreso?.Report(Math.Min(1.0, (double)leidos / total));
                }

                obtenida = Convert.ToHexString(sha.GetHashAndReset());
            }

            if (!string.Equals(obtenida, esperada, StringComparison.OrdinalIgnoreCase))
            {
                BorrarSinFallar(parcial);
                _log.LogWarning(
                    "La suma SHA-256 del instalador {Nombre} no casa: esperada {Esperada}, obtenida {Obtenida}. No se instala.",
                    nombre, esperada, obtenida);
                return new ResultadoDeDescarga(EstadoDeDescarga.SumaNoCasa,
                    Motivo: "El instalador descargado no coincide con la suma SHA-256 publicada. Se ha borrado y no se instalará.");
            }

            if (File.Exists(final)) File.Delete(final);
            File.Move(parcial, final);
            parcial = null;
            progreso?.Report(1.0);
            _log.LogInformation("Instalador {Nombre} descargado y verificado en {Ruta}.", nombre, final);
            return new ResultadoDeDescarga(EstadoDeDescarga.Verificado, final);
        }
        catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
        {
            if (parcial is not null) BorrarSinFallar(parcial);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or UnauthorizedAccessException)
        {
            if (parcial is not null) BorrarSinFallar(parcial);
            _log.LogWarning(ex, "No se ha podido descargar el instalador.");
            return new ResultadoDeDescarga(EstadoDeDescarga.Fallida,
                Motivo: "No se ha podido descargar el instalador. Compruebe la conexión e inténtelo más tarde.");
        }
    }

    /// <summary>
    /// Lee la suma de un fichero <c>.sha256</c>: vale el formato de <c>sha256sum</c>
    /// (<c>suma  nombre</c>), el de <c>Get-FileHash</c> suelto o solo la suma.
    /// </summary>
    /// <param name="texto">Contenido del fichero.</param>
    /// <returns>La suma en hexadecimal mayusculas, o nula si no hay ninguna.</returns>
    public static string? LeerSuma(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var encontrada = SumaHex().Match(texto);
        return encontrada.Success ? encontrada.Value.ToUpperInvariant() : null;
    }

    // Nunca se usa el nombre que venga de fuera como ruta: sin separadores ni «..».
    private static string NombreSeguro(string nombre)
    {
        var limpio = new string(nombre.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray())
            .Replace("..", "_", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(limpio) ? "instalador" : limpio;
    }

    private void BorrarSinFallar(string ruta)
    {
        try
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogDebug(ex, "No se ha podido borrar {Ruta}.", ruta);
        }
    }

    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{64}(?![0-9A-Fa-f])")]
    private static partial Regex SumaHex();
}
