using System.Diagnostics;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>
/// Libera lo que el programa tiene cogido (radio, audio, cuaderno) dejando constancia de lo
/// que tarda y de lo que falla, y sin quedarse colgado para siempre.
/// </summary>
/// <remarks>
/// <para>
/// Al cerrar se libera el contenedor de servicios, y con el el puerto COM de la radio y la
/// tarjeta de sonido. Si un controlador no contesta, esa liberacion no vuelve nunca: la
/// ventana ya no esta, pero el proceso se queda vivo con el puerto cogido y la siguiente vez
/// el programa «no conecta con la radio» sin que se sepa por que.
/// </para>
/// <para>
/// La liberacion se hace <b>en el mismo hilo de siempre</b> (hay servicios que solo se dejan
/// tocar desde el de interfaz); lo que se anade es un vigia en otro hilo que, pasado el plazo,
/// lo apunta en el registro y hace lo que se le diga, que en el programa es terminar el
/// proceso.
/// </para>
/// </remarks>
public static class CierreConPlazo
{
    /// <summary>Lo que se espera a que los servicios se liberen antes de darlo por colgado.</summary>
    public static readonly TimeSpan Plazo = TimeSpan.FromSeconds(5);

    /// <summary>A partir de cuanto una liberacion que termina bien merece un aviso.</summary>
    private static readonly TimeSpan Lento = TimeSpan.FromSeconds(1.5);

    /// <summary>Libera, cronometra y registra.</summary>
    /// <param name="que">Que se esta liberando, para el registro.</param>
    /// <param name="liberar">La liberacion. Se ejecuta en el hilo que llama.</param>
    /// <param name="plazo">Cuanto se espera antes de llamar a <paramref name="alVencer"/>.</param>
    /// <param name="alVencer">Que hacer si el plazo vence con la liberacion a medias. Va en otro hilo.</param>
    /// <param name="registro">Donde se apunta.</param>
    /// <returns>Verdadero si la liberacion termino sin fallos.</returns>
    public static bool Liberar(string que, Action liberar, TimeSpan plazo, Action alVencer, ILogger registro)
    {
        ArgumentNullException.ThrowIfNull(liberar);
        ArgumentNullException.ThrowIfNull(alVencer);
        ArgumentNullException.ThrowIfNull(registro);

        var reloj = Stopwatch.StartNew();
        var terminado = 0;

        using var vigia = new Timer(
            _ =>
            {
                if (Volatile.Read(ref terminado) != 0) return;
                registro.Fatal(
                    "La liberación de {Que} lleva más de {Plazo} s sin terminar; se da por colgada.",
                    que, plazo.TotalSeconds);
                alVencer();
            },
            state: null,
            dueTime: plazo,
            period: Timeout.InfiniteTimeSpan);

        try
        {
            liberar();

            if (reloj.Elapsed >= Lento)
            {
                registro.Warning("Liberación de {Que} terminada en {Milisegundos} ms.", que, reloj.ElapsedMilliseconds);
            }
            else
            {
                registro.Information("Liberación de {Que} terminada en {Milisegundos} ms.", que, reloj.ElapsedMilliseconds);
            }

            return true;
        }
        catch (Exception ex)
        {
            registro.Error(ex, "Fallo al liberar {Que} tras {Milisegundos} ms.", que, reloj.ElapsedMilliseconds);
            return false;
        }
        finally
        {
            Volatile.Write(ref terminado, 1);
        }
    }

    /// <summary>
    /// Termina el proceso cuando la liberacion se ha colgado: mejor un cierre brusco que un
    /// proceso invisible con el puerto de la radio cogido.
    /// </summary>
    /// <remarks>
    /// <c>Environment.Exit</c> ejecuta antes los manejadores de fin de proceso, y entre ellos
    /// esta el del vigilante de PTT, que suelta el PTT. Por si tambien eso se cuelga, a los
    /// tres segundos se mata el proceso sin mas.
    /// </remarks>
    /// <param name="codigo">Codigo de salida.</param>
    public static void TerminarElProceso(int codigo)
    {
        Log.CloseAndFlush();

        var ultimoRecurso = new Thread(() =>
        {
            Thread.Sleep(TimeSpan.FromSeconds(3));
            Process.GetCurrentProcess().Kill();
        })
        {
            IsBackground = true,
            Name = "Cierre a la fuerza",
        };
        ultimoRecurso.Start();

        Environment.Exit(codigo);
    }
}
