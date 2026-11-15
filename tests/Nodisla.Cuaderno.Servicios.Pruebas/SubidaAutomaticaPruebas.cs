using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Pruebas.Dobles;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Red;
using Nodisla.Cuaderno.Servicios.Subidas;

namespace Nodisla.Cuaderno.Servicios.Pruebas;

/// <summary>
/// La cola de subida automatica. Todo con dobles: ningun contacto sale a ningun servicio.
/// </summary>
public sealed class SubidaAutomaticaPruebas : IDisposable
{
    private readonly RepositorioQsoDoble _cuaderno = new();
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-cola-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _ahora = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    private string Ruta => Path.Combine(_carpeta, ColaDeSubidas.NombreDelFichero);

    private ColaDeSubidas Cola(
        IReadOnlyList<IServicioQsl> servicios,
        Func<MedioDeConfirmacion, bool?>? activado = null) =>
        new(_cuaderno, () => servicios, activado, Ruta, () => _ahora, (_, _) => Task.CompletedTask);

    private Qso Guardado(string indicativo = "DL1ABC") =>
        _cuaderno.Sembrar(CuadernoDePrueba.Contacto(indicativo, "20m", "FT8", "2026-09-27T11:58:00Z"));

    private static QsoConfirmacion? Estado(Qso qso, MedioDeConfirmacion medio) =>
        qso.Confirmaciones.FirstOrDefault(c => c.Medio == medio);

