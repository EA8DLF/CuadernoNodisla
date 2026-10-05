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

    /// <summary>
    /// Un único cerrojo para abrir, cerrar y reproducir.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Comprobado el 05-10-2026: CW, RTTY y el módem propio comparten esta misma instancia sin
    /// conocerse entre sí, pero <see cref="ReproducirAsync"/> llamaba a la de verdad tal cual,
    /// sin ningún turno. Si dos de ellos emitían a la vez —por ejemplo, al cambiar de pestaña con
    /// una transmisión todavía apurando el colchón de la tarjeta—, sus muestras se intercalaban
    /// en el mismo <c>BufferedWaveProvider</c>: uno limpiaba el búfer del otro a medio mensaje y
    /// las dos emisiones salían mezcladas, que es justo el «pum pum» en vez de un tono limpio que
    /// se oyó en la antena. Eso se arregló dándole a <see cref="ReproducirAsync"/> un cerrojo
    /// propio para que una emisión entera (de principio a fin, con su silencio final incluido)
    /// tuviera que acabar antes de que empezara la siguiente.
    /// </para>
    /// <para>
    /// <b>Pero ese cerrojo era uno aparte del de <see cref="AbrirAsync"/>/<see cref="CerrarAsync"/>,
    /// y ahí seguía el fallo de verdad</b>: nada impedía que, mientras el módem propio estaba en
    /// medio de una reproducción de verdad (<see cref="ReproducirAsync"/>, en marcha trece
    /// segundos en FT8), RTTY abriera o cerrara la salida compartida alrededor de su propia
    /// transmisión (ver <c>EmisorRtty.TransmitirAsync</c>, que abre justo antes de emitir y cierra
    /// justo después) y, si ese cierre hacía bajar el recuento a cero, parase y desechara de
    /// verdad el <c>WasapiOut</c> de debajo mientras el módem propio le seguía metiendo muestras:
    /// el mismo «pum pum» que 0ef66f5 creyó resuelto, con CW y el módem propio nunca lo habían
    /// notado porque CW no toca esta salida —nada abría ni cerraba la tarjeta mientras el módem
    /// reproducía—, y apareció justo al llegar RTTY, el primero en abrir y cerrar esta salida
    /// alrededor de cada transmisión en vez de dejarla abierta de un tirón. Un único cerrojo para
    /// las tres operaciones lo cierra: abrir o cerrar de verdad ahora esperan a que cualquier
    /// reproducción en marcha termine antes de tocar el dispositivo.
    /// </para>
    /// <para>
    /// <see cref="SilenciarAsync"/> se queda fuera a propósito: tiene que poder cortar una emisión
    /// en marcha sin esperar a que termine ella sola (lo usa el vigilante del PTT para soltar la
    /// antena ya).
    /// </para>
    /// </remarks>
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

    /// <inheritdoc />
    public int FrecuenciaDeMuestreo => _interior.FrecuenciaDeMuestreo;

    /// <summary>Vuelve a mirar qué dispositivos hay, si la implementación real sabe hacerlo.</summary>
    public void Refrescar()
    {
        if (_interior is SalidaDeAudioWasapi real) real.Refrescar();
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Si ya está abierta para otro módulo con un dispositivo o una frecuencia distintos.
    /// </exception>
    /// <remarks>
    /// A diferencia de <see cref="Captura.EntradaDeAudioCompartida"/>, aquí NO se reabre con lo
    /// nuevo pisando el recuento de quien ya la tenía: esto emite, y reabrir de verdad mientras
    /// otro la cree abierta suena como un corte en antena, no como una ligera degradación de la
    /// escucha.
    /// </remarks>
    public async Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default)
    {
        await _cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_referencias > 0)
            {
                if (string.Equals(_idAbierto, idDispositivo, StringComparison.OrdinalIgnoreCase)
                    && _frecuenciaAbierta == frecuenciaDeMuestreo)
                {
                    _referencias++;
                    return;
                }

                // Antes esto reabría con los parámetros nuevos y pisaba _referencias a 1,
                // perdiendo sin avisar la referencia de quien ya la tenía (y, si ese otro estaba
                // reproduciendo, le paraba el dispositivo de debajo mientras sonaba). Mejor un
                // aviso claro que un «pum pum» en la antena.
                throw new InvalidOperationException(
                    $"La salida de audio ya está abierta con otro dispositivo o frecuencia distintos " +
                    $"(«{_idAbierto}» a {_frecuenciaAbierta} Hz); hay que cerrarla del todo antes de " +
                    "abrirla con otra combinación.");
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
    public async Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default)
    {
        await _cerrojo.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _interior.ReproducirAsync(muestras, ct).ConfigureAwait(false);
        }
        finally
        {
            _cerrojo.Release();
        }
    }

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
