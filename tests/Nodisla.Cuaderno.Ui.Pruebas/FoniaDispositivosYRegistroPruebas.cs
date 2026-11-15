using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Lo que pasa con la fonia cuando cambian los dispositivos, que avisa cuando estan mal
/// elegidos y que deja escrito en el registro cuando no puede escuchar o transmitir.
/// Vigilante de verdad sobre un equipo de mentira y caminos de audio de mentira: nada abre una
/// tarjeta de sonido y nada transmite.
/// </summary>
public sealed partial class FoniaDispositivosYRegistroPruebas
{
    private static readonly DispositivoDeAudio CodecIn = new("codec-in", "Micrófono (USB Audio Device)", true, true);
    private static readonly DispositivoDeAudio MicroPc = new("micro-pc", "Micrófono (Lync USB Headset)", true, false);
    private static readonly DispositivoDeAudio OtroMicro = new("otro-micro", "Micrófono (Realtek)", true, false);
    private static readonly DispositivoDeAudio CodecOut = new("codec-out", "Altavoces (USB Audio Device)", false, true);
    private static readonly DispositivoDeAudio CascosPc = new("cascos-pc", "Altavoces (Lync USB Headset)", false, false);
    private static readonly DispositivoDeAudio AltavocesPc = new("altavoces-pc", "Altavoces (Realtek)", false, false);

    // ── Punto 5: cambiar un dispositivo de escucha ───────────────────────

    [Fact]
    public async Task Cambiar_los_altavoces_con_la_escucha_abierta_la_reabre_por_los_nuevos()
    {
        var m = await MontarAsync();
        await m.Fonia.AlternarEscuchaAsync();
        m.Rx.Abierto.Should().Be(("codec-in", "cascos-pc"));

        m.Fonia.Ajustes.Altavoces = AltavocesPc;
        await EsperaALaVentana.DrenarAsync();

        m.Rx.Abierto.Should().Be(("codec-in", "altavoces-pc"));
        m.Rx.Aperturas.Should().Be(2);
        m.Fonia.Escuchando.Should().BeTrue();
    }

    [Fact]
    public async Task Cambiar_el_microfono_o_la_salida_al_equipo_no_toca_la_escucha()
    {
        var m = await MontarAsync();
        await m.Fonia.AlternarEscuchaAsync();

        m.Fonia.Ajustes.Microfono = OtroMicro;
        m.Fonia.Ajustes.SalidaAlEquipo = AltavocesPc;
        await EsperaALaVentana.DrenarAsync();

        m.Rx.Aperturas.Should().Be(1, "son dispositivos de la transmision: la escucha sigue como estaba");
        m.Rx.Abierto.Should().Be(("codec-in", "cascos-pc"));
    }

    [Fact]
    public async Task Sin_escucha_abierta_ni_pedida_cambiar_un_dispositivo_no_abre_nada()
    {
        var m = await MontarAsync();

        m.Fonia.Ajustes.Altavoces = AltavocesPc;
        m.Fonia.Ajustes.EntradaDelEquipo = OtroMicro;
        await EsperaALaVentana.DrenarAsync();

        m.Rx.Aperturas.Should().Be(0);
        m.Fonia.Escuchando.Should().BeFalse();
    }

    /// <summary>
    /// Lo de Jose el 2026-11-15: «escuchar al arrancar» puesto, la escucha sin abrir, corrige los
    /// dispositivos en el desplegable... y seguia sin oirse nada, porque cambiar un dispositivo
    /// solo reabria una escucha que ya estuviera abierta.
    /// </summary>
    [Fact]
    public async Task Con_escuchar_al_arrancar_elegir_el_dispositivo_que_faltaba_abre_la_escucha()
    {
        // La radio apagada al arrancar: no hay codec y la entrada del equipo se queda sin elegir.
        var m = await MontarAsync(entradas: [MicroPc, OtroMicro], escucharAlArrancar: true);
        m.Fonia.Ajustes.EntradaDelEquipo.Should().BeNull();
        await m.Fonia.EscucharSiSePidioAsync();
        m.Fonia.Escuchando.Should().BeFalse("no hay de donde escuchar");

        m.Fonia.Ajustes.EntradaDelEquipo = OtroMicro;
        await EsperaALaVentana.DrenarAsync();

        m.Rx.Abierto.Should().Be(("otro-micro", "cascos-pc"));
        m.Fonia.Escuchando.Should().BeTrue("el operador la tiene pedida y ya hay con que");
    }

    [Fact]
    public async Task Volver_a_buscar_con_la_radio_recien_encendida_abre_la_escucha_pedida()
    {
        var entradas = new List<DispositivoDeAudio> { MicroPc };
        var m = await MontarAsync(entradas: entradas, escucharAlArrancar: true);
        await m.Fonia.EscucharSiSePidioAsync();
        m.Fonia.Escuchando.Should().BeFalse();

        // Se enciende la radio: aparece su codec. El operador pulsa «Volver a buscar».
        entradas.Insert(0, CodecIn);
        m.Fonia.Ajustes.RefrescarDispositivos();
        await EsperaALaVentana.DrenarAsync();

        m.Fonia.Ajustes.EntradaDelEquipo.Should().Be(CodecIn);
        m.Rx.Abierto.Should().Be(("codec-in", "cascos-pc"));
        m.Fonia.Escuchando.Should().BeTrue();
    }

