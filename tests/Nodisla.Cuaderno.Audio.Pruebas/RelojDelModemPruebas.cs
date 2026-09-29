using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Reloj;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// El reloj del modem: lo que mas veces explica que «no decodifica nada».
/// </summary>
/// <remarks>
/// Nada de esto toca la red: se le dan fuentes de hora de mentira y un reloj de sistema puesto
/// a mano, que es la unica forma de comprobar los bordes —el cambio de minuto, el desvio
/// positivo y el negativo— sin esperar a que el reloj de verdad pase por ahi.
/// </remarks>
public class RelojDelModemPruebas
{
    /// <summary>Una fuente de hora que contesta siempre lo mismo.</summary>
    private sealed class FuenteFija : IFuenteDeHora
    {
        private readonly double _desvioMs;
        private readonly double _idaYVuelta;

        public FuenteFija(double desvioMs, double idaYVuelta = 20.0, string nombre = "fuente de prueba")
        {
            _desvioMs = desvioMs;
            _idaYVuelta = idaYVuelta;
            Nombre = nombre;
        }

        public string Nombre { get; }

        public int Consultas { get; private set; }

        public Task<MedidaDeHora?> ConsultarAsync(CancellationToken ct = default)
        {
            Consultas++;
            return Task.FromResult<MedidaDeHora?>(new MedidaDeHora(_desvioMs, _idaYVuelta, Nombre));
        }
    }

    /// <summary>Una fuente de hora que no contesta, como cuando no hay red.</summary>
    private sealed class FuenteMuda : IFuenteDeHora
    {
        public string Nombre => "fuente muda";

        public int Consultas { get; private set; }

        public Task<MedidaDeHora?> ConsultarAsync(CancellationToken ct = default)
        {
            Consultas++;
            return Task.FromResult<MedidaDeHora?>(null);
        }
    }

    /// <summary>
    /// Ajustes sin seguimiento automatico: en las pruebas se mide cuando la prueba lo diga, no
    /// cuando al reloj le apetezca, o las cuentas de consultas no valdrian nada.
    /// </summary>
    private static OpcionesDelReloj Quietas() => new() { SeguimientoAutomatico = false };

    private static RelojDelModem Crear(IFuenteDeHora fuente, DateTimeOffset hora) =>
        new(Quietas(), new[] { fuente }, () => hora);

    [Fact]
    public void SinMedirElDesvioNoEsFiable()
    {
        using var reloj = new RelojDelModem(Quietas(), Array.Empty<IFuenteDeHora>());

        reloj.Desvio.EsFiable.Should().BeFalse();
        reloj.Desvio.DesvioMs.Should().Be(0);
        reloj.Desvio.Fuente.Should().Be("sin medir");
        reloj.EnHora.Should().BeFalse();
        reloj.Resumen().Should().Contain("sin comprobar");
    }

    [Fact]
    public async Task SiNadieContestaElRelojSigueSinSerFiable()
    {
        var muda = new FuenteMuda();
        using var reloj = Crear(muda, DateTimeOffset.UtcNow);

        var desvio = await reloj.MedirAsync();

        desvio.EsFiable.Should().BeFalse();
        desvio.Fuente.Should().Be("sin respuesta");
        muda.Consultas.Should().Be(1);
        reloj.EnHora.Should().BeFalse();
    }

    [Fact]
    public async Task ConUnaMedidaBuenaLaHoraSaleCorregida()
    {
        var hora = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        using var reloj = Crear(new FuenteFija(1500.0), hora);

        var avisos = new List<EstadoDelReloj>();
        reloj.DesvioMedido += (_, estado) => avisos.Add(estado);

        var medido = await reloj.MedirAsync();

        medido.EsFiable.Should().BeTrue();
        medido.DesvioMs.Should().Be(1500.0);
        avisos.Should().ContainSingle();

        // El reloj del sistema adelanta segundo y medio, asi que la hora buena es segundo y
        // medio antes.
        reloj.Ahora.Should().Be(hora - TimeSpan.FromMilliseconds(1500));

        // Y con ese desvio no se esta en hora: FT8 ya decodifica mal.
        reloj.EnHora.Should().BeFalse();
        reloj.Resumen().Should().Contain("adelantado");
    }

