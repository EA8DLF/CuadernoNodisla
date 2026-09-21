using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Valores;

/// <summary>Pruebas del objeto de valor <see cref="Indicativo"/>.</summary>
public sealed class IndicativoPruebas
{
    [Theory]
    [InlineData("ea8dlf", "EA8DLF")]
    [InlineData("  EA8DLF  ", "EA8DLF")]
    [InlineData("ea8dlf/p", "EA8DLF/P")]
    [InlineData("EA8DLF\\P", "EA8DLF/P")]
    [InlineData("EA8DLF-P", "EA8DLF/P")]
    [InlineData("E A 8 D L F", "EA8DLF")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Normalizar_deja_el_texto_en_mayusculas_sin_espacios(string? entrada, string esperado)
    {
        Indicativo.Normalizar(entrada).Should().Be(esperado);
    }

    [Theory]
    [InlineData("EA8DLF")]
    [InlineData("W1AW")]
    [InlineData("9A1A")]
    [InlineData("EA8DLF/P")]
    [InlineData("EA8/DL1ABC/P")]
    [InlineData("3DA0AB")]
    public void Un_indicativo_bien_formado_se_acepta(string texto)
    {
        Indicativo.TryParse(texto, out var i).Should().BeTrue();
        i.Valor.Should().Be(texto);
        i.EsVacio.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]          // nada
    [InlineData("")]            // nada
    [InlineData("EA")]          // demasiado corto
    [InlineData("ABCDEF")]      // sin digitos
    [InlineData("12345")]       // sin letras
    [InlineData("EA8DLF!")]     // caracter no permitido
    [InlineData("EA8 DLF#")]    // caracter no permitido
    [InlineData("EA8DLFEA8DLFEA8DLFEA8DLFEA8")]  // demasiado largo
    public void Un_indicativo_mal_formado_se_rechaza(string? texto)
    {
        Indicativo.TryParse(texto, out var i).Should().BeFalse();
        i.EsVacio.Should().BeTrue();
    }

    [Fact]
    public void Parse_se_queja_cuando_el_indicativo_no_vale()
    {
        var leer = () => Indicativo.Parse("??");

        leer.Should().Throw<FormatException>();
    }

    [Fact]
    public void Crudo_acepta_lo_que_venga_del_ADIF_ajeno()
    {
        // Al importar no se puede perder un QSO por un indicativo raro.
        var i = Indicativo.Crudo("5017josES9z");

        i.Valor.Should().Be("5017JOSES9Z");
        Indicativo.TryParse("5017JOSES9Z", out _).Should().BeTrue();
    }

    [Fact]
    public void El_indicativo_vacio_se_comporta_como_vacio()
    {
        Indicativo.Vacio.EsVacio.Should().BeTrue();
        Indicativo.Vacio.Valor.Should().BeNullOrEmpty();
        Indicativo.Vacio.Base.Should().BeEmpty();
        Indicativo.Vacio.ToString().Should().BeNullOrEmpty();
    }

    [Theory]
    [InlineData("EA8DLF", "EA8DLF")]
    [InlineData("EA8DLF/P", "EA8DLF")]
    [InlineData("EA8DLF/QRP", "EA8DLF")]
    [InlineData("EA8DLF/MM", "EA8DLF")]
    [InlineData("P/EA8DLF", "EA8DLF")]
    [InlineData("EA8/DL1ABC", "DL1ABC")]
    [InlineData("EA8/DL1ABC/P", "DL1ABC")]
    public void Base_se_queda_con_el_fragmento_significativo(string texto, string esperado)
    {
        Indicativo.Crudo(texto).Base.Should().Be(esperado);
    }

    [Theory]
    [InlineData("P", true)]
    [InlineData("M", true)]
    [InlineData("MM", true)]
    [InlineData("AM", true)]
    [InlineData("QRP", true)]
    [InlineData("LH", true)]
    [InlineData("EA8", false)]
    [InlineData("DL1ABC", false)]
    [InlineData("1", false)]
    public void Los_sufijos_de_operacion_se_reconocen(string parte, bool esSufijo)
    {
        Indicativo.EsSufijoDeOperacion(parte).Should().Be(esSufijo);
    }

    [Fact]
    public void Dos_indicativos_con_el_mismo_texto_son_iguales()
    {
        var a = Indicativo.Parse("ea8dlf");
        var b = Indicativo.Parse("EA8DLF");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void El_indicativo_se_convierte_a_texto_solo()
    {
        var i = Indicativo.Parse("EA8DLF/P");

        string texto = i;
        texto.Should().Be("EA8DLF/P");
        i.ToString().Should().Be("EA8DLF/P");
    }

    [Fact]
    public void Normalizar_un_texto_enorme_no_deberia_tumbar_el_proceso()
    {
        // Normalizar es publico y lo llama la importacion de ADIF ajeno, donde un campo
        // puede venir con cualquier tamano: no puede reservar en la pila a ciegas.
        var enorme = new string('a', 4_000_000);

        var normalizar = () => Indicativo.Normalizar(enorme);

        normalizar.Should().NotThrow();
        Indicativo.Normalizar(enorme).Should().HaveLength(4_000_000);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(256)]   // ultimo tamano que cabe en la pila
    [InlineData(257)]   // primero que pasa al monton
    [InlineData(258)]
    [InlineData(1024)]
    public void Normalizar_cruza_el_umbral_de_la_pila_sin_cambiar_de_resultado(int longitud)
    {
        var texto = new string('a', longitud);

        var normalizado = Indicativo.Normalizar(texto);

        normalizado.Should().HaveLength(longitud);
        normalizado.Should().Be(new string('A', longitud));
    }

    [Fact]
    public void Normalizar_un_texto_largo_con_espacios_devuelve_solo_lo_util()
    {
        // El buffer prestado es mas grande que el texto: hay que quedarse con lo escrito.
        var texto = new string(' ', 400) + "ea8dlf-p" + new string(' ', 400);

        Indicativo.Normalizar(texto).Should().Be("EA8DLF/P");
    }
}
