using Nodisla.Cuaderno.Audio.Fonia;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Camino de audio de mentira para trabajar la fonia sin tarjeta, sin microfono y sin radio.
/// </summary>
/// <remarks>No abre ningun dispositivo: los medidores se mueven con un vaiven inventado.</remarks>
public sealed class PuenteDeAudioSimulado : IPuenteDeAudio
{
    private readonly double _fase;

    /// <summary>Monta el camino.</summary>
    /// <param name="fase">Para que los dos medidores no se muevan igual.</param>
    public PuenteDeAudioSimulado(double fase = 0) => _fase = fase;

    /// <inheritdoc />
    public bool EstaAbierto { get; private set; }

    /// <inheritdoc />
    public float Ganancia { get; set; } = 1f;

    /// <inheritdoc />
    public bool Silenciado { get; set; }

    /// <inheritdoc />
    public double Nivel
    {
        get
        {
            if (!EstaAbierto) return 0;
            var t = DateTime.UtcNow.TimeOfDay.TotalSeconds + _fase;
            return Math.Clamp((0.35 + (0.25 * Math.Sin(t * 5.3)) + (0.1 * Math.Sin(t * 13.1))) * Ganancia, 0, 1);
        }
    }

    /// <inheritdoc />
    public bool Saturando => Nivel >= 0.99;

    /// <inheritdoc />
    public DateTimeOffset? UltimoAvanceUtc => EstaAbierto ? DateTimeOffset.UtcNow : null;

    /// <inheritdoc />
    public event EventHandler<Exception>? Fallo
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public Task AbrirAsync(string idEntrada, string idSalida, CancellationToken ct = default)
    {
        EstaAbierto = true;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task CerrarAsync()
    {
        EstaAbierto = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(CerrarAsync());
}
