using System.Data.Common;
using Microsoft.Data.Sqlite;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Datos;

/// <summary>
/// Reparte conexiones al cuaderno del operador, ya configuradas.
/// </summary>
/// <remarks>
/// Es el unico sitio de todo el programa que sabe como se abre el cuaderno: la cadena de
/// conexion sale de <see cref="OpcionesCuaderno"/> y los pragmas, de
/// <see cref="AjustesDeSqlite"/>. Quien consulte con SQL a mano —el motor de diplomas— pide
/// aqui y se lleva una conexion con WAL y claves ajenas puestas, sin tener que saber donde
/// vive el fichero.
/// <para>
/// La conexion que devuelve es <b>suya</b>: quien la pide la cierra. Es una conexion aparte
/// de la de EF Core, asi que no ve lo que haya escrito una transaccion todavia sin confirmar;
/// para leer dentro de una transaccion en curso hay que usar la conexion del contexto, que es
/// lo que hace <see cref="Informes.ConsultasDeInforme"/>.
/// </para>
/// </remarks>
/// <param name="opciones">Rutas del cuaderno.</param>
public sealed class FabricaDeConexion(OpcionesCuaderno opciones) : IFabricaDeConexion
{
    /// <inheritdoc/>
    public string RutaDelCuaderno => opciones.Ruta;

    /// <inheritdoc/>
    public async Task<DbConnection> AbrirAsync(CancellationToken ct = default)
    {
        var conexion = new SqliteConnection(opciones.CadenaDeConexion);
        try
        {
            await conexion.OpenAsync(ct).ConfigureAwait(false);
            await AjustesDeSqlite.AplicarAsync(conexion, ct).ConfigureAwait(false);
            return conexion;
        }
        catch
        {
            await conexion.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
