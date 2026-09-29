using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Servicios.Pruebas.Dobles;

/// <summary>Cuaderno en memoria que sustituye a la capa de datos durante las pruebas.</summary>
public sealed class RepositorioQsoDoble : IRepositorioQso
{
    private readonly List<Qso> _qsos = [];
    private long _siguienteId = 1;

    /// <summary>Contactos guardados, en el orden en que entraron.</summary>
    public IReadOnlyList<Qso> Contenido => _qsos;

    /// <summary>Veces que se ha preguntado por contactos anteriores de un indicativo.</summary>
    public int ConsultasDeTrabajadoAntes { get; private set; }

    /// <summary>Tope que pidio la ultima consulta de «trabajado antes».</summary>
    public int MaximoPedido { get; private set; }

    /// <summary>
    /// Veces que se ha preguntado si un contacto ya estaba. Importa porque en la importacion
    /// inicial, con el cuaderno vacio, no deberia preguntarse ni una sola vez.
    /// </summary>
    public int ConsultasDeDuplicado { get; private set; }

    /// <summary>Veces que se ha guardado un contacto ya existente.</summary>
    public int Actualizaciones { get; private set; }

    /// <summary>Veces que se ha pedido el total de contactos del cuaderno.</summary>
    public int Conteos { get; private set; }

    /// <summary>Mete un contacto directamente, sin pasar por ningun caso de uso.</summary>
    public Qso Sembrar(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);
        qso.Id = _siguienteId++;
        _qsos.Add(qso);
        return qso;
    }

    public Task<Qso?> ObtenerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_qsos.FirstOrDefault(q => q.Id == id));

    public Task<Qso?> ObtenerPorUuidAsync(Guid uuid, CancellationToken ct = default) =>
        Task.FromResult(_qsos.FirstOrDefault(q => q.Uuid == uuid));

    public Task<Pagina<Qso>> BuscarAsync(CriterioQso criterio, int desplazamiento, int limite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(criterio);
        IEnumerable<Qso> consulta = _qsos;

        if (!string.IsNullOrWhiteSpace(criterio.Texto))
        {
            var t = criterio.Texto.Trim();
            consulta = consulta.Where(q =>
                Contiene(q.Call.Valor, t) || Contiene(q.Name, t) || Contiene(q.Qth, t) ||
                Contiene(q.Comentario, t) || Contiene(q.Notas, t));
        }

        if (!string.IsNullOrWhiteSpace(criterio.Call))
        {
            var c = Indicativo.Normalizar(criterio.Call).TrimEnd('*');
            consulta = consulta.Where(q => q.Call.Valor.StartsWith(c, StringComparison.OrdinalIgnoreCase));
        }

        if (criterio.Band is { } banda && !banda.EsVacia)
        {
            consulta = consulta.Where(q => q.Band == banda);
        }

        if (!string.IsNullOrWhiteSpace(criterio.Mode))
        {
            consulta = consulta.Where(q =>
                string.Equals(q.Mode.Principal, criterio.Mode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(q.Mode.Submodo, criterio.Mode, StringComparison.OrdinalIgnoreCase));
        }

        if (criterio.DesdeUtc is { } desde) consulta = consulta.Where(q => q.InicioUtc >= desde);
        if (criterio.HastaUtc is { } hasta) consulta = consulta.Where(q => q.InicioUtc <= hasta);
        if (criterio.EstacionId is { } estacion) consulta = consulta.Where(q => q.EstacionId == estacion);

        var filtrados = consulta.ToList();

        // El contrato exige orden determinista: el campo pedido y el Id como desempate,
        // siempre en el mismo sentido. Si no, la paginacion repite o se salta contactos.
        Func<Qso, IComparable> clave = criterio.OrdenarPor switch
        {
            CampoDeOrden.Indicativo => q => q.Call.Valor,
            CampoDeOrden.Banda => q => q.Band.Nombre,
            CampoDeOrden.Modo => q => q.Mode.NombreUsual,
            CampoDeOrden.Frecuencia => q => q.Freq.Megahercios,
            CampoDeOrden.Nombre => q => q.Name ?? string.Empty,
            CampoDeOrden.Qth => q => q.Qth ?? string.Empty,
            CampoDeOrden.Dxcc => q => q.Dxcc,
            _ => q => q.InicioUtc,
        };

        filtrados = criterio.Descendente
            ? filtrados.OrderByDescending(clave).ThenByDescending(q => q.Id).ToList()
            : filtrados.OrderBy(clave).ThenBy(q => q.Id).ToList();

        var pagina = filtrados.Skip(desplazamiento).Take(limite).ToList();
        return Task.FromResult(new Pagina<Qso>(pagina, filtrados.Count, desplazamiento));
    }

    public Task<long> AnadirAsync(Qso qso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qso);
        qso.Id = _siguienteId++;
        _qsos.Add(qso);
        return Task.FromResult(qso.Id);
    }

    public Task<ResultadoDeLote> AnadirLoteAsync(
        IEnumerable<Qso> qsos,
        bool omitirDuplicados = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qsos);
        var anadidos = 0;
        var omitidos = 0;

        foreach (var q in qsos)
        {
            var clave = q.ClaveNatural;
            if (_qsos.Any(otro => otro.ClaveNatural == clave))
            {
                if (!omitirDuplicados) throw new InvalidOperationException("Contacto duplicado en el lote.");
                omitidos++;
                continue;
            }
            Sembrar(q);
            anadidos++;
        }

        return Task.FromResult(new ResultadoDeLote(anadidos, omitidos, TimeSpan.Zero));
    }

    public Task ActualizarAsync(Qso qso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qso);
        Actualizaciones++;
        var i = _qsos.FindIndex(q => q.Id == qso.Id);
        if (i >= 0) _qsos[i] = qso;
        return Task.CompletedTask;
    }

    public Task EliminarAsync(long id, CancellationToken ct = default)
    {
        _qsos.RemoveAll(q => q.Id == id);
        return Task.CompletedTask;
    }

    public Task<Qso?> BuscarDuplicadoAsync(Qso candidato, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidato);
        ConsultasDeDuplicado++;
        var clave = candidato.ClaveNatural;
        return Task.FromResult(_qsos.FirstOrDefault(q => q.Id != candidato.Id && q.ClaveNatural == clave));
    }

    public Task<IReadOnlyList<Qso>> TrabajadoAntesAsync(
        Indicativo indicativo,
        int maximo = 50,
        CancellationToken ct = default)
    {
        ConsultasDeTrabajadoAntes++;
        MaximoPedido = maximo;

        IReadOnlyList<Qso> resultado = _qsos
            .Where(q => string.Equals(q.Call.Valor, indicativo.Valor, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(q => q.InicioUtc)
            .ThenByDescending(q => q.Id)
            .Take(Math.Max(1, maximo))
            .ToList();
        return Task.FromResult(resultado);
    }

    public Task<int> ContarAsync(CancellationToken ct = default)
    {
        Conteos++;
        return Task.FromResult(_qsos.Count);
    }

    private static bool Contiene(string? campo, string texto) =>
        campo is not null && campo.Contains(texto, StringComparison.OrdinalIgnoreCase);
}
