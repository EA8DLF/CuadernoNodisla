using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Que le ha pasado a un contacto en el programa de concurso.</summary>
public enum AccionSobreContacto
{
    /// <summary>Contacto nuevo.</summary>
    Anadido,

    /// <summary>Contacto corregido despues de haberlo dado por bueno.</summary>
    Reemplazado,

    /// <summary>Contacto borrado.</summary>
    Borrado,
}

/// <summary>Base de los avisos que manda un programa de concurso.</summary>
/// <param name="Aplicacion">Programa que lo envia.</param>
/// <param name="NombreDeEstacion">Nombre de red del ordenador que lo envia.</param>
public abstract record MensajeDeConcurso(string? Aplicacion, string? NombreDeEstacion);

/// <summary>
/// Estado de una de las radios del puesto de concurso.
/// </summary>
/// <remarks>
/// Llega varias veces por segundo mientras se gira el dial, asi que la interfaz debe tratarlo
/// como un caudal y no como un suceso. En montajes de dos radios llega uno por cada una.
/// </remarks>
/// <param name="Aplicacion">Programa que lo envia.</param>
/// <param name="NombreDeEstacion">Nombre de red del ordenador.</param>
/// <param name="NumeroDeRadio">Cual de las radios.</param>
/// <param name="Frecuencia">Frecuencia de recepcion.</param>
/// <param name="FrecuenciaTx">Frecuencia de transmision, distinta al trabajar en split.</param>
/// <param name="Modo">Modo en uso, tal y como lo nombra el programa.</param>
/// <param name="IndicativoOperador">Indicativo del operador a los mandos.</param>
/// <param name="EnLlamada">Esta llamando y no a la caza.</param>
/// <param name="Transmitiendo">Esta transmitiendo.</param>
/// <param name="EnSplit">Trabaja en dos frecuencias.</param>
/// <param name="NombreDeRadio">Nombre del equipo.</param>
/// <param name="Antena">Antena seleccionada.</param>
public sealed record EstadoDeRadioConcurso(
    string? Aplicacion,
    string? NombreDeEstacion,
    int NumeroDeRadio,
    Frecuencia Frecuencia,
    Frecuencia FrecuenciaTx,
    string? Modo,
    string? IndicativoOperador,
    bool EnLlamada,
    bool Transmitiendo,
    bool EnSplit,
    string? NombreDeRadio,
    string? Antena) : MensajeDeConcurso(Aplicacion, NombreDeEstacion)
{
    /// <summary>Banda deducida de la frecuencia de recepcion.</summary>
    public Banda Banda => Banda.DesdeFrecuencia(Frecuencia);
}

