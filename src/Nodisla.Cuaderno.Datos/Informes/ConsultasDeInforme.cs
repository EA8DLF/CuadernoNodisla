using System.Data.Common;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Informes;

/// <summary>Contactos y confirmaciones agrupados por banda.</summary>
/// <remarks>
/// Los tipos de los informes se leen por propiedad y no por constructor: SQLite devuelve
/// todos los enteros como INTEGER de 64 bits y asi Dapper los ajusta al tipo declarado.
/// </remarks>
public sealed record ResumenPorBanda
{
    /// <summary>Banda ADIF.</summary>
    public string Band { get; init; } = string.Empty;

    /// <summary>Contactos hechos en esa banda.</summary>
    public int Contactos { get; init; }

    /// <summary>Contactos con alguna confirmacion recibida.</summary>
    public int Confirmados { get; init; }
}

/// <summary>Contactos y confirmaciones agrupados por modo.</summary>
public sealed record ResumenPorModo
{
    /// <summary>Modo principal ADIF.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>Contactos hechos en ese modo.</summary>
    public int Contactos { get; init; }

    /// <summary>Contactos con alguna confirmacion recibida.</summary>
    public int Confirmados { get; init; }
}

/// <summary>Una casilla de la matriz de entidades DXCC por banda.</summary>
public sealed record CasillaDxcc
{
    /// <summary>Numero de entidad DXCC.</summary>
    public int Dxcc { get; init; }

    /// <summary>Banda ADIF.</summary>
    public string Band { get; init; } = string.Empty;

    /// <summary>Contactos con esa entidad en esa banda.</summary>
    public int Trabajados { get; init; }

    /// <summary>Cuantos estan confirmados por el servicio consultado.</summary>
    public int Confirmados { get; init; }
}

/// <summary>Respuesta al «esto es nuevo?» que se muestra al teclear un indicativo.</summary>
public sealed record Novedad
{
    /// <summary>La entidad ya estaba en el cuaderno.</summary>
    public bool Visto { get; init; }

    /// <summary>Ya estaba en esa banda.</summary>
    public bool VistoEnBanda { get; init; }

    /// <summary>Ya estaba en esa banda y ese modo.</summary>
    public bool VistoEnHueco { get; init; }
}

/// <summary>Cifras generales del cuaderno.</summary>
public sealed record TotalesDelCuaderno
{
    /// <summary>Contactos registrados.</summary>
    public int Contactos { get; init; }

    /// <summary>Indicativos distintos.</summary>
    public int Indicativos { get; init; }

    /// <summary>Entidades DXCC distintas.</summary>
    public int Entidades { get; init; }

    /// <summary>Fecha del contacto mas antiguo.</summary>
    public string? PrimeroUtc { get; init; }

    /// <summary>Fecha del contacto mas reciente.</summary>
    public string? UltimoUtc { get; init; }
}

/// <summary>
/// Las consultas de informe, escritas a mano y leidas con Dapper.
/// </summary>
/// <remarks>
/// EF Core escribe y Dapper lee los informes: las pantallas de estadisticas y diplomas hacen
/// agregaciones sobre decenas de miles de filas, y materializar entidades con seguimiento para
/// luego contarlas cuesta mucho mas que leer un <c>record</c> plano. Se usa la misma conexion
/// que EF Core, asi que comparte transaccion y ajustes de SQLite.
/// </remarks>
/// <param name="contexto">Contexto del cuaderno, del que se toma la conexion.</param>
public sealed class ConsultasDeInforme(ContextoCuaderno contexto)
{
    private DbConnection Conexion => contexto.Database.GetDbConnection();

