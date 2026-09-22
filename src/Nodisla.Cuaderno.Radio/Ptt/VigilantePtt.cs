using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control;

namespace Nodisla.Cuaderno.Radio.Ptt;

/// <summary>
/// El vigilante del PTT: el unico camino permitido para poner el equipo en antena.
/// </summary>
/// <remarks>
/// Todo lo que hace esta clase existe por un solo motivo: que sea <b>imposible</b> dejar el PTT
/// pegado. Un PTT que no se suelta quema la etapa final del equipo o el amplificador.
/// El PTT se baja, sin excepcion, cuando:
/// <list type="bullet">
/// <item>se libera la <see cref="ITransmisionEnCurso"/> (uso normal con <c>await using</c>);</item>
/// <item>se agota el tiempo maximo de transmision;</item>
/// <item>quien transmite deja de latir mas tiempo del tolerado;</item>
/// <item>salta una excepcion en el bloque de transmision;</item>
/// <item>se cancela el testigo de cancelacion;</item>
/// <item>se cierra la aplicacion, incluido el cierre por fallo no atendido;</item>
/// <item>el operador pulsa el boton de panico (<see cref="SoltarYaAsync"/>).</item>
/// </list>
/// Si la via normal falla, se prueban todas las vias de emergencia que ofrezca el control
/// (<see cref="ISueltaDeEmergenciaPtt"/>) y, si tampoco funciona ninguna, se lanza
/// <see cref="PttPegadoException"/>: el fallo nunca se traga en silencio.
///
/// <para><b>Cuanto se tarda en bajar el PTT, como mucho.</b> Esta cifra es la que separa un
/// susto de una etapa final quemada, asi que conviene tenerla escrita:</para>
/// <list type="bullet">
/// <item>
/// Por tiempo agotado, por falta de latido o por cancelacion: el plazo que toque
/// —<see cref="TiempoMaximo"/> o <see cref="TiempoSinLatido"/>— mas
/// <see cref="OpcionesDelVigilante.PasoDeVigilancia"/> mas <see cref="PlazoDeSuelta"/>. El
/// vigilante corre en <b>hilo propio</b>, no en el repartidor de tareas, precisamente para que
/// este plazo se cumpla aunque la aplicacion deje el pool sin un hilo libre.
/// </item>
/// <item>
/// Porque el equipo desaparezca: lo que tarde el control en darse cuenta —en el FT-710,
/// <c>ControlFt710.TiempoMaximoDeDeteccion</c>, que con los valores de partida son 5,1 s— mas
/// <see cref="PlazoDeSuelta"/>. Esa deteccion si depende del sondeo, y por eso detras esta
/// siempre la red de <see cref="TiempoMaximo"/>, que no depende de nada.
/// </item>
/// <item>
/// Pasado <see cref="PlazoDeSuelta"/> sin confirmacion, se dejan de esperar tareas y se tira de
/// las vias sincronas desde el propio hilo de la suelta.
/// </item>
/// </list>
/// </remarks>
public sealed class VigilantePtt : IVigilantePtt, IAsyncDisposable, IDisposable
{
    private const int Libre = 0;
    private const int EnAntenaEstado = 1;
    private const int Soltando = 2;

    /// <summary>Lo que duerme el hilo vigilante cuando no hay nada en antena.</summary>
    private static readonly TimeSpan SiestaSinTransmision = TimeSpan.FromMilliseconds(250);

    /// <summary>Lo que espera el hilo vigilante si algo le revienta, para no hacer un bucle loco.</summary>
    private static readonly TimeSpan SiestaTrasFallo = TimeSpan.FromMilliseconds(50);

    private readonly IControlEquipo _control;
    private readonly OpcionesDelVigilante _opciones;
    private readonly ILogger _registro;
    private readonly object _candado = new();
    private readonly EventHandler _alCerrarseElProceso;
    private readonly UnhandledExceptionEventHandler _alFallarSinAtender;
    private readonly EventHandler<string> _alPerderseElEquipo;

    private readonly ManualResetEventSlim _hayQueMirar = new(false);
    private readonly Thread _hiloDeVigilancia;

