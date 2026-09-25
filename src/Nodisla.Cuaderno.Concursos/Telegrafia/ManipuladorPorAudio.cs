using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Concursos.Telegrafia;

/// <summary>
/// Manipula telegrafia generando audio y sacandolo por la antena.
/// </summary>
/// <remarks>
/// <para>
/// Genera las muestras y se las da a la emision vigilada, que es quien pide la antena al
/// vigilante del PTT. Esta clase <b>no sabe</b> bajar un PTT ni quiere saberlo: si nadie le ha
/// conectado la emision, se niega a manipular y lo dice. Es la unica manera de garantizar que
/// un modulo de concursos no ponga la radio en antena por su cuenta.
/// </para>
/// <para>
/// Un texto se manipula entero de una vez: se generan todas las muestras y se emiten en una
/// sola transmision. Trocearlo daria huecos de PTT en medio de una llamada, que suenan fatal y
/// ademas hacen trabajar al rele de la radio mas de la cuenta.
/// </para>
/// </remarks>
public sealed class ManipuladorPorAudio : IManipuladorDeTelegrafia, IDisposable
{
    private readonly EmisionVigiladaDeMuestras? _emision;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _turno = new(1, 1);
    private readonly object _candado = new();
    private OpcionesDeTelegrafia _opciones;
    private Manipulador _manipulador;
    private GeneradorDeTonos _generador;
    private CancellationTokenSource? _enCurso;
    private bool _desechado;

    /// <summary>Crea el manipulador por audio.</summary>
    /// <param name="emision">
    /// Quien saca las muestras por la antena. Si es nulo, el manipulador no transmite: solo
    /// sirve para generar audio y para probar.
    /// </param>
    /// <param name="opciones">Velocidad, tono y amplitud.</param>
    /// <param name="registro">Para las trazas.</param>
    public ManipuladorPorAudio(
        EmisionVigiladaDeMuestras? emision = null,
        OpcionesDeTelegrafia? opciones = null,
        ILogger<ManipuladorPorAudio>? registro = null)
    {
        _emision = emision;
        _opciones = opciones ?? new OpcionesDeTelegrafia();
        _registro = registro ?? (ILogger)NullLogger.Instance;
        _manipulador = _opciones.Manipulador();
        _generador = _opciones.Generador();
    }

    /// <inheritdoc/>
    public int Ppm => _manipulador.Ppm;

    /// <summary>Hay una salida conectada y por tanto se puede transmitir.</summary>
    public bool PuedeTransmitir => _emision is not null;

    /// <inheritdoc/>
    public bool Manipulando
    {
        get { lock (_candado) return _enCurso is not null; }
    }

    /// <summary>Genera el audio de un texto sin emitirlo.</summary>
    /// <remarks>
    /// Sirve para el monitor de escucha, para la ayuda y para las pruebas. Al no tocar la
    /// radio, se puede llamar siempre.
    /// </remarks>
    /// <param name="texto">Texto a manipular.</param>
    /// <returns>Las muestras, un canal, coma flotante.</returns>
    public float[] Generar(string? texto) => _generador.Generar(_manipulador, texto);

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">No hay ninguna salida vigilada conectada.</exception>
    public async Task EnviarAsync(string texto, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        if (string.IsNullOrWhiteSpace(texto)) return;
        if (_emision is null)
        {
            throw new InvalidOperationException(
                "El manipulador no tiene salida de audio vigilada conectada, así que no transmite. " +
                "Hay que conectarlo a la emisión vigilada del módulo de audio al arrancar la aplicación.");
        }

        var muestras = Generar(texto);
        if (muestras.Length == 0) return;

        await _turno.WaitAsync(ct).ConfigureAwait(false);
        CancellationTokenSource? propio = null;
        try
        {
            propio = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lock (_candado) _enCurso = propio;

            _registro.LogInformation(
                "Manipulando {Cuantos} caracteres a {Ppm} ppm ({Duracion:0.0} s).",
                texto.Length, _manipulador.Ppm, muestras.Length / (double)_generador.FrecuenciaDeMuestreo);

            await _emision(muestras, $"Telegrafía: {Resumir(texto)}", propio.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Se aborto a proposito desde Abortar: no es un error.
            _registro.LogDebug("Manipulación cortada por el operador.");
        }
        finally
        {
            lock (_candado) _enCurso = null;
            propio?.Dispose();
            _turno.Release();
        }
    }

    /// <inheritdoc/>
    public Task AbortarAsync()
    {
        CancellationTokenSource? enCurso;
        lock (_candado) enCurso = _enCurso;
        if (enCurso is null) return Task.CompletedTask;
        try
        {
            return enCurso.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // Termino solo entre medias: no hay nada que cortar.
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc/>
    public Task CambiarVelocidadAsync(int ppm, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        ct.ThrowIfCancellationRequested();
        Reconfigurar(_opciones with { Ppm = ppm });
        return Task.CompletedTask;
    }

    /// <summary>Cambia todos los ajustes de golpe.</summary>
    /// <param name="opciones">Ajustes nuevos.</param>
    public void Reconfigurar(OpcionesDeTelegrafia opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        // Se construyen primero: si la velocidad no vale, salta aqui y los ajustes de antes
        // siguen en pie, en vez de quedarse a medias.
        var manipulador = opciones.Manipulador();
        var generador = opciones.Generador();
        _opciones = opciones;
        _manipulador = manipulador;
        _generador = generador;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_desechado) return;
        _desechado = true;
        _turno.Dispose();
    }

    private static string Resumir(string texto) =>
        texto.Length <= 24 ? texto : texto[..24] + "…";
}
