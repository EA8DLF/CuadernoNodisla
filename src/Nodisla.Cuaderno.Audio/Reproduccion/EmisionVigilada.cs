using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Audio.Reproduccion;

/// <summary>
/// El unico camino correcto para sacar audio por la antena.
/// </summary>
/// <remarks>
/// <para>
/// Reproducir <b>es</b> transmitir. Una emision de FT8 son trece segundos con el equipo en
/// antena, asi que aqui se hace lo que manda el puerto: se pide la transmision al vigilante del
/// PTT, se late mientras suena y se suelta al terminar, pase lo que pase.
/// </para>
/// <para>
/// El detalle que importa es <b>cuando se late</b>. Se late solo mientras la salida dice que el
/// audio avanza de verdad —<see cref="ISalidaDeAudio.UltimoAvanceUtc"/>—. Si la tarjeta se
/// cuelga con la cola llena, esta clase deja de latir y el vigilante baja el PTT por su cuenta;
/// latir a ciegas cada tantos milisegundos seria justo lo contrario: dejar el equipo en antena
/// mientras el programa se cree que va todo bien.
/// </para>
/// <para>
/// Si la salida no sabe decir si avanza, <b>no se late en absoluto</b>: se deja que el tiempo
/// sin latido del vigilante ponga el tope. Prefiere quedarse corto a inventarse una senal de
/// vida que no tiene.
/// </para>
/// </remarks>
public static class EmisionVigilada
{
    /// <summary>Cada cuanto se late mientras el audio avanza.</summary>
    public static readonly TimeSpan PasoDeLatidoPorOmision = TimeSpan.FromMilliseconds(250);

    /// <summary>Cuanto se tolera sin que el audio avance antes de dejar de latir.</summary>
    public static readonly TimeSpan ToleranciaSinAvancePorOmision = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Emite unas muestras poniendo el equipo en antena, y lo saca de antena al acabar.
    /// </summary>
    /// <param name="vigilante">El vigilante del PTT, unico camino para transmitir.</param>
    /// <param name="salida">Salida de audio por donde suena.</param>
    /// <param name="muestras">Muestras a emitir, un canal, coma flotante.</param>
    /// <param name="motivo">Para que se transmite, para el registro.</param>
    /// <param name="pasoDeLatido">Cada cuanto se late; si es nulo, 250 ms.</param>
    /// <param name="toleranciaSinAvance">
    /// Cuanto se tolera sin que el audio avance antes de dejar de latir; si es nulo, un segundo.
    /// </param>
    /// <param name="registro">Donde se anota lo que pasa; si es nulo, no se anota nada.</param>
    /// <param name="ct">Testigo de cancelacion; al cancelarse, se corta y se suelta el PTT.</param>
    /// <returns>Cuando ha terminado de sonar y el PTT esta abajo.</returns>
    /// <exception cref="ArgumentNullException">Si falta el vigilante o la salida.</exception>
    public static async Task EmitirAsync(
        IVigilantePtt vigilante,
        ISalidaDeAudio salida,
        ReadOnlyMemory<float> muestras,
        string motivo,
        TimeSpan? pasoDeLatido = null,
        TimeSpan? toleranciaSinAvance = null,
        ILogger? registro = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(vigilante);
        ArgumentNullException.ThrowIfNull(salida);

        var anotador = registro ?? NullLogger.Instance;
        var paso = pasoDeLatido is { } p && p > TimeSpan.Zero ? p : PasoDeLatidoPorOmision;
        var tolerancia = toleranciaSinAvance is { } t && t > TimeSpan.Zero
            ? t
            : ToleranciaSinAvancePorOmision;

        await using var transmision = await vigilante.PedirAntenaAsync(motivo, ct).ConfigureAwait(false);

        using var finDelLatido = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var latido = LatirAsync(transmision, salida, paso, tolerancia, anotador, finDelLatido.Token);

        try
        {
            await salida.ReproducirAsync(muestras, ct).ConfigureAwait(false);
        }
        finally
        {
            await finDelLatido.CancelAsync().ConfigureAwait(false);

            try
            {
                await latido.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Se esperaba: se estaba terminando.
            }

            try
            {
                await salida.SilenciarAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception fallo)
            {
                anotador.LogWarning(fallo, "Algo falló al silenciar la salida después de emitir.");
            }
        }
    }

    /// <summary>Late mientras el audio avance; si deja de avanzar, deja de latir.</summary>
    private static async Task LatirAsync(
        ITransmisionEnCurso transmision,
        ISalidaDeAudio salida,
        TimeSpan paso,
        TimeSpan tolerancia,
        ILogger registro,
        CancellationToken ct)
    {
        if (salida.UltimoAvanceUtc is null)
        {
            registro.LogWarning(
                "La salida de audio no dice si el sonido avanza, así que no se late: " +
                "el tope de la transmisión lo pone el tiempo sin latido del vigilante.");
            return;
        }

        var avisado = false;

        try
        {
            while (!ct.IsCancellationRequested && transmision.EnAntena)
            {
                if (salida.UltimoAvanceUtc is not { } ultimoAvance)
                {
                    // Ha dejado de saberse si el audio avanza —por ejemplo, porque se cerro el
                    // dispositivo a media emision—. A ciegas no se late.
                    registro.LogError(
                        "La salida de audio ha dejado de decir si el sonido avanza: se deja de latir " +
                        "para que el vigilante baje el PTT.");
                    return;
                }

                var desdeElUltimoAvance = DateTimeOffset.UtcNow - ultimoAvance;

                if (desdeElUltimoAvance > tolerancia)
                {
                    if (!avisado)
                    {
                        avisado = true;
                        registro.LogError(
                            "El audio lleva {Segundos:F1} s sin avanzar: se deja de latir para que el " +
                            "vigilante baje el PTT.",
                            desdeElUltimoAvance.TotalSeconds);
                    }
                }
                else
                {
                    avisado = false;
                    transmision.Latir();
                }

                await Task.Delay(paso, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Se esperaba: se estaba terminando.
        }
    }
}
