using System.Net;
using System.Xml.Linq;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Consulta;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>
/// La ficha de QRZ.com para completar contactos: suscripcion parcial, marcas de LoTW/eQSL,
/// cache de 24 horas y HamQTH de reserva. Respuestas grabadas; nada sale a la red.
/// </summary>
public sealed class FichaDeQrzPruebas : IDisposable
{
    // Respuesta real de QRZ.com a una cuenta SIN suscripcion XML: solo parte de la ficha y el
    // aviso en <Message>.
    private const string FichaSinSuscripcion = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Callsign>
            <call>DL1ABC</call>
            <fname>Hans</fname>
            <name>Muster</name>
            <addr2>Berlin</addr2>
            <country>Germany</country>
          </Callsign>
          <Session>
            <Key>CLAVE-1</Key>
            <Message>A subscription is required to access the complete record.</Message>
          </Session>
        </QRZDatabase>
        """;

    private const string FichaCompleta = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Callsign>
            <call>DL1ABC</call>
            <fname>Hans</fname>
            <name>Muster</name>
            <addr2>Berlin</addr2>
            <state>BE</state>
            <country>Germany</country>
            <grid>JO62qm</grid>
            <dxcc>230</dxcc>
            <cqzone>14</cqzone>
            <ituzone>28</ituzone>
            <email>dl1abc@ejemplo.invalido</email>
            <qslmgr>BURO</qslmgr>
            <lotw>1</lotw>
            <eqsl>0</eqsl>
            <image>https://ejemplo.invalido/dl1abc.jpg</image>
          </Callsign>
          <Session><Key>CLAVE-1</Key></Session>
        </QRZDatabase>
        """;

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-fichas-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _ahora = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    [Fact]
    public void Sin_suscripcion_da_lo_que_hay_sin_fallar_y_guarda_el_aviso()
    {
        var ficha = ConsultaQrzCom.Interpretar(XDocument.Parse(FichaSinSuscripcion), Indicativo.Parse("DL1ABC"));

        ficha.Should().NotBeNull();
        ficha!.Nombre.Should().Be("Hans Muster");
        ficha.Localidad.Should().Be("Berlin");
        ficha.Localizador.EsVacio.Should().BeTrue();
        ficha.Dxcc.Should().BeNull();
        ficha.Aviso.Should().Contain("subscription");
    }

    [Fact]
    public void La_ficha_completa_trae_lotw_eqsl_zonas_y_foto()
    {
        var ficha = ConsultaQrzCom.Interpretar(XDocument.Parse(FichaCompleta), Indicativo.Parse("DL1ABC"))!;

        ficha.UsaLotw.Should().BeTrue();
        ficha.UsaEqsl.Should().BeFalse();
        ficha.ZonaCq.Should().Be(14);
        ficha.ZonaItu.Should().Be(28);
        ficha.Dxcc.Should().Be(230);
        ficha.GestorQsl.Should().Be("BURO");
        ficha.Imagen.Should().NotBeNull();
    }

