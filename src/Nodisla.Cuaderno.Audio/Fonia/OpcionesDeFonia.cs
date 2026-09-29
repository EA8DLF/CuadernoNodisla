namespace Nodisla.Cuaderno.Audio.Fonia;

/// <summary>Tiempos de la fonia por el ordenador.</summary>
public sealed class OpcionesDeFonia
{
    /// <summary>Tope absoluto de una pasada de fonia, por mucho que se configure.</summary>
    public static readonly TimeSpan TiempoMaximoPermitido = TimeSpan.FromMinutes(10);

    private TimeSpan _tiempoMaximo = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Tiempo maximo en antena de una pasada de fonia. Por omision, tres minutos.
    /// </summary>
    /// <remarks>
    /// Es obligatorio y se suma al del vigilante: manda el menor de los dos. En fonia se habla
    /// mas que en FT8, pero un PTT olvidado es igual de caro.
    /// </remarks>
    public TimeSpan TiempoMaximo
    {
        get => _tiempoMaximo;
        set
        {
            if (value <= TimeSpan.Zero || value > TiempoMaximoPermitido)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"El tiempo máximo de fonía debe estar entre cero y {TiempoMaximoPermitido}.");
            }

            _tiempoMaximo = value;
        }
    }

    /// <summary>Cuanto se tolera sin que el audio avance antes de dejar de latir.</summary>
    public TimeSpan ToleranciaSinAvance { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Cada cuanto se mira la transmision y se late.</summary>
    public TimeSpan PasoDeVigilancia { get; set; } = TimeSpan.FromMilliseconds(200);
}
