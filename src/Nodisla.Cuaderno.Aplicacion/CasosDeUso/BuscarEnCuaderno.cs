using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>
/// Consulta el cuaderno con filtro, orden y paginacion. La rejilla nunca pide el cuaderno
/// entero: pide una pagina y el total que hay detras del filtro.
/// </summary>
public sealed class BuscarEnCuaderno(IRepositorioQso repositorioQso)
{
    /// <summary>Numero de contactos por pagina si no se pide otro.</summary>
    public const int LimitePorOmision = 200;

    /// <summary>Tope duro de contactos por pagina, para no ahogar la interfaz.</summary>
    public const int LimiteMaximo = 2000;

    /// <summary>Devuelve una pagina del cuaderno segun el criterio dado.</summary>
    public Task<Pagina<Qso>> EjecutarAsync(
        CriterioQso criterio,
        int desplazamiento = 0,
        int limite = LimitePorOmision,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(criterio);

        var desde = Math.Max(0, desplazamiento);
        var cuantos = Math.Clamp(limite, 1, LimiteMaximo);
        return repositorioQso.BuscarAsync(criterio, desde, cuantos, ct);
    }

    /// <summary>Numero total de contactos del cuaderno, sin filtrar.</summary>
    public Task<int> ContarTodoAsync(CancellationToken ct = default) => repositorioQso.ContarAsync(ct);
}
