using System.IO;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Audio.Procesado;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Voice keyer, grabador de la recepcion y procesado desde la pantalla, con el vigilante de
/// VERDAD sobre un equipo de mentira. Ningun dispositivo de audio se abre y nada transmite: el
/// microfono y los altavoces son los simulados y el camino de audio se bombea a mano.
/// </summary>
public sealed class FoniaVozYGrabadorPruebas : IDisposable
{
    private static readonly DispositivoDeAudio CodecIn = new("codec-in", "Micrófono (USB Audio Device)", true, true);
    private static readonly DispositivoDeAudio MicroPc = new("micro-pc", "Micrófono (Realtek)", true, false);
    private static readonly DispositivoDeAudio CodecOut = new("codec-out", "Altavoces (USB Audio Device)", false, true);
    private static readonly DispositivoDeAudio AltavocesPc = new("altavoces-pc", "Altavoces (Realtek)", false, false);

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-voz-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private sealed record Montaje(
        VistaModeloFonia Fonia,
        VistaModeloMensajesDeVoz Voz,
        VistaModeloGrabacionRx Grabacion,
        VistaModeloEntradaQso Entrada,
        Equipo Radio,
        ControlDeFonia Control,
        Bomba Tx,
        AjustesDelPrograma Ajustes,
        RepositorioQsoEnMemoria Cuaderno);

    private async Task<Montaje> MontarAsync(bool pestillo = true, bool preguntar = false)
    {
        var radio = new Equipo();
        var vigilante = new VigilantePtt(radio, new OpcionesDelVigilante { EngancharseAlCierreDelProceso = false });
        var equipo = new VistaModeloEquipo(radio, vigilante);
        await equipo.ConectarAsync();
        radio.PonerModo("USB");

        var tx = new Bomba();
        var control = new ControlDeFonia(vigilante, new Bomba(), tx, reloj: new RelojQuieto());
        var ajustes = new AjustesDelPrograma();
        ajustes.Digital.PedirConfirmacionAlTransmitir = preguntar;

        var deAjustes = new VistaModeloAjustesFonia(ajustes, null, () => [CodecIn, MicroPc], () => [CodecOut, AltavocesPc]);
        var fonia = new VistaModeloFonia(deAjustes, control, equipo, radio);

        var cuaderno = new RepositorioQsoEnMemoria([]);
        var estaciones = new RepositorioEstacionEnMemoria();
        var entrada = new VistaModeloEntradaQso(
            new RegistrarQso(cuaderno, estaciones),
            new EditarQso(cuaderno, estaciones),
            new ConsultarTrabajadoAntes(cuaderno))
        {
            Esperar = (_, _) => Task.CompletedTask,
        };

        var reproductor = new ReproductorLocalSimulado();
        var voz = new VistaModeloMensajesDeVoz(fonia, control, new AlmacenDeMensajesDeVoz(_carpeta), new GrabadorDeMicrofonoSimulado(), reproductor, ajustes)
        {
            PermitirTransmitir = pestillo,
        };
        var grabacion = new VistaModeloGrabacionRx(control, deAjustes, reproductor, _carpeta, entrada, () => new DateTimeOffset(2026, 10, 2, 18, 30, 5, TimeSpan.Zero));
        fonia.Mensajes = voz;
        fonia.Grabacion = grabacion;
        await EsperaALaVentana.DrenarAsync();
        return new Montaje(fonia, voz, grabacion, entrada, radio, control, tx, ajustes, cuaderno);
    }