    /// <summary>Contactos y confirmaciones por banda, de la banda mas usada a la que menos.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Una fila por banda.</returns>
    public async Task<IReadOnlyList<ResumenPorBanda>> PorBandaAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT q.band                                        AS Band,
                   COUNT(*)                                      AS Contactos,
                   COUNT(DISTINCT CASE WHEN c.recibida = 'Y' THEN q.id END) AS Confirmados
              FROM qso q
              LEFT JOIN qso_confirmacion c ON c.qso_id = q.id
             GROUP BY q.band
             ORDER BY Contactos DESC
            """;

        var filas = await Conexion.QueryAsync<ResumenPorBanda>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return filas.ToList();
    }

    /// <summary>Contactos y confirmaciones por modo principal.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Una fila por modo.</returns>
    public async Task<IReadOnlyList<ResumenPorModo>> PorModoAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT q.mode                                        AS Mode,
                   COUNT(*)                                      AS Contactos,
                   COUNT(DISTINCT CASE WHEN c.recibida = 'Y' THEN q.id END) AS Confirmados
              FROM qso q
              LEFT JOIN qso_confirmacion c ON c.qso_id = q.id
             GROUP BY q.mode
             ORDER BY Contactos DESC
            """;

        var filas = await Conexion.QueryAsync<ResumenPorModo>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return filas.ToList();
    }

    /// <summary>
    /// Matriz de entidades DXCC por banda con el estado de confirmacion de un servicio.
    /// </summary>
    /// <param name="servicio">Codigo del servicio, por ejemplo <c>LOTW</c>.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Una fila por entidad y banda.</returns>
    public async Task<IReadOnlyList<CasillaDxcc>> MatrizDxccPorBandaAsync(
        string servicio = "LOTW",
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT q.dxcc                                        AS Dxcc,
                   q.band                                        AS Band,
                   COUNT(*)                                      AS Trabajados,
                   COUNT(DISTINCT CASE WHEN c.recibida = 'Y' THEN q.id END) AS Confirmados
              FROM qso q
              LEFT JOIN qso_confirmacion c
                     ON c.qso_id = q.id AND c.servicio = @servicio
             WHERE q.dxcc > 0
             GROUP BY q.dxcc, q.band
             ORDER BY q.dxcc, q.band
            """;

        var filas = await Conexion.QueryAsync<CasillaDxcc>(
            new CommandDefinition(sql, new { servicio }, cancellationToken: ct)).ConfigureAwait(false);
        return filas.ToList();
    }

    /// <summary>
    /// Responde de una vez si una entidad DXCC es nueva, nueva en la banda o nueva en el hueco
    /// de banda y modo. Es lo que colorea el aviso mientras se registra el contacto.
    /// </summary>
    /// <param name="dxcc">Entidad DXCC.</param>
    /// <param name="banda">Banda del contacto.</param>
    /// <param name="modo">Modo del contacto.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Que partes ya estaban en el cuaderno.</returns>
    public async Task<Novedad> ConsultarNovedadAsync(
        int dxcc,
        Banda banda,
        Modo modo,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT EXISTS(SELECT 1 FROM qso WHERE dxcc = @dxcc)                                   AS Visto,
                   EXISTS(SELECT 1 FROM qso WHERE dxcc = @dxcc AND band = @band)                  AS VistoEnBanda,
                   EXISTS(SELECT 1 FROM qso WHERE dxcc = @dxcc AND band = @band AND mode = @mode) AS VistoEnHueco
            """;

        var parametros = new { dxcc, band = banda.Nombre ?? string.Empty, mode = modo.Principal ?? string.Empty };
        return await Conexion.QuerySingleAsync<Novedad>(
            new CommandDefinition(sql, parametros, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Cifras generales para la pantalla de resumen.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Los totales del cuaderno.</returns>
    public async Task<TotalesDelCuaderno> TotalesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT COUNT(*)                                      AS Contactos,
                   COUNT(DISTINCT call)                          AS Indicativos,
                   COUNT(DISTINCT CASE WHEN dxcc > 0 THEN dxcc END) AS Entidades,
                   MIN(qso_inicio_utc)                           AS PrimeroUtc,
                   MAX(qso_inicio_utc)                           AS UltimoUtc
              FROM qso
            """;

        return await Conexion.QuerySingleAsync<TotalesDelCuaderno>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
    }
}