    private int _estado = Libre;
    private Transmision? _transmision;
    private CancellationTokenSource? _ctsTransmision;
    private Task _sueltaEnCurso = Task.CompletedTask;
    private long _marcaDeInicio;
    private long _marcaDeLatido;
    private volatile bool _cerrando;
    private bool _desechado;

    /// <summary>Crea el vigilante sobre un control de equipo.</summary>
    /// <param name="control">Control por el que se habla con el equipo.</param>
    /// <param name="opciones">Tiempos y manias; si es nulo, se usan los de partida.</param>
    /// <param name="registro">Donde se anota lo que pasa; si es nulo, no se anota nada.</param>
    public VigilantePtt(
        IControlEquipo control,
        OpcionesDelVigilante? opciones = null,
        ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(control);

        _control = control;
        _opciones = (opciones ?? new OpcionesDelVigilante()).Copiar();
        _registro = registro ?? NullLogger.Instance;

        _alCerrarseElProceso = (_, _) => SoltarEnElCierre(MotivoDeSuelta.Cierre, "cierre del proceso");
        _alFallarSinAtender = (_, _) => SoltarEnElCierre(MotivoDeSuelta.Cierre, "excepción no atendida");
        _alPerderseElEquipo = (_, porque) => SoltarPorPerderElEquipo(porque);

        if (_control is IAvisaDePerdidaDeComunicacion avisador)
        {
            avisador.ComunicacionPerdida += _alPerderseElEquipo;
        }

        if (_opciones.EngancharseAlCierreDelProceso)
        {
            AppDomain.CurrentDomain.ProcessExit += _alCerrarseElProceso;
            AppDomain.CurrentDomain.UnhandledException += _alFallarSinAtender;
        }

        // El vigilante tiene hilo propio, y no es un capricho: el camino que baja el PTT no
        // puede depender de que el repartidor de tareas tenga un hilo libre. Si la aplicacion
        // lo ahoga —un modem digital moliendo, por ejemplo—, una vigilancia montada sobre
        // tareas se retrasa segundos, y son segundos con el equipo en antena.
        _hiloDeVigilancia = new Thread(VigilarSiempre)
        {
            IsBackground = true,
            Name = "Vigilante de PTT",
            Priority = ThreadPriority.AboveNormal,
        };
        _hiloDeVigilancia.Start();
    }

