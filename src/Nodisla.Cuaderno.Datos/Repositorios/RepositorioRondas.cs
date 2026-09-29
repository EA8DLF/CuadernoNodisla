using Microsoft.EntityFrameworkCore;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Datos.Repositorios;

/// <summary>Las rondas de control sobre EF Core y SQLite.</summary>
/// <param name="contexto">Contexto del cuaderno.</param>
public sealed class RepositorioRondas(ContextoCuaderno contexto) : IRepositorioRondas
{
    /// <inheritdoc/>
    public async Task<long> AbrirAsync(RondaDeControl ronda, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ronda);
        contexto.Rondas.Add(ronda);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        return ronda.Id;
    }

    /// <inheritdoc/>
    public async Task<RondaDeControl?> ObtenerAbiertaAsync(CancellationToken ct = default) =>
        await ConParticipantes(contexto.Rondas)
            .Where(r => r.FinUtc == null)
            .OrderByDescending(r => r.InicioUtc)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task<RondaDeControl?> ObtenerAsync(long id, CancellationToken ct = default) =>
        await ConParticipantes(contexto.Rondas)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    /// <remarks>
    /// Trae tambien los participantes: el historial es para poder decir «cuantas rondas y
    /// cuantos participantes», y las rondas de una vida entera de red no llegan ni de lejos al
    /// volumen del cuaderno como para que este <c>Include</c> pese.
    /// </remarks>
    public async Task<IReadOnlyList<RondaDeControl>> ListarAsync(CancellationToken ct = default) =>
        await ConParticipantes(contexto.Rondas)
            .AsNoTracking()
            .OrderByDescending(r => r.InicioUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    public async Task CerrarAsync(long rondaId, DateTimeOffset finUtc, CancellationToken ct = default)
    {
        var ronda = await contexto.Rondas.FirstOrDefaultAsync(r => r.Id == rondaId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No existe ninguna ronda con identificador {rondaId}.");

        ronda.FinUtc = finUtc;
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<long> AnadirParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(participante);
        contexto.ParticipantesDeRonda.Add(participante);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
        return participante.Id;
    }

    /// <inheritdoc/>
    public async Task ActualizarParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(participante);

        var existente = await contexto.ParticipantesDeRonda
            .FirstOrDefaultAsync(p => p.Id == participante.Id, ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No se puede actualizar: no existe ningún participante con identificador {participante.Id}.");

        if (!ReferenceEquals(existente, participante))
        {
            contexto.Entry(existente).CurrentValues.SetValues(participante);
        }

        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task EliminarParticipanteAsync(long participanteId, CancellationToken ct = default)
    {
        // Se borra por seguimiento y no con ExecuteDeleteAsync a proposito: el borrado masivo
        // no pasa por el rastreador y, si la ronda seguia cargada en el mismo contexto —como
        // pasa nada mas anadir un participante y volver a leer la ronda—, la coleccion
        // Participantes se quedaba con la fila fantasma en memoria aunque ya no estuviera en
        // la base.
        var participante = await contexto.ParticipantesDeRonda
            .FirstOrDefaultAsync(p => p.Id == participanteId, ct)
            .ConfigureAwait(false);

        if (participante is null) return;

        contexto.ParticipantesDeRonda.Remove(participante);
        await contexto.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static IQueryable<RondaDeControl> ConParticipantes(IQueryable<RondaDeControl> consulta) => consulta
        .Include(r => r.Participantes.OrderBy(p => p.EntradaUtc));
}
