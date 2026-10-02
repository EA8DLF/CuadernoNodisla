using System.Text;
using System.Text.Json;

namespace Nodisla.Cuaderno.Servidores;

/// <summary>
/// Los ajustes del servidor para otros programas, tal y como se guardan.
/// </summary>
/// <remarks>
/// De fabrica esta todo cerrado: los dos servidores apagados, solo en 127.0.0.1 y sin dejar
/// transmitir a nadie de fuera. Se guarda en su propio fichero, <c>servidores-externos.json</c>
/// de la carpeta de datos.
/// </remarks>
public sealed class OpcionesDeServidores
{
    /// <summary>Nombre del fichero en la carpeta de datos.</summary>
    public const string Fichero = "servidores-externos.json";

    /// <summary>El puerto de rigctld de siempre.</summary>
    public const int PuertoRigctldPorOmision = 4532;

    /// <summary>El puerto TCI de partida (Thetis usa 50001, ExpertSDR 40001).</summary>
    public const int PuertoTciPorOmision = 40001;

    /// <summary>Clientes a la vez como mucho, en cada servidor.</summary>
    public const int MaximoDeClientes = 8;

    private static readonly JsonSerializerOptions Formato = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Servidor compatible con rigctld encendido.</summary>
    public bool RigctldActivo { get; set; }

    /// <summary>Puerto TCP del servidor rigctld.</summary>
    public int PuertoRigctld { get; set; } = PuertoRigctldPorOmision;

    /// <summary>Servidor TCI encendido.</summary>
    public bool TciActivo { get; set; }

    /// <summary>Puerto del servidor TCI (WebSocket).</summary>
    public int PuertoTci { get; set; } = PuertoTciPorOmision;

    /// <summary>
    /// Escuchar tambien en la red local y no solo en 127.0.0.1. Aun asi, solo entran las IP de
    /// <see cref="IpsPermitidas"/>.
    /// </summary>
    public bool AbrirALaRedLocal { get; set; }

    /// <summary>IP o redes (192.168.1.0/24) que pueden entrar desde la red local.</summary>
    public List<string> IpsPermitidas { get; set; } = [];

    /// <summary>Los clientes pueden cambiar frecuencia, modo, VFO, split y potencia.</summary>
    public bool PermitirControl { get; set; } = true;

    /// <summary>
    /// Interruptor general: los clientes externos pueden pedir PTT. Apagado de fabrica.
    /// </summary>
    public bool PermitirTx { get; set; }

    /// <summary>Contraseña opcional del TCI; vacia, no se pide.</summary>
    public string? TokenTci { get; set; }

    /// <summary>Mandar a los clientes TCI los spots que recibe el Cuaderno.</summary>
    public bool EnviarSpots { get; set; } = true;

    /// <summary>Pintar en el bandmap los spots que manden los clientes TCI.</summary>
    public bool RecibirSpots { get; set; } = true;

    /// <summary>Deja todo dentro de limites con sentido.</summary>
    /// <returns>Los mismos ajustes.</returns>
    public OpcionesDeServidores Acotar()
    {
        // El 0 («uno libre») solo lo usan las pruebas; la pantalla no deja escribirlo.
        if (PuertoRigctld is (< 1024 and not 0) or > 65535) PuertoRigctld = PuertoRigctldPorOmision;
        if (PuertoTci is (< 1024 and not 0) or > 65535) PuertoTci = PuertoTciPorOmision;
        if (PuertoTci != 0 && PuertoTci == PuertoRigctld)PuertoTci = PuertoRigctld == PuertoTciPorOmision ? PuertoTciPorOmision + 1 : PuertoTciPorOmision;
        IpsPermitidas = (IpsPermitidas ?? [])
            .Select(i => (i ?? string.Empty).Trim())
            .Where(ControlDeAcceso.EntradaValida)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
        TokenTci = string.IsNullOrWhiteSpace(TokenTci) ? null : TokenTci.Trim();
        return this;
    }

    /// <summary>Una copia independiente.</summary>
    /// <returns>La copia.</returns>
    public OpcionesDeServidores Copiar() => new()
    {
        RigctldActivo = RigctldActivo,
        PuertoRigctld = PuertoRigctld,
        TciActivo = TciActivo,
        PuertoTci = PuertoTci,
        AbrirALaRedLocal = AbrirALaRedLocal,
        IpsPermitidas = [.. IpsPermitidas],
        PermitirControl = PermitirControl,
        PermitirTx = PermitirTx,
        TokenTci = TokenTci,
        EnviarSpots = EnviarSpots,
        RecibirSpots = RecibirSpots,
    };

    /// <summary>Lee de la carpeta de datos; sin fichero o con uno roto, los de fabrica (todo cerrado).</summary>
    /// <param name="carpeta">Carpeta de datos.</param>
    /// <returns>Los ajustes.</returns>
    public static OpcionesDeServidores Leer(string? carpeta)
    {
        if (string.IsNullOrEmpty(carpeta)) return new OpcionesDeServidores();
        var ruta = Path.Combine(carpeta, Fichero);
        try
        {
            return File.Exists(ruta)
                ? (JsonSerializer.Deserialize<OpcionesDeServidores>(File.ReadAllText(ruta, Encoding.UTF8), Formato) ?? new()).Acotar()
                : new OpcionesDeServidores();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new OpcionesDeServidores();
        }
    }

    /// <summary>Guarda en la carpeta de datos.</summary>
    /// <param name="carpeta">Carpeta de datos.</param>
    public void Guardar(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, Fichero);
        File.WriteAllText(ruta + ".tmp", JsonSerializer.Serialize(Acotar(), Formato), new UTF8Encoding(false));
        File.Move(ruta + ".tmp", ruta, overwrite: true);
    }
}
