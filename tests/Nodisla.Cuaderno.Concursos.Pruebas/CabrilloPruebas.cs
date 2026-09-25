using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Cabrillo;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>
/// El formato Cabrillo, comprobado contra la especificacion publica de la version 3.
/// </summary>
public sealed class CabrilloPruebas
{
    private static readonly EscritorCabrillo Escritor = new();

    private static CabeceraCabrillo Cabecera => new()
    {
        Contest = "CQ-WW-CW",
        Indicativo = Indicativo.Parse("EA8DLF"),
        Ubicacion = "DX",
        Nombre = "Jose",
        Locator = Locator.Parse("IL18"),
        PuntuacionReclamada = 1234567,
    };

    private static Qso Contacto(
        string call, string banda = "20m", string modo = "CW", int hora = 12, double mhz = 14.025) => new()
        {
            Call = Indicativo.Parse(call),
            Band = Banda.Parse(banda),
            Mode = Modo.Parse(modo),
            Freq = Frecuencia.DesdeMegahercios((decimal)mhz),
            InicioUtc = new DateTimeOffset(2026, 11, 28, hora, 34, 0, TimeSpan.Zero),
            RstSent = Informe.Parse("599"),
            RstRcvd = Informe.Parse("599"),
            StxString = "33",
            SrxString = "14",
        };

    private static string Generar(params Qso[] contactos) =>
        Escritor.Generar(Cabecera, contactos).Texto;

    [Fact]
    public void EmpiezaYTerminaComoMandaLaEspecificacion()
    {
        var texto = Generar(Contacto("EA1ABC"));
        var lineas = texto.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        lineas[0].Should().Be("START-OF-LOG: 3.0");
        lineas[^1].Should().Be("END-OF-LOG:");
    }

    [Fact]
    public void ElFicheroVaConFinalesDeLineaDeWindows()
    {
        var texto = Generar(Contacto("EA1ABC"));
        texto.Should().Contain("\r\n");
        texto.Replace("\r\n", string.Empty).Should().NotContain("\n", "ningun salto suelto de Unix");
    }

