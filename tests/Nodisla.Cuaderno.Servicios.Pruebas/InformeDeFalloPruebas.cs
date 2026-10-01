using FluentAssertions;
using Nodisla.Cuaderno.Servicios.Informes;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

public sealed class LimpiadorDeDatosPersonalesPruebas
{
    private static string L(string texto) => LimpiadorDeDatosPersonales.Limpiar(texto);

    [Fact]
    public void Tacha_la_contrasena_de_la_api_de_qrz_en_la_direccion()
    {
        var limpio = L("GET https://xmldata.qrz.com/xml/current/?username=EA8DLF;password=Secreta123;agent=x fallo");

        limpio.Should().NotContain("Secreta123").And.NotContain("EA8DLF;");
        limpio.Should().Contain("https://xmldata.qrz.com/xml/current/?username=***;password=***;agent=***");
        limpio.Should().EndWith(" fallo");
    }

    [Fact]
    public void Tacha_la_sesion_y_las_claves_en_parametros()
    {
        var limpio = L("https://logbook.qrz.com/api?KEY=ABCD-1234&ACTION=STATUS y https://x.org/a?s=0f9a8b#frag");

        limpio.Should().NotContain("ABCD-1234").And.NotContain("0f9a8b");
        limpio.Should().Contain("?KEY=***&ACTION=***").And.Contain("?s=***#frag");
    }

    [Fact]
    public void Tacha_usuario_y_contrasena_dentro_de_la_direccion() =>
        L("smtp://luis:clave99@smtp.ejemplo.es:587").Should().Be("smtp://***@smtp.ejemplo.es:587");

    [Theory]
    [InlineData("password=hunter2", "password=***")]
    [InlineData("Contraseña: hunter2", "Contraseña: ***")]
    [InlineData("pwd = hunter2, siguiente", "pwd = ***, siguiente")]
    [InlineData("\"Password\": \"hunter2\"", "\"Password\": \"***\"")]
    [InlineData("api_key=abcdef", "api_key=***")]
    [InlineData("Authorization: Bearer abcdefghijklmnop", "Authorization: Bearer ***")]
    public void Tacha_contrasenas_sueltas(string texto, string esperado) => L(texto).Should().Be(esperado);

    [Fact]
    public void Tacha_fichas_de_github() =>
        L("uso ghp_0123456789abcdefghijABCDEFGHIJ012345 aqui").Should().Be("uso *** aqui");

    [Fact]
    public void Tacha_correos() =>
        L("Escribir a luis.perez@ejemplo.es o a otro+x@sub.dominio.org.")
            .Should().Be("Escribir a [correo] o a [correo].");

    [Theory]
    [InlineData("QTH en IL18rj de Gran Canaria", "QTH en [locator] de Gran Canaria")]
    [InlineData("grid IL18rj45", "grid [locator]")]
    [InlineData("Locator: IL18", "Locator: [locator]")]
    [InlineData("<MY_GRIDSQUARE:6>IL18RJ", "<MY_GRIDSQUARE:6>[locator]")]
    public void Tacha_el_localizador(string texto, string esperado) => L(texto).Should().Be(esperado);

    [Fact]
    public void No_toca_indicativos_ni_modos()
    {
        const string texto = "QSO con EA8DLF en FT8, 14.074 MHz, 20m, rigctld 127.0.0.1:4532";

        L(texto).Should().Be(texto);
    }

    [Theory]
    [InlineData("Estación en 28.1234, -15.4321 ok", "Estación en [coordenadas] ok")]
    [InlineData("lat=28.12 lon=-15.43", "lat=[coordenada] lon=[coordenada]")]
    [InlineData("Latitud: 28,1234", "Latitud: [coordenada]")]
    [InlineData("28°07'12\"N 15°25'30\"W", "[coordenada] [coordenada]")]
    public void Tacha_coordenadas(string texto, string esperado) => L(texto).Should().Be(esperado);

    [Fact]
    public void Tacha_el_usuario_de_windows_en_las_rutas() =>
        L(@"No se pudo abrir C:\Users\Luis\AppData\Roaming\CuadernoNodisla\ajustes.json")
            .Should().Be(@"No se pudo abrir C:\Users\[usuario]\AppData\Roaming\CuadernoNodisla\ajustes.json");

