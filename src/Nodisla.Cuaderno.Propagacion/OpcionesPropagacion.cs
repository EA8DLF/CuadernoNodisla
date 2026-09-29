namespace Nodisla.Cuaderno.Propagacion;

/// <summary>Ajustes del modulo de propagacion.</summary>
/// <remarks>
/// Los valores por omision sirven para una estacion normal sin tocar nada. La potencia y la
/// ganancia de antena se usan solo para el presupuesto de senal de la aproximacion propia.
/// </remarks>
public sealed record OpcionesPropagacion
{
    /// <summary>
    /// Fichero donde se guardan los ultimos indices solares conocidos, para poder arrancar sin red.
    /// </summary>
    public string RutaDeCache { get; init; } = RutaDeCachePredeterminada();

    /// <summary>
    /// Cuanto tiempo se consideran frescos los indices. Pasado ese plazo se siguen ensenando,
    /// pero diciendo su edad.
    /// </summary>
    public TimeSpan Frescura { get; init; } = TimeSpan.FromHours(3);

    /// <summary>Tiempo maximo de espera de cada peticion a la red.</summary>
    public TimeSpan EsperaDeRed { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Cuantas veces se reintenta una peticion que falla.</summary>
    public int Reintentos { get; init; } = 2;

    /// <summary>Cuanto se espera antes del primer reintento; despues se duplica.</summary>
    public TimeSpan EsperaEntreReintentos { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Nombre del cliente HTTP que se pide a la fabrica.</summary>
    public string NombreDelClienteHttp { get; init; } = "propagacion";

    /// <summary>Ganancia de la antena, en decibelios sobre isotropica.</summary>
    /// <remarks>
    /// Se supone la misma antena en los dos extremos, asi que esta ganancia cuenta dos veces en
    /// el presupuesto de senal. Los 6 dBi por omision son un dipolo a media longitud de onda del
    /// suelo mirando a angulos bajos, contando ya la ganancia que le da el terreno. Una yagi de
    /// tres elementos anda por los 10 dBi y una antena isotropica, por cero.
    /// </remarks>
    public double GananciaAntenaDbi { get; init; } = 6.0;

    /// <summary>Ancho de banda del receptor en hercios, para calcular el ruido.</summary>
    public double AnchoDeBandaHz { get; init; } = 2500.0;

    /// <summary>Ambiente de ruido del emplazamiento, que cambia mucho el resultado en bandas bajas.</summary>
    public AmbienteDeRuido AmbienteDeRuido { get; init; } = AmbienteDeRuido.Residencial;

    /// <summary>
    /// Relacion senal-ruido a partir de la cual se da el contacto por posible, en decibelios.
    /// </summary>
    /// <remarks>
    /// Tres decibelios es un contacto justo en telegrafia o en modos digitales. Para una fonia
    /// comoda hacen falta unos diez.
    /// </remarks>
    public double RelacionSenalRuidoRequeridaDb { get; init; } = 3.0;

    /// <summary>
    /// Ruta del ejecutable de ITURHFProp. Vacio significa que se busca donde suele estar, dentro
    /// del propio proyecto.
    /// </summary>
    public string? RutaDelMotorExterno { get; init; }

    /// <summary>
    /// Usar ITURHFProp cuando este instalado. En falso se calcula siempre con la aproximacion
    /// propia, que es lo que hace falta para compararlas.
    /// </summary>
    public bool UsarMotorExterno { get; init; } = true;

    /// <summary>
    /// Tiempo maximo que se le da al motor externo antes de cortarlo y tirar de la aproximacion.
    /// </summary>
    /// <remarks>
    /// Una prediccion de once bandas tarda menos de un segundo en esta maquina. Diez segundos es
    /// margen de sobra; si los pasa, algo va mal y el operador no debe quedarse esperando.
    /// </remarks>
    public TimeSpan EsperaDelMotorExterno { get; init; } = TimeSpan.FromSeconds(10);

    private static string RutaDeCachePredeterminada() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NODISLA",
        "CuadernoNodisla",
        "propagacion",
        "indices-solares.json");
}

/// <summary>
/// Ambiente de ruido segun la Recomendacion UIT-R P.372, que es la que fija las cifras.
/// </summary>
public enum AmbienteDeRuido
{
    /// <summary>Ciudad con industria alrededor: lo peor.</summary>
    Industrial = 0,

    /// <summary>Barrio de viviendas. Es el caso normal.</summary>
    Residencial,

    /// <summary>Campo con tendido electrico cerca.</summary>
    Rural,

    /// <summary>Campo sin nada alrededor: lo mejor que hay.</summary>
    RuralTranquilo,
}
