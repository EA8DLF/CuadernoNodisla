using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Fst4;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Pruebas.Tablas;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Pruebas.Fst4;

/// <summary>Pruebas del protocolo FST4 y FST4W: CRC, trama, modulacion y decodificacion.</summary>
public class Fst4Pruebas
{
    private static CodificadorFst4 Codificador => new(TablasDelRepositorio.Fst4, esFst4w: false);

    private static CodificadorFst4 CodificadorW => new(TablasDelRepositorio.Fst4w, esFst4w: true);

    [Fact]
    public void ElCrc24CuadraYCazaUnBit()
    {
        MensajeDe77Bits.TryEmpaquetar("CQ EA8DLF IL18", out var bits, out _).Should().BeTrue();
        var conCrc = Crc24.AnadirA(bits);
        conCrc.Should().HaveCount(101);
        Crc24.EsValido(conCrc).Should().BeTrue();
        for (var i = 0; i < conCrc.Length; i++)
        {
            var estropeado = conCrc.ToArray();
            estropeado[i] ^= 1;
            Crc24.EsValido(estropeado).Should().BeFalse();
        }
        Crc24.Calcular(new byte[50]).Should().Be(0u);
    }

    [Fact]
    public void LosPeriodosTienenLasVelocidadesDelProtocolo()
    {
        var esperados = new (int Periodo, int Muestras, double Baudios)[]
        {
            (15, 720, 16.667), (30, 1680, 7.143), (60, 3888, 3.086), (120, 8200, 1.463),
            (300, 21504, 0.558), (900, 66560, 0.180), (1800, 134400, 0.0893),
        };
        foreach (var (periodo, muestras, baudios) in esperados)
        {
            var p = ParametrosFst4.DelPeriodo(periodo);
            p.MuestrasPorSimboloA12000.Should().Be(muestras);
            p.Baudios.Should().BeApproximately(baudios, 0.001);
            Senal.Fft.EsPotenciaDeDos(p.MuestrasPorSimboloDeAnalisis).Should().BeTrue();
            p.FrecuenciaDeAnalisis.Should().BeInRange(7000, 18000);
            p.DuracionDeLaSenalSegundos.Should().BeLessThan(periodo);
            p.PosicionesDeDatos.Should().HaveCount(120);
        }
        var acto = () => ParametrosFst4.DelPeriodo(45);
        acto.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LaTramaLlevaLosCincoGruposDeSincronismo()
    {
        Codificador.TryCodificar("EA1ABC EA8DLF -07", out var tonos, out var motivo).Should().BeTrue(motivo);
        tonos.Should().HaveCount(160);
        foreach (var inicio in new[] { 0, 76, 152 })
            tonos.AsSpan(inicio, 8).ToArray().Should().Equal(ParametrosFst4.SincronismoUno.ToArray());
        foreach (var inicio in new[] { 38, 114 })
            tonos.AsSpan(inicio, 8).ToArray().Should().Equal(ParametrosFst4.SincronismoDos.ToArray());

        // La mezcla es su propia inversa y no deja un CQ en ceros.
        MensajeDe77Bits.TryEmpaquetar("CQ EA8DLF IL18", out var bits, out _);
        var palabra = Codificador.PalabraDeCodigo(bits);
        TablasDelRepositorio.Fst4.Codigo.CumpleParidad(palabra).Should().BeTrue();
        var mezclados = palabra.AsSpan(0, 77).ToArray();
        mezclados.Should().NotEqual(bits);
        CodificadorFst4.AplicarMezcla(mezclados);
        mezclados.Should().Equal(bits);
    }

    [Fact]
    public void Fst4wLlevaElMensajeDeWsprSinMezcla()
    {
        CodificadorW.TryCodificar("EA8DLF IL18 37", out var tonos, out var motivo).Should().BeTrue(motivo);
        tonos.Should().HaveCount(160);
        MensajeWspr.TryAnalizar("EA8DLF IL18 37", out var m, out _).Should().BeTrue();
        var palabra = CodificadorW.PalabraDeCodigo(m.Empaquetar());
        palabra.AsSpan(0, 50).ToArray().Should().Equal(m.Empaquetar());
        Crc24.EsValido(palabra.AsSpan(0, 74)).Should().BeTrue();
        TablasDelRepositorio.Fst4w.Codigo.CumpleParidad(palabra).Should().BeTrue();
    }

    [Fact]
    public void LaSenalOcupaCuatroTonosYTieneEnvolventeConstante()
    {
        var p = ParametrosFst4.DelPeriodo(15);
        Codificador.TryCodificar("CQ EA8DLF IL18", out var tonos, out _);
        var senal = ModuladorFst4.Sintetizar(p, tonos, 1500, 12000, amplitud: 0.5);
        senal.Should().HaveCount(160 * 720);
        var potencias = new List<double>();
        for (var desde = 720; desde + 720 < senal.Length - 720; desde += 720)
            potencias.Add(GeneradorDeSenal.PotenciaMedia(senal.AsSpan(desde, 720)));
        potencias.Max().Should().BeApproximately(potencias.Min(), 0.01 * potencias.Max());

        // Espectro: toda la energia entre el tono cero y el tres, mas un poco de falda.
        var magnitudes = new float[(1 << 16) / 2 + 1];
        Senal.Fft.MagnitudesDeSenalReal(senal.AsSpan(0, 1 << 16), 1 << 16, magnitudes);
        var hzPorCasilla = 12000.0 / (1 << 16);
        double dentro = 0, fuera = 0;
        for (var c = 0; c < magnitudes.Length; c++)
        {
            var f = c * hzPorCasilla;
            var e = (double)magnitudes[c] * magnitudes[c];
            if (f > 1500 - 20 && f < 1500 + (3 * 16.667) + 20) dentro += e; else fuera += e;
        }
        (dentro / (dentro + fuera)).Should().BeGreaterThan(0.98);
    }

    [Theory]
    [InlineData(15, 1500.0, 0.0)]
    [InlineData(30, 800.0, 0.3)]
    [InlineData(60, 2200.0, -0.4)]
    [InlineData(120, 1450.0, 0.7)]
    public void UnaSenalLimpiaSeDecodificaEnCadaPeriodo(int periodo, double tono, double desfase)
    {
        const string Texto = "EA1ABC EA8DLF -07";
        var ventana = Ventana(periodo, esFst4w: false, Texto, tono, desfase, decibelios: 10, new Random(periodo));
        var decodificador = new DecodificadorFst4(periodo, TablasDelRepositorio.Fst4, esFst4w: false);
        var resultado = decodificador.Decodificar(ventana, 12000, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());

        resultado.Decodificaciones.Should().ContainSingle();
        var d = resultado.Decodificaciones[0];
        d.Texto.Should().Be(Texto);
        d.Modo.Should().Be(ModoDelModem.Fst4);
        d.TonoHz.Should().BeCloseTo((int)Math.Round(tono), 2);
        d.DesfaseSegundos.Should().BeApproximately(desfase, 0.1);
        d.Decibelios.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Fst4wDecodificaUnaBalizaLimpia()
    {
        const string Texto = "EA8DLF IL18 37";
        var ventana = Ventana(120, esFst4w: true, Texto, 1520, 0.2, decibelios: 0, new Random(9));
        var decodificador = new DecodificadorFst4(120, TablasDelRepositorio.Fst4w, esFst4w: true);
        var resultado = decodificador.Decodificar(ventana, 12000, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
        resultado.Decodificaciones.Should().ContainSingle();
        var d = resultado.Decodificaciones[0];
        d.Texto.Should().Be(Texto);
        d.Modo.Should().Be(ModoDelModem.Fst4w);
        d.Llamante.Valor.Should().Be("EA8DLF");
        d.Locator.Valor.Should().Be("IL18");
    }

    [Fact]
    public void ElModoCambiaDePeriodoYGeneraAudio()
    {
        var modo = new ModoFst4(TablasDelRepositorio.Fst4, esFst4w: false, periodoSegundos: 60);
        modo.Periodo.Should().Be(TimeSpan.FromSeconds(60));
        modo.CambiarPeriodo(15);
        modo.Periodo.Should().Be(TimeSpan.FromSeconds(15));
        modo.FrecuenciaDeAnalisis.Should().Be(8533);
        var audio = modo.Generar("CQ EA8DLF IL18", 1500, 48000);
        audio.Should().HaveCount(160 * 2880);

        var w = new ModoFst4(TablasDelRepositorio.Fst4w, esFst4w: true, periodoSegundos: 120);
        w.Modo.Should().Be(ModoDelModem.Fst4w);
        var actoMalo = () => w.CambiarPeriodo(60);
        actoMalo.Should().Throw<ArgumentOutOfRangeException>();
        var actoTexto = () => w.Generar("CQ EA8DLF IL18", 1500, 12000);
        actoTexto.Should().Throw<FormatException>();
    }

    [Fact]
    public void ConRuidoPuroNoSaleNada()
    {
        var decodificador = new DecodificadorFst4(15, TablasDelRepositorio.Fst4, esFst4w: false);
        for (var v = 0; v < 10; v++)
        {
            var ventana = new float[15 * 12000];
            GeneradorDeSenal.AnadirRuido(ventana, 0.01, 0, 12000, new Random(700 + v));
            var resultado = decodificador.Decodificar(ventana, 12000, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos());
            resultado.Decodificaciones.Should().BeEmpty();
        }
    }

    /// <summary>Una ventana del periodo, a 12000 muestras por segundo, con la senal en su sitio nominal mas el desfase.</summary>
    public static float[] Ventana(int periodo, bool esFst4w, string texto, double tonoHz, double desfaseSegundos, double decibelios, Random azar)
    {
        var p = ParametrosFst4.DelPeriodo(periodo);
        var codificador = esFst4w ? CodificadorW : Codificador;
        if (!codificador.TryCodificar(texto, out var tonos, out var motivo)) throw new InvalidOperationException(motivo);
        const int Fs = 12000;
        var senal = ModuladorFst4.Sintetizar(p, tonos, tonoHz, Fs, amplitud: 0.35);
        var ventana = new float[periodo * Fs];
        var comienzo = (int)Math.Round((p.ComienzoNominalSegundos + desfaseSegundos) * Fs);
        for (var i = 0; i < senal.Length; i++)
        {
            var j = comienzo + i;
            if (j >= 0 && j < ventana.Length) ventana[j] += senal[i];
        }
        GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(senal), decibelios, Fs, azar);
        return ventana;
    }
}
