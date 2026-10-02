using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Audio.Procesado;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>Microfono de mentira para el voice keyer: no abre nada; «graba» una voz inventada.</summary>
public sealed class GrabadorDeMicrofonoSimulado : IGrabadorDeMicrofono
{
    private DateTimeOffset? _desde;

    /// <inheritdoc />
    public bool Grabando => _desde is not null;

    /// <inheritdoc />
    public double Nivel => Grabando ? 0.4 + (0.2 * Math.Sin(DateTime.UtcNow.TimeOfDay.TotalSeconds * 6)) : 0;

    /// <inheritdoc />
    public Task EmpezarAsync(string idMicrofono, CancellationToken ct = default)
    {
        _desde = DateTimeOffset.UtcNow;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<AudioEnMemoria?> PararAsync()
    {
        if (_desde is not { } desde) return Task.FromResult<AudioEnMemoria?>(null);
        _desde = null;

        const int F = 48000;
        var segundos = Math.Clamp((DateTimeOffset.UtcNow - desde).TotalSeconds, 0.5, 60);
        var m = new float[(int)(segundos * F)];
        for (var i = 0; i < m.Length; i++)
        {
            var t = (double)i / F;
            var silaba = Math.Max(0, Math.Sin(2 * Math.PI * 2.5 * t));
            m[i] = (float)(0.3 * silaba * Math.Sin(2 * Math.PI * 180 * t));
        }

        return Task.FromResult<AudioEnMemoria?>(new AudioEnMemoria(m, F));
    }
}

/// <summary>Altavoces de mentira: no suena nada, solo tarda lo que dura el audio.</summary>
public sealed class ReproductorLocalSimulado : IReproductorLocal
{
    private CancellationTokenSource? _corte;

    /// <inheritdoc />
    public bool Sonando => _corte is not null;

    /// <inheritdoc />
    public async Task ReproducirAsync(AudioEnMemoria audio, string idAltavoces, CancellationToken ct = default)
    {
        Parar();
        using var corte = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _corte = corte;
        try
        {
            await Task.Delay(audio.Duracion, corte.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cortado a proposito.
        }
        finally
        {
            if (ReferenceEquals(_corte, corte)) _corte = null;
        }
    }

    /// <inheritdoc />
    public void Parar() => _corte?.Cancel();
}
