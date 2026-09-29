using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Radio.Control;

/// <summary>
/// Portero de <see cref="IControlEquipo.PonerPttAsync"/>.
/// </summary>
/// <remarks>
/// El contrato obliga a publicar <c>PonerPttAsync</c>, pero subir el PTT por ahi salta el
/// vigilante y es la unica forma de que el equipo se quede en antena. Los controles de este
/// ensamblado implementan el metodo de forma <b>explicita</b> —asi no aparece siquiera en el
/// tipo concreto— y lo pasan por este portero, que deja bajar el PTT siempre (bajarlo nunca
/// hace daño) y rechaza subirlo.
/// </remarks>
public static class GuardiaDelPtt
{
    /// <summary>Texto del rechazo, para poder reconocerlo en las pruebas.</summary>
    public const string MensajeDeRechazo =
        "Para transmitir hay que pasar por IVigilantePtt.PedirAntenaAsync: subir el PTT por "
        + "IControlEquipo.PonerPttAsync se salta el vigilante y puede dejar el equipo en antena.";

    /// <summary>
    /// Deja pasar la bajada del PTT y rechaza la subida.
    /// </summary>
    /// <param name="transmitir">Lo que pedia quien llamo.</param>
    /// <param name="bajar">Que hacer para bajar el PTT.</param>
    /// <returns>La tarea de la bajada.</returns>
    /// <exception cref="InvalidOperationException">Si se pedia subir el PTT.</exception>
    public static Task Rechazar(bool transmitir, Func<Task> bajar)
    {
        ArgumentNullException.ThrowIfNull(bajar);

        return transmitir
            ? throw new InvalidOperationException(MensajeDeRechazo)
            : bajar();
    }
}
