using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// La fonia por el PC: todo con puentes, vigilante y reloj de mentira. Ninguna prueba abre una
/// tarjeta de sonido ni toca una radio, y ninguna espera al reloj de pared.
/// </summary>
public sealed class ControlDeFoniaPruebas
{
    private static readonly DateTimeOffset Origen = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Al_hablar_pide_antena_al_vigilante_calla_la_escucha_y_deja_pasar_la_voz()
    {
        var (control, vigilante, rx, tx, _) = Montar();
        await control.EmpezarEscuchaAsync("codec-in", "altavoces");

        await control.EmpezarTransmisionAsync("micro", "codec-out");

        vigilante.EnAntena.Should().BeTrue();
        vigilante.Motivos.Should().ContainSingle().Which.Should().Contain("Fonía");
        tx.EstaAbierto.Should().BeTrue();
        tx.Silenciado.Should().BeFalse("la voz pasa una vez el PTT esta arriba");
        tx.SilenciadoAlPedirAntena.Should().BeTrue("el camino se abre callado antes del PTT, por el VOX");
        rx.Silenciado.Should().BeTrue("sin realimentacion: los altavoces callan mientras se habla");
        control.Transmitiendo.Should().BeTrue();
    }

    [Fact]
    public async Task Al_soltar_baja_el_ptt_cierra_el_micro_y_devuelve_la_escucha()
    {
        var (control, vigilante, rx, tx, _) = Montar();
        await control.EmpezarEscuchaAsync("codec-in", "altavoces");
        await control.EmpezarTransmisionAsync("micro", "codec-out");
        FinDeFonia? fin = null;
        control.TransmisionTerminada += (_, f) => fin = f;

        await control.TerminarTransmisionAsync();

        vigilante.EnAntena.Should().BeFalse();
        tx.EstaAbierto.Should().BeFalse();
        tx.Silenciado.Should().BeTrue();
        rx.Silenciado.Should().BeFalse();
        fin!.Motivo.Should().Be(MotivoDeSuelta.Normal);
    }

    [Fact]
    public async Task El_silencio_que_pidio_el_operador_se_respeta_al_volver_a_recepcion()
    {
        var (control, _, rx, _, _) = Montar();
        await control.EmpezarEscuchaAsync("codec-in", "altavoces");
        control.SilenciarEscucha(true);

        await control.EmpezarTransmisionAsync("micro", "codec-out");
        control.SilenciarEscucha(false);
        rx.Silenciado.Should().BeTrue("mientras se transmite sigue callada pase lo que pase");

        await control.TerminarTransmisionAsync();
        rx.Silenciado.Should().BeFalse();
    }

    [Fact]
    public async Task Late_solo_mientras_el_audio_avanza()
    {
        var (control, vigilante, _, tx, reloj) = Montar();
        await control.EmpezarTransmisionAsync("micro", "codec-out");

        reloj.Avanzar(TimeSpan.FromMilliseconds(200));
        tx.UltimoAvanceUtc = reloj.GetUtcNow();
        control.Vigilar();
        vigilante.Latidos.Should().Be(1);

        // El audio se para: no se late aunque se siga vigilando.
        reloj.Avanzar(TimeSpan.FromMilliseconds(1500));
        control.Vigilar();
        vigilante.Latidos.Should().Be(1);
        control.Transmitiendo.Should().BeTrue("todavia no ha pasado el doble de la tolerancia");
    }

    [Fact]
    public async Task Si_el_audio_se_queda_parado_corta_la_fonia_sin_esperar_al_vigilante()
    {
        var (control, vigilante, _, tx, reloj) = Montar();
        var fin = Fin(control);
        await control.EmpezarTransmisionAsync("micro", "codec-out");
        tx.UltimoAvanceUtc = reloj.GetUtcNow();

        reloj.Avanzar(TimeSpan.FromMilliseconds(2100));
        control.Vigilar();

        (await fin.Task.WaitAsync(TimeSpan.FromSeconds(10))).Motivo.Should().Be(MotivoDeSuelta.SinLatido);
        vigilante.EnAntena.Should().BeFalse();
        tx.EstaAbierto.Should().BeFalse();
    }

