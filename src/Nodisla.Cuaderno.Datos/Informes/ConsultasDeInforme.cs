using System.Data.Common;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Datos.Conversores;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Informes;

/// <summary>
/// Las consultas de informe, escritas a mano y leidas con Dapper.
/// </summary>
/// <remarks>
/// EF Core escribe y Dapper lee los informes: las pantallas de estadisticas y diplomas hacen
/// agregaciones sobre decenas de miles de filas, y materializar entidades con seguimiento para
/// luego contarlas cuesta mucho mas que leer un <c>record</c> plano. Se usa la misma conexion
/// que EF Core, asi que comparte transaccion y ajustes de SQLite.
/// <para>
/// Cuenta como confirmado tanto <c>Y</c> como <c>V</c>: una confirmacion verificada por el
/// servicio vale para diplomas igual o mas que una simple, como dice
/// <see cref="QsoConfirmacion.EstaConfirmada"/>.
/// </para>
/// </remarks>
/// <param name="contexto">Contexto del cuaderno, del que se toma la conexion.</param>
public sealed class ConsultasDeInforme(ContextoCuaderno contexto) : IConsultasDeInforme
{
    /// <summary>Condicion SQL de «esta confirmada», alineada con el dominio.</summary>
    private const string Confirmada = "c.recibida IN ('Y','V')";

    private DbConnection Conexion => contexto.Database.GetDbConnection();

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResumenPorBanda>> PorBandaAsync(CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT q.band                                               AS Band,
                   COUNT(*)                                             AS Contactos,
                   COUNT(DISTINCT CASE WHEN {Confirmada} THEN q.id END) AS Confirmados
              FROM qso q
              LEFT JOIN qso_confirmacion c ON c.qso_id = q.id
             GROUP BY q.band
             ORDER BY Contactos DESC
            """;

        var filas = await Conexion.QueryAsync<FilaResumen>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return filas.Select(f => new ResumenPorBanda(f.Band ?? string.Empty, f.Contactos, f.Confirmados))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ResumenPorModo>> PorModoAsync(CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT q.mode                                               AS Mode,
                   COUNT(*)                                             AS Contactos,
                   COUNT(DISTINCT CASE WHEN {Confirmada} THEN q.id END) AS Confirmados
              FROM qso q
              LEFT JOIN qso_confirmacion c ON c.qso_id = q.id
             GROUP BY q.mode
             ORDER BY Contactos DESC
            """;

        var filas = await Conexion.QueryAsync<FilaResumen>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);
        return filas.Select(f => new ResumenPorModo(f.Mode ?? string.Empty, f.Contactos, f.Confirmados))
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CasillaDxcc>> MatrizDxccPorBandaAsync(
        MedioDeConfirmacion medio = MedioDeConfirmacion.Lotw,
        CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT q.dxcc                                               AS Dxcc,
                   q.band                                               AS Band,
                   COUNT(*)                                             AS Trabajados,
                   COUNT(DISTINCT CASE WHEN {Confirmada} THEN q.id END) AS Confirmados
              FROM qso q
              LEFT JOIN qso_confirmacion c
                     ON c.qso_id = q.id AND c.servicio = @servicio
             WHERE q.dxcc > 0
             GROUP BY q.dxcc, q.band
             ORDER BY q.dxcc, q.band
            """;

        var parametros = new { servicio = ConversorDeMedio.CodigoDe(medio) };
        var filas = await Conexion.QueryAsync<FilaCasilla>(
            new CommandDefinition(sql, parametros, cancellationToken: ct)).ConfigureAwait(false);
        return filas
            .Select(f => new CasillaDxcc(f.Dxcc, f.Band ?? string.Empty, f.Trabajados, f.Confirmados))
            .ToList();
    }

    /// <inheritdoc/>
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

        var parametros = new
        {
            dxcc,
            band = banda.Nombre ?? string.Empty,
            mode = modo.Principal ?? string.Empty,
        };

        var fila = await Conexion.QuerySingleAsync<FilaNovedad>(
            new CommandDefinition(sql, parametros, cancellationToken: ct)).ConfigureAwait(false);
        return new Novedad(fila.Visto, fila.VistoEnBanda, fila.VistoEnHueco);
    }

    /// <inheritdoc/>
    public async Task<TotalesDelCuaderno> TotalesAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT COUNT(*)                                         AS Contactos,
                   COUNT(DISTINCT call)                             AS Indicativos,
                   COUNT(DISTINCT CASE WHEN dxcc > 0 THEN dxcc END) AS Entidades,
                   MIN(qso_inicio_utc)                              AS PrimeroUtc,
                   MAX(qso_inicio_utc)                              AS UltimoUtc
              FROM qso
            """;

        var fila = await Conexion.QuerySingleAsync<FilaTotales>(
            new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);

        return new TotalesDelCuaderno(
            fila.Contactos,
            fila.Indicativos,
            fila.Entidades,
            AInstante(fila.PrimeroUtc),
            AInstante(fila.UltimoUtc));
    }

    private static DateTimeOffset? AInstante(string? texto) =>
        string.IsNullOrEmpty(texto) ? null : ConversoresDeValor.AFecha(texto);

    // Los tipos que lee Dapper van con propiedades y no con constructor: SQLite devuelve todos
    // los enteros como INTEGER de 64 bits y asi se ajustan al tipo declarado.
    private sealed class FilaResumen
    {
        public string? Band { get; init; }
        public string? Mode { get; init; }
        public int Contactos { get; init; }
        public int Confirmados { get; init; }
    }

    private sealed class FilaCasilla
    {
        public int Dxcc { get; init; }
        public string? Band { get; init; }
        public int Trabajados { get; init; }
        public int Confirmados { get; init; }
    }

    private sealed class FilaNovedad
    {
        public bool Visto { get; init; }
        public bool VistoEnBanda { get; init; }
        public bool VistoEnHueco { get; init; }
    }

    private sealed class FilaTotales
    {
        public int Contactos { get; init; }
        public int Indicativos { get; init; }
        public int Entidades { get; init; }
        public string? PrimeroUtc { get; init; }
        public string? UltimoUtc { get; init; }
    }
}
