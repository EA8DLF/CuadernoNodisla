using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>Rondas de control en memoria, para arrancar la ventana sin la capa de datos.</summary>
public sealed class RepositorioRondasEnMemoria : IRepositorioRondas
{
    private readonly List<RondaDeControl> _rondas = [];
    private readonly List<ParticipanteDeRonda> _participantes = [];
    private long _proximoIdDeRonda = 1;
    private long _proximoIdDeParticipante = 1;

    /// <inheritdoc />
    public Task<long> AbrirAsync(RondaDeControl ronda, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ronda);
        ronda.Id = _proximoIdDeRonda++;
        _rondas.Add(ronda);
        return Task.FromResult(ronda.Id);
    }

    /// <inheritdoc />
    public Task<RondaDeControl?> ObtenerAbiertaAsync(CancellationToken ct = default) =>
        Task.FromResult(_rondas
            .Where(r => r.FinUtc is null)
            .OrderByDescending(r => r.InicioUtc)
            .Select(ConParticipantes)
            .FirstOrDefault());

    /// <inheritdoc />
    public Task<RondaDeControl?> ObtenerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_rondas.Where(r => r.Id == id).Select(ConParticipantes).FirstOrDefault());

    /// <inheritdoc />
    public Task<IReadOnlyList<RondaDeControl>> ListarAsync(CancellationToken ct = default)
    {
        IReadOnlyList<RondaDeControl> resultado = _rondas
            .OrderByDescending(r => r.InicioUtc)
            .Select(ConParticipantes)
            .ToList();
        return Task.FromResult(resultado);
    }

    /// <inheritdoc />
    public Task CerrarAsync(long rondaId, DateTimeOffset finUtc, CancellationToken ct = default)
    {
        var ronda = _rondas.FirstOrDefault(r => r.Id == rondaId)
            ?? throw new InvalidOperationException($"No existe ninguna ronda con identificador {rondaId}.");
        ronda.FinUtc = finUtc;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> AnadirParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(participante);
        participante.Id = _proximoIdDeParticipante++;
        _participantes.Add(participante);
        return Task.FromResult(participante.Id);
    }

    /// <inheritdoc />
    public Task ActualizarParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(participante);
        var i = _participantes.FindIndex(p => p.Id == participante.Id);
        if (i >= 0) _participantes[i] = participante;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EliminarParticipanteAsync(long participanteId, CancellationToken ct = default)
    {
        _participantes.RemoveAll(p => p.Id == participanteId);
        return Task.CompletedTask;
    }

    private RondaDeControl ConParticipantes(RondaDeControl ronda)
    {
        ronda.Participantes.Clear();
        ronda.Participantes.AddRange(
            _participantes.Where(p => p.RondaId == ronda.Id).OrderBy(p => p.EntradaUtc));
        return ronda;
    }
}
