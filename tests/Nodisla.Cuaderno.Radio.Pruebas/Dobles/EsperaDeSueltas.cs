using System.Collections.Concurrent;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Pruebas.Dobles;

/// <summary>
/// Se queda esperando a que el vigilante suelte el PTT, y lo apunta.
/// </summary>
/// <remarks>
/// Existe para que ninguna prueba espere «un rato a ver si pasa». El evento del vigilante salta
/// en otro hilo —en el hilo vigilante o en el de la suelta—, asi que hay dos cosas que hacer
/// bien: apuntarlo en algo que se pueda leer desde otro hilo sin sorpresas, y esperar a la
/// senal en vez de a un plazo. Una prueba de seguridad que depende de lo cargada que este la
/// maquina no vale para lo que esta.
/// </remarks>
internal sealed class EsperaDeSueltas
{
    private readonly ConcurrentQueue<MotivoDeSuelta> _motivos = new();
    private readonly TaskCompletionSource<MotivoDeSuelta> _primera =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly TaskCompletionSource<PttPegadoException> _pegado =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly ManualResetEventSlim _haSoltado = new(false);

    /// <summary>Se engancha a los avisos del vigilante.</summary>
    /// <param name="vigilante">Vigilante al que escuchar.</param>
    internal EsperaDeSueltas(VigilantePtt vigilante)
    {
        vigilante.PttSoltado += (_, motivo) =>
        {
            _motivos.Enqueue(motivo);
            _primera.TrySetResult(motivo);
            _haSoltado.Set();
        };

        vigilante.PttPegado += (_, ex) => _pegado.TrySetResult(ex);
    }

    /// <summary>Motivos de todas las sueltas que han saltado, en orden.</summary>
    internal IReadOnlyCollection<MotivoDeSuelta> Motivos => _motivos;

    /// <summary>Espera a que salte la primera suelta.</summary>
    /// <param name="plazo">Cuanto se espera como mucho.</param>
    /// <returns>El motivo de la suelta.</returns>
    internal Task<MotivoDeSuelta> PrimeraAsync(TimeSpan plazo) => _primera.Task.WaitAsync(plazo);

    /// <summary>
    /// Espera a la suelta <b>bloqueando el hilo</b>, sin pasar por el repartidor de tareas.
    /// </summary>
    /// <remarks>
    /// Hace falta para la prueba que ahoga el repartidor a proposito: alli un <c>await</c> no
    /// despertaria nunca —no porque el vigilante llegue tarde, sino porque no hay hilo libre
    /// para continuar la prueba—, y estariamos midiendo el pool en vez del vigilante.
    /// </remarks>
    /// <param name="plazo">Cuanto se espera como mucho.</param>
    /// <returns>El motivo de la suelta, o nulo si no llego a tiempo.</returns>
    internal MotivoDeSuelta? EsperarBloqueando(TimeSpan plazo)
    {
        if (!_haSoltado.Wait(plazo))
        {
            return null;
        }

        return _motivos.TryPeek(out var motivo) ? motivo : null;
    }

    /// <summary>Espera a que el vigilante avise de que el PTT se ha quedado pegado.</summary>
    /// <param name="plazo">Cuanto se espera como mucho.</param>
    /// <returns>El aviso.</returns>
    internal Task<PttPegadoException> PegadoAsync(TimeSpan plazo) => _pegado.Task.WaitAsync(plazo);
}
