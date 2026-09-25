using FluentAssertions;
using Nodisla.Cuaderno.Concursos.Telegrafia;

namespace Nodisla.Cuaderno.Concursos.Pruebas;

/// <summary>El manipulador: alfabeto, temporizacion y audio.</summary>
public sealed class TelegrafiaPruebas
{
    [Theory]
    [InlineData('A', ".-")]
    [InlineData('Z', "--..")]
    [InlineData('5', ".....")]
    [InlineData('/', "-..-.")]
    [InlineData('?', "..--..")]
    // Lo que anade el castellano.
    [InlineData('Ñ', "--.--")]
    [InlineData('ñ', "--.--")]
    [InlineData('É', "..-..")]
    [InlineData('Ç', "-.-..")]
    // Las demas vocales acentuadas se mandan como la vocal sin acento.
    [InlineData('Á', ".-")]
    [InlineData('Ó', "---")]
    public void ElAlfabetoIncluyeLoQueHaceFaltaParaElCastellano(char letra, string esperado)
    {
        AlfabetoMorse.TryCodigo(letra, out var codigo).Should().BeTrue();
        codigo.Should().Be(esperado);
    }

    [Fact]
    public void LaEnieNoEsUnaEne()
    {
        AlfabetoMorse.TryCodigo('Ñ', out var enie);
        AlfabetoMorse.TryCodigo('N', out var ene);
        enie.Should().NotBe(ene);
    }

    [Theory]
    [InlineData("AR", ".-.-.")]
    [InlineData("SK", "...-.-")]
    [InlineData("BT", "-...-")]
    [InlineData("KN", "-.--.")]
    [InlineData("HH", "........")]
    public void LasSenalesDeProcedimientoVanPegadas(string nombre, string esperado)
    {
        AlfabetoMorse.TryProcedimiento(nombre, out var codigo).Should().BeTrue();
        codigo.Should().Be(esperado);
    }

    [Fact]
    public void LaVelocidadSeMideConLaPalabraPatron()
    {
        // PARIS dura 50 unidades: a 20 ppm, un punto son 60 ms y la palabra 3 segundos.
        var manipulador = new Manipulador(20);
        manipulador.Punto.TotalMilliseconds.Should().BeApproximately(60, 0.001);

        // La palabra suelta no lleva el hueco final entre palabras: son 50 - 7 = 43 unidades.
        manipulador.Duracion("PARIS").TotalMilliseconds.Should().BeApproximately(43 * 60, 0.001);
    }

    [Fact]
    public void LaRayaDuraTresPuntosYLosHuecosLoQueDicenLasReglas()
    {
        var manipulador = new Manipulador(25);
        var piezas = manipulador.Manipular("A B");

        piezas.Select(p => p.Tipo).Should().Equal(
            TipoDeElemento.Punto, TipoDeElemento.EntreElementos, TipoDeElemento.Raya,
            TipoDeElemento.EntrePalabras,
            TipoDeElemento.Raya, TipoDeElemento.EntreElementos, TipoDeElemento.Punto,
            TipoDeElemento.EntreElementos, TipoDeElemento.Punto,
            TipoDeElemento.EntreElementos, TipoDeElemento.Punto);

        var punto = manipulador.Punto;
        piezas[2].Duracion.Should().Be(punto * 3);
        piezas[3].Duracion.Should().Be(punto * 7);
        manipulador.EntreCaracteres.Should().Be(punto * 3);
    }

    [Fact]
    public void ConFarnsworthLasLetrasNoCambianYLosHuecosSeAlargan()
    {
        var plano = new Manipulador(20);
        var farnsworth = new Manipulador(20, ppmEfectivas: 10);

        farnsworth.EsFarnsworth.Should().BeTrue();
        farnsworth.Punto.Should().Be(plano.Punto, "las letras van a la misma velocidad");
        farnsworth.EntreCaracteres.Should().BeGreaterThan(plano.EntreCaracteres);
        farnsworth.EntrePalabras.Should().BeGreaterThan(plano.EntrePalabras);

        // La prueba de fuego de Farnsworth: la palabra patron entera, con su hueco final, tiene
        // que durar exactamente los seis segundos de las diez palabras por minuto efectivas.
        var palabraCompleta = farnsworth.Duracion("PARIS") + farnsworth.EntrePalabras;
        palabraCompleta.TotalSeconds.Should().BeApproximately(6.0, 0.001);

        // Y la segunda palabra ya no lleva hueco detras: seis segundos mas la primera sin el suyo.
        farnsworth.Duracion("PARIS PARIS").TotalSeconds.Should()
            .BeApproximately(6.0 + farnsworth.Duracion("PARIS").TotalSeconds, 0.001);
    }

