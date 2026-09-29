using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.HamQth;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>Consulta de indicativos en QRZ.com y HamQTH, las dos con sesion.</summary>
public class ConsultaDeIndicativoPruebas
{
    private const string SesionDeQrz = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Session><Key>CLAVE-1</Key><Count>12</Count></Session>
        </QRZDatabase>
        """;

    private const string SesionDeQrzRenovada = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Session><Key>CLAVE-2</Key></Session>
        </QRZDatabase>
        """;

    private const string FichaDeQrz = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Callsign>
            <call>EA8DLF</call>
            <fname>Luis Alberto</fname>
            <name>Ejemplo</name>
            <addr1>Calle de ejemplo 1</addr1>
            <addr2>Villa Ejemplo</addr2>
            <state>GC</state>
            <country>Canary Islands</country>
            <lat>40.0208</lat>
            <lon>0.0417</lon>
            <grid>JN00aa</grid>
            <cqzone>33</cqzone>
            <ituzone>36</ituzone>
            <dxcc>29</dxcc>
            <qslmgr>DIRECT</qslmgr>
            <email>ea8dlf@ejemplo.invalido</email>
          </Callsign>
          <Session><Key>CLAVE-1</Key></Session>
        </QRZDatabase>
        """;

    private const string SesionCaducadaDeQrz = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Session><Error>Session Timeout</Error></Session>
        </QRZDatabase>
        """;

