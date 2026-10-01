using System.IO;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Fonia;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El panel de fonia de Operar, con el vigilante de VERDAD sobre un equipo de mentira y caminos
/// de audio de mentira. Nada abre una tarjeta de sonido ni el microfono, y nada transmite.
/// </summary>
public sealed class FoniaPruebas
{
    private static readonly DispositivoDeAudio CodecIn = new("codec-in", "Micrófono (USB Audio Device)", true, true);
    private static readonly DispositivoDeAudio MicroPc = new("micro-pc", "Micrófono (Realtek)", true, false);
    private static readonly DispositivoDeAudio CodecOut = new("codec-out", "Altavoces (USB Audio Device)", false, true);
    private static readonly DispositivoDeAudio AltavocesPc = new("altavoces-pc", "Altavoces (Realtek)", false, false);

    private sealed record Montaje(
        VistaModeloFonia Fonia,
        VistaModeloEquipo Equipo,
        EquipoDeMentira Radio,
        VigilantePtt Vigilante,
        PuenteDeMentira Rx,
        PuenteDeMentira Tx,
        ControlDeFonia Control,
        AjustesDelPrograma Ajustes);

    private static async Task<Montaje> MontarAsync(string modoDelEquipo = "USB", bool conmutado = false)
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
        ajustes.Fonia.ModoDelPtt = conmutado ? ModoDelPttDeFonia.Conmutado : ModoDelPttDeFonia.Mantener;

        var deAjustes = new VistaModeloAjustesFonia(
            ajustes,
            carpeta: null,
            () => [CodecIn, MicroPc],
            () => [CodecOut, AltavocesPc],
            deEntrada => deEntrada ? MicroPc.Id : AltavocesPc.Id);

