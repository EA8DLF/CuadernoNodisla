using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// Indicativos que no caben en el formato corriente (HB10GBT, PJ4/K1ABC…): resumenes de 22, 12
/// y 10 bits, mensajes de tipo 1 con resumen y de tipo 4 con el indicativo en claro.
/// </summary>
/// <remarks>
/// Los vectores de referencia (resumenes y los 77 bits) se han sacado compilando ft8_lib
/// (github.com/kgoba/ft8_lib, licencia MIT), que interopera en el aire con WSJT-X. Si el
/// resumen no diera exactamente lo mismo, nadie resolveria nuestros indicativos ni nosotros
/// los suyos.
/// </remarks>
public class IndicativosNoEstandarPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();

    [Theory]
    [InlineData("HB10GBT", 1844237, 1801, 450)]
    [InlineData("EA8DLF", 2784076, 2718, 679)]
    [InlineData("EA1ABC", 527469, 515, 128)]
    [InlineData("K1ABC", 2920267, 2851, 712)]
    [InlineData("W9XYZ", 3982604, 3889, 972)]
    [InlineData("PJ4/K1ABC", 1420834, 1387, 346)]
    [InlineData("EA8/G5LSI", 871857, 851, 212)]
    public void LosResumenesCoincidenConLosDeReferencia(string indicativo, int h22, int h12, int h10)
    {
        CatalogoDeIndicativos.Resumir(indicativo, 22).Should().Be(h22);
        CatalogoDeIndicativos.Resumir(indicativo, 12).Should().Be(h12);
        CatalogoDeIndicativos.Resumir(indicativo, 10).Should().Be(h10);
    }

    [Theory]
    // Tipo 1 con un indicativo resumido en 22 bits.
    [InlineData("<HB10GBT> EA8DLF IL27", "00000011101110100000111101010011011010101110011011010001000011110010100111001")]
    [InlineData("HB10GBT EA8DLF IL27", "00000011101110100000111101010011011010101110011011010001000011110010100111001")]
    [InlineData("EA8DLF <HB10GBT> -14", "01101101010111001101101000100000000111011101000001111010100111111010100101001")]
    [InlineData("<HB10GBT> EA8DLF R-14", "00000011101110100000111101010011011010101110011011010001001111111010100101001")]
    [InlineData("<HB10GBT> EA8DLF RR73", "00000011101110100000111101010011011010101110011011010001000111111010010011001")]
    [InlineData("<HB10GBT> EA8DLF RRR", "00000011101110100000111101010011011010101110011011010001000111111010010010001")]
    [InlineData("<HB10GBT> EA8DLF 73", "00000011101110100000111101010011011010101110011011010001000111111010010100001")]
    [InlineData("EA8DLF <HB10GBT> 73", "01101101010111001101101000100000000111011101000001111010100111111010010100001")]
    // Tipo 4: el raro en claro (58 bits), el otro resumido en 12.
    [InlineData("<EA8DLF> HB10GBT", "10101001111000000000000000000000001100110101110100111110011111010000100000100")]
    [InlineData("<EA8DLF> HB10GBT RRR", "10101001111000000000000000000000001100110101110100111110011111010000100010100")]
    [InlineData("<EA8DLF> HB10GBT RR73", "10101001111000000000000000000000001100110101110100111110011111010000100100100")]
    [InlineData("<W9XYZ> PJ4/K1ABC 73", "11110011000100000000000110100011101000110001000111001010101000000000010110100")]
    [InlineData("CQ HB10GBT", "00000000000000000000000000000000001100110101110100111110011111010000100001100")]
    [InlineData("CQ EA8/G5LSI", "00000000000000000000000011110001111100100001011101110001100010001110010001100")]
    [InlineData("CQ PJ4/K1ABC", "00000000000000000000000110100011101000110001000111001010101000000000010001100")]
    // Los corrientes, de paso: tambien tienen que dar los mismos bits.
    [InlineData("EA8DLF EA1ABC IL18", "01101101010111001101101000100011011010011101010011101000100011110010011110001")]
    [InlineData("CQ EA8DLF IL18", "00000000000000000000000000100011011010101110011011010001000011110010011110001")]
    [InlineData("CQ DX EA8DLF IL18", "00000000000000000100011011110011011010101110011011010001000011110010011110001")]
    [InlineData("EA1ABC EA8DLF -12", "01101101001110101001110100010011011010101110011011010001000111111010100111001")]
    [InlineData("EA1ABC EA8DLF R+05", "01101101001110101001110100010011011010101110011011010001001111111010111000001")]
    [InlineData("K1ABC W9XYZ EN37", "00001001101111011110001101010000011000010100100111011100000010000101011001001")]
    public void LosBitsCoincidenConLosDeReferencia(string texto, string esperado)
    {
        MensajeDe77Bits.TryEmpaquetar(texto, out var bits, out var motivo).Should().BeTrue(motivo);
        string.Concat(bits.Select(b => b == 0 ? '0' : '1')).Should().Be(esperado, $"«{texto}» tiene que emitirse bit a bit como lo hacen los demas");
    }

    [Fact]
    public void ElBitQueDiceCualVaPrimeroEsElUnicoQueCambia()
    {
        // «HB10GBT <EA8DLF> RR73» lleva los mismos campos que «<EA8DLF> HB10GBT RR73» salvo el
        // bit 70, que dice que el que va en claro es el primero.
        MensajeDe77Bits.TryEmpaquetar("<EA8DLF> HB10GBT RR73", out var a, out _).Should().BeTrue();
        MensajeDe77Bits.TryEmpaquetar("HB10GBT <EA8DLF> RR73", out var b, out _).Should().BeTrue();
        for (var i = 0; i < MensajeDe77Bits.Bits; i++)
        {
            if (i == 70) b[i].Should().NotBe(a[i]);
            else b[i].Should().Be(a[i], $"el bit {i} no depende del orden");
        }
    }

    [Theory]
    [InlineData("<HB10GBT> EA8DLF IL27", "<HB10GBT> EA8DLF IL27", "EA8DLF", "HB10GBT")]
    [InlineData("HB10GBT EA8DLF IL27", "<HB10GBT> EA8DLF IL27", "EA8DLF", "HB10GBT")]
    [InlineData("EA8DLF <HB10GBT> -14", "EA8DLF <HB10GBT> -14", "HB10GBT", "EA8DLF")]
    [InlineData("<HB10GBT> EA8DLF R-14", "<HB10GBT> EA8DLF R-14", "EA8DLF", "HB10GBT")]
    [InlineData("EA8DLF <HB10GBT> RR73", "EA8DLF <HB10GBT> RR73", "HB10GBT", "EA8DLF")]
    [InlineData("<HB10GBT> EA8DLF 73", "<HB10GBT> EA8DLF 73", "EA8DLF", "HB10GBT")]
    [InlineData("HB10GBT <EA8DLF>", "HB10GBT <EA8DLF>", "EA8DLF", "HB10GBT")]
    [InlineData("<EA8DLF> HB10GBT RRR", "<EA8DLF> HB10GBT RRR", "HB10GBT", "EA8DLF")]
    [InlineData("HB10GBT <EA8DLF> 73", "HB10GBT <EA8DLF> 73", "EA8DLF", "HB10GBT")]
    [InlineData("<PJ4/K1ABC> W9XYZ", "<PJ4/K1ABC> W9XYZ", "W9XYZ", "PJ4/K1ABC")]
    [InlineData("W9XYZ <PJ4/K1ABC> +03", "W9XYZ <PJ4/K1ABC> +03", "PJ4/K1ABC", "W9XYZ")]
    [InlineData("<W9XYZ> PJ4/K1ABC RRR", "<W9XYZ> PJ4/K1ABC RRR", "PJ4/K1ABC", "W9XYZ")]
    [InlineData("PJ4/K1ABC <W9XYZ> 73", "PJ4/K1ABC <W9XYZ> 73", "W9XYZ", "PJ4/K1ABC")]
    public void ConElCatalogoSeResuelveYSeSabeQuienEsQuien(string texto, string esperado, string llamante, string llamado)
    {
        var catalogo = new CatalogoDeIndicativos();
        catalogo.Recordar("HB10GBT");
        catalogo.Recordar("EA8DLF");
        catalogo.Recordar("PJ4/K1ABC");
        catalogo.Recordar("W9XYZ");

        MensajeDe77Bits.TryEmpaquetar(texto, out var bits, out var motivo).Should().BeTrue(motivo);
        MensajeDe77Bits.TryDesempaquetar(bits, catalogo, out var mensaje).Should().BeTrue();

        mensaje.Texto.Should().Be(esperado);
        mensaje.Llamante.Valor.Should().Be(llamante, "el indicativo que se apunta va sin angulos");
        mensaje.Llamado.Valor.Should().Be(llamado);
        mensaje.TieneIndicativoSinResolver.Should().BeFalse();
    }

    [Fact]
    public void SinCatalogoElResumenSaleComoPuntosYNoSeApunta()
    {
        MensajeDe77Bits.TryEmpaquetar("EA8DLF <HB10GBT> -14", out var bits, out _).Should().BeTrue();
        var catalogo = new CatalogoDeIndicativos();
        MensajeDe77Bits.TryDesempaquetar(bits, catalogo, out var mensaje).Should().BeTrue();

        mensaje.Texto.Should().Be("EA8DLF <...> -14");
        mensaje.Llamante.EsVacio.Should().BeTrue("no se sabe quien es y no se inventa");
        mensaje.TieneIndicativoSinResolver.Should().BeTrue();
    }

    [Fact]
    public void ElCqDeTipo4EnsenaElIndicativoYLoAprende()
    {
        var catalogo = new CatalogoDeIndicativos();
        MensajeDe77Bits.TryEmpaquetar("CQ HB10GBT", out var cq, out _).Should().BeTrue();
        MensajeDe77Bits.TryDesempaquetar(cq, catalogo, out var llamada).Should().BeTrue();
        llamada.Texto.Should().Be("CQ HB10GBT");
        llamada.Llamante.Valor.Should().Be("HB10GBT");

        // Despues del CQ, un mensaje con su resumen ya se resuelve.
        MensajeDe77Bits.TryEmpaquetar("EA8DLF <HB10GBT> -14", out var informe, out _).Should().BeTrue();
        MensajeDe77Bits.TryDesempaquetar(informe, catalogo, out var m).Should().BeTrue();
        m.Texto.Should().Be("EA8DLF <HB10GBT> -14");
    }

    [Theory]
    [InlineData("CQ HB10GBT IL27")]
    [InlineData("CQ DX HB10GBT")]
    public void ElCqDeUnRaroPierdeLoQueNoCabe(string texto)
    {
        MensajeDe77Bits.TryEmpaquetar(texto, out var bits, out var motivo).Should().BeTrue(motivo);
        MensajeDe77Bits.TryDesempaquetar(bits, new CatalogoDeIndicativos(), out var m).Should().BeTrue();
        m.Texto.Should().Be("CQ HB10GBT", "el tipo 4 no tiene sitio para localizador ni cola, y el indicativo tiene que ir entero");
    }

    [Theory]
    [InlineData("<DX> EA8DLF IL18")]
    [InlineData("<...> EA8DLF -14")]
    [InlineData("<YO> HB10GBT")]
    public void LosHuecosYLosResumenesSinResolverNoSeEmiten(string texto)
    {
        MensajeDe77Bits.TryEmpaquetar(texto, out _, out var motivo).Should().BeFalse();
        motivo.Should().Contain("ángulos");
    }

    [Fact]
    public void ElMotivoDelTextoLibreSeEntiende()
    {
        MensajeDe77Bits.TryEmpaquetar("ESTO ES UN TEXTO DEMASIADO LARGO", out _, out var motivo).Should().BeFalse();
        motivo.Should().Contain("texto libre admite 13 caracteres");
        motivo.Should().NotContain("Parameter");

        MensajeDe77Bits.TryEmpaquetar("EA8DLF ABCDEFGH1234 73", out _, out var largo).Should().BeFalse();
        largo.Should().Contain("ABCDEFGH1234").And.Contain("11");
    }

    [Fact]
    public void LoQueSeEmiteSeAprende()
    {
        MensajeDe77Bits.IndicativosQueViajan("<HB10GBT> EA8DLF IL27").Should().Equal("HB10GBT", "EA8DLF");
        MensajeDe77Bits.IndicativosQueViajan("CQ HB10GBT").Should().Equal("HB10GBT");
        MensajeDe77Bits.IndicativosQueViajan("TU5 73 GL").Should().BeEmpty("el texto libre no lleva indicativos");

        var catalogo = new CatalogoDeIndicativos();
        catalogo.Fijar("EA8DLF");
        catalogo.Olvidar();
        catalogo.Resolver(CatalogoDeIndicativos.Resumir("EA8DLF", 12), 12).Should().Be("EA8DLF", "el propio no se olvida al cambiar de banda");
    }

    [Fact]
    public void ElCatalogoNoApuntaLoQueVaEntreAngulos()
    {
        var catalogo = new CatalogoDeIndicativos();
        catalogo.Recordar("<...>");
        catalogo.Recordar("<EA8DLF>");
        catalogo.Cuantos.Should().Be(0);
    }

    [Theory]
    [InlineData(ModoDelModem.Ft8, "<HB10GBT> EA8DLF IL27")]
    [InlineData(ModoDelModem.Ft8, "EA8DLF <HB10GBT> -14")]
    [InlineData(ModoDelModem.Ft8, "<HB10GBT> EA8DLF R-14")]
    [InlineData(ModoDelModem.Ft8, "<HB10GBT> EA8DLF RR73")]
    [InlineData(ModoDelModem.Ft8, "HB10GBT <EA8DLF> 73")]
    [InlineData(ModoDelModem.Ft8, "<EA8DLF> HB10GBT RRR")]
    [InlineData(ModoDelModem.Ft8, "CQ HB10GBT")]
    [InlineData(ModoDelModem.Ft4, "<HB10GBT> EA8DLF -05")]
    [InlineData(ModoDelModem.Ft4, "HB10GBT <EA8DLF>")]
    public void SaleYVuelvePorElAire(ModoDelModem modo, string texto)
    {
        var p = ParametrosDelModo.De(modo);
        new Codificador(Tablas).TryCodificar(texto, modo, out var tonos, out var motivo).Should().BeTrue(motivo);

        const int Frecuencia = 48000;
        var ventana = GeneradorDeSenal.Ventana(p, tonos, 1500, 0, 30, Frecuencia, new Random(3));

        var catalogo = new CatalogoDeIndicativos();
        catalogo.Recordar("HB10GBT");
        catalogo.Recordar("EA8DLF");
        var d = new Decodificador(Tablas).Decodificar(ventana, Frecuencia, modo, DateTimeOffset.UnixEpoch, catalogo).Decodificaciones;

        d.Should().ContainSingle(x => x.Texto == texto, $"«{texto}» tiene que volver tal cual");
    }
}
