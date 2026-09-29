using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas.Dobles;

/// <summary>Rondas de control en memoria para las pruebas.</summary>
public sealed class RepositorioRondasDoble : IRepositorioRondas
{
    private readonly List<RondaDeControl> _rondas = [];
    private readonly List<ParticipanteDeRonda> _participantes = [];
    private long _siguienteIdDeRonda = 1;
    private long _siguienteIdDeParticipante = 1;

    /// <summary>Veces que se ha llamado a <see cref="ActualizarParticipanteAsync"/>.</summary>
    public int ActualizacionesDeParticipante { get; private set; }

    public Task<long> AbrirAsync(RondaDeControl ronda, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ronda);
        ronda.Id = _siguienteIdDeRonda++;
        _rondas.Add(ronda);
        return Task.FromResult(ronda.Id);
    }

    public Task<RondaDeControl?> ObtenerAbiertaAsync(CancellationToken ct = default) =>
        Task.FromResult(_rondas
            .Where(r => r.FinUtc is null)
            .OrderByDescending(r => r.InicioUtc)
            .Select(ConParticipantes)
            .FirstOrDefault());

    public Task<RondaDeControl?> ObtenerAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(_rondas.Where(r => r.Id == id).Select(ConParticipantes).FirstOrDefault());

    public Task<IReadOnlyList<RondaDeControl>> ListarAsync(CancellationToken ct = default)
    {
        IReadOnlyList<RondaDeControl> resultado = _rondas.OrderByDescending(r => r.InicioUtc).ToList();
        return Task.FromResult(resultado);
    }

    public Task CerrarAsync(long rondaId, DateTimeOffset finUtc, CancellationToken ct = default)
    {
        var ronda = _rondas.FirstOrDefault(r => r.Id == rondaId)
            ?? throw new InvalidOperationException($"No existe ninguna ronda con identificador {rondaId}.");
        ronda.FinUtc = finUtc;
        return Task.CompletedTask;
    }

    public Task<long> AnadirParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(participante);
        participante.Id = _siguienteIdDeParticipante++;
        _participantes.Add(participante);
        return Task.FromResult(participante.Id);
    }

    public Task ActualizarParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(participante);
        ActualizacionesDeParticipante++;
        var i = _participantes.FindIndex(p => p.Id == participante.Id);
        if (i >= 0) _participantes[i] = participante;
        return Task.CompletedTask;
    }

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
