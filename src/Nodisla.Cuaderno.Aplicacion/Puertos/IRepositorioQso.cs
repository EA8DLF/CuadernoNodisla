using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Una pagina de resultados y el total que existe tras el filtro.</summary>
/// <typeparam name="T">Tipo de los elementos devueltos.</typeparam>
public sealed record Pagina<T>(IReadOnlyList<T> Elementos, int TotalFiltrado, int Desplazamiento);

/// <summary>Campo por el que se ordena el cuaderno.</summary>
/// <remarks>
/// Es una enumeracion y no una cadena libre a proposito: con texto, la capa de datos y la
/// interfaz se desincronizan en silencio y el fallo no aparece hasta que alguien pulsa esa
/// columna. Anadir un campo aqui obliga a tratarlo en el repositorio.
/// </remarks>
public enum CampoDeOrden
{
    /// <summary>Instante de inicio del contacto. Es el orden natural del cuaderno.</summary>
    Fecha,
    Indicativo,
    Banda,
    Modo,
    Frecuencia,
    Nombre,
    Qth,
    Dxcc,
}

/// <summary>Criterios de busqueda del cuaderno. Todo nulo significa «sin filtrar».</summary>
public sealed record CriterioQso
{
    /// <summary>Texto libre: indicativo, nombre, QTH o notas.</summary>
    public string? Texto { get; init; }

    /// <summary>
    /// Indicativo exacto, o con el comodin <c>*</c> al final para buscar por comienzo
    /// (<c>EA8*</c>). El comodin es <c>*</c> y solo se admite al final.
    /// </summary>
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
    public CampoDeOrden OrdenarPor { get; init; } = CampoDeOrden.Fecha;

    public bool Descendente { get; init; } = true;
}

/// <summary>Resultado de anadir muchos contactos de una vez.</summary>
/// <param name="Anadidos">Contactos que entraron en el cuaderno.</param>
/// <param name="OmitidosPorDuplicado">
/// Contactos que ya existian con la misma clave natural y no se volvieron a guardar.
/// </param>
/// <param name="Duracion">Lo que tardo la operacion, para poder informar al operador.</param>
public sealed record ResultadoDeLote(int Anadidos, int OmitidosPorDuplicado, TimeSpan Duracion)
{
    public int Total => Anadidos + OmitidosPorDuplicado;
}

/// <summary>Acceso al cuaderno. Lo implementa la capa de datos.</summary>
public interface IRepositorioQso
{
    Task<Qso?> ObtenerAsync(long id, CancellationToken ct = default);

    Task<Qso?> ObtenerPorUuidAsync(Guid uuid, CancellationToken ct = default);

    /// <summary>
    /// Devuelve una pagina del cuaderno segun el criterio.
    /// </summary>
    /// <remarks>
    /// La ordenacion debe ser <b>determinista</b>: el campo pedido y, como desempate siempre,
    /// el <c>Id</c> en el mismo sentido. Sin desempate, dos contactos del mismo segundo pueden
    /// repetirse o saltarse al pasar de pagina, y el operador ve un cuaderno que miente.
    /// </remarks>
    Task<Pagina<Qso>> BuscarAsync(CriterioQso criterio, int desplazamiento, int limite, CancellationToken ct = default);

    /// <summary>Anade un contacto y devuelve su identificador.</summary>
    Task<long> AnadirAsync(Qso qso, CancellationToken ct = default);

    /// <summary>
    /// Anade muchos contactos de golpe, como al importar un ADIF.
    /// </summary>
    /// <param name="qsos">Contactos a guardar.</param>
    /// <param name="omitirDuplicados">
    /// Si es cierto, los contactos que ya existan con la misma clave natural se saltan y el
    /// resto entra. Si es falso, un duplicado hace fracasar el lote entero. Al importar un
    /// ADIF interesa lo primero: reimportar un respaldo es una operacion normal.
    /// </param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task<ResultadoDeLote> AnadirLoteAsync(
        IEnumerable<Qso> qsos,
        bool omitirDuplicados = true,
        CancellationToken ct = default);

    Task ActualizarAsync(Qso qso, CancellationToken ct = default);

    Task EliminarAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Busca un contacto que ya exista con la misma clave natural. Es lo que evita duplicar
    /// el cuaderno cada vez que se reimporta un ADIF.
    /// </summary>
    Task<Qso?> BuscarDuplicadoAsync(Qso candidato, CancellationToken ct = default);

    /// <summary>
    /// Contactos anteriores con el mismo indicativo, del mas reciente al mas antiguo,
    /// para el aviso de «trabajado antes».
    /// </summary>
    /// <param name="indicativo">Indicativo a buscar.</param>
    /// <param name="maximo">
    /// Tope de contactos devueltos. El aviso solo necesita unos pocos y esta consulta se
    /// dispara con cada tecla, asi que el limite es parte del contrato, no un detalle
    /// escondido en la implementacion.
    /// </param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task<IReadOnlyList<Qso>> TrabajadoAntesAsync(
        Indicativo indicativo,
        int maximo = 50,
        CancellationToken ct = default);

    Task<int> ContarAsync(CancellationToken ct = default);
}
