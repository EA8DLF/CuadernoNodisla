using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Adif;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Eqsl;

/// <summary>
/// eQSL.cc: subida de contactos y descarga del buzon de entrada.
/// </summary>
/// <remarks>
/// <para>
/// <b>eQSL rechaza el registro entero si ve un campo que no conoce</b>, asi que la subida se
/// hace siempre contra la lista blanca de <see cref="CamposAdmitidos"/> y nunca volcando el
/// contacto completo. Es el fallo mas habitual al integrarse con eQSL y el mas dificil de
/// diagnosticar, porque el servicio responde con un aviso por registro y no con un error.
/// </para>
/// <para>
/// <b>El AG (Authenticity Guaranteed)</b> es la unica marca de eQSL que distingue una
/// confirmacion verificada de una simple tarjeta electronica: solo la tienen los operadores que
/// han demostrado su licencia. Los diplomas que exigen verificacion solo aceptan esas, asi que
/// el campo <c>APP_EQSL_AG</c> del buzon se traduce a
/// <see cref="EstadoDeConfirmacion.Verificado"/> y su ausencia, a
/// <see cref="EstadoDeConfirmacion.Confirmado"/>.
/// </para>
/// <para>
/// La descarga son dos pasos: se pide el buzon, eQSL construye un fichero y devuelve una
/// pagina con el enlace, y hay que seguirlo. Los ficheros se borran a las pocas horas.
/// </para>
/// </remarks>
public sealed partial class ServicioEqsl : IServicioQsl
{
    private readonly IHttpClientFactory _fabrica;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly PoliticaDeReintentos _reintentos;
    private readonly OpcionesEqsl _opciones;
    private readonly ILogger _log;

    /// <summary>
    /// Campos que eQSL acepta. Cualquier otro hace que rechace el registro.
    /// </summary>
    public static IReadOnlyList<string> CamposAdmitidos { get; } =
    [
        "CALL", "QSO_DATE", "TIME_ON", "TIME_OFF", "BAND", "BAND_RX", "FREQ",
        "MODE", "SUBMODE", "RST_SENT", "PROP_MODE", "SAT_NAME", "SAT_MODE", "QSLMSG",
    ];

    /// <summary>Campos del buzon que se conservan para ensenarselos al operador.</summary>
    public static IReadOnlyList<string> CamposDeDetalle { get; } =
        ["APP_EQSL_AG", "GRIDSQUARE", "QTH", "NAME", "RST_SENT", "QSLMSG", "QSL_SENT", "EQSL_QSLRDATE"];