    [Fact]
    public async Task LaMedidaSeGuardaYNoSePreguntaDosVecesSeguidas()
    {
        var fuente = new FuenteFija(10.0);
        using var reloj = Crear(fuente, DateTimeOffset.UtcNow);

        await reloj.MedirAsync();
        await reloj.MedirAsync();

        fuente.Consultas.Should().Be(1);

        await reloj.MedirAsync(forzar: true);
        fuente.Consultas.Should().Be(2);
    }

    [Fact]
    public async Task SeQuedaConLaFuenteQueMenosTardo()
    {
        var lenta = new FuenteFija(300.0, idaYVuelta: 400.0, nombre: "lenta");
        var rapida = new FuenteFija(12.0, idaYVuelta: 8.0, nombre: "rápida");

        using var reloj = new RelojDelModem(
            Quietas(),
            new IFuenteDeHora[] { lenta, rapida },
            () => DateTimeOffset.UtcNow);

        var desvio = await reloj.MedirAsync();

        desvio.Fuente.Should().Be("rápida");
        desvio.DesvioMs.Should().Be(12.0);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(1, 15)]
    [InlineData(14, 15)]
    [InlineData(15, 30)]
    [InlineData(44, 45)]
    [InlineData(45, 60)]
    [InlineData(59, 60)]
    public void LaProximaVentanaDeFt8CaeSiempreEnElCuartoDeMinuto(int segundo, int esperado)
    {
        var instante = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero).AddSeconds(segundo);
        var proxima = RelojDelModem.ProximaVentanaDesde(instante, TimeSpan.FromSeconds(15));

