using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Reloj;

namespace Nodisla.Cuaderno.Audio.Pruebas;

/// <summary>
/// Poner el reloj del ordenador en hora.
/// </summary>
/// <remarks>
/// Aqui no se toca el reloj de nadie: hay un sistema de mentira que apunta lo que le mandan.
/// Es la unica forma honrada de probar esto, porque el caso que hay que asegurar —<b>que no se
/// haga nunca solo</b> y que sin permisos se explique en vez de fallar— no se puede comprobar
/// cambiandole la hora al ordenador de Jose a ver que pasa.
/// </remarks>
public class SincronizadorDeHoraPruebas
{
    private static readonly DateTimeOffset Cuando = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Un sistema de mentira que apunta lo que le piden.</summary>
    private sealed class SistemaDeMentira : IRelojDelSistema
    {
        private readonly List<string> _ordenes = [];

        public bool HayPermisosParaCambiarLaHora { get; init; }

        public bool Revienta { get; init; }

        public DateTime? HoraPuesta { get; private set; }

        public IReadOnlyList<string> Ordenes => _ordenes;

        public void PonerHoraUtc(DateTime instanteUtc)
        {
            if (Revienta)
            {
                throw new InvalidOperationException("Windows no ha dejado cambiar la hora del sistema (error 1314).");
            }

            HoraPuesta = instanteUtc;
        }

        public Task<(int Codigo, string Salida)> EjecutarAsync(
            string programa,
            string argumentos,
            CancellationToken ct = default)
        {
            _ordenes.Add($"{programa} {argumentos}");
            return Task.FromResult((0, "correcto"));
        }
    }

    /// <summary>Una fuente de hora que contesta un desvio fijo.</summary>
    private sealed class FuenteFija(double desvioMs) : IFuenteDeHora
    {
        public string Nombre => "servidor de prueba";

        public Task<MedidaDeHora?> ConsultarAsync(CancellationToken ct = default) =>
            Task.FromResult<MedidaDeHora?>(new MedidaDeHora(desvioMs, 20.0, Nombre));
    }

    private static RelojDelModem Reloj(IFuenteDeHora? fuente = null) => new(
        new OpcionesDelReloj { SeguimientoAutomatico = false },
        fuente is null ? Array.Empty<IFuenteDeHora>() : new[] { fuente },
        () => Cuando);

    [Fact]
    public async Task SinPermisosNoSeFallaConAccesoDenegadoSinoQueSeExplica()
    {
        var sistema = new SistemaDeMentira { HayPermisosParaCambiarLaHora = false };
        using var reloj = Reloj(new FuenteFija(-870));
        var sincronizador = new SincronizadorDeHora(reloj, sistema, ahoraDelSistema: () => Cuando);

        var resultado = await sincronizador.PonerElRelojEnHoraAsync();

        resultado.Hecho.Should().BeFalse();
        sistema.HoraPuesta.Should().BeNull();

        resultado.Mensaje.Should().Contain("administrador");
        resultado.Mensaje.Should().Contain("sincronización de hora de Windows");
        resultado.Mensaje.Should().Contain("hora.roa.es");

        // Y se le dan las órdenes exactas, que es lo único útil en ese momento.
        resultado.Detalle.Should().NotBeNull();
        resultado.Detalle.Should().Contain("w32tm /config");
        resultado.Detalle.Should().Contain("hora.roa.es");
        resultado.DesvioAntesMs.Should().Be(-870);
    }

    [Fact]
    public async Task ConPermisosSePoneLaHoraCorregidaYQuedaRastro()
    {
        var sistema = new SistemaDeMentira { HayPermisosParaCambiarLaHora = true };
        using var reloj = Reloj(new FuenteFija(1500));
        var sincronizador = new SincronizadorDeHora(reloj, sistema, ahoraDelSistema: () => Cuando);

        var resultado = await sincronizador.PonerElRelojEnHoraAsync();

        resultado.Hecho.Should().BeTrue();

        // El reloj del sistema marca las 12:00:00 y adelanta segundo y medio: la hora buena es
        // segundo y medio antes.
        sistema.HoraPuesta.Should().Be(Cuando.UtcDateTime - TimeSpan.FromMilliseconds(1500));

        resultado.DesvioAntesMs.Should().Be(1500);
        resultado.Mensaje.Should().Contain("Reloj puesto en hora");
        resultado.DesvioDespuesMs.Should().Be(1500);
        sincronizador.UltimoIntento.Should().BeSameAs(resultado);
        sincronizador.UltimaSincronizacion.Should().Be(Cuando);
    }

    [Fact]
    public async Task SinSaberLaHoraBuenaNoSeToca()
    {
        var sistema = new SistemaDeMentira { HayPermisosParaCambiarLaHora = true };
        using var reloj = Reloj();
        var sincronizador = new SincronizadorDeHora(reloj, sistema, ahoraDelSistema: () => Cuando);

        var resultado = await sincronizador.PonerElRelojEnHoraAsync();

        resultado.Hecho.Should().BeFalse();
        sistema.HoraPuesta.Should().BeNull();
        resultado.Mensaje.Should().Contain("no se ha podido medir el desvío");
    }

