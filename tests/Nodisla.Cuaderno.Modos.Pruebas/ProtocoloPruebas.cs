using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ft8;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>Pruebas del sello de 14 bits que decide si una decodificacion es de fiar.</summary>
public class Crc14Pruebas
{
    [Fact]
    public void ElSelloPuestoSeReconoce()
    {
        var azar = new Random(5);
        for (var intento = 0; intento < 200; intento++)
        {
            var mensaje = new byte[MensajeDe77Bits.Bits];
            for (var i = 0; i < mensaje.Length; i++) mensaje[i] = (byte)azar.Next(2);
            Crc14.EsValido(Crc14.AnadirA(mensaje)).Should().BeTrue();
        }
    }

    [Fact]
    public void CualquierBitCambiadoTiraElSello()
    {
        // Es la propiedad que de verdad importa: un bit mal no puede pasar por bueno, porque
        // seria una letra cambiada en un indicativo y un contacto falso en el cuaderno.
        var azar = new Random(9);
        var mensaje = new byte[MensajeDe77Bits.Bits];
        for (var i = 0; i < mensaje.Length; i++) mensaje[i] = (byte)azar.Next(2);
        var conSello = Crc14.AnadirA(mensaje);

        for (var bit = 0; bit < conSello.Length; bit++)
        {
            var estropeado = conSello.ToArray();
            estropeado[bit] ^= 1;
            Crc14.EsValido(estropeado).Should().BeFalse($"cambiar el bit {bit} tiene que romper el sello");
        }
    }

    [Fact]
    public void ElMensajeDeTodoCerosPasaElSello()
    {
        // Esto no es un fallo del CRC sino una propiedad suya: el sello de nada es nada. Se
        // comprueba a proposito para dejar constancia de por que el desempaquetado rechaza
        // aparte el mensaje de todo ceros, que es la palabra falsa mas habitual de todas.
        Crc14.EsValido(new byte[Crc14.BitsConCrc]).Should().BeTrue();

        MensajeDe77Bits.TryDesempaquetar(new byte[MensajeDe77Bits.Bits], new CatalogoDeIndicativos(), out _)
            .Should().BeFalse("el mensaje de todo ceros tiene que rechazarse aunque el sello cuadre");
    }
}

/// <summary>Pruebas del empaquetado de mensajes en 77 bits.</summary>
public class MensajePruebas
{
    [Theory]
    [InlineData("CQ EA8DLF IL18")]
    [InlineData("CQ DX EA8DLF IL18")]
    [InlineData("CQ TEST EA8DLF IL18")]
    [InlineData("CQ 123 EA8DLF IL18")]
    [InlineData("EA1ABC EA8DLF IL18")]
    [InlineData("EA1ABC EA8DLF -12")]
    [InlineData("EA1ABC EA8DLF +05")]
    [InlineData("EA1ABC EA8DLF R-12")]
    [InlineData("EA1ABC EA8DLF RRR")]
    [InlineData("EA1ABC EA8DLF RR73")]
    [InlineData("EA1ABC EA8DLF 73")]
    [InlineData("K1ABC W9XYZ EN37")]
    [InlineData("A45XR EA8DLF -09")]
    [InlineData("2E0ABC 9A1AA JN65")]
    [InlineData("QRZ EA8DLF IL18")]
    [InlineData("DE EA8DLF IL18")]
    public void UnMensajeCorrienteVaYVuelveIgual(string texto)
    {
        MensajeDe77Bits.TryEmpaquetar(texto, out var bits, out var motivo).Should().BeTrue(motivo);
        bits.Should().HaveCount(MensajeDe77Bits.Bits);

        MensajeDe77Bits.TryDesempaquetar(bits, new CatalogoDeIndicativos(), out var mensaje).Should().BeTrue();
        mensaje.Texto.Should().Be(texto);
        mensaje.Tipo.Should().Be(TipoDeMensaje.Normal);
    }

