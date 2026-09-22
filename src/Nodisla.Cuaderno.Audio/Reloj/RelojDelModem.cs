using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Audio.Reloj;

/// <summary>
/// La hora corregida que usa el modem propio.
/// </summary>
/// <remarks>
/// <para>
/// Este es el fallo que mas veces explica que el modem «no saque nada»: el ordenador con la
/// hora corrida. FT8 va en ventanas de quince segundos alineadas al reloj universal; con un
/// segundo de desvio las decodificaciones se caen, y con dos no hay ninguna y ademas se
/// transmite fuera de ventana, molestando a los demas sin enterarse.
/// </para>
/// <para>
/// Por eso aqui se mide y <b>se ensena</b>. Mientras no se haya podido medir, el desvio vale
/// cero y <see cref="DesvioDelReloj.EsFiable"/> es falso: no es lo mismo «estoy en hora» que
/// «no tengo ni idea de la hora que es», y el operador tiene que poder distinguirlo.
/// </para>
/// </remarks>
public sealed class RelojDelModem : IRelojDelModem, IDisposable
{
    private readonly OpcionesDelReloj _opciones;
    private readonly IReadOnlyList<IFuenteDeHora> _fuentes;
    private readonly Func<DateTimeOffset> _relojDelSistema;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _unaMedidaCadaVez = new(1, 1);

    private DesvioDelReloj _desvio = SinMedir;
    private CancellationTokenSource? _seguimiento;
    private Task _tareaDeSeguimiento = Task.CompletedTask;
    private bool _desechado;

    /// <summary>Lo que se dice mientras no se ha podido medir nada.</summary>
    private static readonly DesvioDelReloj SinMedir =
        new(0.0, "sin medir", DateTimeOffset.MinValue, EsFiable: false);

    /// <summary>Crea el reloj.</summary>
    /// <param name="opciones">Ajustes; si es nulo, los de partida.</param>
    /// <param name="fuentes">
    /// A quien se le pregunta la hora; si es nulo, se monta un cliente SNTP por cada servidor
    /// de las opciones. En pruebas se le pasan fuentes de mentira y no se toca la red.
    /// </param>
    /// <param name="relojDelSistema">De donde se lee la hora local; si es nulo, la del sistema.</param>
    /// <param name="registro">Donde se anota lo que pasa; si es nulo, no se anota nada.</param>
    public RelojDelModem(
        OpcionesDelReloj? opciones = null,
        IReadOnlyList<IFuenteDeHora>? fuentes = null,
        Func<DateTimeOffset>? relojDelSistema = null,
        ILogger? registro = null)
    {
        _opciones = (opciones ?? new OpcionesDelReloj()).Copiar();
        _relojDelSistema = relojDelSistema ?? (() => DateTimeOffset.UtcNow);
        _registro = registro ?? NullLogger.Instance;

        _fuentes = fuentes ?? _opciones.Servidores
            .Where(servidor => !string.IsNullOrWhiteSpace(servidor))
            .Select(servidor => (IFuenteDeHora)new ClienteSntp(
                servidor,
                _opciones.EsperaPorServidor,
                _relojDelSistema,
                _registro))
            .ToList();
    }

    /// <inheritdoc />
    public DateTimeOffset Ahora
    {
        get
        {
            var desvio = Volatile.Read(ref _desvio);
            return _relojDelSistema() - TimeSpan.FromMilliseconds(desvio.DesvioMs);
        }
    }

    /// <inheritdoc />
    public DesvioDelReloj Desvio => Volatile.Read(ref _desvio);

    /// <summary>
    /// El desvio esta medido y es pequeno. Si es falso, hay que mirarlo antes que nada.
    /// </summary>
    public bool EnHora
    {
        get
        {
            var desvio = Volatile.Read(ref _desvio);
            return desvio.EsFiable && Math.Abs(desvio.DesvioMs) <= _opciones.MargenDeAvisoMs;
        }
    }

    /// <inheritdoc />
    public event EventHandler<DesvioDelReloj>? DesvioMedido;

    /// <summary>Frase corta con el estado del reloj, lista para la barra de estado.</summary>
    /// <returns>Lo que hay que ensenarle al operador.</returns>
    public string Resumen() => Describir(Volatile.Read(ref _desvio));

