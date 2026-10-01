using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Datos con los que se pide modificar un contacto que ya esta en el cuaderno.</summary>
public sealed record PeticionDeEdicion
{
    /// <summary>Contacto con los cambios ya aplicados. Ha de traer su <see cref="Qso.Id"/>.</summary>
    public required Qso Qso { get; init; }

    /// <summary>Perfil de estacion a reaplicar. Si es nulo se respeta el que ya tenia.</summary>
    public long? EstacionId { get; init; }
}

/// <summary>Resultado de intentar modificar un contacto.</summary>
public sealed record ResultadoDeEdicion
{
    /// <summary>Los cambios se han guardado.</summary>
    public bool Correcto => Errores.Count == 0 && !NoEncontrado;

    /// <summary>No existe ningun contacto con ese identificador.</summary>
    public bool NoEncontrado { get; init; }

    /// <summary>Motivos por los que no se ha podido guardar, en espanol y para el operador.</summary>
    public IReadOnlyList<string> Errores { get; init; } = [];

    /// <summary>Contacto tal y como ha quedado guardado.</summary>
    public Qso? Guardado { get; init; }
}

/// <summary>Modifica un contacto existente conservando lo que no se toca desde la interfaz.</summary>
public sealed class EditarQso(
    IRepositorioQso repositorioQso,
    IRepositorioEstacion repositorioEstacion,
    AvisosDeQsos? avisos = null)
{
    /// <summary>Guarda los cambios del contacto de la peticion.</summary>
    public async Task<ResultadoDeEdicion> EjecutarAsync(PeticionDeEdicion peticion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        var qso = peticion.Qso;

        if (qso.Id <= 0)
        {
            return new ResultadoDeEdicion { Errores = ["El contacto que se intenta modificar no tiene identificador."] };
        }

        var existente = await repositorioQso.ObtenerAsync(qso.Id, ct).ConfigureAwait(false);
        if (existente is null) return new ResultadoDeEdicion { NoEncontrado = true };

        // Lo que identifica al contacto entre bases de datos no cambia nunca al editarlo.
        qso.Uuid = existente.Uuid;
        qso.CreadoUtc = existente.CreadoUtc;
        qso.Origen ??= existente.Origen;

        if (peticion.EstacionId is { } id)
        {
            var estacion = await repositorioEstacion.ObtenerAsync(id, ct).ConfigureAwait(false);
            estacion?.AplicarA(qso);
        }

        RegistrarQso.AplicarValoresPorOmision(qso);

        var errores = RegistrarQso.Validar(qso);
        if (errores.Count > 0) return new ResultadoDeEdicion { Errores = errores };

        qso.ModificadoUtc = DateTimeOffset.UtcNow;
        await repositorioQso.ActualizarAsync(qso, ct).ConfigureAwait(false);
        avisos?.Avisar(qso, TipoDeGuardado.Modificado);
        return new ResultadoDeEdicion { Guardado = qso };
    }
}