    [Theory]
    [InlineData("HOLA MUNDO")]
    [InlineData("TU5 73 GL")]
    [InlineData("1234567890123")]
    [InlineData("A/B.C-D+E?F")]
    public void UnTextoLibreVaYVuelveIgual(string texto)
    {
        // El texto libre son 13 caracteres de un alfabeto de 42, y eso da 71 bits: mas de los
        // que caben en el entero mayor de la maquina. Aqui se comprueba que esa cuenta esta bien.
        MensajeDe77Bits.TryEmpaquetar(texto, out var bits, out var motivo).Should().BeTrue(motivo);
        MensajeDe77Bits.TryDesempaquetar(bits, new CatalogoDeIndicativos(), out var mensaje).Should().BeTrue();
        mensaje.Texto.Should().Be(texto);
        mensaje.Tipo.Should().Be(TipoDeMensaje.TextoLibre);
    }

    [Fact]
    public void ElMensajeDiceQuienLlamaYAQuien()
    {
        MensajeDe77Bits.TryEmpaquetar("EA1ABC EA8DLF -12", out var bits, out _).Should().BeTrue();
        MensajeDe77Bits.TryDesempaquetar(bits, new CatalogoDeIndicativos(), out var mensaje).Should().BeTrue();

        mensaje.Llamante.Valor.Should().Be("EA8DLF", "el segundo indicativo es siempre quien emite");
        mensaje.Llamado.Valor.Should().Be("EA1ABC");
        mensaje.EsCq.Should().BeFalse();
        mensaje.Informe.Should().Be(-12);
        mensaje.Locator.EsVacio.Should().BeTrue();
    }

    [Fact]
    public void UnaLlamadaGeneralNoTieneLlamado()
    {
        MensajeDe77Bits.TryEmpaquetar("CQ EA8DLF IL18", out var bits, out _).Should().BeTrue();
        MensajeDe77Bits.TryDesempaquetar(bits, new CatalogoDeIndicativos(), out var mensaje).Should().BeTrue();

        mensaje.EsCq.Should().BeTrue();
        mensaje.Llamado.EsVacio.Should().BeTrue("«CQ» no es un indicativo y no puede acabar en el cuaderno");
        mensaje.Llamante.Valor.Should().Be("EA8DLF");
        mensaje.Locator.Valor.Should().Be("IL18");
    }

    [Fact]
    public void UnIndicativoResumidoSinCatalogoNoSeInventa()
    {
        // El indicativo raro viaja entero y el corriente viaja resumido. Si el receptor no ha
        // oido antes al corriente, no hay manera de saber quien es: se dice, no se adivina.
        MensajeDe77Bits.TryEmpaquetar("EA8DLF EA1ABC/P 73", out var bits, out var motivo).Should().BeTrue(motivo);

        MensajeDe77Bits.TryDesempaquetar(bits, new CatalogoDeIndicativos(), out var aCiegas).Should().BeTrue();
        aCiegas.Tipo.Should().Be(TipoDeMensaje.IndicativoNoEstandar);
        aCiegas.Texto.Should().Contain("<...>");
        aCiegas.TieneIndicativoSinResolver.Should().BeTrue("este mensaje no sirve para apuntar un contacto");

        var catalogo = new CatalogoDeIndicativos();
        catalogo.Recordar("EA8DLF");
        MensajeDe77Bits.TryDesempaquetar(bits, catalogo, out var conCatalogo).Should().BeTrue();
        conCatalogo.Texto.Should().Contain("EA8DLF");
        conCatalogo.TieneIndicativoSinResolver.Should().BeFalse();
    }

    [Fact]
    public void ElResumenDeUnIndicativoEsSiempreElMismo()
    {
        CatalogoDeIndicativos.Resumir("EA8DLF", 22).Should().Be(CatalogoDeIndicativos.Resumir("EA8DLF", 22));
        CatalogoDeIndicativos.Resumir("EA8DLF", 22).Should().NotBe(CatalogoDeIndicativos.Resumir("EA8DLG", 22));
        CatalogoDeIndicativos.Resumir("EA8DLF", 10).Should().BeLessThan(1024);
        CatalogoDeIndicativos.Resumir("EA8DLF", 12).Should().BeLessThan(4096);
    }