    [Fact]
    public async Task El_tiempo_maximo_de_fonia_corta_la_pasada()
    {
        var (control, vigilante, _, tx, reloj) = Montar(new OpcionesDeFonia { TiempoMaximo = TimeSpan.FromMinutes(3) });
        var fin = Fin(control);
        await control.EmpezarTransmisionAsync("micro", "codec-out");

        for (var i = 0; i < 179; i++)
        {
            reloj.Avanzar(TimeSpan.FromSeconds(1));
            tx.UltimoAvanceUtc = reloj.GetUtcNow();
            control.Vigilar();
        }

        control.Transmitiendo.Should().BeTrue();

        reloj.Avanzar(TimeSpan.FromSeconds(1));
        tx.UltimoAvanceUtc = reloj.GetUtcNow();
        control.Vigilar();

        (await fin.Task.WaitAsync(TimeSpan.FromSeconds(10))).Motivo.Should().Be(MotivoDeSuelta.TiempoAgotado);
        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public void Manda_el_tope_mas_corto_entre_fonia_y_vigilante()
    {
        var (control, vigilante, _, _, _) = Montar(new OpcionesDeFonia { TiempoMaximo = TimeSpan.FromMinutes(5) });
        vigilante.TiempoMaximo = TimeSpan.FromMinutes(2);

        control.TiempoMaximoEfectivo.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task El_boton_de_panico_del_vigilante_tambien_corta_la_fonia()
    {
        var (control, vigilante, rx, tx, _) = Montar();
        var fin = Fin(control);
        await control.EmpezarEscuchaAsync("codec-in", "altavoces");
        await control.EmpezarTransmisionAsync("micro", "codec-out");

        await vigilante.SoltarYaAsync(MotivoDeSuelta.Panico);

        (await fin.Task.WaitAsync(TimeSpan.FromSeconds(10))).Motivo.Should().Be(MotivoDeSuelta.Panico);
        tx.EstaAbierto.Should().BeFalse();
        tx.Silenciado.Should().BeTrue();
        rx.Silenciado.Should().BeFalse();
        control.Transmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task No_se_pisa_una_transmision_del_modem()
    {
        var (control, vigilante, _, tx, _) = Montar();
        await using var delModem = await vigilante.PedirAntenaAsync("FT8");

        var intento = () => control.EmpezarTransmisionAsync("micro", "codec-out");

        await intento.Should().ThrowAsync<InvalidOperationException>();
        tx.EstaAbierto.Should().BeFalse();
        vigilante.Motivos.Should().ContainSingle("no se ha pedido antena para la fonia");
    }

    [Fact]
    public async Task Si_el_ptt_no_sube_se_cierra_el_micro_y_se_devuelve_la_escucha()
    {
        var (control, vigilante, rx, tx, _) = Montar();
        await control.EmpezarEscuchaAsync("codec-in", "altavoces");
        vigilante.FallarAlPedir = true;

        var intento = () => control.EmpezarTransmisionAsync("micro", "codec-out");

        await intento.Should().ThrowAsync<IOException>();
        tx.EstaAbierto.Should().BeFalse();
        tx.Silenciado.Should().BeTrue();
        rx.Silenciado.Should().BeFalse();
        control.Transmitiendo.Should().BeFalse();
    }

    [Fact]
    public async Task Si_se_rompe_el_camino_de_audio_se_suelta_el_ptt()
    {
        var (control, vigilante, _, tx, _) = Montar();
        var fin = Fin(control);
        await control.EmpezarTransmisionAsync("micro", "codec-out");

        tx.Romper(new IOException("dispositivo desenchufado"));

        (await fin.Task.WaitAsync(TimeSpan.FromSeconds(10))).Motivo.Should().Be(MotivoDeSuelta.Excepcion);
        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task Al_cerrar_el_programa_se_suelta_todo()
    {
        var (control, vigilante, rx, tx, _) = Montar();
        await control.EmpezarEscuchaAsync("codec-in", "altavoces");
        await control.EmpezarTransmisionAsync("micro", "codec-out");

        await control.DisposeAsync();

        vigilante.EnAntena.Should().BeFalse();
        tx.EstaAbierto.Should().BeFalse();
        rx.EstaAbierto.Should().BeFalse();
    }

    [Fact]
    public async Task Con_el_vigilante_de_verdad_el_panico_baja_el_ptt_del_equipo_y_recoge_la_fonia()
    {
        var equipo = new EquipoDeMentira();
        await using var vigilante = new VigilantePtt(equipo, new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });
        var rx = new PuenteDeMentira();
        var tx = new PuenteDeMentira();
        await using var control = new ControlDeFonia(vigilante, rx, tx, reloj: new RelojDeMentira(Origen));
        var fin = Fin(control);

        await control.EmpezarTransmisionAsync("micro", "codec-out");
        equipo.Ptt.Should().BeTrue();

        await vigilante.SoltarYaAsync(MotivoDeSuelta.Panico);

        equipo.Ptt.Should().BeFalse();
        (await fin.Task.WaitAsync(TimeSpan.FromSeconds(10))).Motivo.Should().Be(MotivoDeSuelta.Panico);
        tx.EstaAbierto.Should().BeFalse();
    }

    private static TaskCompletionSource<FinDeFonia> Fin(ControlDeFonia control)
    {
        var fin = new TaskCompletionSource<FinDeFonia>(TaskCreationOptions.RunContinuationsAsynchronously);
        control.TransmisionTerminada += (_, f) => fin.TrySetResult(f);
        return fin;
    }

    private static (ControlDeFonia, VigilanteDeMentira, PuenteDeMentira, PuenteDeMentira, RelojDeMentira) Montar(
        OpcionesDeFonia? opciones = null)
    {
        var reloj = new RelojDeMentira(Origen);
        var vigilante = new VigilanteDeMentira();
        var rx = new PuenteDeMentira();
        var tx = new PuenteDeMentira();
        tx.AlPedirAntena = vigilante;
        var control = new ControlDeFonia(vigilante, rx, tx, opciones, reloj);
        return (control, vigilante, rx, tx, reloj);
    }

    private sealed class RelojDeMentira(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;

        public void Avanzar(TimeSpan cuanto) => _ahora += cuanto;

        public override DateTimeOffset GetUtcNow() => _ahora;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new TemporizadorQuieto();

        private sealed class TemporizadorQuieto : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class PuenteDeMentira : IPuenteDeAudio
    {
        public VigilanteDeMentira? AlPedirAntena { get; set; }

        public bool SilenciadoAlPedirAntena { get; private set; }

        public bool EstaAbierto { get; private set; }

        public float Ganancia { get; set; } = 1f;

        private bool _silenciado;

        public bool Silenciado
        {
            get => _silenciado;
            set => _silenciado = value;
        }

        public double Nivel => 0;

        public bool Saturando => false;

        public DateTimeOffset? UltimoAvanceUtc { get; set; }

        public event EventHandler<Exception>? Fallo;

        public Task AbrirAsync(string idEntrada, string idSalida, CancellationToken ct = default)
        {
            EstaAbierto = true;
            if (AlPedirAntena is not null) AlPedirAntena.AntesDePedir = () => SilenciadoAlPedirAntena = _silenciado;
            return Task.CompletedTask;
        }

        public Task CerrarAsync()
        {
            EstaAbierto = false;
            UltimoAvanceUtc = null;
            return Task.CompletedTask;
        }

        public void Romper(Exception fallo) => Fallo?.Invoke(this, fallo);

        public ValueTask DisposeAsync() => new(CerrarAsync());
    }

    private sealed class VigilanteDeMentira : IVigilantePtt
    {
        private Transmision? _enCurso;

        public List<string> Motivos { get; } = [];

        public int Latidos { get; private set; }

        public bool FallarAlPedir { get; set; }

        public Action? AntesDePedir { get; set; }

        public TimeSpan TiempoMaximo { get; set; } = TimeSpan.FromMinutes(10);

        public TimeSpan TiempoSinLatido => TimeSpan.FromSeconds(15);

        public bool EnAntena => _enCurso is not null;

        public event EventHandler<MotivoDeSuelta>? PttSoltado;

        public Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default)
        {
            if (EnAntena) throw new InvalidOperationException("Ya hay una transmisión en curso.");
            AntesDePedir?.Invoke();
            if (FallarAlPedir) throw new IOException("El equipo no contesta.");
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

        private sealed class Transmision(VigilanteDeMentira vigilante) : ITransmisionEnCurso
        {
            public bool EnAntena => ReferenceEquals(vigilante._enCurso, this);

            public void Latir() => vigilante.Latidos++;

            public ValueTask DisposeAsync()
            {
                if (EnAntena) vigilante.Soltar(MotivoDeSuelta.Normal);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class EquipoDeMentira : IControlEquipo
    {
        public bool Ptt { get; private set; }

        public ViaDeControl Via => ViaDeControl.CatNativo;

        public EstadoDelEquipo Estado => EstadoDelEquipo.Desconectado with { Conectado = true, Transmitiendo = Ptt };

        public event EventHandler<EstadoDelEquipo>? EstadoCambiado
        {
            add { }
            remove { }
        }

        public Task ConectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerModoAsync(Modo modo, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerPttAsync(bool transmitir, CancellationToken ct = default)
        {
            Ptt = transmitir;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

