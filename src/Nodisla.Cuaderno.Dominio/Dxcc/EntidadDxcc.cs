using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Dxcc;

/// <summary>Una entidad del listado DXCC de la ARRL.</summary>
public sealed record EntidadDxcc
{
    /// <summary>Numero de entidad DXCC. Es la clave que viaja en el campo <c>DXCC</c> de ADIF.</summary>
    public required int Numero { get; init; }

    /// <summary>Nombre en ingles, tal y como lo publica la ARRL.</summary>
    public required string Nombre { get; init; }

    /// <summary>Nombre en espanol para mostrar en pantalla. Si falta, se usa el ingles.</summary>
    public string? NombreEspanol { get; init; }

    /// <summary>Prefijo principal de la entidad, por ejemplo <c>EA8</c>.</summary>
    public required string PrefijoPrincipal { get; init; }

    /// <summary>Continente: <c>EU</c>, <c>AF</c>, <c>NA</c>, <c>SA</c>, <c>AS</c>, <c>OC</c>, <c>AN</c>.</summary>
    public required string Continente { get; init; }

    /// <summary>Zona CQ por omision de la entidad.</summary>
    public int ZonaCq { get; init; }

    /// <summary>Zona ITU por omision de la entidad.</summary>
    public int ZonaItu { get; init; }

    /// <summary>Latitud del centro de la entidad, en grados decimales.</summary>
    public double Latitud { get; init; }

    /// <summary>Longitud del centro de la entidad, en grados decimales.</summary>
    public double Longitud { get; init; }

    /// <summary>Desfase horario respecto a UTC, en horas.</summary>
    public double DesfaseUtc { get; init; }

    /// <summary>Fecha desde la que la entidad es valida para DXCC. Nulo si siempre lo fue.</summary>
    public DateOnly? ValidaDesde { get; init; }

    /// <summary>Fecha en la que la entidad fue borrada del listado. Nulo si sigue vigente.</summary>
    public DateOnly? ValidaHasta { get; init; }

    /// <summary>La entidad ya no cuenta para DXCC.</summary>
    public bool EstaBorrada => ValidaHasta is not null;

    /// <summary>Nombre preferido para la interfaz.</summary>
    public string NombreParaMostrar => NombreEspanol ?? Nombre;

    /// <summary>Coordenada del centro de la entidad.</summary>
    public Coordenada Coordenada => new(Latitud, Longitud);

    /// <summary>Indica si la entidad era valida en la fecha dada.</summary>
    public bool EraValidaEn(DateOnly fecha) =>
        (ValidaDesde is null || fecha >= ValidaDesde) &&
        (ValidaHasta is null || fecha <= ValidaHasta);
}
