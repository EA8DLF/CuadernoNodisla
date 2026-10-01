using System.Globalization;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Nodisla.Cuaderno.Servicios.Correo;

/// <summary>
/// Cliente SMTP propio: STARTTLS, SSL directo (465) o en claro, con AUTH PLAIN o LOGIN.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que no <c>System.Net.Mail.SmtpClient</c>.</b> Microsoft lo da por obsoleto para
/// codigo nuevo y, sobre todo, no sabe hablar SSL directo en el puerto 465: solo STARTTLS. Hay
/// proveedores (y muchos de los de aqui) que solo abren el 465. MailKit lo resuelve, pero es un
/// paquete mas; SMTP para mandar un correo son siete ordenes, asi que se escribe aqui, sin
/// dependencias, como el resto del programa.
/// </para>
/// <para>
/// <b>La contraseña nunca viaja en claro.</b> Si el servidor no es la propia maquina y la
/// conexion no esta cifrada, no se manda: se falla con un aviso. Tampoco se escribe nunca en el
/// registro: lo que se apunta son las ordenes, y la de identificarse se apunta sin argumentos.
/// </para>
/// </remarks>
public sealed class ClienteSmtp : IEnviadorDeCorreo
{
    private readonly ILogger<ClienteSmtp>? _log;

    /// <summary>Crea el cliente.</summary>
    /// <param name="log">Registro; opcional.</param>
    public ClienteSmtp(ILogger<ClienteSmtp>? log = null) => _log = log;

    /// <summary>
    /// Validacion del certificado del servidor. Nula: la del sistema, que es lo que hay que usar.
    /// </summary>
    /// <remarks>Solo las pruebas, con un servidor de mentira y certificado propio, la cambian.</remarks>
    public RemoteCertificateValidationCallback? ValidarCertificado { get; init; }

    /// <summary>Reloj para la cabecera Date.</summary>
    public Func<DateTimeOffset> Reloj { get; init; } = () => DateTimeOffset.Now;

    /// <inheritdoc />
    public async Task EnviarAsync(MensajeDeCorreo mensaje, ConfiguracionSmtp configuracion, string? contrasena, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        ArgumentNullException.ThrowIfNull(configuracion);

        // Se compone ANTES de conectar: si la direccion esta mal no se molesta al servidor.
        var cuerpo = ComposicionMime.Componer(mensaje, configuracion.Remitente, configuracion.NombreDelRemitente, Reloj());

        await using var sesion = await AbrirAsync(configuracion, contrasena, ct).ConfigureAwait(false);
        await sesion.OrdenAsync($"MAIL FROM:<{configuracion.Remitente.Trim()}>", 250, ct).ConfigureAwait(false);

        try
        {
            await sesion.OrdenAsync($"RCPT TO:<{mensaje.Para.Trim()}>", [250, 251], ct).ConfigureAwait(false);
        }
        catch (ErrorDeCorreo ex) when (ex.Codigo is >= 500 and < 600)
        {
            throw new ErrorDeCorreo($"El servidor no acepta la dirección {mensaje.Para}: {ex.Message}", ex)
            {
                Codigo = ex.Codigo,
                EsDelDestinatario = true,
            };
        }

        await sesion.OrdenAsync("DATA", 354, ct).ConfigureAwait(false);
        await sesion.DatosAsync(cuerpo, ct).ConfigureAwait(false);
        await sesion.DespedirseAsync(ct).ConfigureAwait(false);
        _log?.LogInformation("Correo entregado al servidor {Servidor} para {Para}.", configuracion.Servidor, mensaje.Para);
    }

    /// <inheritdoc />
    public async Task ProbarAsync(ConfiguracionSmtp configuracion, string? contrasena, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        await using var sesion = await AbrirAsync(configuracion, contrasena, ct).ConfigureAwait(false);
        await sesion.DespedirseAsync(ct).ConfigureAwait(false);
    }

    private async Task<Sesion> AbrirAsync(ConfiguracionSmtp c, string? contrasena, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(c.Servidor)) throw new ErrorDeCorreo("Falta el servidor de correo saliente (Configuración › Correo de las QSL).");
        if (c.Puerto is <= 0 or > 65535) throw new ErrorDeCorreo($"El puerto {c.Puerto} no es válido.");

