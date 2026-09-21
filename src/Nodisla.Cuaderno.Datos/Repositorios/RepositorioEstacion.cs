using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Repositorios;

/// <summary>Los perfiles de estacion sobre EF Core y SQLite.</summary>
/// <param name="contexto">Contexto del cuaderno.</param>
public sealed class RepositorioEstacion(ContextoCuaderno contexto) : IRepositorioEstacion
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<Estacion>> TodasAsync(
        bool soloActivas = true,
        CancellationToken ct = default) =>
        await contexto.Estaciones
            .AsNoTracking()
            .Where(e => !soloActivas || e.Activo)
            .OrderByDescending(e => e.Predeterminado)
            .ThenBy(e => e.NombrePerfil)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<Estacion?> ObtenerAsync(long id, CancellationToken ct = default) =>
        await contexto.Estaciones.FirstOrDefaultAsync(e => e.Id == id, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<Estacion?> PredeterminadaAsync(CancellationToken ct = default)
    {
        var marcada = await contexto.Estaciones
            .AsNoTracking()
            .Where(e => e.Activo && e.Predeterminado)
            .OrderBy(e => e.Id)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (marcada is not null)
        {
            return marcada;
        }

        // Si no hay ninguno marcado pero solo existe un perfil activo, ese es el que vale.
        var activos = await contexto.Estaciones
            .AsNoTracking()
            .Where(e => e.Activo)
            .Take(2)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return activos.Count == 1 ? activos[0] : null;
    }

    /// <inheritdoc/>
    public async Task<long> AnadirAsync(Estacion estacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(estacion);

        contexto.Estaciones.Add(estacion);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);

        if (estacion.Predeterminado)
        {
            await EstablecerPredeterminadaAsync(estacion.Id, ct).ConfigureAwait(false);
        }

        return estacion.Id;
    }

    /// <inheritdoc/>
    public async Task ActualizarAsync(Estacion estacion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(estacion);

        var existente = await contexto.Estaciones
            .FirstOrDefaultAsync(e => e.Id == estacion.Id, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No se puede actualizar: no existe ningún perfil de estación con identificador {estacion.Id}.");

        if (!ReferenceEquals(existente, estacion))
        {
            contexto.Entry(existente).CurrentValues.SetValues(estacion);
        }

        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task EstablecerPredeterminadaAsync(long id, CancellationToken ct = default)
    {
        var transaccion = await contexto.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            var marcados = await contexto.Estaciones
                .Where(e => e.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Predeterminado, true), ct)
                .ConfigureAwait(false);

            if (marcados == 0)
            {
                throw new InvalidOperationException(
                    $"No existe ningún perfil de estación con identificador {id}.");
            }

            await contexto.Estaciones
                .Where(e => e.Id != id && e.Predeterminado)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Predeterminado, false), ct)
                .ConfigureAwait(false);

            await transaccion.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await transaccion.DisposeAsync().ConfigureAwait(false);
        }

        // Las escrituras directas no pasan por el rastreador: se refresca lo que estuviera cargado.
        foreach (var entrada in contexto.ChangeTracker.Entries<Estacion>().ToList())
        {
            entrada.Entity.Predeterminado = entrada.Entity.Id == id;
            entrada.State = EntityState.Unchanged;
        }
    }
}
