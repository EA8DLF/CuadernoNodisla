using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Macros;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>Las macros: texto con sustituciones, sin atarse a ningun medio.</summary>
public sealed class MacrosPruebas
{
    private static readonly MotorDeMacros Motor = new();

    private static ContextoDeMacro Contexto => new()
    {
        MiIndicativo = Indicativo.Parse("EA8DLF"),
        Corresponsal = "ea1abc",
        Nombre = "Luis",
        MiNombre = "Jose",
        MiQth = "Las Palmas",
        MiLocator = Locator.Parse("IL18"),
        InformeEnviado = Informe.Parse("599"),
        InformeRecibido = Informe.Parse("579"),
        Serie = 7,
        Banda = Banda.Parse("20m"),
        Modo = Modo.Parse("CW"),
        Frecuencia = Frecuencia.DesdeMegahercios(14.025m),
        Concurso = "CQ-WW-CW",
        AhoraUtc = new DateTimeOffset(2026, 11, 28, 12, 34, 0, TimeSpan.Zero),
    };

    [Fact]
    public void SustituyeLoQueSabeYPoneElIndicativoEnMayusculas()
    {
        var salida = Motor.Expandir("<CALL> DE <MICALL> UR <RST>", Contexto);

        salida.Texto.Should().Be("EA1ABC DE EA8DLF UR 599");
        salida.Limpia.Should().BeTrue();
    }

    [Fact]
    public void ElNumeroDeSerieSePuedeRellenarConCeros()
    {
        Motor.Expandir("NR <SERIE>", Contexto).Texto.Should().Be("NR 7");
        Motor.Expandir("NR <SERIE:3>", Contexto).Texto.Should().Be("NR 007");
    }

    [Fact]
    public void LaHoraYLaFechaSalenDelContextoYNoDelReloj()
    {
        // Asi las pruebas no dependen de a que hora corran.
        Motor.Expandir("<FECHA> <HORA>", Contexto).Texto.Should().Be("2026-11-28 1234");
    }

    [Fact]
    public void UnaEtiquetaDesconocidaSeDejaAlaVistaYSeAvisa()
    {
        var salida = Motor.Expandir("HOLA <LOQUESEA> ADIOS", Contexto);

        salida.Texto.Should().Be("HOLA <LOQUESEA> ADIOS");
        salida.Desconocidas.Should().ContainSingle().Which.Should().Be("LOQUESEA");
        salida.Limpia.Should().BeFalse();
    }

    [Fact]
    public void UnaEtiquetaConocidaPeroSinDatoSeAvisaSinRomperElTexto()
    {
        var salida = Motor.Expandir("QTH <QTH>", Contexto);

        salida.Texto.Should().Be("QTH ");
        salida.SinDatos.Should().ContainSingle().Which.Should().Be("QTH");
    }

    [Fact]
    public void ParaEscribirUnAnguloSePonenDos()
    {
        Motor.Expandir("<<AR>> no es una etiqueta", Contexto).Texto
            .Should().Be("<AR> no es una etiqueta");
    }

    [Fact]
    public void UnAnguloSinCerrarEsTextoYNoUnaEtiquetaRota()
    {
        Motor.Expandir("2 < 3", Contexto).Texto.Should().Be("2 < 3");
    }

    [Fact]
    public void ElOperadorPuedeAnadirSustitucionesPropias()
    {
        var conPropias = Contexto with
        {
            Propias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["CLUB"] = "URE LPA" },
        };

        Motor.Expandir("CLUB <CLUB>", conPropias).Texto.Should().Be("CLUB URE LPA");
    }

    [Fact]
    public void LaListaDeSustitucionesEstaDocumentadaParaElEditor()
    {
        MotorDeMacros.Sustituciones.Should().ContainKey("CALL").And.ContainKey("SERIE");
        MotorDeMacros.Sustituciones["MICALL"].Should().Be("Mi indicativo");
    }

    [Fact]
    public void LasMacrosDeLog4OmSeTraducenALaSintaxisPropia()
    {
        // Esta es una linea literal del defaultMacro.txt de Log4OM.
        const string original = "! DE * YR RST <STTXF> <STTX> NAME IS <NAME> QTH IS <MY_QTH>";

        LectorDeMacrosLog4Om.Traducir(original).Should()
            .Be("<CALL> DE <MICALL> YR RST <SERIE:3> <SERIE> NAME IS <NOMBRE> QTH IS <MIQTH>");
    }

    [Fact]
    public void ElFicheroDeMacrosDeLog4OmSeImportaEntero()
    {
        string[] fichero =
        [
            "CQ DE * PSE K###CQ SHORT",
            "CQ DX DE ! *  PSE K###CQ LONG",
            "! DE * PSE K###ANSWER",
            "###",
            "###",
        ];

        var juego = LectorDeMacrosLog4Om.Leer("Log4OM", fichero);

        juego.Macros.Should().HaveCount(3, "las lineas vacias del original no son macros");
        juego.De("F1")!.Nombre.Should().Be("CQ SHORT");
        juego.De("F1")!.Texto.Should().Be("CQ DE <MICALL> PSE K");
        juego.De("F3")!.Texto.Should().Be("<CALL> DE <MICALL> PSE K");
        juego.De("F9").Should().BeNull();
    }

    [Fact]
    public void ElFicheroRealDeLog4OmSeLeeSiEstaEnLaMaquina()
    {
        var ruta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Log4OM2", "macro", "defaultMacro.txt");
        if (!File.Exists(ruta)) return;

        var juego = LectorDeMacrosLog4Om.Leer("Log4OM", File.ReadAllLines(ruta));

        juego.Macros.Should().NotBeEmpty();
        juego.Macros.Should().OnlyContain(m => !m.Texto.Contains('*') && !m.Texto.Contains('!'));
        juego.Macros.Should().OnlyContain(m => !m.Texto.Contains("###", StringComparison.Ordinal));
    }

    [Fact]
    public void LosJuegosDePartidaSeExpandenSinDejarEtiquetasSueltas()
    {
        foreach (var macro in JuegoDeMacros.Concurso.Macros.Concat(JuegoDeMacros.Normal.Macros))
        {
            var salida = Motor.Expandir(macro.Texto, Contexto with { Intercambio = "599 33", Qth = "Madrid" });
            salida.Desconocidas.Should().BeEmpty($"la macro «{macro.Nombre}» usa etiquetas que el motor no conoce");
        }
    }

    [Fact]
    public void UnaMacroNoDicePorQueMedioSale()
    {
        // La misma macro vale para telegrafia, para fonia grabada y para los modos digitales:
        // por eso su texto no lleva nada que solo entienda uno de los tres.
        var macro = JuegoDeMacros.Concurso.De("F1")!;

        macro.Texto.Should().NotContainAny("WPM", "PPM", "{", "}", "|");
        Motor.Expandir(macro.Texto, Contexto).Texto.Should().Be("CQ TEST EA8DLF EA8DLF TEST");
    }
}
