using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion.Geometria;
using Nodisla.Cuaderno.Propagacion.Prediccion;
using Nodisla.Cuaderno.Propagacion.Solar;

namespace Nodisla.Cuaderno.Propagacion;

/// <summary>
/// Propagacion: indices solares del NOAA, geometria del trayecto con paso gris y prediccion de
/// bandas.
/// </summary>
/// <remarks>
/// <para>
/// Los indices se guardan en disco, de modo que el cuaderno arranca con los ultimos conocidos
/// aunque no haya red. Lo que venga del fichero se marca como tal y <see cref="Lectura"/> lleva
/// la fecha de la medida, para que la interfaz pueda decir siempre de cuando son los datos.
/// </para>
/// <para>
/// La prediccion la hace el motor que se le pase. Por omision es la aproximacion propia, que se
/// declara como aproximacion en <see cref="EsAproximacion"/> y en
/// <see cref="MotorDePrediccion"/>.
/// </para>
/// </remarks>
public sealed class ServicioDePropagacion : IPropagacion
{
    private readonly OpcionesPropagacion ajustes;
    private readonly ProveedorDeIndicesSolaresSwpc proveedor;
    private readonly CacheDeIndicesEnDisco cache;
    private readonly IMotorDePrediccion motor;
    private readonly ILogger traza;
    private readonly TimeProvider reloj;
    private LecturaDeIndices? lectura;

