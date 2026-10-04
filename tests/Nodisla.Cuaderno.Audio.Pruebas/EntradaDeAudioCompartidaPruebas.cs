using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Captura;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// El recuento de referencias de <see cref="EntradaDeAudioCompartida"/>: lo que hace falta para que
/// «Pausa» en CW de verdad suelte la tarjeta cuando nadie más la necesita, y para que no se la
/// quite a quien sigue escuchando.
/// </summary>
/// <remarks>
/// Esto es justo lo que faltaba para el fallo real: la tarjeta de audio es un <c>AddSingleton</c>
/// que comparten CW, el módem propio y la fonía. Sin recuento, «Pausa» no podía cerrarla (cortaría
/// a los demás si la estuvieran usando) así que, en la práctica, nadie la cerraba nunca y el hilo
/// de captura de Windows seguía corriendo para siempre.
/// </remarks>
public class EntradaDeAudioCompartidaPruebas
{
    /// <summary>Una entrada de mentira que apunta cuántas veces se abre y se cierra de verdad.</summary>
    private sealed class EntradaDeMentira : IEntradaDeAudio
    {
        public int VecesAbierta { get; private set; }

        public int VecesCerrada { get; private set; }

        public bool FallaAlAbrir { get; set; }

        public IReadOnlyList<DispositivoDeAudio> Dispositivos => [];

        public DispositivoDeAudio? Abierto { get; private set; }

        public double Nivel => 0;

        public event EventHandler<BloqueDeAudio>? BloqueCapturado;

        public event EventHandler<HuecoDeAudio>? MuestrasPerdidas;

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default)
        {
            if (FallaAlAbrir) throw new InvalidOperationException("No hay tarjeta, de mentira.");
            VecesAbierta++;
            Abierto = new DispositivoDeAudio(idDispositivo, idDispositivo, true, false);
            return Task.CompletedTask;
        }

        public Task CerrarAsync(CancellationToken ct = default)
        {
            VecesCerrada++;
            Abierto = null;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        // Para que el compilador no se queje de los eventos sin usar fuera de la interfaz.
        internal void Disparar() => BloqueCapturado?.Invoke(this, new BloqueDeAudio(default, 48000, DateTimeOffset.UtcNow));

        internal void DispararHueco() => MuestrasPerdidas?.Invoke(this, new HuecoDeAudio(0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task DosOyentesConElMismoDispositivoSoloAbrenUnaVez()
    {
        var interior = new EntradaDeMentira();
        var compartida = new EntradaDeAudioCompartida(interior);

        await compartida.AbrirAsync("codec", 48000);
        await compartida.AbrirAsync("codec", 48000);

        interior.VecesAbierta.Should().Be(1, "el segundo oyente se suma, no vuelve a abrir la tarjeta");
        compartida.Referencias.Should().Be(2);
    }

    [Fact]
    public async Task LaTarjetaSoloSeCierraCuandoElUltimoOyenteLaSuelta()
    {
        var interior = new EntradaDeMentira();
        var compartida = new EntradaDeAudioCompartida(interior);

        await compartida.AbrirAsync("codec", 48000); // CW
        await compartida.AbrirAsync("codec", 48000); // el módem propio

        await compartida.CerrarAsync(); // CW se pausa
        interior.VecesCerrada.Should().Be(0, "el módem la sigue usando: no se puede cortar la tarjeta");
        compartida.Abierto.Should().NotBeNull();

        await compartida.CerrarAsync(); // el módem para
        interior.VecesCerrada.Should().Be(1, "ya no queda nadie: ahora sí se cierra de verdad");
        compartida.Abierto.Should().BeNull();
    }

    [Fact]
    public async Task SoltarSinHaberPedidoNoHaceNada()
    {
        var interior = new EntradaDeMentira();
        var compartida = new EntradaDeAudioCompartida(interior);

        await compartida.CerrarAsync();

        interior.VecesCerrada.Should().Be(0);
        compartida.Referencias.Should().Be(0);
    }

    [Fact]
    public async Task UnFalloAlAbrirNoDejaUnaReferenciaFantasma()
    {
        var interior = new EntradaDeMentira { FallaAlAbrir = true };
        var compartida = new EntradaDeAudioCompartida(interior);

        var abrir = async () => await compartida.AbrirAsync("codec", 48000);
        await abrir.Should().ThrowAsync<InvalidOperationException>();

        compartida.Referencias.Should().Be(0, "un intento fallido no cuenta como una referencia abierta");

        // Y se puede reintentar sin arrastrar nada del primer intento.
        interior.FallaAlAbrir = false;
        await compartida.AbrirAsync("codec", 48000);
        compartida.Referencias.Should().Be(1);
    }

    [Fact]
    public async Task UnDispositivoDistintoReabreYReiniciaElRecuento()
    {
        var interior = new EntradaDeMentira();
        var compartida = new EntradaDeAudioCompartida(interior);

        await compartida.AbrirAsync("codec-1", 48000);
        await compartida.AbrirAsync("codec-1", 48000);
        await compartida.AbrirAsync("codec-2", 48000); // otro dispositivo: fuerza una reapertura

        interior.VecesAbierta.Should().Be(2);
        compartida.Referencias.Should().Be(1);
        compartida.Abierto!.Id.Should().Be("codec-2");
    }
}
