using System.Text.Json.Serialization;

namespace Nodisla.Cuaderno.Servicios.Correo;

/// <summary>Como se cifra la conexion con el servidor de correo saliente.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SeguridadSmtp>))]
public enum SeguridadSmtp
{
    /// <summary>Se conecta en claro y se pasa a cifrado con STARTTLS (puerto 587, lo normal).</summary>
    StartTls = 0,

    /// <summary>Cifrado desde el primer byte (puerto 465, «SSL»).</summary>
    SslDirecto,

    /// <summary>Sin cifrar. Solo para un servidor en la propia maquina o la red de casa.</summary>
    Ninguna,
}

/// <summary>El servidor de correo saliente. La contraseña NO va aqui: va al almacen cifrado.</summary>
public sealed class ConfiguracionSmtp
{
    /// <summary>Servidor, por ejemplo <c>smtp.gmail.com</c>.</summary>
    public string Servidor { get; set; } = string.Empty;

    /// <summary>Puerto.</summary>
    public int Puerto { get; set; } = 587;

    /// <summary>Cifrado.</summary>
    public SeguridadSmtp Seguridad { get; set; } = SeguridadSmtp.StartTls;

    /// <summary>Usuario con el que se entra; vacio si el servidor no pide identificarse.</summary>
    public string? Usuario { get; set; }

    /// <summary>Direccion del remitente.</summary>
    public string Remitente { get; set; } = string.Empty;

    /// <summary>Nombre que se ve como remitente, por ejemplo «EA8DLF — José».</summary>
    public string? NombreDelRemitente { get; set; }

    /// <summary>Espera maxima por respuesta del servidor, en segundos.</summary>
    public int EsperaSegundos { get; set; } = 30;

    /// <summary>Hay lo minimo para intentar mandar.</summary>
    [JsonIgnore]
    public bool EstaCompleta =>
        !string.IsNullOrWhiteSpace(Servidor) && Puerto is > 0 and < 65536 && DireccionDeCorreo.EsValida(Remitente);
}

/// <summary>Un fichero adjunto.</summary>
/// <param name="Nombre">Nombre del fichero tal como lo vera el destinatario.</param>
/// <param name="TipoDeMedio">Tipo MIME, por ejemplo <c>image/jpeg</c>.</param>
/// <param name="Bytes">Contenido.</param>
public sealed record AdjuntoDeCorreo(string Nombre, string TipoDeMedio, byte[] Bytes);

/// <summary>Un correo listo para salir.</summary>
/// <param name="Para">Direccion del destinatario.</param>
/// <param name="Asunto">Asunto.</param>
/// <param name="Texto">Cuerpo en texto plano.</param>
/// <param name="Adjuntos">Adjuntos.</param>
public sealed record MensajeDeCorreo(string Para, string Asunto, string Texto, IReadOnlyList<AdjuntoDeCorreo> Adjuntos);

/// <summary>Quien manda los correos: el cliente SMTP de verdad o uno de mentira en pruebas.</summary>
public interface IEnviadorDeCorreo
{
    /// <summary>Manda un correo.</summary>
    /// <param name="mensaje">El correo.</param>
    /// <param name="configuracion">El servidor.</param>
    /// <param name="contrasena">La contraseña, leida del almacen cifrado justo antes; nula si no hace falta.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Tarea que acaba cuando el servidor ha aceptado el correo.</returns>
    /// <exception cref="ErrorDeCorreo">El servidor lo ha rechazado o no se ha podido hablar con el.</exception>
    Task EnviarAsync(MensajeDeCorreo mensaje, ConfiguracionSmtp configuracion, string? contrasena, CancellationToken ct = default);

    /// <summary>Conecta, se identifica y se despide sin mandar nada.</summary>
    /// <param name="configuracion">El servidor.</param>
    /// <param name="contrasena">La contraseña.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Tarea que acaba si todo ha ido bien.</returns>
    /// <exception cref="ErrorDeCorreo">Algo ha fallado; el mensaje dice que.</exception>
    Task ProbarAsync(ConfiguracionSmtp configuracion, string? contrasena, CancellationToken ct = default);
}

/// <summary>Fallo al mandar un correo, con un mensaje que se puede enseñar al operador.</summary>
public sealed class ErrorDeCorreo : Exception
{
    /// <summary>Crea el error.</summary>
    public ErrorDeCorreo()
    {
    }

    /// <summary>Crea el error.</summary>
    /// <param name="message">Lo que ha pasado, en español.</param>
    public ErrorDeCorreo(string message)
        : base(message)
    {
    }

    /// <summary>Crea el error.</summary>
    /// <param name="message">Lo que ha pasado, en español.</param>
    /// <param name="innerException">La causa.</param>
    public ErrorDeCorreo(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Codigo de respuesta SMTP, si lo hubo.</summary>
    public int? Codigo { get; init; }

    /// <summary>
    /// El fallo es del destinatario (buzon inexistente, dominio mal escrito) y no del servidor:
    /// en un envio por lotes se apunta y se sigue con el siguiente.
    /// </summary>
    public bool EsDelDestinatario { get; init; }
}

/// <summary>Comprobacion basica de direcciones de correo.</summary>
public static class DireccionDeCorreo
{
    /// <summary>La direccion tiene forma de direccion: algo, una arroba y un dominio con punto.</summary>
    /// <param name="direccion">La direccion.</param>
    /// <returns>Si parece valida.</returns>
    /// <remarks>
    /// No se intenta validar el RFC entero: se rechaza lo que seguro que no es una direccion y
    /// lo que podria colar una orden en la conversacion SMTP (saltos de linea, «&lt;», «&gt;»).
    /// </remarks>
    public static bool EsValida(string? direccion)
    {
        if (string.IsNullOrWhiteSpace(direccion)) return false;
        var d = direccion.Trim();
        if (d.Length > 254 || d.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>' or ',' or ';' or '"')) return false;
        var arroba = d.LastIndexOf('@');
        if (arroba <= 0 || arroba == d.Length - 1 || d.IndexOf('@') != arroba) return false;
        var dominio = d[(arroba + 1)..];
        return dominio.Contains('.', StringComparison.Ordinal) && !dominio.StartsWith('.') && !dominio.EndsWith('.');
    }
}
