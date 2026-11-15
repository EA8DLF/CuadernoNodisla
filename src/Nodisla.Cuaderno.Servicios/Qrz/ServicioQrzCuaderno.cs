using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
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
/// <para>
/// Todo lo que aqui se da por sabido sale de la guia oficial,
/// https://www.qrz.com/docs/logbook/QRZLogbookAPI.html. Lo que la guia no dice (el texto exacto
/// de los <c>REASON</c>, los nombres de los campos de <c>STATUS</c>) se lee con manga ancha y
/// nunca se toma por cierto.
/// </para>
/// <para>
/// <b>Una clave rechazada se recuerda.</b> Si QRZ dice que la clave no vale o que no tiene
/// permiso, la subida se para y no se vuelve a llamar con esa misma clave hasta que cambie o
/// hasta que el operador pulse «Probar» y vaya bien. Insistir en silencio con una clave mala
/// no sube nada y solo sirve para que QRZ limite al programa.
/// </para>
/// </remarks>
public sealed class ServicioQrzCuaderno : IServicioQsl
{
    private readonly IHttpClientFactory _fabrica;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly PoliticaDeReintentos _reintentos;
    private readonly OpcionesQrz _opciones;
    private readonly ILogger _log;

    // Huella de la clave que QRZ ha rechazado (no la clave) y lo que dijo. Volatil: se lee
    // desde la cola y se escribe desde «Probar».
    private volatile ClaveParada? _parada;

    private sealed record ClaveParada(string Huella, VeredictoDeQrz Veredicto, string Mensaje);

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
    public async Task<bool> ComprobarCredencialesAsync(CancellationToken ct = default) =>
        (await ProbarAsync(ct).ConfigureAwait(false)).Veredicto == VeredictoDeQrz.Correcto;

    /// <summary>
    /// Prueba la clave con <c>STATUS</c>, que no sube ni baja nada, y dice en claro que pasa.
    /// </summary>
    /// <remarks>
    /// Siempre llama a QRZ, tambien con una clave que ya rechazo: es el operador quien lo pide,
    /// una vez por pulsacion, y puede haber contratado la suscripcion entre tanto. Si va bien
    /// quita el freno de la subida; si va mal lo deja puesto.
    /// </remarks>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<PruebaDelCuadernoQrz> ProbarAsync(CancellationToken ct = default)
    {
        var clave = LeerClave();
        if (clave is null)
        {
            return new PruebaDelCuadernoQrz(VeredictoDeQrz.SinDato, Textos.T("Servicios.Qrz.SinClaveDeCuaderno"));
        }

        IReadOnlyDictionary<string, string> respuesta;
        try
        {
            respuesta = await LlamarAsync(
                clave,
                new Dictionary<string, string> { ["ACTION"] = "STATUS" },
                Textos.T("Servicios.Qrz.ComprobarCuaderno"), ct).ConfigureAwait(false);
        }
        catch (ServicioNoDisponibleException ex)
        {
            _log.LogWarning("No se pudo probar la clave del cuaderno de QRZ.com: {Motivo}", ex.Message);
            return new PruebaDelCuadernoQrz(VeredictoDeQrz.SinRed, Textos.F("Servicios.Qrz.Prueba.SinRed", ex.Message));
        }
        catch (RespuestaDelServicioException ex)
        {
            _log.LogWarning("QRZ.com respondió mal al probar la clave del cuaderno: {Motivo}", ex.Message);
            return new PruebaDelCuadernoQrz(VeredictoDeQrz.RespuestaNoEntendida, ex.Message);
        }

        if (EsCorrecta(respuesta))
        {
            _parada = null;
            return LeerEstado(respuesta);
        }

        // STATUS, segun la guia: «FAIL if the Access Key is invalid». Todo lo que no sea un
        // AUTH (permisos) es, por tanto, la clave.
        var (veredicto, mensaje) = Clasificar(respuesta) is { } fallo
            ? fallo
            : (VeredictoDeQrz.ClaveRechazada, MensajeDeClaveRechazada(RazonDe(respuesta)));
        ApuntarParada(clave, veredicto, mensaje);
        return new PruebaDelCuadernoQrz(veredicto, mensaje);
    }

    /// <inheritdoc />
    public async Task<ResultadoDeSubida> SubirAsync(
        IReadOnlyList<Qso> qsos,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qsos);
        var reloj = Stopwatch.StartNew();

        var clave = LeerClave()
            ?? throw new InvalidOperationException(Textos.T("Servicios.Qrz.SinClaveDeCuaderno"));

