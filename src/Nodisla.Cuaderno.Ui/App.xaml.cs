using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nodisla.Cuaderno.Ui.Vistas;
using Serilog;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nodisla.Cuaderno.Ui;

/// <summary>
/// Arranque de la aplicacion. Monta el anfitrion generico, deja listo el registro con Serilog
/// y abre la ventana principal pidiendosela al contenedor de servicios.
/// </summary>
public partial class App : Application
{
    private IHost? _anfitrion;

    /// <summary>Carpeta de datos del programa dentro del perfil del usuario.</summary>
    /// <remarks>
    /// Con <c>CUADERNO_CARPETA</c> puesta se usa esa otra carpeta. Es para verificar: una
    /// instancia de pruebas, al cerrarse, guarda el estado de los paneles y los ajustes, y no
    /// puede pisar los del operador.
    /// </remarks>
    public static string CarpetaDeDatos =>
        Environment.GetEnvironmentVariable("CUADERNO_CARPETA") is { Length: > 0 } otra
            ? otra
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CuadernoNodisla");

    /// <summary>
    /// Deja el cuaderno listo antes de abrir la ventana: crea la base si no esta y aplica
    /// las migraciones pendientes, con copia de seguridad previa.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Va aqui y no en la ventana porque si el cuaderno no se puede abrir <b>no hay programa</b>:
    /// mas vale decirlo con un cartel claro que arrancar y que fallen las pantallas una a una
    /// sin que se entienda por que.
    /// </para>
    /// <para>
    /// La copia la hace el propio migrador antes de tocar nada, y solo si hay migraciones que
    /// aplicar sobre una base que ya existia. Un cuaderno de miles de contactos no se toca sin
    /// dejar antes una copia con la fecha en el nombre.
    /// </para>
    /// </remarks>
    private void PrepararElCuaderno()
    {
        if (ConfiguracionDeServicios.ConPuertosSimulados) return;

        try
        {
            using var ambito = _anfitrion!.Services.CreateScope();
            var migrador = ambito.ServiceProvider.GetRequiredService<Nodisla.Cuaderno.Datos.MigradorDeCuaderno>();

            var copia = migrador.AplicarMigracionesAsync().GetAwaiter().GetResult();

            // Ojo con lo que devuelve: es la ruta de la COPIA, y sale nula tanto cuando no
            // habia migraciones pendientes como cuando el cuaderno se acababa de crear y no
            // habia nada que copiar. Decir «no habia migraciones» en los dos casos era mentira
            // la mitad de las veces, y justo en el caso mas interesante: el primer arranque.
            Log.Information(
                copia is null
                    ? "Cuaderno listo en {Ruta}."
                    : "Cuaderno migrado en {Ruta}. Copia previa en {Copia}.",
                Path.Combine(CarpetaDeDatos, Nodisla.Cuaderno.Datos.OpcionesCuaderno.NombreDelFichero),
                copia);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "No se ha podido preparar el cuaderno.");

            MessageBox.Show(
                Nodisla.Cuaderno.Idiomas.Textos.F("Dialogos.NoSeAbreElCuaderno", ex.Message, CarpetaDeDatos),
                Nodisla.Cuaderno.Idiomas.Textos.T("Comun.NombreDelPrograma"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // El idioma va lo primero: hasta el cartel de «no se puede abrir el cuaderno» lo usa.
        Idiomas.IdiomaDeLaInterfaz.Arrancar(Ajustes.AjustesDelPrograma.Leer(CarpetaDeDatos).Idioma);

        var carpetaDeRegistros = Path.Combine(CarpetaDeDatos, "registros");
        Directory.CreateDirectory(carpetaDeRegistros);

        _anfitrion = Host.CreateDefaultBuilder()
            .UseSerilog((_, configuracion) => configuracion
                .MinimumLevel.Information()
                // El registro de peticiones HTTP escribe la URL entera, y la API XML de QRZ.com
                // lleva usuario y contrasena en la URL: con esto a Information, la contrasena de
                // Jose quedaba en claro en el fichero de registro (28-09-2026). A Warning solo
                // quedan los fallos, sin URL. Entity Framework, igual: escribia cada consulta
                // SQL y el registro crecia 15 MB al dia.
                .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(carpetaDeRegistros, "cuaderno-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14))
            .ConfigureServices((_, servicios) => servicios.AnadirCuaderno())
            .Build();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Fallo sin atender en la interfaz.");
            args.Handled = false;
        };

        // Red de seguridad para las tareas que se disparan y se olvidan (_ = AlgoAsync()): si
        // nadie las espera y fallan, por omision la excepcion desaparece sin dejar ni una linea
        // en el registro hasta que el recolector de basura las recoge. Confirmado en auditoria
        // el 05-10-2026 con un «no emite y no se explica por que» que podria haber sido esto.
        // No arregla el sitio concreto -eso se hace con su propio try/catch, como ya tiene
        // VistaModeloModemPropio- pero deja constancia de cualquier otro que se escape.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Tarea sin observar fallida (disparada y olvidada).");
            args.SetObserved();
        };

        AjustarDibujado();

        Log.Information(
            "Cuaderno NODISLA arranca. Nivel de dibujado {Nivel}, modo {Modo}, cultura {Cultura} " +
            "(miles «{Miles}», decimales «{Decimales}»).",
            RenderCapability.Tier >> 16,
            RenderOptions.ProcessRenderMode,
            CultureInfo.CurrentCulture.Name,
            CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator,
            CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

        PrepararElCuaderno();

        base.OnStartup(e);

        // CUADERNO_RASTREO_ENLACES=fichero: cada enlace roto de WPF es un control o un dato
        // muerto, y sin depurador no se ven. Se apuntan todos en ese fichero. Tiene que ir ANTES
        // de crear la ventana: los enlaces rotos se quejan una sola vez, al enlazarse, y si el
        // rastreo se enciende despues no queda ninguno apuntado.
        if (Environment.GetEnvironmentVariable("CUADERNO_RASTREO_ENLACES") is { Length: > 0 } rastreo)
        {
            System.Diagnostics.Trace.AutoFlush = true;
            System.Diagnostics.PresentationTraceSources.Refresh();
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(
                new System.Diagnostics.TextWriterTraceListener(rastreo));
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level =
                System.Diagnostics.SourceLevels.Warning;
        }

        var ventana = _anfitrion.Services.GetRequiredService<VentanaPrincipal>();
        MainWindow = ventana;

        // MODO DE VERIFICACION SIN MOLESTAR.
        //
        // Con CUADERNO_APARTADA puesta, la ventana se abre FUERA DE LA PANTALLA y sin
        // activarse: no aparece delante de nadie, no le quita el teclado a quien este
        // trabajando y no sale en la barra de tareas. Aun asi se dibuja de verdad, asi que
        // PrintWindow saca una captura buena de lo que se veria.
        //
        // Existe porque verificar no puede costarle al operador que le roben el ordenador:
        // llevabamos semanas abriendole la ventana encima de lo que estuviera haciendo.
        if (Environment.GetEnvironmentVariable("CUADERNO_APARTADA") is { Length: > 0 })
        {
            ventana.WindowStartupLocation = WindowStartupLocation.Manual;
            ventana.ShowActivated = false;
            ventana.ShowInTaskbar = false;
            ventana.Left = -6000;
            ventana.Top = 0;

            // CUADERNO_TAMANO=1366x768: para comprobar la disposicion en pantallas pequeñas.
            if (Environment.GetEnvironmentVariable("CUADERNO_TAMANO") is { Length: > 0 } tamano
                && tamano.Split('x') is [var an, var al]
                && double.TryParse(an, NumberStyles.Float, CultureInfo.InvariantCulture, out var ancho)
                && double.TryParse(al, NumberStyles.Float, CultureInfo.InvariantCulture, out var alto))
            {
                ventana.Width = ancho;
                ventana.Height = alto;
            }

        }

        ventana.Show();
        Log.Information(
            "Ventana principal mostrada. Contenido {Contenido}, tamano {Ancho}x{Alto}, recursos {Recursos}, fondo {Fondo}.",
            ventana.Content?.GetType().Name ?? "ninguno",
            ventana.ActualWidth,
            ventana.ActualHeight,
            Resources.MergedDictionaries.Count,
            TryFindResource("FondoVentana")?.ToString() ?? "no encontrado");
    }

    /// <summary>
    /// Elige el modo de dibujado antes de abrir la ventana.
    /// </summary>
    /// <remarks>
    /// Aqui solo se atienden los dos casos que se saben de antemano: que el operador lo haya
    /// pedido con la variable de entorno <c>CUADERNO_DIBUJADO</c>, o que el equipo no tenga
    /// aceleracion util (nivel de dibujado cero). El caso de verdad problematico —hardware que
    /// dice estar bien y deja la ventana en blanco— no se puede adivinar: lo detecta
    /// <see cref="Recursos.ComprobacionDeDibujado"/> mirando lo que la ventana ha pintado.
    /// </remarks>
    private static void AjustarDibujado()
    {
        var preferencia = Environment.GetEnvironmentVariable("CUADERNO_DIBUJADO");

        if (string.Equals(preferencia, "hardware", StringComparison.OrdinalIgnoreCase)) return;

        var forzarSoftware = string.Equals(preferencia, "software", StringComparison.OrdinalIgnoreCase);

        if (forzarSoftware || (RenderCapability.Tier >> 16) == 0)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }
    }

    /// <inheritdoc />
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Cuaderno NODISLA se cierra.");
        Log.CloseAndFlush();
        _anfitrion?.Dispose();
        base.OnExit(e);
    }
}
