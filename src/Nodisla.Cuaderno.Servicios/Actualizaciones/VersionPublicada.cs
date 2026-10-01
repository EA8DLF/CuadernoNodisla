namespace Nodisla.Cuaderno.Servicios.Actualizaciones;

/// <summary>Una version publicada en GitHub, con lo necesario para instalarla.</summary>
/// <param name="Version">Numero de la version.</param>
/// <param name="Etiqueta">Etiqueta de la publicacion, tal cual (<c>v0.2.0</c>).</param>
/// <param name="Nombre">Titulo de la publicacion.</param>
/// <param name="Notas">Notas de la version (Markdown de GitHub, sin procesar).</param>
/// <param name="Pagina">Pagina de la publicacion en GitHub.</param>
/// <param name="Publicada">Cuando se publico.</param>
/// <param name="Instalador">El instalador (.exe), o nulo si la publicacion no trae ninguno.</param>
/// <param name="Suma">El fichero .sha256 del instalador, o nulo si no se publico.</param>
public sealed record VersionPublicada(
    VersionSemantica Version,
    string Etiqueta,
    string Nombre,
    string Notas,
    Uri Pagina,
    DateTimeOffset? Publicada,
    FicheroPublicado? Instalador,
    FicheroPublicado? Suma)
{
    /// <summary>Se puede descargar e instalar desde el programa: hay instalador y suma.</summary>
    public bool SePuedeInstalar => Instalador is not null && Suma is not null;
}

/// <summary>Un fichero adjunto a una publicacion.</summary>
/// <param name="Nombre">Nombre del fichero.</param>
/// <param name="Direccion">Direccion de descarga.</param>
/// <param name="Bytes">Tamano anunciado por GitHub.</param>
public sealed record FicheroPublicado(string Nombre, Uri Direccion, long Bytes);

/// <summary>Que salio de preguntar a GitHub.</summary>
public enum EstadoDeComprobacion
{
    /// <summary>La instalada es la ultima (o mas nueva).</summary>
    AlDia,

    /// <summary>Hay una version mas nueva.</summary>
    HayVersionNueva,

    /// <summary>
    /// No se pudo saber: sin red, repositorio privado (404), limite de peticiones, respuesta
    /// rara. No es un error para el operador: simplemente no hay aviso.
    /// </summary>
    NoDisponible,
}

/// <summary>Resultado de una comprobacion.</summary>
/// <param name="Estado">Que salio.</param>
/// <param name="Nueva">La version nueva, si la hay.</param>
/// <param name="Motivo">Por que no se pudo saber, para el registro y para la comprobacion manual.</param>
public sealed record ResultadoDeComprobacion(
    EstadoDeComprobacion Estado,
    VersionPublicada? Nueva = null,
    string? Motivo = null)
{
    /// <summary>Esta al dia.</summary>
    public static ResultadoDeComprobacion AlDia { get; } = new(EstadoDeComprobacion.AlDia);

    /// <summary>No se pudo saber.</summary>
    public static ResultadoDeComprobacion NoDisponible(string motivo) =>
        new(EstadoDeComprobacion.NoDisponible, Motivo: motivo);
}