/// <summary>Un contacto de concurso, tal y como lo anuncia el programa al darlo de alta o al corregirlo.</summary>
/// <param name="Accion">Si es alta o correccion.</param>
/// <param name="Aplicacion">Programa que lo envia.</param>
/// <param name="NombreDeEstacion">Nombre de red del ordenador.</param>
/// <param name="Identificador">Identificador unico del contacto en el programa de concurso.</param>
/// <param name="InstanteUtc">Momento del contacto.</param>
/// <param name="Call">Indicativo del corresponsal.</param>
/// <param name="MiIndicativo">Indicativo con el que se transmitio.</param>
public sealed record ContactoDeConcurso(
    AccionSobreContacto Accion,
    string? Aplicacion,
    string? NombreDeEstacion,
    string? Identificador,
    DateTimeOffset InstanteUtc,
    Indicativo Call,
    Indicativo MiIndicativo) : MensajeDeConcurso(Aplicacion, NombreDeEstacion)
{
    /// <summary>Nombre del concurso.</summary>
    public string? Concurso { get; init; }

    /// <summary>Numero de concurso dentro de la base de datos del programa.</summary>
    public int? NumeroDeConcurso { get; init; }

    /// <summary>Frecuencia de recepcion.</summary>
    public Frecuencia FrecuenciaRx { get; init; }

    /// <summary>Frecuencia de transmision.</summary>
    public Frecuencia FrecuenciaTx { get; init; }

    /// <summary>Modo del contacto, tal y como lo nombra el programa.</summary>
    public string? Modo { get; init; }

    /// <summary>Operador a los mandos.</summary>
    public string? Operador { get; init; }

    /// <summary>Informe enviado.</summary>
    public string? InformeEnviado { get; init; }

    /// <summary>Numero de serie enviado.</summary>
    public int? SerieEnviada { get; init; }

    /// <summary>Informe recibido.</summary>
    public string? InformeRecibido { get; init; }

    /// <summary>Numero de serie recibido.</summary>
    public int? SerieRecibida { get; init; }

    /// <summary>Intercambio recibido que no es numero de serie: seccion, provincia, edad…</summary>
    public string? Intercambio { get; init; }

    /// <summary>Seccion, en los concursos que la usan.</summary>
    public string? Seccion { get; init; }

    /// <summary>Localizador del corresponsal.</summary>
    public Locator Locator { get; init; }

    /// <summary>Comentario del contacto.</summary>
    public string? Comentario { get; init; }

    /// <summary>Localidad del corresponsal.</summary>
    public string? Qth { get; init; }

    /// <summary>Nombre del corresponsal.</summary>
    public string? Nombre { get; init; }

    /// <summary>Potencia declarada en vatios.</summary>
    public double? Potencia { get; init; }

    /// <summary>Prefijo de pais.</summary>
    public string? PrefijoDePais { get; init; }

    /// <summary>Prefijo WPX.</summary>
    public string? PrefijoWpx { get; init; }

    /// <summary>Continente.</summary>
    public string? Continente { get; init; }

    /// <summary>Zona CQ o ITU, segun el concurso.</summary>
    public int? Zona { get; init; }

    /// <summary>Puntos que el programa le atribuye.</summary>
    public int? Puntos { get; init; }

    /// <summary>Numero de la radio con la que se hizo.</summary>
    public int? NumeroDeRadio { get; init; }

    /// <summary>El contacto se hizo llamando y no a la caza.</summary>
    public bool? EnLlamada { get; init; }

    /// <summary>Indicativo anterior, cuando el mensaje es una correccion.</summary>
    public Indicativo CallAnterior { get; init; }

    /// <summary>Instante anterior, cuando el mensaje es una correccion.</summary>
    public DateTimeOffset? InstanteAnteriorUtc { get; init; }

    /// <summary>
    /// Todos los campos del mensaje tal y como vinieron. Un programa de concurso manda mas de
    /// cuarenta y cada concurso usa los suyos; guardarlos en crudo evita tener que adivinar
    /// cuales importan antes de saber en que concurso estamos.
    /// </summary>
    public IReadOnlyDictionary<string, string> Campos { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Aviso de que un contacto se ha borrado.</summary>
/// <param name="Aplicacion">Programa que lo envia.</param>
/// <param name="NombreDeEstacion">Nombre de red del ordenador.</param>
/// <param name="Identificador">Identificador unico del contacto en el programa de concurso.</param>
/// <param name="InstanteUtc">Momento del contacto borrado.</param>
/// <param name="Call">Indicativo del contacto borrado.</param>
/// <param name="NumeroDeConcurso">Numero de concurso al que pertenecia.</param>
public sealed record BorradoDeConcurso(
    string? Aplicacion,
    string? NombreDeEstacion,
    string? Identificador,
    DateTimeOffset InstanteUtc,
    Indicativo Call,
    int? NumeroDeConcurso) : MensajeDeConcurso(Aplicacion, NombreDeEstacion);

/// <summary>
/// Puente con un programa de concurso.
/// </summary>
/// <remarks>
/// El que hay al otro lado es N1MM+, que reparte XML en texto plano por UDP y no espera
/// respuesta. La interfaz se queda con esta forma y no con la del XML para que el dia que
/// aparezca un DXLog o un Win-Test solo haya que escribir otro adaptador.
/// </remarks>
public interface IPuenteConcurso : IAsyncDisposable
{
    /// <summary>Puerto UDP en el que se escucha.</summary>
    int Puerto { get; }

    /// <summary>Salta con cada aviso de estado de una radio.</summary>
    event EventHandler<EstadoDeRadioConcurso>? RadioRecibida;

    /// <summary>
    /// Salta con cada contacto nuevo, ya armado y listo para guardarlo en el cuaderno.
    /// </summary>
    event EventHandler<Qso>? QsoRegistrado;

    /// <summary>
    /// Salta cuando el programa corrige un contacto que ya habia enviado. Llega el contacto
    /// nuevo y, cuando el programa lo dice, tambien el indicativo y la hora que tenia antes.
    /// </summary>
    event EventHandler<ContactoDeConcurso>? ContactoReemplazado;

    /// <summary>Salta cuando el programa borra un contacto.</summary>
    event EventHandler<BorradoDeConcurso>? ContactoBorrado;

    /// <summary>Empieza a escuchar.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task ArrancarAsync(CancellationToken ct = default);

    /// <summary>Deja de escuchar.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task PararAsync(CancellationToken ct = default);
}
