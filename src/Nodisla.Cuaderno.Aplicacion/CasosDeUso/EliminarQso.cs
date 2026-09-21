using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>
/// Borra un contacto del cuaderno. La confirmacion del operador es cosa de la interfaz;
/// aqui solo se comprueba que el contacto exista antes de borrarlo.
/// </summary>
public sealed class EliminarQso(IRepositorioQso repositorioQso)
{
    /// <summary>Borra el contacto indicado. Devuelve falso si ya no estaba en el cuaderno.</summary>
    public async Task<bool> EjecutarAsync(long id, CancellationToken ct = default)
    {
        if (id <= 0) return false;

        var existente = await repositorioQso.ObtenerAsync(id, ct).ConfigureAwait(false);
        if (existente is null) return false;

        await repositorioQso.EliminarAsync(id, ct).ConfigureAwait(false);
        return true;
    }
}
