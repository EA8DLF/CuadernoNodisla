using System.Globalization;
using System.Net;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Propagacion;
using Nodisla.Cuaderno.Propagacion.Prediccion;
using Nodisla.Cuaderno.Propagacion.Solar;

namespace Nodisla.Cuaderno.Propagacion.Pruebas;

/// <summary>
/// El servicio entero, con una red de mentira. Aqui se comprueba lo que el puerto exige: que
/// nunca se ensena un dato viejo como si fuera de ahora y que el motor dice la verdad.
/// </summary>
public class ServicioDePropagacionPruebas : IDisposable
{
    private static readonly Coordenada Tenerife = new(28.4636, -16.2518);
    private static readonly Coordenada Madrid = new(40.4168, -3.7038);

    private readonly string carpeta = Path.Combine(
        Path.GetTempPath(),
        "nodisla-propagacion-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Con_red_se_traen_los_indices_y_se_avisa_del_cambio()
    {
        var reloj = new RelojFijo(Momento("2026-09-22T08:00:00Z"));
        using var fabrica = FabricaFalsa.Buena();
        var servicio = new ServicioDePropagacion(fabrica, Opciones(), null, null, reloj);

        IndicesSolares? avisados = null;
        servicio.IndicesActualizados += (_, indices) => avisados = indices;

        var traidos = await servicio.ActualizarIndicesAsync(CancellationToken.None);

        traidos.Should().NotBeNull();
        traidos!.FlujoSolar.Should().Be(104);
        traidos.ManchasSolares.Should().Be(85);
        traidos.IndiceK.Should().Be(0.67);
        traidos.VientoSolarKmS.Should().Be(309);
        traidos.DeCache.Should().BeFalse();
        traidos.DescargadoUtc.Should().Be(Momento("2026-09-22T08:00:00Z"));
        avisados.Should().Be(traidos);
        servicio.Indices.Should().Be(traidos);
    }

    [Fact]
    public async Task La_edad_de_los_indices_se_cuenta_desde_la_medida_mas_vieja()
    {
        var reloj = new RelojFijo(Momento("2026-09-22T08:00:00Z"));
        using var fabrica = FabricaFalsa.Buena();
        var servicio = new ServicioDePropagacion(fabrica, Opciones(), null, null, reloj);

        await servicio.ActualizarIndicesAsync(CancellationToken.None);

        // El flujo solar es del dia 21 a las 20:00 UTC; son doce horas, no los minutos que tiene
        // el viento solar.
        servicio.Indices!.MedidoUtc.Should().Be(Momento("2026-09-21T20:00:00Z"));
        servicio.Indices.Descripcion.Should().Contain("12 h").And.Contain("21/09/2026 20:00");
    }

    [Fact]
    public async Task Sin_red_no_hay_indices_y_se_dice_claramente()
    {
        var reloj = new RelojFijo(Momento("2026-09-22T08:00:00Z"));
        using var fabrica = FabricaFalsa.Caida();
        var servicio = new ServicioDePropagacion(fabrica, Opciones(), null, null, reloj);

        var traidos = await servicio.ActualizarIndicesAsync(CancellationToken.None);

        traidos.Should().BeNull();
        servicio.Indices.Should().BeNull();
    }

    [Fact]
    public async Task Al_arrancar_sin_red_se_usa_lo_guardado_y_se_dice_que_es_lo_guardado()
    {
        var opciones = Opciones();
        var reloj = new RelojFijo(Momento("2026-09-22T08:00:00Z"));

        using (var conRed = FabricaFalsa.Buena())
        {
            var primero = new ServicioDePropagacion(conRed, opciones, null, null, reloj);
            await primero.ActualizarIndicesAsync(CancellationToken.None);
        }

        var masTarde = new RelojFijo(Momento("2026-09-23T08:00:00Z"));
        using var sinRed = FabricaFalsa.Caida();
        var segundo = new ServicioDePropagacion(sinRed, opciones, null, null, masTarde);

        segundo.Indices.Should().NotBeNull();
        segundo.Indices!.FlujoSolar.Should().Be(104);
        segundo.Indices.DeCache.Should().BeTrue();
        segundo.Indices.DescargadoUtc.Should().Be(Momento("2026-09-22T08:00:00Z"));
        segundo.Indices.Descripcion.Should().StartWith("Sin red:").And.Contain("1 día");

        // Y un intento fallido no borra lo que habia.
        var reintento = await segundo.ActualizarIndicesAsync(CancellationToken.None);
        reintento.Should().NotBeNull();
        reintento!.DeCache.Should().BeTrue();
    }

    [Fact]
    public void El_servicio_dice_que_motor_lleva_dentro()
    {
        using var fabrica = FabricaFalsa.Caida();
        var servicio = new ServicioDePropagacion(fabrica, Opciones());

        servicio.EsAproximacion.Should().BeTrue();
        servicio.MotorDePrediccion.Should().Be(MotorAproximacionNodisla.NombreDelMotor);
        servicio.MotorDePrediccion.Should().Contain("no es VOACAP");
    }

    [Fact]
    public void Si_se_enchufa_otro_motor_el_servicio_lo_dice_tal_cual()
    {
        using var fabrica = FabricaFalsa.Caida();
        var servicio = new ServicioDePropagacion(fabrica, Opciones(), new MotorDeMentira());

        servicio.MotorDePrediccion.Should().Be("VOACAP 14.2 (proceso externo)");
        servicio.EsAproximacion.Should().BeFalse();
    }

    [Fact]
    public async Task Se_predice_aunque_no_haya_habido_manera_de_traer_los_indices()
    {
        using var fabrica = FabricaFalsa.Caida();
        var servicio = new ServicioDePropagacion(fabrica, Opciones());

        var prediccion = await servicio.PredecirAsync(
            Tenerife,
            Madrid,
            100,
            Momento("2026-09-22T13:00:00Z"),
            CancellationToken.None);

        prediccion.Should().HaveCount(11);
        servicio.Indices.Should().BeNull();
        // Y cada banda lo lleva escrito: se predijo a ciegas.
        prediccion.Should().OnlyContain(p => p.SinDatosSolares);
    }

    [Fact]
    public void El_trayecto_sale_del_servicio_con_el_paso_gris_puesto()
    {
        using var fabrica = FabricaFalsa.Caida();
        var servicio = new ServicioDePropagacion(fabrica, Opciones());

        var trayecto = servicio.CalcularTrayecto(
            Tenerife,
            new Coordenada(50.0, -16.2518),
            Momento("2026-03-21T07:06:00Z"));

        trayecto.EnPasoGris.Should().BeTrue();
        servicio.AnalizarTrayecto(Tenerife, Madrid, Momento("2026-03-21T07:06:00Z"))
            .SolEnDestino.AmaneceUtc.Should().NotBeNull();
    }

    [Fact]
    public void En_esta_maquina_no_hay_motor_externo_instalado()
    {
        using var fabrica = FabricaFalsa.Caida();
        var servicio = new ServicioDePropagacion(fabrica, Opciones());

        // Si algun dia se instala VOACAP, esta prueba avisa de que ya se puede enchufar.
        servicio.BuscarMotorExterno().Should().BeNull();
    }

    [Fact]
    public void El_detector_encuentra_lo_que_le_pongan_delante()
    {
        var encontrado = DetectorDeMotorExterno.Buscar(
            null,
            ["/opt/itshfbc/bin_win"],
            ruta => ruta.EndsWith("voacapl", StringComparison.Ordinal));

        encontrado.Should().NotBeNull();
        encontrado!.Tipo.Should().Be(TipoDeMotorExterno.Voacap);

        DetectorDeMotorExterno.Buscar(null, ["/opt/nada"], _ => false).Should().BeNull();
    }

    private OpcionesPropagacion Opciones() => new()
    {
        RutaDeCache = Path.Combine(carpeta, "indices.json"),
        Reintentos = 0,
        EsperaDeRed = TimeSpan.FromSeconds(2),
    };

    /// <summary>Borra la carpeta temporal del cache.</summary>
    public void Dispose()
    {
        if (Directory.Exists(carpeta))
        {
            Directory.Delete(carpeta, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static DateTimeOffset Momento(string texto) =>
        DateTimeOffset.Parse(texto, CultureInfo.InvariantCulture);

    /// <summary>Reloj parado, para que las pruebas no dependan de la hora que sea.</summary>
    private sealed class RelojFijo(DateTimeOffset momento) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => momento;
    }

    /// <summary>Un motor cualquiera que no es el nuestro, para ver que el servicio lo respeta.</summary>
    private sealed class MotorDeMentira : IMotorDePrediccion
    {
        public string Nombre => "VOACAP 14.2 (proceso externo)";

        public bool EsAproximacion => false;

        public Task<IReadOnlyList<PrediccionDeBanda>> PredecirAsync(
            SolicitudDePrediccion solicitud,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PrediccionDeBanda>>([]);
    }

    /// <summary>Fabrica de clientes HTTP que sirve boletines de mentira o se cae siempre.</summary>
    private sealed class FabricaFalsa(HttpMessageHandler manejador)
        : System.Net.Http.IHttpClientFactory, IDisposable
    {
        public static FabricaFalsa Buena() => new(new ManejadorConBoletines());

        public static FabricaFalsa Caida() => new(new ManejadorCaido());

        public HttpClient CreateClient(string name) => new(manejador, disposeHandler: false);

        public void Dispose() => manejador.Dispose();
    }

    private sealed class ManejadorCaido : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("no hay red");
    }

    private sealed class ManejadorConBoletines : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            var cuerpo = url switch
            {
                FuentesSwpc.UrlBoletinGeofisico => Boletin,
                FuentesSwpc.UrlIndicesSolaresDiarios => Diarios,
                FuentesSwpc.UrlVientoSolar => """[{"proton_speed": 309, "time_tag": "2026-09-22T07:17:00Z"}]""",
                FuentesSwpc.UrlCampoMagnetico => """[{"bt": 4, "bz_gsm": -3.2, "time_tag": "2026-09-22T07:17:00Z"}]""",
                _ => null,
            };

            if (cuerpo is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(cuerpo),
            });
        }

        private const string Boletin = """
            :Product: Geophysical Alert Message wwv.txt
            :Issued: 2026 Sep 22 0615 UTC
            Solar-terrestrial indices for 21 September follow.
            Solar flux 104 and estimated planetary A-index 3.
            The estimated planetary K-index at 0600 UTC on 22 September was 0.67.

            No space weather storms were observed for the past 24 hours.
            """;

        private const string Diarios = """
            :Product: Daily Solar Data            DSD.txt
            #  Date     10.7cm Number  Hemis. Regions Field  Flux   C  M  X  S  1  2  3
            2026 09 20  101     54       90      3    -999      *   2  0  0  4  1  0  0
            2026 09 21  104     85      140      3    -999      *   2  0  0  1  0  0  0
            """;
    }
}
