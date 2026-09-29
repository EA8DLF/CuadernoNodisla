using FluentAssertions;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Msk144;
using Nodisla.Cuaderno.Modos.Pruebas.Tablas;

namespace Nodisla.Cuaderno.Modos.Pruebas.Msk144;

/// <summary>Pruebas del protocolo MSK144: CRC, trama, modulacion y decodificacion.</summary>
public class Msk144Pruebas
{
    private static CodificadorMsk144 Codificador => new(TablasDelRepositorio.Msk144, TablasDelRepositorio.Msk40);

    private static DecodificadorMsk144 Decodificador => new(TablasDelRepositorio.Msk144, TablasDelRepositorio.Msk40);

    [Fact]
    public void ElCrc13CuadraConsigoMismoYCazaUnBit()
    {
        MensajeDe77Bits.TryEmpaquetar("CQ EA8DLF IL18", out var bits, out _).Should().BeTrue();
        var conCrc = Crc13.AnadirA(bits);
        conCrc.Should().HaveCount(90);
        Crc13.EsValido(conCrc).Should().BeTrue();

        for (var i = 0; i < conCrc.Length; i++)
        {
            var estropeado = conCrc.ToArray();
            estropeado[i] ^= 1;
            Crc13.EsValido(estropeado).Should().BeFalse($"un bit cambiado en la posición {i} tiene que notarse");
        }

        // Un mensaje todo ceros no da CRC cero: el polinomio mezcla.
        Crc13.Calcular(new byte[77]).Should().Be(0);
        var unUno = new byte[77];
        unUno[76] = 1;
        Crc13.Calcular(unUno).Should().NotBe(0);
    }

    [Fact]
    public void LaTramaLlevaElSincronismoDondeDiceElProtocolo()
    {
        Codificador.TryCodificar("EA1ABC EA8DLF -07", out var trama, out var motivo).Should().BeTrue(motivo);
        trama.Should().HaveCount(144);
        trama.AsSpan(0, 8).ToArray().Should().Equal(ParametrosMsk144.Sincronismo.ToArray());
        trama.AsSpan(56, 8).ToArray().Should().Equal(ParametrosMsk144.Sincronismo.ToArray());

        var palabra = new byte[128];
        CodificadorMsk144.PalabraDeLaTrama(trama, palabra);
        TablasDelRepositorio.Msk144.Codigo.CumpleParidad(palabra).Should().BeTrue();
        Crc13.EsValido(palabra.AsSpan(0, 90)).Should().BeTrue();
    }

    [Fact]
    public void LaTramaCortaLlevaElResumenYElInforme()
    {
        Codificador.TryCodificar("<EA1ABC EA8DLF> R+06", out var trama, out var motivo).Should().BeTrue(motivo);
        trama.Should().HaveCount(40);
        trama.AsSpan(0, 8).ToArray().Should().Equal(ParametrosMsk144.SincronismoCorto.ToArray());
        TablasDelRepositorio.Msk40.Codigo.CumpleParidad(trama.AsSpan(8, 32)).Should().BeTrue();

        var bits16 = new byte[16];
        TablasDelRepositorio.Msk40.Codigo.ExtraerMensaje(trama.AsSpan(8, 32), bits16);
        var (resumen, informe) = MensajeCortoMsk144.Desempaquetar(bits16);
        informe.Should().Be("R+06");
        resumen.Should().Be(MensajeCortoMsk144.Resumen("EA1ABC", "EA8DLF"));
        MensajeCortoMsk144.Resumen("EA1ABC", "EA8DLF").Should().NotBe(MensajeCortoMsk144.Resumen("EA8DLF", "EA1ABC"));
    }

    [Fact]
    public void LaSenalTieneEnvolventeConstanteYCabeEnLaBanda()
    {
        Codificador.TryCodificar("CQ EA8DLF IL18", out var trama, out _);
        var senal = ModuladorMsk.Sintetizar(trama, 1500, 48000, repeticiones: 3, amplitud: 0.5);
        senal.Should().HaveCount(3 * 144 * 24);

        // Envolvente: la potencia en cualquier tramo de un bit, lejos de las rampas, es la misma.
        var potencias = new List<double>();
        for (var desde = 2400; desde + 24 < senal.Length - 2400; desde += 24)
            potencias.Add(GeneradorDeSenal.PotenciaMedia(senal.AsSpan(desde, 24)));
        potencias.Max().Should().BeApproximately(potencias.Min(), 0.02 * potencias.Max(), "MSK tiene envolvente constante");
        potencias.Average().Should().BeApproximately(0.125, 0.01, "amplitud 0,5 da potencia 0,125");

        // Tonos: pasando de un cruce por cero al siguiente, la frecuencia instantanea es 1000 o 2000 Hz.
        var cruces = new List<int>();
        for (var i = 2401; i < senal.Length - 2400; i++)
            if (senal[i - 1] < 0 && senal[i] >= 0) cruces.Add(i);
        var periodos = cruces.Zip(cruces.Skip(1), (a, b) => 48000.0 / (b - a)).ToList();
        periodos.Should().OnlyContain(f => (f > 900 && f < 1100) || (f > 1800 && f < 2200) || (f > 1300 && f < 1700),
            "entre cruces por cero solo hay ciclos de 1000 Hz, de 2000 Hz o de transición");
    }

