using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Integraciones.Fldigi;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Fldigi;

/// <summary>
/// FLDigi como modem externo: sondeo del texto recibido, estado del transmisor y, sobre
/// todo, que transmitir pase por el vigilante del PTT.
/// </summary>
public class ModemFldigiPruebas
{
    private static readonly TimeSpan Paciencia = TimeSpan.FromSeconds(10);

    /// <summary>FLDigi de mentira que contesta segun el metodo que le piden.</summary>
    private sealed class FldigiDeMentira : HttpMessageHandler
    {
        private static readonly Regex Metodo = new(@"<methodName>([^<]+)</methodName>", RegexOptions.Compiled);

        /// <summary>Lo que contesta <c>main.get_trx_state</c>.</summary>
        public string Trx { get; set; } = "RX";

        /// <summary>Texto que FLDigi entregara en el proximo <c>rx.get_data</c>.</summary>
        public string PorRecibir { get; set; } = string.Empty;

        /// <summary>Modems que dice conocer.</summary>
        public IReadOnlyList<string> Modems { get; set; } = ["NULL", "CW", "BPSK31", "RTTY", "ThrobX"];

        /// <summary>Metodos que se le han llamado, en orden.</summary>
        public ConcurrentQueue<string> Llamadas { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage peticion, CancellationToken ct)
        {
            var cuerpo = peticion.Content is null
                ? string.Empty
                : await peticion.Content.ReadAsStringAsync(ct);
            var metodo = Metodo.Match(cuerpo) is { Success: true } m ? m.Groups[1].Value : "?";
            Llamadas.Enqueue(metodo);

            var valor = metodo switch
            {
                "fldigi.version" => "<string>4.2.06</string>",
                "main.get_trx_state" => $"<string>{Trx}</string>",
                "rx.get_data" => $"<base64>{Convert.ToBase64String(Encoding.UTF8.GetBytes(Tomar()))}</base64>",
                "modem.get_names" => Lista(Modems),
                "main.get_frequency" => "<double>14070000</double>",
                _ => "<string></string>",
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $"<?xml version=\"1.0\"?><methodResponse><params><param><value>{valor}</value></param></params></methodResponse>",
                    Encoding.UTF8,
                    "text/xml"),
            };
        }

        private string Tomar()
        {
            var texto = PorRecibir;
            PorRecibir = string.Empty;
            return texto;
        }