    /// <summary>Pone en palabras un desvio.</summary>
    /// <param name="desvio">El desvio a describir.</param>
    /// <returns>Una frase para el operador.</returns>
    public static string Describir(DesvioDelReloj desvio)
    {
        ArgumentNullException.ThrowIfNull(desvio);

        if (!desvio.EsFiable)
        {
            return "Reloj sin comprobar: no se ha podido medir el desvío, así que no se sabe si el módem está en hora.";
        }

        var sentido = desvio.DesvioMs >= 0 ? "adelantado" : "atrasado";
        return string.Create(
            CultureInfo.CurrentCulture,
            $"Reloj {sentido} {Math.Abs(desvio.DesvioMs):F0} ms según {desvio.Fuente}.");
    }

    /// <inheritdoc />
    public Task<DesvioDelReloj> MedirAsync(CancellationToken ct = default) => MedirAsync(false, ct);

    /// <summary>
    /// Mide el desvio, aprovechando la ultima medida si todavia vale.
    /// </summary>
    /// <param name="forzar">Verdadero para preguntar aunque la ultima medida siga siendo buena.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El desvio, fiable o no.</returns>
    public async Task<DesvioDelReloj> MedirAsync(bool forzar, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);

        if (!forzar && SigueValiendo(Volatile.Read(ref _desvio)))
        {
            return Volatile.Read(ref _desvio);
        }

        await _unaMedidaCadaVez.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Otro pudo medir mientras se esperaba turno.
            if (!forzar && SigueValiendo(Volatile.Read(ref _desvio)))
            {
                return Volatile.Read(ref _desvio);
            }

            var medida = await PreguntarATodosAsync(ct).ConfigureAwait(false);
            var nuevo = medida is null
                ? new DesvioDelReloj(
                    Volatile.Read(ref _desvio).DesvioMs,
                    "sin respuesta",
                    _relojDelSistema(),
                    EsFiable: false)
                : new DesvioDelReloj(medida.DesvioMs, medida.Fuente, _relojDelSistema(), EsFiable: true);

            Volatile.Write(ref _desvio, nuevo);

            if (nuevo.EsFiable)
            {
                _registro.LogInformation(
                    "Reloj medido contra {Fuente}: {Desvio:F0} ms de desvío.",
                    nuevo.Fuente,
                    nuevo.DesvioMs);

                if (Math.Abs(nuevo.DesvioMs) > _opciones.MargenDeAvisoMs)
                {
                    _registro.LogWarning(
                        "El reloj del ordenador está {Desvio:F0} ms fuera de hora: con este desvío FT8 decodifica mal.",
                        nuevo.DesvioMs);
                }
            }
            else
            {
                _registro.LogWarning(
                    "No contestó ningún servidor de hora: el módem trabaja sin saber si está en hora.");
            }

