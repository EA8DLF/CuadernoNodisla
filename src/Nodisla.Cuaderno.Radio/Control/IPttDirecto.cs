using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Radio.Control;

/// <summary>
/// Camino reservado al vigilante para subir el PTT.
/// </summary>
/// <remarks>
/// Es <c>internal</c> a proposito: fuera de este ensamblado no hay forma comoda de subir el
/// PTT. Los controles de este ensamblado implementan
/// <see cref="IControlEquipo.PonerPttAsync"/> de forma explicita y ademas rechazan en tiempo
/// de ejecucion cualquier intento de subirlo por ahi; el unico que puede es
/// <see cref="Ptt.VigilantePtt"/>, que esta en este mismo ensamblado.
/// </remarks>
internal interface IPttDirecto
{
    /// <summary>Sube o baja el PTT sin preguntar. Solo lo llama el vigilante.</summary>
    /// <param name="transmitir">Verdadero para subir el PTT, falso para bajarlo.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la orden.</returns>
    Task PonerPttDirectoAsync(bool transmitir, CancellationToken ct);
}