    [Fact]
    public async Task Un_contacto_nuevo_se_encola_se_marca_pendiente_y_al_subir_queda_enviado_con_fecha()
    {
        var lotw = new ServicioDoble(MedioDeConfirmacion.Lotw);
        var qrz = new ServicioDoble(MedioDeConfirmacion.QrzCom);
        var cola = Cola([lotw, qrz]);
        var qso = Guardado();

        await cola.EncolarAsync(qso, modificado: false);

        cola.Elementos.Should().HaveCount(2);
        Estado(qso, MedioDeConfirmacion.Lotw)!.Enviado.Should().Be(EstadoDeConfirmacion.Pendiente, "LOTW_QSL_SENT=Q");

        var subidos = await cola.ProcesarAsync();

        subidos.Should().Be(2);
        cola.Elementos.Should().BeEmpty();
        lotw.Subidos.Should().ContainSingle().Which.Call.Valor.Should().Be("DL1ABC");
        Estado(qso, MedioDeConfirmacion.Lotw)!.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);
        Estado(qso, MedioDeConfirmacion.Lotw)!.EnviadoUtc.Should().Be(_ahora);
        Estado(qso, MedioDeConfirmacion.QrzCom)!.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);
        cola.Estados().Single(e => e.Medio == MedioDeConfirmacion.Lotw).Semaforo.Should().Be(SemaforoDeSubida.Verde);
    }

    [Fact]
    public async Task Sin_red_se_reintenta_con_espera_creciente_y_sin_perder_el_contacto()
    {
        var eqsl = new ServicioDoble(MedioDeConfirmacion.Eqsl) { Fallo = new ServicioNoDisponibleException("sin red") };
        var cola = Cola([eqsl]);
        await cola.EncolarAsync(Guardado(), false);

        await cola.ProcesarAsync();
        var elemento = cola.Elementos.Single();
        elemento.Intentos.Should().Be(1);
        elemento.ProximoIntentoUtc.Should().Be(_ahora + TimeSpan.FromMinutes(1));
        cola.Estados().Single(e => e.Medio == MedioDeConfirmacion.Eqsl).Semaforo.Should().Be(SemaforoDeSubida.Ambar);

        // Antes de su turno no se vuelve a llamar.
        _ahora += TimeSpan.FromSeconds(30);
        await cola.ProcesarAsync();
        eqsl.Llamadas.Should().Be(1);

        _ahora += TimeSpan.FromMinutes(1);
        await cola.ProcesarAsync();
        eqsl.Llamadas.Should().Be(2);
        cola.Elementos.Single().ProximoIntentoUtc.Should().Be(_ahora + TimeSpan.FromMinutes(2));

        // Vuelve la red.
        eqsl.Fallo = null;
        _ahora += TimeSpan.FromMinutes(2);
        (await cola.ProcesarAsync()).Should().Be(1);
        cola.Elementos.Should().BeEmpty();
    }

    [Fact]
    public void La_espera_crece_y_se_para_en_una_hora()
    {
        ColaDeSubidas.EsperaDe(1).Should().Be(TimeSpan.FromMinutes(1));
        ColaDeSubidas.EsperaDe(3).Should().Be(TimeSpan.FromMinutes(4));
        ColaDeSubidas.EsperaDe(20).Should().Be(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public async Task La_cola_sobrevive_al_cierre_y_se_intenta_al_arrancar()
    {
        var clubLog = new ServicioDoble(MedioDeConfirmacion.ClubLog) { Fallo = new ServicioNoDisponibleException("sin red") };
        var primera = Cola([clubLog]);
        await primera.EncolarAsync(Guardado(), false);
        await primera.ProcesarAsync();

        File.Exists(Ruta).Should().BeTrue();

        var segunda = Cola([clubLog]);
        segunda.Elementos.Should().ContainSingle().Which.Intentos.Should().Be(1);
    }

    [Fact]
    public async Task Una_cola_danada_se_aparta_y_el_siguiente_guardado_no_la_machaca()
    {
        // Lo que queda si el programa muere a mitad de escribir, o si dos copias se pisan.
        const string loQueHabia = "[ { \"QsoId\": 12, \"Medio\": \"Lotw\", esto se quedo a medias";
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(Ruta, loQueHabia);

        var cola = Cola([new ServicioDoble(MedioDeConfirmacion.Lotw)]);
        cola.Elementos.Should().BeEmpty();
        await cola.EncolarAsync(Guardado(), false);

        var apartados = Directory.GetFiles(_carpeta, ColaDeSubidas.NombreDelFichero + ".dañado-*");
        apartados.Should().ContainSingle();
        File.ReadAllText(apartados[0]).Should().Be(loQueHabia);
        Cola([new ServicioDoble(MedioDeConfirmacion.Lotw)]).Elementos.Should().ContainSingle();
    }

    [Fact]
    public async Task Guardar_la_cola_no_deja_temporales_por_la_carpeta()
    {
        var cola = Cola([new ServicioDoble(MedioDeConfirmacion.Lotw)]);
        await cola.EncolarAsync(Guardado(), false);
        await cola.EncolarAsync(Guardado("EA8XYZ"), false);

        Directory.GetFiles(_carpeta).Should().ContainSingle().Which.Should().Be(Ruta);
    }

    [Fact]
    public async Task Lotw_sin_tqsl_no_se_reintenta_en_bucle_y_la_pastilla_lo_dice()
    {
        var lotw = new ServicioDoble(MedioDeConfirmacion.Lotw) { PuedeSubir = false };
        var cola = Cola([lotw]);
        await cola.EncolarAsync(Guardado(), false);

        await cola.ProcesarAsync();
        await cola.ProcesarAsync(forzar: true);

        lotw.Llamadas.Should().Be(0);
        cola.Elementos.Single().Intentos.Should().Be(0);
        var estado = cola.Estados().Single(e => e.Medio == MedioDeConfirmacion.Lotw);
        estado.Semaforo.Should().Be(SemaforoDeSubida.Rojo);
        estado.Pendientes.Should().Be(1);

        // Se instala TQSL: sale solo.
        lotw.PuedeSubir = true;
        (await cola.ProcesarAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Un_contacto_modificado_se_reenvia_a_club_log_y_qrz_pero_no_a_lotw_ni_eqsl()
    {
        var servicios = new[]
        {
            new ServicioDoble(MedioDeConfirmacion.Lotw),
            new ServicioDoble(MedioDeConfirmacion.Eqsl),
            new ServicioDoble(MedioDeConfirmacion.ClubLog),
            new ServicioDoble(MedioDeConfirmacion.QrzCom),
        };
        var cola = Cola(servicios);
        var qso = Guardado();
        await cola.EncolarAsync(qso, false);
        await cola.ProcesarAsync();

        qso.Name = "Corregido";
        await cola.EncolarAsync(qso, modificado: true);

        cola.Elementos.Select(e => e.Medio).Should().BeEquivalentTo(
            new[] { MedioDeConfirmacion.ClubLog, MedioDeConfirmacion.QrzCom });
        cola.Elementos.Should().OnlyContain(e => e.Modificado);
        Estado(qso, MedioDeConfirmacion.ClubLog)!.Enviado.Should().Be(EstadoDeConfirmacion.Pendiente, "CLUBLOG_QSO_UPLOAD_STATUS=M");
        Estado(qso, MedioDeConfirmacion.Lotw)!.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);

        await cola.ProcesarAsync();
        servicios[2].Subidos.Last().Name.Should().Be("Corregido");
        Estado(qso, MedioDeConfirmacion.ClubLog)!.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);
    }

    [Fact]
    public async Task Modificar_un_contacto_que_nunca_se_subio_no_lo_sube()
    {
        var cola = Cola([new ServicioDoble(MedioDeConfirmacion.ClubLog)]);
        await cola.EncolarAsync(Guardado(), modificado: true);
        cola.Elementos.Should().BeEmpty();
    }

    [Fact]
    public async Task Las_casillas_mandan_y_por_omision_solo_suben_los_que_tienen_credenciales()
    {
        var conCredenciales = new ServicioDoble(MedioDeConfirmacion.Eqsl);
        var sinCredenciales = new ServicioDoble(MedioDeConfirmacion.ClubLog) { Configurado = false };
        var desmarcado = new ServicioDoble(MedioDeConfirmacion.QrzCom);
        var cola = Cola(
            [conCredenciales, sinCredenciales, desmarcado],
            medio => medio == MedioDeConfirmacion.QrzCom ? false : null);

        await cola.EncolarAsync(Guardado(), false);

        cola.Elementos.Should().ContainSingle().Which.Medio.Should().Be(MedioDeConfirmacion.Eqsl);
        cola.Estados().Single(e => e.Medio == MedioDeConfirmacion.QrzCom).Semaforo.Should().Be(SemaforoDeSubida.Apagado);
    }

    [Fact]
    public async Task Un_rechazo_del_servicio_agota_los_reintentos_y_subir_ahora_lo_vuelve_a_intentar()
    {
        var qrz = new ServicioDoble(MedioDeConfirmacion.QrzCom) { Rechazar = "wrong station_callsign" };
        var cola = Cola([qrz]);
        await cola.EncolarAsync(Guardado(), false);

        for (var i = 0; i < ColaDeSubidas.IntentosMaximos; i++)
        {
            await cola.ProcesarAsync();
            _ahora += TimeSpan.FromHours(2);
        }

        var elemento = cola.Elementos.Single();
        elemento.Fallido.Should().BeTrue();
        elemento.UltimoError.Should().Be("wrong station_callsign");
        cola.Estados().Single(e => e.Medio == MedioDeConfirmacion.QrzCom).Semaforo.Should().Be(SemaforoDeSubida.Rojo);

        await cola.ProcesarAsync();
        qrz.Llamadas.Should().Be(ColaDeSubidas.IntentosMaximos, "un fallido no se reintenta solo");

        qrz.Rechazar = null;
        (await cola.ProcesarAsync(forzar: true)).Should().Be(1);
    }

    [Fact]
    public void Escucha_el_punto_comun_de_guardado_y_no_sube_lo_completado_dos_veces()
    {
        var eqsl = new ServicioDoble(MedioDeConfirmacion.Eqsl);
        var cola = Cola([eqsl]);
        var avisos = new AvisosDeQsos();
        cola.Escuchar(avisos);
        var qso = Guardado();

        avisos.Avisar(qso, TipoDeGuardado.Completado);
        avisos.Avisar(qso, TipoDeGuardado.Nuevo);
        avisos.Avisar(qso, TipoDeGuardado.Nuevo);

        cola.Elementos.Should().ContainSingle();
    }

    [Fact]
    public async Task Con_el_cuaderno_de_qrz_de_verdad_y_una_respuesta_grabada_marca_el_contacto()
    {
        // Respuesta grabada del API del cuaderno de QRZ.com; no sale nada a la red.
        var manejador = ManejadorFalso.ConTexto("RESULT=OK&LOGID=987654&COUNT=1");
        var credenciales = new AlmacenDeCredencialesEnMemoria();
        credenciales.Guardar(ClavesDeCredencial.QrzClaveDeCuaderno, "AAAA-BBBB-CCCC-DDDD");
        var qrz = new ServicioQrzCuaderno(
            new FabricaFalsa(manejador), credenciales, new OpcionesQrz(),
            new PoliticaDeReintentos(esperar: (_, _) => Task.CompletedTask));
        var cola = Cola([qrz]);
        var qso = Guardado();

        await cola.EncolarAsync(qso, false);
        (await cola.ProcesarAsync()).Should().Be(1);

        manejador.Cuerpos.Single().Should().NotContain("REPLACE", "la primera subida no pisa lo que QRZ ya tenga confirmado");
        manejador.Cuerpos.Single().Should().Contain("ADIF=%3CCALL", "el registro va solo, sin cabecera, como en la guía de QRZ");
        Estado(qso, MedioDeConfirmacion.QrzCom)!.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);

        // Corregido con F2 despues de subirlo: ahora si va con REPLACE.
        await cola.EncolarAsync(qso, modificado: true);
        (await cola.ProcesarAsync()).Should().Be(1);
        manejador.Cuerpos[1].Should().Contain("OPTION=REPLACE", "un reenvío tras F2 sustituye en vez de dar duplicado");
    }

    [Fact]
    public async Task Un_contacto_borrado_mientras_esperaba_sale_de_la_cola()
    {
        var eqsl = new ServicioDoble(MedioDeConfirmacion.Eqsl);
        var cola = Cola([eqsl]);
        var qso = Guardado();
        await cola.EncolarAsync(qso, false);
        await _cuaderno.EliminarAsync(qso.Id);

        await cola.ProcesarAsync();

        cola.Elementos.Should().BeEmpty();
        eqsl.Llamadas.Should().Be(0);
    }

    /// <summary>Servicio de confirmacion de mentira.</summary>
    // ── Clave rechazada: la cola se para, no reintenta en silencio ───────────

    [Fact]
    public async Task Si_el_servicio_dice_que_la_clave_no_vale_la_cola_se_para_sin_gastar_reintentos()
    {
        var qrz = new ServicioDoble(MedioDeConfirmacion.QrzCom) { Parar = "QRZ no reconoce esta clave de API." };
        var cola = Cola([qrz]);
        await cola.EncolarAsync(Guardado("DL1ABC"), false);
        await cola.EncolarAsync(Guardado("DL2DEF"), false);

        (await cola.ProcesarAsync()).Should().Be(0);

        cola.Elementos.Should().HaveCount(2);
        cola.Elementos.Should().OnlyContain(e => e.Intentos == 0 && !e.Fallido, "una clave mala no es culpa del contacto");
        var estado = cola.Estados().Single(e => e.Medio == MedioDeConfirmacion.QrzCom);
        estado.Semaforo.Should().Be(SemaforoDeSubida.Rojo);
        estado.Parada.Should().Be("QRZ no reconoce esta clave de API.");
        estado.Texto.Should().Contain("QRZ no reconoce esta clave de API.").And.Contain("2");
    }

    [Fact]
    public async Task Cuando_la_clave_vuelve_a_valer_lo_parado_sube_y_la_pastilla_se_pone_verde()
    {
        var qrz = new ServicioDoble(MedioDeConfirmacion.QrzCom) { Parar = "clave rechazada" };
        var cola = Cola([qrz]);
        await cola.EncolarAsync(Guardado(), false);
        await cola.ProcesarAsync();

        qrz.Parar = null;
        (await cola.ProcesarAsync()).Should().Be(1);

        var estado = cola.Estados().Single(e => e.Medio == MedioDeConfirmacion.QrzCom);
        estado.Parada.Should().BeNull();
        estado.Semaforo.Should().Be(SemaforoDeSubida.Verde);
    }

    // ── Los contactos que ya estaban en el cuaderno ──────────────────────────

    private Qso GuardadoEl(string indicativo, string cuando) =>
        _cuaderno.Sembrar(CuadernoDePrueba.Contacto(indicativo, "20m", "FT8", cuando));

    private static void MarcarSubido(Qso qso, MedioDeConfirmacion medio) =>
        qso.Confirmaciones.Add(new QsoConfirmacion { Medio = medio, QsoId = qso.Id, Enviado = EstadoDeConfirmacion.Confirmado });

    [Fact]
    public async Task El_recuento_dice_cuantos_estan_subidos_pendientes_con_error_y_sin_subir()
    {
        var qrz = new ServicioDoble(MedioDeConfirmacion.QrzCom) { Rechazar = "fuera de fechas" };
        var cola = Cola([qrz]);
        MarcarSubido(GuardadoEl("EA1AA", "2026-01-01T10:00:00Z"), MedioDeConfirmacion.QrzCom);
        MarcarSubido(GuardadoEl("EA1BB", "2026-02-01T10:00:00Z"), MedioDeConfirmacion.QrzCom);
        GuardadoEl("EA1CC", "2026-03-01T10:00:00Z");
        GuardadoEl("EA1DD", "2026-04-01T10:00:00Z");
        var enCola = GuardadoEl("EA1EE", "2026-05-01T10:00:00Z");
        var conError = GuardadoEl("EA1FF", "2026-06-01T10:00:00Z");

        await cola.EncolarAsync(conError, false);
        for (var i = 0; i < ColaDeSubidas.IntentosMaximos; i++)
        {
            await cola.ProcesarAsync();
            _ahora += TimeSpan.FromHours(2);
        }
        await cola.EncolarAsync(enCola, false);

        var recuento = (await cola.RecuentosAsync()).Single(r => r.Medio == MedioDeConfirmacion.QrzCom);

        recuento.Total.Should().Be(6);
        recuento.Subidos.Should().Be(2);
        recuento.Pendientes.Should().Be(1);
        recuento.ConError.Should().Be(1);
        recuento.SinSubir.Should().Be(2);
    }

    [Fact]
    public async Task Subir_los_anteriores_encola_solo_lo_que_falta_y_no_duplica_lo_ya_subido_ni_lo_ya_en_cola()
    {
        var qrz = new ServicioDoble(MedioDeConfirmacion.QrzCom);
        var cola = Cola([qrz]);
        MarcarSubido(GuardadoEl("EA1AA", "2026-01-01T10:00:00Z"), MedioDeConfirmacion.QrzCom);
        GuardadoEl("EA1CC", "2026-03-01T10:00:00Z");
        GuardadoEl("EA1DD", "2026-04-01T10:00:00Z");
        var yaEnCola = GuardadoEl("EA1EE", "2026-05-01T10:00:00Z");
        await cola.EncolarAsync(yaEnCola, false);

        var encolados = await cola.EncolarAnterioresAsync(MedioDeConfirmacion.QrzCom);

        encolados.Should().Be(2);
        cola.Elementos.Should().HaveCount(3);
        cola.Elementos.Select(e => e.Indicativo).Should().BeEquivalentTo(["EA1CC", "EA1DD", "EA1EE"]);

        // Pedirlo otra vez no mete nada nuevo.
        (await cola.EncolarAnterioresAsync(MedioDeConfirmacion.QrzCom)).Should().Be(0);

        (await cola.ProcesarAsync(forzar: true)).Should().Be(3);
        qrz.Subidos.Select(q => q.Call.Valor).Should().NotContain("EA1AA");
        (await cola.RecuentosAsync()).Single(r => r.Medio == MedioDeConfirmacion.QrzCom).SinSubir.Should().Be(0);
    }

    [Fact]
    public async Task Subir_los_anteriores_desde_una_fecha_deja_fuera_lo_mas_viejo()
    {
        var cola = Cola([new ServicioDoble(MedioDeConfirmacion.QrzCom)]);
        GuardadoEl("EA1AA", "2025-12-31T23:59:00Z");
        GuardadoEl("EA1BB", "2026-01-01T00:00:00Z");
        GuardadoEl("EA1CC", "2026-06-01T10:00:00Z");
        var desde = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        (await cola.RecuentosAsync(desde)).Single(r => r.Medio == MedioDeConfirmacion.QrzCom).SinSubir.Should().Be(2);
        (await cola.EncolarAnterioresAsync(MedioDeConfirmacion.QrzCom, desde)).Should().Be(2);

        cola.Elementos.Select(e => e.Indicativo).Should().BeEquivalentTo(["EA1BB", "EA1CC"]);
    }

    [Fact]
    public async Task Los_anteriores_suben_por_lotes_del_mas_viejo_al_mas_nuevo_y_la_cola_avisa_en_cada_lote()
    {
        var qrz = new ServicioDoble(MedioDeConfirmacion.QrzCom);
        var cola = Cola([qrz]);
        var total = ColaDeSubidas.TamanoDeLote(MedioDeConfirmacion.QrzCom) * 2 + 3;
        for (var i = 0; i < total; i++)
        {
            GuardadoEl($"EA1A{i:000}", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(total - i).ToString("o"));
        }
        var avisos = 0;
        cola.EstadoCambiado += (_, _) => avisos++;

        await cola.EncolarAnterioresAsync(MedioDeConfirmacion.QrzCom);
        avisos = 0;
        (await cola.ProcesarAsync(forzar: true)).Should().Be(total);

        qrz.Lotes.Should().Equal(
            ColaDeSubidas.TamanoDeLote(MedioDeConfirmacion.QrzCom), ColaDeSubidas.TamanoDeLote(MedioDeConfirmacion.QrzCom), 3);
        qrz.Subidos.First().Call.Valor.Should().Be($"EA1A{total - 1:000}", "primero el mas viejo");
        avisos.Should().BeGreaterThanOrEqualTo(3, "el operador ve bajar los pendientes lote a lote");
    }

    [Fact]
    public async Task Cancelar_la_subida_de_anteriores_saca_de_la_cola_lo_que_faltaba_y_respeta_lo_demas()
    {
        var qrz = new ServicioDoble(MedioDeConfirmacion.QrzCom);
        var cola = Cola([qrz]);
        var lote = ColaDeSubidas.TamanoDeLote(MedioDeConfirmacion.QrzCom);
        for (var i = 0; i < lote * 3; i++)
        {
            GuardadoEl($"EA1A{i:000}", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i).ToString("o"));
        }
        await cola.EncolarAnterioresAsync(MedioDeConfirmacion.QrzCom);
        var deHoy = Guardado("DL9XYZ");
        await cola.EncolarAsync(deHoy, false);

        // Se cancela en mitad de la subida: al acabar el primer lote.
        qrz.AlSubir = () => { if (qrz.Lotes.Count == 1) cola.CancelarAnteriores(MedioDeConfirmacion.QrzCom); };
        var subidos = await cola.ProcesarAsync(forzar: true);

        subidos.Should().Be(lote + 1, "el lote en marcha se termina, y el contacto de hoy no era de la subida de anteriores");
        cola.Elementos.Should().BeEmpty();
        qrz.Subidos.Select(q => q.Call.Valor).Should().Contain("DL9XYZ");
        (await cola.RecuentosAsync()).Single(r => r.Medio == MedioDeConfirmacion.QrzCom).SinSubir.Should().Be(lote * 2);
    }

    [Fact]
    public async Task Los_anteriores_tambien_valen_para_los_demas_servicios_y_se_marcan_en_cola()
    {
        var lotw = new ServicioDoble(MedioDeConfirmacion.Lotw);
        var cola = Cola([lotw]);
        var qso = GuardadoEl("EA1AA", "2026-01-01T10:00:00Z");

        (await cola.EncolarAnterioresAsync(MedioDeConfirmacion.Lotw)).Should().Be(1);

        Estado(qso, MedioDeConfirmacion.Lotw)!.Enviado.Should().Be(EstadoDeConfirmacion.Pendiente);
    }

    [Fact]
    public async Task La_marca_de_anterior_sobrevive_a_cerrar_y_abrir_el_programa()
    {
        var qrz = new ServicioDoble(MedioDeConfirmacion.QrzCom);
        GuardadoEl("EA1AA", "2026-01-01T10:00:00Z");
        await Cola([qrz]).EncolarAnterioresAsync(MedioDeConfirmacion.QrzCom);

        var otra = Cola([qrz]);

        otra.Elementos.Single().Anterior.Should().BeTrue();
        otra.CancelarAnteriores(MedioDeConfirmacion.QrzCom).Should().Be(1);
    }

    private sealed class ServicioDoble(MedioDeConfirmacion medio) : IServicioQsl
    {
        public MedioDeConfirmacion Medio => medio;
        public string Nombre => medio.ToString();
        public bool Configurado { get; set; } = true;
        public bool EstaConfigurado => Configurado;
        public bool PuedeSubir { get; set; } = true;
        public bool PuedeDescargar => false;
        public Exception? Fallo { get; set; }
        public string? Rechazar { get; set; }
        public string? Parar { get; set; }
        public Action? AlSubir { get; set; }
        public List<int> Lotes { get; } = [];
        public int Llamadas { get; private set; }
        public List<Qso> Subidos { get; } = [];

        public Task<bool> ComprobarCredencialesAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<ResultadoDeSubida> SubirAsync(
            IReadOnlyList<Qso> qsos, IProgress<ProgresoDeSincronizacion>? progreso = null, CancellationToken ct = default)
        {
            Llamadas++;
            if (Fallo is not null) throw Fallo;
            var motivos = new Dictionary<string, string>();
            if (Parar is not null)
            {
                foreach (var q in qsos) motivos[q.ClaveNatural] = Parar;
                return Task.FromResult(new ResultadoDeSubida(0, qsos.Count, motivos, TimeSpan.Zero) { Parada = Parar });
            }
            Lotes.Add(qsos.Count);
            AlSubir?.Invoke();
            if (Rechazar is not null)
            {
                foreach (var q in qsos) motivos[q.ClaveNatural] = Rechazar;
                return Task.FromResult(new ResultadoDeSubida(0, qsos.Count, motivos, TimeSpan.Zero));
            }
            Subidos.AddRange(qsos.Select(q => new Qso { Call = q.Call, Name = q.Name }));
            return Task.FromResult(new ResultadoDeSubida(qsos.Count, 0, motivos, TimeSpan.Zero));
        }

        public Task<IReadOnlyList<ConfirmacionDescargada>> DescargarAsync(
            DateTimeOffset? desdeUtc, IProgress<ProgresoDeSincronizacion>? progreso = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConfirmacionDescargada>>([]);
    }
}