            DesvioMedido?.Invoke(this, nuevo);
            return nuevo;
        }
        finally
        {
            _unaMedidaCadaVez.Release();
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Si el periodo no es positivo.</exception>
    public DateTimeOffset ProximaVentana(TimeSpan periodo) => ProximaVentanaDesde(Ahora, periodo);

    /// <summary>
    /// Momento en que empieza la siguiente ventana a partir de un instante dado.
    /// </summary>
    /// <param name="instante">Desde cuando se mira, en hora corregida.</param>
    /// <param name="periodo">Duracion de la ventana: 15 s en FT8, 7,5 s en FT4.</param>
    /// <returns>El comienzo de la siguiente ventana, siempre estrictamente posterior.</returns>
    /// <remarks>
    /// Las ventanas van alineadas al reloj universal, no a cuando arranco el programa: a las
    /// 12:00:00, 12:00:15, 12:00:30... Por eso se cuenta sobre el numero de pulsos absoluto y
    /// no sobre nada que dependa de nosotros. Como 15 y 7,5 segundos caben un numero entero de
    /// veces en un minuto, el comienzo de cada minuto es siempre comienzo de ventana.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Si el periodo no es positivo.</exception>
    public static DateTimeOffset ProximaVentanaDesde(DateTimeOffset instante, TimeSpan periodo)
    {
        if (periodo <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodo),
                periodo,
                "El periodo de la ventana tiene que ser positivo.");
        }

        var pulsos = instante.UtcTicks;
        var resto = pulsos % periodo.Ticks;

        // Si se cae justo en el borde, la «proxima» es la de dentro de un periodo entero, no
        // esta: la que empieza ahora mismo ya no se puede coger desde el principio.
        var faltan = periodo.Ticks - resto;
        return new DateTimeOffset(pulsos + faltan, TimeSpan.Zero);
    }

    /// <summary>
    /// Empieza a remedir el desvio cada tanto, en segundo plano.
    /// </summary>
    /// <remarks>
    /// No se arranca solo al crear el reloj: quien manda es la aplicacion, y asi las pruebas y
    /// el arranque sin red no se ponen a hablar con servidores por su cuenta.
    /// </remarks>
    public void IniciarSeguimiento()
    {
        ObjectDisposedException.ThrowIf(_desechado, this);

        if (_seguimiento is not null)
        {
            return;
        }

        _seguimiento = new CancellationTokenSource();
        _tareaDeSeguimiento = SeguirAsync(_seguimiento.Token);
    }

    /// <summary>Deja de remedir en segundo plano.</summary>
    /// <returns>Cuando el seguimiento ha parado del todo.</returns>
    public async Task PararSeguimientoAsync()
    {
        var seguimiento = _seguimiento;
        if (seguimiento is null)
        {
            return;
        }

        _seguimiento = null;
        await seguimiento.CancelAsync().ConfigureAwait(false);

        try
        {
            await _tareaDeSeguimiento.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Se esperaba: se estaba parando.
        }
        finally
        {
            seguimiento.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_desechado)
        {
            return;
        }

        _desechado = true;

        try
        {
            PararSeguimientoAsync().GetAwaiter().GetResult();
        }
        catch (Exception fallo)
        {
            _registro.LogDebug(fallo, "Algo falló al parar el seguimiento de la hora.");
        }

        _unaMedidaCadaVez.Dispose();
    }

    /// <summary>Una medida sigue valiendo si es fiable y no ha caducado.</summary>
    private bool SigueValiendo(DesvioDelReloj desvio) =>
        desvio.EsFiable && _relojDelSistema() - desvio.MedidoUtc < _opciones.ValidezDeLaMedida;

    /// <summary>
    /// Pregunta a todas las fuentes a la vez y se queda con la que menos tardo en ir y volver.
    /// </summary>
    private async Task<MedidaDeHora?> PreguntarATodosAsync(CancellationToken ct)
    {
        if (_fuentes.Count == 0)
        {
            return null;
        }

        var consultas = _fuentes.Select(fuente => PreguntarAsync(fuente, ct)).ToArray();
        var respuestas = await Task.WhenAll(consultas).ConfigureAwait(false);

        MedidaDeHora? mejor = null;
        foreach (var respuesta in respuestas)
        {
            if (respuesta is not null && (mejor is null || respuesta.IdaYVueltaMs < mejor.IdaYVueltaMs))
            {
                mejor = respuesta;
            }
        }

        return mejor;
    }

    /// <summary>Pregunta a una fuente sin dejar que su fallo tumbe a las demas.</summary>
    private async Task<MedidaDeHora?> PreguntarAsync(IFuenteDeHora fuente, CancellationToken ct)
    {
        try
        {
            return await fuente.ConsultarAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception fallo)
        {
            _registro.LogDebug(fallo, "Falló la consulta de hora a {Fuente}.", fuente.Nombre);
            return null;
        }
    }

    /// <summary>Bucle que remide cada tanto hasta que se le manda parar.</summary>
    private async Task SeguirAsync(CancellationToken ct)
    {
        try
        {
            using var espera = new PeriodicTimer(_opciones.ValidezDeLaMedida);

            await MedirAsync(true, ct).ConfigureAwait(false);

            while (await espera.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await MedirAsync(true, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Se esperaba: se estaba parando.
        }
        catch (Exception fallo)
        {
            _registro.LogError(fallo, "El seguimiento de la hora se ha parado por un fallo.");
        }
    }
}
