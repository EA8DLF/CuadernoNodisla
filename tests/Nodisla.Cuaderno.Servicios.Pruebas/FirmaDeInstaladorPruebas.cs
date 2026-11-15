using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Servicios.Actualizaciones;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>Doble del verificador: dice lo que se le mande, sin mirar el fichero.</summary>
public sealed class FirmaDoble(ResultadoDeFirma resultado) : IVerificadorDeFirma
{
    /// <summary>Firma integra del certificado de NODISLA.</summary>
    public static FirmaDoble DeNodisla { get; } =
        new(new ResultadoDeFirma(EstadoDeFirma.Integra, FirmantesDeConfianza.CertificadoNodisla2026, "CN=doble"));

    public List<string> Mirados { get; } = [];

    public ResultadoDeFirma Verificar(string ruta)
    {
        Mirados.Add(ruta);
        return resultado;
    }
}

/// <summary>La decision «se instala o no» con dobles: sin firmar nada.</summary>
public sealed class FirmantesDeConfianzaPruebas
{
    private const string Otra = "00000000000000000000000000000000000000000000000000000000000000AA";

    [Fact]
    public void El_programa_lleva_fijado_el_certificado_de_NODISLA()
    {
        FirmantesDeConfianza.Nodisla.Huellas.Should().Contain(FirmantesDeConfianza.CertificadoNodisla2026);
        FirmantesDeConfianza.Nodisla.Huellas.Should().OnlyContain(h => h.Length == 64);
    }

    [Fact]
    public void Firma_integra_de_un_firmante_fijado_se_acepta() =>
        new FirmantesDeConfianza([Otra]).Acepta(new ResultadoDeFirma(EstadoDeFirma.Integra, Otra)).Should().BeTrue();

    [Fact]
    public void Vale_la_huella_en_minusculas_y_con_separadores()
    {
        var conDosPuntos = string.Join(':', Enumerable.Range(0, 32).Select(i => Otra.Substring(i * 2, 2).ToLowerInvariant()));

        new FirmantesDeConfianza([conDosPuntos]).Acepta(new ResultadoDeFirma(EstadoDeFirma.Integra, Otra)).Should().BeTrue();
    }

    [Fact]
    public void Firma_integra_de_otro_firmante_se_rechaza() =>
        FirmantesDeConfianza.Nodisla.Acepta(new ResultadoDeFirma(EstadoDeFirma.Integra, Otra)).Should().BeFalse();

    [Theory]
    [InlineData(EstadoDeFirma.SinFirma)]
    [InlineData(EstadoDeFirma.NoValida)]
    public void Sin_firma_integra_se_rechaza_aunque_la_huella_sea_la_buena(EstadoDeFirma estado) =>
        FirmantesDeConfianza.Nodisla
            .Acepta(new ResultadoDeFirma(estado, FirmantesDeConfianza.CertificadoNodisla2026))
            .Should().BeFalse();

    [Fact]
    public void Sin_huella_o_sin_resultado_se_rechaza()
    {
        FirmantesDeConfianza.Nodisla.Acepta(new ResultadoDeFirma(EstadoDeFirma.Integra)).Should().BeFalse();
        FirmantesDeConfianza.Nodisla.Acepta(null).Should().BeFalse();
    }

    [Fact]
    public void Con_la_lista_rotada_valen_el_certificado_viejo_y_el_nuevo()
    {
        var rotada = new FirmantesDeConfianza([FirmantesDeConfianza.CertificadoNodisla2026, Otra]);

        rotada.Acepta(new ResultadoDeFirma(EstadoDeFirma.Integra, FirmantesDeConfianza.CertificadoNodisla2026)).Should().BeTrue();
        rotada.Acepta(new ResultadoDeFirma(EstadoDeFirma.Integra, Otra)).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("919A06FFBDF618A0D1CE38426547328C1FB82E74")]
    [InlineData("ZZD2089F0BBDEA84445C022C8DC00C3D21A563A5EFEE3FC6742A7397FFE5552A")]
    public void Una_huella_que_no_es_SHA256_no_entra_en_la_lista(string huella)
    {
        var crear = () => new FirmantesDeConfianza([huella]);

        crear.Should().Throw<ArgumentException>();
    }
}

