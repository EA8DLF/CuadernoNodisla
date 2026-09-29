using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Servicios.Red;

/// <summary>
/// Reintentos con retardo creciente para las llamadas a los servicios de confirmacion.
/// </summary>
/// <remarks>
/// <para>
/// LoTW, eQSL y Club Log se caen, se ponen en mantenimiento y responden despacio con toda
/// normalidad. Un unico intento convierte cada bache en un error para el operador, y un
/// reintento inmediato solo empeora la congestion; de ahi el retardo creciente con dispersion.
/// </para>
/// <para>
/// Solo se reintenta lo que puede arreglarse solo: fallos de red, tiempos de espera agotados,
/// <c>429</c> y <c>5xx</c>. Un <c>401</c> o un <c>404</c> no mejoran por insistir.
/// </para>
/// </remarks>
public sealed class PoliticaDeReintentos
{
    private readonly ILogger _log;
    private readonly Func<TimeSpan, CancellationToken, Task> _esperar;
    private readonly Random _dispersion = new();

    /// <summary>Numero total de intentos, incluido el primero.</summary>
    public int Intentos { get; init; } = 3;

    /// <summary>Retardo antes del segundo intento.</summary>
    public TimeSpan RetardoInicial { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Por cuanto se multiplica el retardo en cada intento.</summary>
    public double Factor { get; init; } = 2.0;

    /// <summary>Retardo maximo, por si el factor se dispara.</summary>
    public TimeSpan RetardoMaximo { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Crea la politica.</summary>
    /// <param name="log">Registro de trazas.</param>
    /// <param name="esperar">
    /// Como esperar entre intentos. Se puede sustituir en las pruebas para que no tarden
    /// treinta segundos en comprobar el retardo creciente.
    /// </param>
    public PoliticaDeReintentos(
        ILogger<PoliticaDeReintentos>? log = null,
        Func<TimeSpan, CancellationToken, Task>? esperar = null)
    {
        _log = log ?? NullLogger<PoliticaDeReintentos>.Instance;
        _esperar = esperar ?? Task.Delay;
    }

    /// <summary>Retardo que corresponde antes del intento indicado, empezando en 1.</summary>
    /// <param name="intento">Numero del intento que se va a hacer, empezando en 1.</param>
    public TimeSpan RetardoDe(int intento)
    {
        if (intento <= 1) return TimeSpan.Zero;
        var ticks = RetardoInicial.Ticks * Math.Pow(Factor, intento - 2);
        var tope = Math.Min(ticks, RetardoMaximo.Ticks);
        return TimeSpan.FromTicks((long)tope);
    }

    /// <summary>
    /// Ejecuta la llamada y la reintenta si el fallo es de los que se arreglan solos.
    /// </summary>
    /// <typeparam name="T">Tipo del resultado.</typeparam>
    /// <param name="descripcion">Que se esta haciendo, para las trazas. Nunca una credencial.</param>
    /// <param name="accion">Llamada a ejecutar.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<T> EjecutarAsync<T>(
        string descripcion,
        Func<CancellationToken, Task<T>> accion,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accion);

        Exception? ultimo = null;
        for (var intento = 1; intento <= Intentos; intento++)
        {
            if (intento > 1)
            {
                var retardo = ConDispersion(RetardoDe(intento));
                _log.LogInformation(
                    "Reintentando {Descripcion}: intento {Intento} de {Total} tras {Retardo}.",
                    descripcion, intento, Intentos, retardo);
                await _esperar(retardo, ct).ConfigureAwait(false);
            }

            try
            {
                return await accion(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (EsRecuperable(ex))
            {
                ultimo = ex;
                _log.LogWarning(
                    "Fallo recuperable en {Descripcion}, intento {Intento} de {Total}: {Motivo}",
                    descripcion, intento, Intentos, ex.Message);
            }
        }

        throw new ServicioNoDisponibleException(
            $"El servicio no respondió a «{descripcion}» tras {Intentos} intentos.", ultimo);
    }

    /// <summary>Indica si un fallo merece otro intento.</summary>
    /// <param name="ex">Excepcion capturada.</param>
    public static bool EsRecuperable(Exception ex) => ex switch
    {
        // El tiempo de espera del HttpClient llega como cancelacion sin testigo pedido.
        TaskCanceledException => true,
        TimeoutException => true,
        HttpRequestException http => EsRecuperable(http.StatusCode),
        RespuestaDelServicioException respuesta => respuesta.Codigo is not null && EsRecuperable(respuesta.Codigo),
        _ => false,
    };

    /// <summary>Indica si un codigo de estado merece otro intento.</summary>
    /// <param name="codigo">Codigo HTTP devuelto, si se conoce.</param>
    public static bool EsRecuperable(HttpStatusCode? codigo) => codigo switch
    {
        null => true, // Sin respuesta: la conexion ni se llego a establecer.
        HttpStatusCode.RequestTimeout => true,
        HttpStatusCode.TooManyRequests => true,
        >= HttpStatusCode.InternalServerError => true,
        _ => false,
    };

    /// <summary>
    /// Anade hasta un 20 % de dispersion. Sin ella, veinte instalaciones que fallan a la vez
    /// vuelven a llamar exactamente a la vez y el servicio no levanta cabeza.
    /// </summary>
    private TimeSpan ConDispersion(TimeSpan retardo)
    {
        if (retardo <= TimeSpan.Zero) return retardo;
        var factor = 1.0 + (_dispersion.NextDouble() * 0.2);
        return TimeSpan.FromTicks((long)(retardo.Ticks * factor));
    }
}

/// <summary>El servicio no respondio despues de agotar los reintentos.</summary>
public sealed class ServicioNoDisponibleException : Exception
{
    /// <summary>Crea la excepcion.</summary>
    /// <param name="mensaje">Explicacion para el operador.</param>
    /// <param name="interna">Ultimo fallo observado.</param>
    public ServicioNoDisponibleException(string mensaje, Exception? interna = null)
        : base(mensaje, interna) { }
}

/// <summary>El servicio respondio, pero con un error.</summary>
public sealed class RespuestaDelServicioException : Exception
{
    /// <summary>Crea la excepcion.</summary>
    /// <param name="mensaje">Explicacion para el operador.</param>
    /// <param name="codigo">Codigo HTTP, si lo hubo.</param>
    public RespuestaDelServicioException(string mensaje, HttpStatusCode? codigo = null)
        : base(mensaje) => Codigo = codigo;

    /// <summary>Codigo HTTP devuelto por el servicio, si lo hubo.</summary>
    public HttpStatusCode? Codigo { get; }
}
