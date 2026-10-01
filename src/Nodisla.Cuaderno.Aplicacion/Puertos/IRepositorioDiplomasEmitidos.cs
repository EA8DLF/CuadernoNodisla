using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>El historial de diplomas emitidos y su numeracion correlativa.</summary>
public interface IRepositorioDiplomasEmitidos
{
    /// <summary>El numero que le tocaria al siguiente diploma de una serie, sin reservarlo.</summary>
    /// <param name="serie">La serie.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>El numero, desde 1.</returns>
    Task<int> SiguienteNumeroAsync(string serie, CancellationToken ct = default);

    /// <summary>
    /// Apunta un diploma con el siguiente numero de su serie. El numero se asigna aqui, dentro de
    /// una transaccion: dos emisiones seguidas nunca comparten numero.
    /// </summary>
    /// <param name="diploma">El diploma; se le ponen <see cref="DiplomaEmitido.Id"/> y <see cref="DiplomaEmitido.Numero"/>.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>El mismo diploma, ya numerado.</returns>
    Task<DiplomaEmitido> EmitirAsync(DiplomaEmitido diploma, CancellationToken ct = default);

    /// <summary>Los emitidos, del mas reciente al mas antiguo.</summary>
    /// <param name="limite">Cuantos como mucho.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Los diplomas.</returns>
    Task<IReadOnlyList<DiplomaEmitido>> ListarAsync(int limite = 500, CancellationToken ct = default);

    /// <summary>Un emitido por su identificador.</summary>
    /// <param name="id">El identificador.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>El diploma o nulo.</returns>
    Task<DiplomaEmitido?> ObtenerAsync(long id, CancellationToken ct = default);

    /// <summary>Apunta que se ha mandado por correo.</summary>
    /// <param name="id">El diploma.</param>
    /// <param name="correo">A donde.</param>
    /// <param name="cuando">Cuando.</param>
    /// <param name="ct">Cancelacion.</param>
    /// <returns>Tarea.</returns>
    Task MarcarEnviadoAsync(long id, string correo, DateTimeOffset cuando, CancellationToken ct = default);
}
