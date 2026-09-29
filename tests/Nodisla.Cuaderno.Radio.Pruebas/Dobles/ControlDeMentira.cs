using System.Collections.Concurrent;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Pruebas.Dobles;

/// <summary>
/// Equipo de mentira: cuenta las subidas y bajadas del PTT y sabe fallar a la orden.
/// </summary>
/// <remarks>
/// Aqui nunca se transmite de verdad. Todo lo que se prueba del vigilante se prueba contra
/// este doble, que ademas puede simular lo peor: que la via normal de bajar el PTT falle, que
/// fallen todas las vias, o que la orden se quede colgada.
/// </remarks>
internal sealed class ControlDeMentira : IControlEquipo, IPttDirecto, ISueltaDeEmergenciaPtt
{
    private readonly ConcurrentQueue<string> _ordenes = new();
    private int _subidas;
    private int _bajadas;
    private int _bajadasPorEmergencia;
    private int _bajadasSincronas;
    private int _pttArriba;

    internal ControlDeMentira()
    {
        ViasDeSuelta =
        [
            new ViaDeSuelta(
                "mentira: segunda vía",
                async ct =>
                {
                    if (FallanTodasLasVias)
                    {
                        throw new IOException("La segunda vía también falla.");
                    }

                    // La vía asíncrona sufre la misma tardanza que el canal: si el equipo está
                    // atascado, lo está para todo lo que no sea la vía síncrona.
                    if (Tardanza > TimeSpan.Zero)
                    {
                        await Task.Delay(Tardanza, ct).ConfigureAwait(false);
                    }

                    Interlocked.Increment(ref _bajadasPorEmergencia);
                    Bajar();
                },
                () =>
                {
                    if (FallanTodasLasVias)
                    {
                        throw new IOException("La vía síncrona también falla.");
                    }

                    Interlocked.Increment(ref _bajadasSincronas);
                    Bajar();
                }),
        ];
    }

    /// <summary>El PTT esta pedido ahora mismo.</summary>
    internal bool PttArriba => Volatile.Read(ref _pttArriba) != 0;

    /// <summary>Cuantas veces se ha subido el PTT.</summary>
    internal int Subidas => Volatile.Read(ref _subidas);

    /// <summary>Cuantas veces se ha bajado el PTT por la via normal.</summary>
    internal int Bajadas => Volatile.Read(ref _bajadas);

    /// <summary>Cuantas veces se ha bajado por una via de emergencia.</summary>
    internal int BajadasPorEmergencia => Volatile.Read(ref _bajadasPorEmergencia);

    /// <summary>Cuantas veces se ha bajado por la via sincrona del cierre.</summary>
    internal int BajadasSincronas => Volatile.Read(ref _bajadasSincronas);

    /// <summary>Ordenes recibidas, en orden.</summary>
    internal IReadOnlyCollection<string> Ordenes => _ordenes;

    /// <summary>Si la via normal de bajar el PTT falla.</summary>
    internal bool FallaLaViaNormal { get; set; }

    /// <summary>Si fallan tambien todas las vias de emergencia.</summary>
    internal bool FallanTodasLasVias { get; set; }

    /// <summary>Si subir el PTT falla.</summary>
    internal bool FallaAlSubir { get; set; }

    /// <summary>Lo que tarda cada orden, para simular un canal lento o colgado.</summary>
    internal TimeSpan Tardanza { get; set; } = TimeSpan.Zero;

    /// <inheritdoc />
    public ViaDeControl Via => ViaDeControl.Ninguna;

    /// <inheritdoc />
    public EstadoDelEquipo Estado { get; private set; } = EstadoDelEquipo.Desconectado;

    /// <inheritdoc />
    public IReadOnlyList<ViaDeSuelta> ViasDeSuelta { get; }

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default)
    {
        Estado = Estado with { Conectado = true };
        EstadoCambiado?.Invoke(this, Estado);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default)
    {
        Estado = Estado with { Conectado = false };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        Estado = Estado with { Frecuencia = frecuencia };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default)
    {
        Estado = Estado with { Modo = modo };
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    Task IControlEquipo.PonerPttAsync(bool transmitir, CancellationToken ct) =>
        GuardiaDelPtt.Rechazar(transmitir, () => ((IPttDirecto)this).PonerPttDirectoAsync(false, ct));

    /// <inheritdoc />
    async Task IPttDirecto.PonerPttDirectoAsync(bool transmitir, CancellationToken ct)
    {
        _ordenes.Enqueue(transmitir ? "ptt=1" : "ptt=0");

        if (Tardanza > TimeSpan.Zero)
        {
            await Task.Delay(Tardanza, ct).ConfigureAwait(false);
        }

        if (transmitir)
        {
            if (FallaAlSubir)
            {
                throw new IOException("El equipo de mentira no deja subir el PTT.");
            }

            Interlocked.Increment(ref _subidas);
            Volatile.Write(ref _pttArriba, 1);
            return;
        }

        if (FallaLaViaNormal)
        {
            throw new IOException("La vía normal de bajar el PTT está rota.");
        }

        Interlocked.Increment(ref _bajadas);
        Bajar();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Bajar() => Volatile.Write(ref _pttArriba, 0);
}
