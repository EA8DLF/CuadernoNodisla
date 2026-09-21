using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Datos de una estacion obtenidos de un servicio de consulta.</summary>
public sealed record FichaIndicativo
{
    public required Indicativo Indicativo { get; init; }
    public string? Nombre { get; init; }
    public string? Direccion { get; init; }
    public string? Localidad { get; init; }
    public string? Pais { get; init; }
    public string? DivisionPrimaria { get; init; }
    public string? DivisionSecundaria { get; init; }
    public Locator Localizador { get; init; }
    public double? Latitud { get; init; }
    public double? Longitud { get; init; }
    public int? ZonaCq { get; init; }
    public int? ZonaItu { get; init; }
    public int? Dxcc { get; init; }
    public string? CorreoElectronico { get; init; }
    public string? Web { get; init; }
    public string? GestorQsl { get; init; }
    public string? Iota { get; init; }
    public Uri? Imagen { get; init; }

    /// <summary>Servicio que aporto la ficha, para saber de donde salio cada dato.</summary>
    public required string Fuente { get; init; }

    /// <summary>Momento de la consulta, para poder cachear con caducidad.</summary>
    public DateTimeOffset ConsultadoUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Consulta de datos de un indicativo en un servicio externo (QRZ.com, HamQTH…).</summary>
public interface IConsultaIndicativo
{
    /// <summary>Nombre del servicio, para mostrarlo y para elegir el orden de consulta.</summary>
    string Nombre { get; }

    /// <summary>El servicio esta configurado y con credenciales validas.</summary>
    bool EstaDisponible { get; }

    /// <summary>Consulta un indicativo. Devuelve nulo si el servicio no lo conoce.</summary>
    Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default);
}
