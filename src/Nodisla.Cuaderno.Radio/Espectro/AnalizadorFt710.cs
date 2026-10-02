using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;

using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Espectro;

/// <summary>
/// El analizador de espectro del propio FT-710, leido por su puente FT4222 interno.
/// </summary>
/// <remarks>
/// <para>
/// Solo lectura: no hay ni una orden hacia la radio. El span, CENTER/FIX y la velocidad se
/// eligen en el equipo. Para que la radio mande su espectro tiene que estar en ON el menu
/// OPERATION SETTING → GENERAL → SCU-LAN10 (no hace falta la unidad SCU-LAN10); si esta en
/// OFF el puente se abre pero no llegan tramas, y el estado lo dice.
/// </para>
/// <para>
/// Lee en un hilo propio, sin parar: una trama cada ~90 ms (4096 bytes a 375 kHz de reloj
/// SPI). Si el puente desaparece (radio apagada) vuelve a intentarlo cada pocos segundos.
/// </para>
/// </remarks>
public sealed class AnalizadorFt710 : IAnalizadorDeEspectro
{
    /// <summary>Bytes que se leen buscando la marca antes de rendirse.</summary>
    internal const int TopeDeSincronia = 3 * TramaDelAnalizadorFt710.Largo;

    private readonly Func<(AperturaDelPuente Resultado, IPuenteDelAnalizador? Puente, string Detalle)> _abrir;
    private readonly TimeSpan _pausaEntreIntentos;
    private readonly ILogger? _registro;
    private readonly object _cerrojo = new();
    private CancellationTokenSource? _cancelacion;
    private Thread? _hilo;

    /// <summary>Analizador contra el FT4222 de verdad.</summary>
    /// <param name="registro">Registro, opcional.</param>
    public AnalizadorFt710(ILogger? registro = null)
        : this(
            () =>
            {
                var resultado = PuenteFt4222.Abrir(out var puente, out var detalle);
                return (resultado, puente, detalle);
            },
            TimeSpan.FromSeconds(3),
            registro)
    {
    }

    /// <summary>Analizador contra un puente cualquiera (pruebas).</summary>
    /// <param name="abrir">Como se abre el puente.</param>
    /// <param name="pausaEntreIntentos">Espera antes de reintentar si algo falla.</param>
    /// <param name="registro">Registro, opcional.</param>
    /// <param name="plazoDeApertura">
    /// Lo que se espera a que el puente se abra antes de decir que no contesta; nulo,
    /// <see cref="PlazoDeAperturaPorOmision"/>.
    /// </param>
    public AnalizadorFt710(
        Func<(AperturaDelPuente Resultado, IPuenteDelAnalizador? Puente, string Detalle)> abrir,
        TimeSpan pausaEntreIntentos,
        ILogger? registro = null,
        TimeSpan? plazoDeApertura = null)
    {
        _abrir = abrir ?? throw new ArgumentNullException(nameof(abrir));
        _pausaEntreIntentos = pausaEntreIntentos;
        _registro = registro;
        _plazoDeApertura = plazoDeApertura ?? PlazoDeAperturaPorOmision;
    }

    /// <summary>
    /// Lo que tarda de sobra en abrirse el FT4222 (la primera vez, unos segundos). Pasado esto
    /// se avisa de que no contesta, y se sigue esperando a esa misma apertura.
    /// </summary>
    public static readonly TimeSpan PlazoDeAperturaPorOmision = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _plazoDeApertura;

    /// <summary>
    /// La apertura en curso. Si se queda colgada en el controlador de FTDI no se lanza otra
    /// encima: el siguiente arranque espera a esta.
    /// </summary>
    private Task<(AperturaDelPuente Resultado, IPuenteDelAnalizador? Puente, string Detalle)>? _apertura;

    /// <inheritdoc />
    public event EventHandler<TrazaDeEspectro>? TrazaRecibida;

    /// <inheritdoc />
    public event EventHandler? EstadoCambiado;

    /// <inheritdoc />
    public string Origen => Textos.T("Servicios.Radio.Analizador.Origen");

