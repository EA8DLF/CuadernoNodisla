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

        manejador.Cuerpos.Single().Should().Contain("OPTION=REPLACE", "un reenvío tras F2 sustituye en vez de dar duplicado");
        Estado(qso, MedioDeConfirmacion.QrzCom)!.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);
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
        public int Llamadas { get; private set; }
        public List<Qso> Subidos { get; } = [];

        public Task<bool> ComprobarCredencialesAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<ResultadoDeSubida> SubirAsync(
            IReadOnlyList<Qso> qsos, IProgress<ProgresoDeSincronizacion>? progreso = null, CancellationToken ct = default)
        {
            Llamadas++;
            if (Fallo is not null) throw Fallo;
            var motivos = new Dictionary<string, string>();
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
