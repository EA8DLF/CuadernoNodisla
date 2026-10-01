using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Servicios.Actualizaciones;
using Nodisla.Cuaderno.Servicios.ClubLog;
using Nodisla.Cuaderno.Servicios.Credenciales;
using Nodisla.Cuaderno.Servicios.Emparejamiento;
using Nodisla.Cuaderno.Servicios.Eqsl;
using Nodisla.Cuaderno.Servicios.HamQth;
using Nodisla.Cuaderno.Servicios.Lotw;
using Nodisla.Cuaderno.Servicios.Qrz;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios;

/// <summary>Registro de los servicios de confirmacion y de consulta en el contenedor.</summary>
public static class ExtensionesDeServicio
{
    /// <summary>Tiempo de espera de las llamadas normales.</summary>
    private static readonly TimeSpan EsperaNormal = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Tiempo de espera de las descargas grandes. El informe completo de LoTW de un cuaderno
    /// de muchos anos tarda minutos en generarse al otro lado.
    /// </summary>
    private static readonly TimeSpan EsperaDeDescarga = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Registra los clientes HTTP, el almacen de credenciales y las piezas comunes.
    /// </summary>
    /// <param name="servicios">Coleccion de servicios.</param>
    public static IServiceCollection AnadirServiciosDeConfirmacion(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.TryAddSingleton<IAlmacenDeCredenciales>(_ =>
            OperatingSystem.IsWindows()
                ? new AlmacenDeCredencialesDpapi()
                : new AlmacenDeCredencialesEnMemoria());

        servicios.TryAddSingleton<PoliticaDeReintentos>();
        servicios.TryAddSingleton<EmparejadorDeConfirmaciones>();
        servicios.TryAddSingleton<SincronizadorDeConfirmaciones>();
        servicios.TryAddSingleton<ILocalizadorDeTqsl, LocalizadorDeTqsl>();

        Cliente(servicios, NombresDeClienteHttp.Lotw, EsperaDeDescarga);
        Cliente(servicios, NombresDeClienteHttp.Eqsl, EsperaDeDescarga);
        Cliente(servicios, NombresDeClienteHttp.ClubLog, EsperaDeDescarga);
        Cliente(servicios, NombresDeClienteHttp.Qrz, EsperaNormal);
        Cliente(servicios, NombresDeClienteHttp.HamQth, EsperaNormal);

        return servicios;
    }

    /// <summary>Registra LoTW.</summary>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <param name="opciones">Ajustes de LoTW.</param>
    public static IServiceCollection AnadirLotw(this IServiceCollection servicios, OpcionesLotw opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        servicios.AnadirServiciosDeConfirmacion();
        servicios.AddSingleton(opciones);
        servicios.AddSingleton<IServicioQsl>(p => new ServicioLotw(
            p.GetRequiredService<IHttpClientFactory>(),
            p.GetRequiredService<IAlmacenDeCredenciales>(),
            opciones,
            reintentos: p.GetRequiredService<PoliticaDeReintentos>()));
        return servicios;
    }

    /// <summary>Registra eQSL.cc.</summary>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <param name="opciones">Ajustes de eQSL.</param>
    public static IServiceCollection AnadirEqsl(this IServiceCollection servicios, OpcionesEqsl opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        servicios.AnadirServiciosDeConfirmacion();
        servicios.AddSingleton(opciones);
        servicios.AddSingleton<IServicioQsl>(p => new ServicioEqsl(
            p.GetRequiredService<IHttpClientFactory>(),
            p.GetRequiredService<IAlmacenDeCredenciales>(),
            opciones,
            p.GetRequiredService<PoliticaDeReintentos>()));
        return servicios;
    }

    /// <summary>Registra Club Log.</summary>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <param name="opciones">Ajustes de Club Log.</param>
    public static IServiceCollection AnadirClubLog(
        this IServiceCollection servicios, OpcionesClubLog opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        servicios.AnadirServiciosDeConfirmacion();
        servicios.AddSingleton(opciones);
        servicios.AddSingleton<IServicioQsl>(p => new ServicioClubLog(
            p.GetRequiredService<IHttpClientFactory>(),
            p.GetRequiredService<IAlmacenDeCredenciales>(),
            opciones,
            p.GetRequiredService<PoliticaDeReintentos>()));
        return servicios;
    }