    /// <inheritdoc />
    public EstadoDelAnalizador Estado { get; private set; }

    /// <inheritdoc />
    public string Motivo { get; private set; } = string.Empty;

    /// <summary>Tramas buenas recibidas desde que se arranco.</summary>
    public long TramasRecibidas { get; private set; }

    /// <inheritdoc />
    public void Iniciar()
    {
        lock (_cerrojo)
        {
            if (_hilo is not null) return;
            _cancelacion = new CancellationTokenSource();
            var token = _cancelacion.Token;

            // Al cambiar de pestaña, la que se va lo detiene y la que llega lo arranca casi a
            // la vez: el hilo anterior puede seguir con el FT4222 abierto. El nuevo espera a
            // que lo suelte antes de abrirlo, en vez de chocar con el (28-09-2026: Jose se
            // quedaba sin espectro tras cambiar de pestaña).
            var anterior = _hiloQueSeVa;
            _hilo = new Thread(() =>
            {
                anterior?.Join(TimeSpan.FromSeconds(6));
                if (!token.IsCancellationRequested) Bucle(token);
            })
            {
                IsBackground = true,
                Name = "Analizador FT-710",
                Priority = ThreadPriority.AboveNormal,
            };
            _hilo.Start();
        }
    }

    /// <summary>El hilo que se esta deteniendo, para que el siguiente espere a que suelte el puente.</summary>
    private Thread? _hiloQueSeVa;

