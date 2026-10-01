using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Servicios.Actualizaciones;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

public sealed class VersionSemanticaPruebas
{
    [Theory]
    [InlineData("v0.2.0", "0.1.0.0")]
    [InlineData("0.1.1", "0.1.0")]
    [InlineData("1.0.0", "0.99.99")]
    [InlineData("0.10.0", "0.9.0")]
    [InlineData("1.0.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.10", "1.0.0-rc.2")]
    [InlineData("1.0.0-beta", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-alpha.1")]
    [InlineData("0.1.0.1", "0.1.0")]
    public void La_primera_es_mas_nueva(string nueva, string vieja)
    {
        var a = VersionSemantica.Analizar(nueva);
        var b = VersionSemantica.Analizar(vieja);

        (a > b).Should().BeTrue();
        (b < a).Should().BeTrue();
        a.CompareTo(b).Should().BePositive();
    }

    [Theory]
    [InlineData("v0.1.0", "0.1.0.0")]
    [InlineData("0.1", "0.1.0")]
    [InlineData("V1.2.3", "1.2.3+1e0207d")]
    public void Son_la_misma(string a, string b)
    {
        var x = VersionSemantica.Analizar(a);
        var y = VersionSemantica.Analizar(b);

        x.Equals(y).Should().BeTrue();
        x.GetHashCode().Should().Be(y.GetHashCode());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ultima")]
    [InlineData("v1..2")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.x")]
    [InlineData("1.0.0-")]
    public void No_son_versiones(string? texto) =>
        VersionSemantica.TryAnalizar(texto, out _).Should().BeFalse();

    [Fact]
    public void La_de_compilacion_no_cuenta_y_la_v_se_quita()
    {
        var v = VersionSemantica.Analizar("v0.2.0-beta.1+abc");

        v.ToString().Should().Be("0.2.0-beta.1");
        v.EsPrevia.Should().BeTrue();
    }
}

public sealed class PlanDeComprobacionPruebas
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sin_comprobacion_previa_toca() => PlanDeComprobacion.Toca(null, Ahora).Should().BeTrue();

    [Fact]
    public void Hace_una_hora_no_toca() =>
        PlanDeComprobacion.Toca(Ahora.AddHours(-1), Ahora).Should().BeFalse();

    [Fact]
    public void Hace_un_dia_toca() => PlanDeComprobacion.Toca(Ahora.AddDays(-1), Ahora).Should().BeTrue();

    [Fact]
    public void Con_la_ultima_en_el_futuro_toca() =>
        PlanDeComprobacion.Toca(Ahora.AddDays(3), Ahora).Should().BeTrue();
}

public sealed class ComprobadorDeVersionesPruebas
{
    private static readonly VersionSemantica Instalada = VersionSemantica.Analizar("0.1.0.0");

    internal static string Publicacion(
        string etiqueta = "v0.2.0",
        bool conInstalador = true,
        bool conSuma = true,
        string prefijo = "https://github.com/EA8DLF/CuadernoNodisla/releases/download/") =>
        $$"""
        {
          "tag_name": "{{etiqueta}}",
          "name": "Cuaderno NODISLA {{etiqueta}}",
          "body": "- Aviso de versiones nuevas\n- Reportar un fallo",
          "html_url": "https://github.com/EA8DLF/CuadernoNodisla/releases/tag/{{etiqueta}}",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-10-01T10:00:00Z",
          "assets": [
            {{(conInstalador ? $$"""{ "name": "CuadernoNodisla-Instalador-0.2.0.0.exe", "size": 5, "browser_download_url": "{{prefijo}}{{etiqueta}}/CuadernoNodisla-Instalador-0.2.0.0.exe" }""" : "")}}
            {{(conInstalador && conSuma ? "," : "")}}
            {{(conSuma ? $$"""{ "name": "CuadernoNodisla-Instalador-0.2.0.0.exe.sha256", "size": 100, "browser_download_url": "{{prefijo}}{{etiqueta}}/CuadernoNodisla-Instalador-0.2.0.0.exe.sha256" }""" : "")}}
          ]
        }
        """;

    private static ComprobadorDeVersiones Comprobador(ManejadorFalso red, RegistroDePrueba<ComprobadorDeVersiones>? log = null) =>
        new(new FabricaFalsa(red), Instalada, log: log);

    [Fact]
    public async Task Con_version_mayor_avisa_con_notas_e_instalador()
    {
        var red = ManejadorFalso.ConTexto(Publicacion());

        var resultado = await Comprobador(red).ComprobarAsync();

        resultado.Estado.Should().Be(EstadoDeComprobacion.HayVersionNueva);
        resultado.Nueva!.Version.ToString().Should().Be("0.2.0");
        resultado.Nueva.Notas.Should().Contain("Reportar un fallo");
        resultado.Nueva.Instalador!.Nombre.Should().Be("CuadernoNodisla-Instalador-0.2.0.0.exe");
        resultado.Nueva.Suma!.Nombre.Should().EndWith(".exe.sha256");
        resultado.Nueva.SePuedeInstalar.Should().BeTrue();
        red.Direcciones.Should().ContainSingle()
            .Which.AbsoluteUri.Should().Be("https://api.github.com/repos/EA8DLF/CuadernoNodisla/releases/latest");
    }

    [Fact]
    public async Task Con_la_misma_version_esta_al_dia()
    {
        var resultado = await Comprobador(ManejadorFalso.ConTexto(Publicacion("v0.1.0"))).ComprobarAsync();

        resultado.Estado.Should().Be(EstadoDeComprobacion.AlDia);
        resultado.Nueva.Should().BeNull();
    }

    [Fact]
    public async Task El_404_del_repositorio_privado_se_calla()
    {
        var log = new RegistroDePrueba<ComprobadorDeVersiones>();
        var red = new ManejadorFalso((_, _) =>
            ManejadorFalso.Respuesta("""{"message":"Not Found"}""", HttpStatusCode.NotFound));

        var resultado = await Comprobador(red, log).ComprobarAsync();

        resultado.Estado.Should().Be(EstadoDeComprobacion.NoDisponible);
        log.Entradas.Should().NotBeEmpty();
        log.Entradas.Should().OnlyContain(e => e.Nivel == LogLevel.Debug);
    }

    [Fact]
    public async Task Sin_red_tampoco_da_error()
    {
        var log = new RegistroDePrueba<ComprobadorDeVersiones>();
        var red = new ManejadorFalso((_, _) => throw new HttpRequestException("sin red"));

        var resultado = await Comprobador(red, log).ComprobarAsync();

        resultado.Estado.Should().Be(EstadoDeComprobacion.NoDisponible);
        log.Entradas.Should().OnlyContain(e => e.Nivel == LogLevel.Debug);
    }

    [Fact]
    public async Task Con_json_roto_no_esta_disponible()
    {
        var resultado = await Comprobador(ManejadorFalso.ConTexto("<html>")).ComprobarAsync();

        resultado.Estado.Should().Be(EstadoDeComprobacion.NoDisponible);
    }

    [Fact]
    public async Task La_peticion_lleva_user_agent_y_cabecera_de_la_api()
    {
        HttpRequestMessage? vista = null;
        var red = new ManejadorFalso((p, _) =>
        {
            vista = p;
            return ManejadorFalso.Respuesta(Publicacion());
        });

        await Comprobador(red).ComprobarAsync();

        vista!.Headers.UserAgent.ToString().Should().Contain("CuadernoNODISLA");
        vista.Headers.Accept.ToString().Should().Contain("application/vnd.github+json");
        vista.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public void Una_descarga_fuera_del_repositorio_se_ignora()
    {
        var publicada = ComprobadorDeVersiones.Interpretar(
            Publicacion(prefijo: "https://malo.example/"), RepositorioPublico.CuadernoNodisla);

        publicada!.Instalador.Should().BeNull();
        publicada.SePuedeInstalar.Should().BeFalse();
    }
}

public sealed class DescargadorDeInstaladorPruebas : IDisposable
{
    private static readonly byte[] Contenido = Encoding.ASCII.GetBytes("MZ-instalador-de-prueba");
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-pruebas-descarga-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private static VersionPublicada Version() =>
        ComprobadorDeVersiones.Interpretar(ComprobadorDeVersionesPruebas.Publicacion(), RepositorioPublico.CuadernoNodisla)!;

    private static ManejadorFalso Red(string suma) => new((p, _) =>
        p.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
            ? ManejadorFalso.Respuesta(suma)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Contenido) });

    [Fact]
    public async Task Con_la_suma_buena_queda_el_exe_verificado()
    {
        var suma = Convert.ToHexString(SHA256.HashData(Contenido)).ToLowerInvariant();
        var descargador = new DescargadorDeInstalador(new FabricaFalsa(Red($"{suma}  CuadernoNodisla-Instalador-0.2.0.0.exe\n")), _carpeta);

        var resultado = await descargador.DescargarAsync(Version());

        resultado.Estado.Should().Be(EstadoDeDescarga.Verificado);
        File.ReadAllBytes(resultado.Ruta!).Should().Equal(Contenido);
        resultado.Ruta.Should().EndWith(".exe");
    }

    [Fact]
    public async Task Si_la_suma_no_casa_no_queda_nada_que_instalar()
    {
        var mala = new string('0', 64);
        var descargador = new DescargadorDeInstalador(new FabricaFalsa(Red(mala)), _carpeta);

        var resultado = await descargador.DescargarAsync(Version());

        resultado.Estado.Should().Be(EstadoDeDescarga.SumaNoCasa);
        resultado.Ruta.Should().BeNull();
        Directory.GetFiles(_carpeta, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_fichero_de_suma_no_se_descarga()
    {
        var red = Red("da igual");
        var version = ComprobadorDeVersiones.Interpretar(
            ComprobadorDeVersionesPruebas.Publicacion(conSuma: false), RepositorioPublico.CuadernoNodisla)!;

        var resultado = await new DescargadorDeInstalador(new FabricaFalsa(red), _carpeta).DescargarAsync(version);

        resultado.Estado.Should().Be(EstadoDeDescarga.Fallida);
        red.Direcciones.Should().BeEmpty();
    }

    [Fact]
    public async Task Una_suma_ilegible_no_descarga_el_instalador()
    {
        var red = Red("no es una suma");

        var resultado = await new DescargadorDeInstalador(new FabricaFalsa(red), _carpeta).DescargarAsync(Version());

        resultado.Estado.Should().Be(EstadoDeDescarga.Fallida);
        red.Direcciones.Should().ContainSingle();
    }

    [Theory]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789  fichero.exe")]
    [InlineData("SHA256 hash: abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789\r\n")]
    public void Lee_la_suma_en_varios_formatos(string texto) =>
        DescargadorDeInstalador.LeerSuma(texto).Should().Be("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789");
}

/// <summary>Registro que apunta cada entrada con su nivel.</summary>
public sealed class RegistroDePrueba<T> : ILogger<T>
{
    public List<(LogLevel Nivel, string Mensaje)> Entradas { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entradas.Add((logLevel, formatter(state, exception)));
}
