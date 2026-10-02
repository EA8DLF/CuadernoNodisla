using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Audio.Procesado;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// El voice keyer sobre la fonia, solo con dobles: un camino de audio que se «bombea» a mano,
/// un vigilante de mentira y un reloj quieto. Nada transmite ni abre una tarjeta.
/// </summary>
public sealed class VoiceKeyerPruebas
{
    private const int F = 48000;
    private static readonly DateTimeOffset Origen = new(2026, 10, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task El_mensaje_sale_por_el_vigilante_y_solo_suena_con_el_ptt_arriba()
    {
        var (control, vigilante, tx, _) = Montar();
        var mensaje = Tono(segundos: 1);
        var bloqueAntesDelPtt = Array.Empty<float>();
        vigilante.AntesDePedir = () => bloqueAntesDelPtt = tx.Bombear();

        await control.EmitirMensajeAsync(mensaje, "micro", "codec-out");

        vigilante.Motivos.Should().ContainSingle().Which.Should().Contain("mensaje");
        bloqueAntesDelPtt.Should().OnlyContain(m => m == 0f, "antes del PTT el camino saca silencio, ni voz ni mensaje");
        control.EnviandoMensaje.Should().BeTrue();

        var primero = tx.Bombear();
        primero.Should().Equal(mensaje.Muestras.AsSpan(0, primero.Length).ToArray(), "con el PTT arriba suena el mensaje, no el microfono");
    }

    [Fact]
    public async Task Al_acabar_el_mensaje_baja_el_ptt_solo_y_el_microfono_vuelve_a_ser_el_microfono()
    {
        var (control, vigilante, tx, _) = Montar();
        var fin = Fin(control);
        await control.EmitirMensajeAsync(Tono(segundos: 1), "micro", "codec-out");

        // Un segundo de mensaje y 300 ms de cola, en bloques de 10 ms.
        for (var i = 0; i < 140 && !fin.Task.IsCompleted; i++) tx.Bombear();

        (await fin.Task.WaitAsync(TimeSpan.FromSeconds(10))).Motivo.Should().Be(MotivoDeSuelta.Normal);
        vigilante.EnAntena.Should().BeFalse();
        tx.AntesDeLaGanancia.Should().BeNull("la siguiente pasada tiene que ser la voz del micro");
        tx.EstaAbierto.Should().BeFalse();
        control.EnviandoMensaje.Should().BeFalse();
    }

    [Fact]
    public async Task Un_mensaje_mas_largo_que_el_tope_no_llega_a_pedir_antena()
    {
        var (control, vigilante, tx, _) = Montar(new OpcionesDeFonia { TiempoMaximo = TimeSpan.FromSeconds(10) });

        var intento = () => control.EmitirMensajeAsync(Tono(segundos: 12), "micro", "codec-out");

        await intento.Should().ThrowAsync<InvalidOperationException>();
        vigilante.Motivos.Should().BeEmpty();
        tx.EstaAbierto.Should().BeFalse();
        tx.AntesDeLaGanancia.Should().BeNull();
    }

    [Fact]
    public async Task El_tiempo_maximo_corta_el_mensaje_a_media_pasada()
    {
        var (control, vigilante, tx, reloj) = Montar();
        var fin = Fin(control);
        await control.EmitirMensajeAsync(Tono(segundos: 20), "micro", "codec-out");

        // El vigilante baja su tope a mitad del mensaje: manda el menor.
        vigilante.TiempoMaximo = TimeSpan.FromSeconds(5);
        for (var s = 0; s < 6; s++)
        {
            reloj.Avanzar(TimeSpan.FromSeconds(1));
            tx.UltimoAvanceUtc = reloj.GetUtcNow();
            control.Vigilar();
        }

        (await fin.Task.WaitAsync(TimeSpan.FromSeconds(10))).Motivo.Should().Be(MotivoDeSuelta.TiempoAgotado);
        vigilante.EnAntena.Should().BeFalse();
        tx.AntesDeLaGanancia.Should().BeNull();
    }

    [Fact]
    public async Task El_panico_del_vigilante_corta_el_mensaje()
    {
        var (control, vigilante, tx, _) = Montar();
        var fin = Fin(control);
        await control.EmitirMensajeAsync(Tono(segundos: 5), "micro", "codec-out");
        tx.Bombear();

        await vigilante.SoltarYaAsync(MotivoDeSuelta.Panico);

        (await fin.Task.WaitAsync(TimeSpan.FromSeconds(10))).Motivo.Should().Be(MotivoDeSuelta.Panico);
        tx.Bombear().Should().OnlyContain(m => m == 0f);
    }

    [Fact]
    public async Task No_pisa_otra_transmision()
    {
        var (control, vigilante, _, _) = Montar();
        await using var delModem = await vigilante.PedirAntenaAsync("FT8");

        var intento = () => control.EmitirMensajeAsync(Tono(1), "micro", "codec-out");

        await intento.Should().ThrowAsync<InvalidOperationException>();
        vigilante.Motivos.Should().ContainSingle();
    }

    [Fact]
    public async Task El_mensaje_pasa_por_el_procesado_del_micro_y_no_sube_del_techo()
    {
        var (control, _, tx, _) = Montar();
        control.Microfono.Activo = true;
        control.Microfono.TechoDb = -6;
        tx.Ganancia = 4f; // el operador se paso con la ganancia
        await control.EmitirMensajeAsync(Tono(segundos: 1, amplitud: 0.5), "micro", "codec-out");

        var techo = (float)Math.Pow(10, -6 / 20.0);
        for (var i = 0; i < 50; i++) tx.Bombear().Should().OnlyContain(m => Math.Abs(m) <= techo + 1e-6f);
    }

    [Fact]
    public void Sin_armar_el_reproductor_saca_silencio_y_no_avanza()
    {
        var r = new ReproductorEnElAire(Tono(1));
        var bloque = Enumerable.Repeat(0.7f, 480).ToArray();

        r.Procesar(bloque, F);

        bloque.Should().OnlyContain(m => m == 0f);
        r.Avance.Should().Be(0);
    }

    private static AudioEnMemoria Tono(int segundos, double amplitud = 0.3)
    {
        var m = new float[segundos * F];
        for (var i = 0; i < m.Length; i++) m[i] = (float)(amplitud * Math.Sin(2 * Math.PI * 800 * i / F));
        return new AudioEnMemoria(m, F);
    }

    private static TaskCompletionSource<FinDeFonia> Fin(ControlDeFonia control)
    {
        var fin = new TaskCompletionSource<FinDeFonia>(TaskCreationOptions.RunContinuationsAsynchronously);
        control.TransmisionTerminada += (_, f) => fin.TrySetResult(f);
        return fin;
    }

    private static (ControlDeFonia, Vigilante, PuenteQueBombea, Reloj) Montar(OpcionesDeFonia? opciones = null)
    {
        var reloj = new Reloj(Origen);
        var vigilante = new Vigilante();
        var tx = new PuenteQueBombea();
        var control = new ControlDeFonia(vigilante, new PuenteQueBombea(), tx, opciones, reloj);
        return (control, vigilante, tx, reloj);
    }

    /// <summary>
    /// Camino de mentira que hace lo mismo que el de verdad con cada bloque: antes de la
    /// ganancia, ganancia, despues de la ganancia y silencio. El «microfono» mete 0,7 constante.
    /// </summary>
    internal sealed class PuenteQueBombea : IPuenteDeAudio
    {
        public bool EstaAbierto { get; private set; }

        public float Ganancia { get; set; } = 1f;

        public bool Silenciado { get; set; }

        public double Nivel => 0;

        public bool Saturando => false;

        public DateTimeOffset? UltimoAvanceUtc { get; set; }

        public IProcesadorDeAudio? AntesDeLaGanancia { get; set; }

        public IProcesadorDeAudio? TrasLaGanancia { get; set; }

        public event EventHandler<Exception>? Fallo;

        public float[] Bombear(int muestras = 480)
        {
            var bloque = Enumerable.Repeat(0.7f, muestras).ToArray();
            AntesDeLaGanancia?.Procesar(bloque, F);
            for (var i = 0; i < bloque.Length; i++) bloque[i] *= Ganancia;
            TrasLaGanancia?.Procesar(bloque, F);
            if (Silenciado) Array.Clear(bloque);
            return bloque;
        }

        public void Romper(Exception fallo) => Fallo?.Invoke(this, fallo);

        public Task AbrirAsync(string idEntrada, string idSalida, CancellationToken ct = default)
        {
            EstaAbierto = true;
            return Task.CompletedTask;
        }

        public Task CerrarAsync()
        {
            EstaAbierto = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new(CerrarAsync());
    }

    private sealed class Vigilante : IVigilantePtt
    {
        private Transmision? _enCurso;

        public List<string> Motivos { get; } = [];

        public Action? AntesDePedir { get; set; }

        public TimeSpan TiempoMaximo { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan TiempoSinLatido => TimeSpan.FromSeconds(15);

        public bool EnAntena => _enCurso is not null;

        public event EventHandler<MotivoDeSuelta>? PttSoltado;

        public Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default)
        {
            if (EnAntena) throw new InvalidOperationException("Ya hay una transmisión en curso.");
            AntesDePedir?.Invoke();
            Motivos.Add(motivo);
            _enCurso = new Transmision(this);
            return Task.FromResult<ITransmisionEnCurso>(_enCurso);
        }

        public Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico)
        {
            Soltar(motivo);
            return Task.CompletedTask;
        }

        private void Soltar(MotivoDeSuelta motivo)
        {
            if (_enCurso is null) return;
            _enCurso = null;
            PttSoltado?.Invoke(this, motivo);
        }

        private sealed class Transmision(Vigilante vigilante) : ITransmisionEnCurso
        {
            public bool EnAntena => ReferenceEquals(vigilante._enCurso, this);

            public void Latir()
            {
            }

            public ValueTask DisposeAsync()
            {
                if (EnAntena) vigilante.Soltar(MotivoDeSuelta.Normal);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class Reloj(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public void Avanzar(TimeSpan cuanto) => _ahora += cuanto;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Quieto();

        private sealed class Quieto : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
