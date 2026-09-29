using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Lotw;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>LoTW: descarga del informe y firma delegada en TQSL.</summary>
public class LotwPruebas
{
    /// <summary>Trozo real de un informe de LoTW, con el detalle de confirmacion.</summary>
    private const string InformeDeLotw = """
        ARRL Logbook of the World Status Report
        <PROGRAMID:4>LoTW
        <EOH>

        <CALL:5>EA1AB
        <BAND:3>20m
        <MODE:2>CW
        <QSO_DATE:8>20260501
        <TIME_ON:6>121500
        <QSL_RCVD:1>Y
        <QSLRDATE:8>20260510
        <APP_LOTW_RXQSL:19>2026-05-10 09:12:03
        <DXCC:3>281
        <CQZ:2>14
        <GRIDSQUARE:4>IN80
        <EOR>

        <CALL:5>EA2CD
        <BAND:3>40m
        <MODE:4>MFSK
        <SUBMODE:3>FT4
        <QSO_DATE:8>20260502
        <TIME_ON:6>083000
        <QSL_RCVD:1>N
        <EOR>
        <APP_LoTW_EOF>
        """;

    private sealed class FirmanteFalso : IFirmanteTqsl
    {
        private readonly ResultadoDeTqsl _resultado;

        public FirmanteFalso(ResultadoDeTqsl resultado) => _resultado = resultado;

        public bool EstaDisponible => true;

        public string? UltimoFichero { get; private set; }

        public string? ContenidoEnviado { get; private set; }

        public Task<ResultadoDeTqsl> FirmarYSubirAsync(string rutaDelAdif, CancellationToken ct = default)
        {
            UltimoFichero = rutaDelAdif;
            ContenidoEnviado = File.ReadAllText(rutaDelAdif);
            return Task.FromResult(_resultado);
        }
    }

