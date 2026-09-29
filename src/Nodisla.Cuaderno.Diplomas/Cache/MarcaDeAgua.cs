using System.Data.Common;
using Dapper;

namespace Nodisla.Cuaderno.Diplomas.Cache;

/// <summary>
/// Foto barata del estado del cuaderno, para saber si lo cacheado sigue valiendo.
/// </summary>
/// <remarks>
/// <para>
/// La invalidacion de verdad es el aviso explicito: la aplicacion dice cuando entra un contacto
/// o cuando llegan confirmaciones. La marca de agua es la red por debajo, para los cambios que
/// vienen de fuera (otro proceso, una restauracion de copia, una importacion desde la linea de
/// ordenes). Sin ella, una cache podria quedarse vieja en un diploma sin que nadie se entere, y
/// eso es peor que no tener cache.
/// </para>
/// <para>
/// Tiene que ser <b>muy</b> barata, porque se comprueba en el camino del aviso instantaneo. Por
/// eso no cuenta filas: usa <c>PRAGMA data_version</c>, que SQLite incrementa en cuanto otra
/// conexion confirma cualquier cambio en el fichero, y el maximo identificador de cada tabla
/// hija, que sale del indice de la clave primaria sin recorrer nada. Contar los contactos de un
/// cuaderno de 50.000 costaba 37 ms; esto cuesta microsegundos.
/// </para>
/// </remarks>
/// <param name="VersionDeDatos">Contador que SQLite mueve cuando otra conexion escribe.</param>
/// <param name="UltimoContacto">Mayor identificador de <c>qso</c>.</param>
/// <param name="UltimaConfirmacion">Mayor identificador de <c>qso_confirmacion</c>.</param>
/// <param name="UltimaReferencia">Mayor identificador de <c>qso_referencia</c>.</param>
public sealed record MarcaDeAgua(
    long VersionDeDatos,
    long UltimoContacto,
    long UltimaConfirmacion,
    long UltimaReferencia)
{
    /// <summary>Marca de un cuaderno que todavia no se ha leido.</summary>
    public static MarcaDeAgua Ninguna { get; } = new(-1, -1, -1, -1);

    private const string Sql = """
        SELECT (SELECT data_version FROM pragma_data_version())     AS VersionDeDatos,
               (SELECT COALESCE(MAX(id), 0) FROM qso)               AS UltimoContacto,
               (SELECT COALESCE(MAX(id), 0) FROM qso_confirmacion)  AS UltimaConfirmacion,
               (SELECT COALESCE(MAX(id), 0) FROM qso_referencia)    AS UltimaReferencia
        """;

    /// <summary>Lee la marca de agua del cuaderno.</summary>
    /// <param name="conexion">Conexion abierta al cuaderno.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La marca leida.</returns>
    public static async Task<MarcaDeAgua> LeerAsync(DbConnection conexion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conexion);

        var fila = await conexion.QuerySingleAsync<Fila>(
            new CommandDefinition(Sql, cancellationToken: ct)).ConfigureAwait(false);

        return new MarcaDeAgua(
            fila.VersionDeDatos, fila.UltimoContacto, fila.UltimaConfirmacion, fila.UltimaReferencia);
    }

    private sealed class Fila
    {
        public long VersionDeDatos { get; init; }
        public long UltimoContacto { get; init; }
        public long UltimaConfirmacion { get; init; }
        public long UltimaReferencia { get; init; }
    }
}
