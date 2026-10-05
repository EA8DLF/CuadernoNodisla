using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Marco;
using Nodisla.Cuaderno.Modos.Pruebas.Banco;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Pruebas.Wspr;

/// <summary>
/// La cadena entera de WSPR: se codifica, se pone en el aire y se decodifica con el propio decodificador.
/// </summary>
public class CadenaWsprPruebas
{
    private static ResultadoWspr IdaYVuelta(string texto, double db, double desfase, double frecuencia, double deriva, int semilla, DecodificadorWspr? decodificador = null)
    {
        var ventana = BancoWspr.Ventana(texto, frecuencia, desfase, deriva, db, new Random(semilla));
        return (decodificador ?? new DecodificadorWspr()).Decodificar(ventana, DateTimeOffset.UnixEpoch);
    }

    [Theory]
    [InlineData("EA8DLF IL18 37")]
    [InlineData("K1ABC FN42 37")]
    [InlineData("PJ4/K1ABC 37")]
    [InlineData("EA8DLF/P 20")]
    public void UnMensajeSaleYVuelveIgualConPocoRuido(string texto)
    {
        var r = IdaYVuelta(texto, db: 0, desfase: 0, frecuencia: 1500, deriva: 0, semilla: 1);
        r.Decodificaciones.Should().ContainSingle(d => d.Texto == texto);
        r.Decodificaciones.Should().HaveCount(1, "no puede salir nada mas");
    }

    [Fact]
    public void ElTipo3SeResuelveSiSeOyoAntesElTipo2()
    {
        var decodificador = new DecodificadorWspr();
        IdaYVuelta("PJ4/K1ABC 37", 0, 0, 1500, 0, 2, decodificador).Decodificaciones.Should().ContainSingle(d => d.Texto == "PJ4/K1ABC 37");
        var r = IdaYVuelta("<PJ4/K1ABC> FK52UD 37", 0, 0, 1500, 0, 3, decodificador);
        r.Decodificaciones.Should().ContainSingle(d => d.Texto == "<PJ4/K1ABC> FK52UD 37");
        r.Decodificaciones[0].Llamante.Valor.Should().Be("PJ4/K1ABC");
        r.Decodificaciones[0].Locator.Valor.Should().Be("FK52UD");
    }

    [Fact]
    public void ElTipo3SinTipo2PrevioSaleSinResolver()
    {
        var r = IdaYVuelta("<PJ4/K1ABC> FK52UD 37", 0, 0, 1500, 0, 3);
        r.Decodificaciones.Should().ContainSingle(d => d.Texto == "<...> FK52UD 37");
        r.Decodificaciones[0].Llamante.EsVacio.Should().BeTrue("un resumen sin resolver no es un indicativo");
    }

    [Fact]
    public void ElDesfaseLaFrecuenciaYLaDerivaSeMidenBien()
    {
        var r = IdaYVuelta("EA8DLF IL18 37", db: -10, desfase: 0.8, frecuencia: 1437.5, deriva: 0.6, semilla: 4);
        var d = r.Decodificaciones.Should().ContainSingle().Subject;
        d.Texto.Should().Be("EA8DLF IL18 37");
        d.TonoHz.Should().BeInRange(1437, 1438);
        d.DesfaseSegundos.Should().BeApproximately(0.8, 0.1);
        d.Modo.Should().Be(ModoDelModem.Wspr);
        d.Llamante.Valor.Should().Be("EA8DLF");
        d.Locator.Valor.Should().Be("IL18");
    }

    [Fact]
    public void UnaSenalMuyDebilTodaviaSale()
    {
        var r = IdaYVuelta("EA8DLF IL18 37", db: -26, desfase: -0.4, frecuencia: 1562.3, deriva: 0, semilla: 9);
        r.Decodificaciones.Should().ContainSingle(d => d.Texto == "EA8DLF IL18 37");
        r.Decodificaciones[0].Decibelios.Should().BeInRange(-30, -22);
    }

    [Fact]
    public void DosSenalesEnLaMismaVentanaSalenLasDos()
    {
        var azar = new Random(21);
        var a = BancoWspr.Ventana("EA8DLF IL18 37", 1450, 0.2, 0, -15, azar);
        var b = BancoWspr.Ventana("K1ABC FN42 30", 1540, -0.5, 0, -18, azar);
        for (var i = 0; i < a.Length; i++) a[i] += b[i];
        var r = new DecodificadorWspr().Decodificar(a, DateTimeOffset.UnixEpoch);
        r.Decodificaciones.Select(d => d.Texto).Should().BeEquivalentTo(["EA8DLF IL18 37", "K1ABC FN42 30"]);
    }

    [Fact]
    public void ElRuidoPuroNoSacaNada()
    {
        var (mensajes, _, _, _, _, _) = BancoWspr.RuidoPuro(new DecodificadorWspr(), ventanas: 6, semilla: 31);
        mensajes.Should().Be(0);
    }

    [Fact]
    public void ElModoGeneraYDecodificaPorElMarco()
    {
        var modo = new ModoWspr();
        modo.Modo.Should().Be(ModoDelModem.Wspr);
        modo.Periodo.Should().Be(TimeSpan.FromSeconds(120));
        modo.FrecuenciaDeAnalisis.Should().Be(12000);

        var audio = modo.Generar("EA8DLF IL18 37", 1500, 12000);
        audio.Length.Should().Be((int)Math.Round(162 * 8192.0));
        // El pico de fábrica es el conservador compartido por todos los modos (0,3), no el 0,5
        // de antes de que existiera el nivel de salida ajustable (ver NivelDeSalidaPruebas).
        ((double)audio.Max()).Should().BeApproximately(IModoDigital.AmplitudDeSalidaPorDefecto, 0.02);

        var ventana = new float[120 * 12000];
        audio.CopyTo(ventana, 12000);
        modo.Decodificar(ventana, DateTimeOffset.UnixEpoch, CancellationToken.None)
            .Should().ContainSingle(d => d.Texto == "EA8DLF IL18 37" && Math.Abs(d.DesfaseSegundos) < 0.1);

        var accion = () => modo.Generar("EA8DLF IL18 38", 1500, 12000);
        accion.Should().Throw<FormatException>();
    }

    [Fact]
    public void ElAudioGeneradoA48000TieneLaDuracionYElTonoQueToca()
    {
        var modo = new ModoWspr();
        var audio = modo.Generar("EA8DLF IL18 37", 1500, 48000);
        audio.Length.Should().Be(162 * 32768);
        // Se cuentan los cruces por cero de un trozo del medio para estimar la frecuencia.
        var cruces = 0;
        for (var i = 48000; i < 48000 * 11; i++)
            if ((audio[i - 1] < 0 && audio[i] >= 0)) cruces++;
        (cruces / 10.0).Should().BeApproximately(1500, 5);
    }
}
