using System.Diagnostics;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Ptt;
using Nodisla.Cuaderno.Radio.Pruebas.Dobles;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Radio.Pruebas;

/// <summary>
/// Cuanto se tarda de verdad, desde que el equipo deja de contestar, hasta que sale la suelta
/// del PTT. Con la maquina descansada y con la maquina ahogada.
/// </summary>
/// <remarks>
/// Esto no es una prueba de las de aprobar o suspender: es una medida. Esta aqui para que la
/// cifra de <see cref="ControlFt710.TiempoMaximoDeDeteccion"/> no sea una promesa de palabra.
/// </remarks>
public class MedidaDeDeteccionPruebas
{
    private readonly ITestOutputHelper _salida;

    /// <summary>Crea la medida.</summary>
    /// <param name="salida">Por donde se escriben los tiempos medidos.</param>
    public MedidaDeDeteccionPruebas(ITestOutputHelper salida) => _salida = salida;

    /// <summary>Variable de entorno que enciende la medida.</summary>
    public const string Interruptor = "NODISLA_MEDIR_PTT";

    [Fact]
    [Trait("Categoria", "Medida")]
    public async Task Medir_la_deteccion_del_equipo_perdido()
    {
        if (Environment.GetEnvironmentVariable(Interruptor) is not "1")
        {
            // Esta medida ahoga la máquina a propósito, así que no corre con las demás: si lo
            // hiciera, sería ella la que volvería intermitentes al resto de pruebas. Para
            // lanzarla, pon la variable a 1 y filtra por esta clase, con la salida detallada.
            _salida.WriteLine($"Medida apagada. Para encenderla, {Interruptor}=1.");
            return;
        }

        await MedirAsync("máquina descansada", conCarga: false);
        await MedirAsync("máquina ahogada", conCarga: true);
    }

    private async Task MedirAsync(string caso, bool conCarga)
    {
        using var carga = new CancellationTokenSource();
        var quemadores = new List<Task>();
        if (conCarga)
        {
            // Se ahoga la máquina a conciencia: más hilos ocupados que núcleos hay.
            for (var i = 0; i < Environment.ProcessorCount * 2; i++)
            {
                quemadores.Add(Task.Run(
                    () =>
                    {
                        var ruido = 0d;
                        while (!carga.IsCancellationRequested)
                        {
                            ruido += Math.Sqrt(Random.Shared.NextDouble());
                        }

                        return ruido;
                    },
                    CancellationToken.None));
            }
        }

        var medidas = new List<long>();
        try
        {
            for (var vuelta = 0; vuelta < 5; vuelta++)
            {
                medidas.Add(await UnaMedidaAsync());
            }
        }
        finally
        {
            await carga.CancelAsync();
            try
            {
                await Task.WhenAll(quemadores).WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception)
            {
                // La carga es de mentira: da igual cómo termine.
            }
        }

        var opciones = new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromMilliseconds(50),
            EsperaDeOrden = TimeSpan.FromMilliseconds(500),
        };

        _salida.WriteLine(
            $"[{caso}] medidas (ms): {string.Join(", ", medidas)} | "
            + $"peor {medidas.Max()} | cota declarada {ControlFt710.TiempoMaximoDeDeteccion(opciones).TotalMilliseconds} ms"
            + $" | {Detalle.Value}");

        medidas.Should().NotBeEmpty();
    }

    /// <summary>Detalle de la ultima medida, para que quede en la salida.</summary>
    private static readonly ThreadLocal<string> Detalle = new(() => string.Empty);

    private static async Task<long> UnaMedidaAsync()
    {
        var ajustes = new OpcionesFt710
        {
            IntervaloDeSondeo = TimeSpan.FromMilliseconds(50),
            EsperaDeOrden = TimeSpan.FromMilliseconds(500),
            EsperaDeReconexion = TimeSpan.FromMilliseconds(50),
        };

        var registro = new RegistroDeMentira();
        await using var equipo = new Ft710DeMentira();
        var canal = new CanalTcpCat("127.0.0.1", equipo.Puerto, ajustes.EsperaDeOrden, registro);
        await using var control = new ControlFt710(canal, ajustes, registro);
        await control.ConectarAsync();

        await using var vigilante = new VigilantePtt(control, new OpcionesDelVigilante
        {
            TiempoMaximo = TimeSpan.FromSeconds(60),
            TiempoSinLatido = TimeSpan.FromSeconds(60),
            EsperaDeSuelta = TimeSpan.FromMilliseconds(400),
            EngancharseAlCierreDelProceso = false,
        });

        var soltado = new TaskCompletionSource<MotivoDeSuelta>(TaskCreationOptions.RunContinuationsAsynchronously);
        vigilante.PttSoltado += (_, motivo) => soltado.TrySetResult(motivo);

        var transmision = await vigilante.PedirAntenaAsync("medida");
        while (!equipo.EnAntena)
        {
            await Task.Delay(5);
        }

        var preguntasAntes = equipo.Recibidas.Count(orden => orden == "ID;");
        var reloj = Stopwatch.StartNew();
        equipo.Enmudecer();

        var motivo = await soltado.Task.WaitAsync(TimeSpan.FromSeconds(60));
        reloj.Stop();
        motivo.Should().Be(MotivoDeSuelta.EquipoPerdido);

        var preguntasDespues = equipo.Recibidas.Count(orden => orden == "ID;") - preguntasAntes;
        var apuntes = registro.Apuntes
            .Where(a => a.Mensaje.Contains("no contesta", StringComparison.OrdinalIgnoreCase)
                || a.Mensaje.Contains("perdido", StringComparison.OrdinalIgnoreCase)
                || a.Mensaje.Contains("CAT ID;", StringComparison.Ordinal))
            .Select(a => a.Mensaje + (a.Error is null ? string.Empty : $" [{a.Error.GetType().Name}]"))
            .ToList();
        Detalle.Value = $"{preguntasDespues} preguntas sin respuesta; apuntes: " + string.Join(" // ", apuntes.TakeLast(8));

        await transmision.DisposeAsync();
        return reloj.ElapsedMilliseconds;
    }
}