    private static void Grabar(MensajeEnPantalla ranura, double segundos)
    {
        var m = new float[(int)(segundos * 48000)];
        for (var i = 0; i < m.Length; i++) m[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 600 * i / 48000.0));
        ranura.PonerAudio(new AudioEnMemoria(m, 48000));
    }

    [Fact]
    public async Task Sin_el_pestillo_el_voice_keyer_no_transmite()
    {
        var m = await MontarAsync(pestillo: false);
        Grabar(m.Voz.Mensajes[0], 1);

        await m.Voz.EmitirAsync(m.Voz.Mensajes[0]);

        m.Radio.VecesQueSubioElPtt.Should().Be(0);
        m.Control.Transmitiendo.Should().BeFalse();
        m.Voz.Aviso.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Si_el_operador_no_confirma_no_transmite()
    {
        var m = await MontarAsync(preguntar: true);
        string? pregunta = null;
        m.Voz.ConfirmarQueVaATransmitir = p =>
        {
            pregunta = p;
            return false;
        };
        Grabar(m.Voz.Mensajes[0], 1);

        await m.Voz.EmitirAsync(m.Voz.Mensajes[0]);

        pregunta.Should().Contain("F1").And.Contain("CQ");
        m.Radio.VecesQueSubioElPtt.Should().Be(0);
    }

    [Fact]
    public async Task Con_pestillo_sale_por_el_vigilante_y_al_acabar_baja_el_ptt_solo()
    {
        var m = await MontarAsync();
        var fin = new TaskCompletionSource<FinDeFonia>(TaskCreationOptions.RunContinuationsAsynchronously);
        m.Control.TransmisionTerminada += (_, f) => fin.TrySetResult(f);
        Grabar(m.Voz.Mensajes[1], 0.5);

        (await m.Voz.PulsarTeclaAsync(2)).Should().BeTrue();

        m.Radio.Ptt.Should().BeTrue();
        m.Control.EnviandoMensaje.Should().BeTrue();
        m.Tx.Abierto.Should().Be(("micro-pc", "codec-out"));

        for (var i = 0; i < 100 && !fin.Task.IsCompleted; i++) m.Tx.Bombear();

        (await fin.Task.WaitAsync(TimeSpan.FromSeconds(10))).Motivo.Should().Be(MotivoDeSuelta.Normal);
        m.Radio.Ptt.Should().BeFalse();
    }

    [Fact]
    public async Task Un_mensaje_mas_largo_que_el_tiempo_maximo_no_sale()
    {
        var m = await MontarAsync();
        m.Fonia.Ajustes.TiempoMaximoSegundos = 10;
        Grabar(m.Voz.Mensajes[0], 12);

        await m.Voz.EmitirAsync(m.Voz.Mensajes[0]);

        m.Radio.VecesQueSubioElPtt.Should().Be(0);
        m.Voz.Aviso.Should().Contain("10 s");
    }

    [Fact]
    public async Task Pulsar_el_ptt_corta_el_mensaje()
    {
        var m = await MontarAsync();
        Grabar(m.Voz.Mensajes[0], 5);
        await m.Voz.EmitirAsync(m.Voz.Mensajes[0]);
        m.Radio.Ptt.Should().BeTrue();

        await m.Fonia.PttAbajoAsync();

        m.Radio.Ptt.Should().BeFalse();
        m.Control.EnviandoMensaje.Should().BeFalse();
    }

    [Fact]
    public async Task Una_tecla_sin_mensaje_no_hace_nada()
    {
        var m = await MontarAsync();

        (await m.Voz.PulsarTeclaAsync(3)).Should().BeFalse();
        m.Radio.VecesQueSubioElPtt.Should().Be(0);
    }

    [Fact]
    public async Task Grabar_y_escuchar_un_mensaje_es_local_y_se_guarda_en_la_carpeta_de_voz()
    {
        var m = await MontarAsync();
        var ranura = m.Voz.Mensajes[2];

        await m.Voz.GrabarAsync(ranura);
        m.Voz.Grabando.Should().BeTrue();
        await Task.Delay(700);
        await m.Voz.GrabarAsync(ranura);

        ranura.Grabado.Should().BeTrue();
        File.Exists(Path.Combine(_carpeta, "voz", "mensaje-3.wav")).Should().BeTrue();
        m.Radio.VecesQueSubioElPtt.Should().Be(0, "grabar no transmite");

        ranura.Nombre = "Mi QTH";
        var leidos = new AlmacenDeMensajesDeVoz(_carpeta).Leer(n => "?");
        leidos[2].Nombre.Should().Be("Mi QTH");
        leidos[2].Grabado.Should().BeTrue();
    }

    [Fact]
    public async Task Guardar_lo_ultimo_escribe_un_wav_y_lo_adjunta_al_contacto_que_se_registra()
    {
        var m = await MontarAsync();
        var bloque = Enumerable.Range(0, 4800).Select(i => (float)(0.2 * Math.Sin(i * 0.1))).ToArray();
        for (var i = 0; i < 20; i++) m.Control.Grabador.Procesar(bloque, 48000);
        m.Entrada.Indicativo = "EA8XYZ";

        var ruta = m.Grabacion.GuardarLoUltimo();

        ruta.Should().NotBeNull();
        File.Exists(ruta).Should().BeTrue();
        Path.GetFileName(ruta).Should().Be("rx-20261002-183005-EA8XYZ.wav");
        ArchivoWav.Leer(ruta!).Duracion.Should().BeCloseTo(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(5));
        m.Entrada.AudioAdjunto.Should().Be("rx-20261002-183005-EA8XYZ.wav");

        await m.Entrada.GuardarCommand.ExecuteAsync(null);
        var qso = (await m.Cuaderno.BuscarAsync(new CriterioQso(), 0, 10)).Elementos.Single();
        qso.AudioAdjunto.Should().Be("rx-20261002-183005-EA8XYZ.wav");

        // Al abrir el contacto para modificarlo, se ve el audio y se puede escuchar.
        m.Entrada.CargarParaEditar(qso);
        m.Entrada.TieneAudio.Should().BeTrue();
        m.Entrada.EscucharAudioCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Sin_adjuntar_el_audio_se_guarda_pero_no_va_al_contacto()
    {
        var m = await MontarAsync();
        m.Fonia.Ajustes.AdjuntarAlQso = false;
        m.Control.Grabador.Procesar(new float[4800], 48000);

        m.Grabacion.GuardarLoUltimo().Should().NotBeNull();
        m.Entrada.AudioAdjunto.Should().BeEmpty();
    }

    [Fact]
    public async Task Los_ajustes_de_procesado_llegan_en_vivo_a_la_cadena()
    {
        var m = await MontarAsync();

        m.Fonia.Ajustes.ReductorActivo = true;
        m.Fonia.Ajustes.NivelDeReduccion = 40;
        m.Fonia.Ajustes.NotchActivo = true;
        m.Fonia.Ajustes.TechoEscuchaDb = -6;
        m.Fonia.Ajustes.ProcesarMicro = true;
        m.Fonia.Ajustes.TechoMicroDb = -4;
        m.Fonia.Ajustes.MinutosDeGrabacion = 2;

        m.Control.Escucha.ReductorActivo.Should().BeTrue();
        m.Control.Escucha.Nivel.Should().BeApproximately(0.4, 1e-9);
        m.Control.Escucha.NotchActivo.Should().BeTrue();
        m.Control.Escucha.TechoDb.Should().Be(-6);
        m.Control.Microfono.Activo.Should().BeTrue();
        m.Control.Microfono.TechoDb.Should().Be(-4);
        m.Control.Grabador.Minutos.Should().Be(2);

        m.Fonia.Refrescar();
        m.Fonia.LatenciaDelProcesado.Should().StartWith("+");
    }

    [Fact]
    public void Los_ajustes_de_procesado_se_guardan_y_se_acotan()
    {
        var ajustes = new AjustesDelPrograma();
        ajustes.Fonia.TechoMicroDb = 3;
        ajustes.Fonia.TechoEscuchaDb = -50;
        ajustes.Fonia.MinutosDeGrabacion = 99;
        ajustes.Fonia.TipoDeReductor = "Otro";
        ajustes.Guardar(_carpeta);

        var leidos = AjustesDelPrograma.Leer(_carpeta).Fonia;

        leidos.TechoMicroDb.Should().Be(-1, "el techo del micro no se puede subir por encima de -1 dBFS");
        leidos.TechoEscuchaDb.Should().Be(-20);
        leidos.MinutosDeGrabacion.Should().Be(10);
        leidos.TipoDeReductor.Should().Be("Espectral");
    }

    // ── Dobles ───────────────────────────────────────────────────────────────

    private sealed class RelojQuieto : TimeProvider
    {
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

    /// <summary>Camino de audio de mentira que se bombea a mano, como el de verdad por dentro.</summary>
    private sealed class Bomba : IPuenteDeAudio
    {
        public (string Entrada, string Salida)? Abierto { get; private set; }

        public bool EstaAbierto => Abierto is not null;

        public float Ganancia { get; set; } = 1f;

        public bool Silenciado { get; set; }

        public double Nivel => 0;

        public bool Saturando => false;

        public DateTimeOffset? UltimoAvanceUtc { get; set; }

        public IProcesadorDeAudio? AntesDeLaGanancia { get; set; }

        public IProcesadorDeAudio? TrasLaGanancia { get; set; }

        public event EventHandler<Exception>? Fallo
        {
            add { }
            remove { }
        }

        public void Bombear()
        {
            var bloque = new float[480];
            AntesDeLaGanancia?.Procesar(bloque, 48000);
            for (var i = 0; i < bloque.Length; i++) bloque[i] *= Ganancia;
            TrasLaGanancia?.Procesar(bloque, 48000);
        }

        public Task AbrirAsync(string idEntrada, string idSalida, CancellationToken ct = default)
        {
            Abierto = (idEntrada, idSalida);
            return Task.CompletedTask;
        }

        public Task CerrarAsync()
        {
            Abierto = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => new(CerrarAsync());
    }

    /// <summary>Equipo de mentira que dice su modo y apunta el PTT. No habla con ninguna radio.</summary>
    private sealed class Equipo : IControlEquipo
    {
        private EstadoDelEquipo _estado = EstadoDelEquipo.Desconectado;

        public bool Ptt { get; private set; }

        public int VecesQueSubioElPtt { get; private set; }

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
            if (transmitir) VecesQueSubioElPtt++;
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