/// <summary>El descargador con la firma doblada: que hace con cada respuesta del verificador.</summary>
public sealed class DescargadorYFirmaPruebas : IDisposable
{
    private static readonly byte[] Contenido = "MZ-instalador-de-prueba"u8.ToArray();
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-pruebas-firma-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private static VersionPublicada Version() =>
        ComprobadorDeVersiones.Interpretar(ComprobadorDeVersionesPruebas.Publicacion(), RepositorioPublico.CuadernoNodisla)!;

    private static FabricaFalsa RedConSumaBuena()
    {
        var suma = Convert.ToHexString(SHA256.HashData(Contenido));
        return new FabricaFalsa(new ManejadorFalso((p, _) =>
            p.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? ManejadorFalso.Respuesta(suma)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Contenido) }));
    }

    [Theory]
    [InlineData(EstadoDeFirma.SinFirma, null)]
    [InlineData(EstadoDeFirma.NoValida, null)]
    [InlineData(EstadoDeFirma.NoValida, FirmantesDeConfianza.CertificadoNodisla2026)]
    [InlineData(EstadoDeFirma.Integra, null)]
    [InlineData(EstadoDeFirma.Integra, "00000000000000000000000000000000000000000000000000000000000000AA")]
    public async Task Con_la_suma_buena_y_la_firma_mala_se_borra_y_se_apunta(EstadoDeFirma estado, string? huella)
    {
        var log = new RegistroDePrueba<DescargadorDeInstalador>();
        var descargador = new DescargadorDeInstalador(
            RedConSumaBuena(), _carpeta, log, new FirmaDoble(new ResultadoDeFirma(estado, huella, "CN=otro")));

        var resultado = await descargador.DescargarAsync(Version());

        resultado.Estado.Should().Be(EstadoDeDescarga.FirmaNoValida);
        resultado.Ruta.Should().BeNull();
        resultado.Motivo.Should().Contain("firma de NODISLA");
        Directory.GetFiles(_carpeta, "*", SearchOption.AllDirectories).Should().BeEmpty();
        log.Entradas.Should().ContainSingle(e => e.Nivel == LogLevel.Warning && e.Mensaje.Contains("no lleva la firma de NODISLA"));
    }

    [Fact]
    public async Task Un_verificador_que_lanza_cuenta_como_firma_mala()
    {
        var descargador = new DescargadorDeInstalador(RedConSumaBuena(), _carpeta, verificador: new Rompe());

        var resultado = await descargador.DescargarAsync(Version());

        resultado.Estado.Should().Be(EstadoDeDescarga.FirmaNoValida);
        Directory.GetFiles(_carpeta, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task La_firma_se_mira_antes_de_que_exista_el_exe()
    {
        var doble = new FirmaDoble(new ResultadoDeFirma(EstadoDeFirma.Integra, FirmantesDeConfianza.CertificadoNodisla2026));
        var descargador = new DescargadorDeInstalador(RedConSumaBuena(), _carpeta, verificador: doble);

        var resultado = await descargador.DescargarAsync(Version());

        resultado.Estado.Should().Be(EstadoDeDescarga.Verificado);
        doble.Mirados.Should().ContainSingle().Which.Should().EndWith(".parcial");
    }

    [Fact]
    public async Task Si_la_suma_no_casa_ni_se_mira_la_firma()
    {
        var doble = new FirmaDoble(new ResultadoDeFirma(EstadoDeFirma.Integra, FirmantesDeConfianza.CertificadoNodisla2026));
        var red = new FabricaFalsa(new ManejadorFalso((p, _) =>
            p.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? ManejadorFalso.Respuesta(new string('0', 64))
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Contenido) }));

        var resultado = await new DescargadorDeInstalador(red, _carpeta, verificador: doble).DescargarAsync(Version());

        resultado.Estado.Should().Be(EstadoDeDescarga.SumaNoCasa);
        doble.Mirados.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_decir_nada_se_usa_el_verificador_de_verdad_y_un_fichero_sin_firma_no_pasa()
    {
        // Lo que protege al programa publicado: nadie tiene que acordarse de activar la firma.
        var resultado = await new DescargadorDeInstalador(RedConSumaBuena(), _carpeta).DescargarAsync(Version());

        resultado.Estado.Should().Be(EstadoDeDescarga.FirmaNoValida);
        Directory.GetFiles(_carpeta, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    private sealed class Rompe : IVerificadorDeFirma
    {
        public ResultadoDeFirma Verificar(string ruta) => throw new InvalidOperationException("roto");
    }
}

/// <summary>
/// Certificado autofirmado <b>temporal</b> y un ejecutable firmado con el, para las pruebas de
/// verdad contra <c>WinVerifyTrust</c>.
/// </summary>
/// <remarks>
/// El certificado se crea en memoria con la API de .NET y solo se escribe, como PFX con una
/// contrasena aleatoria, en una carpeta temporal que se borra al acabar. <b>No se mete en
/// ningun almacen de certificados</b> (ni «Personal» ni raices de confianza): por eso Windows
/// no confia en el, que es justo el caso de un PC ajeno con el certificado de NODISLA. La firma
/// la pone <c>Set-AuthenticodeSignature</c> de Windows PowerShell, sin sello de tiempo (sin red).
/// </remarks>
public sealed class EjecutableFirmadoDePrueba : IDisposable
{
    public EjecutableFirmadoDePrueba()
    {
        Carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-pruebas-authenticode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Carpeta);

        // Un PE de verdad y pequeno: el propio ensamblado de servicios, copiado.
        SinFirma = Path.Combine(Carpeta, "sin-firma.exe");
        File.Copy(typeof(DescargadorDeInstalador).Assembly.Location, SinFirma);
        Firmado = Path.Combine(Carpeta, "firmado.exe");
        File.Copy(SinFirma, Firmado);

        using var clave = RSA.Create(2048);
        var peticion = new CertificateRequest(
            "CN=Prueba temporal Cuaderno NODISLA (no fiable)", clave, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        peticion.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        peticion.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.3")], critical: false));
        using var certificado = peticion.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        HuellaSha256 = Convert.ToHexString(SHA256.HashData(certificado.RawData));

        var contrasena = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var pfx = Path.Combine(Carpeta, "temporal.pfx");
        File.WriteAllBytes(pfx, certificado.Export(X509ContentType.Pfx, contrasena));
        try
        {
            Firmar(pfx, contrasena, Firmado);
        }
        finally
        {
            File.Delete(pfx);
        }
    }

    /// <summary>Carpeta temporal de la prueba.</summary>
    public string Carpeta { get; }

    /// <summary>PE sin firmar.</summary>
    public string SinFirma { get; }

    /// <summary>El mismo PE, firmado con el certificado temporal.</summary>
    public string Firmado { get; }

    /// <summary>SHA-256 del certificado temporal.</summary>
    public string HuellaSha256 { get; }

    public void Dispose()
    {
        if (Directory.Exists(Carpeta)) Directory.Delete(Carpeta, recursive: true);
    }

    // La contrasena y las rutas van por variables de entorno del proceso hijo, no en la linea
    // de ordenes. Sin perfil, sin ventana y sin tocar Cert:\ .
    private static void Firmar(string pfx, string contrasena, string fichero)
    {
        const string Guion =
            "$ErrorActionPreference='Stop';" +
            "$c=New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($env:CUADERNO_PRUEBA_PFX,$env:CUADERNO_PRUEBA_CLAVE);" +
            "$r=Set-AuthenticodeSignature -FilePath $env:CUADERNO_PRUEBA_FICHERO -Certificate $c -HashAlgorithm SHA256;" +
            "if($null -eq $r.SignerCertificate){Write-Error ('No se ha firmado: '+$r.StatusMessage);exit 1}";
        var inicio = new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"),
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + Guion + "\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        inicio.Environment["CUADERNO_PRUEBA_PFX"] = pfx;
        inicio.Environment["CUADERNO_PRUEBA_CLAVE"] = contrasena;
        inicio.Environment["CUADERNO_PRUEBA_FICHERO"] = fichero;

        using var proceso = Process.Start(inicio)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEnd();
        if (!proceso.WaitForExit(120_000))
        {
            proceso.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Set-AuthenticodeSignature no ha terminado a tiempo.");
        }

        if (proceso.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"No se ha podido firmar el ejecutable de prueba (código {proceso.ExitCode}): {errores} {salida.Result}");
        }
    }
}

/// <summary>
/// Integracion de verdad con Windows: <see cref="VerificadorDeFirmaAuthenticode"/> contra un
/// ejecutable firmado con un certificado autofirmado que Windows no conoce.
/// </summary>
public sealed class VerificadorDeFirmaAuthenticodePruebas(EjecutableFirmadoDePrueba prueba)
    : IClassFixture<EjecutableFirmadoDePrueba>
{
    private const int CERT_E_UNTRUSTEDROOT = unchecked((int)0x800B0109);
    private const int TRUST_E_NOSIGNATURE = unchecked((int)0x800B0100);
    private const int TRUST_E_BAD_DIGEST = unchecked((int)0x80096010);

    private readonly VerificadorDeFirmaAuthenticode _verificador = new();

    private string Copia(string origen, string nombre)
    {
        var destino = Path.Combine(prueba.Carpeta, Guid.NewGuid().ToString("N") + "-" + nombre);
        File.Copy(origen, destino);
        return destino;
    }

    [Fact]
    public void Firmado_y_sin_tocar_se_acepta_con_su_huella_aunque_Windows_no_confie_en_la_raiz()
    {
        var firma = _verificador.Verificar(prueba.Firmado);

        firma.Estado.Should().Be(EstadoDeFirma.Integra);
        firma.HuellaSha256.Should().Be(prueba.HuellaSha256);
        firma.Sujeto.Should().Contain("Prueba temporal");
        firma.Codigo.Should().Be(CERT_E_UNTRUSTEDROOT, "el certificado no esta en ningun almacen: es el caso de un PC ajeno");
        new FirmantesDeConfianza([prueba.HuellaSha256]).Acepta(firma).Should().BeTrue();
    }

    [Fact]
    public void Firmado_por_otro_certificado_se_rechaza()
    {
        var firma = _verificador.Verificar(prueba.Firmado);

        FirmantesDeConfianza.Nodisla.Acepta(firma).Should().BeFalse("la firma es buena, pero no es de NODISLA");
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    public void Alterado_despues_de_firmar_se_rechaza(double donde)
    {
        var alterado = Copia(prueba.Firmado, "alterado.exe");
        var bytes = File.ReadAllBytes(alterado);
        // Un byte del contenido original (codigo o datos), lejos de la firma, que va al final.
        var posicion = (int)(new FileInfo(prueba.SinFirma).Length * donde);
        bytes[posicion] ^= 0xFF;
        File.WriteAllBytes(alterado, bytes);

        var firma = _verificador.Verificar(alterado);

        firma.Estado.Should().Be(EstadoDeFirma.NoValida);
        firma.Codigo.Should().Be(TRUST_E_BAD_DIGEST);
        firma.HuellaSha256.Should().BeNull();
        new FirmantesDeConfianza([prueba.HuellaSha256]).Acepta(firma).Should().BeFalse();
    }

    [Fact]
    public void Con_la_firma_recortada_se_rechaza()
    {
        var recortado = Copia(prueba.Firmado, "recortado.exe");
        var bytes = File.ReadAllBytes(recortado);
        File.WriteAllBytes(recortado, bytes[..^64]);

        var firma = _verificador.Verificar(recortado);

        firma.Estado.Should().NotBe(EstadoDeFirma.Integra);
        new FirmantesDeConfianza([prueba.HuellaSha256]).Acepta(firma).Should().BeFalse();
    }

    [Fact]
    public void Sin_firma_se_rechaza()
    {
        var firma = _verificador.Verificar(prueba.SinFirma);

        firma.Estado.Should().Be(EstadoDeFirma.SinFirma);
        firma.Codigo.Should().Be(TRUST_E_NOSIGNATURE);
        new FirmantesDeConfianza([prueba.HuellaSha256]).Acepta(firma).Should().BeFalse();
    }

    [Fact]
    public void Algo_que_no_es_un_ejecutable_o_que_no_existe_se_rechaza()
    {
        var texto = Path.Combine(prueba.Carpeta, Guid.NewGuid().ToString("N") + ".exe");
        File.WriteAllText(texto, "MZ-esto-no-es-un-ejecutable");

        _verificador.Verificar(texto).Estado.Should().NotBe(EstadoDeFirma.Integra);
        _verificador.Verificar(Path.Combine(prueba.Carpeta, "no-existe.exe")).Estado.Should().Be(EstadoDeFirma.NoValida);
    }

    [Fact]
    public async Task De_punta_a_punta_el_descargador_acepta_el_firmado_por_el_firmante_fijado_y_rechaza_el_resto()
    {
        var firmado = File.ReadAllBytes(prueba.Firmado);
        var alterado = (byte[])firmado.Clone();
        alterado[(int)(new FileInfo(prueba.SinFirma).Length / 2)] ^= 0xFF;
        var fijado = new FirmantesDeConfianza([prueba.HuellaSha256]);

        (await Descargar(firmado, fijado)).Estado.Should().Be(EstadoDeDescarga.Verificado);
        (await Descargar(firmado, FirmantesDeConfianza.Nodisla)).Estado.Should().Be(EstadoDeDescarga.FirmaNoValida);
        (await Descargar(alterado, fijado)).Estado.Should().Be(EstadoDeDescarga.FirmaNoValida);
        (await Descargar(File.ReadAllBytes(prueba.SinFirma), fijado)).Estado.Should().Be(EstadoDeDescarga.FirmaNoValida);
    }

    /// <summary>
    /// Comprobacion a mano de un instalador de verdad con la misma logica que el programa.
    /// Solo hace algo si la variable de entorno <c>CUADERNO_INSTALADOR_A_COMPROBAR</c> apunta a
    /// un fichero; sin ella pasa sin mirar nada (ver docs/20-firma-de-codigo.md).
    /// </summary>
    [Fact]
    public void El_instalador_indicado_a_mano_lleva_la_firma_de_NODISLA()
    {
        var ruta = Environment.GetEnvironmentVariable("CUADERNO_INSTALADOR_A_COMPROBAR");
        if (string.IsNullOrWhiteSpace(ruta)) return;

        File.Exists(ruta).Should().BeTrue($"CUADERNO_INSTALADOR_A_COMPROBAR apunta a «{ruta}»");
        var firma = _verificador.Verificar(ruta);

        firma.Estado.Should().Be(EstadoDeFirma.Integra);
        firma.HuellaSha256.Should().Be(FirmantesDeConfianza.CertificadoNodisla2026);
        FirmantesDeConfianza.Nodisla.Acepta(firma).Should().BeTrue();
    }

    // La suma publicada siempre casa con lo servido: es el ataque del hallazgo (quien cambia
    // la release cambia a la vez el .exe y su .sha256).
    private async Task<ResultadoDeDescarga> Descargar(byte[] contenido, FirmantesDeConfianza firmantes)
    {
        var suma = Convert.ToHexString(SHA256.HashData(contenido));
        var red = new FabricaFalsa(new ManejadorFalso((p, _) =>
            p.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? ManejadorFalso.Respuesta(suma)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(contenido) }));
        var carpeta = Path.Combine(prueba.Carpeta, "descarga-" + Guid.NewGuid().ToString("N"));
        var version = ComprobadorDeVersiones.Interpretar(ComprobadorDeVersionesPruebas.Publicacion(), RepositorioPublico.CuadernoNodisla)!;

        var resultado = await new DescargadorDeInstalador(red, carpeta, firmantes: firmantes).DescargarAsync(version);

        if (resultado.Estado == EstadoDeDescarga.Verificado)
        {
            File.ReadAllBytes(resultado.Ruta!).Should().Equal(contenido);
        }
        else
        {
            Directory.GetFiles(carpeta, "*", SearchOption.AllDirectories).Should().BeEmpty();
        }

        return resultado;
    }
}