    [Fact]
    public async Task SiWindowsSeNiegaSeCuentaYSeOfreceLaOtraVia()
    {
        var sistema = new SistemaDeMentira { HayPermisosParaCambiarLaHora = true, Revienta = true };
        using var reloj = Reloj(new FuenteFija(1500));
        var sincronizador = new SincronizadorDeHora(reloj, sistema, ahoraDelSistema: () => Cuando);

        var resultado = await sincronizador.PonerElRelojEnHoraAsync();

        resultado.Hecho.Should().BeFalse();
        resultado.Mensaje.Should().Contain("Windows no ha dejado");
        resultado.Detalle.Should().Contain("w32tm /config");
    }

    [Fact]
    public async Task ConfigurarElServicioDeHoraDejaElOrdenadorEnHoraParaSiempre()
    {
        var sistema = new SistemaDeMentira { HayPermisosParaCambiarLaHora = true };
        using var reloj = Reloj(new FuenteFija(-870));
        var sincronizador = new SincronizadorDeHora(reloj, sistema, ahoraDelSistema: () => Cuando);

        var resultado = await sincronizador.ConfigurarServicioDeHoraAsync();

        resultado.Hecho.Should().BeTrue();
        resultado.Via.Should().Be(ViaDeSincronizacion.ServicioConfigurado);

        sistema.Ordenes.Should().HaveCount(3);
        sistema.Ordenes[0].Should().Contain("w32tm /config");
        sistema.Ordenes[0].Should().Contain("hora.roa.es");
        sistema.Ordenes[1].Should().Be("net start w32time");
        sistema.Ordenes[2].Should().Be("w32tm /resync");

        // Y no se ha tocado la hora a mano: de eso se encarga Windows a partir de ahora.
        sistema.HoraPuesta.Should().BeNull();
    }

    [Fact]
    public async Task ConfigurarElServicioSinPermisosTambienSeExplica()
    {
        var sistema = new SistemaDeMentira { HayPermisosParaCambiarLaHora = false };
        using var reloj = Reloj(new FuenteFija(-870));
        var sincronizador = new SincronizadorDeHora(reloj, sistema, ahoraDelSistema: () => Cuando);

        var resultado = await sincronizador.ConfigurarServicioDeHoraAsync();

        resultado.Hecho.Should().BeFalse();
        sistema.Ordenes.Should().BeEmpty();
        resultado.Mensaje.Should().Contain("administrador");
        resultado.Detalle.Should().Contain("w32tm /config");
    }

    [Fact]
    public void CrearElSincronizadorNoTocaNadaPorSuCuenta()
    {
        var sistema = new SistemaDeMentira { HayPermisosParaCambiarLaHora = true };
        using var reloj = Reloj(new FuenteFija(5000));

        var sincronizador = new SincronizadorDeHora(reloj, sistema, ahoraDelSistema: () => Cuando);

        // Aunque el desvío sea enorme y haya permisos de sobra, nadie ha pulsado nada.
        sistema.HoraPuesta.Should().BeNull();
        sistema.Ordenes.Should().BeEmpty();
        sincronizador.UltimaSincronizacion.Should().BeNull();
        sincronizador.UltimoIntento.Should().BeNull();
        sincronizador.SePuedePonerEnHora.Should().BeTrue();
    }

    [Fact]
    public async Task ElPuertoSirveParaTodoLoQueNecesitaLaVentana()
    {
        var sistema = new SistemaDeMentira { HayPermisosParaCambiarLaHora = false };
        using var reloj = Reloj(new FuenteFija(-1290));

        // La ventana solo va a ver el puerto: si desde ahí no se puede pintar el aviso y
        // ofrecer la salida, no sirve de nada que la clase lo tenga.
        ISincronizadorDeHora sincronizador = new SincronizadorDeHora(
            reloj,
            sistema,
            ahoraDelSistema: () => Cuando);

        sincronizador.SePuedePonerEnHora.Should().BeFalse();
        sincronizador.UltimaSincronizacion.Should().BeNull();
        sincronizador.InstruccionesParaHacerloAMano.Should().Contain("w32tm /config");

        var resultado = await sincronizador.PonerElRelojEnHoraAsync();
        resultado.Hecho.Should().BeFalse();
        resultado.DesvioAntesMs.Should().Be(-1290);

        var porElServicio = await sincronizador.ConfigurarServicioDeHoraAsync();
        porElServicio.Hecho.Should().BeFalse();
        porElServicio.Detalle.Should().Contain("w32tm /config");

        // Nada de esto ha tocado el reloj de nadie.
        sistema.HoraPuesta.Should().BeNull();
        sistema.Ordenes.Should().BeEmpty();
    }

    [Fact]
    public void LasInstruccionesAManoLlevanElServidorConfigurado()
    {
        var sistema = new SistemaDeMentira();
        using var reloj = Reloj();

        var conElDeSiempre = new SincronizadorDeHora(reloj, sistema);
        conElDeSiempre.ServidorRecomendado.Should().Be("hora.roa.es");
        conElDeSiempre.InstruccionesParaHacerloAMano.Should().Contain("hora.roa.es");

        var conOtro = new SincronizadorDeHora(
            reloj,
            sistema,
            new OpcionesDelReloj { ServidorRecomendado = "time.cloudflare.com" });

        conOtro.InstruccionesParaHacerloAMano.Should().Contain("time.cloudflare.com");
    }
}
