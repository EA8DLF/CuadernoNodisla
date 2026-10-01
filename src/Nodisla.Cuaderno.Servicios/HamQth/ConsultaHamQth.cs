using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Red;
using Nodisla.Cuaderno.Servicios.Xml;

namespace Nodisla.Cuaderno.Servicios.HamQth;

/// <summary>Ajustes de HamQTH.</summary>
public sealed record OpcionesHamQth
{
    /// <summary>Usuario de HamQTH, que es el indicativo con el que se registro la cuenta.</summary>
    public string Usuario { get; init; } = string.Empty;

    /// <summary>Direccion del servicio XML.</summary>
    public Uri UrlDeConsulta { get; init; } = new("https://www.hamqth.com/xml.php");

    /// <summary>
    /// Nombre del programa que se envia en cada consulta. HamQTH lo pide sin espacios.
    /// </summary>
    public string Programa { get; init; } = "CuadernoNODISLA";

    /// <summary>
    /// Cuanto se da por buena una sesion. HamQTH declara una hora; se renueva un poco antes
    /// para no gastar una consulta en descubrir que ya habia caducado.
    /// </summary>
    public TimeSpan DuracionDeSesion { get; init; } = TimeSpan.FromMinutes(55);
}

/// <summary>
/// Consulta de indicativos en HamQTH.
/// </summary>
/// <remarks>
/// Es gratuita y funciona con sesion, igual que QRZ.com pero con una caducidad declarada: una
/// hora. Se reutiliza la sesion mientras dure y, si el servicio contesta que ya no existe, se
/// renueva una vez y se repite la consulta.
/// </remarks>
public sealed class ConsultaHamQth : IConsultaIndicativo
{
    private readonly IHttpClientFactory _fabrica;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly PoliticaDeReintentos _reintentos;
    private readonly OpcionesHamQth _opciones;
    private readonly ILogger _log;
    private readonly TimeProvider _reloj;
    private readonly SemaphoreSlim _cerrojoDeSesion = new(1, 1);
    private string? _sesion;
    private DateTimeOffset _caduca;

    /// <summary>Crea la consulta.</summary>
    /// <param name="fabrica">Fabrica de clientes HTTP.</param>
    /// <param name="credenciales">Almacen de secretos.</param>
    /// <param name="opciones">Ajustes de HamQTH.</param>
    /// <param name="reintentos">Politica de reintentos.</param>
    /// <param name="log">Registro de trazas.</param>
    /// <param name="reloj">Reloj, sustituible en las pruebas para comprobar la caducidad.</param>
    public ConsultaHamQth(
        IHttpClientFactory fabrica,
        IAlmacenDeCredenciales credenciales,
        OpcionesHamQth opciones,
        PoliticaDeReintentos? reintentos = null,
        ILogger<ConsultaHamQth>? log = null,
        TimeProvider? reloj = null)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _reintentos = reintentos ?? new PoliticaDeReintentos();
        _log = log ?? NullLogger<ConsultaHamQth>.Instance;
        _reloj = reloj ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Nombre => "HamQTH";

    /// <inheritdoc />
    public bool EstaDisponible =>
        !string.IsNullOrWhiteSpace(_opciones.Usuario)
        && _credenciales.Existe(ClavesDeCredencial.HamQthContrasena);

    /// <inheritdoc />
    public async Task<FichaIndicativo?> ConsultarAsync(
        Indicativo indicativo, CancellationToken ct = default)
    {
        if (indicativo.EsVacio) return null;
        if (!EstaDisponible)
        {
            throw new InvalidOperationException(
                "HamQTH no está configurado: faltan el usuario o la contraseña.");
        }

        var sesion = await ObtenerSesionAsync(renovar: false, ct).ConfigureAwait(false);
        var documento = await ConsultarConSesionAsync(sesion, indicativo, ct).ConfigureAwait(false);

        if (SesionCaducada(documento))
        {
            _log.LogInformation("La sesión de HamQTH caducó; se renueva y se repite la consulta.");
            sesion = await ObtenerSesionAsync(renovar: true, ct).ConfigureAwait(false);
            documento = await ConsultarConSesionAsync(sesion, indicativo, ct).ConfigureAwait(false);
        }

        return Interpretar(documento, indicativo, _log);
    }

