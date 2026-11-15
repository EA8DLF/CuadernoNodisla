namespace Nodisla.Cuaderno.Servicios.Red;

/// <summary>
/// El servicio ha rechazado el usuario o la contrasena, o tiene la cuenta bloqueada.
/// </summary>
/// <remarks>
/// Es una respuesta del servicio, pero no de las que se arreglan insistiendo, sino al reves:
/// cada intento de mas con una contrasena mala acerca el bloqueo de la cuenta. Quien la lanza
/// ya ha dejado el aviso en el registro <b>una vez</b>; quien la recibe no debe repetirlo en
/// cada consulta.
/// </remarks>
public sealed class SesionRechazadaException : RespuestaDelServicioException
{
    /// <summary>Crea la excepcion.</summary>
    /// <param name="mensaje">Explicacion para el operador.</param>
    /// <param name="bloqueadoHasta">Hasta cuando dura el bloqueo, si el servicio lo ha dicho.</param>
    public SesionRechazadaException(string mensaje, DateTimeOffset? bloqueadoHasta = null)
        : base(mensaje) => BloqueadoHasta = bloqueadoHasta;

    /// <summary>Hasta cuando no se puede volver a pedir sesion; nulo si no hay bloqueo con hora.</summary>
    public DateTimeOffset? BloqueadoHasta { get; }
}
