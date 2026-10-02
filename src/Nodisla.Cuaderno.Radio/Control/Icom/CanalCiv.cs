using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control.Icom;

/// <summary>
/// Canal CI-V: manda tramas a una radio ICOM y recoge sus respuestas.
/// </summary>
/// <remarks>
/// Hoy solo hay canal por puerto serie (<see cref="CanalSerieCiv"/>; el USB de ICOM es un puerto
/// COM). El control por red de IC-705, IC-9700 e IC-7610 (protocolo propio de ICOM por UDP)
/// <b>no esta hecho</b>: iria como otra clase hija de <see cref="CanalCiv"/>.
/// </remarks>
public abstract class CanalCiv : IAsyncDisposable
{
    private readonly AnalizadorDeTramasCiv _analizador = new();
    private readonly SemaphoreSlim _puerta = new(1, 1);
    private readonly object _candado = new();
    private TaskCompletionSource<TramaCiv>? _esperando;
    private int _ordenEsperada = -1;

    /// <summary>Crea el canal.</summary>
    /// <param name="direccionDelEquipo">Direccion CI-V de la radio.</param>
    /// <param name="espera">Lo que se espera cada respuesta.</param>
    /// <param name="registro">Donde anotar.</param>
    /// <param name="direccionDelOrdenador">Direccion del ordenador (E0 en todos los manuales).</param>
    protected CanalCiv(byte direccionDelEquipo, TimeSpan? espera, ILogger? registro, byte direccionDelOrdenador = TramaCiv.DireccionDelOrdenador)
    {
        DireccionDelEquipo = direccionDelEquipo;
        DireccionDelOrdenador = direccionDelOrdenador;
        Espera = espera ?? TimeSpan.FromMilliseconds(350);
        Registro = registro ?? NullLogger.Instance;
    }

    /// <summary>Direccion CI-V de la radio.</summary>
    public byte DireccionDelEquipo { get; }

    /// <summary>Direccion CI-V del ordenador.</summary>
    public byte DireccionDelOrdenador { get; }

    /// <summary>Lo que se espera por cada respuesta.</summary>
    public TimeSpan Espera { get; }

    /// <summary>Donde anotar.</summary>
    protected ILogger Registro { get; }

    /// <summary>El canal esta abierto.</summary>
    public abstract bool Abierto { get; }

    /// <summary>Como llamar al canal en el registro: <c>COM5 a 115200 (CI-V 94)</c>.</summary>
    public abstract string Descripcion { get; }

    /// <summary>Tramas que manda la radio por su cuenta (transceive): cambio de frecuencia o modo en el dial.</summary>
    public event EventHandler<TramaCiv>? TramaEspontanea;

