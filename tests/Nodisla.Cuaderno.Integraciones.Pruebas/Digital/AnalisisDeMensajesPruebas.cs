using FluentAssertions;
using Nodisla.Cuaderno.Integraciones.Digital;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Digital;

/// <summary>
/// Los mensajes de FT8 de verdad, con sus trampas: llamadas dirigidas, indicativos compuestos
/// entre angulos, indicativos que no caben y se sustituyen por puntos, y <c>RR73</c>, que tiene
/// la forma exacta de un localizador valido.
/// </summary>
public class AnalisisDeMensajesPruebas
{
    [Theory]
    [InlineData("CQ EA8DLF IL18", "EA8DLF", "IL18")]
    [InlineData("CQ DX EA8DLF IL18", "EA8DLF", "IL18")]
    [InlineData("CQ POTA K1ABC FN42", "K1ABC", "FN42")]
    [InlineData("CQ TEST W9XYZ EM48", "W9XYZ", "EM48")]
    [InlineData("CQ 135 W9XYZ EM48", "W9XYZ", "EM48")]
    [InlineData("CQ NA EA8DLF", "EA8DLF", "")]
    [InlineData("CQ <EA8DLF> IL18", "EA8DLF", "IL18")]
    [InlineData("CQ EA8DLF/P IL18", "EA8DLF/P", "IL18")]
    public void Una_llamada_general_da_el_llamante_y_su_localizador(
        string texto, string llamante, string locator)
    {
        var analisis = AnalizadorMensajeDigital.Analizar(texto);

        analisis.EsCq.Should().BeTrue();
        analisis.Llamante.Valor.Should().Be(llamante);
        analisis.Llamado.EsVacio.Should().BeTrue("CQ no es un indicativo");
        ComprobarLocalizador(analisis, locator);
    }

    private static void ComprobarLocalizador(AnalisisDeMensaje analisis, string esperado)
    {
        if (esperado.Length == 0) analisis.Locator.EsVacio.Should().BeTrue();
        else analisis.Locator.Valor.Should().Be(esperado);
    }

    [Fact]
    public void Una_llamada_general_sin_indicativo_sigue_siendo_una_llamada_general()
    {
        var analisis = AnalizadorMensajeDigital.Analizar("CQ DX");

        analisis.EsCq.Should().BeTrue();
        analisis.Llamante.EsVacio.Should().BeTrue();
    }

    [Theory]
    [InlineData("K1ABC EA8DLF IL18", "K1ABC", "EA8DLF", "IL18")]
    [InlineData("EA8DLF K1ABC -15", "EA8DLF", "K1ABC", "")]
    [InlineData("EA8DLF K1ABC R-15", "EA8DLF", "K1ABC", "")]
    [InlineData("EA8DLF K1ABC RR73", "EA8DLF", "K1ABC", "")]
    [InlineData("EA8DLF K1ABC RRR", "EA8DLF", "K1ABC", "")]
    [InlineData("EA8DLF K1ABC 73", "EA8DLF", "K1ABC", "")]
    [InlineData("K1ABC <EA8DLF/P> RR73", "K1ABC", "EA8DLF/P", "")]
    [InlineData("<K1ABC> EA8DLF IL18", "K1ABC", "EA8DLF", "IL18")]
    [InlineData("K1ABC EA8DLF R IL18", "K1ABC", "EA8DLF", "IL18")]
    public void Un_mensaje_dirigido_da_a_quien_se_llama_y_quien_llama(
        string texto, string llamado, string llamante, string locator)
    {
        var analisis = AnalizadorMensajeDigital.Analizar(texto);

        analisis.EsCq.Should().BeFalse();
        analisis.Llamado.Valor.Should().Be(llamado);
        analisis.Llamante.Valor.Should().Be(llamante);
        ComprobarLocalizador(analisis, locator);
    }

    [Fact]
    public void Rr73_no_es_un_localizador_aunque_lo_parezca()
    {
        // Pasa la validacion de Maidenhead: dos letras hasta la R y dos digitos.
        Nodisla.Cuaderno.Dominio.Valores.Locator.TryParse("RR73", out _).Should().BeTrue();

        AnalizadorMensajeDigital.Analizar("EA8DLF K1ABC RR73").Locator.EsVacio.Should().BeTrue();
    }

    [Fact]
    public void El_indicativo_que_no_cabe_llega_como_puntos_y_no_se_confunde_con_uno()
    {
        var analisis = AnalizadorMensajeDigital.Analizar("<...> EA8DLF RR73");

        analisis.Llamado.EsVacio.Should().BeTrue();
        analisis.Llamante.Valor.Should().Be("EA8DLF");
    }

    [Fact]
    public void El_mensaje_partido_por_punto_y_coma_se_lee_por_la_segunda_mitad()
    {
        // Es la forma de los mensajes con indicativo no estandar: quien transmite va detras.
        var analisis = AnalizadorMensajeDigital.Analizar("K1ABC RR73; W9XYZ <PJ4/K1ABC> -11");

        analisis.Llamado.Valor.Should().Be("W9XYZ");
        analisis.Llamante.Valor.Should().Be("PJ4/K1ABC");
    }

    [Theory]
    [InlineData("TNX BOB 73 GL")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("73")]
    public void El_texto_libre_no_inventa_indicativos(string texto)
    {
        var analisis = AnalizadorMensajeDigital.Analizar(texto);

        analisis.Llamante.EsVacio.Should().BeTrue();
        analisis.Llamado.EsVacio.Should().BeTrue();
        analisis.EsCq.Should().BeFalse();
    }

    [Fact]
    public void Un_mensaje_nulo_no_lanza()
    {
        var accion = () => AnalizadorMensajeDigital.Analizar(null);

        accion.Should().NotThrow();
    }
}
