using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;

namespace Nodisla.Cuaderno.Radio.Pruebas.Dobles;

/// <summary>
/// Espera a que el control avise de algo, en vez de mirar el reloj a ver si ya ha pasado.
/// </summary>
/// <remarks>
/// El plazo que se le pasa a estos metodos <b>no mide nada</b>: es una red por si la señal no
/// llega nunca. Si una prueba usa un plazo como medida —«en cuatro segundos ya deberia haber
/// reconectado»—, lo que acaba midiendo es lo cargada que esta la maquina, y falla los dias que
/// hay once proyectos compilando a la vez.
/// </remarks>
internal sealed class EsperaDeSenales
{
    /// <summary>Plazo de seguridad: aqui no se mide, solo se evita esperar para siempre.</summary>
    internal static readonly TimeSpan PlazoDeSeguridad = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource<string> _perdida =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<string> _recuperada =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly List<TaskCompletionSource<EstadoDelEquipo>> _esperasDeEstado = [];
    private readonly List<Func<EstadoDelEquipo, bool>> _condiciones = [];
    private readonly object _candado = new();

    /// <summary>Se engancha a los avisos de un control.</summary>
    /// <param name="control">Control al que escuchar.</param>
    internal EsperaDeSenales(IControlEquipo control)
    {
        control.EstadoCambiado += (_, estado) => AlCambiarElEstado(estado);

        if (control is IAvisaDePerdidaDeComunicacion avisador)
        {
            avisador.ComunicacionPerdida += (_, porque) => _perdida.TrySetResult(porque);
        }

        if (control is ControlFt710 ft710)
        {
            ft710.ComunicacionRecuperada += (_, canal) => _recuperada.TrySetResult(canal);
        }
    }

    /// <summary>Espera a que el control avise de que ha perdido el equipo.</summary>
    /// <param name="plazo">Red de seguridad; si es nulo, se usa el plazo de siempre.</param>
    /// <returns>Por que se perdio.</returns>
    internal Task<string> PerdidaAsync(TimeSpan? plazo = null) =>
        _perdida.Task.WaitAsync(plazo ?? PlazoDeSeguridad);

    /// <summary>Espera a que el control avise de que el equipo ha vuelto.</summary>
    /// <param name="plazo">Red de seguridad; si es nulo, se usa el plazo de siempre.</param>
    /// <returns>El canal por el que volvio.</returns>
    internal Task<string> RecuperadaAsync(TimeSpan? plazo = null) =>
        _recuperada.Task.WaitAsync(plazo ?? PlazoDeSeguridad);

    /// <summary>Espera a que el estado del equipo cumpla una condicion.</summary>
    /// <param name="condicion">Lo que tiene que cumplirse.</param>
    /// <param name="estadoDeAhora">Estado actual, por si ya se cumple.</param>
    /// <param name="plazo">Red de seguridad; si es nulo, se usa el plazo de siempre.</param>
    /// <returns>El estado que cumplio la condicion.</returns>
    internal Task<EstadoDelEquipo> EstadoAsync(
        Func<EstadoDelEquipo, bool> condicion,
        EstadoDelEquipo estadoDeAhora,
        TimeSpan? plazo = null)
    {
        if (condicion(estadoDeAhora))
        {
            return Task.FromResult(estadoDeAhora);
        }

        var espera = new TaskCompletionSource<EstadoDelEquipo>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_candado)
        {
            _esperasDeEstado.Add(espera);
            _condiciones.Add(condicion);
        }

        return espera.Task.WaitAsync(plazo ?? PlazoDeSeguridad);
    }

    private void AlCambiarElEstado(EstadoDelEquipo estado)
    {
        lock (_candado)
        {
            for (var i = _esperasDeEstado.Count - 1; i >= 0; i--)
            {
                if (!_condiciones[i](estado))
                {
                    continue;
                }

                _esperasDeEstado[i].TrySetResult(estado);
                _esperasDeEstado.RemoveAt(i);
                _condiciones.RemoveAt(i);
            }
        }
    }
}