    /// <summary>Crea el servicio.</summary>
    /// <param name="fabricaHttp">Fabrica de clientes HTTP.</param>
    /// <param name="opciones">Ajustes del modulo.</param>
    /// <param name="motorDePrediccion">Motor a usar. Por omision, la aproximacion propia.</param>
    /// <param name="registro">Registro opcional.</param>
    /// <param name="reloj">Reloj, para poder fijarlo en las pruebas.</param>
    public ServicioDePropagacion(
        IHttpClientFactory fabricaHttp,
        OpcionesPropagacion? opciones = null,
        IMotorDePrediccion? motorDePrediccion = null,
        ILogger<ServicioDePropagacion>? registro = null,
        TimeProvider? reloj = null)
    {
        ArgumentNullException.ThrowIfNull(fabricaHttp);

        ajustes = opciones ?? new OpcionesPropagacion();
        traza = registro ?? NullLogger<ServicioDePropagacion>.Instance;
        this.reloj = reloj ?? TimeProvider.System;
        motor = motorDePrediccion ?? new MotorAproximacionNodisla(ajustes);
        proveedor = new ProveedorDeIndicesSolaresSwpc(fabricaHttp, ajustes, null);
        cache = new CacheDeIndicesEnDisco(ajustes.RutaDeCache);

        // Arrancar con lo ultimo conocido es mejor que arrancar en blanco, siempre que se diga
        // que viene del fichero. Eso lo lleva la propia lectura.
        lectura = cache.Leer();
        if (lectura is not null)
        {
            traza.LogInformation(
                "Indices solares cargados del cache: {Descripcion}",
                lectura.Describir(this.reloj.GetUtcNow(), ajustes.Frescura));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// La frase de <see cref="IndicesSolares.Descripcion"/> se rehace en cada consulta, porque
    /// lleva dentro cuanto tiempo ha pasado y eso cambia solo con mirar el reloj. Guardarla de
    /// una vez haria que a las cinco horas siguiera diciendo "hace doce minutos".
    /// </remarks>
    public IndicesSolares? Indices => lectura is null ? null : Vestir(lectura, reloj.GetUtcNow());

    /// <inheritdoc/>
    public event EventHandler<IndicesSolares>? IndicesActualizados;

    /// <inheritdoc/>
    public async Task<IndicesSolares?> ActualizarIndicesAsync(CancellationToken ct = default)
    {
        var ahora = reloj.GetUtcNow();
        var nueva = await proveedor.TraerAsync(ahora, ct).ConfigureAwait(false);
        if (nueva is null)
        {
            // Sin red no se inventa nada: se deja lo que hubiera, que ya se ensena con su fecha.
            traza.LogWarning(
                "No hay indices solares nuevos; se mantiene {Descripcion}",
                Indices?.Descripcion ?? SinIndices);
            return Indices;
        }

        lectura = nueva;
        cache.Guardar(nueva);
        var vestidos = Vestir(nueva, ahora);
        traza.LogInformation("Indices solares actualizados: {Descripcion}", vestidos.Descripcion);
        IndicesActualizados?.Invoke(this, vestidos);
        return vestidos;
    }

    /// <summary>Frase que se ensena cuando nunca se ha podido traer nada.</summary>
    private const string SinIndices = "Sin índices solares: no se han podido traer todavía.";

    /// <summary>
    /// Anade a los indices lo que el puerto pide para que la interfaz pueda ser sincera: cuando
    /// se descargaron, si salen de la copia guardada y la frase que lo explica.
    /// </summary>
    private IndicesSolares Vestir(LecturaDeIndices leidos, DateTimeOffset ahora) => leidos.Indices with
    {
        DescargadoUtc = leidos.ObtenidoUtc,
        DeCache = leidos.Origen == OrigenDeLosIndices.Cache,
        Descripcion = leidos.Describir(ahora, ajustes.Frescura),
    };

    /// <inheritdoc/>
    public Trayecto CalcularTrayecto(Coordenada origen, Coordenada destino, DateTimeOffset momentoUtc) =>
        GeometriaDeTrayecto.Calcular(origen, destino, momentoUtc);

    /// <summary>
    /// Analisis completo del trayecto, con el Sol en los dos extremos y el detalle del paso gris.
    /// </summary>
    /// <param name="origen">Punto de partida.</param>
    /// <param name="destino">Punto de llegada.</param>
    /// <param name="momentoUtc">Momento para el que se calcula.</param>
    public AnalisisDeTrayecto AnalizarTrayecto(
        Coordenada origen,
        Coordenada destino,
        DateTimeOffset momentoUtc) =>
        GeometriaDeTrayecto.Analizar(origen, destino, momentoUtc);

    /// <inheritdoc/>
    public Task<IReadOnlyList<PrediccionDeBanda>> PredecirAsync(
        Coordenada origen,
        Coordenada destino,
        double potenciaVatios,
        DateTimeOffset momentoUtc,
        CancellationToken ct = default)
    {
        if (lectura is null)
        {
            traza.LogWarning(
                "Se predice sin indices solares: el resultado sale de condiciones medias supuestas.");
        }

        var solicitud = new SolicitudDePrediccion(origen, destino, potenciaVatios, momentoUtc, Indices);
        return motor.PredecirAsync(solicitud, ct);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Esto dice que motor esta configurado. Lo que vale para un resultado concreto es el
    /// <see cref="PrediccionDeBanda.Motor"/> de esa prediccion, que puede ser otro si el motor
    /// tuvo que estimar.
    /// </remarks>
    public string MotorDePrediccion => motor.Nombre;

    /// <inheritdoc/>
    /// <remarks>
    /// Igual que <see cref="MotorDePrediccion"/>: habla del motor configurado. Para saber si
    /// <b>una</b> prediccion es estimada hay que mirar su
    /// <see cref="PrediccionDeBanda.EsAproximacion"/>.
    /// </remarks>
    public bool EsAproximacion => motor.EsAproximacion;

    /// <summary>
    /// Motor externo instalado en la maquina, si lo hay. Hoy se detecta pero no se usa; lo
    /// explica <see cref="DetectorDeMotorExterno"/>.
    /// </summary>
    public MotorExternoEncontrado? BuscarMotorExterno() =>
        DetectorDeMotorExterno.Buscar(ajustes.RutaDelMotorExterno);
}
