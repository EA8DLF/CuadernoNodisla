using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Nodisla.Cuaderno.Datos;

/// <summary>
/// Pone la base de datos al dia, copiandola antes por si algo sale mal.
/// </summary>
/// <remarks>
/// Perder el cuaderno es inaceptable: son anos de contactos que no se pueden rehacer. Por eso
/// ninguna migracion se aplica sin una copia previa del fichero, y la copia se hace con
/// <c>VACUUM INTO</c>, que produce un fichero consistente aunque haya un WAL a medio volcar.
/// </remarks>
/// <param name="contexto">Contexto del cuaderno.</param>
/// <param name="opciones">Rutas del cuaderno y de las copias.</param>
/// <param name="registro">Registro opcional para dejar constancia de lo que se hace.</param>
public sealed class MigradorDeCuaderno(
    ContextoCuaderno contexto,
    OpcionesCuaderno opciones,
    ILogger<MigradorDeCuaderno>? registro = null)
{
    /// <summary>
    /// Aplica las migraciones pendientes. Si hay alguna y la base ya existia, antes hace una
    /// copia de seguridad.
    /// </summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Ruta de la copia de seguridad, o nulo si no hizo falta ninguna.</returns>
    public async Task<string?> AplicarMigracionesAsync(CancellationToken ct = default)
    {
        // SQLite crea el fichero, pero no la carpeta que lo contiene.
        var carpetaDelCuaderno = Path.GetDirectoryName(Path.GetFullPath(opciones.Ruta));
        if (!string.IsNullOrEmpty(carpetaDelCuaderno))
        {
            Directory.CreateDirectory(carpetaDelCuaderno);
        }

        var pendientes = (await contexto.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false)).ToList();
        if (pendientes.Count == 0)
        {
            return null;
        }

        string? copia = null;
        if (File.Exists(opciones.Ruta) && new FileInfo(opciones.Ruta).Length > 0)
        {
            copia = await CrearCopiaAsync(ct).ConfigureAwait(false);
            registro?.LogInformation(
                "Copia de seguridad del cuaderno antes de migrar: {Copia}", copia);
        }

        registro?.LogInformation(
            "Aplicando {Cuantas} migracion(es) al cuaderno: {Migraciones}",
            pendientes.Count,
            string.Join(", ", pendientes));

        await contexto.Database.MigrateAsync(ct).ConfigureAwait(false);
        return copia;
    }

    /// <summary>
    /// Copia el cuaderno a la carpeta de copias con la fecha en el nombre y va borrando las
    /// copias mas antiguas.
    /// </summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Ruta del fichero de copia creado.</returns>
    public Task<string> CrearCopiaAsync(CancellationToken ct = default) =>
        CrearCopiaAsync(DateTime.UtcNow, ct);

    /// <summary>
    /// Hace una copia del cuaderno si la ultima tiene mas de <paramref name="cada"/>. Es la
    /// copia periodica: se llama al arrancar y, como mucho, deja una al dia.
    /// </summary>
    /// <remarks>
    /// Usa el mismo <c>VACUUM INTO</c> y la misma poda que la copia previa a una migracion, asi
    /// que vale con el cuaderno abierto y en uso. La edad de la ultima copia se saca de la
    /// fecha que lleva en el nombre, no de la del fichero: una carpeta de copias restaurada
    /// desde otro disco trae las fechas de fichero cambiadas.
    /// </remarks>
    /// <param name="cada">Edad a partir de la cual toca otra copia.</param>
    /// <param name="ahoraUtc">Hora actual en UTC; sustituible en las pruebas.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Ruta de la copia creada, o nulo si no tocaba o no hay cuaderno que copiar.</returns>
    public async Task<string?> CrearCopiaSiHaceFaltaAsync(
        TimeSpan cada, DateTime? ahoraUtc = null, CancellationToken ct = default)
    {
        if (!File.Exists(opciones.Ruta) || new FileInfo(opciones.Ruta).Length == 0)
        {
            return null;
        }

        var ahora = ahoraUtc ?? DateTime.UtcNow;
        if (UltimaCopiaUtc() is { } ultima && ahora - ultima < cada)
        {
            return null;
        }

        return await CrearCopiaAsync(ahora, ct).ConfigureAwait(false);
    }

    /// <summary>Cuando se hizo la copia mas reciente, o nulo si no hay ninguna.</summary>
    public DateTime? UltimaCopiaUtc()
    {
        var carpeta = opciones.CarpetaDeCopiasEfectiva;
        if (!Directory.Exists(carpeta))
        {
            return null;
        }

        var prefijo = Path.GetFileNameWithoutExtension(opciones.Ruta) + "-";
        DateTime? ultima = null;
        foreach (var copia in new DirectoryInfo(carpeta).GetFiles(prefijo + "*" + Path.GetExtension(opciones.Ruta)))
        {
            // cuaderno-20261115-123907.sqlite o cuaderno-20261115-123907-1.sqlite
            var resto = Path.GetFileNameWithoutExtension(copia.Name)[prefijo.Length..];
            var cuando = resto.Length >= 15
                && DateTime.TryParseExact(
                    resto[..15],
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var delNombre)
                ? delNombre
                : copia.CreationTimeUtc;

            if (ultima is null || cuando > ultima)
            {
                ultima = cuando;
            }
        }

        return ultima;
    }

    private async Task<string> CrearCopiaAsync(DateTime ahoraUtc, CancellationToken ct)
    {
        var carpeta = opciones.CarpetaDeCopiasEfectiva;
        Directory.CreateDirectory(carpeta);

        var marca = ahoraUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var destino = Path.Combine(
            carpeta,
            $"{Path.GetFileNameWithoutExtension(opciones.Ruta)}-{marca}{Path.GetExtension(opciones.Ruta)}");

        var n = 1;
        while (File.Exists(destino))
        {
            destino = Path.Combine(
                carpeta,
                $"{Path.GetFileNameWithoutExtension(opciones.Ruta)}-{marca}-{n++}{Path.GetExtension(opciones.Ruta)}");
        }

        var conexion = (SqliteConnection)contexto.Database.GetDbConnection();
        var habiaQueAbrir = conexion.State != System.Data.ConnectionState.Open;
        if (habiaQueAbrir)
        {
            await conexion.OpenAsync(ct).ConfigureAwait(false);
        }

        try
        {
            // VACUUM INTO da una copia consistente aunque haya escrituras en el WAL.
            await using var orden = conexion.CreateCommand();
            orden.CommandText = "VACUUM INTO $destino";
            orden.Parameters.AddWithValue("$destino", destino);
            await orden.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            if (habiaQueAbrir)
            {
                await conexion.CloseAsync().ConfigureAwait(false);
            }
        }

        PodarCopias(carpeta);
        return destino;
    }

    /// <summary>Deja solo las copias mas recientes, segun <see cref="OpcionesCuaderno.CopiasAConservar"/>.</summary>
    private void PodarCopias(string carpeta)
    {
        if (opciones.CopiasAConservar <= 0)
        {
            return;
        }

        var patron = $"{Path.GetFileNameWithoutExtension(opciones.Ruta)}-*{Path.GetExtension(opciones.Ruta)}";
        var copias = new DirectoryInfo(carpeta)
            .GetFiles(patron)
            .OrderByDescending(f => f.CreationTimeUtc)
            .Skip(opciones.CopiasAConservar)
            .ToList();

        foreach (var vieja in copias)
        {
            try
            {
                vieja.Delete();
            }
            catch (IOException error)
            {
                registro?.LogWarning(error, "No se pudo borrar la copia antigua {Copia}", vieja.FullName);
            }
            catch (UnauthorizedAccessException error)
            {
                registro?.LogWarning(error, "Sin permiso para borrar la copia antigua {Copia}", vieja.FullName);
            }
        }
    }
}