    /// <summary>Crea el servicio.</summary>
    /// <param name="fabrica">Fabrica de clientes HTTP.</param>
    /// <param name="credenciales">Almacen de secretos.</param>
    /// <param name="opciones">Ajustes de eQSL.</param>
    /// <param name="reintentos">Politica de reintentos.</param>
    /// <param name="log">Registro de trazas.</param>
    public ServicioEqsl(
        IHttpClientFactory fabrica,
        IAlmacenDeCredenciales credenciales,
        OpcionesEqsl opciones,
        PoliticaDeReintentos? reintentos = null,
        ILogger<ServicioEqsl>? log = null)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _reintentos = reintentos ?? new PoliticaDeReintentos();
        _log = log ?? NullLogger<ServicioEqsl>.Instance;
    }

    /// <inheritdoc />
    public MedioDeConfirmacion Medio => MedioDeConfirmacion.Eqsl;

    /// <inheritdoc />
    public string Nombre => "eQSL.cc";

    /// <inheritdoc />
    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(_opciones.Usuario)
        && _credenciales.Existe(ClavesDeCredencial.EqslContrasena);

    /// <inheritdoc />
    public bool PuedeSubir => EstaConfigurado;

    /// <inheritdoc />
    public bool PuedeDescargar => EstaConfigurado;

    /// <inheritdoc />
    public async Task<bool> ComprobarCredencialesAsync(CancellationToken ct = default)
    {
        if (!EstaConfigurado) return false;
        try
        {
            // Se pide el buzon de una fecha imposible: valida la cuenta sin traerse nada.
            var pagina = await PedirBuzonAsync(
                new DateTimeOffset(2999, 1, 1, 0, 0, 0, TimeSpan.Zero), ct).ConfigureAwait(false);
            return !pagina.Contains("Error:", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is RespuestaDelServicioException or ServicioNoDisponibleException)
        {
            _log.LogWarning("No se pudieron validar las credenciales de eQSL: {Motivo}", ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<ResultadoDeSubida> SubirAsync(
        IReadOnlyList<Qso> qsos,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qsos);
        var reloj = Stopwatch.StartNew();
        if (qsos.Count == 0) return new ResultadoDeSubida(0, 0, SinMotivos(), reloj.Elapsed);

        Avisar(progreso, 0, qsos.Count, $"Enviando {qsos.Count} contactos a eQSL…");
        var contrasena = LeerContrasena();
        var registros = new List<IReadOnlyDictionary<string, string>>(qsos.Count);
        foreach (var qso in qsos)
        {
            var campos = new Dictionary<string, string>(
                ConversorDeQso.Proyectar(qso, CamposAdmitidos), StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_opciones.ApodoDeEstacion))
            {
                campos["APP_EQSL_QTH_NICKNAME"] = _opciones.ApodoDeEstacion;
            }
            registros.Add(campos);
        }

        var adif = AdifLigero.EscribirRegistros(
            registros,
            new Dictionary<string, string>
            {
                ["ADIF_VER"] = "3.1.5",
                ["PROGRAMID"] = "Cuaderno NODISLA",
            });

        var respuesta = await _reintentos.EjecutarAsync("subir a eQSL", async testigo =>
        {
            var cliente = _fabrica.CreateClient(NombresDeClienteHttp.Eqsl);
            using var formulario = new MultipartFormDataContent();
            formulario.Add(new StringContent(_opciones.Usuario), "EQSL_USER");
            formulario.Add(new StringContent(contrasena), "EQSL_PSWD");

            var fichero = new StringContent(adif, Encoding.UTF8, "text/plain");
            formulario.Add(fichero, "Filename", "cuaderno.adi");

            using var http = await cliente.PostAsync(_opciones.UrlDeSubida, formulario, testigo)
                .ConfigureAwait(false);
            var cuerpo = await http.Content.ReadAsStringAsync(testigo).ConfigureAwait(false);
            if (!http.IsSuccessStatusCode)
            {
                throw new RespuestaDelServicioException(
                    $"eQSL respondió {(int)http.StatusCode}.", http.StatusCode);
            }
            return cuerpo;
        }, ct).ConfigureAwait(false);

        reloj.Stop();
        var resultado = InterpretarSubida(respuesta, qsos, reloj.Elapsed, _log);
        Avisar(progreso, resultado.Enviados, qsos.Count,
            $"eQSL aceptó {resultado.Enviados} de {qsos.Count} contactos.");
        return resultado;
    }

    /// <summary>
    /// Traduce la respuesta de <c>ImportADIF.cfm</c>, que es texto con marcas
    /// <c>Result:</c>, <c>Warning:</c> y <c>Error:</c>.
    /// </summary>
    /// <param name="respuesta">Cuerpo de la respuesta de eQSL.</param>
    /// <param name="qsos">Contactos que se enviaron, en el mismo orden.</param>
    /// <param name="duracion">Lo que tardo la llamada.</param>
    /// <param name="log">Registro de trazas.</param>
    public static ResultadoDeSubida InterpretarSubida(
        string respuesta, IReadOnlyList<Qso> qsos, TimeSpan duracion, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentNullException.ThrowIfNull(qsos);
        log ??= NullLogger.Instance;

        var motivos = new Dictionary<string, string>(StringComparer.Ordinal);

        var error = RegexError().Match(respuesta);
        if (error.Success)
        {
            // Un «Error:» es fatal: no entro ni un solo registro.
            var texto = error.Groups[1].Value.Trim();
            log.LogWarning("eQSL rechazó la subida entera: {Motivo}", texto);
            foreach (var qso in qsos) motivos[qso.ClaveNatural] = texto;
            return new ResultadoDeSubida(0, qsos.Count, motivos, duracion);
        }

        var aceptados = 0;
        var resultado = RegexResultado().Match(respuesta);
        if (resultado.Success
            && int.TryParse(resultado.Groups[1].Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var cuantos))
        {
            aceptados = cuantos;
        }

        // Los avisos de eQSL son por registro, pero solo traen la fecha: no hay forma honrada de
        // devolverlos a su contacto. Van en Avisos, no en Motivos con una clave inventada.
        var avisos = new List<string>();
        foreach (Match aviso in RegexAviso().Matches(respuesta))
        {
            var texto = aviso.Groups[1].Value.Trim();
            avisos.Add(texto);
            log.LogWarning("eQSL avisó de un registro: {Aviso}", texto);
        }

        var rechazados = Math.Max(0, qsos.Count - aceptados);
        return new ResultadoDeSubida(aceptados, rechazados, motivos, duracion) { Avisos = avisos };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfirmacionDescargada>> DescargarAsync(
        DateTimeOffset? desdeUtc,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default)
    {
        Avisar(progreso, 0, null, "Pidiendo a eQSL que prepare el buzón…");
        var pagina = await PedirBuzonAsync(desdeUtc, ct).ConfigureAwait(false);
        var enlace = ExtraerEnlace(pagina, _opciones.RaizDelSitio)
            ?? throw new RespuestaDelServicioException(
                "eQSL no devolvió el enlace al fichero del buzón. Suele significar que el "
                + "usuario o la contraseña no son correctos, o que no había nada que descargar.");

        _log.LogInformation("Descargando el buzón de eQSL.");
        Avisar(progreso, 0, null, "Descargando el buzón de eQSL…");
        var adif = await _reintentos.EjecutarAsync("descargar el buzón de eQSL", async testigo =>
        {
            var cliente = _fabrica.CreateClient(NombresDeClienteHttp.Eqsl);
            using var http = await cliente.GetAsync(enlace, testigo).ConfigureAwait(false);
            if (!http.IsSuccessStatusCode)
            {
                throw new RespuestaDelServicioException(
                    $"eQSL respondió {(int)http.StatusCode} al pedir el fichero.", http.StatusCode);
            }
            return await http.Content.ReadAsStringAsync(testigo).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        var confirmaciones = Interpretar(adif);
        Avisar(progreso, confirmaciones.Count, confirmaciones.Count,
            $"eQSL devolvió {confirmaciones.Count} confirmaciones.");
        return confirmaciones;
    }

    private void Avisar(
        IProgress<ProgresoDeSincronizacion>? progreso, int hecho, int? total, string mensaje) =>
        progreso?.Report(new ProgresoDeSincronizacion(Nombre, hecho, total, mensaje));

    /// <summary>Traduce el ADIF del buzon de eQSL a confirmaciones.</summary>
    /// <param name="buzon">Contenido del fichero descargado.</param>
    public static IReadOnlyList<ConfirmacionDescargada> Interpretar(string buzon)
    {
        var confirmaciones = new List<ConfirmacionDescargada>();
        foreach (var registro in AdifLigero.LeerRegistros(buzon))
        {
            if (!LectorDeConfirmaciones.TryLeerClave(registro, out var clave)) continue;

            confirmaciones.Add(new ConfirmacionDescargada(
                clave.Call,
                clave.Band,
                clave.Mode,
                clave.InicioUtc,
                MedioDeConfirmacion.Eqsl,
                LectorDeConfirmaciones.FechaDeConfirmacion(registro, "QSLRDATE")
                    ?? LectorDeConfirmaciones.FechaDeConfirmacion(registro, "EQSL_QSLRDATE"),
                // Solo el AG cuenta como verificada; una eQSL sin AG es una tarjeta bonita.
                Verificada: LectorDeConfirmaciones.EsSi(registro, "APP_EQSL_AG"))
            {
                CamposExtra = LectorDeConfirmaciones.CamposExtra(registro, CamposDeDetalle),
            });
        }
        return confirmaciones;
    }

    /// <summary>
    /// Saca de la pagina de respuesta el enlace al fichero ADI, resuelto contra la raiz.
    /// </summary>
    /// <param name="pagina">Pagina HTML devuelta por <c>DownloadInBox.cfm</c>.</param>
    /// <param name="raiz">Raiz del sitio contra la que resolver el enlace relativo.</param>
    public static Uri? ExtraerEnlace(string pagina, Uri raiz)
    {
        ArgumentNullException.ThrowIfNull(pagina);
        ArgumentNullException.ThrowIfNull(raiz);

        foreach (Match enlace in RegexEnlace().Matches(pagina))
        {
            var destino = enlace.Groups[1].Value.Trim();
            if (!destino.EndsWith(".adi", StringComparison.OrdinalIgnoreCase)) continue;
            return Uri.TryCreate(raiz, destino, out var absoluta) ? absoluta : null;
        }
        return null;
    }

    private async Task<string> PedirBuzonAsync(DateTimeOffset? desdeUtc, CancellationToken ct)
    {
        var contrasena = LeerContrasena();
        var consulta = new StringBuilder();
        consulta.Append("UserName=").Append(Uri.EscapeDataString(_opciones.Usuario));
        consulta.Append("&Password=").Append(Uri.EscapeDataString(contrasena));
        if (!string.IsNullOrWhiteSpace(_opciones.ApodoDeEstacion))
        {
            consulta.Append("&QTHNickname=").Append(Uri.EscapeDataString(_opciones.ApodoDeEstacion));
        }
        if (_opciones.SoloConfirmados) consulta.Append("&ConfirmedOnly=1");
        if (desdeUtc is { } desde)
        {
            // RcvdSince filtra por cuando entro en el buzon, en formato AAAAMMDDHHMM.
            consulta.Append("&RcvdSince=")
                .Append(desde.UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture));
        }

        var url = new UriBuilder(_opciones.UrlDelBuzon) { Query = consulta.ToString() }.Uri;
        _log.LogInformation("Pidiendo el buzón de eQSL a {Servidor}.", _opciones.UrlDelBuzon);

        return await _reintentos.EjecutarAsync("pedir el buzón de eQSL", async testigo =>
        {
            var cliente = _fabrica.CreateClient(NombresDeClienteHttp.Eqsl);
            using var http = await cliente.GetAsync(url, testigo).ConfigureAwait(false);
            var cuerpo = await http.Content.ReadAsStringAsync(testigo).ConfigureAwait(false);
            if (!http.IsSuccessStatusCode)
            {
                throw new RespuestaDelServicioException(
                    $"eQSL respondió {(int)http.StatusCode}.", http.StatusCode);
            }
            return cuerpo;
        }, ct).ConfigureAwait(false);
    }

    private string LeerContrasena() =>
        _credenciales.Leer(ClavesDeCredencial.EqslContrasena)
        ?? throw new InvalidOperationException(
            "No hay contraseña de eQSL guardada. Configúrela en los ajustes del programa.");

    private static IReadOnlyDictionary<string, string> SinMotivos() =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    [GeneratedRegex(@"Result:\s*(\d+)\s+out of\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RegexResultado();

    [GeneratedRegex(@"Warning:\s*([^\r\n<]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RegexAviso();

    [GeneratedRegex(@"Error:\s*([^\r\n<]+)", RegexOptions.IgnoreCase)]
    private static partial Regex RegexError();

    [GeneratedRegex("""<a\s[^>]*href\s*=\s*["']?([^"'\s>]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RegexEnlace();
}
