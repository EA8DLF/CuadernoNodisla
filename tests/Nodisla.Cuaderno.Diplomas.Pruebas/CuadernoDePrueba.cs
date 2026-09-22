using System.Globalization;
using Microsoft.Data.Sqlite;
using Nodisla.Cuaderno.Diplomas;
using Nodisla.Cuaderno.Diplomas.Calculo;
using Nodisla.Cuaderno.Dominio.Dxcc;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Nodisla.Cuaderno.Diplomas.Pruebas;

/// <summary>
/// Un cuaderno de mentira con el esquema que el motor consulta de verdad.
/// </summary>
/// <remarks>
/// Se crea a mano en vez de tirar de la capa de datos a proposito: asi las pruebas del motor de
/// diplomas no dependen de que compile otro proyecto, y si alguien renombra una columna del
/// cuaderno estas pruebas son las primeras en avisar.
/// </remarks>
public sealed class CuadernoDePrueba : IAsyncDisposable
{
    /// <summary>Diplomas que se compilan en el catalogo de las pruebas.</summary>
    /// <remarks>
    /// Se eligen pocos y de cada clase para que compilar el catalogo tarde un instante:
    /// <c>DXCC</c> (por campo, con entidades borradas), <c>WAS</c> y <c>H26</c> (por campo, el
    /// mismo campo con exigencias de confirmacion distintas), <c>WPX</c> (por campo, universo
    /// abierto), <c>IOTA</c> y <c>AA</c> (por referencia, modelada y no modelada) y <c>CCC</c>
    /// (por indicativo).
    /// </remarks>
    public static IReadOnlyList<string> DiplomasDePrueba { get; } =
        ["DXCC", "WAS", "H26", "WPX", "IOTA", "AA", "CCC"];

    private const string Esquema = """
        CREATE TABLE qso (
            id               INTEGER PRIMARY KEY AUTOINCREMENT,
            uuid             TEXT NOT NULL DEFAULT '',
            call             TEXT NOT NULL COLLATE NOCASE,
            band             TEXT NOT NULL COLLATE NOCASE,
            mode             TEXT NOT NULL COLLATE NOCASE,
            submode          TEXT COLLATE NOCASE,
            qso_inicio_utc   TEXT NOT NULL,
            dxcc             INTEGER NOT NULL DEFAULT 0,
            cqz              INTEGER,
            ituz             INTEGER,
            cont             TEXT COLLATE NOCASE,
            pfx              TEXT COLLATE NOCASE,
            state            TEXT COLLATE NOCASE,
            cnty             TEXT COLLATE NOCASE,
            qth              TEXT,
            address          TEXT,
            gridsquare       TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
            prop_mode        TEXT COLLATE NOCASE,
            station_callsign TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
            creado_utc       TEXT NOT NULL DEFAULT '',
            modificado_utc   TEXT NOT NULL DEFAULT ''
        );
        CREATE INDEX ix_qso_call           ON qso (call, qso_inicio_utc DESC);
        CREATE INDEX ix_qso_dxcc_band_mode ON qso (dxcc, band, mode);
        CREATE INDEX ix_qso_fecha          ON qso (qso_inicio_utc DESC);

        CREATE TABLE qso_confirmacion (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            qso_id          INTEGER NOT NULL REFERENCES qso(id) ON DELETE CASCADE,
            servicio        TEXT NOT NULL COLLATE NOCASE,
            enviada         TEXT NOT NULL DEFAULT 'N',
            recibida        TEXT NOT NULL DEFAULT 'N',
            fecha_envio_utc TEXT,
            fecha_recep_utc TEXT,
            via_envio       TEXT NOT NULL DEFAULT '',
            nota            TEXT
        );
        CREATE UNIQUE INDEX ux_conf_qso_servicio ON qso_confirmacion (qso_id, servicio);
        CREATE INDEX ix_conf_servicio_rec        ON qso_confirmacion (servicio, recibida, qso_id);

        CREATE TABLE qso_referencia (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            qso_id      INTEGER NOT NULL REFERENCES qso(id) ON DELETE CASCADE,
            award_code  TEXT NOT NULL COLLATE NOCASE,
            programa    TEXT COLLATE NOCASE,
            referencia  TEXT NOT NULL COLLATE NOCASE,
            propia      INTEGER NOT NULL DEFAULT 0,
            descripcion TEXT
        );
        CREATE INDEX ix_ref_award_ref ON qso_referencia (award_code, referencia, propia);
        CREATE INDEX ix_ref_qso       ON qso_referencia (qso_id);
        """;

    private readonly string _carpeta;
    private readonly SqliteConnection _escritura;
    private SqliteTransaction? _lote;
    private MotorDeDiplomas? _motor;

