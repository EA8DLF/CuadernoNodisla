using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Servicios.Correo;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>
/// El cliente SMTP propio contra un servidor SMTP DE MENTIRA en 127.0.0.1. Ningun correo sale
/// de la maquina: el servidor de la prueba solo apunta lo que recibe.
/// </summary>
public sealed class CorreoPruebas
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5];

    private static MensajeDeCorreo Mensaje(string para = "ea1abc@ejemplo.es") => new(
        para,
        "QSL EA8DLF - EA1ABC 2026-09-29 20m FT8 — ¡gracias!",
        "Hola.\n.Una línea que empieza por punto.\n73",
        [new AdjuntoDeCorreo("QSL_EA8DLF_EA1ABC.png", "image/png", Png)]);

    private static ConfiguracionSmtp Configuracion(int puerto, SeguridadSmtp seguridad = SeguridadSmtp.Ninguna, string? usuario = null) => new()
    {
        Servidor = "127.0.0.1",
        Puerto = puerto,
        Seguridad = seguridad,
        Usuario = usuario,
        Remitente = "ea8dlf@ejemplo.es",
        NombreDelRemitente = "EA1ABC Ana",
        EsperaSegundos = 10,
    };

    [Fact]
    public async Task Manda_el_correo_entero_con_adjunto_y_asunto_codificado()
    {
        using var servidor = new ServidorSmtpDeMentira();
        await new ClienteSmtp().EnviarAsync(Mensaje(), Configuracion(servidor.Puerto), null);
        await servidor.Terminado;

        servidor.Ordenes.Should().ContainInOrder("MAIL FROM:<ea8dlf@ejemplo.es>", "RCPT TO:<ea1abc@ejemplo.es>", "DATA", "QUIT");
        var datos = servidor.Datos.ToString();
        datos.Should().Contain("Subject: =?utf-8?B?");
        datos.Should().Contain("Content-Disposition: attachment; filename=\"QSL_EA8DLF_EA1ABC.png\"");
        datos.Should().Contain(Convert.ToBase64String(Png));
        datos.Split("\r\n").Should().OnlyContain(l => l.Length <= 998, "SMTP no admite lineas mas largas");

        // El asunto, descodificado, es el que se escribio.
        var asunto = datos.Split("\r\n").SkipWhile(l => !l.StartsWith("Subject:", StringComparison.Ordinal))
            .TakeWhile((l, i) => i == 0 || l.StartsWith(' '))
            .SelectMany(l => l.Split("=?utf-8?B?").Skip(1))
            .Select(t => Encoding.UTF8.GetString(Convert.FromBase64String(t[..t.IndexOf("?=", StringComparison.Ordinal)])));
        string.Concat(asunto).Should().Be("QSL EA8DLF - EA1ABC 2026-09-29 20m FT8 — ¡gracias!");
    }

    [Fact]
    public async Task Se_identifica_con_AUTH_PLAIN_y_no_apunta_la_contrasena_en_las_ordenes()
    {
        using var servidor = new ServidorSmtpDeMentira { Autenticacion = "AUTH PLAIN LOGIN" };
        await new ClienteSmtp().EnviarAsync(Mensaje(), Configuracion(servidor.Puerto, usuario: "ea8dlf"), "secreto");
        await servidor.Terminado;

        servidor.Credencial.Should().Be("\0ea8dlf\0secreto");
    }

    [Fact]
    public async Task Con_AUTH_LOGIN_tambien()
    {
        using var servidor = new ServidorSmtpDeMentira { Autenticacion = "AUTH LOGIN" };
        await new ClienteSmtp().EnviarAsync(Mensaje(), Configuracion(servidor.Puerto, usuario: "ea8dlf"), "secreto");
        await servidor.Terminado;

        servidor.Credencial.Should().Be("ea8dlf|secreto");
    }

    [Fact]
    public async Task Una_contrasena_rechazada_se_explica()
    {
        using var servidor = new ServidorSmtpDeMentira { Autenticacion = "AUTH PLAIN", RechazarLaCuenta = true };
        var envio = () => new ClienteSmtp().EnviarAsync(Mensaje(), Configuracion(servidor.Puerto, usuario: "ea8dlf"), "mala");

        (await envio.Should().ThrowAsync<ErrorDeCorreo>()).Which.Message.Should().Contain("contraseña de aplicación");
    }

    [Fact]
    public async Task Un_buzon_que_no_existe_es_fallo_del_destinatario()
    {
        using var servidor = new ServidorSmtpDeMentira { RechazarDestinatario = true };
        var envio = () => new ClienteSmtp().EnviarAsync(Mensaje(), Configuracion(servidor.Puerto), null);

        var error = (await envio.Should().ThrowAsync<ErrorDeCorreo>()).Which;
        error.EsDelDestinatario.Should().BeTrue("en un lote se salta y se sigue con el siguiente");
        error.Codigo.Should().Be(550);
    }

    [Fact]
    public async Task Pide_STARTTLS_y_el_servidor_no_lo_ofrece_se_para_sin_mandar_nada()
    {
        using var servidor = new ServidorSmtpDeMentira();
        var envio = () => new ClienteSmtp().EnviarAsync(Mensaje(), Configuracion(servidor.Puerto, SeguridadSmtp.StartTls, "ea8dlf"), "secreto");

        (await envio.Should().ThrowAsync<ErrorDeCorreo>()).Which.Message.Should().Contain("STARTTLS");
        servidor.Credencial.Should().BeNull("la contraseña no puede salir por una conexion sin cifrar");
    }

    [Fact]
    public async Task Con_STARTTLS_cifra_y_luego_se_identifica()
    {
        using var certificado = CertificadoDePrueba();
        using var servidor = new ServidorSmtpDeMentira { Certificado = certificado, Autenticacion = "AUTH PLAIN" };
        var cliente = new ClienteSmtp { ValidarCertificado = (_, _, _, _) => true };
        await cliente.EnviarAsync(Mensaje(), Configuracion(servidor.Puerto, SeguridadSmtp.StartTls, "ea8dlf"), "secreto");
        await servidor.Terminado;

        servidor.Cifrado.Should().BeTrue();
        servidor.Credencial.Should().Be("\0ea8dlf\0secreto");
        servidor.Ordenes.Should().Contain("DATA");
    }

    [Fact]
    public async Task Con_SSL_directo_cifra_desde_el_primer_byte()
    {
        using var certificado = CertificadoDePrueba();
        using var servidor = new ServidorSmtpDeMentira { Certificado = certificado, SslDirecto = true, Autenticacion = "AUTH PLAIN" };
        var cliente = new ClienteSmtp { ValidarCertificado = (_, _, _, _) => true };
        await cliente.EnviarAsync(Mensaje(), Configuracion(servidor.Puerto, SeguridadSmtp.SslDirecto, "ea8dlf"), "secreto");
        await servidor.Terminado;

        servidor.Cifrado.Should().BeTrue();
        servidor.Ordenes.Should().Contain("DATA");
    }

    [Fact]
    public async Task Un_certificado_que_no_vale_no_se_acepta_por_omision()
    {
        using var certificado = CertificadoDePrueba();
        using var servidor = new ServidorSmtpDeMentira { Certificado = certificado, SslDirecto = true };
        var envio = () => new ClienteSmtp().EnviarAsync(Mensaje(), Configuracion(servidor.Puerto, SeguridadSmtp.SslDirecto), null);

        await envio.Should().ThrowAsync<ErrorDeCorreo>();
    }

    [Theory]
    [InlineData("ea1abc@ejemplo.es", true)]
    [InlineData("a.b+c@sub.dominio.org", true)]
    [InlineData("sin-arroba.es", false)]
    [InlineData("dos@@ejemplo.es", false)]
    [InlineData("x@sinpunto", false)]
    [InlineData("x@ejemplo.es>\r\nRCPT TO:<otro@malo.es", false)]
    [InlineData("", false)]
    public void Direcciones(string direccion, bool valida) =>
        DireccionDeCorreo.EsValida(direccion).Should().Be(valida);

    [Fact]
    public void La_cabecera_ASCII_va_tal_cual_y_la_de_acentos_codificada_sin_partir_caracteres()
    {
        ComposicionMime.Codificar("QSL EA8DLF").Should().Be("QSL EA8DLF");
        var largo = new string('ñ', 80);
        var codificado = ComposicionMime.Codificar(largo);
        var texto = string.Concat(codificado.Split("\r\n ").Select(t => Encoding.UTF8.GetString(Convert.FromBase64String(t[10..^2]))));
        texto.Should().Be(largo);
    }

    [Fact]
    public async Task El_buzon_de_mentira_deja_el_eml_y_no_manda_nada()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-correo-" + Guid.NewGuid().ToString("N"));
        try
        {
            var buzon = new BuzonDeSalidaEnDisco(carpeta);
            await buzon.EnviarAsync(Mensaje(), new ConfiguracionSmtp(), null);
            Directory.GetFiles(carpeta, "*.eml").Should().ContainSingle();
            buzon.Enviados.Should().ContainSingle();
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
        }
    }

    private static X509Certificate2 CertificadoDePrueba()
    {
        using var clave = RSA.Create(2048);
        var peticion = new CertificateRequest("CN=127.0.0.1", clave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var efimero = peticion.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // SslStream en Windows necesita la clave en un certificado importado, no en uno efimero.
        return new X509Certificate2(efimero.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>Un servidor SMTP minimo que atiende una conexion y apunta lo que le llega.</summary>
    private sealed class ServidorSmtpDeMentira : IDisposable
    {
        private readonly TcpListener _escucha = new(IPAddress.Loopback, 0);
        private readonly Lazy<Task> _atender;

        public ServidorSmtpDeMentira()
        {
            _escucha.Start();
            _atender = new Lazy<Task>(() => Task.Run(AtenderAsync));
        }

        public int Puerto
        {
            get
            {
                _ = _atender.Value;
                return ((IPEndPoint)_escucha.LocalEndpoint).Port;
            }
        }

        public Task Terminado => _atender.Value;

        public string? Autenticacion { get; init; }

        public bool RechazarLaCuenta { get; init; }

        public bool RechazarDestinatario { get; init; }

        public X509Certificate2? Certificado { get; init; }

        public bool SslDirecto { get; init; }

        public List<string> Ordenes { get; } = [];

        public StringBuilder Datos { get; } = new();

        public string? Credencial { get; private set; }

        public bool Cifrado { get; private set; }

        public void Dispose() => _escucha.Stop();

        private async Task AtenderAsync()
        {
            using var tcp = await _escucha.AcceptTcpClientAsync();
            Stream flujo = tcp.GetStream();
            try
            {
                if (SslDirecto)
                {
                    var ssl = new SslStream(flujo);
                    await ssl.AuthenticateAsServerAsync(Certificado!);
                    flujo = ssl;
                    Cifrado = true;
                }

                var lector = new StreamReader(flujo, Encoding.ASCII, false, 1, leaveOpen: true);
                await Escribir(flujo, "220 mentira ESMTP");
                while (await lector.ReadLineAsync() is { } linea)
                {
                    var orden = linea.Split(' ')[0].ToUpperInvariant();
                    if (orden is "EHLO")
                    {
                        var lineas = new List<string> { "250-mentira" };
                        if (Certificado is not null && !Cifrado) lineas.Add("250-STARTTLS");
                        if (Autenticacion is not null) lineas.Add("250-" + Autenticacion);
                        lineas.Add("250 8BITMIME");
                        foreach (var l in lineas) await Escribir(flujo, l);
                        continue;
                    }

                    if (!orden.StartsWith("AUTH", StringComparison.Ordinal)) Ordenes.Add(linea);
                    switch (orden)
                    {
                        case "STARTTLS":
                            await Escribir(flujo, "220 adelante");
                            var ssl = new SslStream(flujo);
                            await ssl.AuthenticateAsServerAsync(Certificado!);
                            flujo = ssl;
                            lector = new StreamReader(flujo, Encoding.ASCII, false, 1, leaveOpen: true);
                            Cifrado = true;
                            break;
                        case "AUTH" when linea.StartsWith("AUTH PLAIN ", StringComparison.Ordinal):
                            Credencial = Encoding.UTF8.GetString(Convert.FromBase64String(linea[11..]));
                            await Escribir(flujo, RechazarLaCuenta ? "535 5.7.8 no" : "235 dentro");
                            break;
                        case "AUTH":
                            await Escribir(flujo, "334 VXNlcm5hbWU6");
                            var usuario = Encoding.UTF8.GetString(Convert.FromBase64String((await lector.ReadLineAsync())!));
                            await Escribir(flujo, "334 UGFzc3dvcmQ6");
                            var clave = Encoding.UTF8.GetString(Convert.FromBase64String((await lector.ReadLineAsync())!));
                            Credencial = usuario + "|" + clave;
                            await Escribir(flujo, RechazarLaCuenta ? "535 no" : "235 dentro");
                            break;
                        case "MAIL":
                            await Escribir(flujo, "250 vale");
                            break;
                        case "RCPT":
                            await Escribir(flujo, RechazarDestinatario ? "550 5.1.1 no existe" : "250 vale");
                            break;
                        case "DATA":
                            await Escribir(flujo, "354 adelante");
                            while (await lector.ReadLineAsync() is { } dato && dato != ".")
                            {
                                Datos.Append(dato.StartsWith("..", StringComparison.Ordinal) ? dato[1..] : dato).Append("\r\n");
                            }

                            await Escribir(flujo, "250 entregado");
                            break;
                        case "QUIT":
                            await Escribir(flujo, "221 adios");
                            return;
                        default:
                            await Escribir(flujo, "500 que");
                            break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or ObjectDisposedException)
            {
                // El cliente ha colgado (p. ej. al rechazar el certificado): la prueba lo mira por su lado.
            }
            finally
            {
                await flujo.DisposeAsync();
            }
        }

        private static async Task Escribir(Stream flujo, string linea)
        {
            var bytes = Encoding.ASCII.GetBytes(linea + "\r\n");
            await flujo.WriteAsync(bytes);
            await flujo.FlushAsync();
        }
    }

}