        private static string Lista(IReadOnlyList<string> valores)
        {
            var datos = string.Concat(valores.Select(v => $"<value><string>{v}</string></value>"));
            return $"<array><data>{datos}</data></array>";
        }
    }

    /// <summary>Vigilante del PTT de mentira, que apunta lo que le piden.</summary>
    private sealed class VigilanteDeMentira : IVigilantePtt
    {
        private readonly TaskCompletionSource _primerLatido =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Transmision? _actual;

        public TimeSpan TiempoMaximo => TimeSpan.FromMinutes(2);

        public TimeSpan TiempoSinLatido => TimeSpan.FromSeconds(10);

        public bool EnAntena => _actual is { EnAntena: true };

        public int Peticiones { get; private set; }

        public int Sueltas { get; private set; }

        public int Latidos { get; private set; }

        /// <summary>Termina en cuanto quien transmite da su primera senial de vida.</summary>
        public Task PrimerLatido => _primerLatido.Task;

        public event EventHandler<MotivoDeSuelta>? PttSoltado;

        public Task<ITransmisionEnCurso> PedirAntenaAsync(string motivo, CancellationToken ct = default)
        {
            Peticiones++;
            _actual = new Transmision(this);
            return Task.FromResult<ITransmisionEnCurso>(_actual);
        }

        public Task SoltarYaAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Panico)
        {
            Soltar(motivo);
            return Task.CompletedTask;
        }

        private void Soltar(MotivoDeSuelta motivo)
        {
            if (_actual is null) return;
            _actual = null;
            Sueltas++;
            PttSoltado?.Invoke(this, motivo);
        }

        private sealed class Transmision(VigilanteDeMentira vigilante) : ITransmisionEnCurso
        {
            public bool EnAntena { get; private set; } = true;

            public void Latir()
            {
                vigilante.Latidos++;
                vigilante._primerLatido.TrySetResult();
            }

            public ValueTask DisposeAsync()
            {
                if (EnAntena)
                {
                    EnAntena = false;
                    vigilante.Soltar(MotivoDeSuelta.Normal);
                }
                return ValueTask.CompletedTask;
            }
        }
    }

    private static OpcionesFldigi Rapido => new() { Sondeo = TimeSpan.FromMilliseconds(20) };

    [Fact]
    public async Task Enviar_texto_pide_antena_antes_de_transmitir()
    {
        var falso = new FldigiDeMentira { Trx = "TX" };
        var vigilante = new VigilanteDeMentira();
        await using var modem = new ModemFldigi(Rapido, vigilante, new HttpClient(falso));

        await modem.EnviarTextoAsync("EA8DLF de EA1ABC 599");

        vigilante.Peticiones.Should().Be(1);
        vigilante.EnAntena.Should().BeTrue();
        falso.Llamadas.Should().ContainInOrder("text.add_tx", "main.tx");
    }

    [Fact]
    public async Task Cuando_fldigi_vuelve_a_recibir_se_suelta_la_antena()
    {
        var falso = new FldigiDeMentira { Trx = "TX" };
        var vigilante = new VigilanteDeMentira();
        var suelto = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vigilante.PttSoltado += (_, _) => suelto.TrySetResult();

        await using var modem = new ModemFldigi(Rapido, vigilante, new HttpClient(falso));
        await modem.ConectarAsync();
        await modem.EnviarTextoAsync("CQ CQ de EA8DLF");

        vigilante.EnAntena.Should().BeTrue();
        // Hay que latir mientras el equipo esta en antena: se espera al primer latido.
        await vigilante.PrimerLatido.WaitAsync(Paciencia);

        // FLDigi termina la cola y vuelve a recepcion.
        falso.Trx = "RX";
        await suelto.Task.WaitAsync(Paciencia);

        vigilante.EnAntena.Should().BeFalse();
        vigilante.Sueltas.Should().Be(1);
        vigilante.Latidos.Should().BeGreaterThan(0);
        modem.Estado.Should().Be(EstadoDelModem.Recibiendo);
    }

    [Fact]
    public async Task Abortar_corta_la_transmision_y_suelta_la_antena()
    {
        var falso = new FldigiDeMentira { Trx = "TX" };
        var vigilante = new VigilanteDeMentira();
        await using var modem = new ModemFldigi(Rapido, vigilante, new HttpClient(falso));

        await modem.EnviarTextoAsync("texto que no va a salir");
        await modem.AbortarTransmisionAsync();

        falso.Llamadas.Should().ContainInOrder("main.abort", "text.clear_tx", "main.rx");
        vigilante.EnAntena.Should().BeFalse();
    }

    [Fact]
    public async Task Sin_vigilante_se_transmite_igual_pero_el_aviso_esta_escrito()
    {
        var falso = new FldigiDeMentira { Trx = "TX" };
        await using var modem = new ModemFldigi(Rapido, ptt: null, new HttpClient(falso));

        await modem.EnviarTextoAsync("prueba");

        falso.Llamadas.Should().Contain("main.tx");
    }

    [Fact]
    public async Task El_texto_recibido_llega_como_evento()
    {
        var falso = new FldigiDeMentira { PorRecibir = "CQ CQ DE EA8DLF EA8DLF K" };
        var llegado = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var modem = new ModemFldigi(Rapido, null, new HttpClient(falso));
        modem.TextoRecibido += (_, t) => llegado.TrySetResult(t);

        await modem.ConectarAsync();
        var texto = await llegado.Task.WaitAsync(Paciencia);

        texto.Should().Be("CQ CQ DE EA8DLF EA8DLF K");
        modem.EstaConectado.Should().BeTrue();
        modem.Estado.Should().Be(EstadoDelModem.Recibiendo);
    }

    [Fact]
    public async Task Solo_se_ofrecen_los_modems_que_se_pueden_registrar_en_el_cuaderno()
    {
        var falso = new FldigiDeMentira();
        await using var modem = new ModemFldigi(Rapido, null, new HttpClient(falso));

        var modos = await modem.ModosDisponiblesAsync();

        modos.Select(m => m.NombreUsual).Should().Contain(["CW", "PSK31", "RTTY"]);
        // NULL no es un modo y ThrobX no se puede traducir a ADIF: no se ofrecen.
        modos.Select(m => m.NombreUsual).Should().NotContain("NULL");
    }

    [Fact]
    public async Task Si_fldigi_no_esta_conectar_lo_dice()
    {
        var falso = new FldigiDeMentira();
        using var http = new HttpClient(new HandlerQueFalla());
        await using var modem = new ModemFldigi(Rapido, null, http);

        var conectar = async () => await modem.ConectarAsync();

        await conectar.Should().ThrowAsync<ErrorXmlRpc>();
        modem.EstaConectado.Should().BeFalse();
    }

    private sealed class HandlerQueFalla : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct) =>
            throw new HttpRequestException("conexión rechazada");
    }
}
