using FluentAssertions;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Consulta;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>
/// QRZ.com no se reintenta con unas credenciales que ya ha rechazado.
/// </summary>
/// <remarks>
/// En los registros de octubre de 2026 habia decenas de «Username/password incorrect» seguidos
/// (uno por indicativo tecleado) y detras los «blocked until …»: el programa insistia con la
/// misma contrasena mala hasta que QRZ bloqueaba la cuenta.
/// </remarks>
public class QrzCredencialesRechazadasPruebas
{
    private const string Rechazo = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Session><Error>Username/password incorrect</Error></Session>
        </QRZDatabase>
        """;

    // Tal cual sale en el registro real del 04-10-2026.
    private const string Bloqueo = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Session><Error>Username/password incorrect - blocked until 2026-10-04 19:20:08 UTC</Error></Session>
        </QRZDatabase>
        """;

    private const string Sesion = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Session><Key>CLAVE-1</Key></Session>
        </QRZDatabase>
        """;

    private const string Ficha = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Callsign><call>EA8DLF</call><fname>Jose</fname></Callsign>
          <Session><Key>CLAVE-1</Key></Session>
        </QRZDatabase>
        """;

    private static readonly Indicativo Corresponsal = Indicativo.Parse("EA8DLF");

    private sealed class Reloj
    {
        public DateTimeOffset Ahora { get; set; } = new(2026, 10, 4, 19, 0, 0, TimeSpan.Zero);
    }