    private const string NoEncontradoEnQrz = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Session><Key>CLAVE-1</Key><Error>Not found: ZZ9ZZ</Error></Session>
        </QRZDatabase>
        """;

    private const string SesionDeHamQth = """
        <?xml version="1.0"?>
        <HamQTH version="2.7" xmlns="https://www.hamqth.com">
          <session><session_id>SESION-1</session_id></session>
        </HamQTH>
        """;

    private const string FichaDeHamQth = """
        <?xml version="1.0"?>
        <HamQTH version="2.7" xmlns="https://www.hamqth.com">
          <search>
            <callsign>ea8dlf</callsign>
            <nick>Luis</nick>
            <adr_name>Luis Alberto</adr_name>
            <adr_city>Villa Ejemplo</adr_city>
            <country>Canary Islands</country>
            <grid>JN00aa</grid>
            <latitude>40.02</latitude>
            <longitude>0.04</longitude>
            <cq>33</cq>
            <itu>36</itu>
            <adif>29</adif>
            <qsl_via>DIRECT</qsl_via>
          </search>
        </HamQTH>
        """;

    private const string SesionCaducadaDeHamQth = """
        <?xml version="1.0"?>
        <HamQTH version="2.7" xmlns="https://www.hamqth.com">
          <session><error>Session does not exist or expired</error></session>
        </HamQTH>
        """;

    private static ConsultaQrzCom Qrz(ManejadorFalso manejador)
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.QrzContrasena, "secreta");
        return new ConsultaQrzCom(
            new FabricaFalsa(manejador),
            credenciales,
            new OpcionesQrz { Usuario = "EA8DLF" },
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));
    }

    private static ConsultaHamQth HamQth(ManejadorFalso manejador)
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.HamQthContrasena, "secreta");
        return new ConsultaHamQth(
            new FabricaFalsa(manejador),
            credenciales,
            new OpcionesHamQth { Usuario = "EA8DLF" },
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task Qrz_abre_sesion_una_vez_y_la_reutiliza()
    {
        var manejador = ManejadorFalso.PorTurnos(SesionDeQrz, FichaDeQrz, FichaDeQrz);
        var consulta = Qrz(manejador);

        await consulta.ConsultarAsync(Indicativo.Parse("EA8DLF"));
        await consulta.ConsultarAsync(Indicativo.Parse("EA8DLF"));

        // Una llamada de sesion y dos de consulta, no dos de sesion.
        manejador.Direcciones.Should().HaveCount(3);
        manejador.Direcciones[0].Query.Should().Contain("username=EA8DLF");
        manejador.Direcciones[1].Query.Should().Contain("s=CLAVE-1");
    }

    [Fact]
    public async Task Qrz_renueva_la_sesion_caducada_y_repite_la_consulta()
    {
        var manejador = ManejadorFalso.PorTurnos(
            SesionDeQrz, SesionCaducadaDeQrz, SesionDeQrzRenovada, FichaDeQrz);
        var consulta = Qrz(manejador);

        var ficha = await consulta.ConsultarAsync(Indicativo.Parse("EA8DLF"));

        ficha.Should().NotBeNull();
        manejador.Direcciones.Should().HaveCount(4);
        manejador.Direcciones[3].Query.Should().Contain("s=CLAVE-2");
    }

    [Fact]
    public async Task La_ficha_de_qrz_se_traduce_entera()
    {
        var manejador = ManejadorFalso.PorTurnos(SesionDeQrz, FichaDeQrz);

        var ficha = await Qrz(manejador).ConsultarAsync(Indicativo.Parse("EA8DLF"));

        ficha.Should().NotBeNull();
        ficha!.Nombre.Should().Be("Luis Alberto Ejemplo");
        ficha.Localidad.Should().Be("Villa Ejemplo");
        ficha.Localizador.Valor.Should().Be("JN00AA");
        ficha.ZonaCq.Should().Be(33);
        ficha.Dxcc.Should().Be(29);
        ficha.GestorQsl.Should().Be("DIRECT");
        ficha.Fuente.Should().Be("QRZ.com");
    }

    [Fact]
    public async Task Un_indicativo_que_qrz_no_conoce_devuelve_nulo_y_no_revienta()
    {
        var manejador = ManejadorFalso.PorTurnos(SesionDeQrz, NoEncontradoEnQrz);

        var ficha = await Qrz(manejador).ConsultarAsync(Indicativo.Parse("ZZ9ZZ"));

        ficha.Should().BeNull();
    }

    [Fact]
    public async Task Hamqth_abre_sesion_y_traduce_la_ficha()
    {
        var manejador = ManejadorFalso.PorTurnos(SesionDeHamQth, FichaDeHamQth);

        var ficha = await HamQth(manejador).ConsultarAsync(Indicativo.Parse("EA8DLF"));

        ficha.Should().NotBeNull();
        ficha!.Indicativo.Valor.Should().Be("EA8DLF");
        ficha.Nombre.Should().Be("Luis Alberto");
        ficha.Localizador.Valor.Should().Be("JN00AA");
        ficha.ZonaItu.Should().Be(36);
        ficha.Fuente.Should().Be("HamQTH");
        manejador.Direcciones[1].Query.Should().Contain("prg=CuadernoNODISLA");
    }

    [Fact]
    public async Task Hamqth_renueva_la_sesion_cuando_el_servicio_dice_que_ya_no_existe()
    {
        var manejador = ManejadorFalso.PorTurnos(
            SesionDeHamQth, SesionCaducadaDeHamQth, SesionDeHamQth, FichaDeHamQth);

        var ficha = await HamQth(manejador).ConsultarAsync(Indicativo.Parse("EA8DLF"));

        ficha.Should().NotBeNull();
        manejador.Direcciones.Should().HaveCount(4);
    }

    [Fact]
    public async Task Sin_credenciales_se_avisa_en_vez_de_llamar_al_servicio()
    {
        var manejador = ManejadorFalso.ConTexto(string.Empty);
        var consulta = new ConsultaQrzCom(
            new FabricaFalsa(manejador),
            new AlmacenDeCredencialesEnMemoria(),
            new OpcionesQrz { Usuario = "EA8DLF" });

        consulta.EstaDisponible.Should().BeFalse();
        var accion = async () => await consulta.ConsultarAsync(Indicativo.Parse("EA8DLF"));

        await accion.Should().ThrowAsync<InvalidOperationException>();
        manejador.Direcciones.Should().BeEmpty();
    }
}
