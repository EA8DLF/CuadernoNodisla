using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Reproduccion;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// <see cref="SalidaDeAudioCompartida"/>: el recuento de referencias (igual que
/// <see cref="EntradaDeAudioCompartidaPruebas"/>, pero para la salida) y, sobre todo, que dos
/// emisiones a la vez no se intercalen en la misma tarjeta.
/// </summary>
/// <remarks>
/// Esta clase no tenía ni una sola prueba hasta el 05-10-2026, a pesar de que
/// <see cref="Captura.EntradaDeAudioCompartida"/> sí las tenía: una asimetría que dejó pasar el
/// fallo real de hoy. CW, RTTY y el módem propio comparten esta misma instancia sin conocerse
/// entre sí; si dos emiten a la vez, antes se mezclaban sus muestras en la misma tarjeta («pum
/// pum» en la antena en vez de un tono limpio). Ver <see cref="SalidaDeAudioCompartida"/>.
/// </remarks>
public class SalidaDeAudioCompartidaPruebas
{
    /// <summary>Una salida de mentira que apunta cuántas veces se abre, se cierra y se solapa.</summary>
    private sealed class SalidaDeMentira : ISalidaDeAudio
    {
        private int _reproduciendo;

        public int VecesAbierta { get; private set; }

        public int VecesCerrada { get; private set; }

        public bool FallaAlAbrir { get; set; }

        public TimeSpan DuracionDeCadaEmision { get; set; } = TimeSpan.FromMilliseconds(30);

        /// <summary>Cuántas veces se ha pillado a dos reproducciones sonando a la vez.</summary>
        public int Solapes { get; private set; }

        public List<string> Orden { get; } = [];

        public IReadOnlyList<DispositivoDeAudio> Dispositivos => [];

        public DispositivoDeAudio? Abierto { get; private set; }

        public int FrecuenciaDeMuestreo => 48000;

        public DateTimeOffset? UltimoAvanceUtc => DateTimeOffset.UtcNow;

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default)
        {
            if (FallaAlAbrir) throw new InvalidOperationException("No hay tarjeta, de mentira.");
            VecesAbierta++;
            Abierto = new DispositivoDeAudio(idDispositivo, idDispositivo, false, false);
            return Task.CompletedTask;
        }

        public Task CerrarAsync(CancellationToken ct = default)
        {
            VecesCerrada++;
            Abierto = null;
            lock (Orden) Orden.Add("cierra");
            return Task.CompletedTask;
        }

