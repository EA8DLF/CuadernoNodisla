using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Valores;

/// <summary>Pruebas del modo ADIF y del par modo/submodo.</summary>
public sealed class ModoPruebas
{
    [Theory]
    [InlineData("SSB")]
    [InlineData("CW")]
    [InlineData("FM")]
    [InlineData("AM")]
    [InlineData("RTTY")]
    [InlineData("MFSK")]
    [InlineData("PSK")]
    [InlineData("DIGITALVOICE")]
    public void Un_modo_principal_se_acepta_tal_cual(string nombre)
    {
        Modo.TryParse(nombre, null, out var m).Should().BeTrue();
        m.Principal.Should().Be(nombre);
        m.Submodo.Should().BeNull();
        m.NombreUsual.Should().Be(nombre);
    }

    [Theory]
    [InlineData("ssb", "SSB")]
    [InlineData("  cw  ", "CW")]
    public void El_modo_se_normaliza_a_mayusculas(string entrada, string esperado)
    {
        Modo.Parse(entrada).Principal.Should().Be(esperado);
    }

    [Theory]
    [InlineData("USB", "SSB", "USB")]
    [InlineData("LSB", "SSB", "LSB")]
    [InlineData("FT4", "MFSK", "FT4")]
    [InlineData("JS8", "MFSK", "JS8")]
    [InlineData("Q65", "MFSK", "Q65")]
    [InlineData("PSK31", "PSK", "PSK31")]
    [InlineData("JT65B", "JT65", "JT65B")]
    [InlineData("DMR", "DIGITALVOICE", "DMR")]
    [InlineData("C4FM", "DIGITALVOICE", "C4FM")]
    public void Un_submodo_solo_se_resuelve_a_su_modo_principal(string escrito, string principal, string submodo)
    {
        var m = Modo.Parse(escrito);

        m.Principal.Should().Be(principal);
        m.Submodo.Should().Be(submodo);
        m.NombreUsual.Should().Be(submodo);
        m.ToString().Should().Be($"{principal}/{submodo}");
    }

    [Fact]
    public void El_par_modo_y_submodo_se_acepta_separado()
    {
        var m = Modo.Parse("MFSK", "FT4");

        m.Principal.Should().Be("MFSK");
        m.Submodo.Should().Be("FT4");
    }

    [Fact]
    public void El_par_escrito_con_barra_en_un_solo_campo_tambien_vale()
    {
        var m = Modo.Parse("MFSK/FT4");

        m.Principal.Should().Be("MFSK");
        m.Submodo.Should().Be("FT4");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("TELEPATIA", null)]
    [InlineData(null, "TELEPATIA")]
    public void Se_rechaza_lo_que_no_esta_en_el_catalogo(string? modo, string? submodo)
    {
        Modo.TryParse(modo, submodo, out var m).Should().BeFalse();
        m.EsVacio.Should().BeTrue();
    }

    [Fact]
    public void Parse_se_queja_cuando_el_modo_no_existe()
    {
        var leer = () => Modo.Parse("TELEPATIA");

        leer.Should().Throw<FormatException>();
    }

    [Fact]
    public void Crudo_acepta_un_modo_que_ADIF_todavia_no_recoge()
    {
        // Perder un QSO al importar por un modo nuevo seria peor que aceptarlo.
        var m = Modo.Crudo("nuevomodo", "variante");

        m.Principal.Should().Be("NUEVOMODO");
        m.Submodo.Should().Be("VARIANTE");
        m.EsVacio.Should().BeFalse();
    }

    [Fact]
    public void El_modo_vacio_se_comporta_como_vacio()
    {
        Modo.Vacio.EsVacio.Should().BeTrue();
        Modo.Vacio.Principal.Should().BeNullOrEmpty();
        Modo.Vacio.Submodo.Should().BeNull();
    }

    [Fact]
    public void El_catalogo_conoce_los_submodos_de_cada_modo()
    {
        Modo.ModosPrincipales.Should().Contain(["SSB", "CW", "MFSK", "PSK", "RTTY"]);
        Modo.SubmodosDe("SSB").Should().BeEquivalentTo(["LSB", "USB"]);
        Modo.SubmodosDe("FM").Should().BeEmpty();
        Modo.SubmodosDe("TELEPATIA").Should().BeEmpty();
    }

    [Theory]
    [InlineData("FT4", true)]
    [InlineData("FT8", true)]
    [InlineData("JT65", true)]
    [InlineData("WSPR", true)]
    [InlineData("Q65", true)]
    [InlineData("SSB", false)]
    [InlineData("CW", false)]
    [InlineData("FM", false)]
    [InlineData("RTTY", false)]
    public void Se_sabe_que_modos_informan_en_decibelios(string escrito, bool enDecibelios)
    {
        Modo.Parse(escrito).UsaInformeEnDecibelios.Should().Be(enDecibelios);
    }

    [Fact]
    public void Dos_modos_con_el_mismo_par_son_iguales()
    {
        Modo.Parse("FT4").Should().Be(Modo.Parse("MFSK", "FT4"));
    }

    [Fact]
    public void FT8_es_modo_principal_en_ADIF()
    {
        // La tabla Mode de ADIF 3.1.5 declara FT8 como modo, no como submodo de nada.
        var m = Modo.Parse("FT8");

        m.Principal.Should().Be("FT8");
        m.Submodo.Should().BeNull();
        m.NombreUsual.Should().Be("FT8");
        m.ToString().Should().Be("FT8");
        Modo.ModosPrincipales.Should().Contain("FT8");
    }

    [Fact]
    public void FT4_en_cambio_es_submodo_de_MFSK()
    {
        // El parecido de los nombres enganna: FT4 y FT8 no estan al mismo nivel en ADIF.
        var m = Modo.Parse("FT4");

        m.Principal.Should().Be("MFSK");
        m.Submodo.Should().Be("FT4");
        m.ToString().Should().Be("MFSK/FT4");
        Modo.SubmodosDe("MFSK").Should().Contain("FT4");
        Modo.SubmodosDe("MFSK").Should().NotContain("FT8");
    }
}
