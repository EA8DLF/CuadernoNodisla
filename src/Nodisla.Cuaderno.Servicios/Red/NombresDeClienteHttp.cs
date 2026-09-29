namespace Nodisla.Cuaderno.Servicios.Red;

/// <summary>
/// Nombres de los clientes HTTP registrados en la fabrica.
/// </summary>
/// <remarks>
/// Cada servicio tiene su cliente con nombre y su propio tiempo de espera. Se usa
/// <c>IHttpClientFactory</c> y nunca un <c>HttpClient</c> nuevo por llamada: eso agota los
/// puertos efimeros de la maquina y ademas no se entera de los cambios de DNS.
/// </remarks>
public static class NombresDeClienteHttp
{
    /// <summary>Cliente de LoTW (ARRL).</summary>
    public const string Lotw = "lotw";

    /// <summary>Cliente de eQSL.cc.</summary>
    public const string Eqsl = "eqsl";

    /// <summary>Cliente de Club Log.</summary>
    public const string ClubLog = "clublog";

    /// <summary>Cliente de QRZ.com, tanto la consulta XML como el cuaderno.</summary>
    public const string Qrz = "qrz";

    /// <summary>Cliente de HamQTH.</summary>
    public const string HamQth = "hamqth";

    /// <summary>Identificacion del programa que se envia en la cabecera <c>User-Agent</c>.</summary>
    /// <remarks>
    /// QRZ.com exige un agente identificable y limita a los genericos; los demas lo agradecen
    /// cuando hay que depurar un problema con el operador al telefono.
    /// </remarks>
    public const string AgenteDeUsuario = "CuadernoNODISLA/0.1";
}
