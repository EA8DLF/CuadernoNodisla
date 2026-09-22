using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Estado del Sol y del campo magnetico terrestre.</summary>
/// <param name="FlujoSolar">Flujo solar a 10,7 cm.</param>
/// <param name="IndiceA">Indice A geomagnetico del dia.</param>
/// <param name="IndiceK">Indice K de las ultimas tres horas.</param>
/// <param name="ManchasSolares">Numero de manchas.</param>
/// <param name="VientoSolarKmS">Velocidad del viento solar, en kilometros por segundo.</param>
/// <param name="CampoBz">Componente Bz del campo magnetico interplanetario, en nanoteslas.</param>
/// <param name="Tormenta">Hay tormenta geomagnetica en curso.</param>
/// <param name="MedidoUtc">Momento de la medida.</param>
public sealed record IndicesSolares(
    double? FlujoSolar,
    double? IndiceA,
    double? IndiceK,
    double? ManchasSolares,
    double? VientoSolarKmS,
    double? CampoBz,
    bool Tormenta,
    DateTimeOffset MedidoUtc)
{
    /// <summary>Cuando se trajeron estos datos de la red.</summary>
    /// <remarks>
    /// No es lo mismo que <see cref="MedidoUtc"/>: el flujo solar se publica una vez al dia,
    /// asi que una medida de hace veinte horas puede estar recien descargada y ser la ultima
    /// que existe. Medir la frescura por la medida haria saltar el aviso siempre.
    /// </remarks>
    public DateTimeOffset? DescargadoUtc { get; init; }

    /// <summary>
    /// Estos datos salen de la copia guardada, no de una descarga de ahora.
    /// </summary>
    /// <remarks>
    /// La interfaz <b>tiene que decirlo</b>. Ensenar unos indices viejos como si fueran de
    /// ahora lleva al operador a decidir en que banda llamar con datos de otro dia.
    /// </remarks>
    public bool DeCache { get; init; }

    /// <summary>Frase en espanol lista para mostrar, con la procedencia y la antiguedad.</summary>
    public string Descripcion { get; init; } = string.Empty;
}

/// <summary>Prediccion de un trayecto en una banda y una hora.</summary>
/// <param name="Banda">Banda predicha.</param>
/// <param name="HoraUtc">Hora para la que vale.</param>
/// <param name="Fiabilidad">Probabilidad de que el circuito funcione, de 0 a 1.</param>
/// <param name="SenalDbw">Senal prevista en decibelios sobre un vatio.</param>
/// <param name="RelacionSenalRuido">Relacion senal-ruido prevista, en decibelios.</param>
/// <param name="Saltos">Numero de saltos ionosfericos previstos.</param>
public sealed record PrediccionDeBanda(
    Banda Banda,
    DateTimeOffset HoraUtc,
    double Fiabilidad,
    double? SenalDbw,
    double? RelacionSenalRuido,
    int? Saltos)
{
    /// <summary>
    /// Se predijo <b>sin indices solares</b>, suponiendo condiciones medias.
    /// </summary>
    /// <remarks>
    /// Una prediccion hecha a ciegas y una hecha con el Sol de hoy no valen lo mismo, y el
    /// operador tiene derecho a distinguirlas antes de elegir banda.
    /// </remarks>
    public bool SinDatosSolares { get; init; }
}