    [Fact]
    public void LaLineaDeContactoSigueLaPlantillaDeLaEspecificacion()
    {
        var texto = Generar(Contacto("EA1ABC"));
        var linea = texto.Split("\r\n").First(l => l.StartsWith("QSO:", StringComparison.Ordinal));

        // QSO: freq mo date time call rst exch call rst exch t
        var campos = linea["QSO:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        campos.Should().Equal("14025", "CW", "2026-11-28", "1234", "EA8DLF", "599", "33", "EA1ABC", "599", "14", "0");
    }

    [Theory]
    [InlineData("CW", "CW")]
    [InlineData("SSB", "PH")]
    [InlineData("USB", "PH")]
    [InlineData("FM", "FM")]
    [InlineData("RTTY", "RY")]
    [InlineData("FT8", "DG")]
    [InlineData("PSK", "DG")]
    public void SoloSeEscribenLasCincoAbreviaturasDeModoAdmitidas(string modo, string esperado)
    {
        var texto = Generar(Contacto("EA1ABC", modo: modo));
        var linea = texto.Split("\r\n").First(l => l.StartsWith("QSO:", StringComparison.Ordinal));

        linea["QSO:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[1].Should().Be(esperado);
    }

    [Theory]
    // Por debajo de 50 MHz, la frecuencia de verdad en kilohercios.
    [InlineData("20m", 14.025, "14025")]
    [InlineData("160m", 1.825, "1825")]
    // De 50 MHz para arriba, el designador de banda y no la frecuencia.
    [InlineData("6m", 50.150, "50")]
    [InlineData("2m", 144.300, "144")]
    [InlineData("70cm", 432.200, "432")]
    [InlineData("23cm", 1296.200, "1.2G")]
    [InlineData("3cm", 10368.100, "10G")]
    public void LaFrecuenciaSeEscribeComoQuiereCabrillo(string banda, double mhz, string esperado)
    {
        FrecuenciaCabrillo.De(Frecuencia.DesdeMegahercios((decimal)mhz), Banda.Parse(banda))
            .Should().Be(esperado);
    }

    [Fact]
    public void SinFrecuenciaSeUsaElBordeDeLaBandaYNoSeDejaLaColumnaVacia()
    {
        FrecuenciaCabrillo.De(Frecuencia.Cero, Banda.Parse("20m")).Should().Be("14000");
    }

    [Fact]
    public void LosContactosSalenEnOrdenCronologicoAunqueLleguenDesordenados()
    {
        var texto = Generar(
            Contacto("EA1TARDE", hora: 20),
            Contacto("EA1PRONTO", hora: 8),
            Contacto("EA1MEDIO", hora: 14));

        var indicativos = texto.Split("\r\n")
            .Where(l => l.StartsWith("QSO:", StringComparison.Ordinal))
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[7])
            .ToList();

        indicativos.Should().Equal("EA1PRONTO", "EA1MEDIO", "EA1TARDE");
    }

    [Fact]
    public void LosContactosQueNoCuentanVanComoXQso()
    {
        var bueno = Contacto("EA1ABC", hora: 8);
        var malo = Contacto("EA1XYZ", hora: 9);
        var (texto, resultado) = Escritor.Generar(Cabecera, [bueno, malo], q => q.Call.Valor == "EA1XYZ");

        resultado.Lineas.Should().Be(1);
        resultado.Excluidas.Should().Be(1);
        texto.Should().Contain("X-QSO: ");
        texto.Split("\r\n").Count(l => l.StartsWith("QSO:", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void LaPuntuacionVaSinPuntosNiComas()
    {
        Generar(Contacto("EA1ABC")).Should().Contain("CLAIMED-SCORE: 1234567");
    }

    [Fact]
    public void LasCategoriasSalenConLosValoresQueAdmiteLaEspecificacion()
    {
        var texto = Escritor.Generar(
            Cabecera with
            {
                Operadores = CategoriaOperador.Multioperador,
                Transmisores = CategoriaTransmisor.Uno,
                Potencia = CategoriaPotencia.Alta,
                Asistencia = CategoriaAsistencia.ConAyuda,
                Estacion = CategoriaEstacion.Expedicion,
            },
            [Contacto("EA1ABC")]).Texto;

        texto.Should().Contain("CATEGORY-OPERATOR: MULTI-OP");
        texto.Should().Contain("CATEGORY-TRANSMITTER: ONE");
        texto.Should().Contain("CATEGORY-POWER: HIGH");
        texto.Should().Contain("CATEGORY-ASSISTED: ASSISTED");
        texto.Should().Contain("CATEGORY-STATION: EXPEDITION");
    }

    [Fact]
    public void LaBandaYElModoSeDeducenDeLosContactos()
    {
        Generar(Contacto("EA1ABC")).Should().Contain("CATEGORY-BAND: 20M").And.Contain("CATEGORY-MODE: CW");

        Escritor.Generar(Cabecera, [Contacto("EA1ABC"), Contacto("EA1XYZ", banda: "40m", mhz: 7.025)])
            .Texto.Should().Contain("CATEGORY-BAND: ALL");

        Escritor.Generar(Cabecera, [Contacto("EA1ABC"), Contacto("EA1XYZ", modo: "SSB")])
            .Texto.Should().Contain("CATEGORY-MODE: MIXED");
    }

    [Fact]
    public void UnMultioperadorSinCategoriaDeTransmisorSeAvisa()
    {
        var (_, resultado) = Escritor.Generar(
            Cabecera with { Operadores = CategoriaOperador.Multioperador },
            [Contacto("EA1ABC")]);

        resultado.Avisos.Should().ContainSingle().Which.Should().Contain("CATEGORY-TRANSMITTER");
    }

    [Fact]
    public void UnIdentificadorDeConcursoConCaracteresRarosSeAvisa()
    {
        var (_, resultado) = Escritor.Generar(
            Cabecera with { Contest = "CQ WW CW" },
            [Contacto("EA1ABC")]);

        resultado.Avisos.Should().Contain(a => a.Contains("CONTEST", StringComparison.Ordinal));
    }

    [Fact]
    public void UnIndicativoConCaracteresQueCabrilloNoAdmiteSeLimpiaYSeAvisa()
    {
        var sucio = Contacto("EA1ABC");
        sucio.Call = Indicativo.Crudo("EA1-ABC");

        var (texto, resultado) = Escritor.Generar(Cabecera, [sucio]);

        resultado.Avisos.Should().Contain(a => a.Contains("no admite", StringComparison.Ordinal));
        texto.Should().Contain("EA1ABC");
    }

    [Fact]
    public void ElNumeroDeTransmisorSePuedeQuitar()
    {
        var texto = Escritor.Generar(Cabecera with { NumeroDeTransmisor = null }, [Contacto("EA1ABC")]).Texto;
        var linea = texto.Split("\r\n").First(l => l.StartsWith("QSO:", StringComparison.Ordinal));

        linea["QSO:".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(10);
    }

    [Fact]
    public void UnLogSinContactosSigueSiendoUnCabrilloValido()
    {
        var (texto, resultado) = Escritor.Generar(Cabecera, []);

        resultado.Lineas.Should().Be(0);
        texto.Should().StartWith("START-OF-LOG: 3.0").And.EndWith("END-OF-LOG:\r\n");
    }

    [Fact]
    public async Task ElFicheroSeGuardaEnAscii()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"cabrillo-{Guid.NewGuid():N}.log");
        try
        {
            var resultado = await Escritor.GuardarAsync(
                ruta, Cabecera with { Nombre = "José" }, [Contacto("EA1ABC")]);

            resultado.Lineas.Should().Be(1);
            var crudo = await File.ReadAllBytesAsync(ruta);
            crudo.Should().OnlyContain(b => b < 128, "los robots de concurso son viejos y ASCII no falla");
        }
        finally
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
    }
}
