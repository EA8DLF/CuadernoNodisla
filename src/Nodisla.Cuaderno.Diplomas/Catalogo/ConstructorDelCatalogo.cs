using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Dominio.Dxcc;

namespace Nodisla.Cuaderno.Diplomas.Catalogo;

/// <summary>Cifras de una construccion del catalogo.</summary>
/// <param name="Diplomas">Diplomas compilados.</param>
/// <param name="Variantes">Variantes compiladas.</param>
/// <param name="Referencias">Referencias compiladas.</param>
/// <param name="DxccPermitidos">Filas de <c>premio_dxcc_permitido</c> escritas.</param>
/// <param name="VentanasDelResolutor">
/// Referencias de DXCC cuya ventana temporal se ha tomado del resolutor del dominio en vez de
/// la del catalogo original.
/// </param>
/// <param name="Duracion">Lo que tardo.</param>
public sealed record ResumenDelCatalogo(
    int Diplomas,
    int Variantes,
    int Referencias,
    int DxccPermitidos,
    int VentanasDelResolutor,
    TimeSpan Duracion);

/// <summary>
/// Compila el catalogo incrustado en una base SQLite indexada.
/// </summary>
/// <remarks>
/// <para>
/// Aqui es donde se normaliza <c>AllowedDXCC</c>. El original guarda por cada referencia una
/// lista de texto de hasta 3.000 caracteres (<c>#24#;#514#;…</c>) que no se puede indexar y que
/// obliga a resolver en memoria; el catalogo compilado la vuelca en
/// <c>premio_dxcc_permitido</c>, con clave primaria e indice por entidad. Son 1,5 millones de
/// filas que se escriben una sola vez.
/// </para>
/// <para>
/// Las ventanas temporales de las entidades DXCC no se copian del original: se toman del
/// resolutor del dominio, que es quien sabe cuando nacio y cuando se borro cada entidad. Asi el
/// motor no reimplementa esa logica y las dos partes del programa no pueden discrepar.
/// </para>
/// </remarks>
/// <param name="resolutor">Resolutor de entidades DXCC del dominio.</param>
/// <param name="registro">Diario de la aplicacion.</param>
public sealed class ConstructorDelCatalogo(
    IResolutorDxcc resolutor,
    ILogger<ConstructorDelCatalogo>? registro = null)
{
    private const string CodigoDxcc = "DXCC";
    private readonly ILogger _registro = registro ?? NullLogger<ConstructorDelCatalogo>.Instance;

    /// <summary>Esquema del catalogo compilado.</summary>
    private const string Esquema = """
        CREATE TABLE catalogo_info (
            huella       TEXT    NOT NULL,
            version      INTEGER NOT NULL,
            fecha_datos  TEXT    NOT NULL,
            generado_utc TEXT    NOT NULL,
            compilado_utc TEXT   NOT NULL,
            incluidos    TEXT    NOT NULL
        );

        CREATE TABLE premio_referencia (
            award_code   TEXT NOT NULL COLLATE NOCASE,
            referencia   TEXT NOT NULL COLLATE NOCASE,
            alias        TEXT COLLATE NOCASE,
            valido_desde TEXT NOT NULL DEFAULT '',
            valido_hasta TEXT NOT NULL DEFAULT '',
            descripcion  TEXT NOT NULL DEFAULT '',
            grupo        TEXT COLLATE NOCASE,
            subgrupo     TEXT COLLATE NOCASE,
            gridsquare   TEXT COLLATE NOCASE,
            puntuacion   REAL NOT NULL DEFAULT 0,
            bonus        REAL NOT NULL DEFAULT 0,
            valido       INTEGER NOT NULL DEFAULT 1,
            PRIMARY KEY (award_code, referencia, valido_desde, valido_hasta)
        );

        CREATE TABLE premio_patron (
            award_code   TEXT NOT NULL COLLATE NOCASE,
            referencia   TEXT NOT NULL COLLATE NOCASE,
            patron       TEXT NOT NULL,
            prefijo      TEXT NOT NULL,
            PRIMARY KEY (award_code, referencia, patron)
        );

        CREATE TABLE premio_dxcc_permitido (
            award_code   TEXT NOT NULL COLLATE NOCASE,
            referencia   TEXT NOT NULL COLLATE NOCASE,
            dxcc         INTEGER NOT NULL,
            PRIMARY KEY (award_code, referencia, dxcc)
        ) WITHOUT ROWID;
        """;

    /// <summary>Indices, que se crean al final para no pagarlos en cada insercion.</summary>
    private const string Indices = """
        CREATE INDEX ix_pref_busqueda ON premio_referencia (referencia);
        CREATE INDEX ix_pdxcc_dxcc    ON premio_dxcc_permitido (dxcc, award_code);
        CREATE INDEX ix_ppatron_pfx   ON premio_patron (award_code, prefijo);
        """;

    /// <summary>
    /// Deja el catalogo compilado y al dia en la ruta indicada. Si ya existe con la misma
    /// huella, no hace nada.
    /// </summary>
    /// <param name="ruta">Fichero del catalogo compilado.</param>
    /// <param name="catalogo">Parte del catalogo ya leida del recurso.</param>
    /// <param name="incluidos">Diplomas a compilar; vacio significa todos.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Las cifras de la construccion, o nulo si no hizo falta reconstruir.</returns>
    public ResumenDelCatalogo? AsegurarCatalogo(
        string ruta,
        CatalogoDeDiplomas catalogo,
        IReadOnlySet<string>? incluidos = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);

        var marca = Marca(catalogo, incluidos);
        if (EstaAlDia(ruta, marca))
        {
            _registro.LogDebug("El catalogo de diplomas ya esta al dia en {Ruta}.", ruta);
            return null;
        }

        return Construir(ruta, catalogo, incluidos, marca, ct);
    }

    /// <summary>Reconstruye el catalogo desde cero, borrando el que hubiera.</summary>
    /// <param name="ruta">Fichero del catalogo compilado.</param>
    /// <param name="catalogo">Parte del catalogo ya leida del recurso.</param>
    /// <param name="incluidos">Diplomas a compilar; vacio significa todos.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Las cifras de la construccion.</returns>
    public ResumenDelCatalogo Reconstruir(
        string ruta,
        CatalogoDeDiplomas catalogo,
        IReadOnlySet<string>? incluidos = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        return Construir(ruta, catalogo, incluidos, Marca(catalogo, incluidos), ct);
    }

    private static string Marca(CatalogoDeDiplomas catalogo, IReadOnlySet<string>? incluidos)
    {
        var lista = incluidos is { Count: > 0 }
            ? string.Join(',', incluidos.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            : "*";
        return $"{catalogo.Cabecera.Huella}|{lista}";
    }

    private bool EstaAlDia(string ruta, string marca)
    {
        if (!File.Exists(ruta)) return false;

        try
        {
            using var conexion = Abrir(ruta, soloLectura: true);
            using var orden = conexion.CreateCommand();
            orden.CommandText = "SELECT huella FROM catalogo_info LIMIT 1";
            return orden.ExecuteScalar() as string == marca;
        }
        catch (SqliteException ex)
        {
            _registro.LogWarning(ex, "El catalogo de {Ruta} no se puede leer; se reconstruye.", ruta);
            return false;
        }
    }

    private ResumenDelCatalogo Construir(
        string ruta,
        CatalogoDeDiplomas catalogo,
        IReadOnlySet<string>? incluidos,
        string marca,
        CancellationToken ct)
    {
        var reloj = Stopwatch.StartNew();
        var carpeta = Path.GetDirectoryName(Path.GetFullPath(ruta));
        if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

        // Se construye a un lado y se mueve al final: si algo falla a medias, el catalogo
        // bueno que hubiera sigue estando.
        var temporal = ruta + ".nuevo";
        BorrarConSusAnexos(temporal);

        int referencias, permitidos, ventanas;
        using (var conexion = Abrir(temporal, soloLectura: false))
        {
            Ejecutar(conexion, "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA cache_size=-65536;");
            Ejecutar(conexion, Esquema);

            using (var transaccion = conexion.BeginTransaction())
            {
                var porIndicativo = catalogo.Diplomas.Values
                    .Where(p => p.Clase == Aplicacion.Puertos.ClaseDeDiploma.PorIndicativo)
                    .Select(p => p.Codigo)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                (referencias, permitidos, ventanas) =
                    InsertarReferencias(conexion, transaccion, incluidos, porIndicativo, ct);
                transaccion.Commit();
            }

            Ejecutar(conexion, Indices);

            using var orden = conexion.CreateCommand();
            orden.CommandText = """
                INSERT INTO catalogo_info
                    (huella, version, fecha_datos, generado_utc, compilado_utc, incluidos)
                VALUES (@huella, @version, @datos, @generado, @compilado, @incluidos)
                """;
            orden.Parameters.AddWithValue("@huella", marca);
            orden.Parameters.AddWithValue("@version", catalogo.Cabecera.Version);
            orden.Parameters.AddWithValue("@datos", catalogo.Cabecera.FechaDeLosDatos);
            orden.Parameters.AddWithValue("@generado", catalogo.Cabecera.FechaDeGeneracion);
            orden.Parameters.AddWithValue(
                "@compilado", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            orden.Parameters.AddWithValue(
                "@incluidos", incluidos is { Count: > 0 } ? string.Join(',', incluidos) : "*");
            orden.ExecuteNonQuery();

            Ejecutar(conexion, "PRAGMA optimize;");
        }

        SqliteConnection.ClearAllPools();
        BorrarConSusAnexos(ruta);
        File.Move(temporal, ruta, overwrite: true);

        var diplomas = incluidos is { Count: > 0 }
            ? catalogo.Diplomas.Keys.Count(c => incluidos.Contains(c))
            : catalogo.Diplomas.Count;
        var variantes = catalogo.Variantes
            .Where(p => incluidos is not { Count: > 0 } || incluidos.Contains(p.Key))
            .Sum(p => p.Value.Count);

        var resumen = new ResumenDelCatalogo(
            diplomas, variantes, referencias, permitidos, ventanas, reloj.Elapsed);

        _registro.LogInformation(
            "Catalogo de diplomas compilado: {Diplomas} diplomas, {Variantes} variantes, " +
            "{Referencias} referencias y {Permitidos} entidades permitidas en {Segundos:F1} s.",
            resumen.Diplomas, resumen.Variantes, resumen.Referencias, resumen.DxccPermitidos,
            resumen.Duracion.TotalSeconds);

        return resumen;
    }

    private (int Referencias, int Permitidos, int Ventanas) InsertarReferencias(
        SqliteConnection conexion,
        SqliteTransaction transaccion,
        IReadOnlySet<string>? incluidos,
        IReadOnlySet<string> porIndicativo,
        CancellationToken ct)
    {
        using var ordenRef = conexion.CreateCommand();
        ordenRef.Transaction = transaccion;
        ordenRef.CommandText = """
            INSERT OR REPLACE INTO premio_referencia
                (award_code, referencia, alias, valido_desde, valido_hasta, descripcion,
                 grupo, subgrupo, gridsquare, puntuacion, bonus, valido)
            VALUES (@codigo, @ref, @alias, @desde, @hasta, @desc,
                    @grupo, @subgrupo, @grid, @puntos, @bonus, @valido)
            """;
        var pCodigo = ordenRef.Parameters.Add("@codigo", SqliteType.Text);
        var pRef = ordenRef.Parameters.Add("@ref", SqliteType.Text);
        var pAlias = ordenRef.Parameters.Add("@alias", SqliteType.Text);
        var pDesde = ordenRef.Parameters.Add("@desde", SqliteType.Text);
        var pHasta = ordenRef.Parameters.Add("@hasta", SqliteType.Text);
        var pDesc = ordenRef.Parameters.Add("@desc", SqliteType.Text);
        var pGrupo = ordenRef.Parameters.Add("@grupo", SqliteType.Text);
        var pSubgrupo = ordenRef.Parameters.Add("@subgrupo", SqliteType.Text);
        var pGrid = ordenRef.Parameters.Add("@grid", SqliteType.Text);
        var pPuntos = ordenRef.Parameters.Add("@puntos", SqliteType.Real);
        var pBonus = ordenRef.Parameters.Add("@bonus", SqliteType.Real);
        var pValido = ordenRef.Parameters.Add("@valido", SqliteType.Integer);

        using var ordenDxcc = conexion.CreateCommand();
        ordenDxcc.Transaction = transaccion;
        ordenDxcc.CommandText = """
            INSERT OR IGNORE INTO premio_dxcc_permitido (award_code, referencia, dxcc)
            VALUES (@codigo, @ref, @dxcc)
            """;
        var dCodigo = ordenDxcc.Parameters.Add("@codigo", SqliteType.Text);
        var dRef = ordenDxcc.Parameters.Add("@ref", SqliteType.Text);
        var dDxcc = ordenDxcc.Parameters.Add("@dxcc", SqliteType.Integer);

        using var ordenPatron = conexion.CreateCommand();
        ordenPatron.Transaction = transaccion;
        ordenPatron.CommandText = """
            INSERT OR IGNORE INTO premio_patron (award_code, referencia, patron, prefijo)
            VALUES (@codigo, @ref, @patron, @prefijo)
            """;
        var xCodigo = ordenPatron.Parameters.Add("@codigo", SqliteType.Text);
        var xRef = ordenPatron.Parameters.Add("@ref", SqliteType.Text);
        var xPatron = ordenPatron.Parameters.Add("@patron", SqliteType.Text);
        var xPrefijo = ordenPatron.Parameters.Add("@prefijo", SqliteType.Text);

        var referencias = 0;
        var permitidos = 0;
        var ventanas = 0;

        foreach (var referencia in LectorDelRecurso.LeerReferencias(incluidos))
        {
            ct.ThrowIfCancellationRequested();

            var desde = referencia.ValidoDesde ?? string.Empty;
            var hasta = referencia.ValidoHasta ?? string.Empty;
            var valido = referencia.Valida;

            // Las entidades DXCC las fecha el dominio, no el catalogo del original.
            if (string.Equals(referencia.Codigo, CodigoDxcc, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(referencia.Referencia, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero) &&
                resolutor.PorNumero(numero) is { } entidad)
            {
                desde = entidad.ValidaDesde?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
                hasta = entidad.ValidaHasta?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
                valido = !entidad.EstaBorrada;
                ventanas++;
            }

            pCodigo.Value = referencia.Codigo;
            pRef.Value = referencia.Referencia;
            pAlias.Value = (object?)referencia.Alias ?? DBNull.Value;
            pDesde.Value = desde;
            pHasta.Value = hasta;
            pDesc.Value = referencia.Descripcion;
            pGrupo.Value = (object?)referencia.Grupo ?? DBNull.Value;
            pSubgrupo.Value = (object?)referencia.Subgrupo ?? DBNull.Value;
            pGrid.Value = (object?)referencia.Gridsquare ?? DBNull.Value;
            pPuntos.Value = referencia.Puntuacion;
            pBonus.Value = referencia.Bonus;
            pValido.Value = valido ? 1 : 0;
            ordenRef.ExecuteNonQuery();
            referencias++;

            // Los diplomas por indicativo casan con patrones (3B8*) o con indicativos enteros.
            // Se guardan aparte, con la parte fija delante, para que la consulta pueda entrar por
            // indice en vez de comparar cada indicativo con cada patron.
            if (valido && porIndicativo.Contains(referencia.Codigo))
            {
                xCodigo.Value = referencia.Codigo;
                xRef.Value = referencia.Referencia;
                foreach (var patron in new[] { referencia.Referencia, referencia.Alias })
                {
                    if (string.IsNullOrWhiteSpace(patron)) continue;
                    var normal = patron.ToUpperInvariant();
                    xPatron.Value = normal;
                    xPrefijo.Value = ParteFija(normal);
                    ordenPatron.ExecuteNonQuery();
                }
            }

            if (referencia.DxccPermitidos.Count == 0) continue;

            dCodigo.Value = referencia.Codigo;
            dRef.Value = referencia.Referencia;
            foreach (var dxcc in referencia.DxccPermitidos)
            {
                dDxcc.Value = dxcc;
                ordenDxcc.ExecuteNonQuery();
                permitidos++;
            }
        }

        return (referencias, permitidos, ventanas);
    }

    /// <summary>Parte literal de un patron, la que va delante del primer comodin.</summary>
    /// <param name="patron">Patron del catalogo, ya en mayusculas.</param>
    /// <returns>El prefijo fijo; cadena vacia si el patron empieza por comodin.</returns>
    public static string ParteFija(string patron)
    {
        ArgumentNullException.ThrowIfNull(patron);

        var corte = patron.IndexOfAny(['*', '?', '[']);
        if (corte < 0) return patron;
        return corte >= LargoMaximoDePrefijo ? patron[..LargoMaximoDePrefijo] : patron[..corte];
    }

    /// <summary>
    /// Hasta donde se compara la parte fija de un patron. Es el mismo numero que usa la consulta
    /// para generar los recortes del indicativo, asi que los dos lados tienen que coincidir.
    /// </summary>
    public const int LargoMaximoDePrefijo = 14;

    private static SqliteConnection Abrir(string ruta, bool soloLectura)
    {
        var cadena = new SqliteConnectionStringBuilder
        {
            DataSource = ruta,
            Mode = soloLectura ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        var conexion = new SqliteConnection(cadena);
        conexion.Open();
        return conexion;
    }

    private static void Ejecutar(SqliteConnection conexion, string sql)
    {
        using var orden = conexion.CreateCommand();
        orden.CommandText = sql;
        orden.ExecuteNonQuery();
    }

    private static void BorrarConSusAnexos(string ruta)
    {
        foreach (var sufijo in new[] { string.Empty, "-wal", "-shm", "-journal" })
        {
            var fichero = ruta + sufijo;
            if (File.Exists(fichero)) File.Delete(fichero);
        }
    }
}
