using FluentAssertions;
using Nodisla.Cuaderno.Satelites.Orbital;

namespace Nodisla.Cuaderno.Satelites.Pruebas;

/// <summary>Lectura de los ficheros de elementos y aviso de caducidad.</summary>
public sealed class ElementosPruebas
{
    private const string Iss1 = "1 25544U 98067A   24015.54791667  .00016717  00000-0  30177-3 0  9002";
    private const string Iss2 = "2 25544  51.6416 247.4627 0006703 130.5360 325.0288 15.49682159 33601";

    [Fact]
    public void LeeUnJuegoDeElementosConTodosSusCampos()
    {
        var e = LectorDeElementos.LeerPar("ISS (ZARYA)", Iss1, Iss2);

        e.Nombre.Should().Be("ISS (ZARYA)");
        e.NumeroCatalogo.Should().Be(25544);
        e.DesignacionInternacional.Should().Be("98067A");
        e.InclinacionGrados.Should().BeApproximately(51.6416, 1e-6);
        e.NodoAscendenteGrados.Should().BeApproximately(247.4627, 1e-6);
        e.Excentricidad.Should().BeApproximately(0.0006703, 1e-9);
        e.ArgumentoPerigeoGrados.Should().BeApproximately(130.5360, 1e-6);
        e.AnomaliaMediaGrados.Should().BeApproximately(325.0288, 1e-6);
        e.MovimientoMedioVueltasDia.Should().BeApproximately(15.49682159, 1e-9);
        e.BEstrella.Should().BeApproximately(0.30177e-3, 1e-12);
        e.EsEspacioProfundo.Should().BeFalse();
        e.Periodo.TotalMinutes.Should().BeApproximately(92.9, 0.1);
    }

    [Fact]
    public void LaEpocaSaleDelAnoDeDosCifrasYDelDiaFraccionario()
    {
        var e = LectorDeElementos.LeerPar("ISS", Iss1, Iss2);

        // 24015,54791667 = 2024, día 15, a las 13:09:00 UTC.
        e.Epoca.Year.Should().Be(2024);
        e.Epoca.Month.Should().Be(1);
        e.Epoca.Day.Should().Be(15);
        e.Epoca.Hour.Should().Be(13);
        e.Epoca.Minute.Should().Be(9);
    }

    [Fact]
    public void LosAnosDeCincuentaYSieteEnAdelanteSonDelSigloVeinte()
    {
        // El corte del formato: 57-99 es 19xx y 00-56 es 20xx. Es el fallo clásico al leer
        // elementos históricos, así que se fija con una prueba.
        var viejo = LectorDeElementos.LeerPar(
            "OSCAR 7",
            "1 07530U 74089B   80179.78495062  .00000023  00000-0  28098-4 0  4753",
            "2 07530 101.7000 100.0000 0012000 100.0000 260.0000 12.53600000000010");

        viejo.Epoca.Year.Should().Be(1980);
    }

    [Fact]
    public void LeeUnFicheroDeTresLineasPorSateliteYSeSaltaLoQueNoEntiende()
    {
        var fichero = string.Join("\n",
            "# esto es un comentario",
            "ISS (ZARYA)",
            Iss1,
            Iss2,
            "basura que no es nada",
            "SEGUNDO",
            Iss1.Replace("25544", "25545"),
            Iss2.Replace("25544", "25545"));

        var leidos = LectorDeElementos.Leer(fichero);

        leidos.Should().HaveCount(2);
        leidos[0].Nombre.Should().Be("ISS (ZARYA)");
        leidos[1].Nombre.Should().Be("SEGUNDO");
    }

    [Fact]
    public void RechazaUnParDeLineasQueNoCuadran()
    {
        var otro = Iss2.Replace("25544", "25999");
        LectorDeElementos.TryLeerPar("MEZCLA", Iss1, otro, out _).Should().BeFalse();
    }

    [Fact]
    public void ElDigitoDeControlDetectaUnaLineaTocada()
    {
        LectorDeElementos.DigitoDeControlCorrecto(Iss1).Should().BeTrue();
        LectorDeElementos.DigitoDeControlCorrecto(Iss2).Should().BeTrue();

        // Se cambia una cifra sin tocar el dígito final: tiene que saltar.
        var tocada = Iss1[..20] + "9" + Iss1[21..];
        LectorDeElementos.DigitoDeControlCorrecto(tocada).Should().BeFalse();
    }

    [Fact]
    public void AvisaCuandoLosElementosEstanViejos()
    {
        var e = LectorDeElementos.LeerPar("ISS", Iss1, Iss2);
        var frescura = TimeSpan.FromDays(3);

        // Instantes fijos: no se mira el reloj de la máquina en ninguna aserción.
        var recien = e.Epoca.AddHours(6);
        var tarde = e.Epoca.AddDays(20);

        e.EstanFrescos(recien, frescura).Should().BeTrue();
        e.DescribirAntiguedad(recien, frescura).Should().StartWith("Elementos del 15/01/2024");
        e.DescribirAntiguedad(recien, frescura).Should().NotContain("caducados");

        e.EstanFrescos(tarde, frescura).Should().BeFalse();
        e.DescribirAntiguedad(tarde, frescura).Should().Contain("caducados");
        e.DescribirAntiguedad(tarde, frescura).Should().Contain("20 días");
    }
}
