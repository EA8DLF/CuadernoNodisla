using System.Net;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using FluentAssertions;
using Nodisla.Cuaderno.Servicios.ClubLog;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>Club Log y el cuaderno en linea de QRZ.com.</summary>
public class ClubLogYQrzPruebas
{
    private static AlmacenDeCredencialesEnMemoria CredencialesDeClubLog()
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.ClubLogContrasena, "secreta");
        credenciales.Guardar(ClavesDeCredencial.ClubLogApi, "clave-de-api");
        return credenciales;
    }

    private static ServicioClubLog ClubLog(ManejadorFalso manejador) =>
        new(new FabricaFalsa(manejador),
            CredencialesDeClubLog(),
            new OpcionesClubLog { Correo = "ea8dlf@ejemplo.invalido", Indicativo = "EA8DLF" },
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));

    [Fact]
    public void Club_log_esta_configurado_solo_con_correo_indicativo_contrasena_y_clave_de_api()
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        var servicio = new ServicioClubLog(
            new FabricaFalsa(ManejadorFalso.ConTexto("OK")),
            credenciales,
            new OpcionesClubLog { Correo = "a@b.c", Indicativo = "EA8DLF" });

        servicio.EstaConfigurado.Should().BeFalse();

        credenciales.Guardar(ClavesDeCredencial.ClubLogContrasena, "x");
        servicio.EstaConfigurado.Should().BeFalse();

        credenciales.Guardar(ClavesDeCredencial.ClubLogApi, "y");
        servicio.EstaConfigurado.Should().BeTrue();
    }

    [Fact]
    public async Task La_subida_de_lote_va_por_putlogs_y_nunca_borra_el_cuaderno_remoto()
    {
        var manejador = ManejadorFalso.ConTexto("OK");
        var servicio = ClubLog(manejador);

        var resultado = await servicio.SubirAsync(
            [CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z")]);

        resultado.Enviados.Should().Be(1);
        manejador.Direcciones.Single().AbsoluteUri.Should().Be("https://clublog.org/putlogs.php");
        var cuerpo = manejador.Cuerpos.Single()!;
        // clear=1 vaciaria el cuaderno de Club Log antes de subir: nunca se manda.
        cuerpo.Should().Contain("name=clear");
        cuerpo.Should().MatchRegex(@"name=clear\s*\r?\n\r?\n0");
        cuerpo.Should().Contain("<CALL:5>EA1AB");
    }

    [Fact]
    public async Task Un_rechazo_de_club_log_se_cuenta_contacto_a_contacto_y_no_lanza()
    {
        var manejador = new ManejadorFalso(
            (_, _) => ManejadorFalso.Respuesta("Invalid API key", HttpStatusCode.Forbidden));
        var servicio = ClubLog(manejador);
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z");

        var resultado = await servicio.SubirAsync([qso]);

        resultado.Enviados.Should().Be(0);
        resultado.Rechazados.Should().Be(1);
        resultado.Motivos[qso.ClaveNatural].Should().Contain("403");
    }

    [Fact]
    public async Task El_contacto_suelto_va_por_la_via_de_tiempo_real()
    {
        var manejador = ManejadorFalso.ConTexto("OK");
        var servicio = ClubLog(manejador);

        var bien = await servicio.SubirEnDirectoAsync(
            CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z"));

        bien.Should().BeTrue();
        manejador.Direcciones.Single().AbsoluteUri.Should().Be("https://clublog.org/realtime.php");
    }

    [Fact]
    public async Task Club_log_no_tiene_descarga_de_confirmaciones_y_se_dice_asi()
    {
        var servicio = ClubLog(ManejadorFalso.ConTexto("OK"));

        servicio.PuedeSubir.Should().BeTrue();
        servicio.PuedeDescargar.Should().BeFalse();

        var confirmaciones = await servicio.DescargarAsync(null);

        confirmaciones.Should().BeEmpty();
    }

    [Fact]
    public async Task El_sincronizador_ni_consulta_a_un_servicio_que_no_sabe_descargar()
    {
        var manejador = ManejadorFalso.ConTexto("OK");
        var servicio = ClubLog(manejador);

        var resultado = await new Emparejamiento.SincronizadorDeConfirmaciones()
            .SincronizarAsync(servicio, [], desdeUtc: null);

        resultado.Descargadas.Should().Be(0);
        // Lo importante: no se ha llamado a Club Log para nada.
        manejador.Direcciones.Should().BeEmpty();
    }

    // ── QRZ.com, cuaderno en linea ───────────────────────────────────────────

    private static ServicioQrzCuaderno QrzCuaderno(ManejadorFalso manejador)
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.QrzClaveDeCuaderno, "CLAVE-DE-CUADERNO");
        return new ServicioQrzCuaderno(
            new FabricaFalsa(manejador),
            credenciales,
            new OpcionesQrz { Usuario = "EA8DLF", TamanoDePagina = 2 },
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));
    }

    [Fact]
    public void La_respuesta_de_qrz_llega_en_pares_clave_valor_codificados()
    {
        var campos = ServicioQrzCuaderno.Interpretar("RESULT=OK&COUNT=2&LOGIDS=1,2&DATA=a%20b");

        campos["RESULT"].Should().Be("OK");
        campos["COUNT"].Should().Be("2");
        campos["DATA"].Should().Be("a b");
    }

    [Fact]
    public async Task La_subida_a_qrz_es_un_contacto_por_llamada()
    {
        var manejador = ManejadorFalso.ConTexto("RESULT=OK&LOGID=7&COUNT=1");
        var servicio = QrzCuaderno(manejador);

        var resultado = await servicio.SubirAsync(
        [
            CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z"),
            CuadernoDePrueba.Contacto("EA2CD", "20m", "CW", "2026-05-01T12:20:00Z"),
        ]);

        resultado.Enviados.Should().Be(2);
        manejador.Direcciones.Should().HaveCount(2);
        manejador.Cuerpos[0].Should().Contain("ACTION=INSERT");
    }

    [Fact]
    public async Task Un_contacto_repetido_se_cuenta_como_rechazado_con_su_motivo()
    {
        var manejador = ManejadorFalso.ConTexto("RESULT=FAIL&REASON=duplicate%20QSO");
        var servicio = QrzCuaderno(manejador);
        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z");

        var resultado = await servicio.SubirAsync([qso]);

        resultado.Enviados.Should().Be(0);
        resultado.Motivos[qso.ClaveNatural].Should().Be("duplicate QSO");
    }

    [Fact]
    public async Task La_descarga_de_qrz_pagina_con_afterlogid_y_solo_devuelve_lo_confirmado()
    {
        const string primera =
            "RESULT=OK&COUNT=2&ADIF=" +
            "%3CCALL%3A5%3EEA1AB%20%3CBAND%3A3%3E20m%20%3CMODE%3A2%3ECW%20" +
            "%3CQSO_DATE%3A8%3E20260501%20%3CTIME_ON%3A6%3E121500%20" +
            "%3CAPP_QRZLOG_STATUS%3A1%3EC%20%3CAPP_QRZLOG_LOGID%3A2%3E11%20%3CEOR%3E" +
            "%3CCALL%3A5%3EEA2CD%20%3CBAND%3A3%3E40m%20%3CMODE%3A2%3ECW%20" +
            "%3CQSO_DATE%3A8%3E20260502%20%3CTIME_ON%3A6%3E083000%20" +
            "%3CAPP_QRZLOG_STATUS%3A1%3EN%20%3CAPP_QRZLOG_LOGID%3A2%3E12%20%3CEOR%3E";
        const string segunda = "RESULT=OK&COUNT=0&ADIF=";

        var manejador = ManejadorFalso.PorTurnos(primera, segunda);
        var servicio = QrzCuaderno(manejador);

        var confirmaciones = await servicio.DescargarAsync(null);

        confirmaciones.Should().ContainSingle();
        confirmaciones[0].Call.Valor.Should().Be("EA1AB");
        // QRZ no firma nada: confirma, pero no verifica.
        confirmaciones[0].Verificada.Should().BeFalse();
        manejador.Cuerpos[1].Should().Contain("AFTERLOGID");
    }

    [Fact]
    public void El_estado_de_confirmacion_de_qrz_se_lee_de_su_campo_propio()
    {
        var confirmado = new Dictionary<string, string> { ["APP_QRZLOG_STATUS"] = "C" };
        var sinConfirmar = new Dictionary<string, string> { ["APP_QRZLOG_STATUS"] = "N" };

        ServicioQrzCuaderno.EstaConfirmado(confirmado).Should().BeTrue();
        ServicioQrzCuaderno.EstaConfirmado(sinConfirmar).Should().BeFalse();
    }
}
