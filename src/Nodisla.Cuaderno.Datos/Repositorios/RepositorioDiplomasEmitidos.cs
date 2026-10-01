using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Repositorios;

/// <summary>El historial de diplomas emitidos sobre EF Core y SQLite.</summary>
/// <param name="contexto">Contexto del cuaderno.</param>
public sealed class RepositorioDiplomasEmitidos(ContextoCuaderno contexto) : IRepositorioDiplomasEmitidos
{
    private const int Intentos = 3;

    /// <inheritdoc/>
    public async Task<int> SiguienteNumeroAsync(string serie, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serie);
        var ultimo = await contexto.DiplomasEmitidos
            .Where(d => d.Serie == serie)
            .MaxAsync(d => (int?)d.Numero, ct)
            .ConfigureAwait(false);
        return (ultimo ?? 0) + 1;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Leer el ultimo numero y apuntar el nuevo van en la misma transaccion; si aun asi otra
    /// emision se cuela (otra ventana, otro proceso), el indice unico de serie y numero rechaza
    /// el duplicado y se vuelve a intentar con el siguiente.
    /// </remarks>
    public async Task<DiplomaEmitido> EmitirAsync(DiplomaEmitido diploma, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(diploma);
        ArgumentException.ThrowIfNullOrWhiteSpace(diploma.Serie);

        for (var intento = 1; ; intento++)
        {
            await using var transaccion = await contexto.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            try
            {
                diploma.Id = 0;
                diploma.Numero = await SiguienteNumeroAsync(diploma.Serie, ct).ConfigureAwait(false);
                contexto.DiplomasEmitidos.Add(diploma);
                await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
                await transaccion.CommitAsync(ct).ConfigureAwait(false);
                contexto.Entry(diploma).State = EntityState.Detached;
                return diploma;
            }
            catch (DbUpdateException) when (intento < Intentos)
            {
                await transaccion.RollbackAsync(ct).ConfigureAwait(false);
                contexto.Entry(diploma).State = EntityState.Detached;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DiplomaEmitido>> ListarAsync(int limite = 500, CancellationToken ct = default)
    {
        // SQLite no ordena DateTimeOffset en el servidor: la fecha se guarda como texto ISO, que
        // ordena igual, pero EF no lo sabe. Se ordena por identificador, que crece con el tiempo.
        return await contexto.DiplomasEmitidos
            .AsNoTracking()
            .OrderByDescending(d => d.Id)
            .Take(Math.Max(1, limite))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<DiplomaEmitido?> ObtenerAsync(long id, CancellationToken ct = default) =>
        await contexto.DiplomasEmitidos.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task MarcarEnviadoAsync(long id, string correo, DateTimeOffset cuando, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correo);
        var diploma = await contexto.DiplomasEmitidos.FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No existe ningún diploma emitido con identificador {id}.");
        diploma.Correo = correo.Trim();
        diploma.EnviadoUtc = cuando;
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        contexto.Entry(diploma).State = EntityState.Detached;
    }
}