    [Fact]
    public void LoQueNoCabeSeRechazaEnVezDeRecortarse()
    {
        // Emitir un mensaje recortado diría otra cosa. Mejor negarse.
        MensajeDe77Bits.TryEmpaquetar("ESTO SON MUCHISIMOS CARACTERES", out _, out var motivo).Should().BeFalse();
        motivo.Should().NotBeEmpty("al operador hay que decirle por qué no se puede emitir");
        MensajeDe77Bits.TryEmpaquetar("   ", out _, out _).Should().BeFalse();
        MensajeDe77Bits.TryEmpaquetar(null, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void LosInformesFueraDeRangoNoSeEmiten()
    {
        MensajeDe77Bits.TryEmpaquetar($"EA1ABC EA8DLF {MensajeDe77Bits.InformeMinimo:+00;-00}", out _, out _).Should().BeTrue();
        MensajeDe77Bits.TryEmpaquetar($"EA1ABC EA8DLF {MensajeDe77Bits.InformeMaximo:+00;-00}", out _, out _).Should().BeTrue();

        // Fuera de rango cae a texto libre, que es honesto: dice lo que dice y no un informe falso.
        MensajeDe77Bits.TryEmpaquetar("EA1ABC EA8DLF -99", out var bits, out _).Should().BeTrue();
        MensajeDe77Bits.TryDesempaquetar(bits, new CatalogoDeIndicativos(), out var mensaje).Should().BeTrue();
        mensaje.Tipo.Should().NotBe(TipoDeMensaje.Normal);
    }
}

/// <summary>Pruebas de las constantes de los dos modos.</summary>
public class ParametrosDelModoPruebas
{
    [Theory]
    [InlineData(ModoDelModem.Ft8)]
    [InlineData(ModoDelModem.Ft4)]
    public void LasCuentasDeLosSimbolosCuadran(ModoDelModem modo)
    {
        var p = ParametrosDelModo.De(modo);

        // Si esto no cuadrara, o sobrarían bits del mensaje o faltarían símbolos donde ponerlos.
        p.PosicionesDeDatos.Should().HaveCount(p.SimbolosDeDatos);
        (p.SimbolosDeDatos * p.BitsPorSimbolo).Should().Be(174, "el código corrector saca siempre 174 bits");

        var sincronismo = 0;
        for (var i = 0; i < p.SimbolosTotales; i++) if (p.EsSimboloDeSincronismo(i, out _)) sincronismo++;
        (sincronismo + p.SimbolosDeDatos + (2 * p.SimbolosDeRampa)).Should().Be(p.SimbolosTotales);
    }

    [Theory]
    [InlineData(ModoDelModem.Ft8)]
    [InlineData(ModoDelModem.Ft4)]
    public void ElCodigoDeGrayEsReversibleYCambiaUnSoloBit(ModoDelModem modo)
    {
        var p = ParametrosDelModo.De(modo);

        for (var valor = 0; valor < p.Tonos; valor++)
            p.MapaDeGrayInverso[p.MapaDeGray[valor]].Should().Be((byte)valor);

        // La gracia del código de Gray: confundir un tono con el de al lado, que es el error más
        // probable, solo estropea un bit y el corrector lo arregla con facilidad.
        for (var tono = 1; tono < p.Tonos; tono++)
        {
            var diferencia = p.MapaDeGrayInverso[tono] ^ p.MapaDeGrayInverso[tono - 1];
            System.Numerics.BitOperations.PopCount((uint)diferencia).Should().Be(1,
                $"los tonos {tono - 1} y {tono} tienen que diferenciarse en un solo bit");
        }
    }

    [Theory]
    [InlineData(ModoDelModem.Ft8)]
    [InlineData(ModoDelModem.Ft4)]
    public void ElSimboloCaeEnUnaPotenciaDeDosAlAnalizar(ModoDelModem modo)
    {
        var p = ParametrosDelModo.De(modo);

        Senal.Fft.EsPotenciaDeDos(p.MuestrasPorSimboloDeAnalisis).Should().BeTrue(
            "solo así las casillas de la transformada caen exactamente sobre los tonos");
        (p.EspaciadoDeTonosHz * p.DuracionDeSimboloSegundos).Should().BeApproximately(1.0, 1e-12,
            "la separación entre tonos es la inversa de lo que dura un símbolo: de ahí sale la continuidad de fase");

        // A 48000 muestras por segundo, que es lo que da la tarjeta, el símbolo tiene que caer
        // en un número entero de muestras o la emisión se iría desfasando poco a poco.
        p.MuestrasPorSimbolo(48000).Should().BeGreaterThan(0);
    }
}