        public async Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default)
        {
            // Si esto se llama dos veces a la vez, aqui es donde se nota: la segunda entra antes
            // de que la primera haya bajado su marca, exactamente como pasaria si dos emisiones
            // compartieran a la vez la misma SalidaDeAudioWasapi de verdad.
            if (Interlocked.Increment(ref _reproduciendo) > 1) Solapes++;
            lock (Orden) Orden.Add("empieza:" + muestras.Length);
            try
            {
                await Task.Delay(DuracionDeCadaEmision, ct).ConfigureAwait(false);
            }
            finally
            {
                lock (Orden) Orden.Add("acaba:" + muestras.Length);
                Interlocked.Decrement(ref _reproduciendo);
            }
        }

        public Task SilenciarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task DosOyentesConElMismoDispositivoSoloAbrenUnaVez()
    {
        var interior = new SalidaDeMentira();
        var compartida = new SalidaDeAudioCompartida(interior);

        await compartida.AbrirAsync("tarjeta", 48000);
        await compartida.AbrirAsync("tarjeta", 48000);

        interior.VecesAbierta.Should().Be(1, "el segundo que la pide se suma, no vuelve a abrir la tarjeta");
        compartida.Referencias.Should().Be(2);
    }

    [Fact]
    public async Task LaTarjetaSoloSeCierraCuandoElUltimoLaSuelta()
    {
        var interior = new SalidaDeMentira();
        var compartida = new SalidaDeAudioCompartida(interior);

        await compartida.AbrirAsync("tarjeta", 48000); // CW
        await compartida.AbrirAsync("tarjeta", 48000); // el módem propio

        await compartida.CerrarAsync(); // CW para
        interior.VecesCerrada.Should().Be(0, "el módem la sigue usando: no se puede cortar la tarjeta");

        await compartida.CerrarAsync(); // el módem para
        interior.VecesCerrada.Should().Be(1, "ya no queda nadie: ahora sí se cierra de verdad");
    }

    [Fact]
    public async Task UnFalloAlAbrirNoDejaUnaReferenciaFantasma()
    {
        var interior = new SalidaDeMentira { FallaAlAbrir = true };
        var compartida = new SalidaDeAudioCompartida(interior);

        var abrir = async () => await compartida.AbrirAsync("tarjeta", 48000);
        await abrir.Should().ThrowAsync<InvalidOperationException>();

        compartida.Referencias.Should().Be(0, "un intento fallido no cuenta como una referencia abierta");
    }

    [Fact]
    public async Task DosEmisionesALaVezNuncaSuenanALaVez()
    {
        // El fallo real del 05-10-2026: CW/RTTY/el módem comparten esta instancia, y si dos
        // piden emitir casi a la vez (p. ej. al cambiar de modo con una emisión todavía apurando
        // el colchón de la tarjeta), sin turno sus muestras se intercalaban en la misma
        // BufferedWaveProvider. Aqui se piden dos emisiones DE VERDAD a la vez (sin esperar la
        // primera) y se comprueba que la segunda no entra hasta que la primera ha acabado.
        var interior = new SalidaDeMentira();
        var compartida = new SalidaDeAudioCompartida(interior);
        await compartida.AbrirAsync("tarjeta", 48000);

        var primera = compartida.ReproducirAsync(new float[100]);
        var segunda = compartida.ReproducirAsync(new float[200]);
        await Task.WhenAll(primera, segunda);

        interior.Solapes.Should().Be(0, "una emisión entera tiene que acabar antes de que empiece la otra");
        interior.Orden.Should().Equal("empieza:100", "acaba:100", "empieza:200", "acaba:200");
    }

    [Fact]
    public async Task AbrirConOtroDispositivoMientrasHayReferenciasLanzaYNoTocaLaTarjeta()
    {
        // El fallo de verdad tras meter RTTY (confirmado el 05-10-2026, el mismo "pum pum" que
        // 0ef66f5 no llegó a tapar del todo): antes, pedir la salida con un dispositivo o una
        // frecuencia distintos de los que ya tenía otro módulo la reabría de verdad y pisaba
        // _referencias a 1, perdiendo sin avisar la referencia de quien ya la tenía. Ahora tiene
        // que avisar con una excepción y dejar la tarjeta tal cual estaba.
        var interior = new SalidaDeMentira();
        var compartida = new SalidaDeAudioCompartida(interior);
        await compartida.AbrirAsync("tarjeta-del-modem", 48000); // el módem propio, escuchando

        var abrirConOtra = async () => await compartida.AbrirAsync("otra-tarjeta", 48000); // RTTY
        await abrirConOtra.Should().ThrowAsync<InvalidOperationException>();

        interior.VecesAbierta.Should().Be(1, "no se reabre de verdad con lo nuevo");
        interior.VecesCerrada.Should().Be(0, "no se cierra la que ya estaba en uso");
        compartida.Referencias.Should().Be(1, "la referencia de quien ya la tenía no se pierde");
        interior.Abierto!.Id.Should().Be("tarjeta-del-modem", "la tarjeta sigue siendo la de quien la abrió primero");
    }

    [Fact]
    public async Task CerrarEsperaAQueTermineUnaReproduccionEnCursoAntesDeApagarLaTarjeta()
    {
        // El fallo de verdad: abrir/cerrar y reproducir tenían cerrojos distintos, así que cerrar
        // la salida (p. ej. RTTY al terminar su transmisión, si eso dejaba el recuento a cero)
        // podía parar y desechar el dispositivo real MIENTRAS el módem propio todavía le estaba
        // metiendo muestras en medio de sus trece segundos de FT8. Ahora cerrar tiene que esperar
        // a que la reproducción en marcha termine antes de tocar el dispositivo de verdad.
        var interior = new SalidaDeMentira { DuracionDeCadaEmision = TimeSpan.FromMilliseconds(200) };
        var compartida = new SalidaDeAudioCompartida(interior);
        await compartida.AbrirAsync("tarjeta", 48000);

        var reproduciendo = compartida.ReproducirAsync(new float[10]);
        await Task.Delay(30); // le da tiempo a que de verdad esté "sonando"

        var cerrando = compartida.CerrarAsync();
        await Task.WhenAll(reproduciendo, cerrando);

        interior.Orden.Should().Equal("empieza:10", "acaba:10", "cierra");
    }

    [Fact]
    public async Task SilenciarNoEsperaATerminarLaEmisionEnCurso()
    {
        // SilenciarAsync tiene que poder cortar una emision que esta sonando en ese mismo
        // instante, no esperar en la cola del propio cerrojo de ReproducirAsync: para eso existe
        // (AbortarEmisionAsync lo llama justo para interrumpir).
        var interior = new SalidaDeMentira { DuracionDeCadaEmision = TimeSpan.FromSeconds(5) };
        var compartida = new SalidaDeAudioCompartida(interior);
        await compartida.AbrirAsync("tarjeta", 48000);

        var emitiendo = compartida.ReproducirAsync(new float[10]);
        await Task.Delay(30); // le da tiempo a que de verdad este "sonando"

        var silenciar = compartida.SilenciarAsync();
        var completoATiempo = await Task.WhenAny(silenciar, Task.Delay(TimeSpan.FromSeconds(2))) == silenciar;

        completoATiempo.Should().BeTrue("silenciar no puede quedarse esperando a que la propia emisión termine sola");
    }
}
