using System.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Ui.Soporte;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El registro sigue vivo mientras se cierra el programa, y los fallos que nadie atiende
/// quedan escritos.
/// </summary>
public sealed class RegistroYFallosAlCerrarPruebas : IDisposable
{
    private readonly string _carpeta = Path.Combine(
        Path.GetTempPath(), "cuaderno-registro-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Carpeta temporal: si Windows la tiene cogida, ya la limpiara.
        }
    }

    private string LoEscrito() =>
        string.Concat(Directory.GetFiles(_carpeta, "cuaderno-*.log").Select(LeerCompartido));

    private static string LeerCompartido(string ruta)
    {
        using var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var lector = new StreamReader(flujo);
        return lector.ReadToEnd();
    }

    private sealed class ServicioQueFallaAlLiberarse : IDisposable
    {
        public void Dispose() => throw new InvalidOperationException("El puerto COM7 no se deja cerrar.");
    }

    [Fact]
    public void Liberar_el_contenedor_no_cierra_el_registro()
    {
        // Antes el contenedor era el dueno del registro y lo cerraba al liberarse: lo que
        // pasara despues —o durante— no llegaba al fichero.
        var registro = RegistroDelPrograma.Crear(_carpeta);
        var anfitrion = Host.CreateDefaultBuilder().ConElRegistro(registro).Build();
        anfitrion.Services.GetRequiredService<ILogger<RegistroYFallosAlCerrarPruebas>>()
            .LogInformation("Linea escrita por un servicio.");

        anfitrion.Dispose();
        registro.Information("Linea escrita despues de liberar el contenedor.");
        registro.Dispose();

        LoEscrito().Should()
            .Contain("Linea escrita por un servicio.")
            .And.Contain("Linea escrita despues de liberar el contenedor.");
    }

    [Fact]
    public void Lo_que_falla_al_liberar_los_servicios_queda_en_el_fichero_de_registro()
    {
        var registro = RegistroDelPrograma.Crear(_carpeta);
        var anfitrion = Host.CreateDefaultBuilder()
            .ConElRegistro(registro)
            .ConfigureServices(servicios => servicios.AddSingleton<ServicioQueFallaAlLiberarse>())
            .Build();
        anfitrion.Services.GetRequiredService<ServicioQueFallaAlLiberarse>();

        var bien = CierreConPlazo.Liberar("los servicios", anfitrion.Dispose, TimeSpan.FromSeconds(30), () => { }, registro);
        registro.Dispose();

        bien.Should().BeFalse();
        LoEscrito().Should().Contain("[ERR]").And.Contain("El puerto COM7 no se deja cerrar.");
    }

    private sealed class Apuntes : ILogEventSink
    {
        public List<LogEvent> Eventos { get; } = [];

        public void Emit(LogEvent logEvent) => Eventos.Add(logEvent);
    }

    [Fact]
    public void Un_fallo_sin_atender_fuera_de_la_interfaz_se_apunta_como_fatal()
    {
        var apuntes = new Apuntes();
        using var registro = new Serilog.LoggerConfiguration().WriteTo.Sink(apuntes).CreateLogger();
        var fallo = new InvalidOperationException("El hilo del puerto serie ha reventado.");

        RedDeFallos.ApuntarFalloSinAtender(registro, fallo, terminaElProceso: true);

        var evento = apuntes.Eventos.Should().ContainSingle().Which;
        evento.Level.Should().Be(LogEventLevel.Fatal);
        evento.Exception.Should().BeSameAs(fallo);
    }

    [Fact]
    public void Un_fallo_sin_atender_que_no_es_una_excepcion_tambien_se_apunta()
    {
        var apuntes = new Apuntes();
        using var registro = new Serilog.LoggerConfiguration().WriteTo.Sink(apuntes).CreateLogger();

        RedDeFallos.ApuntarFalloSinAtender(registro, "algo raro", terminaElProceso: false);

        var evento = apuntes.Eventos.Should().ContainSingle().Which;
        evento.Level.Should().Be(LogEventLevel.Fatal);
        evento.RenderMessage().Should().Contain("algo raro");
    }

    [Fact]
    public void Una_tarea_olvidada_que_falla_se_apunta_como_error()
    {
        var apuntes = new Apuntes();
        using var registro = new Serilog.LoggerConfiguration().WriteTo.Sink(apuntes).CreateLogger();
        var fallo = new AggregateException(new TimeoutException("La radio no contesta."));

        RedDeFallos.ApuntarTareaOlvidada(registro, fallo);

        var evento = apuntes.Eventos.Should().ContainSingle().Which;
        evento.Level.Should().Be(LogEventLevel.Error);
        evento.Exception.Should().BeSameAs(fallo);
    }
}
