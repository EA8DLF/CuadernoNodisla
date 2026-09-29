using System.Globalization;
using System.Text;

namespace Nodisla.Cuaderno.Servicios.Correo;

/// <summary>
/// Escribe un correo en formato MIME: cabeceras, texto y adjuntos.
/// </summary>
/// <remarks>
/// <para>
/// Todo el contenido va en base64 —el texto tambien— y las cabeceras con acentos en la forma
/// codificada de RFC 2047. Asi el mensaje entero es ASCII de lineas cortas: ningun servidor lo
/// reescribe por el camino y ninguna linea empieza por un punto, que en SMTP es el final.
/// </para>
/// </remarks>
public static class ComposicionMime
{
    /// <summary>Compone el mensaje entero, listo para mandar tras la orden DATA.</summary>
    /// <param name="mensaje">El correo.</param>
    /// <param name="remitente">Direccion del remitente.</param>
    /// <param name="nombreDelRemitente">Nombre visible del remitente.</param>
    /// <param name="ahora">Fecha del mensaje.</param>
    /// <returns>El mensaje, con saltos CRLF y sin el punto final.</returns>
    public static string Componer(MensajeDeCorreo mensaje, string remitente, string? nombreDelRemitente, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        if (!DireccionDeCorreo.EsValida(remitente)) throw new ErrorDeCorreo($"La dirección del remitente no es válida: «{remitente}».");
        if (!DireccionDeCorreo.EsValida(mensaje.Para)) throw new ErrorDeCorreo($"La dirección del destinatario no es válida: «{mensaje.Para}».") { EsDelDestinatario = true };

        var dominio = remitente[(remitente.LastIndexOf('@') + 1)..].Trim();
        var frontera = "=_nodisla_" + Guid.NewGuid().ToString("N");
        var s = new StringBuilder();

        var de = string.IsNullOrWhiteSpace(nombreDelRemitente)
            ? $"<{remitente.Trim()}>"
            : $"{Codificar(nombreDelRemitente.Trim())} <{remitente.Trim()}>";
        Cabecera(s, "From", de);
        Cabecera(s, "To", $"<{mensaje.Para.Trim()}>");
        Cabecera(s, "Subject", Codificar(mensaje.Asunto ?? string.Empty));
        Cabecera(s, "Date", ahora.ToString("ddd, dd MMM yyyy HH:mm:ss ", CultureInfo.InvariantCulture) + ahora.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", string.Empty, StringComparison.Ordinal));
        Cabecera(s, "Message-ID", $"<{Guid.NewGuid():N}@{dominio}>");
        Cabecera(s, "X-Mailer", "Cuaderno NODISLA");
        Cabecera(s, "MIME-Version", "1.0");

        var adjuntos = mensaje.Adjuntos ?? [];
        if (adjuntos.Count == 0)
        {
            Cabecera(s, "Content-Type", "text/plain; charset=utf-8");
            Cabecera(s, "Content-Transfer-Encoding", "base64");
            s.Append("\r\n");
            Base64(s, Encoding.UTF8.GetBytes(Normalizar(mensaje.Texto)));
            return s.ToString();
        }

        Cabecera(s, "Content-Type", $"multipart/mixed; boundary=\"{frontera}\"");
        s.Append("\r\n");
        s.Append("Este mensaje tiene varias partes.\r\n");

        s.Append("--").Append(frontera).Append("\r\n");
        Cabecera(s, "Content-Type", "text/plain; charset=utf-8");
        Cabecera(s, "Content-Transfer-Encoding", "base64");
        s.Append("\r\n");
        Base64(s, Encoding.UTF8.GetBytes(Normalizar(mensaje.Texto)));

        foreach (var adjunto in adjuntos)
        {
            var nombre = NombreSeguro(adjunto.Nombre);
            s.Append("--").Append(frontera).Append("\r\n");
            Cabecera(s, "Content-Type", $"{TipoSeguro(adjunto.TipoDeMedio)}; name=\"{nombre}\"");
            Cabecera(s, "Content-Transfer-Encoding", "base64");
            Cabecera(s, "Content-Disposition", $"attachment; filename=\"{nombre}\"");
            s.Append("\r\n");
            Base64(s, adjunto.Bytes);
        }

        s.Append("--").Append(frontera).Append("--\r\n");
        return s.ToString();
    }

    /// <summary>Codifica un texto de cabecera si lleva algo que no sea ASCII imprimible.</summary>
    /// <param name="texto">El texto.</param>
    /// <returns>El texto tal cual o en la forma <c>=?utf-8?B?...?=</c>.</returns>
    public static string Codificar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        texto = texto.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        if (texto.All(c => c is >= ' ' and <= '~') && !texto.Contains("=?", StringComparison.Ordinal)) return texto;

        // Trozos de 45 bytes (60 caracteres en base64) para que ninguna linea pase de 76.
        var bytes = Encoding.UTF8.GetBytes(texto);
        var trozos = new List<string>();
        var inicio = 0;
        while (inicio < bytes.Length)
        {
            var largo = Math.Min(45, bytes.Length - inicio);

            // No se parte un caracter UTF-8 por la mitad: se retrocede hasta el principio de uno.
            while (inicio + largo < bytes.Length && (bytes[inicio + largo] & 0xC0) == 0x80) largo--;
            trozos.Add("=?utf-8?B?" + Convert.ToBase64String(bytes, inicio, largo) + "?=");
            inicio += largo;
        }

        return string.Join("\r\n ", trozos);
    }

    private static void Cabecera(StringBuilder s, string nombre, string valor) =>
        s.Append(nombre).Append(": ").Append(valor).Append("\r\n");

    private static void Base64(StringBuilder s, byte[] bytes)
    {
        var texto = Convert.ToBase64String(bytes);
        for (var i = 0; i < texto.Length; i += 76)
        {
            s.Append(texto, i, Math.Min(76, texto.Length - i)).Append("\r\n");
        }
    }

    private static string Normalizar(string? texto) =>
        (texto ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Replace("\n", "\r\n", StringComparison.Ordinal);

    private static string NombreSeguro(string nombre)
    {
        var limpio = new string((nombre ?? "adjunto").Select(c => c is >= ' ' and <= '~' && c is not '"' and not '\\' ? c : '_').ToArray());
        return limpio.Length == 0 ? "adjunto" : limpio;
    }

    private static string TipoSeguro(string tipo) =>
        !string.IsNullOrWhiteSpace(tipo) && tipo.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '+' or '.')
            ? tipo
            : "application/octet-stream";
}
