using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
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
    public static string CarpetaDeDatos => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CuadernoNodisla");

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        FijarCulturaEspanola();

        var carpetaDeRegistros = Path.Combine(CarpetaDeDatos, "registros");
        Directory.CreateDirectory(carpetaDeRegistros);

        _anfitrion = Host.CreateDefaultBuilder()
            .UseSerilog((_, configuracion) => configuracion
                .MinimumLevel.Information()
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

        AjustarDibujado();

        Log.Information(
            "Cuaderno NODISLA arranca. Nivel de dibujado {Nivel}, modo {Modo}, cultura {Cultura} " +
            "(miles «{Miles}», decimales «{Decimales}»).",
            RenderCapability.Tier >> 16,
            RenderOptions.ProcessRenderMode,
            CultureInfo.CurrentCulture.Name,
            CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator,
            CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

        base.OnStartup(e);

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
    /// Deja el programa hablando espanol: miles con punto, decimales con coma y fechas
    /// dia-mes-ano. Hay que fijar las tres cosas —la cultura del hilo, la de los hilos que
    /// nazcan despues y el idioma de WPF— porque cada una la mira un sitio distinto: los
    /// <c>ToString</c> del codigo miran la del hilo, y los <c>StringFormat</c> de los enlaces
    /// de XAML miran el <c>Language</c> del elemento, que por omision es ingles.
    /// </summary>
    /// <remarks>
    /// Ojo: la frecuencia y los informes en decibelios NO siguen esta cultura. Van siempre con
    /// punto decimal, que es lo que manda ADIF; de eso se encarga
    /// <see cref="Nodisla.Cuaderno.Aplicacion.CasosDeUso.TextoDeFrecuencia"/>.
    /// </remarks>
    private static void FijarCulturaEspanola()
    {
        // useUserOverride en falso a proposito: si no, se heredan las personalizaciones de
        // «Region» de Windows, y en un equipo donde alguien puso el punto como separador
        // decimal el cuaderno escribiria «20,000 contactos» en vez de «20.000».
        var espanol = new CultureInfo("es-ES", useUserOverride: false);

        Thread.CurrentThread.CurrentCulture = espanol;
        Thread.CurrentThread.CurrentUICulture = espanol;
        CultureInfo.DefaultThreadCurrentCulture = espanol;
        CultureInfo.DefaultThreadCurrentUICulture = espanol;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(espanol.IetfLanguageTag)));
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