    /// <summary>Registra QRZ.com: la consulta de indicativos y el cuaderno en linea.</summary>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <param name="opciones">Ajustes de QRZ.com.</param>
    public static IServiceCollection AnadirQrz(this IServiceCollection servicios, OpcionesQrz opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        servicios.AnadirServiciosDeConfirmacion();
        servicios.AddSingleton(opciones);
        servicios.AddSingleton<IConsultaIndicativo>(p => new ConsultaQrzCom(
            p.GetRequiredService<IHttpClientFactory>(),
            p.GetRequiredService<IAlmacenDeCredenciales>(),
            opciones,
            p.GetRequiredService<PoliticaDeReintentos>()));
        servicios.AddSingleton<IServicioQsl>(p => new ServicioQrzCuaderno(
            p.GetRequiredService<IHttpClientFactory>(),
            p.GetRequiredService<IAlmacenDeCredenciales>(),
            opciones,
            p.GetRequiredService<PoliticaDeReintentos>()));
        return servicios;
    }

    /// <summary>Registra HamQTH.</summary>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <param name="opciones">Ajustes de HamQTH.</param>
    public static IServiceCollection AnadirHamQth(
        this IServiceCollection servicios, OpcionesHamQth opciones)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(opciones);

        servicios.AnadirServiciosDeConfirmacion();
        servicios.AddSingleton(opciones);
        servicios.AddSingleton<IConsultaIndicativo>(p => new ConsultaHamQth(
            p.GetRequiredService<IHttpClientFactory>(),
            p.GetRequiredService<IAlmacenDeCredenciales>(),
            opciones,
            p.GetRequiredService<PoliticaDeReintentos>()));
        return servicios;
    }

    /// <summary>Espera de la consulta a la API de GitHub: corta, no puede hacer esperar a nadie.</summary>
    private static readonly TimeSpan EsperaDeGitHub = TimeSpan.FromSeconds(10);

    /// <summary>Espera de la descarga del instalador (unos 70 MB); se puede cancelar antes.</summary>
    private static readonly TimeSpan EsperaDelInstalador = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Registra el aviso de versiones nuevas: el comprobador contra GitHub y el descargador del
    /// instalador. Registrarlos no sale a la red.
    /// </summary>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <param name="instalada">Version que esta corriendo.</param>
    /// <param name="carpetaDeDescargas">Carpeta temporal del instalador; nula para la de siempre.</param>
    public static IServiceCollection AnadirActualizaciones(
        this IServiceCollection servicios, VersionSemantica instalada, string? carpetaDeDescargas = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(instalada);

        // GitHub rechaza las peticiones sin User-Agent. Se manda el nombre y la version de verdad.
        var agente = new ProductInfoHeaderValue("CuadernoNODISLA", instalada.ToString());
        servicios.AddHttpClient(Red.NombresDeClienteHttp.GitHub, cliente =>
        {
            cliente.Timeout = EsperaDeGitHub;
            cliente.DefaultRequestHeaders.UserAgent.Add(agente);
        });
        servicios.AddHttpClient(Red.NombresDeClienteHttp.GitHubDescargas, cliente =>
        {
            cliente.Timeout = EsperaDelInstalador;
            cliente.DefaultRequestHeaders.UserAgent.Add(agente);
        });

        servicios.TryAddSingleton(p => new ComprobadorDeVersiones(
            p.GetRequiredService<IHttpClientFactory>(),
            instalada,
            RepositorioPublico.CuadernoNodisla,
            p.GetService<Microsoft.Extensions.Logging.ILogger<ComprobadorDeVersiones>>()));
        servicios.TryAddSingleton(p => new DescargadorDeInstalador(
            p.GetRequiredService<IHttpClientFactory>(),
            carpetaDeDescargas,
            p.GetService<Microsoft.Extensions.Logging.ILogger<DescargadorDeInstalador>>()));
        return servicios;
    }

    private static void Cliente(IServiceCollection servicios, string nombre, TimeSpan espera) =>
        servicios.AddHttpClient(nombre, cliente =>
        {
            cliente.Timeout = espera;
            cliente.DefaultRequestHeaders.UserAgent.Add(
                ProductInfoHeaderValue.Parse(NombresDeClienteHttp.AgenteDeUsuario));
        });
}
