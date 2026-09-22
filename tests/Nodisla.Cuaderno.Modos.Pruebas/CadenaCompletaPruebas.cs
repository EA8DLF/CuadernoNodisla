using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// La prueba que cierra el circulo: se codifica un mensaje, se pone en el aire y se decodifica
/// con el propio decodificador.
/// </summary>
/// <remarks>
/// Es la prueba mas valiosa de todas porque no depende de nada de fuera: ni de una grabacion,
/// ni de que otro programa este de acuerdo, ni de una radio encendida. Si esto falla, el modem
/// esta roto, y si funciona hay un cimiento sobre el que medir todo lo demas.
/// </remarks>
public class CadenaCompletaPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.DePruebas();

    [Theory]
    [InlineData(ModoDelModem.Ft8, "CQ EA8DLF IL18")]
    [InlineData(ModoDelModem.Ft8, "EA1ABC EA8DLF IL18")]
    [InlineData(ModoDelModem.Ft8, "EA1ABC EA8DLF -12")]
    [InlineData(ModoDelModem.Ft8, "EA1ABC EA8DLF R-12")]
    [InlineData(ModoDelModem.Ft8, "EA1ABC EA8DLF RR73")]
    [InlineData(ModoDelModem.Ft8, "EA1ABC EA8DLF 73")]
    [InlineData(ModoDelModem.Ft8, "CQ DX EA8DLF IL18")]
    [InlineData(ModoDelModem.Ft4, "CQ EA8DLF IL18")]
    [InlineData(ModoDelModem.Ft4, "EA1ABC EA8DLF -05")]
    public void UnMensajeSaleYVuelveIgualSinRuido(ModoDelModem modo, string texto)
    {
        var decodificaciones = IdaYVuelta(modo, texto, decibelios: 30, desfase: 0, tono: 1500, semilla: 1);

        decodificaciones.Should().ContainSingle(d => d.Texto == texto,
            $"el mensaje «{texto}» tiene que volver tal cual por una ventana sin ruido");
    }

    [Theory]
    [InlineData(ModoDelModem.Ft8)]
    [InlineData(ModoDelModem.Ft4)]
    public void ElDesfaseYElTonoSeMidenBien(ModoDelModem modo)
    {
        const double Tono = 1234;
        const double Desfase = 0.4;
        var decodificaciones = IdaYVuelta(modo, "CQ EA8DLF IL18", decibelios: 20, Desfase, Tono, semilla: 7);

        var d = decodificaciones.Should().ContainSingle().Subject;
        d.TonoHz.Should().BeCloseTo((int)Tono, 4, "el tono se afina hasta unos pocos hercios");
        d.DesfaseSegundos.Should().BeApproximately(Desfase, 0.06, "el instante se afina hasta un cuarto de símbolo");
    }

    [Fact]
    public void VariasEstacionesALaVezSalenTodas()
    {
        var p = ParametrosDelModo.Ft8;
        var codificador = new Codificador(Tablas);
        var azar = new Random(42);
        const int Frecuencia = 48000;

        string[] mensajes = ["CQ EA8DLF IL18", "EA8DLF EA1ABC IN80", "EA1ABC EA8DLF -07", "CQ K1ABC FN42"];
        double[] tonos = [700, 1100, 1600, 2300];

        var ventana = new float[(int)(p.PeriodoSegundos * Frecuencia)];
        double potenciaDeUna = 0;
        for (var i = 0; i < mensajes.Length; i++)
        {
            codificador.TryCodificar(mensajes[i], ModoDelModem.Ft8, out var tonosDelMensaje, out _).Should().BeTrue();
            var senal = Modulador.Sintetizar(p, tonosDelMensaje, tonos[i], Frecuencia, 0.2);
            potenciaDeUna = GeneradorDeSenal.PotenciaMedia(senal);
            var comienzo = (int)(p.ComienzoNominalSegundos * Frecuencia);
            for (var k = 0; k < senal.Length; k++) ventana[comienzo + k] += senal[k];
        }
        GeneradorDeSenal.AnadirRuido(ventana, potenciaDeUna, 10, Frecuencia, azar);

        var decodificador = new Decodificador(Tablas);
        var resultado = decodificador.Decodificar(ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());

        resultado.Decodificaciones.Select(d => d.Texto).Should().BeEquivalentTo(mensajes,
            "cuatro estaciones en frecuencias distintas no se estorban");
    }

    [Fact]
    public void UnaVentanaDeRuidoPuroNoInventaNada()
    {
        // El caso que importa de verdad: si esto fallara, el cuaderno se llenaría de contactos
        // que nunca existieron y los diplomas quedarían contaminados para siempre.
        var p = ParametrosDelModo.Ft8;
        const int Frecuencia = 48000;
        var decodificador = new Decodificador(Tablas);
        var inventadas = 0;

        for (var v = 0; v < 20; v++)
        {
            var azar = new Random(1000 + v);
            var ventana = new float[(int)(p.PeriodoSegundos * Frecuencia)];
            GeneradorDeSenal.AnadirRuido(ventana, 0.05, 0, Frecuencia, azar);
            inventadas += decodificador.Decodificar(ventana, Frecuencia, ModoDelModem.Ft8, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos())
                .Decodificaciones.Count;
        }

        inventadas.Should().Be(0, "veinte ventanas de ruido puro no pueden producir ni un solo mensaje");
    }

    internal static IReadOnlyList<DecodificacionPropia> IdaYVuelta(
        ModoDelModem modo, string texto, double decibelios, double desfase, double tono, int semilla)
    {
        var p = ParametrosDelModo.De(modo);
        var codificador = new Codificador(Tablas);
        codificador.TryCodificar(texto, modo, out var tonos, out var motivo).Should().BeTrue(motivo);

        const int Frecuencia = 48000;
        var ventana = GeneradorDeSenal.Ventana(p, tonos, tono, desfase, decibelios, Frecuencia, new Random(semilla));

        var decodificador = new Decodificador(Tablas);
        return decodificador.Decodificar(ventana, Frecuencia, modo, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos()).Decodificaciones;
    }
}
