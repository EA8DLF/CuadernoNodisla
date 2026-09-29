using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Dxcc;

/// <summary>Resultado de identificar un indicativo contra el listado DXCC.</summary>
public sealed record ResultadoDxcc
{
    /// <summary>Entidad identificada. Nulo si no se ha podido determinar.</summary>
    public EntidadDxcc? Entidad { get; init; }

    /// <summary>Zona CQ concreta, que puede diferir de la de la entidad.</summary>
    public int? ZonaCq { get; init; }

    /// <summary>Zona ITU concreta, que puede diferir de la de la entidad.</summary>
    public int? ZonaItu { get; init; }

    /// <summary>Continente concreto, que puede diferir del de la entidad.</summary>
    public string? Continente { get; init; }

    /// <summary>Coordenada concreta del prefijo, mas precisa que el centro de la entidad.</summary>
    public Coordenada? Coordenada { get; init; }

    /// <summary>Prefijo del fichero de paises que produjo la coincidencia.</summary>
    public string? PrefijoCoincidente { get; init; }

    /// <summary>
    /// El indicativo coincidio con una excepcion nominal del fichero de paises, no con un
    /// patron de prefijo. Estas coincidencias son fiables; las de prefijo, no siempre.
    /// </summary>
    public bool EsCoincidenciaExacta { get; init; }

    /// <summary>
    /// El indicativo lleva sufijos que impiden decidir con certeza, por ejemplo <c>/MM</c>
    /// (movil maritimo, sin entidad) o un prefijo ajeno anadido. Conviene avisar al operador.
    /// </summary>
    public bool NecesitaRevision { get; init; }

    public bool EsDesconocido => Entidad is null;

    public static ResultadoDxcc Desconocido { get; } = new() { NecesitaRevision = true };
}

/// <summary>
/// Identifica la entidad DXCC de un indicativo.
/// </summary>
/// <remarks>
/// La fecha del contacto importa: las entidades nacen y se borran, y un mismo prefijo ha
/// pertenecido a entidades distintas en epocas distintas. Un cuaderno que ignore la fecha
/// asigna mal los contactos antiguos y falsea los diplomas.
/// </remarks>
public interface IResolutorDxcc
{
    /// <summary>Identifica la entidad de un indicativo para una fecha concreta.</summary>
    ResultadoDxcc Resolver(Indicativo indicativo, DateOnly fecha);

    /// <summary>Busca una entidad por su numero DXCC.</summary>
    EntidadDxcc? PorNumero(int numero);

    /// <summary>Todas las entidades conocidas, vigentes y borradas.</summary>
    IReadOnlyList<EntidadDxcc> Todas { get; }

    /// <summary>Fecha de edicion del fichero de paises cargado, para poder avisar si esta viejo.</summary>
    DateOnly? FechaDeLosDatos { get; }
}
