using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Servicios.Adif;
using Nodisla.Cuaderno.Servicios.Red;

namespace Nodisla.Cuaderno.Servicios.Lotw;

/// <summary>
/// LoTW, el sistema de confirmaciones de la ARRL.
/// </summary>
/// <remarks>
/// <para>
/// Las dos mitades de LoTW no se parecen en nada. <b>Subir</b> exige firmar cada contacto con
/// el certificado personal del operador, y eso se delega en TQSL (ver
/// <see cref="FirmanteTqsl"/>). <b>Bajar</b> es una descarga HTTPS corriente con usuario y
/// contrasena contra <c>lotwreport.adi</c>.
/// </para>
/// <para>
/// El corte de la descarga se hace con <c>qso_qslsince</c> y no con <c>qso_qsorxsince</c>: el
/// primero filtra por cuando LoTW <i>confirmo</i> el contacto, que es lo que aqui interesa, y
/// el segundo por cuando LoTW <i>recibio</i> el contacto. Un contacto subido hace tres anos y
/// confirmado ayer aparece con el primero y no con el segundo, que es justo el caso que hay
/// que ver.
/// </para>
/// </remarks>
public sealed class ServicioLotw : IServicioQsl
{
    private readonly IHttpClientFactory _fabrica;
    private readonly IAlmacenDeCredenciales _credenciales;
    private readonly IFirmanteTqsl _firmante;
    private readonly PoliticaDeReintentos _reintentos;
    private readonly OpcionesLotw _opciones;
    private readonly ILogger _log;

    /// <summary>Campos que se le mandan a TQSL.</summary>
    /// <remarks>
    /// Los campos <c>MY_*</c> se dejan fuera a proposito: en el modelo de LoTW quien describe
    /// la estacion es la «Station Location» de TQSL, y mandar ademas los del cuaderno solo
    /// consigue que TQSL avise de discrepancias en cada subida.
    /// </remarks>
    public static IReadOnlyList<string> CamposAdmitidos { get; } =
    [
        "CALL", "QSO_DATE", "TIME_ON", "QSO_DATE_OFF", "TIME_OFF",
        "BAND", "BAND_RX", "FREQ", "FREQ_RX", "MODE", "SUBMODE",
        "PROP_MODE", "SAT_NAME", "SAT_MODE", "STATION_CALLSIGN", "OPERATOR",
    ];

    /// <summary>Campos del informe que se conservan para ensenarselos al operador.</summary>
    public static IReadOnlyList<string> CamposDeDetalle { get; } =
    [
        "DXCC", "COUNTRY", "CQZ", "ITUZ", "GRIDSQUARE", "STATE", "CNTY", "IOTA",
        "APP_LOTW_DXCC", "APP_LOTW_DXCC_ENTITY_STATUS", "APP_LOTW_MODEGROUP",
        "APP_LOTW_RXQSO", "APP_LOTW_RXQSL", "CREDIT_GRANTED",
    ];

