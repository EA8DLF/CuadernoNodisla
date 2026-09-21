using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Radio.Control.Rigctld;

/// <summary>
/// Lanza y vigila el proceso <c>rigctld</c> cuando el programa se encarga de arrancarlo.
/// </summary>
/// <remarks>
/// Si el operador ya tiene <c>rigctld</c> corriendo —porque lo comparte con WSJT-X o con otro
/// programa— no hay que lanzar nada: basta con conectarse. Por eso lanzar es una opcion y no
/// la norma; dos procesos abriendo el mismo puerto serie a la vez no acaban bien.
/// </remarks>
public sealed class LanzadorDeRigctld : IDisposable
{
    private readonly OpcionesRigctld _opciones;
    private readonly ILogger _registro;
    private readonly object _candado = new();

    private Process? _proceso;

    /// <summary>Crea el lanzador.</summary>
    /// <param name="opciones">Ajustes del demonio.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    public LanzadorDeRigctld(OpcionesRigctld opciones, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        _opciones = opciones;
        _registro = registro ?? NullLogger.Instance;
    }

    /// <summary>El proceso lanzado por nosotros sigue vivo.</summary>
    public bool EnMarcha
    {
        get
        {
            lock (_candado)
            {
                return _proceso is { HasExited: false };
            }
        }
    }

    /// <summary>
    /// Busca <c>rigctld.exe</c> en los sitios donde suele estar.
    /// </summary>
    /// <returns>La ruta si se encuentra, o nulo.</returns>
    /// <remarks>
    /// El primer sitio donde se mira es la carpeta de Hamlib que instala Log4OM, que es la que
    /// hay en esta maquina.
    /// </remarks>
    public static string? BuscarRigctld()
    {
        var candidatos = new List<string>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Log4OM2",
                "hamlib",
                "rigctld.exe"),
            Path.Combine(AppContext.BaseDirectory, "hamlib", "rigctld.exe"),
            Path.Combine(AppContext.BaseDirectory, "rigctld.exe"),
        };

        var rutas = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var ruta in rutas)
        {
            if (!string.IsNullOrWhiteSpace(ruta))
            {
                candidatos.Add(Path.Combine(ruta.Trim(), "rigctld.exe"));
            }
        }

        foreach (var candidato in candidatos)
        {
            try
            {
                if (File.Exists(candidato))
                {
                    return candidato;
                }
            }
            catch (Exception)
            {
                // Una ruta imposible del PATH no debe tumbar la busqueda.
            }
        }

        return null;
    }

    /// <summary>
    /// Se asegura de que hay un <c>rigctld</c> escuchando: si ya lo lanzamos, no hace nada.
    /// </summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea del arranque.</returns>
    /// <exception cref="FileNotFoundException">Si no se encuentra <c>rigctld.exe</c>.</exception>
    public async Task AsegurarEnMarchaAsync(CancellationToken ct = default)
    {
        if (EnMarcha)
        {
            return;
        }

        var ruta = _opciones.RutaDeRigctld ?? BuscarRigctld()
            ?? throw new FileNotFoundException(
                "No se encuentra rigctld.exe. Indique su ruta en los ajustes de radio.");

        var arranque = new ProcessStartInfo(ruta)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        foreach (var argumento in ConstruirArgumentos())
        {
            arranque.ArgumentList.Add(argumento);
        }

        var proceso = new Process { StartInfo = arranque, EnableRaisingEvents = true };
        if (!proceso.Start())
        {
            proceso.Dispose();
            throw new InvalidOperationException($"No se pudo lanzar {ruta}.");
        }

        lock (_candado)
        {
            _proceso = proceso;
        }

        _registro.LogInformation(
            "Lanzado rigctld ({Ruta}) con: {Argumentos}",
            ruta,
            string.Join(' ', arranque.ArgumentList));

        // Un respiro para que el demonio abra el puerto antes de intentar conectarse.
        await Task.Delay(TimeSpan.FromMilliseconds(600), ct).ConfigureAwait(false);

        if (proceso.HasExited)
        {
            var error = await LeerErrorAsync(proceso).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"rigctld se cerró nada más arrancar (código {proceso.ExitCode}). {error}".Trim());
        }
    }

    /// <summary>
    /// Mata el proceso que hayamos lanzado. Es el ultimo recurso para soltar un PTT pegado:
    /// al cerrarse el puerto serie, casi todos los equipos vuelven a recepcion.
    /// </summary>
    public void Matar()
    {
        Process? proceso;
        lock (_candado)
        {
            proceso = _proceso;
            _proceso = null;
        }

        if (proceso is null)
        {
            return;
        }

        try
        {
            if (!proceso.HasExited)
            {
                proceso.Kill(entireProcessTree: true);
                proceso.WaitForExit(2000);
                _registro.LogWarning("Se ha matado el proceso rigctld.");
            }
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se pudo matar el proceso rigctld.");
        }
        finally
        {
            proceso.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose() => Matar();

    private List<string> ConstruirArgumentos()
    {
        var argumentos = new List<string>
        {
            "-m",
            _opciones.ModeloDeEquipo.ToString(CultureInfo.InvariantCulture),
            "-t",
            _opciones.Puerto.ToString(CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(_opciones.PuertoSerie))
        {
            argumentos.Add("-r");
            argumentos.Add(_opciones.PuertoSerie.Trim());
        }

        if (_opciones.Baudios > 0)
        {
            argumentos.Add("-s");
            argumentos.Add(_opciones.Baudios.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(_opciones.ArgumentosExtra))
        {
            argumentos.AddRange(
                _opciones.ArgumentosExtra.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return argumentos;
    }

    private static async Task<string> LeerErrorAsync(Process proceso)
    {
        var texto = new StringBuilder();
        try
        {
            texto.Append(await proceso.StandardError.ReadToEndAsync().ConfigureAwait(false));
        }
        catch (Exception)
        {
            // Si no se puede leer el error, al menos queda el codigo de salida.
        }

        return texto.ToString().Trim();
    }
}
