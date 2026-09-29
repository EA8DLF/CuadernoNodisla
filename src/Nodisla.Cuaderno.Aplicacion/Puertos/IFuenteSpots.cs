using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Una referencia de programa de activacion que venia en el comentario de un anuncio.</summary>
/// <param name="Tipo">Programa al que pertenece.</param>
/// <param name="Codigo">Codigo de la referencia, tal y como venia.</param>
public sealed record ReferenciaAnunciada(TipoDeReferencia Tipo, string Codigo);

/// <summary>Un anuncio de una estacion oida, venga del cluster o de donde venga.</summary>
/// <param name="Indicativo">Estacion anunciada.</param>
/// <param name="Frecuencia">Frecuencia donde se la oye.</param>
/// <param name="Anunciante">Quien lo anuncia.</param>
/// <param name="Comentario">Texto libre del anuncio.</param>
/// <param name="RecibidoUtc">Momento en que llego el anuncio.</param>
/// <param name="Fuente">Nombre de la fuente, para poder distinguirlas en pantalla.</param>
public sealed record Spot(
    Indicativo Indicativo,
    Frecuencia Frecuencia,
    Indicativo Anunciante,
    string? Comentario,
    DateTimeOffset RecibidoUtc,
    string Fuente)
{
    /// <summary>Banda deducida de la frecuencia.</summary>
    public Banda Banda => Banda.DesdeFrecuencia(Frecuencia);

    /// <summary>Modo deducido del comentario, cuando el anuncio lo dice.</summary>
    public Modo ModoAnunciado { get; init; }

    /// <summary>Localizador de la estacion, que muchos nodos mandan detras de la hora.</summary>
    public Locator Locator { get; init; }

    /// <summary>
    /// Referencias de programas de activacion que venian en el comentario, por ejemplo
    /// <c>POTA EA8-0012</c> o <c>SOTA EA8/GC-001</c>.
    /// </summary>
    public IReadOnlyList<ReferenciaAnunciada> Referencias { get; init; } = [];

    /// <summary>Velocidad en palabras por minuto, cuando la fuente la da.</summary>
    public int? PalabrasPorMinuto { get; init; }

    /// <summary>
    /// El anuncio lo genero una estacion automatica de escucha, no una persona.
    /// </summary>
    /// <remarks>
    /// Importa para filtrar: una red de escucha automatica puede anunciar la misma estacion
    /// decenas de veces por minuto y ahogar los anuncios hechos por operadores.
    /// </remarks>
    public bool EsDeEscuchaAutomatica { get; init; }

    /// <summary>Entidad DXCC de la estacion anunciada, si se ha podido resolver.</summary>
    public int? Dxcc { get; init; }

    /// <summary>Nombre de la entidad, para mostrarlo sin volver a resolver.</summary>
    public string? Pais { get; init; }

    /// <summary>Continente de la entidad.</summary>
    public string? Continente { get; init; }

    /// <summary>Relacion senal-ruido en decibelios, cuando la fuente la da (skimmers).</summary>
    public int? Decibelios { get; init; }

    /// <summary>
    /// La estacion no esta trabajada en esta banda y modo. Lo rellena el cuaderno, no la fuente.
    /// </summary>
    public bool EsNuevoEnBandaYModo { get; init; }

    /// <summary>La entidad DXCC no esta trabajada en ninguna banda.</summary>
    public bool EsEntidadNueva { get; init; }
}

/// <summary>Como esta la conexion con una fuente de anuncios.</summary>
public enum EstadoDeConexion
{
    Desconectado,
    Conectando,
    /// <summary>Conectado y autenticado, recibiendo anuncios.</summary>
    Conectado,
    /// <summary>Se perdio la conexion y se esta reintentando.</summary>
    Reintentando,
    /// <summary>Fallo que no se arregla reintentando, como un indicativo rechazado.</summary>
    Fallido,
}

/// <summary>
/// Fuente de anuncios de estaciones oidas: un cluster de DX por Telnet, un agregador web
/// o cualquier otra cosa que escupa spots.
/// </summary>
public interface IFuenteSpots : IAsyncDisposable
{
    /// <summary>Nombre con el que se identifica la fuente en pantalla.</summary>
    string Nombre { get; }

    /// <summary>Estado de la conexion.</summary>
    EstadoDeConexion Estado { get; }

    /// <summary>Salta cuando cambia el estado de la conexion.</summary>
    event EventHandler<EstadoDeConexion>? EstadoCambiado;

    /// <summary>Salta con cada anuncio recibido.</summary>
    event EventHandler<Spot>? SpotRecibido;

    /// <summary>
    /// Salta con cada linea que la fuente envia y no es un anuncio: avisos del cluster,
    /// mensajes de otros operadores, respuestas a ordenes. El operador quiere verlas.
    /// </summary>
    event EventHandler<string>? LineaRecibida;

    /// <summary>
    /// Conecta y se mantiene conectado, reintentando si la conexion se cae.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La tarea termina cuando acaba el <b>primer</b> intento, haya salido bien o mal; la
    /// conexion se sigue manteniendo de fondo. Quien llame no debe entender que al volver ya
    /// esta conectado: eso lo dice <see cref="Estado"/> y lo avisa
    /// <see cref="EstadoCambiado"/>.
    /// </para>
    /// <para>
    /// Un cluster se cae a menudo y a horas intempestivas; reconectar solo es parte del
    /// trabajo, no un extra. Los reintentos deben espaciarse para no castigar al servidor.
    /// Un rechazo que no se arregla reintentando —un indicativo que el nodo no admite— debe
    /// dejar el estado en <see cref="EstadoDeConexion.Fallido"/> y parar.
    /// </para>
    /// </remarks>
    Task ConectarAsync(CancellationToken ct = default);

    Task DesconectarAsync(CancellationToken ct = default);

    /// <summary>Envia una orden en crudo a la fuente, si admite ordenes.</summary>
    Task EnviarAsync(string orden, CancellationToken ct = default);
}