    private static ConsultaQrzCom Qrz(ManejadorFalso manejador)
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.QrzContrasena, "secreta");
        return new ConsultaQrzCom(
            new FabricaFalsa(manejador), credenciales, new OpcionesQrz { Usuario = "EA8DLF" },
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));
    }

    private const string Sesion = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Session><Key>CLAVE-1</Key></Session>
        </QRZDatabase>
        """;

    [Fact]
    public async Task La_cache_evita_volver_a_preguntar_y_sobrevive_al_reinicio_durante_24_horas()
    {
        var manejador = ManejadorFalso.PorTurnos(Sesion, FichaCompleta, Sesion, FichaCompleta);
        var qrz = Qrz(manejador);
        var consulta = new ConsultaConCache(() => [qrz], _carpeta, () => _ahora);

        (await consulta.ConsultarAsync(Indicativo.Parse("DL1ABC")))!.Nombre.Should().Be("Hans Muster");
        await consulta.ConsultarAsync(Indicativo.Parse("DL1ABC"));
        manejador.Direcciones.Should().HaveCount(2, "sesión y una consulta; la segunda sale de memoria");

        // Otro arranque del programa: la ficha sale del disco.
        var otra = new ConsultaConCache(() => [Qrz(manejador)], _carpeta, () => _ahora + TimeSpan.FromHours(23));
        var deDisco = await otra.ConsultarAsync(Indicativo.Parse("DL1ABC"));
        deDisco!.Localizador.Valor.Should().BeEquivalentTo("JO62qm");
        deDisco.UsaLotw.Should().BeTrue();
        manejador.Direcciones.Should().HaveCount(2);

        // Pasadas 24 horas se vuelve a preguntar.
        var caducada = new ConsultaConCache(() => [Qrz(manejador)], _carpeta, () => _ahora + TimeSpan.FromHours(25));
        await caducada.ConsultarAsync(Indicativo.Parse("DL1ABC"));
        manejador.Direcciones.Should().HaveCount(4);
    }

    [Fact]
    public async Task Si_qrz_falla_contesta_hamqth()
    {
        var qrz = new ConsultaDoble("QRZ.com") { Fallo = new ServicioNoDisponibleException("sin red") };
        var hamqth = new ConsultaDoble("HamQTH") { Nombre_ = "Hans" };
        var consulta = new ConsultaConCache(() => [qrz, hamqth], carpeta: null, () => _ahora);

        var ficha = await consulta.ConsultarAsync(Indicativo.Parse("DL1ABC"));

        ficha!.Fuente.Should().Be("HamQTH");
    }

    [Fact]
    public async Task Sin_credenciales_no_esta_disponible_y_si_todos_fallan_lo_dice()
    {
        var sinCuenta = new ConsultaDoble("QRZ.com") { Disponible = false };
        new ConsultaConCache(() => [sinCuenta], null).EstaDisponible.Should().BeFalse();

        var caido = new ConsultaDoble("QRZ.com") { Fallo = new ServicioNoDisponibleException("sin red") };
        var consulta = new ConsultaConCache(() => [caido], null);
        var accion = () => consulta.ConsultarAsync(Indicativo.Parse("DL1ABC"));
        await accion.Should().ThrowAsync<ServicioNoDisponibleException>();
    }

    [Fact]
    public async Task Una_sesion_rechazada_por_qrz_no_se_confunde_con_no_encontrado()
    {
        const string malaContrasena = """
            <?xml version="1.0" encoding="utf-8" ?>
            <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
              <Session><Error>Username/password incorrect</Error></Session>
            </QRZDatabase>
            """;
        var consulta = new ConsultaConCache(() => [Qrz(ManejadorFalso.ConTexto(malaContrasena))], null);

        var accion = () => consulta.ConsultarAsync(Indicativo.Parse("DL1ABC"));
        await accion.Should().ThrowAsync<RespuestaDelServicioException>();
    }

    [Fact]
    public async Task Qrz_caido_con_500_acaba_en_fallo_y_no_en_ficha_vacia()
    {
        var manejador = new ManejadorFalso((_, _) => ManejadorFalso.Respuesta("caído", HttpStatusCode.ServiceUnavailable));
        var consulta = new ConsultaConCache(() => [Qrz(manejador)], null);

        var accion = () => consulta.ConsultarAsync(Indicativo.Parse("DL1ABC"));
        await accion.Should().ThrowAsync<ServicioNoDisponibleException>();
    }

    private sealed class ConsultaDoble(string nombre) : IConsultaIndicativo
    {
        public string Nombre => nombre;
        public bool Disponible { get; set; } = true;
        public bool EstaDisponible => Disponible;
        public Exception? Fallo { get; set; }
        public string? Nombre_ { get; set; }

        public Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default)
        {
            if (Fallo is not null) throw Fallo;
            return Task.FromResult<FichaIndicativo?>(new FichaIndicativo
            {
                Indicativo = indicativo,
                Nombre = Nombre_,
                Fuente = nombre,
            });
        }
    }
}