    [Fact]
    public void Limpiar_dos_veces_da_lo_mismo()
    {
        const string texto = "https://a.org/?password=x&k=y luis@x.es IL18rj lat=28.1 C:\\Users\\Luis\\x token abcdefghijk";

        var una = L(texto);

        L(una).Should().Be(una);
    }
}

public sealed class InformeDeFalloPruebas
{
    private static DatosDeInforme Datos(string registro = "") => new()
    {
        Titulo = "El mapa no se abre",
        QuePaso = "Al pulsar Mapa sale en blanco.",
        QueEsperaba = "Ver el mapa.",
        Pasos = "1. Abrir\n2. Pulsar Mapa",
        Entorno = "Versión 0.1.0\nWindows 10\nRadio: FT-710\nCAT: Directo",
        Registro = registro,
    };

    [Fact]
    public void La_direccion_abre_nueva_incidencia_con_titulo_y_cuerpo()
    {
        var envio = InformeDeFallo.Preparar(Datos());

        var direccion = envio.Direccion.AbsoluteUri;
        direccion.Should().StartWith("https://github.com/EA8DLF/CuadernoNodisla/issues/new?title=");
        direccion.Should().Contain("title=El%20mapa%20no%20se%20abre&body=");
        envio.CuerpoEnElPortapapeles.Should().BeFalse();

        var cuerpo = Uri.UnescapeDataString(direccion[(direccion.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        cuerpo.Should().Be(envio.CuerpoCompleto);
        cuerpo.Should().Contain("### Qué pasó").And.Contain("Al pulsar Mapa sale en blanco.")
            .And.Contain("### Pasos para reproducirlo").And.Contain("Radio: FT-710");
    }

    [Fact]
    public void Los_caracteres_especiales_van_codificados()
    {
        var envio = InformeDeFallo.Preparar(Datos() with { Titulo = "Falla & rompe #3 ?x=1 ñ" });

        envio.Direccion.AbsoluteUri.Should().Contain("title=Falla%20%26%20rompe%20%233%20%3Fx%3D1%20%C3%B1&body=");
    }

    [Fact]
    public void El_cuerpo_se_limpia_aunque_el_operador_escriba_datos_personales()
    {
        var envio = InformeDeFallo.Preparar(Datos() with
        {
            QuePaso = "Me escribo con luis@ejemplo.es desde IL18rj",
            Titulo = "Fallo de luis@ejemplo.es",
        });

        envio.CuerpoCompleto.Should().NotContain("luis@ejemplo.es").And.NotContain("IL18rj");
        envio.Direccion.AbsoluteUri.Should().NotContain("ejemplo.es");
    }

    [Fact]
    public void Si_no_cabe_la_direccion_lleva_el_aviso_y_el_cuerpo_va_al_portapapeles()
    {
        var registro = string.Join('\n', Enumerable.Range(0, 2000).Select(i => $"linea {i} del registro"));

        var envio = InformeDeFallo.Preparar(Datos(registro));

        envio.CuerpoEnElPortapapeles.Should().BeTrue();
        envio.Direccion.AbsoluteUri.Length.Should().BeLessThanOrEqualTo(InformeDeFallo.LargoMaximoDeDireccion);
        Uri.UnescapeDataString(envio.Direccion.AbsoluteUri).Should().Contain(InformeDeFallo.AvisoDePortapapeles);
        envio.CuerpoCompleto.Should().Contain("linea 1999 del registro");
    }

    [Fact]
    public void Justo_en_el_limite_cabe()
    {
        var normal = InformeDeFallo.Preparar(Datos());
        var largo = normal.Direccion.AbsoluteUri.Length;

        InformeDeFallo.Preparar(Datos(), largoMaximo: largo).CuerpoEnElPortapapeles.Should().BeFalse();
        InformeDeFallo.Preparar(Datos(), largoMaximo: largo - 1).CuerpoEnElPortapapeles.Should().BeTrue();
    }

    [Fact]
    public void Un_titulo_vacio_no_deja_la_incidencia_sin_titulo() =>
        InformeDeFallo.Titulo("  ").Should().Be("Fallo sin título");

    [Fact]
    public void El_registro_va_plegado_en_bloque_de_codigo()
    {
        var cuerpo = InformeDeFallo.Cuerpo(Datos("2026-10-01 [ERR] Algo ```raro```"));

        cuerpo.Should().Contain("<details>").And.Contain("````text").And.Contain("[ERR] Algo");
    }
}
