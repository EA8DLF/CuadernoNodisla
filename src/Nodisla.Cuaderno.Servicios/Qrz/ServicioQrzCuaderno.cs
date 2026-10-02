using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Servicios.Adif;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Qrz;

/// <summary>
/// El cuaderno en linea de QRZ.com: subida y descarga de contactos.
/// </summary>
/// <remarks>
/// <para>
/// No confundir con <see cref="ConsultaQrzCom"/>: son dos servicios de la misma casa con
/// credenciales distintas. Este va con la clave de cuaderno que se genera en la web de QRZ, y
/// esa clave da acceso de lectura y escritura al cuaderno entero, asi que se guarda cifrada
/// como cualquier otra contrasena.
/// </para>
/// <para>
/// La API responde en <c>clave=valor</c> separados por ampersands y con los valores
/// codificados como en una direccion, no en XML ni en JSON. <c>INSERT</c> admite un contacto
/// por llamada, de modo que subir un lote son tantas llamadas como contactos.
/// </para>
/// </remarks>
public sealed class ServicioQrzCuaderno : IServicioQsl
{
    private readonly IHttpClientFactory _fabrica;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly PoliticaDeReintentos _reintentos;
    private readonly OpcionesQrz _opciones;
    private readonly ILogger _log;

    /// <summary>Campos que se le mandan al cuaderno de QRZ.com.</summary>
    public static IReadOnlyList<string> CamposAdmitidos { get; } =
    [
        "CALL", "QSO_DATE", "TIME_ON", "QSO_DATE_OFF", "TIME_OFF",
        "BAND", "BAND_RX", "FREQ", "FREQ_RX", "MODE", "SUBMODE",
        "RST_SENT", "RST_RCVD", "NAME", "QTH", "GRIDSQUARE", "MY_GRIDSQUARE",
        "STATION_CALLSIGN", "OPERATOR", "PROP_MODE", "SAT_NAME", "SAT_MODE",
        "TX_PWR", "COMMENT", "DXCC", "CQZ", "ITUZ", "STATE", "CNTY", "COUNTRY",
    ];

    /// <summary>Campos de la descarga que se conservan para ensenarselos al operador.</summary>
    public static IReadOnlyList<string> CamposDeDetalle { get; } =
        ["APP_QRZLOG_LOGID", "APP_QRZLOG_STATUS", "GRIDSQUARE", "NAME", "QTH", "DXCC", "COUNTRY"];