        proxima.Should().Be(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero).AddSeconds(esperado));
    }

    [Fact]
    public void EnElBordeDelMinutoLaProximaVentanaEsLaSiguiente()
    {
        // Justo en el segundo cero: la ventana que empieza ahora ya no se puede coger entera,
        // asi que la proxima es la de dentro de quince segundos.
        var borde = new DateTimeOffset(2026, 9, 22, 12, 1, 0, TimeSpan.Zero);
        RelojDelModem.ProximaVentanaDesde(borde, TimeSpan.FromSeconds(15))
            .Should().Be(borde.AddSeconds(15));

        // Y un pelo antes del minuto, la proxima es el propio minuto.
        var casi = borde.AddTicks(-1);
        RelojDelModem.ProximaVentanaDesde(casi, TimeSpan.FromSeconds(15)).Should().Be(borde);
    }

    [Fact]
    public void LaVentanaDeFt4EsDeSieteSegundosYMedio()
    {
        var instante = new DateTimeOffset(2026, 9, 22, 12, 0, 3, TimeSpan.Zero);
        RelojDelModem.ProximaVentanaDesde(instante, TimeSpan.FromSeconds(7.5))
            .Should().Be(new DateTimeOffset(2026, 9, 22, 12, 0, 7, 500, TimeSpan.Zero));
    }

    [Fact]
    public async Task ConElRelojAdelantadoLaVentanaSeCalculaConLaHoraBuena()
    {
        // El sistema marca las 12:00:14,5 pero adelanta dos segundos: de verdad son las
        // 12:00:12,5, asi que la proxima ventana es la de :15 y no la de :30.
        var delSistema = new DateTimeOffset(2026, 9, 22, 12, 0, 14, 500, TimeSpan.Zero);
        using var reloj = Crear(new FuenteFija(2000.0), delSistema);
        await reloj.MedirAsync();

        reloj.Ahora.Should().Be(new DateTimeOffset(2026, 9, 22, 12, 0, 12, 500, TimeSpan.Zero));
        reloj.ProximaVentana(TimeSpan.FromSeconds(15))
            .Should().Be(new DateTimeOffset(2026, 9, 22, 12, 0, 15, TimeSpan.Zero));
    }

    [Fact]
    public async Task ConElRelojAtrasadoLaVentanaTambienSaleBien()
    {
        // El sistema marca las 12:00:14,5 pero atrasa dos segundos: de verdad son las
        // 12:00:16,5, asi que la proxima ventana es la de :30.
        var delSistema = new DateTimeOffset(2026, 9, 22, 12, 0, 14, 500, TimeSpan.Zero);
        using var reloj = Crear(new FuenteFija(-2000.0), delSistema);
        await reloj.MedirAsync();

        reloj.Ahora.Should().Be(new DateTimeOffset(2026, 9, 22, 12, 0, 16, 500, TimeSpan.Zero));
        reloj.ProximaVentana(TimeSpan.FromSeconds(15))
            .Should().Be(new DateTimeOffset(2026, 9, 22, 12, 0, 30, TimeSpan.Zero));
        reloj.Resumen().Should().Contain("atrasado");
    }

    [Fact]
    public async Task ElDesvioSeRemidePorSuCuentaSinQueNadieLoPida()
    {
        // Un reloj puede irse durante una sesión larga, así que se vuelve a medir solo. Aquí el
        // periodo es de milisegundos para no tener la prueba esperando cinco minutos.
        var fuente = new FuenteFija(300.0);
        var opciones = new OpcionesDelReloj
        {
            SeguimientoAutomatico = true,
            PeriodoDeSeguimiento = TimeSpan.FromMilliseconds(100),
            ValidezDeLaMedida = TimeSpan.FromMilliseconds(1),
        };

        using var reloj = new RelojDelModem(opciones, new[] { fuente }, () => DateTimeOffset.UtcNow);
        reloj.SeSigueSolo.Should().BeTrue();

        var medidas = 0;
        reloj.DesvioMedido += (_, _) => Interlocked.Increment(ref medidas);

        await Task.Delay(500);
        await reloj.PararSeguimientoAsync();

        // Sin tocar nada, el reloj ha preguntado la hora varias veces.
        fuente.Consultas.Should().BeGreaterThan(2);
        medidas.Should().BeGreaterThan(0);
        reloj.SeSigueSolo.Should().BeFalse();
    }

    [Fact]
    public async Task ElEstadoLlegaEnPalabrasYConElVeredictoHecho()
    {
        var hora = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        using var reloj = Crear(new FuenteFija(-870.0), hora);

        EstadoDelReloj? avisado = null;
        reloj.DesvioMedido += (_, estado) => avisado = estado;

        await reloj.MedirAsync();

        avisado.Should().NotBeNull();
        avisado!.Calidad.Should().Be(CalidadDelReloj.Regular);
        avisado.Consejo.Should().Contain("decodificar peor");

        reloj.Estado.Calidad.Should().Be(CalidadDelReloj.Regular);
        reloj.Estado.Desvio.Fuente.Should().Be("fuente de prueba");
        reloj.Estado.EsFiable.Should().BeTrue();
        reloj.Estado.Desvio.MedidoUtc.Should().Be(hora);
        reloj.Estado.DesvioParaMostrar.Should().Be("-870 ms");
    }

    [Fact]
    public async Task ElModemSigueCorrigiendoAunqueElOperadorNoHagaNada()
    {
        var hora = new DateTimeOffset(2026, 9, 25, 12, 0, 14, 500, TimeSpan.Zero);
        using var reloj = Crear(new FuenteFija(2000.0), hora);
        await reloj.MedirAsync();

        // Nadie ha tocado el reloj de Windows —sigue marcando lo mismo— y aun así el módem
        // trabaja con la hora buena y con la ventana que toca.
        reloj.Estado.Calidad.Should().Be(CalidadDelReloj.FueraDeVentana);
        reloj.Ahora.Should().Be(new DateTimeOffset(2026, 9, 25, 12, 0, 12, 500, TimeSpan.Zero));
        reloj.ProximaVentana(TimeSpan.FromSeconds(15))
            .Should().Be(new DateTimeOffset(2026, 9, 25, 12, 0, 15, TimeSpan.Zero));
    }

    [Fact]
    public void UnPeriodoQueNoSeaPositivoNoTieneVentana()
    {
        var fallo = () => RelojDelModem.ProximaVentanaDesde(DateTimeOffset.UtcNow, TimeSpan.Zero);
        fallo.Should().Throw<ArgumentOutOfRangeException>();
    }
}
