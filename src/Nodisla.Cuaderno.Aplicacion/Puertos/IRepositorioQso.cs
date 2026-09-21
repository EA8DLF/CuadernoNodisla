using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Una pagina de resultados y el total que existe tras el filtro.</summary>
/// <typeparam name="T">Tipo de los elementos devueltos.</typeparam>
public sealed record Pagina<T>(IReadOnlyList<T> Elementos, int TotalFiltrado, int Desplazamiento);

/// <summary>Criterios de busqueda del cuaderno. Todo nulo significa «sin filtrar».</summary>
public sealed record CriterioQso
{
    /// <summary>Texto libre: indicativo, nombre, QTH o notas.</summary>
    public string? Texto { get; init; }

    /// <summary>Indicativo exacto o con comodin al final.</summary>
    public string? Call { get; init; }

    public Banda? Band { get; init; }

    /// <summary>Modo principal ADIF, por ejemplo <c>MFSK</c>.</summary>
    public string? Mode { get; init; }

    public int? Dxcc { get; init; }

    public DateTimeOffset? DesdeUtc { get; init; }

    public DateTimeOffset? HastaUtc { get; init; }

    public long? EstacionId { get; init; }

    /// <summary>Filtrar por confirmacion recibida en un medio concreto.</summary>
    public MedioDeConfirmacion? ConfirmadoPor { get; init; }

    /// <summary>Campo por el que ordenar. Por omision, el instante de inicio.</summary>
    public string OrdenarPor { get; init; } = nameof(Qso.InicioUtc);

    public bool Descendente { get; init; } = true;
}

/// <summary>Acceso al cuaderno. Lo implementa la capa de datos.</summary>
public interface IRepositorioQso
{
    Task<Qso?> ObtenerAsync(long id, CancellationToken ct = default);

    Task<Qso?> ObtenerPorUuidAsync(Guid uuid, CancellationToken ct = default);

    Task<Pagina<Qso>> BuscarAsync(CriterioQso criterio, int desplazamiento, int limite, CancellationToken ct = default);

    /// <summary>Anade un contacto y devuelve su identificador.</summary>
    Task<long> AnadirAsync(Qso qso, CancellationToken ct = default);

    /// <summary>Anade muchos contactos de golpe, como al importar un ADIF.</summary>
    Task<int> AnadirLoteAsync(IEnumerable<Qso> qsos, CancellationToken ct = default);

    Task ActualizarAsync(Qso qso, CancellationToken ct = default);

    Task EliminarAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Busca un contacto que ya exista con la misma clave natural. Es lo que evita duplicar
    /// el cuaderno cada vez que se reimporta un ADIF.
    /// </summary>
    Task<Qso?> BuscarDuplicadoAsync(Qso candidato, CancellationToken ct = default);

    /// <summary>Contactos anteriores con el mismo indicativo, para el aviso de «trabajado antes».</summary>
    Task<IReadOnlyList<Qso>> TrabajadoAntesAsync(Indicativo indicativo, CancellationToken ct = default);

    Task<int> ContarAsync(CancellationToken ct = default);
}
