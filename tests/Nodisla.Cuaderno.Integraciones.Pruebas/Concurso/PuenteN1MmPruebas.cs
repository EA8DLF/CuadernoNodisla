using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Integraciones.Concurso;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Concurso;

/// <summary>Los mensajes XML de N1MM+: estado de la radio, altas, correcciones y bajas.</summary>
public class PuenteN1MmPruebas
{
    private const string RadioInfo = """
        <?xml version="1.0" encoding="utf-8"?>
        <RadioInfo>
          <app>N1MM</app>
          <StationName>SALON</StationName>
          <RadioNr>1</RadioNr>
          <Freq>1407400</Freq>
          <TXFreq>1407450</TXFreq>
          <Mode>USB</Mode>
          <OpCall>EA8DLF</OpCall>
          <IsRunning>True</IsRunning>
          <FocusEntry>4325</FocusEntry>
          <Antenna>3</Antenna>
          <Rotors>Rotor1</Rotors>
          <FocusRadioNr>1</FocusRadioNr>
          <IsStereo>False</IsStereo>
          <IsSplit>True</IsSplit>
          <ActiveRadioNr>1</ActiveRadioNr>
          <IsTransmitting>False</IsTransmitting>
          <FunctionKeyCaption>F1 CQ</FunctionKeyCaption>
          <RadioName>IC-7300</RadioName>
          <AuxAntSelected>0</AuxAntSelected>
          <AuxAntSelectedName></AuxAntSelectedName>
        </RadioInfo>
        """;

    private const string ContactInfo = """
        <?xml version="1.0" encoding="utf-8"?>
        <contactinfo>
          <app>N1MM</app>
          <contestname>CQWW</contestname>
          <contestnr>17</contestnr>
          <timestamp>2026-09-21 10:15:30</timestamp>
          <mycall>EA8DLF</mycall>
          <band>14.0</band>
          <rxfreq>1407400</rxfreq>
          <txfreq>1407400</txfreq>
          <operator>EA8DLF</operator>
          <mode>CW</mode>
          <call>K1ABC</call>
          <countryprefix>K</countryprefix>
          <wpxprefix>K1</wpxprefix>
          <stationprefix>EA8</stationprefix>
          <continent>NA</continent>
          <snt>599</snt>
          <sntnr>12</sntnr>
          <rcv>599</rcv>
          <rcvnr>34</rcvnr>
          <gridsquare>FN42</gridsquare>
          <exchange1>05</exchange1>
          <section></section>
          <comment>Buena senal</comment>
          <qth>Boston</qth>
          <name>Bob</name>
          <power>100</power>
          <misctext></misctext>
          <zone>5</zone>
          <points>3</points>
          <radionr>1</radionr>
          <IsRunQSO>1</IsRunQSO>
          <StationName>SALON</StationName>
          <ID>b4f0ee2e9c9b4a2fa6f7b9f0e5a1c333</ID>
        </contactinfo>
        """;

    [Fact]
    public void El_estado_de_la_radio_se_lee_con_la_frecuencia_en_decenas_de_hercios()
    {
        var mensaje = AnalizadorN1Mm.Analizar(RadioInfo, out var motivo);

        motivo.Should().BeNull();
        var radio = mensaje.Should().BeOfType<EstadoDeRadioConcurso>().Subject;
        radio.NumeroDeRadio.Should().Be(1);
        radio.Frecuencia.Megahercios.Should().Be(14.074m);
        radio.FrecuenciaTx.Megahercios.Should().Be(14.0745m);
        radio.Banda.Nombre.Should().Be("20m");
        radio.Modo.Should().Be("USB");
        radio.EnLlamada.Should().BeTrue();
        radio.EnSplit.Should().BeTrue();
        radio.Transmitiendo.Should().BeFalse();
        radio.NombreDeRadio.Should().Be("IC-7300");
        radio.NombreDeEstacion.Should().Be("SALON");
    }

    [Fact]
    public void Un_contacto_nuevo_se_lee_entero()
    {
        var contacto = AnalizadorN1Mm.Analizar(ContactInfo, out _)
            .Should().BeOfType<ContactoDeConcurso>().Subject;

        contacto.Accion.Should().Be(AccionSobreContacto.Anadido);
        contacto.Call.Valor.Should().Be("K1ABC");
        contacto.MiIndicativo.Valor.Should().Be("EA8DLF");
        contacto.Concurso.Should().Be("CQWW");
        contacto.InstanteUtc.Should().Be(new DateTimeOffset(2026, 9, 21, 10, 15, 30, TimeSpan.Zero));
        contacto.SerieEnviada.Should().Be(12);
        contacto.SerieRecibida.Should().Be(34);
        contacto.Zona.Should().Be(5);
        contacto.Locator.Valor.Should().Be("FN42");
        contacto.EnLlamada.Should().BeTrue();
        contacto.Identificador.Should().Be("b4f0ee2e9c9b4a2fa6f7b9f0e5a1c333");
        contacto.Campos.Should().ContainKey("stationprefix");
    }

    [Fact]
    public void El_contacto_de_concurso_se_convierte_en_un_qso_del_cuaderno()
    {
        var contacto = (ContactoDeConcurso)AnalizadorN1Mm.Analizar(ContactInfo, out _)!;

        var qso = AnalizadorN1Mm.AQso(contacto);

        qso.Call.Valor.Should().Be("K1ABC");
        qso.Band.Nombre.Should().Be("20m");
        qso.Freq.Megahercios.Should().Be(14.074m);
        qso.Mode.Principal.Should().Be("CW");
        qso.InicioUtc.Should().Be(new DateTimeOffset(2026, 9, 21, 10, 15, 30, TimeSpan.Zero));
        qso.InicioUtc.Offset.Should().Be(TimeSpan.Zero);
        qso.RstSent.Texto.Should().Be("599");
        qso.Stx.Should().Be(12);
        qso.Srx.Should().Be(34);
        qso.SrxString.Should().Be("05");
        qso.ContestId.Should().Be("CQWW");
        qso.Cqz.Should().Be(5);
        qso.Cont.Should().Be("NA");
        qso.Pfx.Should().Be("K1");
        qso.TxPwr.Should().Be(100);
        qso.FreqRx.Should().BeNull("no se trabajo en dos frecuencias");
        qso.Origen.Should().Be("N1MM+");
    }