/// <summary>Datos de un trayecto entre dos puntos.</summary>
/// <param name="Origen">Punto de partida.</param>
/// <param name="Destino">Punto de llegada.</param>
/// <param name="DistanciaKm">Distancia por el camino corto.</param>
/// <param name="RumboCorto">Rumbo del camino corto, en grados.</param>
/// <param name="RumboLargo">Rumbo del camino largo, en grados.</param>
/// <param name="AmaneceEnDestino">Hora del orto en el destino, si la hay ese dia.</param>
/// <param name="AnocheceEnDestino">Hora del ocaso en el destino, si la hay ese dia.</param>
/// <param name="EnPasoGris">El trayecto cruza ahora mismo la linea del amanecer o el atardecer.</param>
public sealed record Trayecto(
    Coordenada Origen,
    Coordenada Destino,
    double DistanciaKm,
    double RumboCorto,
    double RumboLargo,
    DateTimeOffset? AmaneceEnDestino,
    DateTimeOffset? AnocheceEnDestino,
    bool EnPasoGris)
{
    /// <summary>
    /// Por que no hay orto ni ocaso, cuando no los hay.
    /// </summary>
    /// <remarks>
    /// Sin esto, el dia polar y la noche polar se ven igual desde fuera —los dos dejan las dos
    /// horas en nulo— y son lo contrario: en uno el Sol no se pone y en el otro no sale.
    /// </remarks>
    public MotivoSinEventoSolar MotivoSinEventos { get; init; } = MotivoSinEventoSolar.NoAplica;
}

/// <summary>Por que un lugar no tiene orto ni ocaso en una fecha.</summary>
public enum MotivoSinEventoSolar
{
    /// <summary>Si hay orto y ocaso; el motivo no viene al caso.</summary>
    NoAplica,
    /// <summary>El Sol no se pone en todo el dia.</summary>
    DiaPolar,
    /// <summary>El Sol no sale en todo el dia.</summary>
    NochePolar,
}

/// <summary>
/// Propagacion: como esta el Sol, que bandas deberian abrir y hacia donde.
/// </summary>
/// <remarks>
/// <para>
/// El original empaqueta un motor VOACAP nativo de 16 MB que no se puede reutilizar. Las dos
/// salidas honestas son ejecutar VOACAP o ITURHFPROP como proceso externo y leer su resultado,
/// o calcular una aproximacion propia diciendo que lo es. Lo que no vale es dar una prediccion
/// inventada con pinta de precisa: el operador la usa para decidir en que banda llamar.
/// </para>
/// <para>
/// Los indices solares vienen de la red y la red falla. Si no hay datos frescos, se dice desde
/// cuando son los que hay; no se ensena un valor viejo como si fuera de ahora.
/// </para>
/// </remarks>
public interface IPropagacion
{
    /// <summary>Ultimos indices solares conocidos. Nulo si nunca se han podido traer.</summary>
    IndicesSolares? Indices { get; }

    /// <summary>Salta cuando llegan indices nuevos.</summary>
    event EventHandler<IndicesSolares>? IndicesActualizados;

    /// <summary>Trae los indices solares de la red.</summary>
    Task<IndicesSolares?> ActualizarIndicesAsync(CancellationToken ct = default);

    /// <summary>Calcula los datos geometricos de un trayecto: distancia, rumbos y paso gris.</summary>
    Trayecto CalcularTrayecto(Coordenada origen, Coordenada destino, DateTimeOffset momentoUtc);

    /// <summary>
    /// Predice que bandas deberian abrir hacia un destino.
    /// </summary>
    /// <param name="origen">Mi estacion.</param>
    /// <param name="destino">Adonde quiero llegar.</param>
    /// <param name="potenciaVatios">Potencia con la que se transmitiria.</param>
    /// <param name="momentoUtc">Momento para el que se predice.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task<IReadOnlyList<PrediccionDeBanda>> PredecirAsync(
        Coordenada origen,
        Coordenada destino,
        double potenciaVatios,
        DateTimeOffset momentoUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Que motor esta detras de la prediccion, para poder decirselo al operador.
    /// </summary>
    /// <remarks>
    /// Si es una aproximacion propia y no VOACAP, la interfaz <b>tiene que decirlo</b>. La
    /// diferencia entre una prediccion calculada y una estimacion cambia lo que el operador
    /// hace con ella.
    /// </remarks>
    string MotorDePrediccion { get; }

    /// <summary>La prediccion es una aproximacion propia, no un motor reconocido.</summary>
    bool EsAproximacion { get; }
}
