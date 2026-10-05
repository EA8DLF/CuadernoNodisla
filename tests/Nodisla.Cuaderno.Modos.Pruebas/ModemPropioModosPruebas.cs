using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Marco;
using Nodisla.Cuaderno.Modos.Modem;
using Nodisla.Cuaderno.Modos.Q65;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// El modem despachando por el registro de modos: WSPR y Q65 por el mismo camino en vivo que
/// FT8, la alineacion de las ventanas largas y el silencio del comienzo al emitir.
/// </summary>
/// <remarks>
/// Nada de esto abre una tarjeta ni acciona un PTT: la entrada, la salida y el vigilante son de
/// mentira y solo apuntan lo que se les da.
/// </remarks>
public class ModemPropioModosPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();
    private static readonly TimeSpan Paciencia = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ElModemOfreceLosModosRegistrados()
    {
        await using var modem = new ModemPropio(Tablas, new RelojParado(DateTimeOffset.UnixEpoch));
        modem.ModosDisponibles.Should().Equal(
            ModoDelModem.Ft8, ModoDelModem.Ft4, ModoDelModem.Wspr, ModoDelModem.Jt65, ModoDelModem.Jt9,
            ModoDelModem.Q65, ModoDelModem.Msk144, ModoDelModem.Fst4, ModoDelModem.Fst4w);
        modem.ModosDisponibles.Should().HaveCount(Enum.GetValues<ModoDelModem>().Length, "los nueve están registrados");
        ((IModemPropio)modem).ModosDisponibles.Should().Equal(modem.ModosDisponibles, "la pantalla lee el puerto, no la clase");
    }

    [Fact]
    public async Task UnModoNoRegistradoSeRechaza()
    {
        var soloFt8 = new RegistroDeModos().Anadir(new Nodisla.Cuaderno.Modos.Ft8.ModoFt8(ModoDelModem.Ft8, Tablas, new Nodisla.Cuaderno.Modos.Ft8.CatalogoDeIndicativos()));
        await using var modem = new ModemPropio(Tablas, new RelojParado(DateTimeOffset.UnixEpoch), new EntradaDeMentira(), modos: soloFt8);
        modem.ModosDisponibles.Should().Equal(ModoDelModem.Ft8);
        var escuchar = async () => await modem.EscucharAsync(ModoDelModem.Wspr);
        await escuchar.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ElComienzoDeLaSenalCuadraConElQueDeclaraCadaModo()
    {
        var modos = ModemPropio.ModosDeSerie(Tablas);
        TimeSpan Comienzo(ModoDelModem m) => modos.Obtener(m).ComienzoDeLaSenal;

        Comienzo(ModoDelModem.Ft8).Should().Be(TimeSpan.FromSeconds(0.5));
        Comienzo(ModoDelModem.Ft4).Should().Be(TimeSpan.FromSeconds(0.5));
        Comienzo(ModoDelModem.Wspr).Should().Be(TimeSpan.FromSeconds(1));
        Comienzo(ModoDelModem.Jt65).Should().Be(((Nodisla.Cuaderno.Modos.Jt65.ModoJt65)modos.Obtener(ModoDelModem.Jt65)).ComienzoNominal);
        Comienzo(ModoDelModem.Jt9).Should().Be(((Nodisla.Cuaderno.Modos.Jt9.ModoJt9)modos.Obtener(ModoDelModem.Jt9)).ComienzoNominal);
        Comienzo(ModoDelModem.Q65).Should().Be(TimeSpan.FromSeconds(((ModoQ65)modos.Obtener(ModoDelModem.Q65)).ComienzoNominalSegundos));
        Comienzo(ModoDelModem.Fst4).Should().Be(TimeSpan.FromSeconds(1), "FST4 de 60 s empieza en el segundo 1");
        Comienzo(ModoDelModem.Fst4w).Should().Be(TimeSpan.FromSeconds(1));

        modos.Obtener(ModoDelModem.Jt65).FrecuenciaDeAnalisis.Should().Be(11025);
        modos.Obtener(ModoDelModem.Jt9).FrecuenciaDeAnalisis.Should().Be(12000);
    }

    [Theory]
    [InlineData(15, "2026-09-27T10:00:37Z", "2026-09-27T10:00:30Z")]
    [InlineData(120, "2026-09-27T10:01:59Z", "2026-09-27T10:00:00Z")]
    [InlineData(120, "2026-09-27T10:02:00Z", "2026-09-27T10:02:00Z")]
    [InlineData(300, "2026-09-27T10:09:59Z", "2026-09-27T10:05:00Z")]
    [InlineData(300, "2026-09-27T10:10:01Z", "2026-09-27T10:10:00Z")]
    public void LasVentanasLargasCaenEnMinutosParesYMultiplosDeCinco(int periodo, string instante, string esperado)
    {
        IModoDigital.ComienzoDeVentana(DateTimeOffset.Parse(instante), TimeSpan.FromSeconds(periodo))
            .Should().Be(DateTimeOffset.Parse(esperado));
    }

    [Fact]
    public async Task WsprDecodificaPorElCaminoEnVivo()
    {
        var modo = new ModoWspr();
        var senal = modo.Generar("EA8DLF IL18 37", 1500, DobleRemuestreoPruebas.FrecuenciaDelCodec);
        var ventana = await EscucharUnaVentanaAsync(ModoDelModem.Wspr, TimeSpan.FromSeconds(120), senal,
            comienzoDeLaSenal: 1.3, decibelios: -22, semilla: 5);

        ventana.Decodificaciones.Should().ContainSingle(d => d.Texto == "EA8DLF IL18 37");
        var d = ventana.Decodificaciones[0];
        d.Modo.Should().Be(ModoDelModem.Wspr);
        d.DesfaseSegundos.Should().BeApproximately(0.3, 0.15);
        d.TonoHz.Should().BeInRange(1499, 1501);
    }

    [Fact]
    public async Task Q65DecodificaPorElCaminoEnVivo()
    {
        var modo = new ModoQ65(ParametrosDeQ65.De(60, SubmodoDeQ65.A), TablasDeQ65.Cargar());
        var senal = modo.Generar("EA8DLF K1ABC FN42", 1500, DobleRemuestreoPruebas.FrecuenciaDelCodec);
        var ventana = await EscucharUnaVentanaAsync(ModoDelModem.Q65, TimeSpan.FromSeconds(60), senal,
            comienzoDeLaSenal: modo.ComienzoNominalSegundos, decibelios: -15, semilla: 6);

        ventana.Decodificaciones.Should().ContainSingle(d => d.Texto == "EA8DLF K1ABC FN42");
        ventana.Decodificaciones[0].Modo.Should().Be(ModoDelModem.Q65);
    }

    [Theory]
    [InlineData(ModoDelModem.Ft8, "CQ EA8DLF IL18", 0.5)]
    [InlineData(ModoDelModem.Wspr, "EA8DLF IL18 37", 1.0)]
    [InlineData(ModoDelModem.Q65, "CQ EA8DLF IL18", 1.0)]
    [InlineData(ModoDelModem.Jt65, "CQ EA8DLF IL18", 1.0)]
    [InlineData(ModoDelModem.Jt9, "CQ EA8DLF IL18", 1.0)]
    public async Task AlEmitirAlPrincipioDeLaVentanaSeGuardaElSilencioDelComienzo(ModoDelModem modo, string texto, double segundos)
    {
        var ventana = DateTimeOffset.Parse("2026-09-27T10:00:00Z");
        var salida = new SalidaDeMentira();
        var vigilante = new VigilanteDeMentira();
        await using var modem = new ModemPropio(Tablas, new RelojParado(ventana), new EntradaDeMentira(), salida, vigilante);
        await modem.EscucharAsync(modo);
        await modem.PararAsync();

        await modem.EmitirAsync(texto, 1500);

        vigilante.Pedidas.Should().Be(1);
        var muestras = salida.Reproducido!;
        var silencio = (int)Math.Round(segundos * 48000);
        muestras.AsSpan(0, silencio).ToArray().Should().OnlyContain(m => m == 0f);
        muestras.AsSpan(silencio, 4800).ToArray().Should().Contain(m => Math.Abs(m) > 0.05f);
    }

    /// <summary>
    /// Si la salida se abrió a otra frecuencia que 48.000 (p. ej. 96.000, puesta en Ajustes), la
    /// señal tiene que generarse a ESA frecuencia, no a un 48.000 fijo.
    /// </summary>
    /// <remarks>
    /// Comprobado el 05-10-2026: antes de este arreglo, <c>EmitirAsync</c> generaba siempre a
    /// 48.000 sin mirar a qué frecuencia se había abierto la salida de verdad. Si la salida
    /// estaba a 96.000, la salida de audio remuestreaba una señal de 48.000 como si fuera de
    /// 96.000: la mitad de las muestras que tocaban, al doble de velocidad de la que tocaba, y lo
    /// que sonaba era un destrozo de aliasing, no un tono limpio.
    /// </remarks>
    [Fact]
    public async Task LaSenalSeGeneraALaFrecuenciaConLaQueSeAbrioLaSalida()
    {
        var ventana = DateTimeOffset.Parse("2026-09-27T10:00:00Z");
        var salida = new SalidaDeMentira();
        await salida.AbrirAsync("tarjeta", 96000);
        var vigilante = new VigilanteDeMentira();
        await using var modem = new ModemPropio(Tablas, new RelojParado(ventana), new EntradaDeMentira(), salida, vigilante);
        await modem.EscucharAsync(ModoDelModem.Ft8);
        await modem.PararAsync();

        await modem.EmitirAsync("CQ EA8DLF IL18", 1500);

        var muestras = salida.Reproducido!;
        // Medio segundo de silencio A 96.000, no a 48.000: con el fallo de antes salian la mitad
        // de muestras (el silencio en 48.000) y el mensaje quedaba comprimido al doble de tono.
        var silencioA96000 = (int)Math.Round(0.5 * 96000);
        muestras.Length.Should().BeGreaterThan(silencioA96000, "a 96.000 el mensaje entero ocupa muchas mas muestras que a 48.000");
        muestras.AsSpan(0, silencioA96000).ToArray().Should().OnlyContain(m => m == 0f);
        muestras.AsSpan(silencioA96000, 9600).ToArray().Should().Contain(m => Math.Abs(m) > 0.05f);
    }

    /// <summary>
    /// Pasa por el modem, en bloques de 50 ms a 96000 Hz subidos desde 44100 como hace la captura,
    /// cinco segundos de ruido, una ventana con la senal y unos segundos de la siguiente.
    /// </summary>
    private static async Task<VentanaDecodificada> EscucharUnaVentanaAsync(
        ModoDelModem modo, TimeSpan periodo, float[] senal, double comienzoDeLaSenal, double decibelios, int semilla)
    {
        const int Codec = DobleRemuestreoPruebas.FrecuenciaDelCodec;
        const int Configurada = DobleRemuestreoPruebas.FrecuenciaConfigurada;
        const double Antes = 5;
        const double Despues = 3;

        var total = (int)Math.Round((Antes + periodo.TotalSeconds + Despues) * Codec);
        var flujo = new float[total];
        var comienzo = (int)Math.Round((Antes + comienzoDeLaSenal) * Codec);
        senal.CopyTo(flujo, comienzo);
        GeneradorDeSenal.AnadirRuido(flujo, GeneradorDeSenal.PotenciaMedia(senal), decibelios, Codec, new Random(semilla));
        var a96000 = DobleRemuestreoPruebas.InterpolacionLinealComoAudioCaptura(flujo, Codec, Configurada);

        // Una ventana que empieza en minuto par, que vale para las de 60 y las de 120 s.
        var ventanaUtc = DateTimeOffset.Parse("2026-09-27T10:00:00Z");
        var entrada = new EntradaDeMentira();
        await using var modem = new ModemPropio(Tablas, new RelojParado(ventanaUtc.AddSeconds(-Antes)), entrada);

        var lista = new TaskCompletionSource<VentanaDecodificada>(TaskCreationOptions.RunContinuationsAsynchronously);
        modem.VentanaLista += (_, v) => { if (v.VentanaUtc == ventanaUtc) lista.TrySetResult(v); };

        await modem.EscucharAsync(modo);
        entrada.Emitir(a96000, Configurada, ventanaUtc.AddSeconds(-Antes), TimeSpan.FromMilliseconds(50));
        var resultado = await lista.Task.WaitAsync(Paciencia);
        await modem.PararAsync();
        return resultado;
    }

    private sealed class EntradaDeMentira : IEntradaDeAudio
    {
        public IReadOnlyList<DispositivoDeAudio> Dispositivos { get; } = [];

        public DispositivoDeAudio? Abierto => null;

        public double Nivel => 0;

        public event EventHandler<BloqueDeAudio>? BloqueCapturado;

        public event EventHandler<HuecoDeAudio>? MuestrasPerdidas;

        public void Emitir(float[] muestras, int frecuencia, DateTimeOffset inicio, TimeSpan porBloque)
        {
            var n = (int)Math.Round(porBloque.TotalSeconds * frecuencia);
            for (var desde = 0; desde + n <= muestras.Length; desde += n)
                BloqueCapturado?.Invoke(this, new BloqueDeAudio(
                    muestras.AsMemory(desde, n).ToArray(), frecuencia, inicio.AddSeconds((double)desde / frecuencia)));
            _ = MuestrasPerdidas;
        }

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default) => Task.CompletedTask;

        public Task CerrarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SalidaDeMentira : ISalidaDeAudio
    {
        public float[]? Reproducido { get; private set; }

        public DateTimeOffset? UltimoAvanceUtc => null;

        public IReadOnlyList<DispositivoDeAudio> Dispositivos { get; } = [];

        public DispositivoDeAudio? Abierto => null;

        public int FrecuenciaDeMuestreo { get; private set; } = 48000;

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default)
        {
            FrecuenciaDeMuestreo = frecuenciaDeMuestreo;
            return Task.CompletedTask;
        }

        public Task CerrarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default)
        {
            Reproducido = muestras.ToArray();
            return Task.CompletedTask;
        }

        public Task SilenciarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class VigilanteDeMentira : IVigilantePtt
    {
        public int Pedidas { get; private set; }

        public TimeSpan TiempoMaximo => TimeSpan.FromMinutes(3);

        public TimeSpan TiempoSinLatido => TimeSpan.FromSeconds(3);

        public bool EnAntena => false;

        public event EventHandler<MotivoDeSuelta>? PttSoltado;

        public Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default)
        {
            Pedidas++;
            _ = PttSoltado;
            return Task.FromResult<ITransmisionEnCurso>(new TransmisionDeMentira());
        }

        public Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico) => Task.CompletedTask;

        private sealed class TransmisionDeMentira : ITransmisionEnCurso
        {
            public bool EnAntena => false;

            public void Latir()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class RelojParado(DateTimeOffset ahora) : IRelojDelModem
    {
        public DateTimeOffset Ahora { get; } = ahora;

        public DesvioDelReloj Desvio { get; } = new(0, "pruebas", DateTimeOffset.UnixEpoch, EsFiable: true);

        public EstadoDelReloj Estado => new(Desvio, CalidadDelReloj.Bien, "En hora.", string.Empty, "0 ms");

        public event EventHandler<EstadoDelReloj>? DesvioMedido;

        public Task<DesvioDelReloj> MedirAsync(bool forzar = false, CancellationToken ct = default)
        {
            DesvioMedido?.Invoke(this, Estado);
            return Task.FromResult(Desvio);
        }

        public DateTimeOffset ProximaVentana(TimeSpan periodo) => Ahora + periodo;
    }
}
