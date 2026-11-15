using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Subidas;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// QRZ.com en la configuracion: un recuadro por tarea, la prueba de cada credencial, el aviso
/// de haber pegado el dato en la casilla que no era, el recuento de lo subido y la subida de
/// los contactos que ya estaban en el cuaderno. Nada sale a la red y nada toca los datos del
/// operador.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class QrzEnLaConfiguracionPruebas : IDisposable
{
    private const string ClaveDeApi = "ABCD-0A0B-1C1D-2E2F";

    private readonly RepositorioQsoEnMemoria _cuaderno = new([]);
    private readonly AlmacenDeMentira _almacen = new();
    private readonly AjustesDelPrograma _ajustes = new();
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-qrz-" + Guid.NewGuid().ToString("N"));
    private readonly List<Qso> _subidosAQrz = [];
    private string? _pararQrz;

    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    // ── Montaje ──────────────────────────────────────────────────────────────

    private sealed class SinConsulta : IConsultaIndicativo
    {
        public string Nombre => "QRZ.com";
        public bool EstaDisponible => true;

        public Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default) =>
            Task.FromResult<FichaIndicativo?>(null);
    }

    private sealed class QrzDePapel(QrzEnLaConfiguracionPruebas dueno) : IServicioQsl
    {
        public MedioDeConfirmacion Medio => MedioDeConfirmacion.QrzCom;
        public string Nombre => "QRZ";
        public bool EstaConfigurado => dueno._almacen.Existe(ClavesDeCredencial.QrzClaveDeCuaderno);
        public bool PuedeSubir => EstaConfigurado;
        public bool PuedeDescargar => false;

        public Task<bool> ComprobarCredencialesAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<ResultadoDeSubida> SubirAsync(
            IReadOnlyList<Qso> qsos, IProgress<ProgresoDeSincronizacion>? progreso = null, CancellationToken ct = default)
        {
            var motivos = new Dictionary<string, string>();
            if (dueno._pararQrz is { } parada)
            {
                foreach (var q in qsos) motivos[q.ClaveNatural] = parada;
                return Task.FromResult(new ResultadoDeSubida(0, qsos.Count, motivos, TimeSpan.Zero) { Parada = parada });
            }
            lock (dueno._subidosAQrz) dueno._subidosAQrz.AddRange(qsos);
            return Task.FromResult(new ResultadoDeSubida(qsos.Count, 0, motivos, TimeSpan.Zero));
        }

        public Task<IReadOnlyList<ConfirmacionDescargada>> DescargarAsync(
            DateTimeOffset? desdeUtc, IProgress<ProgresoDeSincronizacion>? progreso = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConfirmacionDescargada>>([]);
    }

    private (VistaModeloQrz Qrz, VistaModeloSubidas Subidas, ColaDeSubidas Cola) Montar(
        PruebaDelCuadernoQrz? cuaderno = null, PruebaDeLaFichaQrz? ficha = null)
    {
        var cola = new ColaDeSubidas(
            _cuaderno,
            () => [new QrzDePapel(this)],
            medio => CuentasDeServicios.Activado(_ajustes.Servicios, medio),
            ruta: null,
            esperar: (_, _) => Task.CompletedTask);
        var subidas = new VistaModeloSubidas(cola, new CompletadorDeQso(new SinConsulta()), _ajustes, _carpeta);
        var qrz = new VistaModeloQrz(
            new SecretoDeServicio(_almacen, ClavesDeCredencial.QrzClaveDeCuaderno, "QRZ.com", "Clave", "Ayuda"),
            new SecretoDeServicio(_almacen, ClavesDeCredencial.QrzContrasena, "QRZ.com", "Contraseña", "Ayuda"),
            subidas,
            _ => Task.FromResult(cuaderno ?? new PruebaDelCuadernoQrz(VeredictoDeQrz.Correcto, "Clave válida.")),
            _ => Task.FromResult(ficha ?? new PruebaDeLaFichaQrz(VeredictoDeQrz.Correcto, "Acceso correcto.")));
        return (qrz, subidas, cola);
    }

    private async Task<Qso> Sembrar(string indicativo, string cuando, bool subidoAQrz = false)
    {
        var qso = new Qso
        {
            Call = Indicativo.Parse(indicativo),
            Band = Banda.Parse("20m"),
            Mode = Modo.Parse("FT8"),
            InicioUtc = DateTimeOffset.Parse(cuando, System.Globalization.CultureInfo.InvariantCulture),
            StationCallsign = Indicativo.Parse("EA8DLF"),
        };
        if (subidoAQrz)
        {
            qso.Confirmaciones.Add(new QsoConfirmacion
            {
                Medio = MedioDeConfirmacion.QrzCom,
                Enviado = EstadoDeConfirmacion.Confirmado,
            });
        }
        await _cuaderno.AnadirAsync(qso);
        return qso;
    }

    // ── El dato en la casilla que no era ─────────────────────────────────────

    [Fact]
    public void Pegar_la_clave_de_api_en_la_casilla_de_la_contrasena_avisa_y_deja_guardar()
    {
        var (qrz, _, _) = Montar();

        qrz.Contrasena.Nuevo = ClaveDeApi;

        qrz.Contrasena.HayAviso.Should().BeTrue();
        qrz.Contrasena.Aviso.Should().Contain("clave de API");
        qrz.Contrasena.GuardarCommand.CanExecute(null).Should().BeTrue("se avisa, no se impide");
    }

    [Fact]
    public void Una_contrasena_normal_en_su_casilla_no_avisa()
    {
        var (qrz, _, _) = Montar();

        qrz.Contrasena.Nuevo = "Una-contrasena-cualquiera";

        qrz.Contrasena.HayAviso.Should().BeFalse();
    }

    [Fact]
    public void Escribir_una_contrasena_en_la_casilla_de_la_clave_avisa_y_una_clave_no()
    {
        var (qrz, _, _) = Montar();

        qrz.ClaveDeApi.Nuevo = "Una-contrasena-cualquiera";
        qrz.ClaveDeApi.HayAviso.Should().BeTrue();

        qrz.ClaveDeApi.Nuevo = "  " + ClaveDeApi + " ";
        qrz.ClaveDeApi.HayAviso.Should().BeFalse("los espacios de haberla pegado no cuentan");

        qrz.ClaveDeApi.Nuevo = string.Empty;
        qrz.ClaveDeApi.HayAviso.Should().BeFalse("una casilla vacia no avisa de nada");
    }

    // ── Lo guardado sigue valiendo ───────────────────────────────────────────

    [Fact]
    public void Los_secretos_se_guardan_con_las_claves_de_siempre_y_la_clave_sin_espacios()
    {
        var (qrz, _, _) = Montar();

        qrz.ClaveDeApi.Nuevo = " " + ClaveDeApi + "\r\n";
        qrz.ClaveDeApi.Guardar();
        qrz.Contrasena.Nuevo = "secreta";
        qrz.Contrasena.Guardar();

        _almacen.Guardadas.Should().Contain("qrz.cuaderno.clave", ClaveDeApi);
        _almacen.Guardadas.Should().Contain("qrz.contrasena", "secreta");
    }

    [Fact]
    public void Lo_que_ya_estaba_guardado_se_ve_sin_reescribirlo()
    {
        // Como lo tiene el operador hoy: secretos en el almacen y casillas en ajustes.json.
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        _almacen.Guardar("qrz.contrasena", "secreta");
        Directory.CreateDirectory(_carpeta);
        File.WriteAllText(
            Path.Combine(_carpeta, AjustesDelPrograma.NombreDelFichero),
            """{ "Servicios": { "CompletarConQrz": true, "UsuarioQrz": "EA8DLF", "SubirAQrz": true } }""");
        var leidos = AjustesDelPrograma.Leer(_carpeta);
        _ajustes.Servicios = leidos.Servicios;

        var (qrz, _, _) = Montar();

        qrz.ClaveDeApi.Guardado.Should().BeTrue();
        qrz.Contrasena.Guardado.Should().BeTrue();
        qrz.Usuario.Should().Be("EA8DLF");
        qrz.Completar.Should().BeTrue();
        qrz.Subida.Activado.Should().BeTrue();
        qrz.EstadoDeLaSubida.Should().Be(EstadoDeServicio.Configurado);
        qrz.EstadoDeLaFicha.Should().Be(EstadoDeServicio.Configurado);
    }

    [Fact]
    public void Activar_y_desactivar_se_escribe_en_los_mismos_campos_de_los_ajustes()
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        var (qrz, _, _) = Montar();

        qrz.Subida.Activado = false;
        qrz.Completar = false;

        _ajustes.Servicios.SubirAQrz.Should().BeFalse();
        _ajustes.Servicios.CompletarConQrz.Should().BeFalse();
        AjustesDelPrograma.Leer(_carpeta).Servicios.SubirAQrz.Should().BeFalse("se guarda en ajustes.json");
        qrz.EstadoDeLaSubidaTexto.Should().Be("Sin activar");
    }

    // ── Probar ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Probar_una_clave_buena_lo_dice_en_verde_y_guarda_antes_lo_tecleado()
    {
        var (qrz, _, _) = Montar(new PruebaDelCuadernoQrz(
            VeredictoDeQrz.Correcto, "Clave válida: da acceso al libro de EA8DLF, que tiene 1.812 contactos en QRZ."));
        qrz.ClaveDeApi.Nuevo = ClaveDeApi;

        await qrz.ProbarLaClaveAsync();

        _almacen.Guardadas.Should().ContainKey("qrz.cuaderno.clave", "se prueba lo que se ve en la casilla");
        qrz.TonoDeLaClave.Should().Be(TonoDeAviso.Correcto);
        qrz.PruebaDeLaClave.Should().Contain("EA8DLF").And.Contain("1.812");
        qrz.ProbandoLaClave.Should().BeFalse();
    }

    [Fact]
    public async Task Probar_una_clave_buena_con_la_subida_sin_activar_lo_avisa()
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        _ajustes.Servicios.SubirAQrz = false;
        var (qrz, _, _) = Montar();

        await qrz.ProbarLaClaveAsync();

        qrz.TonoDeLaClave.Should().Be(TonoDeAviso.Aviso);
        qrz.PruebaDeLaClave.Should().Contain("sin activar");
    }

    [Fact]
    public async Task Probar_una_clave_buena_dice_cuantos_contactos_del_cuaderno_no_estan_subidos()
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        await Sembrar("DL1ABC", "2026-01-01T10:00:00Z");
        await Sembrar("DL2DEF", "2026-02-01T10:00:00Z");
        await Sembrar("DL3GHI", "2026-03-01T10:00:00Z", subidoAQrz: true);
        var (qrz, _, _) = Montar();

        await qrz.ProbarLaClaveAsync();

        qrz.TonoDeLaClave.Should().Be(TonoDeAviso.Correcto);
        qrz.PruebaDeLaClave.Should().Contain("2 contactos");
    }

    [Theory]
    [InlineData(VeredictoDeQrz.ClaveRechazada, TonoDeAviso.Error)]
    [InlineData(VeredictoDeQrz.SinSuscripcion, TonoDeAviso.Error)]
    [InlineData(VeredictoDeQrz.SinRed, TonoDeAviso.Aviso)]
    public async Task Probar_una_clave_que_falla_dice_por_que_con_su_color(VeredictoDeQrz veredicto, TonoDeAviso tono)
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        var (qrz, _, _) = Montar(new PruebaDelCuadernoQrz(veredicto, "Lo que ha dicho QRZ."));

        await qrz.ProbarLaClaveAsync();

        qrz.TonoDeLaClave.Should().Be(tono);
        qrz.PruebaDeLaClave.Should().Be("Lo que ha dicho QRZ.");
    }

    [Fact]
    public async Task Probar_el_acceso_guarda_antes_el_usuario_y_ensena_el_aviso_de_qrz()
    {
        var (qrz, _, _) = Montar(ficha: new PruebaDeLaFichaQrz(VeredictoDeQrz.Correcto, "Acceso correcto.")
        {
            Aviso = "A subscription is required",
        });
        qrz.Usuario = " ea8dlf ";
        qrz.Contrasena.Nuevo = "secreta";

        await qrz.ProbarElAccesoAsync();

        _ajustes.Servicios.UsuarioQrz.Should().Be("ea8dlf");
        _almacen.Guardadas.Should().ContainKey("qrz.contrasena");
        qrz.TonoDelAcceso.Should().Be(TonoDeAviso.Aviso);
        qrz.PruebaDelAcceso.Should().Contain("Acceso correcto.").And.Contain("A subscription is required");
    }

    [Fact]
    public async Task Probar_el_acceso_con_la_contrasena_mala_sale_en_rojo()
    {
        _almacen.Guardar("qrz.contrasena", "mala");
        var (qrz, _, _) = Montar(ficha: new PruebaDeLaFichaQrz(VeredictoDeQrz.ClaveRechazada, "QRZ.com ha rechazado el usuario o la contraseña."));

        await qrz.ProbarElAccesoAsync();

        qrz.TonoDelAcceso.Should().Be(TonoDeAviso.Error);
    }

    // ── La clave rechazada para la cola, y cambiarla la suelta ───────────────

    [Fact]
    public async Task Una_clave_rechazada_para_la_cola_con_aviso_y_guardar_otra_la_pone_en_marcha()
    {
        _almacen.Guardar("qrz.cuaderno.clave", "MALA");
        var (qrz, subidas, cola) = Montar();
        _pararQrz = "QRZ no reconoce esta clave de API.";
        var qso = await Sembrar("DL1ABC", "2026-01-01T10:00:00Z");
        await cola.EncolarAsync(qso, modificado: false);

        await cola.ProcesarAsync();
        subidas.Refrescar();

        qrz.Subida.Semaforo.Should().Be(SemaforoDeSubida.Rojo);
        qrz.Subida.Estado.Should().Contain("QRZ no reconoce esta clave de API.");
        qrz.EstadoDeLaSubidaTexto.Should().Be("Con problemas");
        subidas.Pastillas[3].Semaforo.Should().Be(SemaforoDeSubida.Rojo, "la pastilla de QRZ en la barra lo dice tambien");
        cola.Elementos.Should().ContainSingle().Which.Intentos.Should().Be(0);

        // El operador pone la clave buena.
        _pararQrz = null;
        qrz.ClaveDeApi.Nuevo = ClaveDeApi;
        qrz.ClaveDeApi.Guardar();
        await cola.ProcesarAsync();
        subidas.Refrescar();

        qrz.Subida.Semaforo.Should().Be(SemaforoDeSubida.Verde);
        _subidosAQrz.Should().ContainSingle();
    }

    // ── El recuento y los contactos anteriores ───────────────────────────────

    [Fact]
    public async Task La_fila_dice_cuantos_hay_subidos_y_cuantos_sin_subir()
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        await Sembrar("DL1ABC", "2026-01-01T10:00:00Z");
        await Sembrar("DL2DEF", "2026-02-01T10:00:00Z");
        await Sembrar("DL3GHI", "2026-03-01T10:00:00Z", subidoAQrz: true);
        var (qrz, subidas, _) = Montar();

        await subidas.RecuentoEnCurso;

        qrz.Subida.Recuento!.Subidos.Should().Be(1);
        qrz.Subida.SinSubir.Should().Be(2);
        qrz.Subida.RecuentoTexto.Should().Contain("1 subidos").And.Contain("2 sin subir");
        qrz.Subida.HayAnterioresQueOfrecer.Should().BeTrue("activada y con contactos de antes sin subir");
    }

    [Fact]
    public async Task Con_la_subida_sin_activar_no_se_ofrece_subir_los_anteriores()
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        _ajustes.Servicios.SubirAQrz = false;
        await Sembrar("DL1ABC", "2026-01-01T10:00:00Z");
        var (qrz, subidas, _) = Montar();

        await subidas.RecuentoEnCurso;

        qrz.Subida.SinSubir.Should().Be(1, "el recuento se ve igual");
        qrz.Subida.HayAnterioresQueOfrecer.Should().BeFalse();
    }

    [Fact]
    public async Task Subir_los_anteriores_pide_confirmacion_dice_cuantos_y_los_sube()
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        await Sembrar("DL1ABC", "2025-06-01T10:00:00Z");
        await Sembrar("DL2DEF", "2026-02-01T10:00:00Z");
        await Sembrar("DL3GHI", "2026-03-01T10:00:00Z");
        var (qrz, subidas, cola) = Montar();
        await subidas.RecuentoEnCurso;
        var fila = qrz.Subida;

        await fila.PedirAnterioresAsync();

        fila.PidiendoAnteriores.Should().BeTrue();
        fila.ASubir.Should().Be(3);
        fila.TextoDeConfirmar.Should().Be("Subir 3 contactos");
        cola.Elementos.Should().BeEmpty("hasta confirmar no se encola nada");

        await fila.ConfirmarAnterioresAsync();
        await cola.ProcesarAsync(forzar: true);
        await subidas.RecuentoEnCurso;
        subidas.Refrescar();

        _subidosAQrz.Select(q => q.Call.Valor).Should().Equal("DL1ABC", "DL2DEF", "DL3GHI");
        fila.PidiendoAnteriores.Should().BeFalse();
        fila.AnterioresEnMarcha.Should().BeFalse();
        fila.ParteDeAnteriores.Should().Contain("terminada");
    }

    [Fact]
    public async Task Subir_los_anteriores_desde_una_fecha_solo_cuenta_y_sube_los_de_despues()
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        await Sembrar("DL1ABC", "2025-06-01T10:00:00Z");
        await Sembrar("DL2DEF", "2026-02-01T10:00:00Z");
        var (qrz, subidas, cola) = Montar();
        await subidas.RecuentoEnCurso;
        var fila = qrz.Subida;
        await fila.PedirAnterioresAsync();

        fila.Desde = new DateTime(2026, 1, 1);
        await Esperar(() => fila.ASubir == 1);
        await fila.ConfirmarAnterioresAsync();

        cola.Elementos.Should().ContainSingle().Which.Indicativo.Should().Be("DL2DEF");
    }

    [Fact]
    public async Task Descartar_la_confirmacion_no_sube_nada()
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        await Sembrar("DL1ABC", "2026-02-01T10:00:00Z");
        var (qrz, subidas, cola) = Montar();
        await subidas.RecuentoEnCurso;
        await qrz.Subida.PedirAnterioresAsync();

        qrz.Subida.DescartarAnterioresCommand.Execute(null);

        qrz.Subida.PidiendoAnteriores.Should().BeFalse();
        cola.Elementos.Should().BeEmpty();
    }

    [Fact]
    public async Task Mientras_suben_los_anteriores_se_ve_el_avance_y_se_puede_cancelar()
    {
        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        for (var i = 0; i < 4; i++) await Sembrar($"DL{i}ABC", $"2026-02-0{i + 1}T10:00:00Z");
        var (qrz, subidas, cola) = Montar();
        await subidas.RecuentoEnCurso;
        var fila = qrz.Subida;
        _pararQrz = "parada para que no suban todavía";
        await fila.PedirAnterioresAsync();
        await fila.ConfirmarAnterioresAsync();
        subidas.Refrescar();

        fila.AnterioresEnMarcha.Should().BeTrue();
        fila.ProgresoTexto.Should().Contain("0 de 4");
        fila.CancelarAnterioresCommand.CanExecute(null).Should().BeTrue();

        fila.CancelarAnterioresCommand.Execute(null);
        subidas.Refrescar();

        cola.Elementos.Should().BeEmpty();
        fila.AnterioresEnMarcha.Should().BeFalse();
        fila.ParteDeAnteriores.Should().Contain("4 contactos");
    }

    // ── La tarjeta de «Cuentas y servicios» ──────────────────────────────────

    [Fact]
    public void En_cuentas_y_servicios_qrz_ya_no_pide_secretos_y_lleva_a_su_apartado()
    {
        var modelo = Configuracion();

        var tarjeta = modelo.Tarjetas.Single(t => t.Nombre == "QRZ.com");

        tarjeta.Secretos.Should().BeEmpty("los secretos de QRZ se escriben en un solo sitio");
        tarjeta.HayCampos.Should().BeFalse();
        tarjeta.Aviso.Should().Contain("Subidas y QRZ");
        tarjeta.Configurar!.Execute(null);
        modelo.IndiceDelApartado.Should().Be(VistaModeloAjustes.ApartadoSubidas);
        modelo.Qrz!.ClaveDeApi.Should().BeSameAs(modelo.Secretos.Single(s => s.Clave == ClavesDeCredencial.QrzClaveDeCuaderno));
    }

    private VistaModeloAjustes Configuracion()
    {
        var cola = new ColaDeSubidas(
            _cuaderno,
            () => ServicioQslSimulado.Todos,
            medio => CuentasDeServicios.Activado(_ajustes.Servicios, medio),
            ruta: null,
            esperar: (_, _) => Task.CompletedTask);
        var subidas = new VistaModeloSubidas(cola, new CompletadorDeQso(new SinConsulta()), _ajustes, _carpeta);
        return new VistaModeloAjustes(
            _almacen,
            new ImportarAdif(new Nodisla.Cuaderno.Adif.LectorAdif(), _cuaderno),
            _cuaderno,
            "Aviso de la frase de paso.",
            () => null)
        {
            Subidas = subidas,
        };
    }

    // ── En pantalla ──────────────────────────────────────────────────────────

    public static readonly TheoryData<double, double> Tamanos = new()
    {
        { 1896, 740 },
        { 1330, 490 },
    };

    [Theory]
    [MemberData(nameof(Tamanos))]
    public Task El_apartado_de_subidas_y_qrz_se_pinta_entero_sin_enlaces_rotos_ni_nada_cortado(double ancho, double alto) =>
        HiloDeVentana.Ejecutar(async () =>
        {
            await Sembrar("DL1ABC", "2026-01-01T10:00:00Z");
            var modelo = Configuracion();
            modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoSubidas;
            var (ventana, panel) = Pintar(new PanelDeAjustes { DataContext = modelo }, ancho, alto);
            try
            {
                await modelo.Subidas!.RecuentoEnCurso;
                await Asentar();

                // Abierta la confirmacion de los anteriores, que es lo que mas sitio pide.
                await modelo.Qrz!.Subida.PedirAnterioresAsync();
                modelo.Qrz.ClaveDeApi.Nuevo = "esto no es una clave";
                await Asentar();

                // Solo lo de este apartado: el arbol logico de la ventana arrastra los demas
                // apartados, que cuelgan de un modelo de ventana que esta prueba no monta.
                var vistas = Todos<AjustesDeQrz>(panel).Cast<DependencyObject>()
                    .Concat(Todos<AjustesDeSubidas>(panel))
                    .ToList();
                vistas.Should().HaveCount(2);
                vistas.SelectMany(EnlacesRotos).Should().BeEmpty("cada enlace roto es un control o un dato muerto");
                Todos<AjustesDeQrz>(panel).Should().ContainSingle();

                var casillas = Todos<PasswordBox>(panel).Where(c => c.IsVisible).ToList();
                casillas.Should().HaveCount(2, "la clave de API y la contraseña, cada una en su recuadro");
                casillas.Should().OnlyContain(c => c.ActualWidth > 200, "una casilla estrecha no se usa");

                var cortados = Todos<Control>(panel)
                    .Where(c => c.IsVisible && c is Button or TextBox or PasswordBox or CheckBox or DatePicker && c.TemplatedParent is null)
                    .Where(c => c.TransformToAncestor(panel).Transform(new Point(c.ActualWidth, 0)).X > panel.ActualWidth + 1)
                    .Select(c => $"{c.GetType().Name} «{(c as ContentControl)?.Content}»")
                    .ToList();
                cortados.Should().BeEmpty("nada se sale por la derecha");

                var sinOrden = Todos<Button>(panel)
                    .Where(b => b.IsVisible && b.TemplatedParent is null && b.Command is null)
                    .Select(b => b.Content?.ToString())
                    .ToList();
                sinOrden.Should().BeEmpty("todo botón tiene que hacer algo");

                // Los rotulos que el operador tiene que encontrar.
                var textos = Todos<TextBlock>(panel).Where(t => t.IsVisible).Select(t => t.Text).ToList();
                textos.Should().Contain("Subir mis contactos a QRZ");
                textos.Should().Contain("Rellenar la ficha del corresponsal con QRZ");
                textos.Should().Contain(t => t.Contains("qrz.com/docs/logbook30/api"));
                Todos<Button>(panel).Where(b => b.IsVisible).Select(b => b.Content?.ToString())
                    .Should().Contain(["Probar la clave", "Probar el acceso", "Subir 1 contactos"]);
            }
            finally
            {
                ventana.Close();
            }
        });

    [Fact]
    public Task Teclear_en_la_casilla_de_la_clave_llega_al_modelo_y_guardar_la_vacia() => HiloDeVentana.Ejecutar(async () =>
    {
        var modelo = Configuracion();
        modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoSubidas;
        var (ventana, panel) = Pintar(new PanelDeAjustes { DataContext = modelo }, 1330, 490);
        try
        {
            await Asentar();
            var casilla = Todos<PasswordBox>(panel).First(c => c.DataContext == modelo.Qrz!.ClaveDeApi);
            var guardar = Todos<Button>(panel).First(b => b.DataContext == modelo.Qrz!.ClaveDeApi && Equals(b.Content, "Guardar"));
            guardar.IsEnabled.Should().BeFalse("sin nada tecleado");

            casilla.Password = ClaveDeApi;
            await Asentar();

            modelo.Qrz!.ClaveDeApi.Nuevo.Should().Be(ClaveDeApi);
            guardar.IsEnabled.Should().BeTrue();

            ((IInvokeProvider)new ButtonAutomationPeer(guardar)).Invoke();
            await Asentar();

            _almacen.Guardadas.Should().Contain("qrz.cuaderno.clave", ClaveDeApi);
            casilla.Password.Should().BeEmpty("guardado: los puntos se van");
        }
        finally
        {
            ventana.Close();
        }
    });

    /// <summary>
    /// Retrato del apartado, para mirarlo sin abrir el programa. Solo escribe si se le dice
    /// donde con <c>CUADERNO_RETRATO_QRZ</c> (una carpeta); sin ella no hace nada.
    /// </summary>
    [Fact]
    public Task Retrato_del_apartado_de_qrz() => HiloDeVentana.Ejecutar(async () =>
    {
        if (Environment.GetEnvironmentVariable("CUADERNO_RETRATO_QRZ") is not { Length: > 0 } carpeta) return;
        Directory.CreateDirectory(carpeta);

        _almacen.Guardar("qrz.cuaderno.clave", ClaveDeApi);
        _almacen.Guardar("qrz.contrasena", "secreta");
        _ajustes.Servicios.UsuarioQrz = "EA8DLF";
        for (var i = 0; i < 37; i++) await Sembrar($"DL{i % 10}A{(char)('A' + i % 26)}", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddHours(i).ToString("o"), i < 5);
        var modelo = Configuracion();
        modelo.IndiceDelApartado = VistaModeloAjustes.ApartadoSubidas;

        foreach (var (nombre, ancho, alto, preparar) in new (string, double, double, Func<Task>)[]
        {
            ("1-al-entrar", 1330, 1100, () => Task.CompletedTask),
            ("2-confirmando-y-avisos", 1330, 1250, async () =>
            {
                await modelo.Qrz!.Subida.PedirAnterioresAsync();
                modelo.Qrz.Contrasena.Nuevo = ClaveDeApi;
                modelo.Qrz.TonoDeLaClave = TonoDeAviso.Correcto;
                modelo.Qrz.PruebaDeLaClave = "Clave válida: da acceso al libro de EA8DLF, que tiene 1.812 contactos en QRZ. En su cuaderno hay 32 contactos que no se han subido a QRZ: puede subirlos aquí arriba, con «Subir los contactos anteriores».";
                modelo.Qrz.TonoDelAcceso = TonoDeAviso.Error;
                modelo.Qrz.PruebaDelAcceso = "QRZ.com ha rechazado el usuario o la contraseña. No se volverá a intentar hasta que los cambie en Configuración › Subidas y QRZ.";
            }),
            ("3-estrecho", 900, 1500, () => Task.CompletedTask),
        })
        {
            var (ventana, panel) = Pintar(new PanelDeAjustes { DataContext = modelo }, ancho, alto);
            try
            {
                ventana.Background = (Brush)Application.Current.FindResource("FondoVentana");
                await modelo.Subidas!.RecuentoEnCurso;
                await preparar();
                await Asentar();
                var mapa = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)panel.ActualWidth, (int)panel.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                var dibujo = new DrawingVisual();
                using (var lienzo = dibujo.RenderOpen())
                {
                    lienzo.DrawRectangle(ventana.Background, null, new Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
                    lienzo.DrawRectangle(new VisualBrush(panel), null, new Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
                }
                mapa.Render(dibujo);
                var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
                png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(mapa));
                using var salida = File.Create(Path.Combine(carpeta, nombre + ".png"));
                png.Save(salida);
            }
            finally
            {
                ventana.Close();
            }
        }
    });

    // ── Utilidades ───────────────────────────────────────────────────────────

    private static async Task Esperar(Func<bool> condicion)
    {
        for (var i = 0; i < 300 && !condicion(); i++) await Task.Delay(10);
        condicion().Should().BeTrue("la cuenta tenía que haber terminado");
    }

    private static (Window Ventana, T Panel) Pintar<T>(T panel, double ancho, double alto)
        where T : FrameworkElement
    {
        var ventana = new Window
        {
            Content = panel,
            Width = ancho,
            Height = alto,
            SizeToContent = SizeToContent.Manual,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -8000,
            Top = 0,
        };
        ventana.Show();
        ventana.UpdateLayout();
        return (ventana, panel);
    }

    private static async Task Asentar()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private static List<string> EnlacesRotos(DependencyObject raiz)
    {
        var rotos = new List<string>();
        var vistos = new HashSet<DependencyObject>();
        var pendientes = new Stack<DependencyObject>();
        pendientes.Push(raiz);

        while (pendientes.Count > 0)
        {
            var nodo = pendientes.Pop();
            if (!vistos.Add(nodo)) continue;

            var valores = nodo.GetLocalValueEnumerator();
            while (valores.MoveNext())
            {
                if (System.Windows.Data.BindingOperations.GetBindingExpression(nodo, valores.Current.Property) is
                    { Status: System.Windows.Data.BindingStatus.PathError or System.Windows.Data.BindingStatus.UpdateTargetError } b)
                {
                    rotos.Add($"{nodo.GetType().Name}.{valores.Current.Property.Name} <- {b.ParentBinding.Path?.Path} ({b.DataItem?.GetType().Name ?? "nulo"})");
                }
            }

            if (nodo is Visual)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(nodo); i++) pendientes.Push(VisualTreeHelper.GetChild(nodo, i));
            }

            foreach (var hijo in LogicalTreeHelper.GetChildren(nodo).OfType<DependencyObject>()) pendientes.Push(hijo);
        }

        return rotos;
    }

    private static IEnumerable<T> Todos<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var hijo = VisualTreeHelper.GetChild(raiz, i);
            if (hijo is T t) yield return t;
            foreach (var nieto in Todos<T>(hijo)) yield return nieto;
        }
    }
}
