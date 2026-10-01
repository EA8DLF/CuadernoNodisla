using System.IO;
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Nodisla.Cuaderno.Servicios.Actualizaciones;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Soporte;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El aviso de versiones y el informe de fallos, sin red, sin ventanas y sin la hora de verdad.
/// </summary>
public sealed class ActualizacionesYFallosPruebas : IDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-pruebas-act-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private const string Publicacion = """
        {
          "tag_name": "v0.2.0", "name": "0.2.0", "body": "Novedades", "draft": false, "prerelease": false,
          "html_url": "https://github.com/EA8DLF/CuadernoNodisla/releases/tag/v0.2.0",
          "assets": [
            { "name": "CuadernoNodisla-Instalador-0.2.0.0.exe", "size": 3, "browser_download_url": "https://github.com/EA8DLF/CuadernoNodisla/releases/download/v0.2.0/CuadernoNodisla-Instalador-0.2.0.0.exe" },
            { "name": "CuadernoNodisla-Instalador-0.2.0.0.exe.sha256", "size": 64, "browser_download_url": "https://github.com/EA8DLF/CuadernoNodisla/releases/download/v0.2.0/CuadernoNodisla-Instalador-0.2.0.0.exe.sha256" }
          ]
        }
        """;

    private sealed class Red(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Peticiones { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Peticiones++;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class Fabrica(HttpMessageHandler manejador) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(manejador, disposeHandler: false);
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private static HttpResponseMessage Texto(string texto, HttpStatusCode codigo = HttpStatusCode.OK) =>
        new(codigo) { Content = new StringContent(texto) };

    private sealed class Apuntes
    {
        public int Confirmaciones;
        public string? Lanzado;
        public bool Cerrado;
        public Uri? Abierta;
        public string? Copiado;

        public AccionesDelSistema Acciones(bool aceptar = true) => new()
        {
            Confirmar = (_, _, _) => { Confirmaciones++; return aceptar; },
            LanzarInstalador = ruta => Lanzado = ruta,
            CerrarPrograma = () => Cerrado = true,
            AbrirEnNavegador = uri => Abierta = uri,
            CopiarAlPortapapeles = texto => Copiado = texto,
        };
    }

    private VistaModeloActualizaciones Modelo(Red red, AjustesDeActualizaciones ajustes, Apuntes apuntes) => new(
        new ComprobadorDeVersiones(new Fabrica(red), VersionSemantica.Analizar("0.1.0.0")),
        new DescargadorDeInstalador(new Fabrica(red), Path.Combine(_carpeta, "descargas")),
        ajustes,
        _carpeta,
        new RelojFijo(Ahora),
        apuntes.Acciones());

    [Fact]
    public async Task Al_arrancar_con_version_nueva_se_enciende_la_barra_y_no_abre_nada()
    {
        var apuntes = new Apuntes();
        var modelo = Modelo(new Red(_ => Texto(Publicacion)), new AjustesDeActualizaciones(), apuntes);

        await modelo.ComprobarAlArrancarAsync();

        modelo.AvisoVisible.Should().BeTrue();
        modelo.TituloDelAviso.Should().Contain("0.2.0");
        modelo.Notas.Should().Be("Novedades");
        apuntes.Confirmaciones.Should().Be(0);
        apuntes.Abierta.Should().BeNull();
        AjustesDeActualizaciones.Leer(_carpeta).UltimaComprobacion.Should().Be(Ahora);
    }

    [Fact]
    public async Task Comprobado_hace_una_hora_no_sale_a_la_red()
    {
        var red = new Red(_ => Texto(Publicacion));
        var modelo = Modelo(red, new AjustesDeActualizaciones { UltimaComprobacion = Ahora.AddHours(-1) }, new Apuntes());

        await modelo.ComprobarAlArrancarAsync();

        red.Peticiones.Should().Be(0);
        modelo.AvisoVisible.Should().BeFalse();
    }

    [Fact]
    public async Task Con_la_casilla_quitada_no_sale_a_la_red()
    {
        var red = new Red(_ => Texto(Publicacion));
        var modelo = Modelo(red, new AjustesDeActualizaciones { ComprobarAlArrancar = false }, new Apuntes());

        await modelo.ComprobarAlArrancarAsync();

        red.Peticiones.Should().Be(0);
    }

    [Fact]
    public async Task El_404_al_arrancar_no_ensena_nada_ni_gasta_el_dia()
    {
        var modelo = Modelo(new Red(_ => Texto("{}", HttpStatusCode.NotFound)), new AjustesDeActualizaciones(), new Apuntes());

        await modelo.ComprobarAlArrancarAsync();

        modelo.AvisoVisible.Should().BeFalse();
        modelo.Estado.Should().BeEmpty();
        AjustesDeActualizaciones.Leer(_carpeta).UltimaComprobacion.Should().BeNull();
    }

    [Fact]
    public async Task Si_la_suma_no_casa_no_se_pregunta_ni_se_instala()
    {
        var apuntes = new Apuntes();
        var red = new Red(p => p.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
            ? Texto(new string('0', 64))
            : p.RequestUri.Host == "api.github.com" ? Texto(Publicacion) : Texto("MZ!"));
        var modelo = Modelo(red, new AjustesDeActualizaciones(), apuntes);
        await modelo.BuscarActualizacionesCommand.ExecuteAsync(null);

        await modelo.DescargarEInstalarCommand.ExecuteAsync(null);

        apuntes.Confirmaciones.Should().Be(0);
        apuntes.Lanzado.Should().BeNull();
        apuntes.Cerrado.Should().BeFalse();
        modelo.Estado.Should().Contain("no coincide");
    }

    [Fact]
    public async Task Con_la_suma_buena_pregunta_lanza_y_cierra()
    {
        var apuntes = new Apuntes();
        var suma = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData("MZ!"u8.ToArray()));
        var red = new Red(p => p.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
            ? Texto(suma)
            : p.RequestUri.Host == "api.github.com" ? Texto(Publicacion) : Texto("MZ!"));
        var modelo = Modelo(red, new AjustesDeActualizaciones(), apuntes);
        await modelo.BuscarActualizacionesCommand.ExecuteAsync(null);

        await modelo.DescargarEInstalarCommand.ExecuteAsync(null);

        apuntes.Confirmaciones.Should().Be(1);
        apuntes.Lanzado.Should().EndWith("CuadernoNodisla-Instalador-0.2.0.0.exe");
        apuntes.Cerrado.Should().BeTrue();
    }

    [Fact]
    public void El_formulario_ensena_lo_adjunto_limpio_y_envia_lo_que_se_ve()
    {
        var apuntes = new Apuntes();
        var modelo = new VistaModeloReportarFallo(
            () => "Versión: 0.1.0\nRadio: yaesu-ft710",
            () => "linea con password=secreto y jose@correo.es",
            apuntes.Acciones())
        {
            Titulo = "Se cuelga",
            QuePaso = "Al conectar desde IL18rj",
        };

        modelo.IncluirRegistro = true;
        modelo.Registro.Should().Contain("password=secreto", "el lector ya entrega el texto limpio; aquí es un doble");
        modelo.VistaPrevia.Should().NotContain("secreto").And.NotContain("jose@correo.es").And.NotContain("IL18rj");

        modelo.EnviarCommand.Execute(null);

        apuntes.Abierta!.AbsoluteUri.Should().StartWith("https://github.com/EA8DLF/CuadernoNodisla/issues/new?title=Se%20cuelga&body=");
        Uri.UnescapeDataString(apuntes.Abierta.AbsoluteUri).Should().Contain(modelo.VistaPrevia);
        apuntes.Copiado.Should().BeNull();
    }

    [Fact]
    public void Sin_titulo_no_se_puede_enviar()
    {
        var modelo = new VistaModeloReportarFallo(() => "", () => "", new Apuntes().Acciones()) { QuePaso = "algo" };

        modelo.EnviarCommand.CanExecute(null).Should().BeFalse();
    }
}
