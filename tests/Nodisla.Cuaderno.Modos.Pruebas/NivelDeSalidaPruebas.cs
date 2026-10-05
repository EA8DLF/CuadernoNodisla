using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Fst4;
using Nodisla.Cuaderno.Modos.Jt65;
using Nodisla.Cuaderno.Modos.Jt9;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Marco;
using Nodisla.Cuaderno.Modos.Msk144;
using Nodisla.Cuaderno.Modos.Pruebas.Tablas;
using Nodisla.Cuaderno.Modos.Q65;
using Nodisla.Cuaderno.Modos.Rtty;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// El nivel de salida es el «Pwr» de JTDX que le faltaba a este programa: antes la amplitud de
/// cada modo estaba escrita a fuego en 0,5 en el código, sin ningún mando para quien tuviera una
/// interfaz o una entrada de datos del equipo que saturase con ese nivel. Estas pruebas
/// comprueban, modo por modo, que <c>AmplitudDeSalida</c> cambia de verdad el pico del audio que
/// <c>Generar</c> entrega — lo que se mide aquí, en bruto, antes de llegar a ninguna tarjeta —, y
/// no solo que la propiedad se pueda escribir.
/// </summary>
/// <remarks>
/// Una grabación real de loopback de una transmisión de FT8 (herramienta aparte, con NAudio) ya
/// demostró que el audio que este programa manda al PC es limpio y con el pico fijo que toque: lo
/// que faltaba era poder bajar ese pico para no saturar la entrada del equipo en analógico, algo
/// que ninguna grabación hecha antes del hardware real puede ver. Estas pruebas verifican el
/// mecanismo (la amplitud sí cambia la señal generada); la prueba de que baja la saturación real
/// la hace Jose con su propia grabación de loopback, con el equipo de verdad.
/// </remarks>
public class NivelDeSalidaPruebas
{
    private const string Mensaje = "CQ EA8DLF IL18";
    private const string MensajeWspr = "EA8DLF IL18 37";
    private const int TonoHz = 1500;
    private const int FrecuenciaDeMuestreo = 48000;

    private static readonly TablasDelProtocolo TablasFt8 = TablasDelProtocolo.Cargar();

    private static float Pico(float[] senal) => senal.Length == 0 ? 0f : senal.Max(MathF.Abs);

    [Fact]
    public void ElValorDeFabricaEsMasConservadorQueElMaximoDeAntes()
    {
        // El 0,5 (-6 dBFS) que llevaba el programa escrito a fuego era el máximo razonable para
        // el audio en el ordenador, pero justo lo que podía saturar una entrada sensible del
        // equipo. El de fábrica tiene que quedar claramente por debajo, nunca a cero.
        IModoDigital.AmplitudDeSalidaPorDefecto.Should().BeLessThan(0.5).And.BeGreaterThan(0);
    }

    [Fact]
    public void Ft8CambiaElPicoDeLaSenalAlCambiarElNivel()
    {
        var modo = new ModoFt8(ModoDelModem.Ft8, TablasFt8, new CatalogoDeIndicativos());
        modo.AmplitudDeSalida.Should().Be(IModoDigital.AmplitudDeSalidaPorDefecto, "el nivel de fábrica tiene que ser el conservador de todos los modos");

        modo.AmplitudDeSalida = 0.2;
        var bajo = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        modo.AmplitudDeSalida = 0.8;
        var alto = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        bajo.Should().BeApproximately(0.2f, 0.01f);
        alto.Should().BeApproximately(0.8f, 0.01f);
    }

    [Fact]
    public void Fst4CambiaElPicoDeLaSenalAlCambiarElNivel()
    {
        var modo = new ModoFst4(TablasDelRepositorio.Fst4, esFst4w: false, periodoSegundos: 60);

        modo.AmplitudDeSalida = 0.25;
        var bajo = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        modo.AmplitudDeSalida = 0.9;
        var alto = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        bajo.Should().BeApproximately(0.25f, 0.01f);
        alto.Should().BeApproximately(0.9f, 0.01f);
    }

    [Fact]
    public void Jt65CambiaElPicoDeLaSenalAlCambiarElNivel()
    {
        var modo = new ModoJt65();

        modo.AmplitudDeSalida = 0.2;
        var bajo = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        modo.AmplitudDeSalida = 0.7;
        var alto = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        bajo.Should().BeApproximately(0.2f, 0.01f);
        alto.Should().BeApproximately(0.7f, 0.01f);
    }

    [Fact]
    public void Jt9CambiaElPicoDeLaSenalAlCambiarElNivel()
    {
        var modo = new ModoJt9();

        modo.AmplitudDeSalida = 0.3;
        var bajo = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        modo.AmplitudDeSalida = 1.0;
        var alto = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        bajo.Should().BeApproximately(0.3f, 0.01f);
        alto.Should().BeApproximately(1.0f, 0.01f);
    }

    [Fact]
    public void Msk144CambiaElPicoDeLaSenalAlCambiarElNivel()
    {
        var modo = new ModoMsk144(TablasDelRepositorio.Msk144, TablasDelRepositorio.Msk40);

        modo.AmplitudDeSalida = 0.2;
        var bajo = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        modo.AmplitudDeSalida = 0.8;
        var alto = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        bajo.Should().BeApproximately(0.2f, 0.01f);
        alto.Should().BeApproximately(0.8f, 0.01f);
    }

    [Fact]
    public void Q65CambiaElPicoDeLaSenalAlCambiarElNivel()
    {
        var modo = new ModoQ65(ParametrosDeQ65.De(60, SubmodoDeQ65.A), TablasDeQ65.Cargar());

        modo.AmplitudDeSalida = 0.2;
        var bajo = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        modo.AmplitudDeSalida = 0.8;
        var alto = Pico(modo.Generar(Mensaje, TonoHz, FrecuenciaDeMuestreo));

        bajo.Should().BeApproximately(0.2f, 0.01f);
        alto.Should().BeApproximately(0.8f, 0.01f);
    }

    [Fact]
    public void WsprCambiaElPicoDeLaSenalAlCambiarElNivel()
    {
        var modo = new ModoWspr();

        modo.AmplitudDeSalida = 0.25;
        var bajo = Pico(modo.Generar(MensajeWspr, TonoHz, FrecuenciaDeMuestreo));

        modo.AmplitudDeSalida = 0.9;
        var alto = Pico(modo.Generar(MensajeWspr, TonoHz, FrecuenciaDeMuestreo));

        bajo.Should().BeApproximately(0.25f, 0.01f);
        alto.Should().BeApproximately(0.9f, 0.01f);
    }

    [Fact]
    public void RttyCambiaElPicoDeLaSenalAlCambiarLaAmplitud()
    {
        var bajo = Pico(GeneradorRtty.Generar(Mensaje, TonoHz, 8000, amplitud: 0.2, silencioDelante: 0, silencioDetras: 0));
        var alto = Pico(GeneradorRtty.Generar(Mensaje, TonoHz, 8000, amplitud: 0.8, silencioDelante: 0, silencioDetras: 0));

        bajo.Should().BeApproximately(0.2f, 0.01f);
        alto.Should().BeApproximately(0.8f, 0.01f);
    }

    [Fact]
    public void OpcionesDelEmisorRttyArrancaConElNivelConservadorDeFabrica() =>
        new OpcionesDelEmisorRtty().Amplitud.Should().Be(IModoDigital.AmplitudDeSalidaPorDefecto);
}