    /// <summary>Crea el servicio.</summary>
    /// <param name="fabrica">Fabrica de clientes HTTP.</param>
    /// <param name="credenciales">Almacen de secretos.</param>
    /// <param name="opciones">Ajustes de QRZ.com.</param>
    /// <param name="reintentos">Politica de reintentos.</param>
    /// <param name="log">Registro de trazas.</param>
    public ServicioQrzCuaderno(
        IHttpClientFactory fabrica,
        IAlmacenDeCredenciales credenciales,
        OpcionesQrz opciones,
        PoliticaDeReintentos? reintentos = null,
        ILogger<ServicioQrzCuaderno>? log = null)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _reintentos = reintentos ?? new PoliticaDeReintentos();
        _log = log ?? NullLogger<ServicioQrzCuaderno>.Instance;
    }

    /// <inheritdoc />
    public MedioDeConfirmacion Medio => MedioDeConfirmacion.QrzCom;

    /// <inheritdoc />
    public string Nombre => Textos.T("Servicios.Qrz.NombreDelCuaderno");

    /// <inheritdoc />
    public bool EstaConfigurado => _credenciales.Existe(ClavesDeCredencial.QrzClaveDeCuaderno);

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
            var respuesta = await LlamarAsync(
                new Dictionary<string, string> { ["ACTION"] = "STATUS" },
                Textos.T("Servicios.Qrz.ComprobarCuaderno"), ct).ConfigureAwait(false);
            return EsCorrecta(respuesta);
        }
        catch (Exception ex) when (ex is RespuestaDelServicioException or ServicioNoDisponibleException)
        {
            _log.LogWarning("No se pudo validar la clave del cuaderno de QRZ.com: {Motivo}", ex.Message);
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

        var enviados = 0;
        var hechos = 0;
        var motivos = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var qso in qsos)
        {
            ct.ThrowIfCancellationRequested();

            // QRZ solo admite un contacto por llamada, asi que aqui el progreso es de verdad
            // util: en un lote grande son cientos de peticiones seguidas.
            Avisar(progreso, hechos, qsos.Count, Textos.F("Servicios.Qrz.SubiendoContacto", qso.Call.Valor));

            var registro = ConversorDeQso.Proyectar(qso, CamposAdmitidos);
            var adif = AdifLigero.EscribirRegistros([registro]);

            try
            {
                var respuesta = await LlamarAsync(
                    new Dictionary<string, string>
                    {
                        ["ACTION"] = "INSERT",

                        // REPLACE: si el contacto ya estaba (reenvio tras modificarlo con F2, o
                        // un reintento cuya respuesta se perdio) se sustituye en vez de dar
                        // «duplicate». Si no estaba, se inserta igual.
                        ["OPTION"] = "REPLACE",
                        ["ADIF"] = adif,
                    },
                    Textos.F("Servicios.Qrz.SubirContacto", qso.Call.Valor), ct).ConfigureAwait(false);

                if (EsCorrecta(respuesta)) enviados++;
                else motivos[qso.ClaveNatural] = Motivo(respuesta);
            }
            catch (Exception ex) when (ex is RespuestaDelServicioException or ServicioNoDisponibleException)
            {
                motivos[qso.ClaveNatural] = ex.Message;
            }

            hechos++;
        }

        Avisar(progreso, hechos, qsos.Count, Textos.F("Servicios.Qrz.Acepto", enviados, qsos.Count));
        reloj.Stop();
        var rechazados = qsos.Count - enviados;
        if (rechazados > 0)
        {
            // El motivo que da QRZ (REASON), para poder saber por que; nunca la clave.
            _log.LogWarning(
                "QRZ.com rechazó {Rechazados} de {Total} contactos: {Motivos}",
                rechazados, qsos.Count, string.Join(" | ", motivos.Values.Distinct()));
        }
        return new ResultadoDeSubida(enviados, rechazados, motivos, reloj.Elapsed);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfirmacionDescargada>> DescargarAsync(
        DateTimeOffset? desdeUtc,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default)
    {
        var confirmaciones = new List<ConfirmacionDescargada>();
        var ultimoId = 0L;
        var pagina = 0;

        // FETCH pagina con MAX y AFTERLOGID: se pide de identificador en adelante hasta que
        // devuelve menos de lo pedido.
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            pagina++;
            Avisar(progreso, confirmaciones.Count, null,
                Textos.F("Servicios.Qrz.Descargando", pagina));

            var opciones = new List<string>
            {
                "TYPE:ADIF",
                $"MAX:{_opciones.TamanoDePagina.ToString(CultureInfo.InvariantCulture)}",
            };
            if (ultimoId > 0) opciones.Add($"AFTERLOGID:{ultimoId.ToString(CultureInfo.InvariantCulture)}");
            if (desdeUtc is { } desde)
            {
                opciones.Add($"MODSINCE:{desde.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
            }

            var respuesta = await LlamarAsync(
                new Dictionary<string, string>
                {
                    ["ACTION"] = "FETCH",
                    ["OPTION"] = string.Join(',', opciones),
                },
                Textos.T("Servicios.Qrz.DescargarCuaderno"), ct).ConfigureAwait(false);

            if (!EsCorrecta(respuesta))
            {
                throw new RespuestaDelServicioException(
                    Textos.F("Servicios.Qrz.NoDevolvioCuaderno", Motivo(respuesta)));
            }

            if (!respuesta.TryGetValue("ADIF", out var adif) || string.IsNullOrWhiteSpace(adif)) break;

            var registros = AdifLigero.LeerRegistros(adif);
            if (registros.Count == 0) break;

            foreach (var registro in registros)
            {
                if (LectorDeConfirmaciones.Campo(registro, "APP_QRZLOG_LOGID") is { } id
                    && long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero)
                    && numero > ultimoId)
                {
                    ultimoId = numero;
                }

                if (!EstaConfirmado(registro)) continue;
                if (!LectorDeConfirmaciones.TryLeerClave(registro, out var clave)) continue;

                confirmaciones.Add(new ConfirmacionDescargada(
                    clave.Call,
                    clave.Band,
                    clave.Mode,
                    clave.InicioUtc,
                    MedioDeConfirmacion.QrzCom,
                    LectorDeConfirmaciones.FechaDeConfirmacion(registro, "QSLRDATE"),
                    // QRZ.com no firma nada: da por confirmado el contacto cuando las dos
                    // partes lo tienen en su cuaderno. Es una confirmacion, no una verificacion.
                    Verificada: false)
                {
                    CamposExtra = LectorDeConfirmaciones.CamposExtra(registro, CamposDeDetalle),
                });
            }

            if (registros.Count < _opciones.TamanoDePagina) break;
        }

        _log.LogInformation(
            "QRZ.com devolvió {Cuantas} confirmaciones.", confirmaciones.Count);
        Avisar(progreso, confirmaciones.Count, confirmaciones.Count,
            Textos.F("Servicios.Qrz.Devolvio", confirmaciones.Count));
        return confirmaciones;
    }

    private void Avisar(
        IProgress<ProgresoDeSincronizacion>? progreso, int hecho, int? total, string mensaje) =>
        progreso?.Report(new ProgresoDeSincronizacion(Nombre, hecho, total, mensaje));

    /// <summary>
    /// Indica si el registro descargado esta confirmado. QRZ marca la confirmacion con
    /// <c>APP_QRZLOG_STATUS</c> a <c>C</c>, y algunos volcados traen ademas <c>QSL_RCVD</c>.
    /// </summary>
    /// <param name="registro">Campos del registro descargado.</param>
    public static bool EstaConfirmado(IReadOnlyDictionary<string, string> registro)
    {
        ArgumentNullException.ThrowIfNull(registro);
        if (LectorDeConfirmaciones.EsSi(registro, "QSL_RCVD")) return true;
        var estado = LectorDeConfirmaciones.Campo(registro, "APP_QRZLOG_STATUS");
        return estado is not null && estado.StartsWith("C", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Interpreta el cuerpo de una respuesta de la API, que llega como
    /// <c>clave=valor</c> separados por ampersands y con los valores codificados.
    /// </summary>
    /// <param name="cuerpo">Cuerpo de la respuesta.</param>
    public static IReadOnlyDictionary<string, string> Interpretar(string cuerpo)
    {
        var campos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(cuerpo)) return campos;

        foreach (var trozo in cuerpo.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var corte = trozo.IndexOf('=');
            if (corte < 0) { campos[trozo.Trim()] = string.Empty; continue; }
            var clave = trozo[..corte].Trim();
            var valor = trozo[(corte + 1)..];
            campos[clave] = Uri.UnescapeDataString(valor.Replace('+', ' '));
        }
        return campos;
    }

    private static bool EsCorrecta(IReadOnlyDictionary<string, string> respuesta) =>
        respuesta.TryGetValue("RESULT", out var resultado)
        && (resultado.Equals("OK", StringComparison.OrdinalIgnoreCase)
            || resultado.Equals("REPLACE", StringComparison.OrdinalIgnoreCase));

    private static string Motivo(IReadOnlyDictionary<string, string> respuesta)
    {
        if (respuesta.TryGetValue("REASON", out var motivo) && !string.IsNullOrWhiteSpace(motivo))
        {
            return motivo;
        }
        if (respuesta.TryGetValue("RESULT", out var resultado))
        {
            return resultado.Equals("AUTH", StringComparison.OrdinalIgnoreCase)
                ? Textos.T("Servicios.Qrz.ClaveNoAceptada")
                : Textos.F("Servicios.Qrz.RespondioTexto", resultado);
        }
        return Textos.T("Servicios.Qrz.NoSeEntiende");
    }

    private async Task<IReadOnlyDictionary<string, string>> LlamarAsync(
        IReadOnlyDictionary<string, string> parametros, string descripcion, CancellationToken ct)
    {
        var clave = _credenciales.Leer(ClavesDeCredencial.QrzClaveDeCuaderno)
            ?? throw new InvalidOperationException(Textos.T("Servicios.Qrz.SinClaveDeCuaderno"));

        var campos = new Dictionary<string, string>(parametros, StringComparer.OrdinalIgnoreCase)
        {
            ["KEY"] = clave,
        };

        return await _reintentos.EjecutarAsync(descripcion, async testigo =>
        {
            var cliente = _fabrica.CreateClient(NombresDeClienteHttp.Qrz);
            using var formulario = new FormUrlEncodedContent(campos);
            using var http = await cliente.PostAsync(_opciones.UrlDelCuaderno, formulario, testigo)
                .ConfigureAwait(false);
            var cuerpo = await http.Content.ReadAsStringAsync(testigo).ConfigureAwait(false);
            if (!http.IsSuccessStatusCode)
            {
                throw new RespuestaDelServicioException(
                    Textos.F("Servicios.Respondio", "QRZ.com", (int)http.StatusCode), http.StatusCode);
            }
            return Interpretar(cuerpo);
        }, ct).ConfigureAwait(false);
    }
}