    /// <summary>
    /// Suelta el PTT porque se ha perdido al equipo.
    /// </summary>
    /// <remarks>
    /// Si la radio se apaga o se desenchufa en mitad de una transmision, el vigilante no puede
    /// quedarse pensando que sigue en antena: manda bajar el PTT —puede que la orden ya no
    /// llegue a ninguna parte, pero las vias de emergencia se prueban igual— y deja de creerse
    /// en antena, que es lo que importa para lo que venga despues.
    /// </remarks>
    private void SoltarPorPerderElEquipo(string porque)
    {
        _registro.LogWarning("Se ha perdido el equipo ({Porque}); se suelta el PTT.", porque);

        var tarea = SoltarNucleoAsync(null, MotivoDeSuelta.EquipoPerdido);
        _ = tarea.ContinueWith(
            terminada =>
            {
                if (terminada.Exception is { } fallo)
                {
                    _registro.LogError(fallo.GetBaseException(), "No se pudo bajar el PTT tras perder el equipo.");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <inheritdoc />
    public TimeSpan TiempoMaximo => _opciones.TiempoMaximo;

    /// <inheritdoc />
    public TimeSpan TiempoSinLatido => _opciones.TiempoSinLatido;

    /// <inheritdoc />
    public bool EnAntena => Volatile.Read(ref _estado) == EnAntenaEstado;

    /// <inheritdoc />
    public event EventHandler<MotivoDeSuelta>? PttSoltado;

    /// <summary>
    /// Salta cuando han fallado todas las vias de soltar el PTT. Es la senal de que hay que
    /// apagar el equipo a mano.
    /// </summary>
    public event EventHandler<PttPegadoException>? PttPegado;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Si ya hay una transmision en curso.</exception>
    /// <exception cref="ObjectDisposedException">Si el vigilante ya se ha desechado.</exception>
    public async Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        ct.ThrowIfCancellationRequested();

        var razon = string.IsNullOrWhiteSpace(motivo) ? "sin motivo declarado" : motivo.Trim();

        // Nunca se sube el PTT encima de una bajada a medio hacer.
        await EsperarSueltaPendienteAsync().ConfigureAwait(false);

        if (Interlocked.CompareExchange(ref _estado, EnAntenaEstado, Libre) != Libre)
        {
            throw new InvalidOperationException(
                "Ya hay una transmisión en curso: hay que soltar la anterior antes de pedir antena otra vez.");
        }

        var ahora = Stopwatch.GetTimestamp();
        Volatile.Write(ref _marcaDeInicio, ahora);
        Volatile.Write(ref _marcaDeLatido, ahora);

        CancellationTokenSource cts;
        Transmision transmision;
        lock (_candado)
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            transmision = new Transmision(this, razon, cts.Token);
            _ctsTransmision = cts;
            _transmision = transmision;
        }

        // Se despierta al hilo vigilante: a partir de aqui hay algo que vigilar.
        _hayQueMirar.Set();

        try
        {
            await PonerPttAsync(true, cts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se pudo subir el PTT ({Razon}).", razon);

            // Aunque subirlo haya fallado, puede haber quedado arriba: se baja igualmente.
            try
            {
                await SoltarNucleoAsync(transmision, MotivoDeSuelta.Excepcion).ConfigureAwait(false);
            }
            catch (PttPegadoException pegado)
            {
                _registro.LogError(pegado, "PTT pegado tras fallar al subirlo.");
            }

            throw;
        }

        _registro.LogInformation(
            "PTT arriba ({Razon}). Tope {Tope}, sin latido {SinLatido}.",
            razon,
            _opciones.TiempoMaximo,
            _opciones.TiempoSinLatido);

        return transmision;
    }

    /// <inheritdoc />
    public Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico) =>
        SoltarNucleoAsync(null, motivo);

    /// <summary>
    /// Transmite ejecutando un bloque y garantiza la suelta tambien cuando el bloque falla o
    /// se cancela, distinguiendo el motivo en el registro.
    /// </summary>
    /// <param name="motivo">Para que se transmite.</param>
    /// <param name="cuerpo">Lo que se hace mientras el equipo esta en antena.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la transmision.</returns>
    public async Task TransmitirAsync(
        string motivo,
        Func<ITransmisionEnCurso, Task> cuerpo,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cuerpo);

        var transmision = (Transmision)await PedirAntenaAsync(motivo, ct).ConfigureAwait(false);
        await using (transmision.ConfigureAwait(false))
        {
            try
            {
                await cuerpo(transmision).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await SoltarNucleoAsync(transmision, MotivoDeSuelta.Cancelado).ConfigureAwait(false);
                throw;
            }
            catch (Exception)
            {
                await SoltarNucleoAsync(transmision, MotivoDeSuelta.Excepcion).ConfigureAwait(false);
                throw;
            }
        }
    }

    /// <summary>
    /// Suelta el PTT y se desengancha del cierre del proceso.
    /// </summary>
    /// <remarks>No desecha el control del equipo: el vigilante no es su dueño.</remarks>
    /// <returns>La tarea de la liberacion.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_desechado)
        {
            return;
        }

        _desechado = true;
        Desenganchar();

        try
        {
            await SoltarNucleoAsync(null, MotivoDeSuelta.Cierre).ConfigureAwait(false);
        }
        catch (PttPegadoException ex)
        {
            _registro.LogError(ex, "PTT pegado al cerrar el vigilante.");
        }
        finally
        {
            PararLaVigilancia();
        }
    }

    /// <summary>Version bloqueante de la liberacion, para quien no pueda esperar una tarea.</summary>
    public void Dispose()
    {
        if (_desechado)
        {
            return;
        }

        _desechado = true;
        Desenganchar();
        SoltarEnElCierre(MotivoDeSuelta.Cierre, "liberación del vigilante");
        PararLaVigilancia();
    }

    /// <summary>Para el hilo vigilante. Solo se llama al liberar el vigilante.</summary>
    private void PararLaVigilancia()
    {
        _cerrando = true;
        _hayQueMirar.Set();
        try
        {
            if (!_hiloDeVigilancia.Join(TimeSpan.FromSeconds(2)))
            {
                _registro.LogWarning("El hilo del vigilante no ha terminado a tiempo.");
            }
        }
        catch (Exception ex)
        {
            _registro.LogDebug(ex, "Fallo al esperar al hilo del vigilante.");
        }

        _hayQueMirar.Dispose();
    }

