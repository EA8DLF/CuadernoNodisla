using System.Data.Common;

namespace Nodisla.Cuaderno.Datos;

/// <summary>
/// Los ajustes que toda conexion al cuaderno tiene que llevar puestos.
/// </summary>
/// <remarks>
/// Estan aqui, en un solo sitio, porque el cuaderno se abre desde dos caminos: EF Core, que
/// pasa por <see cref="Interceptores.InterceptorDePragmas"/>, y las conexiones sueltas que
/// reparte <see cref="FabricaDeConexion"/> para el SQL escrito a mano. Si cada camino pusiera
/// los suyos, bastaria olvidarse de <c>foreign_keys</c> en uno para que el borrado en cascada
/// dejara filas huerfanas solo a veces, que es el peor fallo posible: el que no se ve.
/// </remarks>
public static class AjustesDeSqlite
{
    /// <summary>Ordenes que se ejecutan nada mas abrir una conexion.</summary>
    public const string Sql =
        "PRAGMA journal_mode=WAL;" +
        "PRAGMA foreign_keys=ON;" +
        "PRAGMA busy_timeout=5000;" +
        "PRAGMA synchronous=NORMAL;";

    /// <summary>Aplica los ajustes a una conexion ya abierta.</summary>
    /// <param name="conexion">Conexion abierta.</param>
    public static void Aplicar(DbConnection conexion)
    {
        ArgumentNullException.ThrowIfNull(conexion);
        using var orden = conexion.CreateCommand();
        orden.CommandText = Sql;
        orden.ExecuteNonQuery();
    }

    /// <summary>Aplica los ajustes a una conexion ya abierta.</summary>
    /// <param name="conexion">Conexion abierta.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public static async Task AplicarAsync(DbConnection conexion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(conexion);
        await using var orden = conexion.CreateCommand();
        orden.CommandText = Sql;
        await orden.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
