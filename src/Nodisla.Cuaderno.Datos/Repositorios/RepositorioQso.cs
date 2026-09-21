using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Datos.Configuracion;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Repositorios;

/// <summary>El cuaderno sobre EF Core y SQLite.</summary>
/// <param name="contexto">Contexto del cuaderno.</param>
public sealed class RepositorioQso(ContextoCuaderno contexto) : IRepositorioQso
{
    /// <summary>Cuantos contactos anteriores se devuelven en el aviso de «trabajado antes».</summary>
    public const int MaximoTrabajadoAntes = 50;

    /// <summary>Cuantos contactos se escriben por tanda en el alta en lote.</summary>
    private const int TamanoDeTanda = 2000;

    /// <inheritdoc/>
    public async Task<Qso?> ObtenerAsync(long id, CancellationToken ct = default) =>
        await ConHijas(contexto.Qsos).FirstOrDefaultAsync(q => q.Id == id, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<Qso?> ObtenerPorUuidAsync(Guid uuid, CancellationToken ct = default) =>
        await ConHijas(contexto.Qsos).FirstOrDefaultAsync(q => q.Uuid == uuid, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<Pagina<Qso>> BuscarAsync(
        CriterioQso criterio,
        int desplazamiento,
        int limite,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(criterio);
        ArgumentOutOfRangeException.ThrowIfNegative(desplazamiento);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limite);

        var consulta = Filtrar(ConsultaBase(criterio), criterio);

        // El recuento y la pagina se resuelven en SQL; a memoria solo llega la pagina.
        var total = await consulta.CountAsync(ct).ConfigureAwait(false);
        var elementos = await Ordenar(consulta, criterio)
            .Skip(desplazamiento)
            .Take(limite)
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new Pagina<Qso>(elementos, total, desplazamiento);
    }

    /// <inheritdoc/>
    public async Task<long> AnadirAsync(Qso qso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qso);

        Sellar(qso, esAlta: true);
        contexto.Qsos.Add(qso);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        return qso.Id;
    }

    /// <inheritdoc/>
    public async Task<int> AnadirLoteAsync(IEnumerable<Qso> qsos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qsos);

        var deteccionAutomatica = contexto.ChangeTracker.AutoDetectChangesEnabled;
        contexto.ChangeTracker.AutoDetectChangesEnabled = false;

        // Una sola transaccion para todo el lote: o entra el ADIF entero o no entra nada.
        var transaccion = await contexto.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var tanda = new List<Qso>(TamanoDeTanda);
            var total = 0;

            foreach (var qso in qsos)
            {
                ArgumentNullException.ThrowIfNull(qso);
                Sellar(qso, esAlta: true);
                tanda.Add(qso);

                if (tanda.Count >= TamanoDeTanda)
                {
                    total += await GuardarTandaAsync(tanda, ct).ConfigureAwait(false);
                }
            }