    /// <summary>Anota un latido de quien transmite.</summary>
    /// <param name="transmision">La transmision que late.</param>
    internal void Latido(Transmision transmision)
    {
        if (!ReferenceEquals(Volatile.Read(ref _transmision), transmision))
        {
            // Una transmision ya terminada no revive por latir.
            return;
        }

        Volatile.Write(ref _marcaDeLatido, Stopwatch.GetTimestamp());
    }

    /// <summary>Dice si esa transmision sigue siendo la que tiene el PTT.</summary>
    /// <param name="transmision">Transmision a comprobar.</param>
    /// <returns>Verdadero si sigue en antena.</returns>
    internal bool SigueEnAntena(Transmision transmision) =>
        ReferenceEquals(Volatile.Read(ref _transmision), transmision)
        && Volatile.Read(ref _estado) == EnAntenaEstado;

    /// <summary>Suelta el PTT por cuenta de una transmision concreta.</summary>
    /// <param name="transmision">Quien pide la suelta.</param>
    /// <param name="motivo">Por que se suelta.</param>
    /// <returns>La tarea de la suelta.</returns>
    internal Task SoltarPorLaTransmisionAsync(Transmision transmision, MotivoDeSuelta motivo) =>
        SoltarNucleoAsync(transmision, motivo);

    /// <summary>
    /// Baja el PTT de verdad. Prueba la via normal y, si falla, todas las de emergencia.
    /// </summary>
    private Task SoltarNucleoAsync(Transmision? quien, MotivoDeSuelta motivo)
    {
        Task tarea;

        lock (_candado)
        {
            var enCurso = _sueltaEnCurso;
            if (!enCurso.IsCompleted)
            {
                // Ya hay una suelta en marcha: dos sueltas a la vez no pueden dejar el PTT puesto.
                return EsperarSinPropagarCancelacion(enCurso);
            }

            if (quien is not null && !ReferenceEquals(quien, _transmision))
            {
                // Esa transmision ya no tiene el PTT; no hay nada que bajar por ella.
                return Task.CompletedTask;
            }

            var cts = _ctsTransmision;
            _ctsTransmision = null;
            _transmision = null;
            Volatile.Write(ref _estado, Soltando);

            // En hilo propio, fuera del candado y fuera del repartidor de tareas: la suelta no
            // puede depender ni de quien la pide ni de que haya un hilo libre en el pool.
            tarea = EnHiloPropio("Suelta de PTT", () => SoltarConPlazo(motivo, cts));
            _sueltaEnCurso = tarea;
        }

        return tarea;
    }

    /// <summary>
    /// Plazo duro de una suelta: lo que se le da a la via normal y a cada via de emergencia,
    /// mas una espera de gracia.
    /// </summary>
    /// <remarks>
    /// Pasado este plazo se dejan de esperar tareas y se tira de las vias sincronas. Es la
    /// cota que convierte «deberia soltar» en «suelta en menos de tanto».
    /// </remarks>
    public TimeSpan PlazoDeSuelta
    {
        get
        {
            var vias = _control is ISueltaDeEmergenciaPtt emergencia ? emergencia.ViasDeSuelta.Count : 0;
            return _opciones.EsperaDeSuelta * (vias + 2);
        }
    }

