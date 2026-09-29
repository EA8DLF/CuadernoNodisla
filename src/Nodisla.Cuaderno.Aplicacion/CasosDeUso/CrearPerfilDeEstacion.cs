using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using DominioIndicativo = Nodisla.Cuaderno.Dominio.Valores.Indicativo;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Datos minimos con los que se crea un perfil de estacion.</summary>
public sealed record PeticionDePerfil
{
    /// <summary>Indicativo con el que se transmite. Es lo unico imprescindible.</summary>
    public required string Indicativo { get; init; }

    /// <summary>Nombre con el que se elige el perfil. Si falta se llama «Casa».</summary>
    public string? NombrePerfil { get; init; }

    /// <summary>Localizador de la estacion.</summary>
    public string? Localizador { get; init; }

    /// <summary>Nombre del operador.</summary>
    public string? NombreOperador { get; init; }

    /// <summary>Localidad desde la que se opera.</summary>
    public string? Localidad { get; init; }
}

/// <summary>Resultado de crear un perfil de estacion.</summary>
public sealed record ResultadoDePerfil
{
    /// <summary>El perfil se ha creado.</summary>
    public bool Correcto => Errores.Count == 0;

    /// <summary>Motivos por los que no se ha podido crear, en espanol y para el operador.</summary>
    public IReadOnlyList<string> Errores { get; init; } = [];

    /// <summary>Perfil creado, ya con su identificador.</summary>
    public Estacion? Creado { get; init; }
}

/// <summary>
/// Crea el perfil de estacion del primer arranque. Sin un perfil no se puede registrar nada:
/// ADIF exige <c>STATION_CALLSIGN</c> y un cuaderno sin el no sirve para pedir diplomas.
/// </summary>
public sealed class CrearPerfilDeEstacion(IRepositorioEstacion repositorio)
{
    /// <summary>Nombre que se le pone al perfil si el operador no escribe ninguno.</summary>
    public const string NombrePorOmision = "Casa";

    /// <summary>Indica si ya hay algun perfil de estacion dado de alta.</summary>
    public async Task<bool> HayAlgunoAsync(CancellationToken ct = default)
    {
        var perfiles = await repositorio.TodasAsync(soloActivas: false, ct).ConfigureAwait(false);
        return perfiles.Count > 0;
    }

    /// <summary>Crea el perfil y lo deja como predeterminado.</summary>
    public async Task<ResultadoDePerfil> EjecutarAsync(PeticionDePerfil peticion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(peticion);

        var errores = new List<string>();

        if (!DominioIndicativo.TryParse(peticion.Indicativo, out var indicativo))
        {
            errores.Add(string.IsNullOrWhiteSpace(peticion.Indicativo)
                ? "Falta el indicativo de la estación."
                : $"El indicativo «{peticion.Indicativo}» no tiene una forma válida.");
        }

        var localizador = Locator.Vacio;
        if (!string.IsNullOrWhiteSpace(peticion.Localizador)
            && !Locator.TryParse(peticion.Localizador, out localizador))
        {
            errores.Add($"El localizador «{peticion.Localizador}» no es válido. Ejemplo: IL18QK.");
        }

        if (errores.Count > 0) return new ResultadoDePerfil { Errores = errores };

        var estacion = new Estacion
        {
            NombrePerfil = string.IsNullOrWhiteSpace(peticion.NombrePerfil)
                ? NombrePorOmision
                : peticion.NombrePerfil.Trim(),
            StationCallsign = indicativo,
            Operator = Vacio(peticion.NombreOperador) is { } op ? op : indicativo.Valor,
            OwnerCallsign = indicativo.Valor,
            MyName = Vacio(peticion.NombreOperador),
            MyCity = Vacio(peticion.Localidad),
            MyGridsquare = localizador,
            Predeterminado = true,
            Activo = true,
        };

        var id = await repositorio.AnadirAsync(estacion, ct).ConfigureAwait(false);
        estacion.Id = id;
        await repositorio.EstablecerPredeterminadaAsync(id, ct).ConfigureAwait(false);

        return new ResultadoDePerfil { Creado = estacion };
    }

    private static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
