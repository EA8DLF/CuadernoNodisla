using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Acceso a las rondas de control (NET Control) y sus participantes.</summary>
public interface IRepositorioRondas
{
    /// <summary>Abre una ronda nueva y devuelve su identificador.</summary>
    Task<long> AbrirAsync(RondaDeControl ronda, CancellationToken ct = default);

    /// <summary>
    /// La ronda abierta, con sus participantes, si hay alguna. Solo puede haber una a la vez:
    /// es lo que permite reabrirla sola al reiniciar el programa.
    /// </summary>
    Task<RondaDeControl?> ObtenerAbiertaAsync(CancellationToken ct = default);

    /// <summary>Una ronda por su identificador, con sus participantes.</summary>
    Task<RondaDeControl?> ObtenerAsync(long id, CancellationToken ct = default);

    /// <summary>Todas las rondas, de la mas reciente a la mas antigua, sin sus participantes.</summary>
    Task<IReadOnlyList<RondaDeControl>> ListarAsync(CancellationToken ct = default);

    /// <summary>Cierra la ronda, dejando constancia del instante de cierre.</summary>
    Task CerrarAsync(long rondaId, DateTimeOffset finUtc, CancellationToken ct = default);

    /// <summary>Anade un participante a la ronda y devuelve su identificador.</summary>
    Task<long> AnadirParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default);

    /// <summary>Actualiza los datos de un participante ya anadido (RST, comentario, trabajado…).</summary>
    Task ActualizarParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default);

    /// <summary>Quita un participante de la ronda.</summary>
    Task EliminarParticipanteAsync(long participanteId, CancellationToken ct = default);
}