    private static (ConsultaQrzCom Consulta, AlmacenDeCredencialesEnMemoria Credenciales,
        RegistroDePrueba<ConsultaQrzCom> Registro, Reloj Reloj) Montar(ManejadorFalso manejador)
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.QrzContrasena, "mala");
        var registro = new RegistroDePrueba<ConsultaQrzCom>();
        var reloj = new Reloj();
        var consulta = new ConsultaQrzCom(
            new FabricaFalsa(manejador),
            credenciales,
            new OpcionesQrz { Usuario = "EA8DLF" },
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask),
            registro,
            () => reloj.Ahora);
        return (consulta, credenciales, registro, reloj);
    }

    private static async Task ConsultarYFallarAsync(IConsultaIndicativo consulta)
    {
        var accion = async () => await consulta.ConsultarAsync(Corresponsal);
        await accion.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public async Task Tras_un_rechazo_no_se_vuelve_a_pedir_sesion_con_las_mismas_credenciales()
    {
        var manejador = ManejadorFalso.ConTexto(Rechazo);
        var (consulta, _, _, _) = Montar(manejador);

        await ConsultarYFallarAsync(consulta);
        await ConsultarYFallarAsync(consulta);
        await ConsultarYFallarAsync(consulta);

        // Un solo intento de sesion: los otros dos se cortan antes de salir a la red.
        manejador.Direcciones.Should().HaveCount(1);
    }

    [Fact]
    public async Task El_rechazo_se_avisa_una_sola_vez_en_el_registro()
    {
        var (consulta, _, registro, _) = Montar(ManejadorFalso.ConTexto(Rechazo));

        await ConsultarYFallarAsync(consulta);
        await ConsultarYFallarAsync(consulta);
        await ConsultarYFallarAsync(consulta);

        registro.Entradas.Where(e => e.Nivel >= LogLevel.Warning).Should().ContainSingle()
            .Which.Mensaje.Should().Contain("Username/password incorrect");
    }

    [Fact]
    public async Task El_rechazo_sale_como_sesion_rechazada_y_no_se_reintenta()
    {
        var manejador = ManejadorFalso.ConTexto(Rechazo);
        var (consulta, _, _, _) = Montar(manejador);

        var accion = async () => await consulta.ConsultarAsync(Corresponsal);

        await accion.Should().ThrowAsync<SesionRechazadaException>();
        manejador.Direcciones.Should().HaveCount(1);
    }

    [Fact]
    public async Task Al_cambiar_la_contrasena_se_vuelve_a_pedir_sesion()
    {
        var manejador = ManejadorFalso.PorTurnos(Rechazo, Sesion, Ficha);
        var (consulta, credenciales, _, _) = Montar(manejador);

        await ConsultarYFallarAsync(consulta);
        credenciales.Guardar(ClavesDeCredencial.QrzContrasena, "buena");
        var ficha = await consulta.ConsultarAsync(Corresponsal);

        ficha.Should().NotBeNull();
        manejador.Direcciones.Should().HaveCount(3);
        manejador.Direcciones[1].Query.Should().Contain("password=buena");
    }

    [Fact]
    public async Task Con_la_cuenta_bloqueada_no_se_pide_sesion_hasta_pasada_la_hora()
    {
        var manejador = ManejadorFalso.PorTurnos(Bloqueo, Sesion, Ficha);
        var (consulta, credenciales, _, reloj) = Montar(manejador);

        await ConsultarYFallarAsync(consulta);

        // Dentro del bloqueo no se pide ni cambiando la contrasena: QRZ contestaria lo mismo.
        reloj.Ahora = new DateTimeOffset(2026, 10, 4, 19, 20, 7, TimeSpan.Zero);
        credenciales.Guardar(ClavesDeCredencial.QrzContrasena, "buena");
        await ConsultarYFallarAsync(consulta);
        manejador.Direcciones.Should().HaveCount(1);

        reloj.Ahora = new DateTimeOffset(2026, 10, 4, 19, 20, 9, TimeSpan.Zero);
        var ficha = await consulta.ConsultarAsync(Corresponsal);

        ficha.Should().NotBeNull();
        manejador.Direcciones.Should().HaveCount(3);
    }

    [Fact]
    public async Task Pasado_el_bloqueo_se_prueba_una_vez_y_si_la_rechazan_no_se_insiste()
    {
        var manejador = ManejadorFalso.PorTurnos(Bloqueo, Rechazo);
        var (consulta, _, _, reloj) = Montar(manejador);

        await ConsultarYFallarAsync(consulta);
        reloj.Ahora = reloj.Ahora.AddHours(2);
        await ConsultarYFallarAsync(consulta);
        await ConsultarYFallarAsync(consulta);
        await ConsultarYFallarAsync(consulta);

        manejador.Direcciones.Should().HaveCount(2);
    }

    [Fact]
    public async Task El_bloqueo_dice_hasta_cuando_dura()
    {
        var (consulta, _, registro, _) = Montar(ManejadorFalso.ConTexto(Bloqueo));

        var accion = async () => await consulta.ConsultarAsync(Corresponsal);

        var fallo = (await accion.Should().ThrowAsync<SesionRechazadaException>()).Which;
        fallo.BloqueadoHasta.Should().Be(new DateTimeOffset(2026, 10, 4, 19, 20, 8, TimeSpan.Zero));
        fallo.Message.Should().Contain("2026-10-04 19:20");
        registro.Entradas.Where(e => e.Nivel >= LogLevel.Warning).Should().ContainSingle();
    }

    [Theory]
    [InlineData("Username/password incorrect", true)]
    [InlineData("Username/password incorrect - blocked until 2026-10-04 19:20:08 UTC", true)]
    [InlineData("Session Timeout", false)]
    [InlineData("Not found: ZZ9ZZ", false)]
    [InlineData(null, false)]
    public void Distingue_el_rechazo_de_credenciales_de_los_demas_errores(string? error, bool esperado) =>
        ConsultaQrzCom.EsRechazoDeCredenciales(error).Should().Be(esperado);

    [Fact]
    public async Task La_cadena_con_cache_no_repite_el_aviso_con_cada_indicativo()
    {
        var manejador = ManejadorFalso.ConTexto(Rechazo);
        var (qrz, _, _, _) = Montar(manejador);
        var registro = new RegistroDePrueba<ConsultaConCache>();
        var cadena = new ConsultaConCache(() => [qrz], carpeta: null, log: registro);

        foreach (var indicativo in new[] { "EA8DLF", "WA6KHK", "AE6Y", "K6VO" })
        {
            var accion = async () => await cadena.ConsultarAsync(Indicativo.Parse(indicativo));
            await accion.Should().ThrowAsync<SesionRechazadaException>();
        }

        manejador.Direcciones.Should().HaveCount(1);
        registro.Entradas.Where(e => e.Nivel >= LogLevel.Information).Should().BeEmpty();
    }

    // ── El boton «Probar el acceso» ──────────────────────────────────────────

    private const string SesionConAviso = """
        <?xml version="1.0" encoding="utf-8" ?>
        <QRZDatabase version="1.34" xmlns="http://xmldata.qrz.com">
          <Session><Key>CLAVE-1</Key><Message>A subscription is required to access the complete record.</Message></Session>
        </QRZDatabase>
        """;

    [Fact]
    public async Task Probar_el_acceso_con_buenas_credenciales_dice_correcto_y_trae_el_aviso_de_qrz()
    {
        var (consulta, _, _, _) = Montar(ManejadorFalso.ConTexto(SesionConAviso));

        var prueba = await consulta.ProbarAsync();

        prueba.Veredicto.Should().Be(VeredictoDeQrz.Correcto);
        prueba.Aviso.Should().Contain("subscription");
    }

    [Fact]
    public async Task Probar_el_acceso_con_la_contrasena_mala_lo_dice_y_no_insiste_con_la_misma()
    {
        var manejador = ManejadorFalso.ConTexto(Rechazo);
        var (consulta, _, _, _) = Montar(manejador);

        (await consulta.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.ClaveRechazada);
        (await consulta.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.ClaveRechazada);

        // El freno vale tambien para el boton: la segunda pulsacion no sale a la red.
        manejador.Direcciones.Should().HaveCount(1);
    }

    [Fact]
    public async Task Probar_el_acceso_tras_cambiar_la_contrasena_si_vuelve_a_intentarlo()
    {
        var manejador = ManejadorFalso.PorTurnos(Rechazo, Sesion);
        var (consulta, credenciales, _, _) = Montar(manejador);
        (await consulta.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.ClaveRechazada);

        credenciales.Guardar(ClavesDeCredencial.QrzContrasena, "buena");

        (await consulta.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.Correcto);
        manejador.Direcciones.Should().HaveCount(2);
    }

    [Fact]
    public async Task Probar_el_acceso_con_la_cuenta_bloqueada_dice_hasta_cuando()
    {
        var (consulta, _, _, _) = Montar(ManejadorFalso.ConTexto(Bloqueo));

        var prueba = await consulta.ProbarAsync();

        prueba.Veredicto.Should().Be(VeredictoDeQrz.Bloqueada);
        prueba.BloqueadaHasta.Should().Be(new DateTimeOffset(2026, 10, 4, 19, 20, 8, TimeSpan.Zero));
    }

    [Fact]
    public async Task Probar_el_acceso_sin_red_no_apunta_ningun_rechazo()
    {
        var falla = true;
        var manejador = new ManejadorFalso((_, _) =>
            falla ? throw new HttpRequestException("sin red") : ManejadorFalso.Respuesta(Sesion));
        var (consulta, _, _, _) = Montar(manejador);

        (await consulta.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.SinRed);

        falla = false;
        (await consulta.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.Correcto);
    }

    [Fact]
    public async Task Probar_el_acceso_sin_contrasena_guardada_no_llama_a_nadie()
    {
        var manejador = ManejadorFalso.ConTexto(Sesion);
        var (consulta, credenciales, _, _) = Montar(manejador);
        credenciales.Borrar(ClavesDeCredencial.QrzContrasena);

        (await consulta.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.SinDato);
        manejador.Direcciones.Should().BeEmpty();
    }
}