    /// <summary>Abre el canal.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    public async Task AbrirAsync(CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _analizador.Vaciar();
            await AbrirNucleoAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <summary>
    /// Manda un cuerpo CI-V y espera la respuesta de la radio.
    /// </summary>
    /// <param name="cuerpo">Orden, suborden y datos.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>
    /// La respuesta (con <c>FB</c>/<c>FA</c> o con datos), o nulo si no contesta. El eco de lo
    /// mandado no cuenta como respuesta.
    /// </returns>
    /// <exception cref="Ft710.OrdenPeligrosaException">Si la orden no se puede mandar.</exception>
    public async Task<TramaCiv?> PreguntarAsync(byte[] cuerpo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cuerpo);
        OrdenesIcom.ComprobarQueEsSegura(cuerpo);

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!Abierto) throw new InvalidOperationException(Textos.F("Servicios.Radio.CanalCerrado", Descripcion));

            var espera = new TaskCompletionSource<TramaCiv>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_candado)
            {
                _esperando = espera;
                _ordenEsperada = cuerpo.Length > 0 ? cuerpo[0] : -1;
            }

            try
            {
                var trama = new TramaCiv(DireccionDelEquipo, DireccionDelOrdenador, cuerpo);
                await EscribirNucleoAsync(trama.ABytes(), ct).ConfigureAwait(false);

                using var tope = CancellationTokenSource.CreateLinkedTokenSource(ct);
                tope.CancelAfter(Espera);
                try
                {
                    return await espera.Task.WaitAsync(tope.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Registro.LogDebug("{Canal}: sin respuesta a {Orden}.", Descripcion, Hex.De(cuerpo));
                    return null;
                }
            }
            finally
            {
                lock (_candado)
                {
                    _esperando = null;
                    _ordenEsperada = -1;
                }
            }
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <summary>
    /// Manda un cuerpo bloqueando y sin esperar respuesta ni candado: solo para bajar el PTT
    /// cuando todo lo demas ha fallado o el proceso se muere.
    /// </summary>
    /// <param name="cuerpo">Orden.</param>
    public void MandarSincrono(byte[] cuerpo)
    {
        ArgumentNullException.ThrowIfNull(cuerpo);
        OrdenesIcom.ComprobarQueEsSegura(cuerpo);
        EscribirNucleoSincrono(new TramaCiv(DireccionDelEquipo, DireccionDelOrdenador, cuerpo).ABytes());
    }

    /// <summary>
    /// Despierta la radio apagada: una tira de <c>FE</c> y luego <c>18 01</c> (manuales: 150
    /// <c>FE</c> a 115200, 25 a 19200, 7 a 4800...).
    /// </summary>
    /// <param name="preambulos">Cuantos <c>FE</c> mandar antes.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    public async Task DespertarAsync(int preambulos, CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var trama = new TramaCiv(DireccionDelEquipo, DireccionDelOrdenador, [0x18, 0x01]).ABytes();
            var bytes = Enumerable.Repeat(TramaCiv.Preambulo, Math.Max(0, preambulos)).Concat(trama).ToArray();
            await EscribirNucleoAsync(bytes, ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <summary>El canal puede accionar RTS/DTR.</summary>
    public virtual bool PuedeAccionarLineas => false;

    /// <summary>Sube o baja la linea que hace de PTT.</summary>
    /// <param name="transmitir">Verdadero para subirla.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    public virtual Task PonerLineaDePttAsync(bool transmitir, CancellationToken ct = default) =>
        Task.FromException(new NotSupportedException($"El canal {Descripcion} no tiene líneas de control."));

    /// <summary>Baja o sube la linea de PTT bloqueando.</summary>
    /// <param name="transmitir">Verdadero para subirla.</param>
    public virtual void PonerLineaDePttSincrono(bool transmitir) =>
        throw new NotSupportedException($"El canal {Descripcion} no tiene líneas de control.");

    /// <summary>Cierra el canal.</summary>
    public void Cerrar()
    {
        CerrarNucleo();
        _analizador.Vaciar();
    }

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync()
    {
        Cerrar();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Lo llama la clase hija con lo que llega del bus: se corta en tramas, se tira el eco y
    /// cada trama va a quien espera respuesta o al aviso del transceive.
    /// </summary>
    /// <param name="bytes">Bytes recibidos.</param>
    protected void AlRecibir(ReadOnlySpan<byte> bytes)
    {
        foreach (var trama in _analizador.Anadir(bytes))
        {
            // Eco de lo que hemos mandado nosotros: el bus CI-V se oye a si mismo.
            if (trama.Origen == DireccionDelOrdenador)
            {
                continue;
            }

            // Otra radio del mismo bus: no es asunto nuestro. Con la direccion de difusion (la
            // identificacion con 19 00) vale cualquiera.
            if (DireccionDelEquipo != TramaCiv.Difusion && trama.Origen != DireccionDelEquipo)
            {
                continue;
            }

            if (trama.Destino == DireccionDelOrdenador)
            {
                TaskCompletionSource<TramaCiv>? espera;
                int orden;
                lock (_candado)
                {
                    espera = _esperando;
                    orden = _ordenEsperada;
                }

                // Solo es la respuesta si es FB/FA o repite la orden preguntada: un aviso
                // transceive dirigido al ordenador no puede colarse como respuesta.
                if (espera is not null && (trama.EsBien || trama.EsNoAdmitido || trama.Orden == orden))
                {
                    espera.TrySetResult(trama);
                    continue;
                }
            }

            if (trama.Destino == TramaCiv.Difusion || trama.Destino == DireccionDelOrdenador)
            {
                try
                {
                    TramaEspontanea?.Invoke(this, trama);
                }
                catch (Exception ex)
                {
                    Registro.LogWarning(ex, "Fallo al atender un aviso transceive de {Canal}.", Descripcion);
                }
            }
        }
    }

    /// <summary>Abre el medio.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    protected abstract Task AbrirNucleoAsync(CancellationToken ct);

    /// <summary>Escribe bytes en el medio.</summary>
    /// <param name="bytes">Bytes.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    protected abstract Task EscribirNucleoAsync(byte[] bytes, CancellationToken ct);

    /// <summary>Escribe bytes bloqueando, sin candados.</summary>
    /// <param name="bytes">Bytes.</param>
    protected abstract void EscribirNucleoSincrono(byte[] bytes);

    /// <summary>Cierra el medio.</summary>
    protected abstract void CerrarNucleo();
}