    /// <summary>Crea el servicio.</summary>
    /// <param name="fabrica">Fabrica de clientes HTTP.</param>
    /// <param name="credenciales">Almacen de secretos.</param>
    /// <param name="opciones">Ajustes de LoTW.</param>
    /// <param name="firmante">Firmante de TQSL; si es nulo se crea uno sobre las opciones.</param>
    /// <param name="reintentos">Politica de reintentos.</param>
    /// <param name="log">Registro de trazas.</param>
    public ServicioLotw(
        IHttpClientFactory fabrica,
        IAlmacenDeCredenciales credenciales,
        OpcionesLotw opciones,
        IFirmanteTqsl? firmante = null,
        PoliticaDeReintentos? reintentos = null,
        ILogger<ServicioLotw>? log = null)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _credenciales = credenciales ?? throw new ArgumentNullException(nameof(credenciales));
        _opciones = opciones ?? throw new ArgumentNullException(nameof(opciones));
        _firmante = firmante ?? new FirmanteTqsl(
            opciones, fraseDePaso: () => credenciales.Leer(ClavesDeCredencial.TqslFraseDePaso));
        _reintentos = reintentos ?? new PoliticaDeReintentos();
        _log = log ?? NullLogger<ServicioLotw>.Instance;
    }

    /// <inheritdoc />
    public MedioDeConfirmacion Medio => MedioDeConfirmacion.Lotw;

    /// <inheritdoc />
    public string Nombre => "LoTW";

    /// <inheritdoc />
    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(_opciones.Usuario)
        && _credenciales.Existe(ClavesDeCredencial.LotwContrasena);

    /// <inheritdoc />
    /// <remarks>
    /// En LoTW subir y bajar no dependen de lo mismo: bajar solo necesita usuario y contrasena,
    /// pero subir necesita ademas <b>TQSL instalado</b> y una ubicacion de estacion elegida.
    /// Por eso esto puede ser falso en una maquina y cierto en otra con la misma cuenta.
    /// </remarks>
    public bool PuedeSubir => _firmante.EstaDisponible
        && !string.IsNullOrWhiteSpace(_opciones.UbicacionDeEstacion);

    /// <inheritdoc />
    public bool PuedeDescargar => EstaConfigurado;

    /// <summary>
    /// Explicacion, lista para ensenar en pantalla, de por que no se puede subir a LoTW.
    /// Nula si si se puede.
    /// </summary>
    /// <remarks>
    /// Existe para que la interfaz diga que pasa en vez de limitarse a desactivar un boton:
    /// «no se puede subir a LoTW porque no está TQSL instalado» es accionable; un botón gris,
    /// no.
    /// </remarks>
    public string? MotivoDeNoPoderSubir
    {
        get
        {
            if (!_firmante.EstaDisponible)
            {
                return "No se puede subir a LoTW porque no está TQSL instalado en este equipo. "
                    + "LoTW exige firmar los contactos con el certificado de la ARRL: instale "
                    + "Trusted QSL, o indique dónde está tqsl.exe en los ajustes de LoTW.";
            }
            if (string.IsNullOrWhiteSpace(_opciones.UbicacionDeEstacion))
            {
                return "No se puede subir a LoTW porque no se ha elegido una ubicación de "
                    + "estación de TQSL. Es el nombre que usted le puso en TQSL, no su indicativo.";
            }
            return null;
        }
    }

    /// <summary>
    /// Aviso que la interfaz debe mostrar <b>antes</b> de pedir la frase de paso del certificado.
    /// </summary>
    /// <remarks>
    /// No es un detalle de implementacion que se pueda callar: durante la subida la frase viaja
    /// en la linea de ordenes de TQSL y es visible para cualquier programa que mire la lista de
    /// procesos. No hay otra via; la decision es del operador, pero informada.
    /// </remarks>
    public static string AvisoDeLaFraseDePaso =>
        "Si su certificado de LoTW tiene frase de paso, tenga en cuenta que TQSL solo la acepta "
        + "por línea de órdenes: mientras dura la subida, la frase es visible en la lista de "
        + "procesos del equipo. Aquí se guarda cifrada y nunca se escribe en el registro de "
        + "actividad, pero esa ventana no la podemos cerrar nosotros.";

    /// <inheritdoc />
    public async Task<bool> ComprobarCredencialesAsync(CancellationToken ct = default)
    {
        if (!EstaConfigurado) return false;
        try
        {
            // Se pide una ventana vacia: valida la contrasena sin descargarse el cuaderno entero.
            var texto = await DescargarInformeAsync(
                new Dictionary<string, string>
                {
                    ["qso_query"] = "1",
                    ["qso_qsl"] = "yes",
                    ["qso_startdate"] = "2999-01-01",
                },
                ct).ConfigureAwait(false);
            return texto.Contains("<eoh>", StringComparison.OrdinalIgnoreCase)
                || texto.Contains("APP_LoTW_EOF", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is RespuestaDelServicioException or ServicioNoDisponibleException)
        {
            _log.LogWarning("No se pudieron validar las credenciales de LoTW: {Motivo}", ex.Message);
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

        if (qsos.Count == 0)
        {
            return new ResultadoDeSubida(0, 0, VacioDeMotivos(), reloj.Elapsed);
        }

        Avisar(progreso, 0, qsos.Count, "Preparando el fichero para TQSL…");

        var registros = qsos.Select(q => ConversorDeQso.Proyectar(q, CamposAdmitidos)).ToList();
        var contenido = AdifLigero.EscribirRegistros(
            registros,
            new Dictionary<string, string>
            {
                ["ADIF_VER"] = "3.1.5",
                ["PROGRAMID"] = "Cuaderno NODISLA",
            });

        var temporal = Path.Combine(
            Path.GetTempPath(), $"nodisla-lotw-{Guid.NewGuid():N}.adi");
        try
        {
            await File.WriteAllTextAsync(temporal, contenido, new UTF8Encoding(false), ct)
                .ConfigureAwait(false);

            Avisar(progreso, 0, qsos.Count, "Firmando y subiendo con TQSL…");
            var resultado = await _firmante.FirmarYSubirAsync(temporal, ct).ConfigureAwait(false);
            reloj.Stop();

            if (resultado.EsExito)
            {
                Avisar(progreso, qsos.Count, qsos.Count, "Subida a LoTW terminada.");
                _log.LogInformation(
                    "LoTW: {Cuantos} contactos firmados y subidos en {Duracion}.",
                    qsos.Count, reloj.Elapsed);
                return new ResultadoDeSubida(qsos.Count, 0, VacioDeMotivos(), reloj.Elapsed);
            }

            var motivo = resultado.Descripcion.Length > 0
                ? resultado.Descripcion
                : FirmanteTqsl.DescripcionDe(resultado.Codigo);
            _log.LogWarning("LoTW rechazó la subida: {Motivo} (código {Codigo})", motivo, resultado.Codigo);

            var motivos = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var qso in qsos) motivos[qso.ClaveNatural] = motivo;
            return new ResultadoDeSubida(0, qsos.Count, motivos, reloj.Elapsed);
        }
        finally
        {
            BorrarSinRuido(temporal);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfirmacionDescargada>> DescargarAsync(
        DateTimeOffset? desdeUtc,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default)
    {
        var parametros = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["qso_query"] = "1",
            ["qso_qsl"] = "yes",
        };

        if (_opciones.PedirDetalleDeQsl) parametros["qso_qsldetail"] = "yes";
        if (!string.IsNullOrWhiteSpace(_opciones.FiltrarPorIndicativoPropio))
        {
            parametros["qso_owncall"] = _opciones.FiltrarPorIndicativoPropio;
        }
        if (desdeUtc is { } desde)
        {
            parametros["qso_qslsince"] =
                desde.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        // El informe completo de un cuaderno de muchos anos tarda minutos en generarse al otro
        // lado, y hasta que llega no hay nada que contar: el aviso de que se esta esperando es
        // lo unico que distingue «va lento» de «se ha colgado».
        Avisar(progreso, 0, null, desdeUtc is null
            ? "Pidiendo a LoTW el informe completo; puede tardar varios minutos…"
            : "Pidiendo a LoTW las confirmaciones nuevas…");

        var texto = await DescargarInformeAsync(parametros, ct).ConfigureAwait(false);

        Avisar(progreso, 0, null, "Leyendo el informe de LoTW…");
        var confirmaciones = Interpretar(texto);

        Avisar(progreso, confirmaciones.Count, confirmaciones.Count,
            $"LoTW devolvió {confirmaciones.Count} confirmaciones.");
        return confirmaciones;
    }

    private void Avisar(
        IProgress<ProgresoDeSincronizacion>? progreso, int hecho, int? total, string mensaje) =>
        progreso?.Report(new ProgresoDeSincronizacion(Nombre, hecho, total, mensaje));

    /// <summary>Traduce el informe ADI de LoTW a confirmaciones.</summary>
    /// <param name="informe">Contenido del fichero <c>lotwreport.adi</c>.</param>
    public static IReadOnlyList<ConfirmacionDescargada> Interpretar(string informe)
    {
        var confirmaciones = new List<ConfirmacionDescargada>();
        foreach (var registro in AdifLigero.LeerRegistros(informe))
        {
            // Solo interesan las confirmaciones; el informe puede traer contactos sin confirmar.
            if (!LectorDeConfirmaciones.EsSi(registro, "QSL_RCVD")) continue;
            if (!LectorDeConfirmaciones.TryLeerClave(registro, out var clave)) continue;

            confirmaciones.Add(new ConfirmacionDescargada(
                clave.Call,
                clave.Band,
                clave.Mode,
                clave.InicioUtc,
                MedioDeConfirmacion.Lotw,
                FechaDeLaConfirmacion(registro),
                // En LoTW una confirmacion siempre esta verificada: las dos partes han firmado
                // el contacto con su certificado. No hay confirmacion «de palabra».
                Verificada: true)
            {
                CamposExtra = LectorDeConfirmaciones.CamposExtra(registro, CamposDeDetalle),
            });
        }
        return confirmaciones;
    }

    private static DateTimeOffset? FechaDeLaConfirmacion(IReadOnlyDictionary<string, string> registro)
    {
        // APP_LOTW_RXQSL viene con fecha y hora; QSLRDATE, solo con fecha.
        var rx = LectorDeConfirmaciones.Campo(registro, "APP_LOTW_RXQSL");
        if (!string.IsNullOrWhiteSpace(rx)
            && DateTime.TryParse(rx, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var cuando))
        {
            return new DateTimeOffset(cuando, TimeSpan.Zero);
        }
        return LectorDeConfirmaciones.FechaDeConfirmacion(registro, "QSLRDATE");
    }

    private async Task<string> DescargarInformeAsync(
        IReadOnlyDictionary<string, string> parametros, CancellationToken ct)
    {
        var contrasena = _credenciales.Leer(ClavesDeCredencial.LotwContrasena)
            ?? throw new InvalidOperationException(
                "No hay contraseña de LoTW guardada. Configúrela en los ajustes del programa.");

        var consulta = new StringBuilder();
        consulta.Append("login=").Append(Uri.EscapeDataString(_opciones.Usuario));
        consulta.Append("&password=").Append(Uri.EscapeDataString(contrasena));
        foreach (var (clave, valor) in parametros)
        {
            consulta.Append('&').Append(clave).Append('=').Append(Uri.EscapeDataString(valor));
        }

        var url = new UriBuilder(_opciones.UrlDelInforme) { Query = consulta.ToString() }.Uri;

        // Se traza la direccion sin la consulta: lleva la contrasena en claro.
        _log.LogInformation("Descargando el informe de LoTW de {Servidor}.", _opciones.UrlDelInforme);

        return await _reintentos.EjecutarAsync("descargar el informe de LoTW", async testigo =>
        {
            var cliente = _fabrica.CreateClient(NombresDeClienteHttp.Lotw);
            using var respuesta = await cliente.GetAsync(url, testigo).ConfigureAwait(false);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(testigo).ConfigureAwait(false);

            if (!respuesta.IsSuccessStatusCode)
            {
                throw new RespuestaDelServicioException(
                    $"LoTW respondió {(int)respuesta.StatusCode}.", respuesta.StatusCode);
            }

            // Con la contrasena mal, LoTW devuelve una pagina web con codigo 200. La unica
            // forma fiable de distinguirla es que un ADI de verdad trae cabecera.
            if (!cuerpo.Contains("<eoh>", StringComparison.OrdinalIgnoreCase)
                && !cuerpo.Contains("APP_LoTW_EOF", StringComparison.OrdinalIgnoreCase))
            {
                throw new RespuestaDelServicioException(
                    "LoTW no devolvió un fichero ADIF. Lo habitual es que el usuario o la "
                    + "contraseña no sean correctos.");
            }
            return cuerpo;
        }, ct).ConfigureAwait(false);
    }

    private static IReadOnlyDictionary<string, string> VacioDeMotivos() =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    private void BorrarSinRuido(string ruta)
    {
        try
        {
            if (File.Exists(ruta)) File.Delete(ruta);
        }
        catch (IOException ex)
        {
            _log.LogWarning("No se pudo borrar el fichero temporal {Ruta}: {Motivo}", ruta, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.LogWarning("No se pudo borrar el fichero temporal {Ruta}: {Motivo}", ruta, ex.Message);
        }
    }
}
