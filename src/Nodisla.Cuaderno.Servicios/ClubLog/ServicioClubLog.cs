using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Adif;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.ClubLog;

/// <summary>
/// Club Log: subida del cuaderno y de contactos sueltos.
/// </summary>
/// <remarks>
/// <para>
/// Club Log tiene dos entradas de subida y hay que respetar para que sirve cada una.
/// <c>putlogs.php</c> es la de ficheros: se le manda el ADIF entero y el mezcla con lo que ya
/// tiene. <c>realtime.php</c> es la de un contacto suelto, para ir subiendo segun se registra,
/// y su documentacion es explicita en que <b>no se use para lotes</b>: la carga que pone sobre
/// su base de datos solo es aceptable al ritmo al que teclea una persona. Por eso
/// <see cref="SubirAsync"/> usa siempre la primera y la segunda esta en un metodo aparte.
/// </para>
/// <para>
/// <b>Club Log no ofrece una descarga de confirmaciones.</b> Lo que publica es el estado de un
/// cuaderno frente a sus propias listas, no un flujo de QSL recibidas que se pueda volcar sobre
/// el cuaderno local. <see cref="DescargarAsync"/> devuelve una lista vacia y lo deja dicho en
/// las trazas, en vez de fingir una sincronizacion que no existe.
/// </para>
/// </remarks>
public sealed class ServicioClubLog : IServicioQsl
{
    private readonly IHttpClientFactory _fabrica;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly PoliticaDeReintentos _reintentos;
    private readonly OpcionesClubLog _opciones;
    private readonly ILogger _log;

    /// <summary>Campos que se le mandan a Club Log.</summary>
    /// <remarks>
    /// Club Log calcula la entidad DXCC por su cuenta con su propia base de excepciones, que es
    /// mejor que la de casi todos los cuadernos; mandarle la nuestra solo sirve para discutir.
    /// </remarks>
    public static IReadOnlyList<string> CamposAdmitidos { get; } =
    [
        "CALL", "QSO_DATE", "TIME_ON", "QSO_DATE_OFF", "TIME_OFF",
        "BAND", "BAND_RX", "FREQ", "MODE", "SUBMODE",
        "RST_SENT", "RST_RCVD", "PROP_MODE", "SAT_NAME", "QSL_VIA",
        "OPERATOR", "STATION_CALLSIGN", "GRIDSQUARE", "MY_GRIDSQUARE", "TX_PWR",
    ];