        var fonia = new VistaModeloFonia(deAjustes, control, equipo, radio);
        await EsperaALaVentana.DrenarAsync();
        return new Montaje(fonia, equipo, radio, vigilante, rx, tx, control, ajustes);
    }

    [Fact]
    public async Task Elige_solo_el_codec_para_el_equipo_y_los_del_pc_para_micro_y_altavoces()
    {
        var m = await MontarAsync();

        m.Fonia.Ajustes.EntradaDelEquipo.Should().Be(CodecIn);
        m.Fonia.Ajustes.SalidaAlEquipo.Should().Be(CodecOut);
        m.Fonia.Ajustes.Microfono.Should().Be(MicroPc);
        m.Fonia.Ajustes.Altavoces.Should().Be(AltavocesPc);
        m.Fonia.Ajustes.AvisoDeDispositivos.Should().BeEmpty();
    }

    [Fact]
    public async Task Avisa_si_el_micro_elegido_es_la_entrada_del_equipo()
    {
        var m = await MontarAsync();

        m.Fonia.Ajustes.Microfono = CodecIn;

        m.Fonia.Ajustes.AvisoDeDispositivos.Should().Contain("retransmitiría");
    }

    [Fact]
    public async Task Mantener_pulsado_transmite_por_el_vigilante_y_al_soltar_baja_el_ptt()
    {
        var m = await MontarAsync();
        m.Fonia.PuedeTransmitir.Should().BeTrue();

        await m.Fonia.PttAbajoAsync();

        m.Radio.Ptt.Should().BeTrue();
        m.Vigilante.EnAntena.Should().BeTrue();
        m.Fonia.Transmitiendo.Should().BeTrue();
        m.Tx.Abierto.Should().Be(("micro-pc", "codec-out"));
        m.Tx.Silenciado.Should().BeFalse();

        await m.Fonia.PttArribaAsync();

        m.Radio.Ptt.Should().BeFalse();
        m.Fonia.Transmitiendo.Should().BeFalse();
        m.Tx.Abierto.Should().BeNull();
    }

    [Fact]
    public async Task Conmutado_un_clic_empieza_y_otro_acaba()
    {
        var m = await MontarAsync(conmutado: true);

        await m.Fonia.PttAbajoAsync();
        await m.Fonia.PttArribaAsync();
        m.Radio.Ptt.Should().BeTrue("en conmutado levantar el boton no suelta");

        await m.Fonia.PttAbajoAsync();
        m.Radio.Ptt.Should().BeFalse();
    }

    [Fact]
    public async Task En_modo_de_datos_el_ptt_de_fonia_se_apaga_y_no_pide_antena()
    {
        var m = await MontarAsync("PKTUSB");

        m.Fonia.PuedeTransmitir.Should().BeFalse();
        m.Fonia.MotivoDeNoPoder.Should().Contain("solo va en SSB, AM y FM");

        await m.Fonia.PttAbajoAsync();

        m.Radio.Ptt.Should().BeFalse();
        m.Radio.VecesQueSubioElPtt.Should().Be(0);
        m.Tx.Abierto.Should().BeNull();
    }

    [Fact]
    public async Task Sin_equipo_conectado_no_se_puede_transmitir()
    {
        var m = await MontarAsync();
        await m.Equipo.DesconectarAsync();
        await EsperaALaVentana.DrenarAsync();

        m.Fonia.PuedeTransmitir.Should().BeFalse();
        m.Fonia.MotivoDeNoPoder.Should().Contain("Conecte el equipo");
    }

    [Fact]
    public async Task Soltar_ptt_de_la_barra_corta_tambien_la_fonia()
    {
        var m = await MontarAsync(conmutado: true);
        var fin = Fin(m.Control);
        await m.Fonia.PttAbajoAsync();

        await m.Equipo.SoltarPttCommand.ExecuteAsync(null);

        (await EsperarFinAsync(fin)).Motivo.Should().Be(MotivoDeSuelta.Panico);
        m.Radio.Ptt.Should().BeFalse();
        m.Tx.Abierto.Should().BeNull();
        m.Rx.Silenciado.Should().BeFalse();
        m.Fonia.Transmitiendo.Should().BeFalse();
        m.Fonia.Aviso.Should().Contain("SOLTAR PTT");
    }

    [Fact]
    public async Task Perder_el_foco_del_programa_suelta_el_ptt()
    {
        var m = await MontarAsync(conmutado: true);
        await m.Fonia.PttAbajoAsync();

        await m.Fonia.AlPerderElFocoAsync();

        m.Radio.Ptt.Should().BeFalse();
        m.Fonia.Aviso.Should().Contain("foco");
    }

    [Fact]
    public async Task Si_el_equipo_pasa_a_datos_a_media_pasada_se_corta()
    {
        var m = await MontarAsync(conmutado: true);
        var fin = Fin(m.Control);
        await m.Fonia.PttAbajoAsync();

        m.Radio.PonerModo("PKTUSB");

        (await EsperarFinAsync(fin)).Motivo.Should().Be(MotivoDeSuelta.Cancelado);
        m.Radio.Ptt.Should().BeFalse();
    }

    [Fact]
    public async Task El_tiempo_maximo_de_los_ajustes_se_aplica_a_la_fonia()
    {
        var m = await MontarAsync();

        m.Fonia.Ajustes.TiempoMaximoSegundos = 90;

        m.Control.TiempoMaximoEfectivo.Should().Be(TimeSpan.FromSeconds(90));
        m.Fonia.TextoDelTope.Should().Be("máx. 1:30");
    }

    [Fact]
    public async Task Volumen_silencio_y_ganancia_llegan_a_los_caminos_de_audio()
    {
        var m = await MontarAsync();

        m.Fonia.Ajustes.VolumenRx = 150;
        m.Fonia.Ajustes.SilencioRx = true;
        m.Fonia.Ajustes.GananciaTxDb = 6;

        m.Rx.Ganancia.Should().BeApproximately(1.5f, 0.001f);
        m.Rx.Silenciado.Should().BeTrue();
        m.Tx.Ganancia.Should().BeApproximately(1.995f, 0.01f);
    }

    [Fact]
    public async Task La_escucha_se_abre_del_codec_a_los_altavoces_del_pc()
    {
        var m = await MontarAsync();

        await m.Fonia.AlternarEscuchaAsync();

        m.Rx.Abierto.Should().Be(("codec-in", "altavoces-pc"));
        m.Fonia.Escuchando.Should().BeTrue();

        await m.Fonia.AlternarEscuchaAsync();
        m.Rx.Abierto.Should().BeNull();
    }

    [Fact]
    public async Task Si_la_radio_se_apaga_con_la_escucha_abierta_el_panel_lo_dice()
    {
        var m = await MontarAsync();
        await m.Fonia.AlternarEscuchaAsync();

        m.Rx.Romper(new InvalidOperationException("El dispositivo de audio ya no existe."));
        await EsperaALaVentana.DrenarAsync();

        m.Fonia.Escuchando.Should().BeFalse();
        m.Fonia.Aviso.Should().Contain("se ha cortado");
        m.Control.Escuchando.Should().BeFalse();
    }

    [Fact]
    public async Task Si_deja_de_llegar_audio_del_equipo_avisa_sin_colgarse()
    {
        var m = await MontarAsync();
        await m.Fonia.AlternarEscuchaAsync();
        var ahora = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);
        m.Rx.UltimoAvanceUtc = ahora;

        m.Fonia.AvisoSiNoLlegaAudio(ahora.AddSeconds(1)).Should().BeEmpty();
        m.Fonia.AvisoSiNoLlegaAudio(ahora.AddSeconds(5)).Should().Contain("No llega audio del equipo desde hace 5 s");
    }

    [Fact]
    public void Los_ajustes_de_fonia_se_guardan_y_se_acotan()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-fonia-" + Guid.NewGuid().ToString("N"));
        try
        {
            var ajustes = new AjustesDelPrograma();
            ajustes.Fonia.TiempoMaximoSegundos = 5000;
            ajustes.Fonia.TeclaDelPtt = "F12";
            ajustes.Guardar(carpeta);

            var leidos = AjustesDelPrograma.Leer(carpeta);

            leidos.Fonia.TiempoMaximoSegundos.Should().Be(600);
            leidos.Fonia.TeclaDelPtt.Should().Be("F12");
            leidos.Fonia.ModoDelPtt.Should().Be(ModoDelPttDeFonia.Mantener);
        }
        finally
        {
            if (Directory.Exists(carpeta)) Directory.Delete(carpeta, recursive: true);
        }
    }

    [Theory]
    [InlineData("USB", true)]
    [InlineData("LSB", true)]
    [InlineData("AM", true)]
    [InlineData("FM", true)]
    [InlineData("CW", false)]
    [InlineData("FT8", false)]
    [InlineData("RTTY", false)]
    [InlineData("—", false)]
    public void Solo_los_modos_de_voz_admiten_el_ptt_de_fonia(string modo, bool deVoz) =>
        VistaModeloFonia.EsModoDeVoz(modo).Should().Be(deVoz);

    /// <summary>
    /// Espera a que acabe la pasada y a que la ventana procese lo que dejo encolado: el panel se
    /// entera por el hilo de la ventana, como en el programa.
    /// </summary>
    private static async Task<FinDeFonia> EsperarFinAsync(TaskCompletionSource<FinDeFonia> fin)
    {
        var resultado = await fin.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await EsperaALaVentana.DrenarAsync();
        return resultado;
    }

    private static TaskCompletionSource<FinDeFonia> Fin(ControlDeFonia control)
    {
        var fin = new TaskCompletionSource<FinDeFonia>(TaskCreationOptions.RunContinuationsAsynchronously);
        control.TransmisionTerminada += (_, f) => fin.TrySetResult(f);
        return fin;
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

        public bool EstaAbierto => Abierto is not null;

        public float Ganancia { get; set; } = 1f;

        public bool Silenciado { get; set; }

        public double Nivel => 0;

        public bool Saturando => false;

        public DateTimeOffset? UltimoAvanceUtc { get; set; }

        public event EventHandler<Exception>? Fallo;

        /// <summary>Como el de verdad: al romperse se cierra y avisa.</summary>
        public void Romper(Exception fallo)
        {
            Abierto = null;
            Fallo?.Invoke(this, fallo);
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
    private sealed class EquipoDeMentira : IControlEquipo
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
