namespace Nodisla.Cuaderno.Servicios.Actualizaciones;

/// <summary>
/// Donde vive el programa en GitHub. Un solo sitio para las direcciones, para que el aviso de
/// versiones y el informe de fallos no se desincronicen si el repositorio cambia de nombre.
/// </summary>
/// <remarks>
/// <b>Nada de fichas de acceso.</b> Todo lo que hace el programa contra GitHub es anonimo: leer
/// la ultima publicacion por la API publica y abrir en el navegador la pagina de «nueva
/// incidencia», donde el operador entra con su propia cuenta. Un token dentro del ejecutable
/// lo puede sacar cualquiera que lo descargue.
/// </remarks>
public sealed record RepositorioPublico(string Propietario, string Nombre)
{
    /// <summary>El repositorio de Cuaderno NODISLA.</summary>
    public static RepositorioPublico CuadernoNodisla { get; } = new("EA8DLF", "CuadernoNodisla");

    /// <summary>API: ultima publicacion estable (GitHub ya excluye borradores y previas).</summary>
    public Uri UltimaPublicacion => new($"https://api.github.com/repos/{Propietario}/{Nombre}/releases/latest");

    /// <summary>Pagina de «nueva incidencia».</summary>
    public string NuevaIncidencia => $"https://github.com/{Propietario}/{Nombre}/issues/new";

    /// <summary>Pagina de publicaciones.</summary>
    public Uri Publicaciones => new($"https://github.com/{Propietario}/{Nombre}/releases");

    /// <summary>
    /// Prefijo que tiene que tener toda descarga. Una respuesta de la API que apunte a otro
    /// sitio no se descarga.
    /// </summary>
    public string PrefijoDeDescargas => $"https://github.com/{Propietario}/{Nombre}/releases/download/";
}
