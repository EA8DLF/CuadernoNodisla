using System.Globalization;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Reloj;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// El veredicto del reloj en palabras.
/// </summary>
/// <remarks>
/// Lo que se prueba aqui no es aritmetica, es lo que va a leer el operador. Por eso se
/// comprueba el texto: que cuando se transmite fuera de ventana <b>se diga con esas palabras</b>
/// y no con un numero, porque es lo unico que le hace entender que el problema ya no es suyo
/// sino de los demas.
/// </remarks>
public class VeredictoDelRelojPruebas
{
    private static readonly DateTimeOffset Cuando = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private static OpcionesDelReloj Umbrales() => new();

    private static EstadoDelReloj Con(double desvioMs, bool fiable = true) =>
        VeredictoDelReloj.Componer(
            new DesvioDelReloj(desvioMs, "servidor de prueba", Cuando, fiable),
            Umbrales());

    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(-200)]
    public void PorDebajoDeDoscientosMilisegundosNoHayNadaQueHacer(double desvioMs)
    {
        var estado = Con(desvioMs);

        estado.Calidad.Should().Be(CalidadDelReloj.Bien);
        estado.HayQueHacerAlgo.Should().BeFalse();
        estado.Veredicto.Should().Contain("en hora");

        // Vacio a proposito: no hay nada que hacer, y decirlo con una frase solo invita a
        // buscarle tres pies al gato.
        estado.Consejo.Should().BeEmpty();
    }

    [Theory]
    [InlineData(201)]
    [InlineData(-480)]
    [InlineData(999)]
    public void EntreDoscientosMilisegundosYUnSegundoSeDecodificaPeor(double desvioMs)
    {
        var estado = Con(desvioMs);

        estado.Calidad.Should().Be(CalidadDelReloj.Regular);
        estado.HayQueHacerAlgo.Should().BeTrue();
        estado.Consejo.Should().Contain("decodificar peor");
        estado.Consejo.Should().Contain("conviene sincronizar");

        // Todavia no se molesta a nadie: eso no se dice hasta que es verdad.
        estado.Consejo.Should().NotContain("fuera de ventana");
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(-1500)]
    [InlineData(4000)]
    public void PasadoElSegundoSeTransmiteFueraDeVentana(double desvioMs)
    {
        var estado = Con(desvioMs);

        estado.Calidad.Should().Be(CalidadDelReloj.FueraDeVentana);
        estado.HayQueHacerAlgo.Should().BeTrue();
        estado.Consejo.Should().Contain("fuera de ventana");
        estado.Consejo.Should().Contain("molestando a los demás");
        estado.Consejo.Should().Contain("antes de transmitir");
    }

    [Fact]
    public void ElDesvioDelOperadorEraDeLosQueHayQueArreglar()
    {
        // La medida de verdad que se sacó de su ordenador: 870 ms atrasado.
        var estado = Con(-870);

        estado.Calidad.Should().Be(CalidadDelReloj.Regular);
        estado.Veredicto.Should().Contain("atrasado");
        estado.DesvioParaMostrar.Should().Be("-870 ms");
    }

    [Fact]
    public void SinMedirSeDiceQueNoSeSabe()
    {
        var estado = Con(0, fiable: false);

        estado.Calidad.Should().Be(CalidadDelReloj.SinMedir);
        estado.EsFiable.Should().BeFalse();
        estado.Veredicto.Should().Contain("sin comprobar");
        estado.Consejo.Should().Contain("no se sabe si el módem está en hora");
        estado.DesvioParaMostrar.Should().Be("sin medir");
    }

    [Fact]
    public void ElDesvioGrandeSeEnsenaEnSegundos()
    {
        // La coma de los decimales la pone el idioma del ordenador, no nosotros.
        var coma = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;

        Con(-2500).DesvioParaMostrar.Should().Be($"-2{coma}50 s");
        Con(350).DesvioParaMostrar.Should().Be("+350 ms");
    }

    [Fact]
    public void LosUmbralesSePuedenCambiar()
    {
        var estrictos = new OpcionesDelReloj { MargenBuenoMs = 50, MargenFueraDeVentanaMs = 300 };
        var desvio = new DesvioDelReloj(100, "servidor de prueba", Cuando, EsFiable: true);

        VeredictoDelReloj.Componer(desvio, estrictos).Calidad.Should().Be(CalidadDelReloj.Regular);
        VeredictoDelReloj.Componer(desvio, Umbrales()).Calidad.Should().Be(CalidadDelReloj.Bien);
    }

    [Fact]
    public void ElMargenDeFueraDeVentanaTieneQueSerMayorQueElBueno()
    {
        var fallo = () => new OpcionesDelReloj { MargenFueraDeVentanaMs = 100 };
        fallo.Should().Throw<ArgumentOutOfRangeException>();
    }
}
