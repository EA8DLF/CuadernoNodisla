using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Audio.Reproduccion;

/// <summary>
/// Envuelve una <see cref="ISalidaDeAudio"/> real y la comparte por recuento de referencias.
/// </summary>
/// <remarks>
/// Lo mismo que <see cref="Captura.EntradaDeAudioCompartida"/> pero para la salida: el módem propio
/// y la fonía abren y cierran la misma tarjeta de reproducción sin saber el uno del otro. Ver el
/// porqué en esa clase.
/// </remarks>
public sealed class SalidaDeAudioCompartida : ISalidaDeAudio
{
    private readonly ISalidaDeAudio _interior;
    private readonly SemaphoreSlim _cerrojo = new(1, 1);
    private int _referencias;
    private string? _idAbierto;
    private int _frecuenciaAbierta;

    /// <summary>Envuelve la salida real.</summary>
    /// <param name="interior">La implementación de verdad (WASAPI, o de mentira en las pruebas).</param>
    public SalidaDeAudioCompartida(ISalidaDeAudio interior)
    {
        ArgumentNullException.ThrowIfNull(interior);
        _interior = interior;
    }

    /// <summary>Cuántos la tienen pedida ahora mismo (para diagnóstico y pruebas).</summary>
    public int Referencias => Volatile.Read(ref _referencias);

    /// <inheritdoc />
    public DateTimeOffset? UltimoAvanceUtc => _interior.UltimoAvanceUtc;

    /// <inheritdoc />
    public IReadOnlyList<DispositivoDeAudio> Dispositivos => _interior.Dispositivos;

    /// <inheritdoc />
    public DispositivoDeAudio? Abierto => _interior.Abierto;

    /// <summary>Vuelve a mirar qué dispositivos hay, si la implementación real sabe hacerlo.</summary>
    public void Refrescar()
    {
        if (_interior is SalidaDeAudioWasapi real) real.Refrescar();
    }

    /// <inheritdoc />
    public async Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default)
    {
        await _cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_referencias > 0
                && string.Equals(_idAbierto, idDispositivo, StringComparison.OrdinalIgnoreCase)
                && _frecuenciaAbierta == frecuenciaDeMuestreo)
            {
                _referencias++;
                return;
            }

            await _interior.AbrirAsync(idDispositivo, frecuenciaDeMuestreo, ct).ConfigureAwait(false);

            _idAbierto = idDispositivo;
            _frecuenciaAbierta = frecuenciaDeMuestreo;
            _referencias = 1;
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    /// <inheritdoc />
    public async Task CerrarAsync(CancellationToken ct = default)
    {
        await _cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_referencias <= 0) return;

            _referencias--;
            if (_referencias > 0) return;

            _idAbierto = null;
            await _interior.CerrarAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    /// <inheritdoc />
    public Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default) =>
        _interior.ReproducirAsync(muestras, ct);

    /// <inheritdoc />
    public Task SilenciarAsync(CancellationToken ct = default) => _interior.SilenciarAsync(ct);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _cerrojo.WaitAsync().ConfigureAwait(false);
        try
        {
            _referencias = 0;
            _idAbierto = null;
        }
        finally
        {
            _cerrojo.Release();
        }

        await _interior.DisposeAsync().ConfigureAwait(false);
        _cerrojo.Dispose();
    }
}