        var espera = TimeSpan.FromSeconds(Math.Clamp(c.EsperaSegundos, 5, 300));
        var servidor = c.Servidor.Trim();
        TcpClient? tcp = new();
        try
        {
            using (var limite = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                limite.CancelAfter(espera);
                try
                {
                    await tcp.ConnectAsync(servidor, c.Puerto, limite.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new ErrorDeCorreo($"El servidor {servidor}:{c.Puerto} no contesta.");
                }
                catch (SocketException ex)
                {
                    throw new ErrorDeCorreo($"No se puede conectar con {servidor}:{c.Puerto}: {ex.Message}", ex);
                }
            }

            var sesion = new Sesion(tcp, espera, _log);
            tcp = null;
            try
            {
                if (c.Seguridad == SeguridadSmtp.SslDirecto) await sesion.CifrarAsync(servidor, ValidarCertificado, ct).ConfigureAwait(false);

                await sesion.EsperarAsync(220, ct).ConfigureAwait(false);
                var capacidades = await sesion.SaludarAsync(ct).ConfigureAwait(false);

                if (c.Seguridad == SeguridadSmtp.StartTls)
                {
                    if (!capacidades.Contains("STARTTLS"))
                    {
                        throw new ErrorDeCorreo($"El servidor {servidor} no ofrece STARTTLS en el puerto {c.Puerto}. Pruebe «SSL directo» en el 465.");
                    }

                    await sesion.OrdenAsync("STARTTLS", 220, ct).ConfigureAwait(false);
                    await sesion.CifrarAsync(servidor, ValidarCertificado, ct).ConfigureAwait(false);
                    capacidades = await sesion.SaludarAsync(ct).ConfigureAwait(false);
                }

                if (!string.IsNullOrWhiteSpace(c.Usuario))
                {
                    if (!sesion.Cifrada && !EsLocal(servidor))
                    {
                        throw new ErrorDeCorreo("No se manda la contraseña por una conexión sin cifrar. Elija STARTTLS o SSL directo.");
                    }

                    if (string.IsNullOrEmpty(contrasena))
                    {
                        throw new ErrorDeCorreo("Falta la contraseña del correo: guárdela en Configuración › Correo de las QSL.");
                    }

                    await sesion.IdentificarseAsync(c.Usuario.Trim(), contrasena, capacidades, ct).ConfigureAwait(false);
                }

                return sesion;
            }
            catch
            {
                await sesion.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            tcp?.Dispose();
        }
    }

    private static bool EsLocal(string servidor) =>
        string.Equals(servidor, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(servidor, out var ip) && IPAddress.IsLoopback(ip));

    /// <summary>Una conversacion con el servidor.</summary>
    private sealed class Sesion : IAsyncDisposable
    {
        private readonly TcpClient _tcp;
        private readonly TimeSpan _espera;
        private readonly ILogger? _log;
        private readonly byte[] _buffer = new byte[4096];
        private Stream _flujo;
        private int _leidos;
        private int _posicion;

        public Sesion(TcpClient tcp, TimeSpan espera, ILogger? log)
        {
            _tcp = tcp;
            _espera = espera;
            _log = log;
            _flujo = tcp.GetStream();
        }

        public bool Cifrada { get; private set; }

        public async Task CifrarAsync(string servidor, RemoteCertificateValidationCallback? validar, CancellationToken ct)
        {
            var ssl = new SslStream(_flujo, leaveInnerStreamOpen: false, validar);
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(_espera);
            try
            {
                await ssl.AuthenticateAsClientAsync(
                    new SslClientAuthenticationOptions
                    {
                        TargetHost = servidor,
                        EnabledSslProtocols = SslProtocols.None, // lo que el sistema de por bueno (TLS 1.2/1.3)
                    },
                    limite.Token).ConfigureAwait(false);
            }
            catch (AuthenticationException ex)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
                throw new ErrorDeCorreo($"No se ha podido cifrar la conexión con {servidor}: {ex.Message}", ex);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
                throw new ErrorDeCorreo($"El servidor {servidor} no termina de negociar el cifrado. ¿Es el puerto correcto para ese cifrado?");
            }

            _flujo = ssl;
            _leidos = _posicion = 0;
            Cifrada = true;
        }

        public async Task<HashSet<string>> SaludarAsync(CancellationToken ct)
        {
            await EscribirAsync("EHLO " + NombreDeEsteEquipo(), "EHLO", ct).ConfigureAwait(false);
            var (codigo, lineas) = await LeerRespuestaAsync(ct).ConfigureAwait(false);
            if (codigo != 250) throw Error(codigo, lineas, "EHLO");

            var capacidades = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var linea in lineas.Skip(1))
            {
                var partes = linea.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length == 0) continue;
                capacidades.Add(partes[0]);
                if (string.Equals(partes[0], "AUTH", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var mecanismo in partes.Skip(1)) capacidades.Add("AUTH " + mecanismo);
                }
            }

