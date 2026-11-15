using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nodisla.Cuaderno.Ui.Soporte;
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
    private Soporte.InstanciaUnica? _instancia;
    private readonly CancellationTokenSource _alCerrar = new();

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
    /// <returns>
    /// Falso si el cuaderno no se ha podido abrir: ya se ha avisado y el arranque no debe
    /// seguir (no se crea la ventana).
    /// </returns>
    private bool PrepararElCuaderno()
    {
        if (ConfiguracionDeServicios.ConPuertosSimulados) return true;

        return ArranqueDelCuaderno.Intentar(() =>
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
        },
        ex =>
        {
            Log.Fatal(ex, "No se ha podido preparar el cuaderno.");

            MessageBox.Show(
                Nodisla.Cuaderno.Idiomas.Textos.F("Dialogos.NoSeAbreElCuaderno", ex.Message, CarpetaDeDatos),
                Nodisla.Cuaderno.Idiomas.Textos.T("Comun.NombreDelPrograma"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });
    }

    /// <inheritdoc />
    protected override void OnStartup(StartupEventArgs e)
    {
        // Antes que nada, y antes de tocar nada: si ya hay una copia abierta, esta le pide que
        // se ponga delante y sale. Ni registro, ni ajustes, ni cuaderno, ni puertos.
        if (!SerLaPrimeraCopia())
        {
            Shutdown(0);
            return;
        }

        // El idioma va lo primero: hasta el cartel de «no se puede abrir el cuaderno» lo usa.
        Idiomas.IdiomaDeLaInterfaz.Arrancar(Ajustes.AjustesDelPrograma.Leer(CarpetaDeDatos).Idioma);

        // El registro es del arranque, no del contenedor: asi sigue abierto mientras se
        // liberan la radio y el audio al cerrar, y se cierra lo ultimo de todo (OnExit).
        Log.Logger = Soporte.RegistroDelPrograma.Crear(Path.Combine(CarpetaDeDatos, "registros"));

        _anfitrion = Host.CreateDefaultBuilder()
            .ConElRegistro(Log.Logger)
            .ConfigureServices((_, servicios) => servicios.AnadirCuaderno())
            .Build();

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Fallo sin atender en la interfaz.");
            args.Handled = false;
        };

        // Lo que revienta en un hilo que no es el de interfaz (puerto serie, audio, un
        // temporizador) no pasa por el aviso de arriba: tumba el proceso sin dejar rastro. El
        // vigilante de PTT se engancha a este mismo aviso para soltar el PTT; aqui solo se
        // deja escrito el motivo, sin cerrar el registro, que el vigilante aun puede escribir.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Soporte.RedDeFallos.ApuntarFalloSinAtender(Log.Logger, args.ExceptionObject, args.IsTerminating);

        // Red de seguridad para las tareas que se disparan y se olvidan (_ = AlgoAsync()): si
        // nadie las espera y fallan, por omision la excepcion desaparece sin dejar ni una linea
        // en el registro hasta que el recolector de basura las recoge. Confirmado en auditoria
        // el 05-10-2026 con un «no emite y no se explica por que» que podria haber sido esto.
        // No arregla el sitio concreto -eso se hace con su propio try/catch, como ya tiene
        // VistaModeloModemPropio- pero deja constancia de cualquier otro que se escape.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Soporte.RedDeFallos.ApuntarTareaOlvidada(Log.Logger, args.Exception);
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

        AvisarSiLosAjustesNoSeLeyeron();

        // Sin cuaderno no hay programa. Shutdown solo deja pedido el cierre: si no se corta
        // aqui, lo de abajo crea y muestra la ventana sobre un cuaderno que no se ha abierto.
        if (!PrepararElCuaderno())
        {
            Shutdown(1);
            return;
        }

        LanzarLaCopiaDiaria();

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
        _instancia?.AlLlamarOtraCopia(() => Dispatcher.BeginInvoke(TraerAlFrente));
        Log.Information(
            "Ventana principal mostrada. Contenido {Contenido}, tamano {Ancho}x{Alto}, recursos {Recursos}, fondo {Fondo}.",
            ventana.Content?.GetType().Name ?? "ninguno",
            ventana.ActualWidth,
            ventana.ActualHeight,
            Resources.MergedDictionaries.Count,
            TryFindResource("FondoVentana")?.ToString() ?? "no encontrado");
    }

    /// <summary>
    /// Copia diaria del cuaderno: si la ultima copia tiene mas de un dia, se hace otra.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hasta ahora el cuaderno solo se copiaba antes de una migracion, es decir, al cambiar de
    /// version: entre dos versiones podian pasar meses de contactos sin una sola copia.
    /// </para>
    /// <para>
    /// Va en segundo plano y con un rato de margen para no competir con el arranque, con su
    /// propia conexion (<c>VACUUM INTO</c> copia bien aunque se este operando) y la misma poda
    /// que las copias de migracion. Si falla, queda un aviso en el registro y nada mas: una
    /// copia que no sale no puede impedir operar.
    /// </para>
    /// </remarks>
    private void LanzarLaCopiaDiaria()
    {
        if (ConfiguracionDeServicios.ConPuertosSimulados) return;

        var anfitrion = _anfitrion!;
        var alCerrar = _alCerrar.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(EsperaAntesDeLaCopiaDiaria, alCerrar).ConfigureAwait(false);

                using var ambito = anfitrion.Services.CreateScope();
                var migrador = ambito.ServiceProvider.GetRequiredService<Nodisla.Cuaderno.Datos.MigradorDeCuaderno>();
                var copia = await migrador.CrearCopiaSiHaceFaltaAsync(CadaCuantoSeCopiaElCuaderno, ct: alCerrar)
                    .ConfigureAwait(false);

                if (copia is not null) Log.Information("Copia diaria del cuaderno hecha en {Copia}.", copia);
            }
            catch (OperationCanceledException)
            {
                // El programa se ha cerrado antes: ya se hara en el proximo arranque.
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "No se ha podido hacer la copia diaria del cuaderno.");
            }
        });
    }

    private static readonly TimeSpan CadaCuantoSeCopiaElCuaderno = TimeSpan.FromHours(24);
    private static readonly TimeSpan EsperaAntesDeLaCopiaDiaria = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Si <c>ajustes.json</c> no se pudo leer, se dice: en el registro y con un cartel.
    /// </summary>
    /// <remarks>
    /// La lectura que lo descubre es la primera del arranque, la del idioma, y entonces aun no
    /// hay registro montado. El fichero ya esta apartado (o apuntado para no pisarlo); aqui
    /// solo se deja constancia y se avisa, para que el operador no se encuentre el programa
    /// con los ajustes de fabrica sin saber por que ni donde han quedado los suyos.
    /// </remarks>
    private static void AvisarSiLosAjustesNoSeLeyeron()
    {
        if (Ajustes.AjustesDelPrograma.FicheroIlegible is not { } fichero) return;

        Log.Error(
            "Los ajustes guardados no se han podido leer; se arranca con los de fábrica. Lo anterior queda en {Fichero}.",
            fichero);

        // Una verificacion desatendida no se queda esperando a que alguien pulse «Aceptar».
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUADERNO_APARTADA"))) return;

        MessageBox.Show(
            Nodisla.Cuaderno.Idiomas.Textos.F("Dialogos.AjustesIlegibles", fichero),
            Nodisla.Cuaderno.Idiomas.Textos.T("Comun.NombreDelPrograma"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>
    /// Coge el cerrojo de instancia unica. Falso si ya hay otra copia abierta: entonces se la
    /// llama para que se ponga delante y esta no debe seguir arrancando.
    /// </summary>
    /// <remarks>
    /// El modo de demostracion no lleva cerrojo y una carpeta de <c>CUADERNO_CARPETA</c> lleva
    /// el suyo (ver <see cref="Soporte.InstanciaUnica"/>): las capturas de la ayuda y las
    /// verificaciones siguen arrancando con el programa del operador abierto.
    /// </remarks>
    private bool SerLaPrimeraCopia()
    {
        var nombre = Soporte.InstanciaUnica.NombreDelCerrojo(
            Environment.GetEnvironmentVariable("CUADERNO_CARPETA"),
            ConfiguracionDeServicios.ConPuertosSimulados);
        if (nombre is null) return true;

        _instancia = Soporte.InstanciaUnica.Tomar(nombre);
        if (_instancia is not null) return true;

        // Una verificacion apartada no le quita la pantalla a nadie, tampoco asi.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUADERNO_APARTADA")))
        {
            Soporte.InstanciaUnica.LlamarALaPrimera(nombre);
        }

        return false;
    }

    /// <summary>Otra copia ha intentado abrirse: la ventana de esta se pone delante.</summary>
    private void TraerAlFrente()
    {
        if (MainWindow is not { } ventana) return;
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CUADERNO_APARTADA"))) return;

        Log.Information("Se ha intentado abrir otra copia del programa; esta se pone delante.");
        if (ventana.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(ventana);
        ventana.Show();
        ventana.Activate();

        // Activate no siempre basta si Windows no cede el primer plano: un instante de
        // «siempre encima» la saca delante sin dejarla fija ahi.
        var estabaEncima = ventana.Topmost;
        ventana.Topmost = true;
        ventana.Topmost = estabaEncima;
        ventana.Focus();
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
    /// <remarks>
    /// <para>
    /// El orden importa. Antes se cerraba el registro y <b>despues</b> se liberaban los
    /// servicios: si el puerto de la radio o la tarjeta de sonido fallaban al soltarse, no
    /// quedaba ni una linea. Ahora el registro se cierra lo ultimo.
    /// </para>
    /// <para>
    /// La liberacion va con plazo (<see cref="Soporte.CierreConPlazo"/>): si un controlador no
    /// contesta y no vuelve, se apunta y se termina el proceso, en vez de dejar un proceso sin
    /// ventana con el puerto COM cogido.
    /// </para>
    /// </remarks>
    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Cuaderno NODISLA se cierra.");
        _alCerrar.Cancel();

        if (_anfitrion is { } anfitrion)
        {
            _anfitrion = null;
            Soporte.CierreConPlazo.Liberar(
                "los servicios (radio, audio y cuaderno)",
                anfitrion.Dispose,
                Soporte.CierreConPlazo.Plazo,
                () => Soporte.CierreConPlazo.TerminarElProceso(1),
                Log.Logger);
        }

        base.OnExit(e);

        // Mientras quede algo por soltar, otra copia no debe poder arrancar.
        _instancia?.Dispose();

        // Y el registro, lo ultimo de todo.
        Log.CloseAndFlush();
    }
}
