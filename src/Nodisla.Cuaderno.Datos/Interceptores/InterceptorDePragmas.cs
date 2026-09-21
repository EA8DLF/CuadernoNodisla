using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Nodisla.Cuaderno.Datos.Interceptores;

/// <summary>
/// Aplica los ajustes de SQLite en cuanto se abre la conexion: WAL, claves ajenas e
/// integridad razonable.
/// </summary>
/// <remarks>
/// <c>foreign_keys</c> es por conexion y SQLite lo trae apagado de fabrica, asi que sin esto
/// el borrado en cascada de confirmaciones y referencias no ocurriria. WAL permite leer
/// mientras se escribe, que es justo lo que hace el programa al importar un ADIF con la
/// ventana del cuaderno abierta. Como Dapper usa la misma conexion que EF Core, los ajustes
/// valen tambien para las consultas de informe.
/// </remarks>
public sealed class InterceptorDePragmas : DbConnectionInterceptor
{
    private const string Ajustes =
        "PRAGMA journal_mode=WAL;" +
        "PRAGMA foreign_keys=ON;" +
        "PRAGMA busy_timeout=5000;" +
        "PRAGMA synchronous=NORMAL;";

    /// <summary>Instancia compartida; el interceptor no guarda estado.</summary>
    public static InterceptorDePragmas Instancia { get; } = new();

    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(connection);
        using var orden = connection.CreateCommand();
        orden.CommandText = Ajustes;
        orden.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    /// <inheritdoc/>
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using (var orden = connection.CreateCommand())
        {
            orden.CommandText = Ajustes;
            await orden.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }
}
