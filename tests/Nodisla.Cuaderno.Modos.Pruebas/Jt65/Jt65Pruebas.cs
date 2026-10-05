using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Jt65;
using Nodisla.Cuaderno.Modos.Marco;

namespace Nodisla.Cuaderno.Modos.Pruebas.Jt65;

/// <summary>Pruebas de las tablas y el empaquetado de JT65.</summary>
public class ProtocoloJt65Pruebas
{
    [Fact]
    public void ElVectorDeSincronismoTiene126IntervalosYLaMitadSonSincronismo()
    {
        TablasJt65.Sincronismo.Length.Should().Be(126);
        TablasJt65.PosicionesDeSincronismo.Should().HaveCount(63);
        TablasJt65.PosicionesDeDatos.Should().HaveCount(63);
        TablasJt65.PosicionesDeSincronismo.Concat(TablasJt65.PosicionesDeDatos).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ElEntrelazadoEsUnaPermutacionYSeDeshace()
    {
        TablasJt65.EntrelazadoHaciaElAire.Should().OnlyHaveUniqueItems().And.HaveCount(63);
        for (var i = 0; i < 63; i++)
            TablasJt65.EntrelazadoDesdeElAire[TablasJt65.EntrelazadoHaciaElAire[i]].Should().Be(i);
        // Dos simbolos seguidos de la palabra quedan a nueve intervalos de datos.
        (TablasJt65.EntrelazadoHaciaElAire[1] - TablasJt65.EntrelazadoHaciaElAire[0]).Should().Be(9);
    }

    [Fact]
    public void ElCodigoDeGrayCambiaUnBitEntreVecinos()
    {
        for (var s = 1; s < 64; s++)
            System.Numerics.BitOperations.PopCount((uint)(TablasJt65.Gray[s] ^ TablasJt65.Gray[s - 1])).Should().Be(1);
        for (var s = 0; s < 64; s++) TablasJt65.GrayInverso[TablasJt65.Gray[s]].Should().Be((byte)s);
    }

    [Theory]
    [InlineData("CQ EA8DLF IL18")]
    [InlineData("CQ DX EA8DLF IL18")]
    [InlineData("CQ 070 EA8DLF IL18")]
    [InlineData("QRZ EA8DLF IL18")]
    [InlineData("EA8DLF EA1ABC IN80")]
    [InlineData("EA1ABC EA8DLF -15")]
    [InlineData("EA1ABC EA8DLF R-01")]
    [InlineData("EA1ABC EA8DLF RRR")]
    [InlineData("EA1ABC EA8DLF RO")]
    [InlineData("EA1ABC EA8DLF 73")]
    [InlineData("K1ABC W9XYZ FN42")]
    [InlineData("VK3ABC ZL2XYZ RE78")]
    [InlineData("PY2ABC LU1XYZ GG66")]
    [InlineData("JA1XYZ EA8DLF PM95")]
    [InlineData("3DA0AB EA8DLF")]
    [InlineData("HOLA MUNDO")]
    [InlineData("TNX 73 GL")]
    [InlineData("EA8DLF/P NO")]
    public void LosMensajesVanYVuelvenIguales(string texto)
    {
        MensajeDe72Bits.TryEmpaquetar(texto, out var bits, out var motivo).Should().BeTrue(motivo);
        bits.Should().HaveCount(72);
        MensajeDe72Bits.TryDesempaquetar(bits, out var mensaje).Should().BeTrue();
        mensaje.Texto.Should().Be(texto);
    }

    [Fact]
    public void LosCamposSeEntienden()
    {
        MensajeDe72Bits.TryEmpaquetar("CQ EA8DLF IL18", out var bits, out _);
        MensajeDe72Bits.TryDesempaquetar(bits, out var cq).Should().BeTrue();
        cq.EsCq.Should().BeTrue();
        cq.Llamante.Valor.Should().Be("EA8DLF");
        cq.Llamado.EsVacio.Should().BeTrue();
        cq.Locator.Valor.Should().Be("IL18");
        cq.EsTextoLibre.Should().BeFalse();

        MensajeDe72Bits.TryEmpaquetar("EA1ABC EA8DLF -15", out bits, out _);
        MensajeDe72Bits.TryDesempaquetar(bits, out var informe).Should().BeTrue();
        informe.Informe.Should().Be(-15);
        informe.Llamado.Valor.Should().Be("EA1ABC");
        informe.EsCq.Should().BeFalse();

        MensajeDe72Bits.TryEmpaquetar("HOLA MUNDO", out bits, out _);
        MensajeDe72Bits.TryDesempaquetar(bits, out var libre).Should().BeTrue();
        libre.EsTextoLibre.Should().BeTrue();
        libre.Llamante.EsVacio.Should().BeTrue();
    }

    [Fact]
    public void ElTextoLibreOcupaLosBitsQueDiceElProtocolo()
    {
        // Texto libre: bit alto del tercer campo puesto; el primero de un mensaje que empieza
        // por "0" (indice cero) tiene los 27 bits altos del primer campo a cero.
        MensajeDe72Bits.TryEmpaquetar("00000", out var bits, out _);
        bits[56].Should().Be(1, "la marca de texto libre es el bit alto del tercer campo");
        bits[..27].Should().OnlyContain(b => b == 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CQ EA8DLF IL18 EXTRA")]
    [InlineData("ESTO ES DEMASIADO LARGO")]
    [InlineData("HOLA, MUNDO")]
    [InlineData("EA1ABC EA8DLF -45")]
    public void LoQueNoCabeSeRechaza(string texto)
    {
        MensajeDe72Bits.TryEmpaquetar(texto, out _, out var motivo).Should().BeFalse();
        motivo.Should().NotBeEmpty();
    }

    [Fact]
    public void LosBitsAlAzarCasiNuncaSonUnMensaje()
    {
        // Sin CRC, el desempaquetado es el ultimo filtro: conviene saber que fraccion de los
        // 72 bits al azar pasan por mensaje. Los indicativos corrientes son 262 millones de
        // 268 posibles y el tercer campo acepta 32 465 de 32 768: pasa mas de la mitad. Por
        // eso el filtro de verdad esta en el decodificador blando, no aqui.
        var azar = new Random(9);
        var bits = new byte[72];
        var pasan = 0;
        for (var i = 0; i < 2000; i++)
        {
            for (var b = 0; b < 72; b++) bits[b] = (byte)azar.Next(2);
            if (MensajeDe72Bits.TryDesempaquetar(bits, out _)) pasan++;
        }
        pasan.Should().BeLessThan(2000);
    }

    [Fact]
    public void CodificarYDemodularSinRuidoDevuelveElMensaje()
    {
        var codificador = new CodificadorJt65();
        codificador.TryCodificar("CQ EA8DLF IL18", out var tonos, out var motivo).Should().BeTrue(motivo);
        tonos.Should().HaveCount(126);
        foreach (var p in TablasJt65.PosicionesDeSincronismo) tonos[p].Should().Be(0);
        foreach (var p in TablasJt65.PosicionesDeDatos) tonos[p].Should().BeInRange(2, 65);

        var audio = ModuladorJt65.Sintetizar(ParametrosJt65.A, tonos, 1270.5, ParametrosJt65.FrecuenciaDeAnalisis, 0.5);
        audio.Length.Should().Be(126 * 4096);
        var ventana = new float[ParametrosJt65.A.MuestrasDeLaVentana];
        audio.CopyTo(ventana, ParametrosJt65.FrecuenciaDeAnalisis);

        var decodificador = new DecodificadorJt65();
        var resultado = decodificador.Decodificar(ventana, DateTimeOffset.UnixEpoch);
        resultado.Decodificaciones.Should().ContainSingle();
        var d = resultado.Decodificaciones[0];
        d.Texto.Should().Be("CQ EA8DLF IL18");
        d.TonoHz.Should().BeInRange(1268, 1273);
        Math.Abs(d.DesfaseSegundos).Should().BeLessThan(0.1);
        d.Modo.Should().Be(ModoDelModem.Jt65);
    }

    [Fact]
    public void ElModoGeneraYDecodificaConDesfaseYRuidoModerado()
    {
        var modo = new ModoJt65();
        modo.Modo.Should().Be(ModoDelModem.Jt65);
        modo.Periodo.Should().Be(TimeSpan.FromSeconds(60));
        modo.FrecuenciaDeAnalisis.Should().Be(11025);

        var senal = modo.Generar("EA1ABC EA8DLF -15", 1500, 11025);
        var ventana = new float[ParametrosJt65.A.MuestrasDeLaVentana];
        var comienzo = (int)Math.Round(1.7 * 11025);
        for (var i = 0; i < senal.Length; i++) ventana[comienzo + i] = senal[i];
        GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(senal), -15, 11025, new Random(3));

        var salida = modo.Decodificar(ventana, DateTimeOffset.UnixEpoch, CancellationToken.None);
        salida.Should().ContainSingle();
        salida[0].Texto.Should().Be("EA1ABC EA8DLF -15");
        salida[0].DesfaseSegundos.Should().BeApproximately(0.7, 0.1);
        salida[0].TonoHz.Should().BeInRange(1498, 1502);
    }

    [Fact]
    public void UnMensajeQueNoCabeLanzaFormatException()
    {
        var modo = new ModoJt65();
        var generar = () => modo.Generar("ESTO NO CABE EN TRECE", 1500, 48000);
        generar.Should().Throw<FormatException>();
    }

    [Fact]
    public void ElAudioGeneradoEmpiezaYAcabaEnSilencioSuave()
    {
        var modo = new ModoJt65();
        var senal = modo.Generar("CQ EA8DLF IL18", 1500, 48000);
        senal.Length.Should().Be((int)Math.Round(126 * 4096.0 / 11025 * 48000));
        Math.Abs(senal[0]).Should().BeLessThan(0.01f);
        Math.Abs(senal[^1]).Should().BeLessThan(0.01f);
        // El pico de fábrica es el conservador compartido por todos los modos (0,3), no el 0,5
        // de antes de que existiera el nivel de salida ajustable (ver NivelDeSalidaPruebas).
        senal.Max().Should().BeApproximately((float)IModoDigital.AmplitudDeSalidaPorDefecto, 0.01f);
    }
}