            return capacidades;
        }

        public async Task IdentificarseAsync(string usuario, string contrasena, HashSet<string> capacidades, CancellationToken ct)
        {
            if (capacidades.Contains("AUTH PLAIN"))
            {
                var credencial = Convert.ToBase64String(Encoding.UTF8.GetBytes($"\0{usuario}\0{contrasena}"));
                await EscribirAsync("AUTH PLAIN " + credencial, "AUTH PLAIN ***", ct).ConfigureAwait(false);
                await ComprobarAutenticacionAsync(ct).ConfigureAwait(false);
                return;
            }

            if (capacidades.Contains("AUTH LOGIN"))
            {
                await EscribirAsync("AUTH LOGIN", "AUTH LOGIN", ct).ConfigureAwait(false);
                await EsperarAsync(334, ct).ConfigureAwait(false);
                await EscribirAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(usuario)), "(usuario)", ct).ConfigureAwait(false);
                await EsperarAsync(334, ct).ConfigureAwait(false);
                await EscribirAsync(Convert.ToBase64String(Encoding.UTF8.GetBytes(contrasena)), "(contraseña)", ct).ConfigureAwait(false);
                await ComprobarAutenticacionAsync(ct).ConfigureAwait(false);
                return;
            }

            throw new ErrorDeCorreo("El servidor no ofrece una forma de identificarse que el programa sepa usar (PLAIN o LOGIN).");
        }

        public Task OrdenAsync(string orden, int esperado, CancellationToken ct) => OrdenAsync(orden, [esperado], ct);

        public async Task OrdenAsync(string orden, int[] esperados, CancellationToken ct)
        {
            await EscribirAsync(orden, orden, ct).ConfigureAwait(false);
            var (codigo, lineas) = await LeerRespuestaAsync(ct).ConfigureAwait(false);
            if (!esperados.Contains(codigo)) throw Error(codigo, lineas, orden.Split(' ')[0]);
        }

        public async Task EsperarAsync(int esperado, CancellationToken ct)
        {
            var (codigo, lineas) = await LeerRespuestaAsync(ct).ConfigureAwait(false);
            if (codigo != esperado) throw Error(codigo, lineas, "saludo");
        }

        public async Task DatosAsync(string cuerpo, CancellationToken ct)
        {
            var s = new StringBuilder(cuerpo.Length + 64);
            foreach (var linea in cuerpo.Split("\r\n"))
            {
                // Una linea que empieza por punto se dobla: un punto solo seria el final del correo.
                if (linea.StartsWith('.')) s.Append('.');
                s.Append(linea).Append("\r\n");
            }

            // Split deja una linea vacia de mas al final si el cuerpo acaba en CRLF: se quita.
            if (cuerpo.EndsWith("\r\n", StringComparison.Ordinal)) s.Length -= 2;
            s.Append(".\r\n");

            var bytes = Encoding.ASCII.GetBytes(s.ToString());
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(_espera * 4);
            await _flujo.WriteAsync(bytes, limite.Token).ConfigureAwait(false);
            await _flujo.FlushAsync(limite.Token).ConfigureAwait(false);
            _log?.LogDebug("SMTP > (mensaje de {Bytes} bytes)", bytes.Length);

            var (codigo, lineas) = await LeerRespuestaAsync(ct).ConfigureAwait(false);
            if (codigo != 250) throw Error(codigo, lineas, "DATA");
        }

        public async Task DespedirseAsync(CancellationToken ct)
        {
            try
            {
                await EscribirAsync("QUIT", "QUIT", ct).ConfigureAwait(false);
                await LeerRespuestaAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or ErrorDeCorreo or OperationCanceledException)
            {
                // El correo ya esta entregado: que el servidor cuelgue sin despedirse no importa.
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _flujo.DisposeAsync().ConfigureAwait(false);
            _tcp.Dispose();
        }

        private async Task ComprobarAutenticacionAsync(CancellationToken ct)
        {
            var (codigo, lineas) = await LeerRespuestaAsync(ct).ConfigureAwait(false);
            if (codigo == 235) return;
            if (codigo == 535)
            {
                throw new ErrorDeCorreo(
                    "El servidor rechaza el usuario o la contraseña (535). En Gmail, Outlook o Yahoo hace falta una «contraseña de aplicación», no la de siempre.")
                { Codigo = codigo };
            }

            throw Error(codigo, lineas, "AUTH");
        }

        private async Task EscribirAsync(string linea, string paraElRegistro, CancellationToken ct)
        {
            if (linea.Contains('\r') || linea.Contains('\n')) throw new ErrorDeCorreo("Orden SMTP con saltos de línea: no se manda.");
            var bytes = Encoding.UTF8.GetBytes(linea + "\r\n");
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(_espera);
            try
            {
                await _flujo.WriteAsync(bytes, limite.Token).ConfigureAwait(false);
                await _flujo.FlushAsync(limite.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ErrorDeCorreo("El servidor de correo no recibe datos (tiempo agotado).");
            }
            catch (IOException ex)
            {
                throw new ErrorDeCorreo($"Se ha cortado la conexión con el servidor de correo: {ex.Message}", ex);
            }

            _log?.LogDebug("SMTP > {Orden}", paraElRegistro);
        }

        private async Task<(int Codigo, List<string> Lineas)> LeerRespuestaAsync(CancellationToken ct)
        {
            var lineas = new List<string>();
            while (true)
            {
                var linea = await LeerLineaAsync(ct).ConfigureAwait(false);
                if (linea.Length < 3 || !int.TryParse(linea.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture, out var codigo))
                {
                    throw new ErrorDeCorreo($"Respuesta del servidor que no es SMTP: «{Recortar(linea)}». ¿Es el puerto y el cifrado correctos?");
                }

                lineas.Add(linea.Length > 4 ? linea[4..] : string.Empty);
                _log?.LogDebug("SMTP < {Linea}", Recortar(linea));
                if (linea.Length == 3 || linea[3] != '-') return (codigo, lineas);
                if (lineas.Count > 200) throw new ErrorDeCorreo("El servidor manda una respuesta interminable.");
            }
        }

        private async Task<string> LeerLineaAsync(CancellationToken ct)
        {
            var linea = new List<byte>(128);
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(_espera);
            while (true)
            {
                if (_posicion >= _leidos)
                {
                    try
                    {
                        _leidos = await _flujo.ReadAsync(_buffer, limite.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new ErrorDeCorreo("El servidor de correo no contesta (tiempo agotado).");
                    }
                    catch (IOException ex)
                    {
                        throw new ErrorDeCorreo($"Se ha cortado la conexión con el servidor de correo: {ex.Message}", ex);
                    }

                    _posicion = 0;
                    if (_leidos == 0) throw new ErrorDeCorreo("El servidor de correo ha cerrado la conexión.");
                }

                var b = _buffer[_posicion++];
                if (b == (byte)'\n')
                {
                    if (linea.Count > 0 && linea[^1] == (byte)'\r') linea.RemoveAt(linea.Count - 1);
                    return Encoding.UTF8.GetString(linea.ToArray());
                }

                linea.Add(b);
                if (linea.Count > 8192) throw new ErrorDeCorreo("El servidor manda una línea demasiado larga.");
            }
        }

        private static ErrorDeCorreo Error(int codigo, List<string> lineas, string orden) =>
            new($"El servidor ha contestado {codigo} a {orden}: {Recortar(string.Join(" ", lineas))}") { Codigo = codigo };

        private static string Recortar(string texto) => texto.Length <= 300 ? texto : texto[..300] + "…";

        private static string NombreDeEsteEquipo()
        {
            try
            {
                var nombre = new string(Dns.GetHostName().Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.').ToArray());
                return nombre.Length > 0 ? nombre : "localhost";
            }
            catch (SocketException)
            {
                return "localhost";
            }
        }
    }
}