    private static (ServicioLotw Servicio, ManejadorFalso Manejador) Montar(string respuesta)
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.LotwContrasena, "secreta");
        var manejador = ManejadorFalso.ConTexto(respuesta);
        var servicio = new ServicioLotw(
            new FabricaFalsa(manejador),
            credenciales,
            new OpcionesLotw { Usuario = "EA8DLF", UbicacionDeEstacion = "EA8DLF" },
            new FirmanteFalso(new ResultadoDeTqsl(0, "Success", string.Empty)),
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));
        return (servicio, manejador);
    }

    [Fact]
    public void Del_informe_solo_salen_las_confirmaciones()
    {
        var confirmaciones = ServicioLotw.Interpretar(InformeDeLotw);

        confirmaciones.Should().HaveCount(1);
        var una = confirmaciones[0];
        una.Call.Valor.Should().Be("EA1AB");
        una.Band.Nombre.Should().Be("20m");
        una.Mode.Should().Be("CW");
        una.Medio.Should().Be(MedioDeConfirmacion.Lotw);
    }

    [Fact]
    public void Una_confirmacion_de_lotw_siempre_esta_verificada()
    {
        // Las dos partes han firmado el contacto con su certificado: no hay termino medio.
        ServicioLotw.Interpretar(InformeDeLotw)[0].Verificada.Should().BeTrue();
    }

    [Fact]
    public void Se_queda_con_la_fecha_y_la_hora_de_la_confirmacion()
    {
        var una = ServicioLotw.Interpretar(InformeDeLotw)[0];

        una.ConfirmadaUtc.Should().Be(
            DateTimeOffset.Parse("2026-05-10T09:12:03Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void El_detalle_del_corresponsal_se_conserva_para_ensenarselo_al_operador()
    {
        var una = ServicioLotw.Interpretar(InformeDeLotw)[0];

        una.CamposExtra.Should().ContainKey("DXCC").WhoseValue.Should().Be("281");
        una.CamposExtra.Should().ContainKey("GRIDSQUARE").WhoseValue.Should().Be("IN80");
    }

    [Fact]
    public async Task La_descarga_manda_los_parametros_que_documenta_la_arrl()
    {
        var (servicio, manejador) = Montar(InformeDeLotw);

        await servicio.DescargarAsync(
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        var consulta = manejador.Direcciones.Single().Query;
        consulta.Should().Contain("qso_query=1");
        consulta.Should().Contain("qso_qsl=yes");
        consulta.Should().Contain("qso_qsldetail=yes");
        // El corte se hace por cuando LoTW confirmo, no por cuando recibio el contacto.
        consulta.Should().Contain("qso_qslsince=");
        consulta.Should().Contain("login=EA8DLF");
    }

    [Fact]
    public async Task Una_pagina_de_error_no_pasa_por_un_informe()
    {
        // Con la contrasena mal, LoTW devuelve una pagina web con codigo 200.
        var (servicio, _) = Montar("<html><body>Username/password incorrect</body></html>");

        var accion = async () => await servicio.DescargarAsync(null);

        await accion.Should().ThrowAsync<RespuestaDelServicioException>();
    }

    [Fact]
    public async Task La_subida_le_pasa_a_tqsl_un_adif_con_los_campos_de_lotw_y_sin_los_demas()
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.LotwContrasena, "secreta");
        var firmante = new FirmanteFalso(new ResultadoDeTqsl(0, "Success", string.Empty));
        var servicio = new ServicioLotw(
            new FabricaFalsa(ManejadorFalso.ConTexto(string.Empty)),
            credenciales,
            new OpcionesLotw { Usuario = "EA8DLF", UbicacionDeEstacion = "EA8DLF" },
            firmante);

        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z");
        qso.Comentario = "esto no va a LoTW";
        qso.Name = "Pepe";

        var resultado = await servicio.SubirAsync([qso]);

        resultado.Enviados.Should().Be(1);
        resultado.Rechazados.Should().Be(0);
        firmante.ContenidoEnviado.Should().Contain("<CALL:5>EA1AB");
        firmante.ContenidoEnviado.Should().NotContain("COMMENT");
        firmante.ContenidoEnviado.Should().NotContain("NAME");
        // El fichero temporal se borra en cuanto TQSL termina.
        File.Exists(firmante.UltimoFichero!).Should().BeFalse();
    }

    [Fact]
    public async Task Si_tqsl_rechaza_la_subida_se_dice_por_que_contacto_a_contacto()
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.LotwContrasena, "secreta");
        var servicio = new ServicioLotw(
            new FabricaFalsa(ManejadorFalso.ConTexto(string.Empty)),
            credenciales,
            new OpcionesLotw { Usuario = "EA8DLF", UbicacionDeEstacion = "EA8DLF" },
            new FirmanteFalso(new ResultadoDeTqsl(2, "rejected by LoTW", string.Empty)));

        var qso = CuadernoDePrueba.Contacto("EA1AB", "20m", "CW", "2026-05-01T12:15:00Z");
        var resultado = await servicio.SubirAsync([qso]);

        resultado.Enviados.Should().Be(0);
        resultado.Rechazados.Should().Be(1);
        resultado.Motivos.Should().ContainKey(qso.ClaveNatural);
    }

    [Theory]
    [InlineData("10:00:00 AM: Final Status: Success (0)", 0, true)]
    [InlineData("10:00:00 AM: Final Status: Some QSOs were already uploaded (9)", 9, true)]
    [InlineData("10:00:00 AM: Final Status: rejected by LoTW (2)", 2, false)]
    public void El_estado_final_de_tqsl_se_lee_de_la_salida_de_errores(
        string salida, int codigo, bool exito)
    {
        var (leido, _) = FirmanteTqsl.InterpretarEstado(salida, codigoDeSalida: 99);

        leido.Should().Be(codigo);
        new ResultadoDeTqsl(leido, string.Empty, salida).EsExito.Should().Be(exito);
    }

    [Fact]
    public void Sin_linea_de_estado_se_recurre_al_codigo_de_salida_del_proceso()
    {
        var (codigo, descripcion) = FirmanteTqsl.InterpretarEstado(string.Empty, codigoDeSalida: 11);

        codigo.Should().Be(11);
        descripcion.Should().Contain("LoTW");
    }

    [Fact]
    public void La_orden_de_tqsl_es_la_documentada_y_no_abre_ventanas()
    {
        var firmante = new FirmanteTqsl(
            new OpcionesLotw { UbicacionDeEstacion = "Casa de EA8DLF" },
            new LocalizadorFalso(null));

        var argumentos = firmante.ArgumentosDe(@"C:\temp\log.adi");

        argumentos.Should().ContainInOrder("-d", "-a", "compliant", "-u", "-x", "-l", "Casa de EA8DLF");
        argumentos.Last().Should().Be(@"C:\temp\log.adi");
    }

    [Fact]
    public async Task Sin_tqsl_instalado_se_dice_claramente_en_vez_de_fallar_a_secas()
    {
        var firmante = new FirmanteTqsl(
            new OpcionesLotw { UbicacionDeEstacion = "EA8DLF" },
            new LocalizadorFalso(null));

        firmante.EstaDisponible.Should().BeFalse();

        var accion = async () => await firmante.FirmarYSubirAsync("cualquiera.adi");

        (await accion.Should().ThrowAsync<TqslNoInstaladoException>())
            .WithMessage("*Trusted QSL*");
    }

    [Fact]
    public void Sin_tqsl_se_puede_descargar_pero_no_subir_y_se_dice_por_que()
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.LotwContrasena, "secreta");
        var servicio = new ServicioLotw(
            new FabricaFalsa(ManejadorFalso.ConTexto(string.Empty)),
            credenciales,
            new OpcionesLotw { Usuario = "EA8DLF", UbicacionDeEstacion = "EA8DLF" },
            new FirmanteAusente());

        servicio.EstaConfigurado.Should().BeTrue();
        servicio.PuedeDescargar.Should().BeTrue();
        servicio.PuedeSubir.Should().BeFalse();
        servicio.MotivoDeNoPoderSubir.Should().Contain("no está TQSL instalado");
    }

    [Fact]
    public void Con_tqsl_pero_sin_ubicacion_de_estacion_tampoco_se_puede_subir()
    {
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.LotwContrasena, "secreta");
        var servicio = new ServicioLotw(
            new FabricaFalsa(ManejadorFalso.ConTexto(string.Empty)),
            credenciales,
            new OpcionesLotw { Usuario = "EA8DLF" },
            new FirmanteFalso(new ResultadoDeTqsl(0, "Success", string.Empty)));

        servicio.PuedeSubir.Should().BeFalse();
        servicio.MotivoDeNoPoderSubir.Should().Contain("ubicación de estación");
    }

    [Fact]
    public void Se_avisa_de_que_la_frase_de_paso_es_visible_en_la_lista_de_procesos()
    {
        // El aviso es para la interfaz: El operador tiene que poder decidir con el dato delante.
        ServicioLotw.AvisoDeLaFraseDePaso.Should().Contain("lista de procesos");
    }

    [Fact]
    public async Task La_descarga_avisa_del_avance_porque_puede_tardar_minutos()
    {
        var (servicio, _) = Montar(InformeDeLotw);
        var progreso = new ProgresoDeMentira();

        await servicio.DescargarAsync(null, progreso);

        progreso.Avisos.Should().HaveCountGreaterThan(1);
        progreso.Avisos[0].Mensaje.Should().Contain("minutos");
        progreso.Avisos[^1].Hecho.Should().Be(1);
    }

    [Fact]
    public void La_ruta_de_tqsl_de_los_ajustes_manda_sobre_la_detectada()
    {
        var firmante = new FirmanteTqsl(
            new OpcionesLotw
            {
                UbicacionDeEstacion = "EA8DLF",
                RutaDeTqsl = @"E:\portatil\tqsl.exe",
            },
            new LocalizadorFalso(@"C:\Program Files (x86)\TrustedQSL\tqsl.exe"));

        firmante.EstaDisponible.Should().BeTrue();
    }

    [Fact]
    public void El_localizador_prefiere_la_ruta_del_registro_a_las_rutas_de_costumbre()
    {
        var localizador = new LocalizadorDeTqsl(
            leerRegistro: (clave, valor) =>
                clave.Contains("TrustedQSL") && valor == "InstallPath" ? @"D:\TrustedQSL\" : null,
            existe: ruta => ruta == @"D:\TrustedQSL\tqsl.exe");

        localizador.Localizar().Should().Be(@"D:\TrustedQSL\tqsl.exe");
    }

    [Fact]
    public void El_lector_del_registro_encuentra_tqsl_si_esta_instalado_en_esta_maquina()
    {
        // Comprueba de verdad la lectura del registro, no un doble. Si en esta maquina no hay
        // TQSL, la prueba se salta sola: la instalacion no es un requisito para compilar.
        var carpeta = LocalizadorDeTqsl.LeerDelRegistro(
            LocalizadorDeTqsl.ClaveDelRegistro, "InstallPath");
        if (carpeta is null) return;

        var ruta = new LocalizadorDeTqsl().Localizar();

        ruta.Should().NotBeNull();
        Path.GetFileName(ruta!).Should().Be("tqsl.exe");
        File.Exists(ruta).Should().BeTrue();
    }

    [Fact]
    public void El_localizador_devuelve_nulo_cuando_tqsl_no_esta()
    {
        var localizador = new LocalizadorDeTqsl(
            leerRegistro: (_, _) => null,
            existe: _ => false);

        localizador.Localizar().Should().BeNull();
    }

    /// <summary>Un TQSL que no esta instalado.</summary>
    private sealed class FirmanteAusente : IFirmanteTqsl
    {
        public bool EstaDisponible => false;

        public Task<ResultadoDeTqsl> FirmarYSubirAsync(string rutaDelAdif, CancellationToken ct = default) =>
            throw new TqslNoInstaladoException("No hay TQSL.");
    }

    private sealed class LocalizadorFalso : ILocalizadorDeTqsl
    {
        private readonly string? _ruta;

        public LocalizadorFalso(string? ruta) => _ruta = ruta;

        public string? Localizar() => _ruta;
    }
}
