using System.IO;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>
/// El registro del programa: un fichero por dia en la carpeta de registros.
/// </summary>
/// <remarks>
/// <para>
/// El registro lo crea y lo cierra el arranque, <b>no el contenedor de servicios</b>. Cuando
/// lo montaba el contenedor, liberarlo cerraba tambien el registro, y ademas se cerraba a
/// mano justo antes: nada de lo que fallara al soltar la radio o el audio quedaba escrito, que
/// es precisamente cuando mas falta hace.
/// </para>
/// </remarks>
public static class RegistroDelPrograma
{
    /// <summary>Cuantos ficheros de registro se conservan: un mes, a uno por dia.</summary>
    /// <remarks>
    /// Eran 14, y en la cuenta entraban tambien los <c>_001</c> de los arranques dobles: una
    /// averia de hace dos semanas ya no tenia registro que mirar.
    /// </remarks>
    public const int FicherosAConservar = 31;

    /// <summary>Crea el registro en la carpeta indicada.</summary>
    /// <param name="carpetaDeRegistros">Carpeta de los ficheros <c>cuaderno-AAAAMMDD.log</c>.</param>
    public static Serilog.Core.Logger Crear(string carpetaDeRegistros)
    {
        Directory.CreateDirectory(carpetaDeRegistros);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            // El registro de peticiones HTTP escribe la URL entera, y la API XML de QRZ.com
            // lleva usuario y contrasena en la URL: con esto a Information, la contrasena de
            // Jose quedaba en claro en el fichero de registro (28-09-2026). A Warning solo
            // quedan los fallos, sin URL. Entity Framework, igual: escribia cada consulta
            // SQL y el registro crecia 15 MB al dia.
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(carpetaDeRegistros, "cuaderno-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: FicherosAConservar)
            .CreateLogger();
    }

    /// <summary>
    /// Hace que el contenedor escriba en el registro <b>sin hacerse dueno de el</b>: liberar
    /// el contenedor no lo cierra.
    /// </summary>
    /// <param name="constructor">Constructor del anfitrion.</param>
    /// <param name="registro">El registro creado con <see cref="Crear"/>.</param>
    public static IHostBuilder ConElRegistro(this IHostBuilder constructor, ILogger registro) =>
        constructor.UseSerilog(registro, dispose: false);
}
