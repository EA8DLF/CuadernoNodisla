using System.Data.Common;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>
/// De donde salen las conexiones al cuaderno.
/// </summary>
/// <remarks>
/// Existe para que los modulos que consultan el cuaderno con SQL a mano —los informes, el motor
/// de diplomas— no tengan que conocer la cadena de conexion ni construirla por su cuenta. Que
/// cada uno abriera la suya significaria que un cambio de sitio del fichero, o de los pragmas,
/// hay que acordarse de hacerlo en varios lugares; y olvidarse en uno da fallos raros y tardios.
/// </remarks>
public interface IFabricaDeConexion
{
    /// <summary>Abre una conexion al cuaderno del operador, ya configurada.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task<DbConnection> AbrirAsync(CancellationToken ct = default);

    /// <summary>Ruta del fichero del cuaderno, para mostrarla y para las copias de seguridad.</summary>
    string RutaDelCuaderno { get; }
}

/// <summary>
/// Avisa al motor de diplomas de que el cuaderno ha cambiado.
/// </summary>
/// <remarks>
/// El motor tiene una marca de agua que detecta cambios por su cuenta, pero eso es la red de
/// seguridad, no el camino: comprobarla en cada consulta cuesta diez veces mas que no hacerlo.
/// Quien anada contactos o baje confirmaciones debe avisar por aqui.
/// </remarks>
public interface INotificadorDeDiplomas
{
    /// <summary>El cuaderno ha cambiado y el progreso guardado ya no vale.</summary>
    void CuadernoCambiado();
}
