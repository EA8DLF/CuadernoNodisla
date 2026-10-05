using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Reproduccion;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// Lo que pasa con el PTT mientras suena el audio.
/// </summary>
/// <remarks>
/// <para>
/// Aqui no hay radio ni tarjeta de sonido: hay un vigilante de mentira que apunta los latidos y
/// una salida de mentira que puede portarse bien o colgarse. Es la unica forma de probar el
/// caso que importa —la tarjeta se queda parada con la cola llena— sin poner un equipo de
/// verdad en antena.
/// </para>
/// <para>
/// Lo que se comprueba es que el latido es <b>honesto</b>: mientras el audio avanza se late, y
/// en cuanto deja de avanzar se deja de latir, que es lo que hace que el vigilante baje el PTT.
/// </para>
/// </remarks>
public class EmisionVigiladaPruebas
{
    /// <summary>Un vigilante de PTT de mentira que apunta lo que le hacen.</summary>
    private sealed class VigilanteDeMentira : IVigilantePtt
    {
        private int _latidos;
        private int _enAntena;

        public TimeSpan TiempoMaximo => TimeSpan.FromMinutes(1);

        public TimeSpan TiempoSinLatido => TimeSpan.FromSeconds(15);

        public bool EnAntena => Volatile.Read(ref _enAntena) == 1;

        public int Latidos => Volatile.Read(ref _latidos);

        public MotivoDeSuelta? UltimoMotivo { get; private set; }

        public event EventHandler<MotivoDeSuelta>? PttSoltado;

        public Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default)
        {
            Volatile.Write(ref _enAntena, 1);
            return Task.FromResult<ITransmisionEnCurso>(new Transmision(this));
        }