    /// <summary>Crea el servicio.</summary>
    /// <param name="fabrica">Fabrica de clientes HTTP.</param>
    /// <param name="credenciales">Almacen de secretos.</param>
    /// <param name="opciones">Ajustes de Club Log.</param>
    /// <param name="reintentos">Politica de reintentos.</param>
    /// <param name="log">Registro de trazas.</param>
    public ServicioClubLog(
        IHttpClientFactory fabrica,
        IAlmacenDeCredenciales credenciales,
        OpcionesClubLog opciones,
        PoliticaDeReintentos? reintentos = null,
        ILogger<ServicioClubLog>? log = null)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _reintentos = reintentos ?? new PoliticaDeReintentos();
        _log = log ?? NullLogger<ServicioClubLog>.Instance;
    }

    /// <inheritdoc />
    public MedioDeConfirmacion Medio => MedioDeConfirmacion.ClubLog;

    /// <inheritdoc />
    public string Nombre => "Club Log";

    /// <inheritdoc />
    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(_opciones.Correo)
        && !string.IsNullOrWhiteSpace(_opciones.Indicativo)
        && _credenciales.Existe(ClavesDeCredencial.ClubLogContrasena)
        && _credenciales.Existe(ClavesDeCredencial.ClubLogApi);

    /// <inheritdoc />
    public bool PuedeSubir => EstaConfigurado;

    /// <inheritdoc />
    /// <remarks>
    /// Siempre falso: Club Log no publica ninguna forma de traerse las confirmaciones. Lo que
    /// ofrece es el estado de un cuaderno frente a sus propias listas, que no es lo mismo y no
    /// se puede volcar sobre el cuaderno local.
    /// </remarks>
    public bool PuedeDescargar => false;

    /// <inheritdoc />
    public async Task<bool> ComprobarCredencialesAsync(CancellationToken ct = default)
    {
        if (!EstaConfigurado) return false;
        try
        {
            // Un fichero ADIF vacio: Club Log valida la cuenta y no altera el cuaderno.
            var vacio = AdifLigero.EscribirRegistros([], new Dictionary<string, string>
            {
                ["ADIF_VER"] = "3.1.5",
                ["PROGRAMID"] = "Cuaderno NODISLA",
            });
            await EnviarFicheroAsync(vacio, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is RespuestaDelServicioException or ServicioNoDisponibleException)
        {
            _log.LogWarning("No se pudieron validar las credenciales de Club Log: {Motivo}", ex.Message);
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

        Avisar(progreso, 0, qsos.Count, $"Enviando {qsos.Count} contactos a Club Log…");

        var registros = qsos.Select(q => ConversorDeQso.Proyectar(q, CamposAdmitidos)).ToList();
        var adif = AdifLigero.EscribirRegistros(registros, new Dictionary<string, string>
        {
            ["ADIF_VER"] = "3.1.5",
            ["PROGRAMID"] = "Cuaderno NODISLA",
        });

        try
        {
            var respuesta = await EnviarFicheroAsync(adif, ct).ConfigureAwait(false);
            reloj.Stop();
            _log.LogInformation(
                "Club Log aceptó {Cuantos} contactos en {Duracion}. Respuesta: {Respuesta}",
                qsos.Count, reloj.Elapsed, Recortar(respuesta));
            Avisar(progreso, qsos.Count, qsos.Count, "Subida a Club Log terminada.");
            return new ResultadoDeSubida(qsos.Count, 0, SinMotivos(), reloj.Elapsed);
        }
        catch (Exception ex) when (ex is RespuestaDelServicioException or ServicioNoDisponibleException)
        {
            reloj.Stop();
            _log.LogWarning("Club Log rechazó la subida: {Motivo}", ex.Message);
            var motivos = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var qso in qsos) motivos[qso.ClaveNatural] = ex.Message;
            return new ResultadoDeSubida(0, qsos.Count, motivos, reloj.Elapsed);
        }
    }

    /// <summary>
    /// Sube un unico contacto en el momento de registrarlo, por la via de tiempo real.
    /// </summary>
    /// <remarks>
    /// Solo para contactos sueltos segun se hacen. Para varios, <see cref="SubirAsync"/>.
    /// </remarks>
    /// <param name="qso">Contacto recien registrado.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<bool> SubirEnDirectoAsync(Qso qso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qso);

        var registro = ConversorDeQso.Proyectar(qso, CamposAdmitidos);
        var sb = new StringBuilder();
        foreach (var (campo, valor) in registro) AdifLigero.EscribirCampo(sb, campo, valor);
        sb.Append("<EOR>");

        var (contrasena, api) = LeerSecretos();
        try
        {
            await _reintentos.EjecutarAsync("subir un contacto en directo a Club Log", async testigo =>
            {
                var cliente = _fabrica.CreateClient(NombresDeClienteHttp.ClubLog);
                using var formulario = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["email"] = _opciones.Correo,
                    ["password"] = contrasena,
                    ["callsign"] = _opciones.Indicativo,
                    ["adif"] = sb.ToString(),
                    ["api"] = api,
                });
                using var http = await cliente.PostAsync(_opciones.UrlEnDirecto, formulario, testigo)
                    .ConfigureAwait(false);
                return await ComprobarAsync(http, testigo).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is RespuestaDelServicioException or ServicioNoDisponibleException)
        {
            _log.LogWarning(
                "Club Log no aceptó el contacto con {Indicativo}: {Motivo}", qso.Call.Valor, ex.Message);
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Club Log no publica una descarga de confirmaciones, asi que no hay nada que traer. Se
    /// devuelve una lista vacia en lugar de inventarse un origen de datos.
    /// </remarks>
    public Task<IReadOnlyList<ConfirmacionDescargada>> DescargarAsync(
        DateTimeOffset? desdeUtc,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default)
    {
        _log.LogInformation(
            "Club Log no ofrece descarga de confirmaciones; no hay nada que sincronizar hacia el cuaderno.");
        Avisar(progreso, 0, 0, "Club Log no permite descargar confirmaciones.");
        return Task.FromResult<IReadOnlyList<ConfirmacionDescargada>>([]);
    }

    private void Avisar(
        IProgress<ProgresoDeSincronizacion>? progreso, int hecho, int? total, string mensaje) =>
        progreso?.Report(new ProgresoDeSincronizacion(Nombre, hecho, total, mensaje));

    private async Task<string> EnviarFicheroAsync(string adif, CancellationToken ct)
    {
        var (contrasena, api) = LeerSecretos();

        return await _reintentos.EjecutarAsync("subir el cuaderno a Club Log", async testigo =>
        {
            var cliente = _fabrica.CreateClient(NombresDeClienteHttp.ClubLog);
            using var formulario = new MultipartFormDataContent();
            formulario.Add(new StringContent(_opciones.Correo), "email");
            formulario.Add(new StringContent(contrasena), "password");
            formulario.Add(new StringContent(_opciones.Indicativo), "callsign");
            formulario.Add(new StringContent(api), "api");
            // clear=0: se mezcla con lo que ya hay. Nunca 1: eso vacia el cuaderno de Club Log.
            formulario.Add(new StringContent("0"), "clear");

            var fichero = new StringContent(adif, Encoding.UTF8, "text/plain");
            formulario.Add(fichero, "file", "cuaderno.adi");

            using var http = await cliente.PostAsync(_opciones.UrlDeSubida, formulario, testigo)
                .ConfigureAwait(false);
            return await ComprobarAsync(http, testigo).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    private static async Task<string> ComprobarAsync(HttpResponseMessage http, CancellationToken ct)
    {
        var cuerpo = await http.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (http.IsSuccessStatusCode) return cuerpo;

        throw new RespuestaDelServicioException(
            $"Club Log respondió {(int)http.StatusCode}: {Recortar(cuerpo)}", http.StatusCode);
    }

    private (string Contrasena, string Api) LeerSecretos()
    {
        var contrasena = _credenciales.Leer(ClavesDeCredencial.ClubLogContrasena)
            ?? throw new InvalidOperationException(
                "No hay contraseña de Club Log guardada. Configúrela en Configuración › Cuentas y servicios.");
        var api = _credenciales.Leer(ClavesDeCredencial.ClubLogApi)
            ?? throw new InvalidOperationException(
                "No hay clave de API de Club Log guardada. Se pide al soporte de Club Log y se "
                + "guarda cifrada en Configuración › Cuentas y servicios.");
        return (contrasena, api);
    }

    private static string Recortar(string texto) =>
        texto.Length <= 200 ? texto.Trim() : texto[..200].Trim() + "…";

    private static IReadOnlyDictionary<string, string> SinMotivos() =>
        new Dictionary<string, string>(StringComparer.Ordinal);
}
