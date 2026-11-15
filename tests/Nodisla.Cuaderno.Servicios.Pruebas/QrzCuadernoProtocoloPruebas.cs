using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>
/// El cuaderno de QRZ.com contra lo que dice su guia oficial
/// (https://www.qrz.com/docs/logbook/QRZLogbookAPI.html). Nada sale a la red.
/// </summary>
public class QrzCuadernoProtocoloPruebas
{
    private const string Clave = "ABCD-0A0B-1C1D-2E2F";

    private static (ServicioQrzCuaderno Servicio, AlmacenDeCredencialesEnMemoria Credenciales) Montar(
        ManejadorFalso manejador, int pagina = 2)
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.QrzClaveDeCuaderno, Clave);
        var servicio = new ServicioQrzCuaderno(
            new FabricaFalsa(manejador),
            credenciales,
            new OpcionesQrz { Usuario = "EA8DLF", TamanoDePagina = pagina },
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));
        return (servicio, credenciales);
    }

    private static Qso Contacto(string indicativo = "EA1AB", string hora = "2026-05-01T12:15:00Z") =>
        CuadernoDePrueba.Contacto(indicativo, "20m", "CW", hora);

    private static Qso YaSubido(string indicativo = "EA1AB")
    {
        var qso = Contacto(indicativo);
        qso.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.QrzCom,
            Enviado = EstadoDeConfirmacion.Pendiente,
        });
        return qso;
    }

    // ── La respuesta ─────────────────────────────────────────────────────────

    [Fact]
    public void Un_adif_con_entidades_html_no_se_parte_por_sus_ampersands()
    {
        // Asi lo trata el cliente de terceros qspylib: parte solo donde empieza otro NOMBRE=.
        var campos = ServicioQrzCuaderno.Interpretar(
            "RESULT=OK&COUNT=1&ADIF=&lt;call:5&gt;EA1AB&lt;band:3&gt;20m&lt;comment:5&gt;A &amp; B&lt;eor&gt;");

        campos["RESULT"].Should().Be("OK");
        campos["COUNT"].Should().Be("1");
        campos["ADIF"].Should().Be("<call:5>EA1AB<band:3>20m<comment:5>A & B<eor>");
    }

    [Fact]
    public void Un_adif_sin_codificar_conserva_sus_signos_mas_y_sus_porcentajes()
    {
        var campos = ServicioQrzCuaderno.Interpretar("RESULT=OK&ADIF=<call:5>EA1AB<comment:8>5+9 100%<eor>");

        campos["ADIF"].Should().Be("<call:5>EA1AB<comment:8>5+9 100%<eor>");
    }

    // ── INSERT ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Un_contacto_nuevo_se_inserta_sin_replace_para_no_pisar_confirmaciones()
    {
        // La guia avisa: REPLACE «WILL overwrite confirmed QSOs with the supplied unconfirmed QSO».
        var manejador = ManejadorFalso.ConTexto("RESULT=OK&LOGID=7&COUNT=1");
        var (servicio, _) = Montar(manejador);

        var resultado = await servicio.SubirAsync([Contacto()]);

        resultado.Enviados.Should().Be(1);
        manejador.Cuerpos[0].Should().Contain("ACTION=INSERT").And.NotContain("REPLACE");
    }

    [Fact]
    public async Task Un_contacto_corregido_despues_de_subirlo_va_con_replace()
    {
        var manejador = ManejadorFalso.ConTexto("RESULT=REPLACE&LOGID=7&COUNT=1");
        var (servicio, _) = Montar(manejador);

        var resultado = await servicio.SubirAsync([YaSubido()]);

        resultado.Enviados.Should().Be(1);
        manejador.Cuerpos[0].Should().Contain("OPTION=REPLACE");
    }

    [Fact]
    public async Task Un_duplicado_cuenta_como_subido_porque_ya_esta_en_el_libro()
    {
        var manejador = ManejadorFalso.ConTexto("RESULT=FAIL&REASON=Unable to add QSO to database: duplicate&COUNT=0");
        var (servicio, _) = Montar(manejador);
        var qso = Contacto();

        var resultado = await servicio.SubirAsync([qso]);

        resultado.Enviados.Should().Be(1);
        resultado.Motivos.Should().BeEmpty();
        resultado.Parada.Should().BeNull();
    }

    [Fact]
    public async Task Un_rechazo_normal_lleva_el_motivo_de_qrz_y_no_para_la_subida()
    {
        var manejador = ManejadorFalso.ConTexto("RESULT=FAIL&REASON=wrong station_callsign for this logbook&COUNT=0");
        var (servicio, _) = Montar(manejador);
        var uno = Contacto();
        var otro = Contacto("EA2CD", "2026-05-01T12:20:00Z");

        var resultado = await servicio.SubirAsync([uno, otro]);

        resultado.Enviados.Should().Be(0);
        resultado.Parada.Should().BeNull();
        resultado.Motivos[uno.ClaveNatural].Should().Contain("wrong station_callsign");
        manejador.Cuerpos.Should().HaveCount(2, "un rechazo de un contacto no impide probar el siguiente");
    }

    [Theory]
    [InlineData("RESULT=AUTH")]
    [InlineData("RESULT=AUTH&REASON=user does not have a valid QRZ subscription")]
    [InlineData("RESULT=FAIL&REASON=invalid api key ABCD")]
    public async Task Un_fallo_de_clave_o_de_permiso_para_la_subida_al_primer_contacto(string respuesta)
    {
        var manejador = ManejadorFalso.ConTexto(respuesta);
        var (servicio, _) = Montar(manejador);
        var uno = Contacto();
        var otro = Contacto("EA2CD", "2026-05-01T12:20:00Z");

        var resultado = await servicio.SubirAsync([uno, otro]);

        resultado.Enviados.Should().Be(0);
        resultado.Parada.Should().NotBeNullOrWhiteSpace();
        resultado.Motivos.Should().ContainKeys(uno.ClaveNatural, otro.ClaveNatural);
        manejador.Cuerpos.Should().HaveCount(1, "con la clave rechazada no se sigue llamando");
        resultado.Parada.Should().NotContain(Clave, "la clave no se ensena nunca");
    }

    [Fact]
    public async Task Con_la_clave_ya_rechazada_no_se_vuelve_a_llamar_hasta_que_cambie()
    {
        var turno = 0;
        var manejador = new ManejadorFalso((_, _) => ManejadorFalso.Respuesta(
            turno++ == 0 ? "RESULT=FAIL&REASON=invalid api key" : "RESULT=OK&LOGID=1&COUNT=1"));
        var (servicio, credenciales) = Montar(manejador);

        (await servicio.SubirAsync([Contacto()])).Parada.Should().NotBeNull();
        var repetido = await servicio.SubirAsync([Contacto()]);

        repetido.Parada.Should().NotBeNull();
        manejador.Cuerpos.Should().HaveCount(1, "la misma clave rechazada no se reintenta");

        credenciales.Guardar(ClavesDeCredencial.QrzClaveDeCuaderno, "1111-2222-3333-4444");
        var conOtra = await servicio.SubirAsync([Contacto()]);

        conOtra.Enviados.Should().Be(1);
        manejador.Cuerpos.Should().HaveCount(2);
    }

    // ── STATUS: el boton «Probar» ────────────────────────────────────────────

    [Fact]
    public async Task Probar_una_clave_buena_dice_de_que_libro_es_y_cuantos_contactos_tiene()
    {
        var manejador = ManejadorFalso.ConTexto(
            "RESULT=OK&DATA=CALLSIGN%3DEA8DLF%26BOOK_NAME%3DMi%20libro%26COUNT%3D1812%26CONFIRMED%3D640%26OWNER%3DEA8DLF");
        var (servicio, _) = Montar(manejador);

        var prueba = await servicio.ProbarAsync();

        prueba.Veredicto.Should().Be(VeredictoDeQrz.Correcto);
        prueba.Indicativo.Should().Be("EA8DLF");
        prueba.Libro.Should().Be("Mi libro");
        prueba.Contactos.Should().Be(1812);
        prueba.Confirmados.Should().Be(640);
        manejador.Cuerpos[0].Should().Contain("ACTION=STATUS");
    }

    [Fact]
    public async Task Probar_entiende_el_estado_tambien_si_llega_sin_envolver_en_data()
    {
        var (servicio, _) = Montar(ManejadorFalso.ConTexto("RESULT=OK&CALLSIGN=EA8DLF&COUNT=12&CONFIRMED=3"));

        var prueba = await servicio.ProbarAsync();

        prueba.Veredicto.Should().Be(VeredictoDeQrz.Correcto);
        prueba.Indicativo.Should().Be("EA8DLF");
        prueba.Contactos.Should().Be(12);
    }

    [Fact]
    public async Task Probar_una_clave_buena_sin_datos_del_libro_sigue_siendo_correcto()
    {
        var (servicio, _) = Montar(ManejadorFalso.ConTexto("RESULT=OK"));

        var prueba = await servicio.ProbarAsync();

        prueba.Veredicto.Should().Be(VeredictoDeQrz.Correcto);
        prueba.Indicativo.Should().BeNull();
        prueba.Contactos.Should().BeNull();
    }

    [Fact]
    public async Task Probar_una_clave_que_qrz_no_reconoce_dice_clave_rechazada()
    {
        // Guia oficial, STATUS: «FAIL if the Access Key is invalid».
        var (servicio, _) = Montar(ManejadorFalso.ConTexto("RESULT=FAIL&REASON=invalid api key"));

        var prueba = await servicio.ProbarAsync();

        prueba.Veredicto.Should().Be(VeredictoDeQrz.ClaveRechazada);
        prueba.Mensaje.Should().NotContain(Clave);
    }

    [Fact]
    public async Task Probar_sin_permiso_dice_que_falta_la_suscripcion()
    {
        // Guia oficial: «AUTH when API Access lacked sufficient privileges for the operation».
        var (servicio, _) = Montar(ManejadorFalso.ConTexto("RESULT=AUTH"));

        (await servicio.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.SinSuscripcion);
    }

    [Fact]
    public async Task Probar_sin_red_dice_sin_red_y_no_marca_la_clave_como_mala()
    {
        var falla = true;
        var manejador = new ManejadorFalso((_, _) =>
            falla ? throw new HttpRequestException("sin red") : ManejadorFalso.Respuesta("RESULT=OK&LOGID=1&COUNT=1"));
        var (servicio, _) = Montar(manejador);

        (await servicio.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.SinRed);

        falla = false;
        (await servicio.SubirAsync([Contacto()])).Enviados.Should().Be(1);
    }

    [Fact]
    public async Task Probar_sin_clave_guardada_no_llama_a_nadie()
    {
        var manejador = ManejadorFalso.ConTexto("RESULT=OK");
        var (servicio, credenciales) = Montar(manejador);
        credenciales.Borrar(ClavesDeCredencial.QrzClaveDeCuaderno);

        (await servicio.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.SinDato);
        manejador.Cuerpos.Should().BeEmpty();
    }

    [Fact]
    public async Task Probar_a_mano_si_vuelve_a_llamar_y_si_va_bien_quita_el_freno()
    {
        // El operador contrata la suscripcion y pulsa «Probar» con la misma clave.
        var turno = 0;
        var manejador = new ManejadorFalso((_, _) => ManejadorFalso.Respuesta(
            turno++ == 0 ? "RESULT=AUTH" : "RESULT=OK&LOGID=1&COUNT=1"));
        var (servicio, _) = Montar(manejador);
        (await servicio.SubirAsync([Contacto()])).Parada.Should().NotBeNull();

        (await servicio.ProbarAsync()).Veredicto.Should().Be(VeredictoDeQrz.Correcto);
        (await servicio.SubirAsync([Contacto()])).Enviados.Should().Be(1);
    }

    [Fact]
    public async Task La_clave_pegada_con_espacios_o_saltos_se_manda_limpia()
    {
        var manejador = ManejadorFalso.ConTexto("RESULT=OK");
        var (servicio, credenciales) = Montar(manejador);
        credenciales.Guardar(ClavesDeCredencial.QrzClaveDeCuaderno, "  " + Clave + "\r\n");

        await servicio.ProbarAsync();

        manejador.Cuerpos[0].Should().Contain("KEY=" + Clave).And.NotContain("%0D").And.NotContain("KEY=+");
    }

    // ── FETCH ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task La_pagina_siguiente_empieza_en_el_mayor_logid_mas_uno()
    {
        // Guia oficial, PAGING: AFTERLOGID es «greater than or equal»; se pide «the highest value plus one».
        const string primera =
            "RESULT=OK&COUNT=2&ADIF=" +
            "<call:5>EA1AB<band:3>20m<mode:2>CW<qso_date:8>20260501<time_on:6>121500" +
            "<app_qrzlog_status:1>C<app_qrzlog_logid:2>11<eor>" +
            "<call:5>EA2CD<band:3>40m<mode:2>CW<qso_date:8>20260502<time_on:6>083000" +
            "<app_qrzlog_status:1>C<app_qrzlog_logid:2>12<eor>";
        var manejador = ManejadorFalso.PorTurnos(primera, "RESULT=OK&COUNT=0&ADIF=");
        var (servicio, _) = Montar(manejador);

        var confirmaciones = await servicio.DescargarAsync(null);

        confirmaciones.Should().HaveCount(2);
        manejador.Cuerpos[0].Should().Contain(Uri.EscapeDataString("AFTERLOGID:0"));
        manejador.Cuerpos[1].Should().Contain(Uri.EscapeDataString("AFTERLOGID:13"));
    }

    // ── La forma de la clave ─────────────────────────────────────────────────

    [Theory]
    [InlineData("ABCD-0A0B-1C1D-2E2F", true)]
    [InlineData("abcd-0a0b-1c1d-2e2f", true)]
    [InlineData("ABCD0A0B1C1D2E2F", true)]
    [InlineData("  ABCD-0A0B-1C1D-2E2F \r\n", true)]
    [InlineData("MiContrasena2026", false)]
    [InlineData("ABCD-0A0B-1C1D", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void La_clave_de_api_se_reconoce_por_la_forma_del_ejemplo_de_la_guia(string? texto, bool esperado) =>
        FormatoDeClaveQrz.PareceClaveDeApi(texto).Should().Be(esperado);
}