    /// <summary>Traduce la respuesta XML de HamQTH a una ficha.</summary>
    /// <param name="documento">Documento devuelto por el servicio.</param>
    /// <param name="indicativo">Indicativo consultado, por si la respuesta no lo repite.</param>
    /// <param name="log">Registro de trazas.</param>
    public static FichaIndicativo? Interpretar(
        XDocument documento, Indicativo indicativo, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(documento);
        log ??= NullLogger.Instance;

        var raiz = documento.Root;
        var ficha = LecturaXml.Hijo(raiz, "search");
        if (ficha is null)
        {
            var error = LecturaXml.Texto(LecturaXml.Hijo(raiz, "session"), "error");
            if (error is not null) log.LogInformation("HamQTH respondió: {Error}", error);
            return null;
        }

        return new FichaIndicativo
        {
            Indicativo = LecturaXml.Texto(ficha, "callsign") is { } call
                ? Indicativo.Crudo(call)
                : indicativo,
            Nombre = LecturaXml.Texto(ficha, "adr_name") ?? LecturaXml.Texto(ficha, "nick"),
            Direccion = LecturaXml.Texto(ficha, "adr_street1"),
            Localidad = LecturaXml.Texto(ficha, "adr_city") ?? LecturaXml.Texto(ficha, "qth"),
            Pais = LecturaXml.Texto(ficha, "country"),
            DivisionPrimaria = LecturaXml.Texto(ficha, "us_state") ?? LecturaXml.Texto(ficha, "district"),
            DivisionSecundaria = LecturaXml.Texto(ficha, "us_county"),
            Localizador = Locator.TryParse(LecturaXml.Texto(ficha, "grid"), out var grid)
                ? grid
                : Locator.Vacio,
            Latitud = LecturaXml.Decimal(ficha, "latitude"),
            Longitud = LecturaXml.Decimal(ficha, "longitude"),
            ZonaCq = LecturaXml.Entero(ficha, "cq"),
            ZonaItu = LecturaXml.Entero(ficha, "itu"),
            Dxcc = LecturaXml.Entero(ficha, "adif"),
            CorreoElectronico = LecturaXml.Texto(ficha, "email"),
            Web = LecturaXml.Texto(ficha, "web"),
            GestorQsl = LecturaXml.Texto(ficha, "qsl_via"),
            Iota = LecturaXml.Texto(ficha, "iota"),
            Imagen = LecturaXml.Direccion(ficha, "picture"),
            UsaLotw = LecturaXml.Texto(ficha, "lotw") is { } lotw ? lotw.StartsWith('Y') : null,
            UsaEqsl = LecturaXml.Texto(ficha, "eqsl") is { } eqsl ? eqsl.StartsWith('Y') : null,
            Fuente = "HamQTH",
        };
    }

    /// <summary>Indica si la respuesta dice que la sesion ya no vale.</summary>
    /// <param name="documento">Documento devuelto por el servicio.</param>
    public static bool SesionCaducada(XDocument documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        var error = LecturaXml.Texto(LecturaXml.Hijo(documento.Root, "session"), "error");
        return error is not null
            && error.Contains("Session does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<XDocument> ConsultarConSesionAsync(
        string sesion, Indicativo indicativo, CancellationToken ct)
    {
        var consulta =
            $"id={Uri.EscapeDataString(sesion)}"
            + $"&callsign={Uri.EscapeDataString(indicativo.Valor)}"
            + $"&prg={Uri.EscapeDataString(_opciones.Programa)}";
        return await PedirAsync(consulta, $"consultar {indicativo.Valor} en HamQTH", ct)
            .ConfigureAwait(false);
    }

    private async Task<string> ObtenerSesionAsync(bool renovar, CancellationToken ct)
    {
        var ahora = _reloj.GetUtcNow();
        if (!renovar && _sesion is { } vigente && ahora < _caduca) return vigente;

        await _cerrojoDeSesion.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ahora = _reloj.GetUtcNow();
            if (!renovar && _sesion is { } yaHecha && ahora < _caduca) return yaHecha;

            var contrasena = _credenciales.Leer(ClavesDeCredencial.HamQthContrasena)
                ?? throw new InvalidOperationException(
                    "No hay contraseña de HamQTH guardada. Configúrela en Configuración › Cuentas y servicios.");

            var consulta =
                $"u={Uri.EscapeDataString(_opciones.Usuario)}"
                + $"&p={Uri.EscapeDataString(contrasena)}";

            var documento = await PedirAsync(consulta, "abrir sesión en HamQTH", ct).ConfigureAwait(false);
            var sesion = LecturaXml.Hijo(documento.Root, "session");
            var id = LecturaXml.Texto(sesion, "session_id");
            if (id is null)
            {
                var error = LecturaXml.Texto(sesion, "error") ?? "sin detalle";
                throw new RespuestaDelServicioException($"HamQTH no dio sesión: {error}");
            }

            _sesion = id;
            _caduca = _reloj.GetUtcNow() + _opciones.DuracionDeSesion;
            return id;
        }
        finally
        {
            _cerrojoDeSesion.Release();
        }
    }

    private async Task<XDocument> PedirAsync(string consulta, string descripcion, CancellationToken ct)
    {
        var url = new UriBuilder(_opciones.UrlDeConsulta) { Query = consulta }.Uri;

        return await _reintentos.EjecutarAsync(descripcion, async testigo =>
        {
            var cliente = _fabrica.CreateClient(NombresDeClienteHttp.HamQth);
            using var http = await cliente.GetAsync(url, testigo).ConfigureAwait(false);
            var cuerpo = await http.Content.ReadAsStringAsync(testigo).ConfigureAwait(false);
            if (!http.IsSuccessStatusCode)
            {
                throw new RespuestaDelServicioException(
                    $"HamQTH respondió {(int)http.StatusCode}.", http.StatusCode);
            }
            try
            {
                return XDocument.Parse(cuerpo);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new RespuestaDelServicioException(
                    $"HamQTH no devolvió un XML válido: {ex.Message}");
            }
        }, ct).ConfigureAwait(false);
    }
}