    /// <summary>Crea un cuaderno vacio en una carpeta temporal.</summary>
    public CuadernoDePrueba()
    {
        _carpeta = Path.Combine(Path.GetTempPath(), "nodisla-diplomas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_carpeta);
        Ruta = Path.Combine(_carpeta, "cuaderno.sqlite");

        CadenaDeConexion = new SqliteConnectionStringBuilder
        {
            DataSource = Ruta,
            Pooling = false,
        }.ToString();

        _escritura = new SqliteConnection(CadenaDeConexion);
        _escritura.Open();
        Ejecutar(Esquema);
    }

    /// <summary>Ruta del fichero del cuaderno.</summary>
    public string Ruta { get; }

    /// <summary>Cadena de conexion del cuaderno.</summary>
    public string CadenaDeConexion { get; }

    /// <summary>
    /// Carpeta donde se compila el catalogo. Es la misma para todas las pruebas: el catalogo se
    /// compila una vez y se reaprovecha mientras no cambie el recurso.
    /// </summary>
    public static string CarpetaDelCatalogo { get; } =
        Path.Combine(Path.GetTempPath(), "nodisla-catalogo-diplomas");

    /// <summary>Motor de diplomas apuntando a este cuaderno.</summary>
    /// <param name="ajustar">Retoques de configuracion.</param>
    /// <returns>El motor, ya creado.</returns>
    public MotorDeDiplomas Motor(Action<OpcionesDeDiplomas>? ajustar = null)
    {
        if (_motor is not null) return _motor;

        Directory.CreateDirectory(CarpetaDelCatalogo);
        var opciones = new OpcionesDeDiplomas
        {
            CadenaDeConexionDelCuaderno = CadenaDeConexion,
            RutaDelCatalogo = Path.Combine(CarpetaDelCatalogo, "diplomas.sqlite"),
        };
        foreach (var codigo in DiplomasDePrueba) opciones.DiplomasIncluidos.Add(codigo);
        ajustar?.Invoke(opciones);

        _motor = new MotorDeDiplomas(opciones, ResolutorDxcc.Predeterminado);
        return _motor;
    }

    /// <summary>Abre una conexion de lectura con el catalogo ya adjuntado.</summary>
    /// <returns>La conexion abierta; hay que liberarla.</returns>
    public SqliteConnection AbrirConCatalogo()
    {
        Motor().PrepararAsync().GetAwaiter().GetResult();

        var conexion = new SqliteConnection(CadenaDeConexion);
        conexion.Open();
        using var orden = conexion.CreateCommand();
        orden.CommandText = $"ATTACH DATABASE @ruta AS {ConsultasDeProgreso.AliasDelCatalogo}";
        orden.Parameters.AddWithValue("@ruta", Path.Combine(CarpetaDelCatalogo, "diplomas.sqlite"));
        orden.ExecuteNonQuery();
        return conexion;
    }

    /// <summary>Anade un contacto al cuaderno.</summary>
    /// <param name="call">Indicativo del corresponsal.</param>
    /// <param name="banda">Banda ADIF.</param>
    /// <param name="modo">Modo principal ADIF.</param>
    /// <param name="fecha">Instante del contacto, en UTC.</param>
    /// <param name="dxcc">Entidad DXCC.</param>
    /// <param name="state">Division primaria.</param>
    /// <param name="cqz">Zona CQ.</param>
    /// <param name="ituz">Zona ITU.</param>
    /// <param name="cont">Continente.</param>
    /// <param name="pfx">Prefijo WPX.</param>
    /// <param name="gridsquare">Localizador.</param>
    /// <param name="submodo">Submodo ADIF.</param>
    /// <returns>El identificador del contacto.</returns>
    public long AnadirQso(
        string call,
        string banda = "20m",
        string modo = "SSB",
        DateTimeOffset? fecha = null,
        int dxcc = 0,
        string? state = null,
        int? cqz = null,
        int? ituz = null,
        string? cont = null,
        string? pfx = null,
        string gridsquare = "",
        string? submodo = null)
    {
        var instante = (fecha ?? new DateTimeOffset(2024, 5, 1, 12, 0, 0, TimeSpan.Zero))
            .UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

        using var orden = Orden();
        orden.CommandText = """
            INSERT INTO qso (uuid, call, band, mode, submode, qso_inicio_utc, dxcc, cqz, ituz,
                             cont, pfx, state, gridsquare, creado_utc, modificado_utc)
            VALUES (@uuid, @call, @banda, @modo, @submodo, @fecha, @dxcc, @cqz, @ituz,
                    @cont, @pfx, @state, @grid, @ahora, @ahora);
            SELECT last_insert_rowid();
            """;
        orden.Parameters.AddWithValue("@uuid", Guid.NewGuid().ToString("D"));
        orden.Parameters.AddWithValue("@call", call);
        orden.Parameters.AddWithValue("@banda", banda);
        orden.Parameters.AddWithValue("@modo", modo);
        orden.Parameters.AddWithValue("@submodo", (object?)submodo ?? DBNull.Value);
        orden.Parameters.AddWithValue("@fecha", instante);
        orden.Parameters.AddWithValue("@dxcc", dxcc);
        orden.Parameters.AddWithValue("@cqz", (object?)cqz ?? DBNull.Value);
        orden.Parameters.AddWithValue("@ituz", (object?)ituz ?? DBNull.Value);
        orden.Parameters.AddWithValue("@cont", (object?)cont ?? DBNull.Value);
        orden.Parameters.AddWithValue("@pfx", (object?)pfx ?? DBNull.Value);
        orden.Parameters.AddWithValue("@state", (object?)state ?? DBNull.Value);
        orden.Parameters.AddWithValue("@grid", gridsquare);
        orden.Parameters.AddWithValue(
            "@ahora", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        return (long)(orden.ExecuteScalar() ?? 0L);
    }

    /// <summary>Anade una confirmacion a un contacto.</summary>
    /// <param name="qsoId">Contacto.</param>
    /// <param name="servicio">Codigo del servicio: <c>QSL</c>, <c>LOTW</c>, <c>EQSL</c>…</param>
    /// <param name="recibida">Codigo del estado recibido: <c>Y</c>, <c>V</c>, <c>N</c>…</param>
    public void AnadirConfirmacion(long qsoId, string servicio, string recibida)
    {
        using var orden = Orden();
        orden.CommandText = """
            INSERT INTO qso_confirmacion (qso_id, servicio, enviada, recibida, fecha_recep_utc)
            VALUES (@qso, @servicio, 'Y', @recibida, @fecha)
            ON CONFLICT (qso_id, servicio) DO UPDATE SET recibida = @recibida, fecha_recep_utc = @fecha
            """;
        orden.Parameters.AddWithValue("@qso", qsoId);
        orden.Parameters.AddWithValue("@servicio", servicio);
        orden.Parameters.AddWithValue("@recibida", recibida);
        orden.Parameters.AddWithValue(
            "@fecha", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        orden.ExecuteNonQuery();
    }

    /// <summary>Anade una referencia de programa a un contacto.</summary>
    /// <param name="qsoId">Contacto.</param>
    /// <param name="tipo">Tipo guardado: <c>IOTA</c>, <c>SOTA</c>… o <c>OTRA</c>.</param>
    /// <param name="referencia">Codigo de la referencia.</param>
    /// <param name="programa">Nombre del programa cuando el tipo es <c>OTRA</c>.</param>
    /// <param name="propia">La referencia es de mi estacion.</param>
    public void AnadirReferencia(
        long qsoId, string tipo, string referencia, string? programa = null, bool propia = false)
    {
        using var orden = Orden();
        orden.CommandText = """
            INSERT INTO qso_referencia (qso_id, award_code, programa, referencia, propia)
            VALUES (@qso, @tipo, @programa, @ref, @propia)
            """;
        orden.Parameters.AddWithValue("@qso", qsoId);
        orden.Parameters.AddWithValue("@tipo", tipo);
        orden.Parameters.AddWithValue("@programa", (object?)programa ?? DBNull.Value);
        orden.Parameters.AddWithValue("@ref", referencia);
        orden.Parameters.AddWithValue("@propia", propia ? 1 : 0);
        orden.ExecuteNonQuery();
    }

    /// <summary>Ejecuta SQL suelto contra el cuaderno.</summary>
    /// <param name="sql">Sentencias a ejecutar.</param>
    public void Ejecutar(string sql)
    {
        using var orden = Orden();
        orden.CommandText = sql;
        orden.ExecuteNonQuery();
    }

    /// <summary>Carga muchos contactos de golpe dentro de una sola transaccion.</summary>
    /// <param name="carga">Lo que hay que escribir.</param>
    public void EnLote(Action<CuadernoDePrueba> carga)
    {
        ArgumentNullException.ThrowIfNull(carga);

        _lote = _escritura.BeginTransaction();
        try
        {
            carga(this);
            _lote.Commit();
        }
        finally
        {
            _lote.Dispose();
            _lote = null;
        }
    }

    private SqliteCommand Orden()
    {
        var orden = _escritura.CreateCommand();
        orden.Transaction = _lote;
        return orden;
    }

    /// <summary>Cierra el cuaderno y borra la carpeta temporal.</summary>
    /// <returns>La tarea del cierre.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_motor is not null) await _motor.DisposeAsync().ConfigureAwait(false);
        await _escritura.DisposeAsync().ConfigureAwait(false);
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Si Windows todavia tiene el fichero cogido, la carpeta temporal se queda; no
            // merece la pena hacer fallar una prueba por eso.
        }
    }
}
