using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Servicios.Red;
using Nodisla.Cuaderno.Servicios.Xml;

namespace Nodisla.Cuaderno.Servicios.Qrz;

/// <summary>
/// Consulta de indicativos contra el servicio XML de QRZ.com.
/// </summary>
/// <remarks>
/// <para>
/// QRZ.com trabaja con sesion: primero se entrega usuario y contrasena y se recibe una clave de
/// sesion, y a partir de ahi todas las consultas van con esa clave. La clave caduca sin aviso
/// (y tambien si cambia la IP del usuario), de modo que la unica forma correcta de usarla es
/// reutilizarla hasta que el servicio diga que ya no vale y entonces renovarla una vez.
/// Reautenticarse en cada consulta es lo que hace que QRZ limite a un programa.
/// </para>
/// <para>
/// Los separadores de la consulta son puntos y coma, no ampersands: asi lo documenta QRZ.
/// </para>
/// </remarks>
public sealed class ConsultaQrzCom : IConsultaIndicativo
{
    private readonly IHttpClientFactory _fabrica;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly PoliticaDeReintentos _reintentos;
    private readonly OpcionesQrz _opciones;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _cerrojoDeSesion = new(1, 1);
    private string? _clave;

    /// <summary>Crea la consulta.</summary>
    /// <param name="fabrica">Fabrica de clientes HTTP.</param>
    /// <param name="credenciales">Almacen de secretos.</param>
    /// <param name="opciones">Ajustes de QRZ.com.</param>
    /// <param name="reintentos">Politica de reintentos.</param>
    /// <param name="log">Registro de trazas.</param>
    public ConsultaQrzCom(
        IHttpClientFactory fabrica,
        IAlmacenDeCredenciales credenciales,
        OpcionesQrz opciones,
        PoliticaDeReintentos? reintentos = null,
        ILogger<ConsultaQrzCom>? log = null)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _reintentos = reintentos ?? new PoliticaDeReintentos();
        _log = log ?? NullLogger<ConsultaQrzCom>.Instance;
    }

    /// <inheritdoc />
    public string Nombre => "QRZ.com";

    /// <inheritdoc />
    public bool EstaDisponible =>
        !string.IsNullOrWhiteSpace(_opciones.Usuario)
        && _credenciales.Existe(ClavesDeCredencial.QrzContrasena);

    /// <inheritdoc />
    public async Task<FichaIndicativo?> ConsultarAsync(
        Indicativo indicativo, CancellationToken ct = default)
    {
        if (indicativo.EsVacio) return null;
        if (!EstaDisponible)
        {
            throw new InvalidOperationException(
                "QRZ.com no está configurado: faltan el usuario o la contraseña.");
        }

        var clave = await ObtenerClaveAsync(renovar: false, ct).ConfigureAwait(false);
        var documento = await ConsultarConClaveAsync(clave, indicativo, ct).ConfigureAwait(false);

        if (SesionCaducada(documento))
        {
            _log.LogInformation("La sesión de QRZ.com caducó; se renueva y se repite la consulta.");
            clave = await ObtenerClaveAsync(renovar: true, ct).ConfigureAwait(false);
            documento = await ConsultarConClaveAsync(clave, indicativo, ct).ConfigureAwait(false);
        }

        return Interpretar(documento, indicativo, _log);
    }

    /// <summary>Traduce la respuesta XML de QRZ.com a una ficha.</summary>
    /// <param name="documento">Documento devuelto por el servicio.</param>
    /// <param name="indicativo">Indicativo consultado, por si la respuesta no lo repite.</param>
    /// <param name="log">Registro de trazas.</param>
    public static FichaIndicativo? Interpretar(
        XDocument documento, Indicativo indicativo, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(documento);
        log ??= NullLogger.Instance;

        var raiz = documento.Root;
        var sesion = LecturaXml.Hijo(raiz, "Session");
        var error = LecturaXml.Texto(sesion, "Error");
        var ficha = LecturaXml.Hijo(raiz, "Callsign");

        if (ficha is null)
        {
            if (error is not null) log.LogInformation("QRZ.com respondió: {Error}", error);
            return null;
        }

        var nombre = Juntar(
            LecturaXml.Texto(ficha, "fname"), LecturaXml.Texto(ficha, "name"));

        return new FichaIndicativo
        {
            Indicativo = LecturaXml.Texto(ficha, "call") is { } call
                ? Indicativo.Crudo(call)
                : indicativo,
            Nombre = nombre,
            Direccion = LecturaXml.Texto(ficha, "addr1"),
            Localidad = LecturaXml.Texto(ficha, "addr2"),
            Pais = LecturaXml.Texto(ficha, "country"),
            DivisionPrimaria = LecturaXml.Texto(ficha, "state"),
            DivisionSecundaria = LecturaXml.Texto(ficha, "county"),
            Localizador = Locator.TryParse(LecturaXml.Texto(ficha, "grid"), out var grid)
                ? grid
                : Locator.Vacio,
            Latitud = LecturaXml.Decimal(ficha, "lat"),
            Longitud = LecturaXml.Decimal(ficha, "lon"),
            ZonaCq = LecturaXml.Entero(ficha, "cqzone"),
            ZonaItu = LecturaXml.Entero(ficha, "ituzone"),
            Dxcc = LecturaXml.Entero(ficha, "dxcc"),
            CorreoElectronico = LecturaXml.Texto(ficha, "email"),
            Web = LecturaXml.Texto(ficha, "url"),
            GestorQsl = LecturaXml.Texto(ficha, "qslmgr"),
            Iota = LecturaXml.Texto(ficha, "iota"),
            Imagen = LecturaXml.Direccion(ficha, "image"),
            UsaLotw = Marca(LecturaXml.Texto(ficha, "lotw")),
            UsaEqsl = Marca(LecturaXml.Texto(ficha, "eqsl")),

            // Sin suscripcion XML de pago QRZ solo da una parte de la ficha y lo dice aqui.
            Aviso = LecturaXml.Texto(sesion, "Message"),
            Fuente = "QRZ.com",
        };
    }

    /// <summary>Indica si la respuesta dice que la sesion ya no vale.</summary>
    /// <param name="documento">Documento devuelto por el servicio.</param>
    public static bool SesionCaducada(XDocument documento)
    {
        ArgumentNullException.ThrowIfNull(documento);
        var error = LecturaXml.Texto(LecturaXml.Hijo(documento.Root, "Session"), "Error");
        if (error is null) return false;
        return error.Contains("Session Timeout", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Invalid session key", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Session does not exist", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<XDocument> ConsultarConClaveAsync(
        string clave, Indicativo indicativo, CancellationToken ct)
    {
        var consulta = $"s={Uri.EscapeDataString(clave)};callsign={Uri.EscapeDataString(indicativo.Valor)}";
        return await PedirAsync(consulta, $"consultar {indicativo.Valor} en QRZ.com", ct)
            .ConfigureAwait(false);
    }

    private async Task<string> ObtenerClaveAsync(bool renovar, CancellationToken ct)
    {
        if (!renovar && _clave is { } vigente) return vigente;

        await _cerrojoDeSesion.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!renovar && _clave is { } yaHecha) return yaHecha;

            var contrasena = _credenciales.Leer(ClavesDeCredencial.QrzContrasena)
                ?? throw new InvalidOperationException(
                    "No hay contraseña de QRZ.com guardada. Configúrela en los ajustes del programa.");

            var consulta =
                $"username={Uri.EscapeDataString(_opciones.Usuario)}"
                + $";password={Uri.EscapeDataString(contrasena)}"
                + $";agent={Uri.EscapeDataString(_opciones.Agente)}";

            var documento = await PedirAsync(consulta, "abrir sesión en QRZ.com", ct).ConfigureAwait(false);
            var sesion = LecturaXml.Hijo(documento.Root, "Session");

            var clave = LecturaXml.Texto(sesion, "Key");
            if (clave is null)
            {
                var error = LecturaXml.Texto(sesion, "Error") ?? "sin detalle";
                throw new RespuestaDelServicioException($"QRZ.com no dio sesión: {error}");
            }

            // El aviso de suscripcion no impide consultar, pero el operador debe verlo.
            if (LecturaXml.Texto(sesion, "Message") is { } mensaje)
            {
                _log.LogInformation("QRZ.com avisa: {Mensaje}", mensaje);
            }

            _clave = clave;
            return clave;
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
            var cliente = _fabrica.CreateClient(NombresDeClienteHttp.Qrz);
            using var http = await cliente.GetAsync(url, testigo).ConfigureAwait(false);
            var cuerpo = await http.Content.ReadAsStringAsync(testigo).ConfigureAwait(false);
            if (!http.IsSuccessStatusCode)
            {
                throw new RespuestaDelServicioException(
                    $"QRZ.com respondió {(int)http.StatusCode}.", http.StatusCode);
            }
            try
            {
                return XDocument.Parse(cuerpo);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new RespuestaDelServicioException(
                    $"QRZ.com no devolvió un XML válido: {ex.Message}");
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>QRZ marca con <c>1</c> o <c>0</c> si la estacion usa LoTW o eQSL.</summary>
    private static bool? Marca(string? texto) => texto?.Trim() switch
    {
        "1" or "Y" or "y" => true,
        "0" or "N" or "n" => false,
        _ => null,
    };

    private static string? Juntar(string? nombre, string? apellido)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return apellido;
        if (string.IsNullOrWhiteSpace(apellido)) return nombre;
        return $"{nombre} {apellido}";
    }
}
