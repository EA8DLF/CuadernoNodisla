using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Control;

/// <summary>
/// Control sin equipo: la frecuencia y el modo los pone el operador a mano.
/// </summary>
/// <remarks>
/// Es la via de partida del programa. No hay nada que se pueda quedar en antena, pero se
/// comporta como los demas controles para que el vigilante del PTT funcione igual con o sin
/// equipo y el resto de la aplicacion no tenga que saber si hay radio conectada.
/// </remarks>
public sealed class ControlNulo : IControlEquipo, IPttDirecto, ISueltaDeEmergenciaPtt
{
    private readonly ILogger _registro;
    private readonly object _candado = new();
    private EstadoDelEquipo _estado;

    /// <summary>Crea el control sin equipo.</summary>
    /// <param name="registro">Donde se anota lo que hace el operador a mano.</param>
    public ControlNulo(ILogger? registro = null)
    {
        _registro = registro ?? NullLogger.Instance;
        _estado = EstadoDelEquipo.Desconectado with { LeidoUtc = DateTimeOffset.UtcNow };
        ViasDeSuelta = [new ViaDeSuelta("sin equipo", _ => Task.CompletedTask, () => { })];
    }

    /// <inheritdoc />
    public ViaDeControl Via => ViaDeControl.Ninguna;

    /// <inheritdoc />
    public EstadoDelEquipo Estado
    {
        get
        {
            lock (_candado)
            {
                return _estado;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ViaDeSuelta> ViasDeSuelta { get; }

    /// <inheritdoc />
    public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

    /// <inheritdoc />
    public Task ConectarAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Cambiar(estado => estado with { Conectado = true });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DesconectarAsync(CancellationToken ct = default)
    {
        Cambiar(estado => estado with { Conectado = false, Transmitiendo = false });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Cambiar(estado => estado with { Frecuencia = frecuencia });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Cambiar(estado => estado with { Modo = modo });
        return Task.CompletedTask;
    }

    /// <summary>
    /// No hay equipo al que subir el PTT; llamarlo por fuera del vigilante sigue siendo un error.
    /// </summary>
    /// <param name="transmitir">Verdadero para subir el PTT.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la orden.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task IControlEquipo.PonerPttAsync(bool transmitir, CancellationToken ct) =>
        GuardiaDelPtt.Rechazar(transmitir, () => ((IPttDirecto)this).PonerPttDirectoAsync(false, ct));

    /// <inheritdoc />
    Task IPttDirecto.PonerPttDirectoAsync(bool transmitir, CancellationToken ct)
    {
        Cambiar(estado => estado with { Transmitiendo = transmitir });
        if (transmitir)
        {
            _registro.LogDebug("PTT simulado arriba: no hay equipo conectado.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Cambiar(estado => estado with { Conectado = false, Transmitiendo = false });
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Apunta lo que el operador dice tener puesto cuando no hay equipo que preguntar.
    /// </summary>
    /// <param name="frecuencia">Frecuencia declarada por el operador.</param>
    /// <param name="modo">Modo declarado por el operador.</param>
    public void ApuntarAMano(Frecuencia frecuencia, Modo modo) =>
        Cambiar(estado => estado with { Frecuencia = frecuencia, Modo = modo });

    private void Cambiar(Func<EstadoDelEquipo, EstadoDelEquipo> cambio)
    {
        EstadoDelEquipo nuevo;
        lock (_candado)
        {
            nuevo = cambio(_estado) with { LeidoUtc = DateTimeOffset.UtcNow };
            _estado = nuevo;
        }

        EstadoCambiado?.Invoke(this, nuevo);
    }
}