    /// <summary>
    /// Ejecuta la suelta y, si la parte asincrona no confirma dentro del plazo, tira de las
    /// vias sincronas desde este mismo hilo.
    /// </summary>
    private void SoltarConPlazo(MotivoDeSuelta motivo, CancellationTokenSource? cts)
    {
        var aviso = new AvisoDeSuelta();
        Exception? fallo = null;
        var plazo = PlazoDeSuelta;

        try
        {
            var tarea = EjecutarSueltaAsync(motivo, cts, aviso);
            if (tarea.Wait(plazo))
            {
                if (!tarea.IsFaulted)
                {
                    return;
                }

                fallo = tarea.Exception?.GetBaseException();
            }
            else
            {
                _registro.LogError(
                    "La suelta del PTT no se confirmó en {Plazo}; se prueban las vías síncronas.",
                    plazo);
            }
        }
        catch (Exception ex)
        {
            fallo = ex;
        }

        // Aqui se llega con el PTT posiblemente arriba: o fallaron todas las vias, o ninguna
        // contesto a tiempo. Queda el ultimo recurso, que no espera a nadie.
        Volatile.Write(ref _estado, Libre);
        if (SoltarPorViasSincronas("suelta con plazo agotado"))
        {
            aviso.AvisarUnaVez(this, motivo);
            return;
        }

        if (fallo is PttPegadoException pegado)
        {
            throw pegado;
        }

        var seQuedoPuesto = new PttPegadoException(
            "¡PTT PEGADO! Ni las vías normales ni las síncronas han podido bajar el PTT. Apague "
            + "el equipo o el amplificador a mano ahora mismo.",
            fallo is null ? [] : [fallo]);
        _registro.LogError(seQuedoPuesto, seQuedoPuesto.Message);
        Avisar(PttPegado, seQuedoPuesto);
        throw seQuedoPuesto;
    }