    [Fact]
    public void PedirUnaVelocidadImposibleFalla()
    {
        var accion = () => new Manipulador(200);
        accion.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LoQueNoSeSabeManipularSeDescartaSinDejarMudaLaLlamada()
    {
        var manipulador = new Manipulador(25);
        var conBasura = manipulador.Manipular("CQ ¡TEST!");
        var limpio = manipulador.Manipular("CQ TEST!");

        conBasura.Count.Should().Be(limpio.Count, "el signo de apertura no existe en Morse");
        manipulador.Manipular("   ").Should().BeEmpty();
    }

    [Fact]
    public void LasSenalesEntreAngulosSeManipulanPegadas()
    {
        var manipulador = new Manipulador(25);
        var conSenal = manipulador.Manipular("<AR>");
        var porSeparado = manipulador.Manipular("A R");

        conSenal.Count(p => p.ConTono).Should().Be(5);
        conSenal.Should().NotContain(p => p.Tipo == TipoDeElemento.EntrePalabras);
        porSeparado.Should().Contain(p => p.Tipo == TipoDeElemento.EntrePalabras);
    }

    [Fact]
    public void EscribirEnPuntosYRayasSirveParaEnsenarloEnPantalla()
    {
        AlfabetoMorse.Escribir("SOS").Should().Be("... --- ...");
        AlfabetoMorse.Escribir("A B").Should().Be(".- / -...");
    }

    [Fact]
    public void ElAudioDuraLoQueTieneQueDurar()
    {
        var manipulador = new Manipulador(20);
        var generador = new GeneradorDeTonos(frecuenciaDeMuestreo: 48000);
        var muestras = generador.Generar(manipulador, "PARIS");

        var segundos = muestras.Length / 48000.0;
        segundos.Should().BeApproximately(manipulador.Duracion("PARIS").TotalSeconds, 0.001);
    }

    [Fact]
    public void ElTonoEntraYSaleConRampaParaQueNoHayaClics()
    {
        var generador = new GeneradorDeTonos(48000, tono: 700, amplitud: 0.8);
        var muestras = generador.Generar(new Manipulador(20), "E");

        muestras.Should().NotBeEmpty();
        Math.Abs(muestras[0]).Should().BeLessThan(0.01f, "un tono que arranca de golpe hace clic");
        Math.Abs(muestras[^1]).Should().BeLessThan(0.01f);
        muestras.Max(Math.Abs).Should().BeLessThanOrEqualTo(0.8f);
        muestras.Max(Math.Abs).Should().BeGreaterThan(0.5f, "en medio el tono llega a su amplitud");
    }

    [Fact]
    public void ElSilencioEsSilencioDeVerdad()
    {
        var manipulador = new Manipulador(20);
        var generador = new GeneradorDeTonos(48000);
        var piezas = manipulador.Manipular("E E");

        // La pieza del medio es el hueco entre palabras: tiene que salir a cero.
        var antes = 0;
        foreach (var pieza in piezas)
        {
            var cuantas = (int)Math.Round(pieza.Duracion.TotalSeconds * 48000);
            if (pieza.Tipo == TipoDeElemento.EntrePalabras)
            {
                var muestras = generador.Generar(piezas);
                muestras.Skip(antes).Take(cuantas).Should().OnlyContain(m => m == 0f);
                return;
            }
            antes += cuantas;
        }
        Assert.Fail("no habia hueco entre palabras");
    }

    [Fact]
    public void UnTonoImposibleFalla()
    {
        var accion = () => new GeneradorDeTonos(48000, tono: 20000);
        accion.Should().Throw<ArgumentOutOfRangeException>();
    }
}
