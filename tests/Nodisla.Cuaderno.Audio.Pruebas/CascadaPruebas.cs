using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Cascada;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// La cascada, comprobada con senales inventadas y sin tocar ninguna tarjeta de sonido.
/// </summary>
/// <remarks>
/// La prueba que de verdad importa es la primera: si una senoide de frecuencia conocida no cae
/// en su casilla, la cascada miente y todo lo que se construya encima —ver donde esta una
/// estacion, elegir el tono de transmision— miente con ella.
/// </remarks>
public class CascadaPruebas
{
    private const int Frecuencia = 48000;
    private const int Tamano = 4096;

    private static OpcionesDeCascada Opciones() => new()
    {
        TamanoDeTransformada = Tamano,
        Salto = Tamano / 2,
        FrecuenciaMaximaHz = 4000,
    };

    private static BloqueDeAudio Senoide(double hz, int muestras, double amplitud = 1.0)
    {
        var datos = new float[muestras];
        for (var i = 0; i < muestras; i++)
        {
            datos[i] = (float)(amplitud * Math.Sin(2.0 * Math.PI * hz * i / Frecuencia));
        }

        return new BloqueDeAudio(datos, Frecuencia, new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void UnaSenoideCaeEnSuCasilla()
    {
        // 1500 Hz cae justo en el centro de la casilla 128 con estos ajustes.
        var calculadora = new CalculadoraDeCascada(Opciones());
        var columnas = calculadora.Procesar(Senoide(1500.0, Tamano * 2));

        columnas.Should().NotBeEmpty();

        var magnitudes = columnas[0].Magnitudes.Span;
        var casillaMasAlta = 0;
        for (var i = 1; i < magnitudes.Length; i++)
        {
            if (magnitudes[i] > magnitudes[casillaMasAlta])
            {
                casillaMasAlta = i;
            }
        }

        calculadora.HzPorCasilla.Should().BeApproximately(Frecuencia / (double)Tamano, 1e-9);
        casillaMasAlta.Should().Be(128);
        (casillaMasAlta * calculadora.HzPorCasilla).Should().BeApproximately(1500.0, 0.001);
    }

    [Fact]
    public void UnaSenoideDeAmplitudUnoSaleACeroDecibelios()
    {
        var calculadora = new CalculadoraDeCascada(Opciones());
        var columnas = calculadora.Procesar(Senoide(1500.0, Tamano * 2));

        var magnitudes = columnas[0].Magnitudes.Span;
        magnitudes[128].Should().BeApproximately(0f, 0.1f);

        // Y lo que hay lejos del tono esta muy por debajo: si no, la ventana no esta haciendo
        // su trabajo y las senales debiles quedarian tapadas.
        magnitudes[200].Should().BeLessThan(-60f);
    }

    [Fact]
    public void UnaSenoideQueNoCaeEnElCentroSeQuedaEnLaCasillaDeAlLado()
    {
        var calculadora = new CalculadoraDeCascada(Opciones());
        var columnas = calculadora.Procesar(Senoide(1000.0, Tamano * 2));

        var magnitudes = columnas[0].Magnitudes.Span;
        var casillaMasAlta = 0;
        for (var i = 1; i < magnitudes.Length; i++)
        {
            if (magnitudes[i] > magnitudes[casillaMasAlta])
            {
                casillaMasAlta = i;
            }
        }

        var esperada = 1000.0 / calculadora.HzPorCasilla;
        Math.Abs(casillaMasAlta - esperada).Should().BeLessThan(1.0);
    }

    [Fact]
    public void LasColumnasVanFechadasSegunDondeEmpiezaCadaTramo()
    {
        var calculadora = new CalculadoraDeCascada(Opciones());
        var bloque = Senoide(1500.0, Tamano * 2);
        var columnas = calculadora.Procesar(bloque);

        // Con salto de medio tramo, de 8192 muestras salen tres tramos: en 0, en 2048 y en 4096.
        columnas.Should().HaveCount(3);
        columnas[0].InstanteUtc.Should().Be(bloque.InstanteUtc);

        var esperado = bloque.InstanteUtc + TimeSpan.FromSeconds((Tamano / 2.0) / Frecuencia);
        columnas[1].InstanteUtc.Should().BeCloseTo(esperado, TimeSpan.FromMicroseconds(1));
    }

    [Fact]
    public void SoloSeEntreganCasillasHastaLaFrecuenciaMaxima()
    {
        var calculadora = new CalculadoraDeCascada(Opciones());
        var columnas = calculadora.Procesar(Senoide(1500.0, Tamano));

        var ultima = (columnas[0].Magnitudes.Length - 1) * calculadora.HzPorCasilla;
        ultima.Should().BeLessThanOrEqualTo(4000.0 + calculadora.HzPorCasilla);
        columnas[0].Magnitudes.Length.Should().BeLessThan((Tamano / 2) + 1);
    }

    [Fact]
    public void LaTransformadaSoloAdmitePotenciasDeDos()
    {
        var real = new float[100];
        var imaginaria = new float[100];

        var fallo = () => TransformadaRapida.Transformar(real, imaginaria);
        fallo.Should().Throw<ArgumentException>();
    }
}
