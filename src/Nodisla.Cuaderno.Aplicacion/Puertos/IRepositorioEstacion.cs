using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Acceso a los perfiles de estacion.</summary>
public interface IRepositorioEstacion
{
    Task<IReadOnlyList<Estacion>> TodasAsync(bool soloActivas = true, CancellationToken ct = default);

    Task<Estacion?> ObtenerAsync(long id, CancellationToken ct = default);

    /// <summary>Perfil marcado como predeterminado, o el unico que haya.</summary>
    Task<Estacion?> PredeterminadaAsync(CancellationToken ct = default);

    Task<long> AnadirAsync(Estacion estacion, CancellationToken ct = default);

    Task ActualizarAsync(Estacion estacion, CancellationToken ct = default);

    /// <summary>Marca un perfil como predeterminado y desmarca el anterior.</summary>
    Task EstablecerPredeterminadaAsync(long id, CancellationToken ct = default);
}
