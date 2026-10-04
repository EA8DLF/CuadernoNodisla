using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Audio.Captura;

/// <summary>
/// Envuelve una <see cref="IEntradaDeAudio"/> real y la comparte por recuento de referencias.
/// </summary>
/// <remarks>
/// <para>
/// El codec del equipo lo abren y lo cierran tres cosas que no se conocen entre sí: la telegrafía,
/// el módem propio y la fonía, todas sobre la misma instancia (<c>AddSingleton</c>). Sin esto, cada
/// una que llama a <see cref="CerrarAsync"/> cierra la tarjeta para las demás, y cada una que deja
/// de necesitarla (p. ej. al pulsar «Pausa» en CW) no puede cerrarla porque podría estar en uso —
/// así que, por diseño, nadie la cerraba nunca y el hilo de captura de Windows seguía corriendo
/// para siempre aunque no hubiera un solo oyente enganchado. Era justo lo que no se podía apagar
/// de verdad.
/// </para>
/// <para>
/// Con esto, <see cref="AbrirAsync"/> cuenta cuántos la quieren abierta y solo abre la tarjeta de
/// verdad al pasar de cero a uno; <see cref="CerrarAsync"/> solo la cierra de verdad al volver a
/// cero. Si alguien la pide con un dispositivo o una frecuencia distintos de los que ya están
/// abiertos para otro, se reabre con lo nuevo y el recuento vuelve a uno: no hay manera de servir
/// dos tarjetas a la vez con una sola instancia, así que se prioriza al que pide ahora.
/// </para>
/// </remarks>
public sealed class EntradaDeAudioCompartida : IEntradaDeAudio
{
    private readonly IEntradaDeAudio _interior;
    private readonly SemaphoreSlim _cerrojo = new(1, 1);
    private int _referencias;
    private string? _idAbierto;
    private int _frecuenciaAbierta;

    /// <summary>Envuelve la entrada real.</summary>
    /// <param name="interior">La implementación de verdad (WASAPI, o de mentira en las pruebas).</param>
    public EntradaDeAudioCompartida(IEntradaDeAudio interior)
    {
        ArgumentNullException.ThrowIfNull(interior);
        _interior = interior;
    }

    /// <summary>Cuántos la tienen pedida ahora mismo (para diagnóstico y pruebas).</summary>
    public int Referencias => Volatile.Read(ref _referencias);

    /// <inheritdoc />
    public IReadOnlyList<DispositivoDeAudio> Dispositivos => _interior.Dispositivos;

    /// <inheritdoc />
    public DispositivoDeAudio? Abierto => _interior.Abierto;

    /// <inheritdoc />
    public double Nivel => _interior.Nivel;

    /// <inheritdoc />
    public event EventHandler<BloqueDeAudio>? BloqueCapturado
    {
        add => _interior.BloqueCapturado += value;
        remove => _interior.BloqueCapturado -= value;
    }

    /// <inheritdoc />
    public event EventHandler<HuecoDeAudio>? MuestrasPerdidas
    {
        add => _interior.MuestrasPerdidas += value;
        remove => _interior.MuestrasPerdidas -= value;
    }

    /// <summary>Vuelve a mirar qué dispositivos hay, si la implementación real sabe hacerlo.</summary>
    public void Refrescar()
    {
        if (_interior is EntradaDeAudioWasapi real) real.Refrescar();
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
                // Ya está abierta con lo mismo que se pide: un oyente más, sin tocar la tarjeta.
                _referencias++;
                return;
            }

            await _interior.AbrirAsync(idDispositivo, frecuenciaDeMuestreo, ct).ConfigureAwait(false);

            // Solo se cuenta si de verdad se ha abierto: un fallo de aquí para arriba no deja
            // ninguna referencia fantasma que luego nadie suelte.
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
            if (_referencias <= 0) return; // nadie la tenía pedida: nada que soltar

            _referencias--;
            if (_referencias > 0) return; // todavía la quiere alguien más

            _idAbierto = null;
            await _interior.CerrarAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _cerrojo.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // El cierre de la aplicación no negocia con el recuento: se suelta pase lo que pase.
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