    [Fact]
    public void Una_correccion_llega_como_reemplazo_con_los_datos_antiguos()
    {
        var xml = ContactInfo
            .Replace("<contactinfo>", "<contactreplace>", StringComparison.Ordinal)
            .Replace("</contactinfo>", "</contactreplace>", StringComparison.Ordinal)
            .Replace("<call>K1ABC</call>",
                "<call>K1ABC/P</call><oldcall>K1ABC</oldcall><oldtimestamp>2026-09-21 10:15:30</oldtimestamp>",
                StringComparison.Ordinal);

        var contacto = AnalizadorN1Mm.Analizar(xml, out _).Should().BeOfType<ContactoDeConcurso>().Subject;

        contacto.Accion.Should().Be(AccionSobreContacto.Reemplazado);
        contacto.Call.Valor.Should().Be("K1ABC/P");
        contacto.CallAnterior.Valor.Should().Be("K1ABC");
        contacto.InstanteAnteriorUtc.Should().Be(new DateTimeOffset(2026, 9, 21, 10, 15, 30, TimeSpan.Zero));
    }

    [Fact]
    public void Un_borrado_se_lee_con_su_identificador()
    {
        const string xml = """
            <contactdelete>
              <app>N1MM</app>
              <timestamp>2026-09-21 10:15:30</timestamp>
              <call>K1ABC</call>
              <contestnr>17</contestnr>
              <StationName>SALON</StationName>
              <ID>b4f0ee2e9c9b4a2fa6f7b9f0e5a1c333</ID>
            </contactdelete>
            """;

        var borrado = AnalizadorN1Mm.Analizar(xml, out _).Should().BeOfType<BorradoDeConcurso>().Subject;

        borrado.Call.Valor.Should().Be("K1ABC");
        borrado.NumeroDeConcurso.Should().Be(17);
        borrado.Identificador.Should().Be("b4f0ee2e9c9b4a2fa6f7b9f0e5a1c333");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("esto no es xml")]
    [InlineData("<sinCerrar>")]
    [InlineData("<otracosa><a>1</a></otracosa>")]
    public void Lo_que_no_se_entiende_se_descarta_con_motivo(string xml)
    {
        var mensaje = AnalizadorN1Mm.Analizar(xml, out var motivo);

        mensaje.Should().BeNull();
        motivo.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void El_puente_no_se_cae_con_datagramas_malos()
    {
        var registro = new Nodisla.Cuaderno.Integraciones.Pruebas.Digital.RegistroDePrueba();
        var puente = new PuenteN1MmUdp(new OpcionesPuenteN1Mm(), registro);

        puente.Admitir("basura").Should().BeFalse();
        puente.Admitir(string.Empty).Should().BeFalse();
        puente.Admitir(RadioInfo).Should().BeTrue();

        registro.Lineas.Should().Contain(t => t.Contains("descartado", StringComparison.Ordinal));
        puente.Radios.Should().ContainKey(1);
    }

    [Fact]
    public void El_puente_avisa_de_cada_clase_de_mensaje()
    {
        var puente = new PuenteN1MmUdp();
        var qsos = new List<Qso>();
        var radios = new List<EstadoDeRadioConcurso>();
        var borrados = new List<BorradoDeConcurso>();
        puente.QsoRegistrado += (_, q) => qsos.Add(q);
        puente.RadioRecibida += (_, r) => radios.Add(r);
        puente.ContactoBorrado += (_, b) => borrados.Add(b);

        puente.Admitir(RadioInfo);
        puente.Admitir(ContactInfo);
        puente.Admitir("""
            <contactdelete><app>N1MM</app><timestamp>2026-09-21 10:15:30</timestamp>
            <call>K1ABC</call><contestnr>17</contestnr><ID>x</ID></contactdelete>
            """);

        radios.Should().HaveCount(1);
        qsos.Should().HaveCount(1);
        borrados.Should().HaveCount(1);
    }

    [Fact]
    public async Task El_puente_escucha_de_verdad_por_el_socket()
    {
        var puerto = PuertoLibre();
        await using var puente = new PuenteN1MmUdp(new OpcionesPuenteN1Mm { Puerto = puerto });

        var recibido = new TaskCompletionSource<Qso>(TaskCreationOptions.RunContinuationsAsynchronously);
        puente.QsoRegistrado += (_, q) => recibido.TrySetResult(q);

        await puente.ArrancarAsync();

        using var emisor = new UdpClient(AddressFamily.InterNetwork);
        var datos = Encoding.UTF8.GetBytes(ContactInfo);
        await emisor.SendAsync(datos, datos.Length, new IPEndPoint(IPAddress.Loopback, puerto));

        var terminada = await Task.WhenAny(recibido.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        terminada.Should().Be(recibido.Task, "el datagrama tenia que haber llegado");

        (await recibido.Task).Call.Valor.Should().Be("K1ABC");

        await puente.PararAsync();
    }

    private static int PuertoLibre()
    {
        using var sonda = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        sonda.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)sonda.LocalEndPoint!).Port;
    }
}