    /// <summary>Arranca algo en un hilo propio y devuelve la tarea que termina con el.</summary>
    private static Task EnHiloPropio(string nombre, Action trabajo)
    {
        var terminado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var hilo = new Thread(() =>
        {
            try
            {
                trabajo();
                terminado.TrySetResult();
            }
            catch (Exception ex)
            {
                terminado.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = nombre,
            Priority = ThreadPriority.AboveNormal,
        };

        hilo.Start();
        return terminado.Task;
    }

    /// <summary>Recorre las vias sincronas de suelta. Devuelve si alguna funciono.</summary>
    private bool SoltarPorViasSincronas(string cuando)
    {
        if (_control is not ISueltaDeEmergenciaPtt emergencia)
        {
            return false;
        }

        foreach (var via in emergencia.ViasDeSuelta)
        {
            if (via.SoltarSincrono is null)
            {
                continue;
            }

            try
            {
                via.SoltarSincrono();
                _registro.LogInformation("PTT abajo en la {Cuando} por «{Via}».", cuando, via.Nombre);
                return true;
            }
            catch (Exception ex)
            {
                _registro.LogError(ex, "Falló la vía síncrona «{Via}».", via.Nombre);
            }
        }

        return false;
    }

    /// <summary>Lleva la cuenta de si ya se ha avisado de una suelta, para no avisar dos veces.</summary>
    private sealed class AvisoDeSuelta
    {
        private int _avisado;

        internal void AvisarUnaVez(VigilantePtt vigilante, MotivoDeSuelta motivo)
        {
            if (Interlocked.Exchange(ref _avisado, 1) != 0)
            {
                return;
            }

            vigilante._registro.LogInformation("PTT abajo por «{Motivo}».", motivo);
            vigilante.Avisar(vigilante.PttSoltado, motivo);
        }
    }

    private async Task EjecutarSueltaAsync(MotivoDeSuelta motivo, CancellationTokenSource? cts, AvisoDeSuelta aviso)
    {
        var fallos = new List<Exception>();
        var soltado = false;
        string? viaBuena = null;

        try
        {
            using var espera = new CancellationTokenSource(_opciones.EsperaDeSuelta);
            try
            {
                await PonerPttAsync(false, espera.Token).ConfigureAwait(false);
                soltado = true;
                viaBuena = "vía normal";
            }
            catch (Exception ex)
            {
                fallos.Add(ex);
                _registro.LogWarning(ex, "La vía normal no ha podido bajar el PTT; se prueban las vías de emergencia.");
            }

            if (!soltado && _control is ISueltaDeEmergenciaPtt emergencia)
            {
                foreach (var via in emergencia.ViasDeSuelta)
                {
                    using var esperaVia = new CancellationTokenSource(_opciones.EsperaDeSuelta);
                    try
                    {
                        await via.SoltarAsync(esperaVia.Token).ConfigureAwait(false);
                        soltado = true;
                        viaBuena = via.Nombre;
                        break;
                    }
                    catch (Exception ex)
                    {
                        fallos.Add(ex);
                        _registro.LogWarning(ex, "Falló la vía de emergencia «{Via}» al bajar el PTT.", via.Nombre);
                    }
                }
            }
        }
        finally
        {
            Volatile.Write(ref _estado, Libre);
            if (cts is not null)
            {
                try
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _registro.LogDebug(ex, "Fallo al cancelar la vigilancia.");
                }

                cts.Dispose();
            }
        }

        if (!soltado)
        {
            var pegado = new PttPegadoException(
                "¡PTT PEGADO! Han fallado todas las vías de bajar el PTT. Apague el equipo o el "
                + "amplificador a mano ahora mismo.",
                fallos);
            _registro.LogError(pegado, pegado.Message);
            Avisar(PttPegado, pegado);
            throw pegado;
        }

        _registro.LogDebug("PTT abajo por la {Via}.", viaBuena);
        aviso.AvisarUnaVez(this, motivo);
    }

    /// <summary>
    /// El bucle del hilo vigilante. Mira el reloj, no espera a nadie.
    /// </summary>
    /// <remarks>
    /// Va en un hilo propio y usa <see cref="Thread.Sleep(TimeSpan)"/> en vez de esperas
    /// asincronas a proposito: asi el tope de tiempo de transmision y el latido se cumplen
    /// aunque el repartidor de tareas este ahogado, que es justo cuando mas falta hace.
    /// </remarks>
    private void VigilarSiempre()
    {
        while (!_cerrando)
        {
            try
            {
                var transmision = Volatile.Read(ref _transmision);
                if (transmision is null || Volatile.Read(ref _estado) != EnAntenaEstado)
                {
                    // Nada en antena: a dormir hasta que alguien pida la antena.
                    _hayQueMirar.Wait(SiestaSinTransmision);
                    _hayQueMirar.Reset();
                    continue;
                }

                var motivo = QueTocaHacer(transmision, out var siguienteMirada);
                if (motivo is not null)
                {
                    SoltarDesdeLaVigilancia(transmision, motivo.Value);
                    continue;
                }

                if (siguienteMirada > TimeSpan.Zero)
                {
                    Thread.Sleep(siguienteMirada);
                }
            }
            catch (Exception ex)
            {
                // Aqui no se sale del bucle por nada del mundo: si el vigilante muere, no hay
                // quien baje el PTT.
                _registro.LogError(ex, "El hilo del vigilante falló; sigue vigilando.");
                Thread.Sleep(SiestaTrasFallo);
            }
        }
    }

    /// <summary>Decide si hay que soltar y, si no, cuanto se puede dormir.</summary>
    private MotivoDeSuelta? QueTocaHacer(Transmision transmision, out TimeSpan siguienteMirada)
    {
        siguienteMirada = TimeSpan.Zero;

        if (transmision.Testigo.IsCancellationRequested)
        {
            return MotivoDeSuelta.Cancelado;
        }

        var enAntena = Stopwatch.GetElapsedTime(Volatile.Read(ref _marcaDeInicio));
        if (enAntena >= _opciones.TiempoMaximo)
        {
            return MotivoDeSuelta.TiempoAgotado;
        }

        var sinLatir = Stopwatch.GetElapsedTime(Volatile.Read(ref _marcaDeLatido));
        if (sinLatir >= _opciones.TiempoSinLatido)
        {
            return MotivoDeSuelta.SinLatido;
        }

        var siguiente = _opciones.PasoDeVigilancia;
        var loQueFaltaParaElTope = _opciones.TiempoMaximo - enAntena;
        var loQueFaltaSinLatir = _opciones.TiempoSinLatido - sinLatir;
        if (loQueFaltaParaElTope < siguiente)
        {
            siguiente = loQueFaltaParaElTope;
        }

        if (loQueFaltaSinLatir < siguiente)
        {
            siguiente = loQueFaltaSinLatir;
        }

        siguienteMirada = siguiente > TimeSpan.Zero ? siguiente : TimeSpan.Zero;
        return null;
    }

    private void SoltarDesdeLaVigilancia(Transmision transmision, MotivoDeSuelta motivo)
    {
        try
        {
            var tarea = SoltarNucleoAsync(transmision, motivo);

            // El hilo vigilante espera a su propia suelta: no tiene nada mejor que hacer y asi
            // no se solapan dos sueltas seguidas.
            if (!tarea.Wait(PlazoDeSuelta + PlazoDeSuelta))
            {
                _registro.LogError("La suelta por «{Motivo}» no ha terminado a tiempo.", motivo);
            }
        }
        catch (AggregateException ex) when (ex.GetBaseException() is PttPegadoException pegado)
        {
            _registro.LogError(pegado, "PTT pegado al soltar por «{Motivo}».", motivo);
        }
        catch (Exception ex)
        {
            _registro.LogError(ex, "Fallo inesperado al soltar por «{Motivo}».", motivo);
        }
    }

    /// <summary>
    /// Manda la orden al equipo por el camino reservado si lo hay, o por el del contrato.
    /// </summary>
    private Task PonerPttAsync(bool transmitir, CancellationToken ct) =>
        _control is IPttDirecto directo
            ? directo.PonerPttDirectoAsync(transmitir, ct)
            : _control.PonerPttAsync(transmitir, ct);

    private Task EsperarSueltaPendienteAsync()
    {
        Task pendiente;
        lock (_candado)
        {
            pendiente = _sueltaEnCurso;
        }

        return pendiente.IsCompleted ? Task.CompletedTask : EsperarSinPropagarCancelacion(pendiente);
    }

    private async Task EsperarSinPropagarCancelacion(Task tarea)
    {
        try
        {
            await tarea.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogDebug(ex, "La suelta anterior terminó con fallo.");
        }
    }

    /// <summary>
    /// Suelta bloqueando, para el cierre del proceso: ahi no hay tiempo ni se puede confiar en
    /// que el planificador de tareas siga vivo.
    /// </summary>
    private void SoltarEnElCierre(MotivoDeSuelta motivo, string quien)
    {
        var bien = false;
        try
        {
            var tarea = SoltarNucleoAsync(null, motivo);
            bien = tarea.Wait(_opciones.EsperaDeSuelta + _opciones.EsperaDeSuelta) && !tarea.IsFaulted;
        }
        catch (Exception ex)
        {
            _registro.LogError(ex, "Fallo al soltar el PTT en el {Quien}.", quien);
        }

        if (bien)
        {
            return;
        }

        _registro.LogError("La suelta del PTT en el {Quien} no se confirmó; se prueban las vías síncronas.", quien);
        SoltarPorViasSincronas(quien);
    }

    private void Desenganchar()
    {
        if (_control is IAvisaDePerdidaDeComunicacion avisador)
        {
            avisador.ComunicacionPerdida -= _alPerderseElEquipo;
        }

        if (!_opciones.EngancharseAlCierreDelProceso)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit -= _alCerrarseElProceso;
        AppDomain.CurrentDomain.UnhandledException -= _alFallarSinAtender;
    }

    private void Avisar<T>(EventHandler<T>? evento, T argumento)
    {
        if (evento is null)
        {
            return;
        }

        foreach (var suscriptor in evento.GetInvocationList().Cast<EventHandler<T>>())
        {
            try
            {
                suscriptor(this, argumento);
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "Un suscriptor del vigilante lanzó una excepción.");
            }
        }
    }

    /// <summary>Una transmision en curso. Liberarla baja el PTT.</summary>
    internal sealed class Transmision : ITransmisionEnCurso
    {
        private readonly VigilantePtt _vigilante;
        private int _liberada;

        internal Transmision(VigilantePtt vigilante, string motivo, CancellationToken testigo)
        {
            _vigilante = vigilante;
            Motivo = motivo;
            Testigo = testigo;
        }

        /// <summary>Para que se pidio la antena.</summary>
        public string Motivo { get; }

        /// <summary>Testigo de cancelacion de esta transmision.</summary>
        internal CancellationToken Testigo { get; }

        /// <inheritdoc />
        public bool EnAntena => _vigilante.SigueEnAntena(this);

        /// <inheritdoc />
        public void Latir() => _vigilante.Latido(this);

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _liberada, 1) != 0)
            {
                // Doble liberacion: se espera a la suelta que ya haya en marcha, sin mandar otra orden.
                return new ValueTask(_vigilante.EsperarSueltaPendienteAsync());
            }

            return new ValueTask(_vigilante.SoltarPorLaTransmisionAsync(this, MotivoDeSuelta.Normal));
        }
    }
}
