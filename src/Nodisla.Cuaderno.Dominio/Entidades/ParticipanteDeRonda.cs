using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>
/// Un participante de una <see cref="RondaDeControl"/>: su indicativo, cuando entro, el informe
/// de senal en los dos sentidos y un comentario corto. Es el «QSO activo» de la ronda mientras
/// no se confirma; al marcarlo como trabajado se convierte en un contacto real del cuaderno.
/// </summary>
public sealed class ParticipanteDeRonda
{
    public long Id { get; set; }

    /// <summary>Ronda a la que pertenece.</summary>
    public long RondaId { get; set; }

    /// <summary>Indicativo del participante.</summary>
    public Indicativo Call { get; set; }

    /// <summary>Instante en que entro en la ronda.</summary>
    public DateTimeOffset EntradaUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Informe de senal enviado.</summary>
    public Informe RstEnviado { get; set; }

    /// <summary>Informe de senal recibido.</summary>
    public Informe RstRecibido { get; set; }

    /// <summary>Comentario corto: nombre, QTH, lo que haga falta anotar al vuelo.</summary>
    public string? Comentario { get; set; }

    /// <summary>Entidad DXCC resuelta para el indicativo, si se pudo determinar.</summary>
    public int? Dxcc { get; set; }

    /// <summary>Nombre del pais resuelto, para mostrarlo en la lista sin volver a consultar.</summary>
    public string? Pais { get; set; }

    /// <summary>Se ha confirmado como contacto de verdad.</summary>
    public bool Trabajado { get; set; }

    /// <summary>Identificador del QSO real del cuaderno, una vez confirmado.</summary>
    public long? QsoId { get; set; }
}
