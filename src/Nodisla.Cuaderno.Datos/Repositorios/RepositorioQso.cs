using System.Diagnostics;
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
    public async Task<ResultadoDeLote> AnadirLoteAsync(
        IEnumerable<Qso> qsos,
        bool omitirDuplicados = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qsos);

        var reloj = Stopwatch.StartNew();
        var deteccionAutomatica = contexto.ChangeTracker.AutoDetectChangesEnabled;
        contexto.ChangeTracker.AutoDetectChangesEnabled = false;

        // Una sola transaccion para todo el lote: o entra el ADIF entero o no entra nada.
        var transaccion = await contexto.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var tanda = new List<Qso>(TamanoDeTanda);
            var anadidos = 0;
            var omitidos = 0;

            foreach (var qso in qsos)
            {
                ArgumentNullException.ThrowIfNull(qso);
                Sellar(qso, esAlta: true);
                tanda.Add(qso);

                if (tanda.Count >= TamanoDeTanda)
                {
                    var llena = await GuardarTandaAsync(tanda, omitirDuplicados, ct).ConfigureAwait(false);
                    anadidos += llena.Anadidos;
                    omitidos += llena.Omitidos;
                }
            }

            var ultima = await GuardarTandaAsync(tanda, omitirDuplicados, ct).ConfigureAwait(false);
            anadidos += ultima.Anadidos;
            omitidos += ultima.Omitidos;

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
            reloj.Stop();
            return new ResultadoDeLote(anadidos, omitidos, reloj.Elapsed);
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
        int maximo = 50,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximo);

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
            .Take(maximo)
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
            // Verificada cuenta igual que confirmada, como en QsoConfirmacion.EstaConfirmada.
            consulta = consulta.Where(q => q.Confirmaciones.Any(
                c => c.Medio == medio
                     && (c.Recibido == EstadoDeConfirmacion.Confirmado
                         || c.Recibido == EstadoDeConfirmacion.Verificado)));
        }

        return consulta;
    }

    private static IQueryable<Qso> Ordenar(IQueryable<Qso> consulta, CriterioQso criterio)
    {
        var descendente = criterio.Descendente;

        IOrderedQueryable<Qso> ordenada = criterio.OrdenarPor switch
        {
            CampoDeOrden.Indicativo => descendente
                ? consulta.OrderByDescending(q => q.Call)
                : consulta.OrderBy(q => q.Call),
            CampoDeOrden.Banda => descendente
                ? consulta.OrderByDescending(q => q.Band)
                : consulta.OrderBy(q => q.Band),
            CampoDeOrden.Modo => descendente
                ? consulta.OrderByDescending(q => q.Mode)
                : consulta.OrderBy(q => q.Mode),
            CampoDeOrden.Frecuencia => descendente
                ? consulta.OrderByDescending(q => q.Freq)
                : consulta.OrderBy(q => q.Freq),
            CampoDeOrden.Nombre => descendente
                ? consulta.OrderByDescending(q => q.Name)
                : consulta.OrderBy(q => q.Name),
            CampoDeOrden.Qth => descendente
                ? consulta.OrderByDescending(q => q.Qth)
                : consulta.OrderBy(q => q.Qth),
            CampoDeOrden.Dxcc => descendente
                ? consulta.OrderByDescending(q => q.Dxcc)
                : consulta.OrderBy(q => q.Dxcc),
            _ => descendente
                ? consulta.OrderByDescending(q => q.InicioUtc)
                : consulta.OrderBy(q => q.InicioUtc),
        };

        // Desempate obligatorio por Id, en el mismo sentido: sin el, dos contactos del mismo
        // segundo se repiten o se saltan al pasar de pagina.
        return descendente ? ordenada.ThenByDescending(q => q.Id) : ordenada.ThenBy(q => q.Id);
    }

    /// <summary>Convierte lo que escribe el operador en una consulta de FTS5 por prefijo.</summary>
    private static string APatronFts(string texto)
    {
        var limpio = texto.Trim().Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{limpio}\"*";
    }

    /// <summary>
    /// Guarda una tanda. Si se piden omitir duplicados, antes consulta de una sola vez que
    /// claves naturales de la tanda ya estan en el cuaderno y descarta esos contactos.
    /// </summary>
    private async Task<(int Anadidos, int Omitidos)> GuardarTandaAsync(
        List<Qso> tanda,
        bool omitirDuplicados,
        CancellationToken ct)
    {
        if (tanda.Count == 0)
        {
            return (0, 0);
        }

        var omitidos = 0;
        if (omitirDuplicados)
        {
            var yaEstan = await ClavesExistentesAsync(tanda, ct).ConfigureAwait(false);
            var aGuardar = new List<Qso>(tanda.Count);
            foreach (var qso in tanda)
            {
                // El conjunto descarta tambien los repetidos dentro del propio fichero.
                if (yaEstan.Add(qso.ClaveNatural))
                {
                    aGuardar.Add(qso);
                }
                else
                {
                    omitidos++;
                }
            }

            tanda.Clear();
            tanda.AddRange(aGuardar);
        }

        var guardados = tanda.Count;
        if (guardados > 0)
        {
            contexto.Qsos.AddRange(tanda);
            await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        tanda.Clear();
        contexto.ChangeTracker.Clear();
        return (guardados, omitidos);
    }

    /// <summary>
    /// Claves naturales de la tanda que ya existen en el cuaderno. Se consulta por indicativo,
    /// que es la primera columna de <c>ux_qso_natural</c>, y se comparan sin distinguir
    /// mayusculas igual que hace la propia columna.
    /// </summary>
    private async Task<HashSet<string>> ClavesExistentesAsync(List<Qso> tanda, CancellationToken ct)
    {
        var indicativos = tanda.Select(q => q.Call).Distinct().ToList();

        var candidatos = await contexto.Qsos
            .AsNoTracking()
            .Where(q => indicativos.Contains(q.Call))
            .Select(q => new { q.Call, q.Band, q.Mode, q.InicioUtc })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var claves = new HashSet<string>(candidatos.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var candidato in candidatos)
        {
            claves.Add(ClaveNatural(candidato.Call, candidato.Band, candidato.Mode, candidato.InicioUtc));
        }

        return claves;
    }

    /// <summary>Misma forma que <see cref="Qso.ClaveNatural"/> sin materializar el contacto.</summary>
    private static string ClaveNatural(Indicativo call, Banda band, Modo mode, DateTimeOffset inicio) =>
        FormattableString.Invariant(
            $"{call.Valor}|{band.Nombre}|{mode.Principal}|{inicio.UtcDateTime:yyyy-MM-dd HH:mm:ss}");

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
