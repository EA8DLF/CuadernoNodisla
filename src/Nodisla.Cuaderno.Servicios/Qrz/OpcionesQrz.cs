namespace Nodisla.Cuaderno.Servicios.Qrz;

/// <summary>
/// Ajustes de QRZ.com.
/// </summary>
/// <remarks>
/// QRZ.com son <b>dos servicios distintos con credenciales distintas</b> y conviene no
/// mezclarlos: la consulta de indicativos por XML va con el usuario y la contrasena de la
/// cuenta, y el cuaderno en linea va con una clave de cuaderno que se genera aparte en la web.
/// Tener una no implica tener la otra.
/// </remarks>
public sealed record OpcionesQrz
{
    /// <summary>Usuario de QRZ.com para la consulta de indicativos.</summary>
    public string Usuario { get; init; } = string.Empty;

    /// <summary>Direccion del servicio XML de consulta de indicativos.</summary>
    public Uri UrlDeConsulta { get; init; } = new("https://xmldata.qrz.com/xml/current/");

    /// <summary>Direccion de la API del cuaderno en linea.</summary>
    public Uri UrlDelCuaderno { get; init; } = new("https://logbook.qrz.com/api");

    /// <summary>
    /// Nombre con el que el programa se identifica ante QRZ.com. QRZ lo exige y limita a los
    /// programas que se presentan con un nombre generico.
    /// </summary>
    public string Agente { get; init; } = "CuadernoNODISLA-0.1";

    /// <summary>Cuantos contactos pide por pagina al descargar del cuaderno.</summary>
    public int TamanoDePagina { get; init; } = 250;
}