            total += await GuardarTandaAsync(tanda, ct).ConfigureAwait(false);
            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            return total;
        }
        finally
        {
            await transaccion.DisposeAsync().ConfigureAwait(false);
            contexto.ChangeTracker.Clear();
            contexto.ChangeTracker.AutoDetectChangesEnabled = deteccionAutomatica;
        }
    }

    /// <inheritdoc/>
    public async Task ActualizarAsync(Qso qso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qso);

        var existente = await ConHijas(contexto.Qsos)
            .FirstOrDefaultAsync(q => q.Id == qso.Id, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No se puede actualizar: no existe ningún contacto con identificador {qso.Id}.");

        if (!ReferenceEquals(existente, qso))
        {
            contexto.Entry(existente).CurrentValues.SetValues(qso);
            contexto.Entry(existente).Property<string?>(NombresDeColumna.PropiedadSubmodo).CurrentValue =
                qso.Mode.Submodo;
            CopiarHijas(existente, qso);
        }

        Sellar(existente, esAlta: false);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        await contexto.Qsos.Where(q => q.Id == id).ExecuteDeleteAsync(ct).ConfigureAwait(false);

        // El borrado directo no pasa por el rastreador: si el contacto estaba cargado, se suelta.
        var rastreado = contexto.ChangeTracker.Entries<Qso>().FirstOrDefault(e => e.Entity.Id == id);
        if (rastreado is not null)
        {
            rastreado.State = EntityState.Detached;
        }
    }

    /// <inheritdoc/>
    public async Task<Qso?> BuscarDuplicadoAsync(Qso candidato, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidato);

        // Mismas columnas y mismo orden que ux_qso_natural.
        return await contexto.Qsos
            .AsNoTracking()
            .Where(q => q.Call == candidato.Call
                        && q.Band == candidato.Band
                        && q.Mode == candidato.Mode
                        && q.InicioUtc == candidato.InicioUtc
                        && q.Id != candidato.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Qso>> TrabajadoAntesAsync(
        Indicativo indicativo,
        CancellationToken ct = default)
    {
        if (indicativo.EsVacio)
        {
            return [];
        }

        // Va por ix_qso_call (call, qso_inicio_utc DESC): se dispara con cada tecla que escribe
        // el operador, asi que no puede hacer nada mas que recorrer el indice.
        return await contexto.Qsos
            .AsNoTracking()
            .Where(q => q.Call == indicativo)
            .OrderByDescending(q => q.InicioUtc)
            .Take(MaximoTrabajadoAntes)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<int> ContarAsync(CancellationToken ct = default) => contexto.Qsos.CountAsync(ct);

    private static IQueryable<Qso> ConHijas(IQueryable<Qso> consulta) => consulta
        .Include(q => q.Confirmaciones)
        .Include(q => q.Referencias)
        .Include(q => q.CamposExtra)
        .AsSplitQuery();

    /// <summary>
    /// Consulta de partida. Si hay texto libre o indicativo con comodin se arranca de SQL
    /// escrito a mano, porque FTS5 y <c>LIKE</c> no se expresan en LINQ; el resto de filtros,
    /// la ordenacion y la paginacion se componen encima y se resuelven igualmente en SQL.
    /// </summary>
    private IQueryable<Qso> ConsultaBase(CriterioQso criterio)
    {
        var condiciones = new List<string>();
        var parametros = new List<object>();

        if (!string.IsNullOrWhiteSpace(criterio.Texto))
        {
            condiciones.Add(
                $"q.id IN (SELECT rowid FROM qso_fts WHERE qso_fts MATCH {{{parametros.Count}}})");
            parametros.Add(APatronFts(criterio.Texto));
        }

        if (!string.IsNullOrWhiteSpace(criterio.Call) && criterio.Call.Contains('*', StringComparison.Ordinal))
        {
            condiciones.Add($"q.call LIKE {{{parametros.Count}}}");
            parametros.Add(Indicativo.Normalizar(criterio.Call).Replace('*', '%'));
        }

        if (condiciones.Count == 0)
        {
            return contexto.Qsos;
        }

        var sql = "SELECT q.* FROM qso AS q WHERE " + string.Join(" AND ", condiciones);
        return contexto.Qsos.FromSqlRaw(sql, [.. parametros]);
    }

    private static IQueryable<Qso> Filtrar(IQueryable<Qso> consulta, CriterioQso criterio)
    {
        if (!string.IsNullOrWhiteSpace(criterio.Call)
            && !criterio.Call.Contains('*', StringComparison.Ordinal))
        {
            var indicativo = Indicativo.Crudo(criterio.Call);
            consulta = consulta.Where(q => q.Call == indicativo);
        }

        if (criterio.Band is { } banda && !banda.EsVacia)
        {
            consulta = consulta.Where(q => q.Band == banda);
        }

        if (!string.IsNullOrWhiteSpace(criterio.Mode))
        {
            var modo = Modo.Crudo(criterio.Mode, null);
            consulta = consulta.Where(q => q.Mode == modo);
        }

        if (criterio.Dxcc is { } dxcc)
        {
            consulta = consulta.Where(q => q.Dxcc == dxcc);
        }

        if (criterio.DesdeUtc is { } desde)
        {
            consulta = consulta.Where(q => q.InicioUtc >= desde);
        }

        if (criterio.HastaUtc is { } hasta)
        {
            consulta = consulta.Where(q => q.InicioUtc <= hasta);
        }

        if (criterio.EstacionId is { } estacion)
        {
            consulta = consulta.Where(q => q.EstacionId == estacion);
        }

        if (criterio.ConfirmadoPor is { } medio)
        {
            consulta = consulta.Where(q => q.Confirmaciones.Any(
                c => c.Medio == medio && c.Recibido == EstadoDeConfirmacion.Confirmado));
        }

        return consulta;
    }

    private static IQueryable<Qso> Ordenar(IQueryable<Qso> consulta, CriterioQso criterio)
    {
        var descendente = criterio.Descendente;
        var campo = string.IsNullOrWhiteSpace(criterio.OrdenarPor)
            ? nameof(Qso.InicioUtc)
            : criterio.OrdenarPor.Trim();

        IOrderedQueryable<Qso> ordenada = campo.ToUpperInvariant() switch
        {
            "INICIOUTC" => descendente
                ? consulta.OrderByDescending(q => q.InicioUtc)
                : consulta.OrderBy(q => q.InicioUtc),
            "CALL" => descendente ? consulta.OrderByDescending(q => q.Call) : consulta.OrderBy(q => q.Call),
            "BAND" => descendente ? consulta.OrderByDescending(q => q.Band) : consulta.OrderBy(q => q.Band),
            "MODE" => descendente ? consulta.OrderByDescending(q => q.Mode) : consulta.OrderBy(q => q.Mode),
            "FREQ" => descendente ? consulta.OrderByDescending(q => q.Freq) : consulta.OrderBy(q => q.Freq),
            "DXCC" => descendente ? consulta.OrderByDescending(q => q.Dxcc) : consulta.OrderBy(q => q.Dxcc),
            "COUNTRY" => descendente
                ? consulta.OrderByDescending(q => q.Country)
                : consulta.OrderBy(q => q.Country),
            "NAME" => descendente ? consulta.OrderByDescending(q => q.Name) : consulta.OrderBy(q => q.Name),
            "QTH" => descendente ? consulta.OrderByDescending(q => q.Qth) : consulta.OrderBy(q => q.Qth),
            "CREADOUTC" => descendente
                ? consulta.OrderByDescending(q => q.CreadoUtc)
                : consulta.OrderBy(q => q.CreadoUtc),
            "MODIFICADOUTC" => descendente
                ? consulta.OrderByDescending(q => q.ModificadoUtc)
                : consulta.OrderBy(q => q.ModificadoUtc),
            "ID" => descendente ? consulta.OrderByDescending(q => q.Id) : consulta.OrderBy(q => q.Id),
            _ => throw new ArgumentException(
                string.Format(
                    CultureInfo.CurrentCulture,
                    "No se puede ordenar el cuaderno por «{0}».",
                    campo),
                nameof(criterio)),
        };

        // Desempate estable: sin el, dos contactos del mismo segundo pueden saltar de pagina.
        return descendente ? ordenada.ThenByDescending(q => q.Id) : ordenada.ThenBy(q => q.Id);
    }

    /// <summary>Convierte lo que escribe el operador en una consulta de FTS5 por prefijo.</summary>
    private static string APatronFts(string texto)
    {
        var limpio = texto.Trim().Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{limpio}\"*";
    }

    private async Task<int> GuardarTandaAsync(List<Qso> tanda, CancellationToken ct)
    {
        if (tanda.Count == 0)
        {
            return 0;
        }

        contexto.Qsos.AddRange(tanda);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);

        var guardados = tanda.Count;
        tanda.Clear();
        contexto.ChangeTracker.Clear();
        return guardados;
    }

    private static void Sellar(Qso qso, bool esAlta)
    {
        var ahora = DateTimeOffset.UtcNow;
        if (esAlta && qso.CreadoUtc == default)
        {
            qso.CreadoUtc = ahora;
        }

        qso.ModificadoUtc = ahora;
        if (qso.Uuid == Guid.Empty)
        {
            qso.Uuid = Guid.NewGuid();
        }
    }

    /// <summary>
    /// Sustituye las filas hijas del contacto guardado por las del grafo que llega de fuera.
    /// Son pocas filas por contacto y asi no hay que cuadrar identificadores ajenos.
    /// </summary>
    private void CopiarHijas(Qso destino, Qso origen)
    {
        contexto.Confirmaciones.RemoveRange(destino.Confirmaciones);
        contexto.Referencias.RemoveRange(destino.Referencias);
        contexto.CamposExtra.RemoveRange(destino.CamposExtra);
        destino.Confirmaciones.Clear();
        destino.Referencias.Clear();
        destino.CamposExtra.Clear();

        foreach (var confirmacion in origen.Confirmaciones)
        {
            destino.Confirmaciones.Add(new QsoConfirmacion
            {
                QsoId = destino.Id,
                Medio = confirmacion.Medio,
                Enviado = confirmacion.Enviado,
                Recibido = confirmacion.Recibido,
                EnviadoUtc = confirmacion.EnviadoUtc,
                RecibidoUtc = confirmacion.RecibidoUtc,
                Via = confirmacion.Via,
                Nota = confirmacion.Nota,
            });
        }

        foreach (var referencia in origen.Referencias)
        {
            destino.Referencias.Add(new QsoReferencia
            {
                QsoId = destino.Id,
                Tipo = referencia.Tipo,
                NombrePrograma = referencia.NombrePrograma,
                Codigo = referencia.Codigo,
                Lado = referencia.Lado,
                Descripcion = referencia.Descripcion,
            });
        }

        foreach (var campo in origen.CamposExtra)
        {
            destino.CamposExtra.Add(new QsoCampoExtra
            {
                QsoId = destino.Id,
                Nombre = campo.Nombre,
                Valor = campo.Valor,
                TipoAdif = campo.TipoAdif,
            });
        }
    }
}
