namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>
/// Estado de confirmacion de un contacto por una via concreta.
/// </summary>
/// <remarks>
/// Log4OM guarda esto como un JSON dentro de una columna de texto, lo que impide consultarlo
/// con SQL y obliga a calcular los diplomas recorriendo todo el cuaderno en memoria. Aqui es
/// una tabla hija indexada: "cuantos DXCC confirmados por LoTW en 20 metros" pasa a ser una
/// consulta, no un recorrido.
/// </remarks>
public sealed class QsoConfirmacion
{
    public long Id { get; set; }

    public long QsoId { get; set; }

    /// <summary>Via de la confirmacion.</summary>
    public MedioDeConfirmacion Medio { get; set; }

    /// <summary>Estado de lo enviado por mi parte.</summary>
    public EstadoDeConfirmacion Enviado { get; set; } = EstadoDeConfirmacion.Ninguno;

    /// <summary>Estado de lo recibido de la otra parte.</summary>
    public EstadoDeConfirmacion Recibido { get; set; } = EstadoDeConfirmacion.Ninguno;

    /// <summary>Fecha del envio, en UTC.</summary>
    public DateTimeOffset? EnviadoUtc { get; set; }

    /// <summary>Fecha de la recepcion, en UTC.</summary>
    public DateTimeOffset? RecibidoUtc { get; set; }

    /// <summary>Via de envio de la tarjeta, solo para el medio en papel.</summary>
    public ViaDeEnvio Via { get; set; } = ViaDeEnvio.Ninguna;

    /// <summary>Comentario del servicio o anotacion propia sobre esta confirmacion.</summary>
    public string? Nota { get; set; }

    /// <summary>La confirmacion cuenta para diplomas.</summary>
    public bool EstaConfirmada =>
        Recibido is EstadoDeConfirmacion.Confirmado or EstadoDeConfirmacion.Verificado;

    /// <summary>El servicio verifico la confirmacion, no solo la registro.</summary>
    public bool EstaVerificada => Recibido == EstadoDeConfirmacion.Verificado;
}
