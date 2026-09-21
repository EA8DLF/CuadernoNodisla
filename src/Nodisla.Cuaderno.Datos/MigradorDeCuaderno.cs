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
    public async Task<string> CrearCopiaAsync(CancellationToken ct = default)
    {
        var carpeta = opciones.CarpetaDeCopiasEfectiva;
        Directory.CreateDirectory(carpeta);

        var marca = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
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
