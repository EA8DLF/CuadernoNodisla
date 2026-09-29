using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Datos con los que se pide abrir una ronda de control.</summary>
public sealed record PeticionDeRonda
{
    public required string Nombre { get; init; }
    public string? ClubOEvento { get; init; }
    public Banda Band { get; init; }
    public Modo Mode { get; init; }
    public Frecuencia Freq { get; init; }
    public long? EstacionId { get; init; }
    public string? Notas { get; init; }
}

/// <summary>Resultado de anadir un indicativo a la ronda.</summary>
public sealed record ResultadoDeParticipante
{
    public bool Correcto => Error is null;

    public ParticipanteDeRonda? Participante { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Abre, alimenta y cierra una ronda de control (NET Control): la lista de participantes que
/// van entrando en una red de radioaficionados, y la conversion de cada uno en un contacto real
/// del cuaderno cuando el director lo confirma.
/// </summary>
/// <remarks>
/// <para>
/// Solo puede haber <b>una ronda abierta a la vez</b>. Es una decision deliberada: una red es
/// una sesion de radio, no una coleccion de listas paralelas, y el original tambien la reabre
/// sola al arrancar si quedo una a medias. Aqui esa reapertura no es un fichero de estado
/// aparte: es preguntar al repositorio si hay una ronda sin cerrar, que es donde vive la verdad.
/// </para>
/// <para>
/// Marcar un participante como trabajado <b>no borra la fila de la ronda</b>: se queda con su
/// marca y su enlace al QSO nuevo, para que la lista siga contando la sesion entera aunque el
/// contacto ya este en el cuaderno.
/// </para>
/// </remarks>
public sealed class GestionarRonda(
    IRepositorioRondas repositorioRondas,
    RegistrarQso registrarQso,
    IResolutorDxcc? resolutorDxcc = null)
{
    /// <summary>
    /// Abre una ronda nueva. Si ya hay una abierta, no crea otra: devuelve la que estaba.
    /// </summary>
    public async Task<RondaDeControl> AbrirAsync(PeticionDeRonda peticion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(peticion);

        var abierta = await repositorioRondas.ObtenerAbiertaAsync(ct).ConfigureAwait(false);
        if (abierta is not null) return abierta;

        var ronda = new RondaDeControl
        {
            Nombre = peticion.Nombre.Trim(),
            ClubOEvento = string.IsNullOrWhiteSpace(peticion.ClubOEvento) ? null : peticion.ClubOEvento.Trim(),
            Band = peticion.Band,
            Mode = peticion.Mode,
            Freq = peticion.Freq,
            EstacionId = peticion.EstacionId,
            Notas = string.IsNullOrWhiteSpace(peticion.Notas) ? null : peticion.Notas.Trim(),
            InicioUtc = DateTimeOffset.UtcNow,
        };

        var id = await repositorioRondas.AbrirAsync(ronda, ct).ConfigureAwait(false);
        ronda.Id = id;
        return ronda;
    }

    /// <summary>La ronda abierta, si hay alguna. Es lo que permite reabrirla sola al arrancar.</summary>
    public Task<RondaDeControl?> ObtenerAbiertaAsync(CancellationToken ct = default) =>
        repositorioRondas.ObtenerAbiertaAsync(ct);

    /// <summary>Todas las rondas, para el historial.</summary>
    public Task<IReadOnlyList<RondaDeControl>> ListarAsync(CancellationToken ct = default) =>
        repositorioRondas.ListarAsync(ct);

    /// <summary>Cierra la ronda. Los participantes ya anadidos quedan tal y como estaban.</summary>
    public Task CerrarAsync(long rondaId, CancellationToken ct = default) =>
        repositorioRondas.CerrarAsync(rondaId, DateTimeOffset.UtcNow, ct);

    /// <summary>
    /// Anade un participante por su indicativo. Rellena la entidad DXCC y el pais si hay un
    /// resolutor disponible; sin el, el participante entra igual, solo que sin esos datos.
    /// </summary>
    public async Task<ResultadoDeParticipante> AnadirParticipanteAsync(
        long rondaId,
        string indicativoTexto,
        CancellationToken ct = default)
    {
        if (!Indicativo.TryParse(indicativoTexto, out var indicativo))
        {
            return new ResultadoDeParticipante { Error = $"«{indicativoTexto}» no es un indicativo válido." };
        }

        var participante = new ParticipanteDeRonda
        {
            RondaId = rondaId,
            Call = indicativo,
            EntradaUtc = DateTimeOffset.UtcNow,
        };

        if (resolutorDxcc is not null)
        {
            var resultado = resolutorDxcc.Resolver(indicativo, DateOnly.FromDateTime(DateTime.UtcNow));
            if (resultado.Entidad is { } entidad)
            {
                participante.Dxcc = entidad.Numero;
                participante.Pais = entidad.NombreEspanol ?? entidad.Nombre;
            }
        }

        var id = await repositorioRondas.AnadirParticipanteAsync(participante, ct).ConfigureAwait(false);
        participante.Id = id;
        return new ResultadoDeParticipante { Participante = participante };
    }

    /// <summary>Actualiza el RST enviado/recibido y el comentario de un participante.</summary>
    public Task ActualizarParticipanteAsync(ParticipanteDeRonda participante, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(participante);
        return repositorioRondas.ActualizarParticipanteAsync(participante, ct);
    }

    /// <summary>Quita un participante de la ronda sin dejar rastro en el cuaderno.</summary>
    public Task EliminarParticipanteAsync(long participanteId, CancellationToken ct = default) =>
        repositorioRondas.EliminarParticipanteAsync(participanteId, ct);

    /// <summary>
    /// Confirma un participante como contacto trabajado: crea el QSO real en el cuaderno con la
    /// banda, el modo y la frecuencia de la ronda, y su hora de entrada, y deja el participante
    /// marcado y enlazado a ese contacto.
    /// </summary>
    /// <remarks>
    /// Se admite el duplicado a proposito, igual que en los modos digitales: si el mismo
    /// indicativo entra dos veces en la misma ronda —cosa que pasa, la gente vuelve a llamar—
    /// el director decide si lo confirma otra vez, no se lo impide el cuaderno.
    /// </remarks>
    public async Task<ResultadoDeRegistro> MarcarTrabajadoAsync(
        RondaDeControl ronda,
        ParticipanteDeRonda participante,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ronda);
        ArgumentNullException.ThrowIfNull(participante);

        var qso = new Qso
        {
            Call = participante.Call,
            Band = ronda.Band,
            Mode = ronda.Mode,
            Freq = ronda.Freq,
            InicioUtc = participante.EntradaUtc,
            RstSent = participante.RstEnviado,
            RstRcvd = participante.RstRecibido,
            Comentario = participante.Comentario,
            Dxcc = participante.Dxcc ?? 0,
            Country = participante.Pais,
            Origen = "ronda",
        };

        var resultado = await registrarQso
            .EjecutarAsync(
                new PeticionDeRegistro { Qso = qso, EstacionId = ronda.EstacionId, AdmitirDuplicado = true },
                ct)
            .ConfigureAwait(false);

        if (!resultado.Correcto) return resultado;

        participante.Trabajado = true;
        participante.QsoId = resultado.Id;
        await repositorioRondas.ActualizarParticipanteAsync(participante, ct).ConfigureAwait(false);

        return resultado;
    }
}