        public Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico)
        {
            Soltar(motivo);
            return Task.CompletedTask;
        }

        private void Soltar(MotivoDeSuelta motivo)
        {
            if (Interlocked.Exchange(ref _enAntena, 0) == 0)
            {
                return;
            }

            UltimoMotivo = motivo;
            PttSoltado?.Invoke(this, motivo);
        }

        private sealed class Transmision : ITransmisionEnCurso
        {
            private readonly VigilanteDeMentira _vigilante;

            public Transmision(VigilanteDeMentira vigilante) => _vigilante = vigilante;

            public bool EnAntena => _vigilante.EnAntena;

            public void Latir() => Interlocked.Increment(ref _vigilante._latidos);

            public ValueTask DisposeAsync()
            {
                _vigilante.Soltar(MotivoDeSuelta.Normal);
                return ValueTask.CompletedTask;
            }
        }
    }

    /// <summary>Una salida de audio de mentira, que puede portarse bien o colgarse.</summary>
    private sealed class SalidaDeMentira : ISalidaDeAudio
    {
        private long _ultimoAvance = DateTimeOffset.UtcNow.UtcTicks;
        private int _sonando;

        public bool SeCuelga { get; init; }

        public TimeSpan Duracion { get; init; } = TimeSpan.FromMilliseconds(500);

        public bool Silenciada { get; private set; }

        public IReadOnlyList<DispositivoDeAudio> Dispositivos => Array.Empty<DispositivoDeAudio>();

        public DispositivoDeAudio? Abierto => null;

        public int FrecuenciaDeMuestreo => 48000;

        public DateTimeOffset? UltimoAvanceUtc =>
            new DateTimeOffset(Volatile.Read(ref _ultimoAvance), TimeSpan.Zero);

        public bool EstaSonando => Volatile.Read(ref _sonando) == 1;

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task CerrarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public async Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default)
        {
            Volatile.Write(ref _sonando, 1);

            try
            {
                if (SeCuelga)
                {
                    // La tarjeta se queda parada: no avanza ni una muestra mas.
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return;
                }

                var fin = DateTimeOffset.UtcNow + Duracion;
                while (DateTimeOffset.UtcNow < fin)
                {
                    await Task.Delay(20, ct).ConfigureAwait(false);
                    Volatile.Write(ref _ultimoAvance, DateTimeOffset.UtcNow.UtcTicks);
                }
            }
            finally
            {
                Volatile.Write(ref _sonando, 0);
            }
        }

        public Task SilenciarAsync(CancellationToken ct = default)
        {
            Silenciada = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task MientrasElAudioAvanzaSeLateYAlAcabarSeSueltaElPtt()
    {
        var vigilante = new VigilanteDeMentira();
        var salida = new SalidaDeMentira { Duracion = TimeSpan.FromMilliseconds(500) };

        await EmisionVigilada.EmitirAsync(
            vigilante,
            salida,
            new float[4800],
            "prueba",
            pasoDeLatido: TimeSpan.FromMilliseconds(50),
            toleranciaSinAvance: TimeSpan.FromMilliseconds(200));

        vigilante.Latidos.Should().BeGreaterThan(2);
        vigilante.EnAntena.Should().BeFalse();
        vigilante.UltimoMotivo.Should().Be(MotivoDeSuelta.Normal);
        salida.Silenciada.Should().BeTrue();
    }

    [Fact]
    public async Task SiElAudioSeCuelgaSeDejaDeLatirParaQueElVigilanteBajeElPtt()
    {
        var vigilante = new VigilanteDeMentira();
        var salida = new SalidaDeMentira { SeCuelga = true };
        using var corte = new CancellationTokenSource();

        var emision = EmisionVigilada.EmitirAsync(
            vigilante,
            salida,
            new float[4800],
            "prueba de cuelgue",
            pasoDeLatido: TimeSpan.FromMilliseconds(25),
            toleranciaSinAvance: TimeSpan.FromMilliseconds(200),
            registro: null,
            ct: corte.Token);

        // Pasada la tolerancia, los latidos tienen que haberse parado del todo.
        await Task.Delay(500);
        var cuandoYaDeberiaHaberParado = vigilante.Latidos;

        await Task.Delay(500);
        vigilante.Latidos.Should().Be(cuandoYaDeberiaHaberParado);

        // En una emision de verdad, aqui el vigilante ya habria bajado el PTT por falta de
        // latido. Se corta la emision para terminar la prueba.
        await corte.CancelAsync();

        var fallo = async () => await emision;
        await fallo.Should().ThrowAsync<OperationCanceledException>();

        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task SiLaSalidaNoSabeDecirSiAvanzaNoSeLateAOscuras()
    {
        var vigilante = new VigilanteDeMentira();
        var salida = new SalidaMuda();

        await EmisionVigilada.EmitirAsync(
            vigilante,
            salida,
            new float[4800],
            "prueba sin avance",
            pasoDeLatido: TimeSpan.FromMilliseconds(25));

        // Ni un latido inventado: el tope lo pone el tiempo sin latido del vigilante.
        vigilante.Latidos.Should().Be(0);
        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task SiLaSalidaDejaDeSaberSiAvanzaSeDejaDeLatir()
    {
        var vigilante = new VigilanteDeMentira();
        var salida = new SalidaQueSeQuedaCiega();
        using var corte = new CancellationTokenSource();

        var emision = EmisionVigilada.EmitirAsync(
            vigilante,
            salida,
            new float[4800],
            "prueba de ceguera",
            pasoDeLatido: TimeSpan.FromMilliseconds(25),
            toleranciaSinAvance: TimeSpan.FromMilliseconds(200),
            registro: null,
            ct: corte.Token);

        await Task.Delay(200);
        vigilante.Latidos.Should().BeGreaterThan(0);

        // A partir de aqui ya no se sabe si el audio avanza: no se late a ciegas.
        salida.QuedarseCiega();
        await Task.Delay(300);
        var alQuedarseCiega = vigilante.Latidos;

        await Task.Delay(300);
        vigilante.Latidos.Should().Be(alQuedarseCiega);

        await corte.CancelAsync();
        var fallo = async () => await emision;
        await fallo.Should().ThrowAsync<OperationCanceledException>();

        vigilante.EnAntena.Should().BeFalse();
    }

    /// <summary>Una salida que al rato deja de saber si el audio avanza.</summary>
    private sealed class SalidaQueSeQuedaCiega : ISalidaDeAudio
    {
        private int _ciega;

        public DateTimeOffset? UltimoAvanceUtc =>
            Volatile.Read(ref _ciega) == 1 ? null : DateTimeOffset.UtcNow;

        public IReadOnlyList<DispositivoDeAudio> Dispositivos => Array.Empty<DispositivoDeAudio>();

        public DispositivoDeAudio? Abierto => null;

        public int FrecuenciaDeMuestreo => 48000;

        public void QuedarseCiega() => Volatile.Write(ref _ciega, 1);

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task CerrarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default) =>
            Task.Delay(Timeout.Infinite, ct);

        public Task SilenciarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>Una salida que no sabe decir si el audio avanza.</summary>
    private sealed class SalidaMuda : ISalidaDeAudio
    {
        public DateTimeOffset? UltimoAvanceUtc => null;

        public IReadOnlyList<DispositivoDeAudio> Dispositivos => Array.Empty<DispositivoDeAudio>();

        public DispositivoDeAudio? Abierto => null;

        public int FrecuenciaDeMuestreo => 48000;

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task CerrarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default) =>
            Task.Delay(200, ct);

        public Task SilenciarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
