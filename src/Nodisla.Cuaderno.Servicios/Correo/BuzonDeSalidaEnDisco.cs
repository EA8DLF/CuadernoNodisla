using System.Text;

namespace Nodisla.Cuaderno.Servicios.Correo;

/// <summary>
/// Enviador de mentira: en vez de mandar el correo lo deja como <c>.eml</c> en una carpeta.
/// </summary>
/// <remarks>
/// Es el que se usa con <c>CUADERNO_SIMULADO</c> y en las pruebas: se ve exactamente lo que
/// habria salido —se abre el <c>.eml</c> con cualquier programa de correo— sin mandar nada a
/// nadie ni tocar la cuenta del operador.
/// </remarks>
public sealed class BuzonDeSalidaEnDisco : IEnviadorDeCorreo
{
    /// <summary>Crea el buzon.</summary>
    /// <param name="carpeta">Donde se dejan los correos.</param>
    public BuzonDeSalidaEnDisco(string carpeta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carpeta);
        Carpeta = carpeta;
    }

    /// <summary>Donde se dejan los correos.</summary>
    public string Carpeta { get; }

    /// <summary>Los correos «mandados» en esta sesion.</summary>
    public List<MensajeDeCorreo> Enviados { get; } = [];

    /// <inheritdoc />
    public Task EnviarAsync(MensajeDeCorreo mensaje, ConfiguracionSmtp configuracion, string? contrasena, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        ArgumentNullException.ThrowIfNull(configuracion);
        var remitente = DireccionDeCorreo.EsValida(configuracion.Remitente) ? configuracion.Remitente : "simulado@nodisla.invalid";
        var cuerpo = ComposicionMime.Componer(mensaje, remitente, configuracion.NombreDelRemitente, DateTimeOffset.Now);
        Directory.CreateDirectory(Carpeta);
        var nombre = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{new string(mensaje.Para.Where(char.IsAsciiLetterOrDigit).ToArray())}.eml";
        File.WriteAllText(Path.Combine(Carpeta, nombre), cuerpo, Encoding.ASCII);
        lock (Enviados) Enviados.Add(mensaje);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ProbarAsync(ConfiguracionSmtp configuracion, string? contrasena, CancellationToken ct = default) => Task.CompletedTask;
}
