using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
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
/// <para>
/// <b>Un rechazo de credenciales se recuerda.</b> Si QRZ contesta «Username/password
/// incorrect» no se vuelve a pedir sesion hasta que cambien el usuario o la contrasena, y si
/// anade «blocked until …» no se pide hasta pasada esa hora. Sin esto cada indicativo
/// tecleado era otro intento con la misma contrasena mala, hasta que QRZ bloqueaba la cuenta
/// (visto en los registros de octubre de 2026: decenas de rechazos seguidos y de bloqueos).
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
    private readonly Func<DateTimeOffset> _ahora;
    private string? _clave;

    // Lo que QRZ ha rechazado: la huella del usuario y la contrasena (no la contrasena) y, si
    // lo dijo, hasta cuando dura el bloqueo. Solo se tocan con el cerrojo de sesion cogido.
    private string? _huellaRechazada;
    private DateTimeOffset? _bloqueadoHasta;

    // Lo que QRZ dijo al abrir la ultima sesion (suscripcion parcial…), para «Probar».
    private string? _avisoDeSesion;

    /// <summary>Crea la consulta.</summary>
    /// <param name="fabrica">Fabrica de clientes HTTP.</param>
    /// <param name="credenciales">Almacen de secretos.</param>
    /// <param name="opciones">Ajustes de QRZ.com.</param>
    /// <param name="reintentos">Politica de reintentos.</param>
    /// <param name="log">Registro de trazas.</param>
    /// <param name="ahora">Reloj, sustituible en las pruebas.</param>
    public ConsultaQrzCom(
        IHttpClientFactory fabrica,
        IAlmacenDeCredenciales credenciales,
        OpcionesQrz opciones,
        PoliticaDeReintentos? reintentos = null,
        ILogger<ConsultaQrzCom>? log = null,
        Func<DateTimeOffset>? ahora = null)
    {
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
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
            throw new InvalidOperationException(Textos.F("Servicios.NoConfigurado", "QRZ.com"));
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

    /// <summary>
    /// Prueba el usuario y la contrasena abriendo una sesion nueva, sin consultar a nadie.
    /// </summary>
    /// <remarks>
    /// Respeta el freno de los rechazos: con el mismo usuario y la misma contrasena que QRZ ya
    /// rechazo no se sale a la red (cada intento fallido acerca el bloqueo de la cuenta), y
    /// con la cuenta bloqueada no se pide hasta que pase la hora. En cuanto cambia cualquiera
    /// de los dos datos, se prueba de verdad.
    /// </remarks>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<PruebaDeLaFichaQrz> ProbarAsync(CancellationToken ct = default)
    {
        if (!EstaDisponible)
        {
            return new PruebaDeLaFichaQrz(VeredictoDeQrz.SinDato, Textos.T("Servicios.Qrz.Ficha.SinDatos"));
        }

        try
        {
            await ObtenerClaveAsync(renovar: true, ct).ConfigureAwait(false);
            return new PruebaDeLaFichaQrz(
                VeredictoDeQrz.Correcto, Textos.F("Servicios.Qrz.Ficha.Correcta", _opciones.Usuario))
            {
                Aviso = _avisoDeSesion,
            };
        }
        catch (SesionRechazadaException ex)
        {
            return new PruebaDeLaFichaQrz(
                ex.BloqueadoHasta is null ? VeredictoDeQrz.ClaveRechazada : VeredictoDeQrz.Bloqueada, ex.Message)
            {
                BloqueadaHasta = ex.BloqueadoHasta,
            };
        }
        catch (ServicioNoDisponibleException ex)
        {
            return new PruebaDeLaFichaQrz(VeredictoDeQrz.SinRed, Textos.F("Servicios.Qrz.Ficha.SinRed", ex.Message));
        }
        catch (RespuestaDelServicioException ex)
        {
            return new PruebaDeLaFichaQrz(VeredictoDeQrz.RespuestaNoEntendida, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new PruebaDeLaFichaQrz(VeredictoDeQrz.SinDato, ex.Message);
        }
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
        return await PedirAsync(consulta, Textos.F("Servicios.ConsultarIndicativo", indicativo.Valor, "QRZ.com"), ct)
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
                ?? throw new InvalidOperationException(Textos.F("Servicios.SinContrasena", "QRZ.com"));

            var huella = Huella(_opciones.Usuario, contrasena);
            NoInsistirSiYaLoRechazaron(huella);

            var consulta =
                $"username={Uri.EscapeDataString(_opciones.Usuario)}"
                + $";password={Uri.EscapeDataString(contrasena)}"
                + $";agent={Uri.EscapeDataString(_opciones.Agente)}";

            var documento = await PedirAsync(consulta, Textos.F("Servicios.AbrirSesion", "QRZ.com"), ct).ConfigureAwait(false);
            var sesion = LecturaXml.Hijo(documento.Root, "Session");

            var clave = LecturaXml.Texto(sesion, "Key");
            if (clave is null)
            {
                var error = LecturaXml.Texto(sesion, "Error") ?? Textos.T("Servicios.SinDetalle");
                if (EsRechazoDeCredenciales(error)) throw ApuntarRechazo(huella, error);
                throw new RespuestaDelServicioException(Textos.F("Servicios.SinSesion", "QRZ.com", error));
            }

            _huellaRechazada = null;
            _bloqueadoHasta = null;

            // El aviso de suscripcion no impide consultar, pero el operador debe verlo.
            _avisoDeSesion = LecturaXml.Texto(sesion, "Message");
            if (_avisoDeSesion is { } mensaje)
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

    /// <summary>Indica si el error de sesion es un rechazo de usuario o contrasena, o un bloqueo.</summary>
    /// <param name="error">Texto del elemento <c>Error</c> de la sesion.</param>
    public static bool EsRechazoDeCredenciales(string? error) =>
        error is not null
        && (error.Contains("incorrect", StringComparison.OrdinalIgnoreCase)
            || error.Contains("blocked", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Saca la hora de «blocked until 2026-10-04 19:20:08 UTC». Nulo si el error no la trae.
    /// </summary>
    /// <param name="error">Texto del elemento <c>Error</c> de la sesion.</param>
    public static DateTimeOffset? BloqueadoHasta(string? error)
    {
        if (error is null) return null;
        var hora = Regex.Match(
            error, @"blocked until\s+(\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2})", RegexOptions.IgnoreCase);
        return hora.Success
            && DateTimeOffset.TryParseExact(
                hora.Groups[1].Value.Replace('T', ' '),
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var hasta)
            ? hasta
            : null;
    }

    /// <summary>
    /// Corta antes de llamar a QRZ si ya rechazo estas credenciales o la cuenta sigue bloqueada.
    /// </summary>
    private void NoInsistirSiYaLoRechazaron(string huella)
    {
        if (_bloqueadoHasta is { } hasta)
        {
            // Mientras dure el bloqueo no se pide ni con otra contrasena: QRZ contesta lo
            // mismo y cada intento puede alargarlo.
            if (_ahora() < hasta) throw new SesionRechazadaException(TextoDeBloqueo(hasta), hasta);

            // Pasada la hora se deja UN intento: no hay forma de saber si la contrasena era
            // mala o si solo estaba bloqueada la cuenta. Si vuelve a decir «incorrect» se
            // queda apuntada otra vez y ya no se insiste.
            _bloqueadoHasta = null;
            _huellaRechazada = null;
        }

        if (_huellaRechazada == huella)
        {
            throw new SesionRechazadaException(Textos.T("Servicios.Qrz.CredencialesRechazadas"));
        }
    }

    /// <summary>Apunta el rechazo, avisa una vez en el registro y prepara la excepcion.</summary>
    private SesionRechazadaException ApuntarRechazo(string huella, string error)
    {
        _clave = null;
        _huellaRechazada = huella;
        _bloqueadoHasta = BloqueadoHasta(error);

        // Dice «blocked» pero no se entiende la hora: una hora de margen antes de reintentar.
        if (_bloqueadoHasta is null && error.Contains("blocked", StringComparison.OrdinalIgnoreCase))
        {
            _bloqueadoHasta = _ahora().AddHours(1);
        }

        if (_bloqueadoHasta is { } hasta)
        {
            _log.LogWarning(
                "QRZ.com tiene la cuenta bloqueada hasta {Hasta:u} («{Error}»). No se vuelve a pedir sesión hasta entonces.",
                hasta, error);
            return new SesionRechazadaException(TextoDeBloqueo(hasta), hasta);
        }

        _log.LogWarning(
            "QRZ.com ha rechazado el usuario o la contraseña («{Error}»). No se vuelve a pedir sesión hasta que cambien.",
            error);
        return new SesionRechazadaException(Textos.T("Servicios.Qrz.CredencialesRechazadas"));
    }

    private static string TextoDeBloqueo(DateTimeOffset hasta) =>
        Textos.F(
            "Servicios.Qrz.Bloqueado",
            hasta.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));

    /// <summary>
    /// Huella del usuario y la contrasena: basta para saber si han cambiado sin tener la
    /// contrasena rechazada guardada en claro.
    /// </summary>
    private static string Huella(string usuario, string contrasena) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(usuario + "\n" + contrasena)));

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
                    Textos.F("Servicios.Respondio", "QRZ.com", (int)http.StatusCode), http.StatusCode);
            }
            try
            {
                return XDocument.Parse(cuerpo);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new RespuestaDelServicioException(
                    Textos.F("Servicios.XmlNoValido", "QRZ.com", ex.Message));
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
