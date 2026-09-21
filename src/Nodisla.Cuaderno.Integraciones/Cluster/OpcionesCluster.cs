using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>
/// Datos para conectarse a un cluster de DX por Telnet.
/// </summary>
/// <remarks>
/// El guion de arranque es el mismo que usa Log4OM, con las mismas marcas
/// <c>&lt;CALLSIGN&gt;</c> y <c>&lt;PASSWORD&gt;</c>, para poder importar sin traducir nada
/// lo que el operador ya tenia configurado.
/// </remarks>
public sealed record OpcionesCluster
{
    /// <summary>Nombre del cluster, el que se ve en pantalla.</summary>
    public required string Nombre { get; init; }

    /// <summary>Maquina a la que conectarse.</summary>
    public required string Servidor { get; init; }

    /// <summary>Puerto de Telnet. Lo habitual es 7300, y algunos nodos usan el 23 o el 8000.</summary>
    public int Puerto { get; init; } = 7300;

    /// <summary>Indicativo con el que se accede.</summary>
    public required Indicativo Indicativo { get; init; }

    /// <summary>
    /// Sufijo del indicativo con el que identificarse, cuando el operador tiene varias
    /// sesiones abiertas. Log4OM lo llama <c>SSID</c>.
    /// </summary>
    public string? Sufijo { get; init; }

    /// <summary>Contrasenia, si el nodo la pide. La mayoria no la pide.</summary>
    public string? Contrasena { get; init; }

    /// <summary>
    /// Ordenes que se envian al conectar. Las marcas <c>&lt;CALLSIGN&gt;</c> y
    /// <c>&lt;PASSWORD&gt;</c> dicen donde van el indicativo y la contrasenia; el resto se
    /// envia tal cual una vez dentro.
    /// </summary>
    public IReadOnlyList<string> GuionDeArranque { get; init; } = GuionPredeterminado;

    /// <summary>
    /// Guion de arranque de Log4OM, copiado de <c>clusterdefaultscript.txt</c>.
    /// </summary>
    public static IReadOnlyList<string> GuionPredeterminado { get; } =
        ["<CALLSIGN>", "<PASSWORD>", "SH/DX 30"];

    /// <summary>Marca del guion que se sustituye por el indicativo.</summary>
    public const string MarcaIndicativo = "<CALLSIGN>";

    /// <summary>Marca del guion que se sustituye por la contrasenia.</summary>
    public const string MarcaContrasena = "<PASSWORD>";

    /// <summary>Tiempo que se espera a que el socket abra.</summary>
    public TimeSpan EsperaDeConexion { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Tiempo que se espera a que el nodo pida el indicativo. Si no lo pide, se envia
    /// igualmente: hay nodos que no preguntan nada y se quedan callados esperando.
    /// </summary>
    public TimeSpan EsperaDelAviso { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Tiempo sin recibir un solo byte tras el cual se da la conexion por muerta. Un cluster
    /// vivo manda algo cada pocos minutos, aunque solo sea una linea de servicio.
    /// </summary>
    public TimeSpan SilencioMaximo { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>Espera antes del primer reintento.</summary>
    public TimeSpan EsperaPrimerReintento { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Tope de la espera entre reintentos.</summary>
    public TimeSpan EsperaMaximaReintento { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Por cuanto se multiplica la espera en cada reintento fallido.
    /// </summary>
    /// <remarks>
    /// Un cluster se cae a menudo y de madrugada. Reintentar cada segundo no lo levanta
    /// antes y si molesta al que lo mantiene, asi que la espera crece hasta el tope.
    /// </remarks>
    public double FactorDeReintento { get; init; } = 2.0;

    /// <summary>Reconectar solo cuando se cae la conexion.</summary>
    public bool ReconectarSolo { get; init; } = true;

    /// <summary>Indicativo completo con el que hay que identificarse, sufijo incluido.</summary>
    public string IndicativoDeAcceso =>
        string.IsNullOrWhiteSpace(Sufijo) ? Indicativo.Valor : $"{Indicativo.Valor}-{Sufijo.Trim()}";
}
