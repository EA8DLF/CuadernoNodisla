using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Integraciones.Fldigi;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Fldigi;

/// <summary>
/// Puente con FLDigi por XML-RPC, contra un FLDigi de mentira que contesta lo mismo que el
/// de verdad.
/// </summary>
public class PuenteFldigiPruebas
{
    /// <summary>FLDigi de mentira: guarda lo que se le pide y contesta lo que le digan.</summary>
    private sealed class FldigiDeMentira : HttpMessageHandler
    {
        private readonly Func<string, string> _contestar;

        public FldigiDeMentira(Func<string, string> contestar) => _contestar = contestar;

        /// <summary>Cuerpo de cada peticion recibida.</summary>
        public ConcurrentQueue<string> Peticiones { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage peticion, CancellationToken ct)
        {
            var cuerpo = peticion.Content is null
                ? string.Empty
                : await peticion.Content.ReadAsStringAsync(ct);
            Peticiones.Enqueue(cuerpo);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_contestar(cuerpo), Encoding.UTF8, "text/xml"),
            };
        }
    }

    private static string Respuesta(string valor) =>
        $"<?xml version=\"1.0\"?><methodResponse><params><param><value>{valor}</value></param></params></methodResponse>";

    private static PuenteFldigi Puente(FldigiDeMentira falso) =>
        new(new OpcionesFldigi(), new HttpClient(falso));

    [Fact]
    public async Task Se_lee_la_frecuencia_que_fldigi_da_en_hercios()
    {
        var falso = new FldigiDeMentira(_ => Respuesta("<double>14074000.000</double>"));
        await using var puente = Puente(falso);

        var frecuencia = await puente.LeerFrecuenciaAsync();

        frecuencia.Megahercios.Should().Be(14.074m);
        falso.Peticiones.Should().ContainSingle()
            .Which.Should().Contain("<methodName>main.get_frequency</methodName>");
    }

    [Fact]
    public async Task Se_pone_la_frecuencia_en_hercios()
    {
        var falso = new FldigiDeMentira(_ => Respuesta("<double>7074000</double>"));
        await using var puente = Puente(falso);

        var anterior = await puente.PonerFrecuenciaAsync(Frecuencia.DesdeMegahercios(14.074m));

        anterior.Megahercios.Should().Be(7.074m);
        falso.Peticiones.Should().ContainSingle()
            .Which.Should().Contain("<double>14074000</double>");
    }

    [Fact]
    public async Task Se_lee_el_modo_traduciendo_el_nombre_del_modem()
    {
        var falso = new FldigiDeMentira(_ => Respuesta("<string>BPSK31</string>"));
        await using var puente = Puente(falso);

        var modo = await puente.LeerModoAsync();

        modo.NombreUsual.Should().Be("PSK31");
        modo.Principal.Should().Be("PSK");
    }

    [Fact]
    public async Task Se_pone_el_modem_que_corresponde_al_modo()
    {
        var falso = new FldigiDeMentira(_ => Respuesta("<string></string>"));
        await using var puente = Puente(falso);

        Modo.TryParse("PSK31", null, out var psk).Should().BeTrue();
        await puente.PonerModoAsync(psk);

        falso.Peticiones.Should().ContainSingle().Which.Should().Contain("BPSK31");
    }

    [Fact]
    public async Task El_texto_recibido_viene_en_base64()
    {
        var texto = "CQ CQ DE EA8DLF EA8DLF K";
        var falso = new FldigiDeMentira(_ =>
            Respuesta($"<base64>{Convert.ToBase64String(Encoding.UTF8.GetBytes(texto))}</base64>"));
        await using var puente = Puente(falso);

        var recibido = await puente.LeerRecibidoAsync();

        recibido.Should().Be(texto);
    }

    [Fact]
    public async Task Se_manda_texto_a_transmitir()
    {
        var falso = new FldigiDeMentira(_ => Respuesta("<string></string>"));
        await using var puente = Puente(falso);

        await puente.EnviarTextoAsync("EA8DLF de EA1ABC 599 <TX>");

        var peticion = falso.Peticiones.Single();
        peticion.Should().Contain("<methodName>text.add_tx</methodName>");
        // Los signos de XML del texto van escapados, no en crudo.
        peticion.Should().Contain("&lt;TX&gt;");
    }

    [Theory]
    [InlineData("RX", EstadoDeTransmision.Recibiendo)]
    [InlineData("TX", EstadoDeTransmision.Transmitiendo)]
    [InlineData("TUNE", EstadoDeTransmision.Sintonizando)]
    [InlineData("vete a saber", EstadoDeTransmision.Desconocido)]
    public async Task Se_lee_el_estado_del_transmisor(string dice, EstadoDeTransmision esperado)
    {
        var falso = new FldigiDeMentira(_ => Respuesta($"<string>{dice}</string>"));
        await using var puente = Puente(falso);

        (await puente.LeerEstadoAsync()).Should().Be(esperado);
    }

    [Fact]
    public async Task Un_fallo_del_servidor_llega_con_su_codigo_y_su_texto()
    {
        var falso = new FldigiDeMentira(_ =>
            "<?xml version=\"1.0\"?><methodResponse><fault><value><struct>" +
            "<member><name>faultCode</name><value><i4>1</i4></value></member>" +
            "<member><name>faultString</name><value><string>Unknown method</string></value></member>" +
            "</struct></value></fault></methodResponse>");
        await using var puente = Puente(falso);

        var llamada = async () => await puente.VersionAsync();

        var fallo = await llamada.Should().ThrowAsync<ErrorXmlRpc>();
        fallo.Which.Codigo.Should().Be(1);
        fallo.Which.Message.Should().Be("Unknown method");
    }

    [Fact]
    public async Task Si_fldigi_no_esta_se_dice_sin_reventar()
    {
        var falso = new FldigiDeMentira(_ => throw new HttpRequestException("conexión rechazada"));
        await using var puente = Puente(falso);

        (await puente.RespondeAsync()).Should().BeFalse();
    }

    [Fact]
    public void La_peticion_es_xml_rpc_del_de_toda_la_vida()
    {
        var xml = ClienteXmlRpc.ArmarPeticion("text.get_rx", [0, 120]);

        xml.Should().StartWith("<?xml version=\"1.0\"?><methodCall>");
        xml.Should().Contain("<methodName>text.get_rx</methodName>");
        xml.Should().Contain("<value><i4>0</i4></value>");
        xml.Should().Contain("<value><i4>120</i4></value>");
    }
}
