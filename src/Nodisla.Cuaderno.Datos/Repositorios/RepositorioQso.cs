using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Datos.Configuracion;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Datos.Repositorios;

/// <summary>El cuaderno sobre EF Core y SQLite.</summary>
/// <param name="contexto">Contexto del cuaderno.</param>
/// <param name="diplomas">
/// A quien avisar de que el cuaderno ha cambiado. Es opcional: si nadie lo registra, no se
/// avisa a nadie y el motor de diplomas se entera igual por su marca de agua, solo que mas
/// caro. Se avisa despues de confirmar la transaccion y solo si se escribio algo de verdad.
/// </param>
public sealed class RepositorioQso(
    ContextoCuaderno contexto,
    INotificadorDeDiplomas? diplomas = null) : IRepositorioQso
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
        var elementos = await ConHijasEnPagina(Ordenar(consulta, criterio)
                .Skip(desplazamiento)
                .Take(limite))
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
        diplomas?.CuadernoCambiado();
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
            if (anadidos > 0)
            {
                diplomas?.CuadernoCambiado();
            }

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
            ConciliarConfirmaciones(existente, qso);
            ConciliarReferencias(existente, qso);
            ConciliarCamposExtra(existente, qso);
        }

        Sellar(existente, esAlta: false);
        var escritas = await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        if (escritas > 0)
        {
            diplomas?.CuadernoCambiado();
        }
    }

    /// <inheritdoc/>
    public async Task EliminarAsync(long id, CancellationToken ct = default)
    {
        var borrados = await contexto.Qsos.Where(q => q.Id == id)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
        if (borrados > 0)
        {
            diplomas?.CuadernoCambiado();
        }

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

        // Mismas columnas y mismo orden que ux_qso_natural. Vuelve con sus filas hijas
        // porque quien busca un duplicado va a fundirlo: sin las confirmaciones que ya
        // tiene guardadas, la fusion las daria por perdidas y las volveria a escribir.
        return await ConHijas(contexto.Qsos)
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
        // el operador, asi que no puede hacer nada mas que recorrer el indice. Aun asi trae
        // las filas hijas: son como mucho «maximo» contactos y el aviso ensena si la QSL
        // quedo confirmada. Devolver contactos a medias costaria mas caro de lo que ahorra,
        // que es justo lo que ya paso una vez.
        return await ConHijasEnPagina(contexto.Qsos
                .Where(q => q.Call == indicativo)
                .OrderByDescending(q => q.InicioUtc)
                .ThenByDescending(q => q.Id)
                .Take(maximo))
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<int> ContarAsync(CancellationToken ct = default) => contexto.Qsos.CountAsync(ct);

    /// <summary>
    /// Anade las tres colecciones hijas a una consulta de <b>un solo</b> contacto.
    /// </summary>
    /// <remarks>
    /// Aqui no se parte la consulta: para un contacto el producto cartesiano son unas pocas
    /// decenas de filas y sale mas barato que cuatro viajes a la base.
    /// </remarks>
    private static IQueryable<Qso> ConHijas(IQueryable<Qso> consulta) => consulta
        .Include(q => q.Confirmaciones)
        .Include(q => q.Referencias)
        .Include(q => q.CamposExtra);

    /// <summary>
    /// Anade las tres colecciones hijas a una consulta de <b>muchos</b> contactos.
    /// </summary>
    /// <remarks>
    /// Con varias colecciones a la vez, una sola consulta multiplicaria cada contacto por sus
    /// confirmaciones, por sus referencias y por sus campos extra: una pagina de 200 contactos
    /// con una decena de campos cada uno son miles de filas anchas repetidas. Partida en
    /// cuatro consultas, cada fila viaja una vez.
    /// </remarks>
    private static IQueryable<Qso> ConHijasEnPagina(IQueryable<Qso> consulta) => consulta
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
    /// Deja las confirmaciones guardadas como las del contacto que llega de fuera: actualiza
    /// las que siguen, anade las que faltan y <b>borra las que ya no estan</b>.
    /// </summary>
    /// <remarks>
    /// El emparejamiento es por identificador cuando llega y, si no, por servicio, que es la
    /// clave de <c>ux_conf_qso_servicio</c>. Un grafo desconectado —el que sale de fundir un
    /// ADIF con lo guardado— trae unas filas con identificador y otras sin el, y las dos
    /// tienen que acabar en la fila que les toca en vez de duplicarse.
    /// </remarks>
    private void ConciliarConfirmaciones(Qso destino, Qso origen)
    {
        var porId = PorIdentificador(destino.Confirmaciones, c => c.Id);
        var porServicio = new Dictionary<MedioDeConfirmacion, QsoConfirmacion>();
        foreach (var c in destino.Confirmaciones)
        {
            porServicio.TryAdd(c.Medio, c);
        }

        var conservadas = new HashSet<long>();
        foreach (var entrante in origen.Confirmaciones)
        {
            var actual = Emparejar(entrante.Id, porId);
            if (actual is null && porServicio.TryGetValue(entrante.Medio, out var porMedio)
                && !conservadas.Contains(porMedio.Id))
            {
                actual = porMedio;
            }

            if (actual is null)
            {
                destino.Confirmaciones.Add(new QsoConfirmacion
                {
                    QsoId = destino.Id,
                    Medio = entrante.Medio,
                    Enviado = entrante.Enviado,
                    Recibido = entrante.Recibido,
                    EnviadoUtc = entrante.EnviadoUtc,
                    RecibidoUtc = entrante.RecibidoUtc,
                    Via = entrante.Via,
                    Nota = entrante.Nota,
                });
                continue;
            }

            actual.Medio = entrante.Medio;
            actual.Enviado = entrante.Enviado;
            actual.Recibido = entrante.Recibido;
            actual.EnviadoUtc = entrante.EnviadoUtc;
            actual.RecibidoUtc = entrante.RecibidoUtc;
            actual.Via = entrante.Via;
            actual.Nota = entrante.Nota;
            conservadas.Add(actual.Id);
        }

        Retirar(destino.Confirmaciones, contexto.Confirmaciones, c => c.Id, conservadas);
    }

    /// <summary>
    /// Deja las referencias guardadas como las del contacto que llega, borrando las que ya no
    /// estan. Se emparejan por identificador o por programa, codigo y lado.
    /// </summary>
    private void ConciliarReferencias(Qso destino, Qso origen)
    {
        var porId = PorIdentificador(destino.Referencias, r => r.Id);
        var porClave = new Dictionary<string, QsoReferencia>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in destino.Referencias)
        {
            porClave.TryAdd(ClaveDeReferencia(r), r);
        }

        var conservadas = new HashSet<long>();
        foreach (var entrante in origen.Referencias)
        {
            var actual = Emparejar(entrante.Id, porId);
            if (actual is null && porClave.TryGetValue(ClaveDeReferencia(entrante), out var porTexto)
                && !conservadas.Contains(porTexto.Id))
            {
                actual = porTexto;
            }

            if (actual is null)
            {
                destino.Referencias.Add(new QsoReferencia
                {
                    QsoId = destino.Id,
                    Tipo = entrante.Tipo,
                    NombrePrograma = entrante.NombrePrograma,
                    Codigo = entrante.Codigo,
                    Lado = entrante.Lado,
                    Descripcion = entrante.Descripcion,
                });
                continue;
            }

            actual.Tipo = entrante.Tipo;
            actual.NombrePrograma = entrante.NombrePrograma;
            actual.Codigo = entrante.Codigo;
            actual.Lado = entrante.Lado;
            actual.Descripcion = entrante.Descripcion;
            conservadas.Add(actual.Id);
        }

        Retirar(destino.Referencias, contexto.Referencias, r => r.Id, conservadas);
    }

    /// <summary>
    /// Deja los campos ADIF no modelados como los del contacto que llega, borrando los que ya
    /// no estan. Se emparejan por identificador o por nombre de campo, que es la clave de
    /// <c>ux_campo_extra</c>.
    /// </summary>
    private void ConciliarCamposExtra(Qso destino, Qso origen)
    {
        var porId = PorIdentificador(destino.CamposExtra, c => c.Id);
        var porNombre = new Dictionary<string, QsoCampoExtra>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in destino.CamposExtra)
        {
            porNombre.TryAdd(c.Nombre, c);
        }

        var conservados = new HashSet<long>();
        foreach (var entrante in origen.CamposExtra)
        {
            var actual = Emparejar(entrante.Id, porId);
            if (actual is null && porNombre.TryGetValue(entrante.Nombre, out var porTexto)
                && !conservados.Contains(porTexto.Id))
            {
                actual = porTexto;
            }

            if (actual is null)
            {
                destino.CamposExtra.Add(new QsoCampoExtra
                {
                    QsoId = destino.Id,
                    Nombre = entrante.Nombre,
                    Valor = entrante.Valor,
                    TipoAdif = entrante.TipoAdif,
                });
                continue;
            }

            actual.Nombre = entrante.Nombre;
            actual.Valor = entrante.Valor;
            actual.TipoAdif = entrante.TipoAdif;
            conservados.Add(actual.Id);
        }

        Retirar(destino.CamposExtra, contexto.CamposExtra, c => c.Id, conservados);
    }

    private static string ClaveDeReferencia(QsoReferencia referencia) =>
        $"{referencia.Tipo}|{referencia.NombrePrograma}|{referencia.Codigo}|{referencia.Lado}";

    private static Dictionary<long, T> PorIdentificador<T>(List<T> filas, Func<T, long> identificador)
    {
        var indice = new Dictionary<long, T>(filas.Count);
        foreach (var fila in filas)
        {
            var id = identificador(fila);
            if (id != 0)
            {
                indice[id] = fila;
            }
        }

        return indice;
    }

    private static T? Emparejar<T>(long id, Dictionary<long, T> porId)
        where T : class =>
        id != 0 && porId.TryGetValue(id, out var fila) ? fila : null;

    /// <summary>Borra de la base las filas hijas que ya no vienen en el contacto.</summary>
    private static void Retirar<T>(
        List<T> coleccion,
        DbSet<T> tabla,
        Func<T, long> identificador,
        HashSet<long> conservadas)
        where T : class
    {
        var sobrantes = coleccion.Where(f => !conservadas.Contains(identificador(f))
                                             && identificador(f) != 0).ToList();
        foreach (var sobrante in sobrantes)
        {
            coleccion.Remove(sobrante);
            tabla.Remove(sobrante);
        }
    }
}