        // Esta misma clave ya la rechazo QRZ: no se llama otra vez. Se contesta lo mismo que
        // entonces, sin red, para que la cola siga parada y el operador siga viendo por que.
        if (_parada is { } previa && previa.Huella == Huella(clave))
        {
            return Parado(qsos, [], previa.Mensaje, enviados: 0, reloj);
        }

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
            var adif = SoloElRegistro(AdifLigero.EscribirRegistros([registro]));
            var parametros = new Dictionary<string, string>
            {
                ["ACTION"] = "INSERT",
                ["ADIF"] = adif,
            };

            // REPLACE solo para lo que ya se habia subido y se ha corregido despues (F2). La
            // guia avisa de que REPLACE pisa un contacto CONFIRMADO con el que llega sin
            // confirmar, asi que no se usa a ciegas: un contacto que se sube por primera vez y
            // resulta que ya estaba en QRZ (un reintento cuya respuesta se perdio, o un libro
            // que ya se habia cargado por ADIF) vuelve como duplicado y se da por subido sin
            // tocar lo que QRZ tiene.
            if (YaSeHabiaSubido(qso)) parametros["OPTION"] = "REPLACE";

            try
            {
                var respuesta = await LlamarAsync(
                    clave, parametros, Textos.F("Servicios.Qrz.SubirContacto", qso.Call.Valor), ct).ConfigureAwait(false);

                if (EsCorrecta(respuesta) || EsDuplicado(respuesta))
                {
                    enviados++;
                }
                else if (Clasificar(respuesta) is { } fallo)
                {
                    // Clave o permisos: no es cosa de este contacto, es de todos. Se para aqui.
                    ApuntarParada(clave, fallo.Veredicto, fallo.Mensaje);
                    return Parado(qsos, motivos, fallo.Mensaje, enviados, reloj, desde: hechos);
                }
                else
                {
                    motivos[qso.ClaveNatural] = Motivo(respuesta);
                }
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
        var claveDeApi = LeerClave()
            ?? throw new InvalidOperationException(Textos.T("Servicios.Qrz.SinClaveDeCuaderno"));
        var siguienteId = 0L;
        var pagina = 0;

        // FETCH pagina con MAX y AFTERLOGID, como recomienda la guia: AFTERLOGID es «mayor o
        // igual», asi que cada pagina empieza en el mayor identificador visto MAS UNO. Sin el
        // mas uno, el ultimo contacto de cada pagina volvia a salir en la siguiente.
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            pagina++;
            var desdeDeEstaPagina = siguienteId;
            Avisar(progreso, confirmaciones.Count, null,
                Textos.F("Servicios.Qrz.Descargando", pagina));

            var opciones = new List<string>
            {
                "TYPE:ADIF",
                $"MAX:{_opciones.TamanoDePagina.ToString(CultureInfo.InvariantCulture)}",
            };
            opciones.Add($"AFTERLOGID:{siguienteId.ToString(CultureInfo.InvariantCulture)}");
            if (desdeUtc is { } desde)
            {
                opciones.Add($"MODSINCE:{desde.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
            }

            var respuesta = await LlamarAsync(
                claveDeApi,
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
                    && numero >= siguienteId)
                {
                    siguienteId = numero + 1;
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

            // Sin identificadores no hay por donde seguir: la pagina siguiente seria la misma.
            if (siguienteId == desdeDeEstaPagina) break;
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
    /// <c>clave=valor</c> separados por ampersands.
    /// </summary>
    /// <remarks>
    /// Solo se parte donde empieza otro <c>NOMBRE=</c>: el ADIF de una descarga puede traer
    /// sus signos de menor y mayor como entidades HTML (<c>&amp;lt;call:5&amp;gt;</c>), y
    /// partir por cada ampersand lo hacia trizas. Los valores se descodifican si vienen
    /// codificados como en una direccion; un ADIF en crudo se deja como llega.
    /// </remarks>
    /// <param name="cuerpo">Cuerpo de la respuesta.</param>
    public static IReadOnlyDictionary<string, string> Interpretar(string cuerpo)
    {
        var campos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(cuerpo)) return campos;

        foreach (var trozo in Regex.Split(cuerpo.Trim(), "&(?=[A-Za-z_][A-Za-z0-9_]*=)"))
        {
            if (trozo.Length == 0) continue;
            var corte = trozo.IndexOf('=');
            if (corte < 0) { campos[trozo.Trim()] = string.Empty; continue; }
            var clave = trozo[..corte].Trim();
            campos[clave] = Descodificar(trozo[(corte + 1)..]);
        }
        return campos;
    }

    private static string Descodificar(string valor)
    {
        // Un valor codificado como en una direccion no lleva ni espacios ni signos de menor.
        var codificado = valor.IndexOf('<') < 0 && valor.IndexOf(' ') < 0
            && (valor.Contains('%', StringComparison.Ordinal) || valor.Contains('+', StringComparison.Ordinal));
        if (codificado)
        {
            try
            {
                valor = Uri.UnescapeDataString(valor.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                // No era una codificacion valida: se deja como llego.
            }
        }

        return valor.Contains('&', StringComparison.Ordinal) && valor.Contains(';', StringComparison.Ordinal)
            ? WebUtility.HtmlDecode(valor)
            : valor;
    }

    /// <summary>
    /// Saca del <c>STATUS</c> lo que se le ensena al operador: de quien es el libro y cuantos
    /// contactos tiene.
    /// </summary>
    /// <remarks>
    /// La guia dice que <c>DATA</c> trae pares <c>nombre=valor</c> («Total QSO's in book,
    /// total confirmed… book owner, bookid, book name») pero no da los nombres. Se buscan los
    /// que se ven en la practica, dentro de <c>DATA</c> o sueltos, y lo que no aparezca se
    /// queda sin decir: la clave vale igual.
    /// </remarks>
    private static PruebaDelCuadernoQrz LeerEstado(IReadOnlyDictionary<string, string> respuesta)
    {
        var datos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (clave, valor) in respuesta) datos[clave] = valor;
        if (respuesta.TryGetValue("DATA", out var anidado) && anidado.Contains('=', StringComparison.Ordinal))
        {
            foreach (var (clave, valor) in Interpretar(anidado)) datos[clave] = valor;
        }

        string? Texto(params string[] nombres) => nombres
            .Select(n => datos.GetValueOrDefault(n))
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

        int? Numero(params string[] nombres) =>
            int.TryParse(Texto(nombres), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

        var indicativo = Texto("CALLSIGN", "CALL", "OWNER");
        var libro = Texto("BOOK_NAME", "BOOKNAME");
        var contactos = Numero("COUNT", "TOTAL", "QSO_COUNT");
        var confirmados = Numero("CONFIRMED", "CONFIRMED_COUNT");

        var mensaje = (indicativo, contactos) switch
        {
            ({ } i, { } c) => Textos.F("Servicios.Qrz.Prueba.CorrectaConLibro", i, c),
            ({ } i, null) => Textos.F("Servicios.Qrz.Prueba.CorrectaDe", i),
            (null, { } c) => Textos.F("Servicios.Qrz.Prueba.CorrectaConContactos", c),
            _ => Textos.T("Servicios.Qrz.Prueba.Correcta"),
        };

        return new PruebaDelCuadernoQrz(VeredictoDeQrz.Correcto, mensaje)
        {
            Indicativo = indicativo,
            Libro = libro,
            Contactos = contactos,
            Confirmados = confirmados,
        };
    }

    private static bool EsCorrecta(IReadOnlyDictionary<string, string> respuesta) =>
        respuesta.TryGetValue("RESULT", out var resultado)
        && (resultado.Equals("OK", StringComparison.OrdinalIgnoreCase)
            || resultado.Equals("REPLACE", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// QRZ no ha insertado el contacto porque ya lo tenia. No es un error: esta subido.
    /// </summary>
    private static bool EsDuplicado(IReadOnlyDictionary<string, string> respuesta) =>
        RazonDe(respuesta) is { } razon && razon.Contains("duplicate", StringComparison.OrdinalIgnoreCase);

    private static string? RazonDe(IReadOnlyDictionary<string, string> respuesta) =>
        respuesta.TryGetValue("REASON", out var motivo) && !string.IsNullOrWhiteSpace(motivo) ? motivo.Trim() : null;

    /// <summary>
    /// Dice si la respuesta es un fallo de la clave o de sus permisos —que para la subida
    /// entera— y con que palabras contarlo. Nulo si es otra cosa.
    /// </summary>
    private static (VeredictoDeQrz Veredicto, string Mensaje)? Clasificar(IReadOnlyDictionary<string, string> respuesta)
    {
        var razon = RazonDe(respuesta);
        var resultado = respuesta.GetValueOrDefault("RESULT") ?? string.Empty;

        // «AUTH when API Access lacked sufficient privileges for the operation»: la clave se
        // reconoce pero no puede hacer esto. Lo normal es que falte la suscripcion.
        if (resultado.Equals("AUTH", StringComparison.OrdinalIgnoreCase)
            || (razon?.Contains("subscription", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return (VeredictoDeQrz.SinSuscripcion, razon is null
                ? Textos.T("Servicios.Qrz.SinSuscripcion")
                : Textos.T("Servicios.Qrz.SinSuscripcion") + " " + Textos.F("Servicios.Qrz.DiceQrz", QuitarClaves(razon)));
        }

        if (razon is not null
            && (razon.Contains("api key", StringComparison.OrdinalIgnoreCase)
                || razon.Contains("access key", StringComparison.OrdinalIgnoreCase)
                || razon.Contains("invalid key", StringComparison.OrdinalIgnoreCase)))
        {
            return (VeredictoDeQrz.ClaveRechazada, MensajeDeClaveRechazada(razon));
        }

        return null;
    }

    private static string MensajeDeClaveRechazada(string? razon) =>
        razon is null
            ? Textos.T("Servicios.Qrz.ClaveRechazada")
            : Textos.T("Servicios.Qrz.ClaveRechazada") + " " + Textos.F("Servicios.Qrz.DiceQrz", QuitarClaves(razon));

    /// <summary>
    /// QRZ repite a veces la clave en el motivo. Lo que tenga forma de clave no se ensena ni
    /// se escribe en el registro.
    /// </summary>
    private static string QuitarClaves(string texto) =>
        Regex.Replace(texto, "[0-9A-Fa-f]{4}(-?[0-9A-Fa-f]{4}){3}", "····");

    private static string Motivo(IReadOnlyDictionary<string, string> respuesta)
    {
        if (RazonDe(respuesta) is { } motivo) return Textos.F("Servicios.Qrz.DiceQrz", QuitarClaves(motivo));
        if (respuesta.TryGetValue("RESULT", out var resultado))
        {
            return Textos.F("Servicios.Qrz.RespondioTexto", resultado);
        }
        return Textos.T("Servicios.Qrz.NoSeEntiende");
    }

    /// <summary>
    /// Quita la cabecera del ADI: a QRZ se le manda el registro solo, como en el ejemplo de la
    /// guia (<c>ADIF=&lt;band:3&gt;80m…&lt;eor&gt;</c>). Una cabecera delante no esta
    /// documentada y no hay por que averiguar si la traga.
    /// </summary>
    private static string SoloElRegistro(string adi)
    {
        var fin = adi.IndexOf("<EOH>", StringComparison.OrdinalIgnoreCase);
        return (fin < 0 ? adi : adi[(fin + 5)..]).Trim();
    }

    private static bool YaSeHabiaSubido(Qso qso) =>
        qso.Confirmaciones.Any(c => c.Medio == MedioDeConfirmacion.QrzCom
            && c.Enviado is EstadoDeConfirmacion.Pendiente
                or EstadoDeConfirmacion.Confirmado
                or EstadoDeConfirmacion.Verificado);

    private void ApuntarParada(string clave, VeredictoDeQrz veredicto, string mensaje)
    {
        if (_parada is null)
        {
            _log.LogWarning(
                "QRZ.com no acepta la clave del cuaderno ({Veredicto}): {Mensaje} La subida a QRZ queda parada hasta que cambie la clave o se pruebe con éxito.",
                veredicto, mensaje);
        }
        _parada = new ClaveParada(Huella(clave), veredicto, mensaje);
    }

    private static ResultadoDeSubida Parado(
        IReadOnlyList<Qso> qsos,
        Dictionary<string, string> motivos,
        string mensaje,
        int enviados,
        Stopwatch reloj,
        int desde = 0)
    {
        // Lo que quedaba sin intentar lleva el mismo motivo: nadie debe darlo por subido.
        for (var i = desde; i < qsos.Count; i++) motivos[qsos[i].ClaveNatural] = mensaje;
        reloj.Stop();
        return new ResultadoDeSubida(enviados, qsos.Count - enviados, motivos, reloj.Elapsed) { Parada = mensaje };
    }

    /// <summary>La clave guardada, sin los espacios ni saltos de linea de haberla pegado.</summary>
    private string? LeerClave() =>
        _credenciales.Leer(ClavesDeCredencial.QrzClaveDeCuaderno)?.Trim() is { Length: > 0 } clave ? clave : null;

    private static string Huella(string clave) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clave)));

    private async Task<IReadOnlyDictionary<string, string>> LlamarAsync(
        string clave, IReadOnlyDictionary<string, string> parametros, string descripcion, CancellationToken ct)
    {
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
