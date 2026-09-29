using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Entidades;

/// <summary>
/// Una ronda de control: una red de radioaficionados con un director, una lista de
/// participantes que van entrando y saliendo, y un QSO activo que se edita sobre la marcha.
/// </summary>
/// <remarks>
/// Es la funcionalidad de «NET Control» del original (inventario funcional, punto 23). No es
/// un modo del cuaderno ni un tipo de QSO: es una sesion aparte que, participante a
/// participante, va alimentando el cuaderno de verdad a traves de <see cref="ParticipanteDeRonda"/>.
/// </remarks>
public sealed class RondaDeControl
{
    public long Id { get; set; }

    /// <summary>Nombre de la ronda, por ejemplo «Red de los domingos EA8».</summary>
    public required string Nombre { get; set; }

    /// <summary>Club o evento al que esta ligada la ronda, si lo hay.</summary>
    public string? ClubOEvento { get; set; }

    /// <summary>Banda en la que se celebra.</summary>
    public Banda Band { get; set; }

    /// <summary>Modo en el que se celebra.</summary>
    public Modo Mode { get; set; }

    /// <summary>Frecuencia de la ronda, para dejarla en cada contacto que se confirme.</summary>
    public Frecuencia Freq { get; set; }

    /// <summary>Perfil de estacion con el que se dirige la ronda.</summary>
    public long? EstacionId { get; set; }

    /// <summary>Notas libres del director de la red.</summary>
    public string? Notas { get; set; }

    /// <summary>Instante en que se abrio la ronda.</summary>
    public DateTimeOffset InicioUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Instante en que se cerro. Nulo mientras sigue abierta.</summary>
    public DateTimeOffset? FinUtc { get; set; }

    /// <summary>La ronda sigue abierta.</summary>
    public bool EstaAbierta => FinUtc is null;

    /// <summary>Participantes de la ronda, en el orden en que fueron entrando.</summary>
    public List<ParticipanteDeRonda> Participantes { get; } = [];

    /// <summary>Cuantos participantes se han marcado como trabajados.</summary>
    public int Trabajados => Participantes.Count(p => p.Trabajado);
}