    [Theory]
    [InlineData("CQ EA8DLF IL18", 0.0, 7.31)]
    [InlineData("EA1ABC EA8DLF -07", 80.0, 2.05)]
    [InlineData("K1ABC EA8DLF R-15", -95.0, 11.9)]
    [InlineData("EA8DLF K1ABC RR73", 33.3, 0.4)]
    public void UnPingLimpioSeDecodificaConSuInstanteYSuFrecuencia(string texto, double desviacionHz, double segundo)
    {
        var ventana = Ventana(texto, 1500 + desviacionHz, segundo, tramas: 1, decibelios: 30, azar: new Random(1), frecuenciaDeMuestreo: 12000);
        var resultado = Decodificador.Decodificar(ventana, 12000, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());

        resultado.Decodificaciones.Should().ContainSingle();
        var d = resultado.Decodificaciones[0];
        d.Texto.Should().Be(texto);
        d.DesfaseSegundos.Should().BeApproximately(segundo, 0.01);
        d.TonoHz.Should().BeCloseTo((int)Math.Round(1500 + desviacionHz), 2);
        d.Modo.Should().Be(Aplicacion.Puertos.ModoDelModem.Msk144);
        d.Decibelios.Should().BeGreaterThan(15);
    }

    [Fact]
    public void ElAudioA48000TambienSeDecodifica()
    {
        var ventana = Ventana("CQ EA8DLF IL18", 1500, 5.0, tramas: 4, decibelios: 20, azar: new Random(2), frecuenciaDeMuestreo: 48000);
        var resultado = Decodificador.Decodificar(ventana, 48000, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
        resultado.Decodificaciones.Should().ContainSingle().Which.Texto.Should().Be("CQ EA8DLF IL18");
    }

    [Fact]
    public void UnPingDebilSoloSaleSumandoTramas()
    {
        // A -4 dB una trama sola no sale; con siete sumadas en fase, casi siempre.
        var decodificador = Decodificador;
        var salieron = 0;
        var porPromediado = 0;
        for (var intento = 0; intento < 6; intento++)
        {
            var ventana = Ventana("CQ EA8DLF IL18", 1500 + (intento * 7), 3.0 + intento, tramas: 7, decibelios: -4, azar: new Random(100 + intento), frecuenciaDeMuestreo: 12000);
            var resultado = decodificador.Decodificar(ventana, 12000, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
            resultado.Decodificaciones.Should().OnlyContain(d => d.Texto == "CQ EA8DLF IL18");
            if (resultado.Decodificaciones.Count > 0) salieron++;
            porPromediado += resultado.PorPromediado;
        }
        salieron.Should().BeGreaterThanOrEqualTo(4);
        porPromediado.Should().BeGreaterThan(0, "a esta relación el promediado es lo que saca el mensaje");
    }

    [Fact]
    public void ElMensajeCortoSoloSaleSiSeConoceElPar()
    {
        var ventana = Ventana("<EA1ABC EA8DLF> R+06", 1500, 4.0, tramas: 10, decibelios: 20, azar: new Random(3), frecuenciaDeMuestreo: 12000);

        var sinPares = Decodificador;
        sinPares.Decodificar(ventana, 12000, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos())
            .Decodificaciones.Should().BeEmpty("sin un par conocido no hay con qué comprobar el resumen");

        var conPares = Decodificador;
        conPares.RecordarPar("EA1ABC", "EA8DLF");
        conPares.RecordarPar("EA8DLF", "EA1ABC");
        var resultado = conPares.Decodificar(ventana, 12000, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
        resultado.Decodificaciones.Should().ContainSingle().Which.Texto.Should().Be("<EA1ABC EA8DLF> R+06");
        resultado.Cortos.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ConRuidoPuroNoSaleNada()
    {
        var decodificador = Decodificador;
        decodificador.RecordarPar("EA1ABC", "EA8DLF");
        for (var v = 0; v < 20; v++)
        {
            var ventana = new float[15 * 12000];
            GeneradorDeSenal.AnadirRuido(ventana, potenciaDeLaSenal: 0.01, decibelios: 0, 12000, new Random(500 + v));
            var resultado = decodificador.Decodificar(ventana, 12000, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
            resultado.Decodificaciones.Should().BeEmpty("con ruido puro no puede salir ningún mensaje");
        }
    }

    /// <summary>Una ventana de 15 s con un ping de tantas tramas en el instante pedido.</summary>
    public static float[] Ventana(string texto, double portadoraHz, double segundo, int tramas, double decibelios, Random azar, int frecuenciaDeMuestreo)
    {
        var codificador = Codificador;
        if (!codificador.TryCodificar(texto, out var trama, out var motivo)) throw new InvalidOperationException(motivo);
        var senal = ModuladorMsk.Sintetizar(trama, portadoraHz, frecuenciaDeMuestreo, tramas, amplitud: 0.35);
        var ventana = new float[15 * frecuenciaDeMuestreo];
        var comienzo = (int)Math.Round(segundo * frecuenciaDeMuestreo);
        for (var i = 0; i < senal.Length; i++)
        {
            var j = comienzo + i;
            if (j >= 0 && j < ventana.Length) ventana[j] += senal[i];
        }
        GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(senal), decibelios, frecuenciaDeMuestreo, azar);
        return ventana;
    }
}
