using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Modem;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Pruebas;

/// <summary>
/// Las seis funciones nuevas del modem propio pedidas el 08-10-2026: AGCc, Filtrar, Tune, modo
/// SWL, Bypass y el «Sincronizar» manual. De estas seis, solo AGCc, Filtrar, Tune y Sincronizar
/// tocan el modem/DSP de verdad -las otras dos (SWL y Bypass) son enteramente del viewmodel y se
/// prueban en <c>Nodisla.Cuaderno.Ui.Pruebas</c>-.
/// </summary>
public class ModemPropioFuncionesPropiasPruebas
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();

    // ── Filtrar: paso de banda sobre el audio real ──────────────────────────

    [Fact]
    public void FiltroPasoBandaDejaPasarElCentroYAtenuaFueraDeBanda()
    {
        const int Frecuencia = 48000;
        var filtro = new FiltroPasoBanda();
        filtro.Ajustar(200, 2900, Frecuencia);

        var dentro = Tono(1000, Frecuencia, 0.2, 1.0);
        filtro.Procesar(dentro);
        var rmsDentro = Rms(dentro.AsSpan(dentro.Length / 2));

        filtro.Reiniciar();
        var fuera = Tono(60, Frecuencia, 0.2, 1.0);
        filtro.Procesar(fuera);
        var rmsFuera = Rms(fuera.AsSpan(fuera.Length / 2));

        // Dentro de banda, el seno sobrevive casi entero; muy por debajo del corte bajo (60 Hz,
        // frente a 200), se come la mayor parte.
        rmsDentro.Should().BeGreaterThan(0.5, "1.000 Hz está en el centro de la banda que deja pasar");
        rmsFuera.Should().BeLessThan(rmsDentro * 0.5, "60 Hz está muy por debajo del corte de 200 Hz");
    }

    // ── AGCc: nivela el audio hacia un objetivo ──────────────────────────────

    [Fact]
    public void AgcDigitalLevantaUnaSenalFlojaHaciaElObjetivo()
    {
        const int Frecuencia = 48000;
        var agc = new AgcDigital();
        var floja = Tono(700, Frecuencia, 1.0, 0.01);
        var picoAntes = Pico(floja);

        agc.Procesar(floja, Frecuencia);
        var picoDespues = Pico(floja.AsSpan(floja.Length / 2)); // tras converger el envolvente

        picoDespues.Should().BeGreaterThan(picoAntes * 5, "una señal floja (pico 0,01) tiene que subir mucho hacia el objetivo");
        floja.ToArray().Should().OnlyContain(m => m >= -1f && m <= 1f, "nunca se sale de lo que admite el audio");
    }

    [Fact]
    public void AgcDigitalNuncaRecortaPorEncimaDeUno()
    {
        const int Frecuencia = 48000;
        var agc = new AgcDigital();
        var fuerte = Tono(700, Frecuencia, 1.0, 3.0); // mas alla de lo que admite un audio de verdad

        agc.Procesar(fuerte, Frecuencia);

        fuerte.ToArray().Should().OnlyContain(m => m >= -1f && m <= 1f);
    }

    // ── Tune: tono puro y continuo ───────────────────────────────────────────

    [Fact]
    public void GenerarTonoContinuoSacaUnSegundoDelTonoPedido()
    {
        const int Frecuencia = 48000;
        const int TonoHz = 700;
        var senal = ModemPropio.GenerarTonoContinuo(TonoHz, Frecuencia, 0.5);

        senal.Should().HaveCount(Frecuencia, "es un segundo exacto, a esta frecuencia de muestreo");
        senal[0].Should().BeApproximately(0f, 1e-4f, "un seno puro empieza en fase cero");
        Pico(senal).Should().BeApproximately(0.5, 0.01);

        // Cuenta de cruces por cero: en un seno de TonoHz hercios durante un segundo hay
        // aproximadamente 2*TonoHz cruces.
        var cruces = 0;
        for (var i = 1; i < senal.Length; i++)
            if (Math.Sign(senal[i]) != Math.Sign(senal[i - 1]) && senal[i - 1] != 0) cruces++;
        cruces.Should().BeCloseTo(2 * TonoHz, 4);
    }

    [Fact]
    public async Task EmitirTonoAsyncSuenaEnBucleHastaQueSeCancelaYSueltaElPtt()
    {
        var salida = new SalidaDeTonoDeMentira();
        var vigilante = new VigilanteDeMentira();
        await using var modem = new ModemPropio(Tablas, new RelojParado(DateTimeOffset.UnixEpoch), new EntradaDeMentira(), salida, vigilante);

        using var cts = new CancellationTokenSource();
        var tarea = modem.EmitirTonoAsync(700, cts.Token);

        await salida.SegundaReproduccion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        modem.EstaEmitiendo.Should().BeTrue("mientras suena el tono, el modem está emitiendo");
        await cts.CancelAsync();
        await tarea;

        vigilante.Pedidas.Should().Be(1, "un único «pedir antena», no una por cada vuelta del bucle");
        salida.VecesSilenciado.Should().BeGreaterThan(0, "al parar, se corta lo que sonaba");
        modem.EstaEmitiendo.Should().BeFalse();
        salida.UltimoReproducido.Should().NotBeNull();
        salida.UltimoReproducido!.Length.Should().Be(48000);
    }

    // ── Sincronizar: ajuste manual de la alineación de ventana ───────────────

    [Fact]
    public async Task AjusteDeVentanaDecideAQueVentanaPerteneceElAudioQueLlegaJustoAntesDelLimite()
    {
        var ventanaRecibida = await VentanaQueCierraConAjuste(TimeSpan.FromSeconds(0.2));

        // El bloque llega 0,1 s ANTES del límite real de las 10:00:15; sin ajuste caería en la
        // ventana de las 10:00:00. Con +0,2 s de ajuste, el modem lo cuenta ya en la de las
        // 10:00:15: es justo lo que corrige «Sincronizar» cuando el desfase es sistemático.
        ventanaRecibida.Should().Be(DateTimeOffset.Parse("2026-09-27T10:00:15Z"));
    }

    [Fact]
    public async Task SinAjusteDeVentanaElMismoAudioCaeEnLaVentanaAnterior()
    {
        var ventanaRecibida = await VentanaQueCierraConAjuste(TimeSpan.Zero);

        ventanaRecibida.Should().Be(DateTimeOffset.Parse("2026-09-27T10:00:00Z"));
    }

    private static async Task<DateTimeOffset?> VentanaQueCierraConAjuste(TimeSpan ajuste)
    {
        const int Frecuencia = 48000;
        var comienzoDelBloque = DateTimeOffset.Parse("2026-09-27T10:00:14.9Z");

        var entrada = new EntradaDeMentira();
        await using var modem = new ModemPropio(Tablas, new RelojParado(comienzoDelBloque), entrada)
        {
            AjusteDeVentana = ajuste,
        };

        DateTimeOffset? ventanaRecibida = null;
        var lista = new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        modem.VentanaLista += (_, v) =>
        {
            ventanaRecibida ??= v.VentanaUtc;
            lista.TrySetResult(v.VentanaUtc);
        };

        await modem.EscucharAsync(ModoDelModem.Ft8);
        var muestras = new float[(int)(15.3 * Frecuencia)];
        entrada.Emitir(muestras, Frecuencia, comienzoDelBloque, TimeSpan.FromMilliseconds(50));
        await lista.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await modem.PararAsync();

        return ventanaRecibida;
    }

    // ── Utilidades de la señal, comunes a estas pruebas ─────────────────────

    private static float[] Tono(double hz, int frecuenciaDeMuestreo, double segundos, double amplitud)
    {
        var muestras = new float[(int)(segundos * frecuenciaDeMuestreo)];
        for (var i = 0; i < muestras.Length; i++)
            muestras[i] = (float)(amplitud * Math.Sin(2.0 * Math.PI * hz * i / frecuenciaDeMuestreo));
        return muestras;
    }

    private static double Rms(ReadOnlySpan<float> muestras)
    {
        double suma = 0;
        foreach (var m in muestras) suma += (double)m * m;
        return Math.Sqrt(suma / muestras.Length);
    }

    private static double Pico(ReadOnlySpan<float> muestras)
    {
        double pico = 0;
        foreach (var m in muestras) pico = Math.Max(pico, Math.Abs(m));
        return pico;
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

    private sealed class SalidaDeTonoDeMentira : ISalidaDeAudio
    {
        public TaskCompletionSource SegundaReproduccion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _veces;

        public float[]? UltimoReproducido { get; private set; }
        public int VecesSilenciado { get; private set; }
        public DateTimeOffset? UltimoAvanceUtc => null;
        public IReadOnlyList<DispositivoDeAudio> Dispositivos { get; } = [];
        public DispositivoDeAudio? Abierto => null;
        public int FrecuenciaDeMuestreo => 48000;

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default) => Task.CompletedTask;
        public Task CerrarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default)
        {
            UltimoReproducido = muestras.ToArray();
            _veces++;
            if (_veces == 2) SegundaReproduccion.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SilenciarAsync(CancellationToken ct = default)
        {
            VecesSilenciado++;
            return Task.CompletedTask;
        }

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
            public void Latir() { }
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
