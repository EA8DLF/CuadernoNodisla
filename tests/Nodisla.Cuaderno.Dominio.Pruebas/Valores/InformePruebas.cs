using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Valores;

/// <summary>Pruebas del informe de senal: RS, RST, decibelios y lo que venga.</summary>
public sealed class InformePruebas
{
    [Theory]
    [InlineData("59")]
    [InlineData("55")]
    [InlineData("11")]
    [InlineData("39")]
    public void Un_informe_de_fonia_se_lee_como_RS(string texto)
    {
        var i = Informe.Parse(texto);

        i.Forma.Should().Be(FormaDeInforme.Rs);
        i.Texto.Should().Be(texto);
        i.Decibelios.Should().BeNull();
    }

    [Theory]
    [InlineData("599")]
    [InlineData("579")]
    [InlineData("111")]
    [InlineData("539")]
    public void Un_informe_de_telegrafia_se_lee_como_RST(string texto)
    {
        var i = Informe.Parse(texto);

        i.Forma.Should().Be(FormaDeInforme.Rst);
        i.Texto.Should().Be(texto);
    }

    [Theory]
    [InlineData("-15", -15)]
    [InlineData("+03", 3)]
    [InlineData("-01", -1)]
    [InlineData("+00", 0)]
    [InlineData("-24", -24)]
    [InlineData("+60", 60)]
    [InlineData("-40", -40)]
    public void Un_informe_de_tarjeta_de_sonido_se_lee_en_decibelios(string texto, int db)
    {
        var i = Informe.Parse(texto);

        i.Forma.Should().Be(FormaDeInforme.Decibelios);
        i.Texto.Should().Be(texto);
        i.Decibelios.Should().Be(db);
    }

    [Theory]
    [InlineData("69")]        // legibilidad 6: no existe
    [InlineData("50")]        // senal 0: no existe
    [InlineData("590")]       // tono 0: no existe
    [InlineData("5999")]      // cuatro digitos
    [InlineData("basura")]
    [InlineData("-41")]       // fuera del margen razonable de SNR
    [InlineData("+61")]       // fuera del margen razonable de SNR
    [InlineData("5NN")]       // los ceros por N de los concursos
    public void Lo_que_no_encaja_se_guarda_tal_cual(string texto)
    {
        var i = Informe.Parse(texto);

        i.Forma.Should().Be(FormaDeInforme.Libre);
        i.Texto.Should().Be(texto);
        i.Decibelios.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_texto_no_hay_informe(string? texto)
    {
        var i = Informe.Parse(texto);

        i.EsVacio.Should().BeTrue();
        i.Forma.Should().Be(FormaDeInforme.Ninguno);
    }

    [Fact]
    public void El_informe_se_recorta_de_espacios()
    {
        Informe.Parse("  599  ").Texto.Should().Be("599");
    }

    [Theory]
    [InlineData(0, "+00")]
    [InlineData(-15, "-15")]
    [InlineData(3, "+03")]
    [InlineData(-1, "-01")]
    public void El_informe_en_decibelios_se_escribe_con_signo_y_dos_cifras(int db, string esperado)
    {
        var i = Informe.DesdeDecibelios(db);

        i.Texto.Should().Be(esperado);
        i.Forma.Should().Be(FormaDeInforme.Decibelios);
        i.Decibelios.Should().Be(db);
    }

    [Theory]
    [InlineData("SSB", "59")]
    [InlineData("FM", "59")]
    [InlineData("AM", "59")]
    [InlineData("CW", "599")]
    [InlineData("RTTY", "599")]
    [InlineData("PSK31", "599")]
    public void Cada_modo_trae_su_informe_por_omision(string modo, string esperado)
    {
        Informe.PorOmisionPara(Modo.Parse(modo)).Texto.Should().Be(esperado);
    }

    [Theory]
    [InlineData("FT4")]
    [InlineData("FT8")]
    [InlineData("JT65")]
    [InlineData("WSPR")]
    public void Los_modos_que_informan_en_decibelios_no_tienen_informe_por_omision(string modo)
    {
        // El SNR lo aporta el decodificador; poner un 59 de oficio seria mentir.
        Informe.PorOmisionPara(Modo.Parse(modo)).EsVacio.Should().BeTrue();
    }

    [Fact]
    public void Un_modo_vacio_no_tiene_informe_por_omision()
    {
        Informe.PorOmisionPara(Modo.Vacio).EsVacio.Should().BeTrue();
    }

    [Fact]
    public void El_informe_se_convierte_a_texto_solo()
    {
        var i = Informe.Parse("599");

        string texto = i;
        texto.Should().Be("599");
        i.ToString().Should().Be("599");
    }

    [Fact]
    public void Dos_informes_con_el_mismo_texto_son_iguales()
    {
        Informe.Parse("59").Should().Be(Informe.Parse("59"));
        Informe.Parse("59").Should().NotBe(Informe.Parse("599"));
    }
}
