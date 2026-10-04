using FluentAssertions;
using Nodisla.Cuaderno.Modos.Rtty;

namespace Nodisla.Cuaderno.Modos.Pruebas.Rtty;

/// <summary>Ida y vuelta de la tabla ITA2/Baudot-Murray: letras, cifras, cambios de juego y USOS.</summary>
public class TablaBaudotPruebas
{
    [Fact]
    public void LasVeintiseisLetrasVanYVuelven()
    {
        for (var c = 'A'; c <= 'Z'; c++)
        {
            var codigo = TablaBaudot.Codigo(c, JuegoBaudot.Letras);
            codigo.Should().NotBeNull($"la letra {c} tiene que estar en el juego de letras");

            var juego = JuegoBaudot.Letras;
            TablaBaudot.Decodificar(codigo!.Value, ref juego).Should().Be(c.ToString());
            juego.Should().Be(JuegoBaudot.Letras, "una letra no cambia de juego");
        }
    }

    [Fact]
    public void LosDiezDigitosVanYVuelven()
    {
        for (var c = '0'; c <= '9'; c++)
        {
            var codigo = TablaBaudot.Codigo(c, JuegoBaudot.Cifras);
            codigo.Should().NotBeNull($"el dígito {c} tiene que estar en el juego de cifras");

            var juego = JuegoBaudot.Cifras;
            TablaBaudot.Decodificar(codigo!.Value, ref juego).Should().Be(c.ToString());
        }
    }

    [Theory]
    [InlineData('-')]
    [InlineData('\'')]
    [InlineData('!')]
    [InlineData('&')]
    [InlineData('#')]
    [InlineData('(')]
    [InlineData(')')]
    [InlineData('"')]
    [InlineData('/')]
    [InlineData(':')]
    [InlineData(';')]
    [InlineData('?')]
    [InlineData(',')]
    [InlineData('.')]
    [InlineData('$')]
    public void LaPuntuacionEstandarDeCifrasVaYVuelve(char c)
    {
        var codigo = TablaBaudot.Codigo(c, JuegoBaudot.Cifras);
        codigo.Should().NotBeNull($"'{c}' tiene que estar en el juego de cifras");

        var juego = JuegoBaudot.Cifras;
        TablaBaudot.Decodificar(codigo!.Value, ref juego).Should().Be(c.ToString());
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('\r')]
    [InlineData('\n')]
    public void EspacioRetornoYSaltoValenEnLosDosJuegos(char c)
    {
        TablaBaudot.EsComun(c).Should().BeTrue();
        TablaBaudot.Codigo(c, JuegoBaudot.Letras).Should().NotBeNull();
        TablaBaudot.Codigo(c, JuegoBaudot.Cifras).Should().NotBeNull();
        TablaBaudot.Codigo(c, JuegoBaudot.Letras).Should().Be(TablaBaudot.Codigo(c, JuegoBaudot.Cifras), "es el mismo código en los dos juegos");
    }

    [Fact]
    public void LosCambiosDeJuegoNoEmitenTextoYMueveElEstado()
    {
        var juego = JuegoBaudot.Letras;
        TablaBaudot.Decodificar(TablaBaudot.CambioACifras, ref juego).Should().BeEmpty();
        juego.Should().Be(JuegoBaudot.Cifras);

        TablaBaudot.Decodificar(TablaBaudot.CambioALetras, ref juego).Should().BeEmpty();
        juego.Should().Be(JuegoBaudot.Letras);
    }

    [Fact]
    public void UnEspacioEnCifrasVuelveALetrasPorUsos()
    {
        var juego = JuegoBaudot.Cifras;
        var uno = TablaBaudot.Decodificar(TablaBaudot.Codigo('1', JuegoBaudot.Cifras)!.Value, ref juego);
        uno.Should().Be("1");
        juego.Should().Be(JuegoBaudot.Cifras, "un dígito no cambia el juego");

        var espacio = TablaBaudot.Decodificar(TablaBaudot.Codigo(' ', JuegoBaudot.Cifras)!.Value, ref juego);
        espacio.Should().Be(" ");
        juego.Should().Be(JuegoBaudot.Letras, "USOS: un espacio en CIFRAS vuelve a LETRAS");

        // Y sin mandar un cambio explícito a LETRAS, el siguiente código ya se lee como letra.
        var a = TablaBaudot.Decodificar(TablaBaudot.Codigo('A', JuegoBaudot.Letras)!.Value, ref juego);
        a.Should().Be("A");
    }

    [Fact]
    public void UnEspacioEnLetrasNoCambiaDeJuego()
    {
        var juego = JuegoBaudot.Letras;
        TablaBaudot.Decodificar(TablaBaudot.Codigo(' ', JuegoBaudot.Letras)!.Value, ref juego);
        juego.Should().Be(JuegoBaudot.Letras);
    }

    [Fact]
    public void UnCaracterQueNoEstaEnLaTablaNoTieneCodigo()
    {
        TablaBaudot.Codigo('ñ', JuegoBaudot.Letras).Should().BeNull();
        TablaBaudot.Codigo('@', JuegoBaudot.Cifras).Should().BeNull();
        TablaBaudot.JuegoRequerido('ñ', JuegoBaudot.Letras).Should().BeNull();
    }

    [Theory]
    [InlineData("CQ CQ DE EA8DLF PSE K")]
    [InlineData("RST 599 599 NAME JOSE QTH TENERIFE")]
    [InlineData("TEST 1234567890 - ' ! & # ( ) \" / : ; ? , . FIN")]
    public void UnTextoCompletoVaYVuelveCodigoACodigo(string texto)
    {
        var codigos = GeneradorRtty.Codigos(texto);
        var juego = JuegoBaudot.Letras;
        var leido = new System.Text.StringBuilder();
        foreach (var codigo in codigos) leido.Append(TablaBaudot.Decodificar(codigo, ref juego));

        leido.ToString().Should().Be(texto, "lo que se manda tiene que volver exactamente igual");
    }
}
