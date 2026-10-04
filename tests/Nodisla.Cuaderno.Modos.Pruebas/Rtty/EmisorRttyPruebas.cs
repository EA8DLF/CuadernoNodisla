using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Rtty;

namespace Nodisla.Cuaderno.Modos.Pruebas.Rtty;

/// <summary>
/// El emisor de RTTY de extremo a extremo: pide antena una vez, genera y reproduce el audio real
/// por una salida de mentira, y lo que sale vuelve a entrar por <see cref="CanalRtty"/> igual que
/// en <c>BancoRtty</c>. Nada de esto toca hardware: la salida y el vigilante son dobles.
/// </summary>
public class EmisorRttyPruebas
{
    private const int Frecuencia = 8000;
    private const double TonoHz = 1500;

    [Fact]
    public async Task EmiteYElCanalLoDecodificaEnBucleCerrado()
    {
        var salida = new SalidaDeMentira();
        var vigilante = new VigilanteDeMentira();
        var opciones = new OpcionesDelEmisorRtty { FrecuenciaDeMuestreo = Frecuencia };
        var emisor = new EmisorRtty(salida, vigilante, opciones) { TonoDeMarcaHz = TonoHz };

        var texto = "CQ CQ CQ DE EA8DLF EA8DLF K";
        var fin = await emisor.TransmitirAsync(texto, "prueba");

        fin.Should().Be(FinDelEnvioRtty.Completo);
        vigilante.Pedidas.Should().Be(1, "se pide la antena una sola vez, no una vez por carácter");
        vigilante.UltimaTransmision!.Sueltas.Should().Be(1, "el PTT se suelta al terminar");
        emisor.Enviando.Should().BeFalse();

        var canal = new CanalRtty(Frecuencia, TonoHz, new ParametrosRtty());
        var recibido = new System.Text.StringBuilder();
        canal.Texto += t => recibido.Append(t);
        foreach (var x in salida.Reproducido) canal.Anadir(x);

        canal.Enganchado.Should().BeTrue("la señal generada es limpia, sin ruido");
        recibido.ToString().Should().Contain("EA8DLF").And.Contain("CQ");
    }

    private sealed class SalidaDeMentira : ISalidaDeAudio
    {
        private readonly List<float> _reproducido = [];

        public float[] Reproducido => _reproducido.ToArray();

        public DateTimeOffset? UltimoAvanceUtc => DateTimeOffset.UtcNow;

        public IReadOnlyList<DispositivoDeAudio> Dispositivos { get; } = [];

        public DispositivoDeAudio? Abierto { get; private set; }

        public Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default) => Task.CompletedTask;

        public Task CerrarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default)
        {
            _reproducido.AddRange(muestras.ToArray());
            return Task.CompletedTask;
        }

        public Task SilenciarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class VigilanteDeMentira : IVigilantePtt
    {
        public int Pedidas { get; private set; }

        public TransmisionDeMentira? UltimaTransmision { get; private set; }

        public TimeSpan TiempoMaximo => TimeSpan.FromMinutes(3);

        public TimeSpan TiempoSinLatido => TimeSpan.FromSeconds(10);

        public bool EnAntena => UltimaTransmision?.EnAntena ?? false;

        public event EventHandler<MotivoDeSuelta>? PttSoltado;

        public Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default)
        {
            Pedidas++;
            UltimaTransmision = new TransmisionDeMentira();
            _ = PttSoltado;
            return Task.FromResult<ITransmisionEnCurso>(UltimaTransmision);
        }

        public Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico)
        {
            UltimaTransmision?.Cerrar();
            return Task.CompletedTask;
        }

        public sealed class TransmisionDeMentira : ITransmisionEnCurso
        {
            public bool EnAntena { get; private set; } = true;

            public int Latidos { get; private set; }

            public int Sueltas { get; private set; }

            public void Latir() => Latidos++;

            public void Cerrar() => EnAntena = false;

            public ValueTask DisposeAsync()
            {
                Sueltas++;
                EnAntena = false;
                return ValueTask.CompletedTask;
            }
        }
    }
}