    [Fact]
    public async Task La_escucha_parada_a_mano_no_se_reabre_al_cambiar_un_dispositivo()
    {
        var m = await MontarAsync(escucharAlArrancar: true);
        await m.Fonia.EscucharSiSePidioAsync();
        m.Fonia.Escuchando.Should().BeTrue();
        await m.Fonia.AlternarEscuchaAsync();
        m.Fonia.Escuchando.Should().BeFalse();

        m.Fonia.Ajustes.Altavoces = AltavocesPc;
        await EsperaALaVentana.DrenarAsync();

        m.Fonia.Escuchando.Should().BeFalse("la paro el operador: no se le vuelve a abrir por su cuenta");
        m.Rx.Aperturas.Should().Be(1);
    }

    // ── Montaje ──────────────────────────────────────────────────────────

    private sealed record Montaje(
        VistaModeloFonia Fonia,
        VistaModeloEquipo Equipo,
        EquipoDeMentira Radio,
        PuenteDeMentira Rx,
        PuenteDeMentira Tx,
        Anotador Anotado);

    private static async Task<Montaje> MontarAsync(
        string modoDelEquipo = "USB",
        IReadOnlyList<DispositivoDeAudio>? entradas = null,
        IReadOnlyList<DispositivoDeAudio>? salidas = null,
        bool escucharAlArrancar = false,
        Action<AjustesDeFonia>? preparar = null)
    {
        var radio = new EquipoDeMentira();
        var vigilante = new VigilantePtt(radio, new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });
        var equipo = new VistaModeloEquipo(radio, vigilante);
        await equipo.ConectarAsync();
        radio.PonerModo(modoDelEquipo);

        var rx = new PuenteDeMentira();
        var tx = new PuenteDeMentira();
        var control = new ControlDeFonia(vigilante, rx, tx, reloj: new RelojQuieto());
        var ajustes = new AjustesDelPrograma();
        ajustes.Fonia.EscucharAlArrancar = escucharAlArrancar;
        preparar?.Invoke(ajustes.Fonia);

        var anotador = new Anotador();
        var registro = new LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(anotador).CreateLogger();

        entradas ??= [CodecIn, MicroPc, OtroMicro];
        salidas ??= [CodecOut, CascosPc, AltavocesPc];
        var deAjustes = new VistaModeloAjustesFonia(
            ajustes,
            carpeta: null,
            () => [.. entradas],
            () => [.. salidas],
            deEntrada => deEntrada ? MicroPc.Id : CascosPc.Id,
            registro);

        var fonia = new VistaModeloFonia(deAjustes, control, equipo, radio, registro);
        await EsperaALaVentana.DrenarAsync();
        return new Montaje(fonia, equipo, radio, rx, tx, anotador);
    }

    private sealed class Anotador : ILogEventSink
    {
        private readonly List<LogEvent> _lineas = [];

        public IReadOnlyList<LogEvent> Lineas
        {
            get
            {
                lock (_lineas) return [.. _lineas];
            }
        }

        public IReadOnlyList<string> Textos => [.. Lineas.Select(l => l.RenderMessage())];

        public void Emit(LogEvent logEvent)
        {
            lock (_lineas) _lineas.Add(logEvent);
        }
    }

    private sealed class RelojQuieto : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            new Quieto();

        private sealed class Quieto : ITimer
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
        public (string Entrada, string Salida)? Abierto { get; private set; }

        public int Aperturas { get; private set; }

        public bool EstaAbierto => Abierto is not null;

        public float Ganancia { get; set; } = 1f;

        public bool Silenciado { get; set; }

        public double Nivel => 0;

        public bool Saturando => false;

        public DateTimeOffset? UltimoAvanceUtc { get; set; }

        public Nodisla.Cuaderno.Audio.Procesado.IProcesadorDeAudio? AntesDeLaGanancia { get; set; }

        public Nodisla.Cuaderno.Audio.Procesado.IProcesadorDeAudio? TrasLaGanancia { get; set; }

#pragma warning disable CS0067 // Los caminos de mentira no se rompen en estas pruebas.
        public event EventHandler<Exception>? Fallo;
#pragma warning restore CS0067

        public Task AbrirAsync(string idEntrada, string idSalida, CancellationToken ct = default)
        {
            Abierto = (idEntrada, idSalida);
            Aperturas++;
            return Task.CompletedTask;
        }

        public Task CerrarAsync()
        {
            Abierto = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new(CerrarAsync());
    }

    private sealed class EquipoDeMentira : IControlEquipo
    {
        private EstadoDelEquipo _estado = EstadoDelEquipo.Desconectado;

        public bool Ptt { get; private set; }

        public ViaDeControl Via => ViaDeControl.CatNativo;

        public EstadoDelEquipo Estado => _estado;

        public event EventHandler<EstadoDelEquipo>? EstadoCambiado;

        public void PonerModo(string modoDelEquipo) =>
            Cambiar(_estado with { Modo = TraductorDeModos.PorOmision.DesdeElEquipo(modoDelEquipo) });

        public Task ConectarAsync(CancellationToken ct = default)
        {
            Cambiar(_estado with { Conectado = true, Frecuencia = Frecuencia.DesdeHercios(14_200_000), LeidoUtc = DateTimeOffset.UnixEpoch });
            return Task.CompletedTask;
        }

        public Task DesconectarAsync(CancellationToken ct = default)
        {
            Cambiar(EstadoDelEquipo.Desconectado);
            return Task.CompletedTask;
        }

        public Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerModoAsync(Modo modo, CancellationToken ct = default) => Task.CompletedTask;

        public Task PonerPttAsync(bool transmitir, CancellationToken ct = default)
        {
            Ptt = transmitir;
            Cambiar(_estado with { Transmitiendo = transmitir });
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private void Cambiar(EstadoDelEquipo nuevo)
        {
            _estado = nuevo;
            EstadoCambiado?.Invoke(this, nuevo);
        }
    }
}
