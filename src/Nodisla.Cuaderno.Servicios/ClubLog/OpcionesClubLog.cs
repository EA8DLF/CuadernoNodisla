namespace Nodisla.Cuaderno.Servicios.ClubLog;

/// <summary>Ajustes de Club Log.</summary>
/// <remarks>
/// <para>
/// Ni la contrasena ni la clave de API estan aqui. Ese es justo el defecto del programa
/// original: el <c>config.ini</c> de Log4OM lleva la clave de API de Club Log en claro, a la
/// vista de cualquiera que abra el fichero. Aqui las dos van cifradas en el almacen de
/// credenciales y no se escriben nunca en un fichero de ajustes.
/// </para>
/// <para>
/// La clave de API se pide al soporte de Club Log y es propia de cada programa; no se comparte
/// ni se reutiliza la de otro.
/// </para>
/// </remarks>
public sealed record OpcionesClubLog
{
    /// <summary>
    /// Correo con el que esta registrada la cuenta en Club Log. Club Log identifica por correo,
    /// no por indicativo.
    /// </summary>
    public string Correo { get; init; } = string.Empty;

    /// <summary>Indicativo del cuaderno al que subir, que debe pertenecer a esa cuenta.</summary>
    public string Indicativo { get; init; } = string.Empty;

    /// <summary>Direccion de subida de ficheros completos.</summary>
    public Uri UrlDeSubida { get; init; } = new("https://clublog.org/putlogs.php");

    /// <summary>Direccion de subida de contactos sueltos, segun se registran.</summary>
    public Uri UrlEnDirecto { get; init; } = new("https://clublog.org/realtime.php");
}