    /// <inheritdoc />
    public async Task DetenerAsync()
    {
        Thread? hilo;
        CancellationTokenSource? cancelacion;
        lock (_cerrojo)
        {
            hilo = _hilo;
            cancelacion = _cancelacion;
            cancelacion?.Cancel();
            _hilo = null;
            _cancelacion = null;
            _hiloQueSeVa = hilo;
        }

        if (hilo is not null)
        {
            // La primera apertura del FT4222 puede quedarse colgada un rato: no se bloquea
            // la interfaz esperandola para siempre.
            await Task.Run(() => hilo.Join(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        }

        // Solo se libera lo de ESTE arranque, y solo se da por parado si entre tanto nadie
        // lo ha vuelto a arrancar: antes se liberaba el arranque nuevo y se pisaba su estado.
        cancelacion?.Dispose();
        bool sigueParado;
        lock (_cerrojo)
        {
            if (ReferenceEquals(_hiloQueSeVa, hilo)) _hiloQueSeVa = null;
            sigueParado = _hilo is null;
        }

        if (sigueParado) CambiarEstado(EstadoDelAnalizador.Parado, string.Empty);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await DetenerAsync().ConfigureAwait(false);

        // Una apertura que quedo pendiente suelta el puente en cuanto termine.
        Task<(AperturaDelPuente Resultado, IPuenteDelAnalizador? Puente, string Detalle)>? pendiente;
        lock (_cerrojo)
        {
            pendiente = _apertura;
            _apertura = null;
        }

        _ = pendiente?.ContinueWith(
            t => t.Result.Puente?.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
    }

    private void Bucle(CancellationToken token)
    {
        var trama = new byte[TramaDelAnalizadorFt710.Largo];
        while (!token.IsCancellationRequested)
        {
            var apertura = AbrirConPlazo(token);
            if (apertura is null) return;
            var (resultado, puente, detalle) = apertura.Value;
            if (resultado != AperturaDelPuente.Abierto || puente is null)
            {
                CambiarEstado(
                    resultado switch
                    {
                        AperturaDelPuente.SinBiblioteca => EstadoDelAnalizador.SinBiblioteca,
                        AperturaDelPuente.SinDispositivo => EstadoDelAnalizador.SinDispositivo,
                        _ => EstadoDelAnalizador.Fallo,
                    },
                    detalle);
                Esperar(token);
                continue;
            }

            using (puente)
            {
                _registro?.LogInformation("Analizador del FT-710: {Detalle}", detalle);
                LeerMientrasSePueda(puente, trama, token);
            }

            if (!token.IsCancellationRequested) Esperar(token);
        }
    }

    /// <summary>Lo que se dice cuando el puente no termina de abrirse.</summary>
    internal static string MotivoDeAperturaColgada => Textos.T("Servicios.Radio.Analizador.Colgado");

    /// <summary>
    /// Abre el puente sin quedarse mudo si el controlador de FTDI no vuelve (01-10-2026: el
    /// analizador se quedo sin decir nada ni en el registro ni en pantalla). Pasado el plazo lo
    /// dice y sigue esperando a ESA apertura; si se cancela, la deja pendiente para que el
    /// siguiente arranque la espere en vez de colgar otra encima.
    /// </summary>
    /// <returns>Como ha ido; nulo si se ha cancelado antes de saberlo.</returns>
    private (AperturaDelPuente Resultado, IPuenteDelAnalizador? Puente, string Detalle)? AbrirConPlazo(CancellationToken token)
    {
        Task<(AperturaDelPuente Resultado, IPuenteDelAnalizador? Puente, string Detalle)> apertura;
        lock (_cerrojo)
        {
            apertura = _apertura ??= Task.Factory.StartNew(
                _abrir, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        try
        {
            if (!apertura.Wait(_plazoDeApertura, token))
            {
                CambiarEstado(EstadoDelAnalizador.Fallo, MotivoDeAperturaColgada);
                apertura.Wait(token);
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (AggregateException ex)
        {
            lock (_cerrojo)
            {
                if (ReferenceEquals(_apertura, apertura)) _apertura = null;
            }

            return (AperturaDelPuente.Fallo, null,
                Textos.F("Servicios.Radio.Analizador.NoAbre", ex.InnerException?.Message ?? ex.Message));
        }

        lock (_cerrojo)
        {
            if (ReferenceEquals(_apertura, apertura)) _apertura = null;
        }

        return apertura.Result;
    }

    private void LeerMientrasSePueda(IPuenteDelAnalizador puente, byte[] trama, CancellationToken token)
    {
        var sincronizado = false;
        while (!token.IsCancellationRequested)
        {
            if (!sincronizado)
            {
                if (!Sincronizar(puente, token))
                {
                    CambiarEstado(
                        EstadoDelAnalizador.SinTramas,
                        Textos.T("Servicios.Radio.Analizador.SinTramas"));
                    return;
                }

                sincronizado = true;
            }

            if (!puente.Leer(trama))
            {
                CambiarEstado(EstadoDelAnalizador.SinDispositivo, Textos.T("Servicios.Radio.Analizador.Perdido"));
                return;
            }

            if (!TramaDelAnalizadorFt710.IntentarDescifrar(trama, out var traza) || traza is null)
            {
                sincronizado = false;
                continue;
            }

            TramasRecibidas++;
            CambiarEstado(EstadoDelAnalizador.Recibiendo, string.Empty);
            TrazaRecibida?.Invoke(this, traza);
        }
    }

    /// <summary>Lee byte a byte hasta ver la marca de fin cuatro veces seguidas.</summary>
    private static bool Sincronizar(IPuenteDelAnalizador puente, CancellationToken token)
    {
        Span<byte> uno = stackalloc byte[1];
        var cola = TramaDelAnalizadorFt710.Cola;
        var aciertos = 0;
        for (var i = 0; i < TopeDeSincronia && !token.IsCancellationRequested; i++)
        {
            if (!puente.Leer(uno)) return false;
            if (uno[0] == cola[aciertos % 4])
            {
                aciertos++;
                if (aciertos == 16) return true;
            }
            else
            {
                aciertos = uno[0] == cola[0] ? 1 : 0;
            }
        }

        return false;
    }

    private void Esperar(CancellationToken token) => token.WaitHandle.WaitOne(_pausaEntreIntentos);

    private void CambiarEstado(EstadoDelAnalizador estado, string motivo)
    {
        if (Estado == estado && Motivo == motivo) return;
        Estado = estado;
        Motivo = motivo;
        _registro?.LogInformation("Analizador del FT-710: {Estado} {Motivo}", estado, motivo);
        EstadoCambiado?.Invoke(this, EventArgs.Empty);
    }
}
