using FluentAssertions;
using Nodisla.Cuaderno.Ui.Soporte;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Al cerrar, lo que tarda o falla al liberar la radio y el audio queda en el registro, y una
/// liberacion colgada no deja el proceso vivo para siempre.
/// </summary>
public class CierreConPlazoPruebas
{
    private sealed class Apuntes : ILogEventSink
    {
        private readonly List<LogEvent> _eventos = [];

        public IReadOnlyList<LogEvent> Eventos
        {
            get
            {
                lock (_eventos) return _eventos.ToList();
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (_eventos) _eventos.Add(logEvent);
        }
    }

    private static (ILogger Registro, Apuntes Apuntes) Registro()
    {
        var apuntes = new Apuntes();
        return (new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(apuntes).CreateLogger(), apuntes);
    }

    [Fact]
    public void Una_liberacion_normal_queda_apuntada_con_lo_que_ha_tardado()
    {
        var (registro, apuntes) = Registro();
        var vencido = false;

        var bien = CierreConPlazo.Liberar("los servicios", () => { }, TimeSpan.FromSeconds(30), () => vencido = true, registro);

        bien.Should().BeTrue();
        vencido.Should().BeFalse();
        apuntes.Eventos.Should().ContainSingle()
            .Which.RenderMessage().Should().Contain("los servicios").And.Contain("ms");
    }

    [Fact]
    public void Un_fallo_al_liberar_se_registra_como_error_y_no_tumba_el_cierre()
    {
        var (registro, apuntes) = Registro();
        var fallo = new InvalidOperationException("El puerto COM7 no existe.");

        var bien = CierreConPlazo.Liberar("los servicios", () => throw fallo, TimeSpan.FromSeconds(30), () => { }, registro);

        bien.Should().BeFalse();
        var evento = apuntes.Eventos.Should().ContainSingle().Which;
        evento.Level.Should().Be(LogEventLevel.Error);
        evento.Exception.Should().BeSameAs(fallo);
    }

    [Fact]
    public void Una_liberacion_colgada_se_apunta_y_dispara_el_cierre_a_la_fuerza()
    {
        var (registro, apuntes) = Registro();
        using var soltar = new ManualResetEventSlim();
        var vencido = false;

        // La liberacion no vuelve hasta que el vigia actua, como un controlador que no contesta.
        CierreConPlazo.Liberar(
            "los servicios",
            () => soltar.Wait(TimeSpan.FromSeconds(20)).Should().BeTrue("el vigia tenia que haber actuado"),
            TimeSpan.FromMilliseconds(100),
            () =>
            {
                vencido = true;
                soltar.Set();
            },
            registro);

        vencido.Should().BeTrue();
        apuntes.Eventos.Should().Contain(e => e.Level == LogEventLevel.Fatal);
    }

    [Fact]
    public void Si_termina_a_tiempo_el_vigia_no_actua_despues()
    {
        var (registro, apuntes) = Registro();
        var vencido = false;

        CierreConPlazo.Liberar("los servicios", () => { }, TimeSpan.FromMilliseconds(50), () => vencido = true, registro);
        Thread.Sleep(300);

        vencido.Should().BeFalse();
        apuntes.Eventos.Should().NotContain(e => e.Level == LogEventLevel.Fatal);
    }
}
