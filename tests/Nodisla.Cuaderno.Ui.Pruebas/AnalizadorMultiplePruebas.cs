using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.Recursos;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La tecla MULTI: analizador arriba, osciloscopio y AF-FFT del audio de recepcion abajo (como
/// la pantalla MULTI del FT-710). Es de la pantalla del programa y no manda nada a la radio.
/// </summary>
public sealed class AnalizadorMultiplePruebas
{
    [Fact]
    public void La_tecla_multi_pone_y_quita_la_vista_multiple_y_el_rotulo_lo_dice()
    {
        var modelo = new VistaModeloAnalizador(null);
        modelo.Ajustar(new AjusteDelAnalizador(200_000, 2, ModoDelAnalizador.Centro, TresD: false, Ampliado: false));

        modelo.AlternarMultipleCommand.Execute(null);
        modelo.Multiple.Should().BeTrue();
        modelo.RotuloEnPantalla.Should().Be("CENTER  SPEED FAST1  SPAN 200kHz  MULTI");

        modelo.AlternarMultipleCommand.Execute(null);
        modelo.Multiple.Should().BeFalse();
        modelo.RotuloEnPantalla.Should().Be("CENTER  SPEED FAST1  SPAN 200kHz");
    }

    [Fact]
    public void Con_audio_el_osciloscopio_y_el_af_fft_reciben_las_muestras()
    {
        EnHiloDeInterfaz(() =>
        {
            var audio = new AudioDeMentira { EstaAbierto = true };
            var modelo = new VistaModeloAnalizador(null, audio: audio);

            // Sin MULTI no se escucha el audio.
            audio.Emitir(Tono(1_000, 960));
            modelo.BloquesDeAudio.Should().Be(0);

            modelo.AlternarMultipleCommand.Execute(null);
            modelo.HayAudio.Should().BeTrue();
            for (var i = 0; i < 10; i++) audio.Emitir(Tono(1_000, 960, i * 960));
            modelo.PintarElAudio();

            modelo.BloquesDeAudio.Should().Be(10);
            modelo.PintorDelAudio.FrecuenciaDelPico.Should().BeApproximately(1_000, 25, "el AF-FFT ve el tono de 1 kHz");
            modelo.PintorDelAudio.Osciloscopio.Should().Contain(p => (p & 0x00FFFFFF) == 0x2FE6F0, "la onda se pinta en cian");
            modelo.ImagenDelOsciloscopio.Should().NotBeNull();
            modelo.ImagenDelAfFft!.PixelWidth.Should().Be(PintorDelAudio.Ancho);

            // Al quitar MULTI se deja de escuchar.
            modelo.AlternarMultipleCommand.Execute(null);
            audio.Emitir(Tono(1_000, 960));
            modelo.BloquesDeAudio.Should().Be(10);
            audio.Suscritos.Should().Be(0);
        });
    }

    [Fact]
    public void Sin_audio_abierto_la_vista_multiple_lo_dice()
    {
        var modelo = new VistaModeloAnalizador(null, audio: new AudioDeMentira { EstaAbierto = false });
        modelo.AlternarMultipleCommand.Execute(null);
        modelo.HayAudio.Should().BeFalse();

        new VistaModeloAnalizador(null).HayAudio.Should().BeFalse("sin entrada de audio tampoco hay");
    }

    [Fact]
    public void El_osciloscopio_ocupa_diez_divisiones_de_diez_milisegundos()
    {
        PintorDelAudio.MuestrasNecesarias(48_000).Should().Be(4_800);
        var pintor = new PintorDelAudio();
        pintor.Pintar(Tono(2_500, 4_800), 48_000);
        pintor.FrecuenciaDelPico.Should().BeApproximately(2_500, 25);
    }

    private static float[] Tono(double hz, int cuantas, int desde = 0) =>
        [.. Enumerable.Range(desde, cuantas).Select(i => (float)(0.3 * Math.Sin(2 * Math.PI * hz * i / 48_000.0)))];

    private static void EnHiloDeInterfaz(Action accion)
    {
        Exception? fallo = null;
        var hilo = new Thread(() =>
        {
            try { accion(); }
            catch (Exception ex) { fallo = ex; }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        if (fallo is not null) throw new InvalidOperationException(fallo.Message, fallo);
    }

    private sealed class AudioDeMentira : IEntradaDeAudio
    {
        private EventHandler<BloqueDeAudio>? _bloque;

        public bool EstaAbierto { get; init; }

        public int Suscritos => _bloque?.GetInvocationList().Length ?? 0;

        public IReadOnlyList<DispositivoDeAudio> Dispositivos => [];

        public DispositivoDeAudio? Abierto => EstaAbierto ? new("codec", "Codec USB (de mentira)", true, true) : null;

        public double Nivel => 0.5;

        public event EventHandler<BloqueDeAudio>? BloqueCapturado
        {
            add => _bloque += value;
            remove => _bloque -= value;
        }

        public event EventHandler<HuecoDeAudio>? MuestrasPerdidas
        {
            add { }
            remove { }
        }

        public void Emitir(float[] muestras) =>
            _bloque?.Invoke(this, new BloqueDeAudio(muestras, 48_000, DateTimeOffset.UtcNow));

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default) => Task.CompletedTask;

        public Task CerrarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
