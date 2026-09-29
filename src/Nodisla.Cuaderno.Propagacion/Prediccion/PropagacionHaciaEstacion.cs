using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Propagacion.Prediccion;

/// <summary>
/// Lo que se sabe de «mi propagacion hacia esa estacion» ahora mismo.
/// </summary>
/// <param name="Fiabilidad">Probabilidad de que el circuito funcione, de 0 a 1.</param>
/// <param name="RelacionSenalRuido">Relacion senal-ruido prevista, o nula si no significa nada.</param>
/// <param name="MufMhz">MUF mediana del trayecto.</param>
/// <param name="DistanciaKm">Distancia por el camino corto.</param>
/// <param name="RumboGrados">Rumbo del camino corto desde mi estacion.</param>
/// <param name="Saltos">Saltos por la capa F2.</param>
/// <param name="CalculadaUtc">Momento para el que se calculo.</param>
/// <param name="IndicesDeCopia">Los indices solares venian de la copia guardada, no de ahora.</param>
public sealed record PrevisionHaciaEstacion(
    double Fiabilidad,
    double? RelacionSenalRuido,
    double MufMhz,
    double DistanciaKm,
    double RumboGrados,
    int Saltos,
    DateTimeOffset CalculadaUtc,
    bool IndicesDeCopia)
{
    /// <summary>Motor que hizo la cuenta, para decirselo al operador.</summary>
    public string Motor { get; init; } = MotorAproximacionNodisla.NombreDelMotor;
}

/// <summary>
/// Calcula, para cada anuncio del cluster, si la banda deberia estar abierta ahora entre mi
/// estacion y la anunciada, en la frecuencia en la que la anuncian.
/// </summary>
/// <remarks>
/// <para>
/// Usa la aproximacion propia y <b>no</b> ITURHFProp: un cluster manda un anuncio por segundo
/// y cada llamada a ITURHFProp es un proceso externo de segundos. La aproximacion ordena bien
/// las bandas entre si, que es lo que hace falta para saber si merece la pena girar el dial.
/// </para>
/// <para>
/// Sin indices solares no se calcula nada: un porcentaje hecho con un Sol supuesto, en una
/// lista que se mira de reojo, se leeria como si fuera de hoy. Mejor la celda vacia.
/// </para>
/// <para>
/// Se guarda en caché por trayecto, frecuencia y modo durante <see cref="Vigencia"/>: la
/// ionosfera no cambia de un anuncio al siguiente, y la misma entidad sale decenas de veces
/// por hora. Al pasar la vigencia o cambiar los indices, se vuelve a calcular.
/// </para>
/// </remarks>
public sealed class PropagacionHaciaEstacion
{
    /// <summary>Cuanto vale una cuenta antes de repetirla.</summary>
    public static readonly TimeSpan VigenciaPorOmision = TimeSpan.FromMinutes(15);

    /// <summary>Potencia supuesta de las dos estaciones, en vatios.</summary>
    public const double PotenciaVatios = 100.0;

    /// <summary>Por debajo de esto no hay capa F que valga: onda larga y media.</summary>
    public const double FrecuenciaMinimaMhz = 1.7;

    /// <summary>Por encima de esto la capa F2 no aplica: VHF y superiores.</summary>
    public const double FrecuenciaMaximaMhz = 54.0;

    private readonly MotorAproximacionNodisla _motor;
    private readonly Dictionary<Clave, PrevisionHaciaEstacion> _cache = [];
    private long _tramo = long.MinValue;
    private object? _indicesDeLaCache;

    /// <summary>Monta la calculadora.</summary>
    /// <param name="opciones">Ajustes de antena y ruido.</param>
    /// <param name="vigencia">Cuanto vale una cuenta; 15 minutos por omision.</param>
    public PropagacionHaciaEstacion(OpcionesPropagacion? opciones = null, TimeSpan? vigencia = null)
    {
        _motor = new MotorAproximacionNodisla(opciones);
        Vigencia = vigencia is { Ticks: > 0 } v ? v : VigenciaPorOmision;
    }

    /// <summary>Cuanto vale una cuenta antes de repetirla.</summary>
    public TimeSpan Vigencia { get; }

    /// <summary>Cuentas guardadas ahora mismo.</summary>
    public int EnCache => _cache.Count;

    /// <summary>
    /// Prevision hacia una estacion, o nula si no hay con que calcularla.
    /// </summary>
    /// <param name="origen">Mi estacion; nula si el perfil no tiene localizador.</param>
    /// <param name="destino">La estacion anunciada; nula si no se sabe donde esta.</param>
    /// <param name="frecuenciaMhz">Frecuencia del anuncio.</param>
    /// <param name="modo">Modo del anuncio, para saber que relacion senal-ruido basta.</param>
    /// <param name="ahoraUtc">Momento para el que se calcula.</param>
    /// <param name="indices">Indices solares; sin ellos no se calcula.</param>
    public PrevisionHaciaEstacion? Prever(
        Coordenada? origen,
        Coordenada? destino,
        double frecuenciaMhz,
        string? modo,
        DateTimeOffset ahoraUtc,
        IndicesSolares? indices)
    {
        if (origen is not { } o || destino is not { } d || indices is null) return null;
        if (frecuenciaMhz is < FrecuenciaMinimaMhz or > FrecuenciaMaximaMhz) return null;

        // Mismo sitio (o casi): no hay trayecto ionosferico que predecir.
        var distancia = Geodesia.DistanciaKm(o, d);
        if (distancia < 1.0) return null;

        // Cada tramo de vigencia empieza con la cache vacia; tambien cuando cambian los indices.
        var tramo = ahoraUtc.UtcTicks / Vigencia.Ticks;
        // Los indices se comparan por lo que miden, no por el objeto: el servicio los rehace en
        // cada consulta con una frase distinta («hace 12 minutos») y eso no es un Sol nuevo.
        var huella = (indices.MedidoUtc, indices.DescargadoUtc, indices.FlujoSolar, indices.IndiceK,
            indices.ManchasSolares, indices.Tormenta);
        if (tramo != _tramo || !Equals(huella, _indicesDeLaCache))
        {
            _cache.Clear();
            _tramo = tramo;
            _indicesDeLaCache = huella;
        }

        var (ancho, requerida, grupo) = ExigenciaDelModo(modo);
        var clave = new Clave(
            Math.Round(o.Latitud, 2), Math.Round(o.Longitud, 2),
            Math.Round(d.Latitud, 2), Math.Round(d.Longitud, 2),
            Math.Round(frecuenciaMhz, 1),
            grupo);

        if (_cache.TryGetValue(clave, out var guardada)) return guardada;

        // La cuenta se hace para la mitad del tramo, no para el segundo exacto: asi todas las
        // filas del mismo tramo salen coherentes entre si y no depende de cuando llego cada una.
        var momento = new DateTimeOffset(tramo * Vigencia.Ticks, TimeSpan.Zero) + (Vigencia / 2);
        var solicitud = new SolicitudDePrediccion(o, d, PotenciaVatios, momento, indices);
        var prevision = _motor.PredecirEnFrecuencia(solicitud, frecuenciaMhz, ancho, requerida);

        var resultado = new PrevisionHaciaEstacion(
            prevision.Fiabilidad,
            prevision.RelacionSenalRuido,
            prevision.MufMhz,
            Math.Round(distancia, 0),
            Math.Round(Geodesia.RumboGrados(o, d), 0),
            prevision.Saltos,
            momento,
            indices.DeCache);

        _cache[clave] = resultado;
        return resultado;
    }

    /// <summary>Olvida todas las cuentas, para rehacerlas con datos nuevos.</summary>
    public void Olvidar()
    {
        _cache.Clear();
        _tramo = long.MinValue;
        _indicesDeLaCache = null;
    }

    /// <summary>
    /// Ancho de banda y relacion senal-ruido suficiente para cada modo.
    /// </summary>
    /// <remarks>
    /// Umbrales de decodificacion publicados de cada modo, referidos a 2500 Hz los digitales
    /// (como los da WSJT-X) y al ancho del filtro en CW y fonia. Nulo en el ancho quiere decir
    /// «el de los ajustes».
    /// </remarks>
    /// <param name="modo">Modo del anuncio.</param>
    public static (double? AnchoHz, double? RequeridaDb, string Grupo) ExigenciaDelModo(string? modo) =>
        (modo ?? string.Empty).ToUpperInvariant() switch
        {
            "FT8" or "JS8" => (2500.0, -20.0, "FT8"),
            "FT4" => (2500.0, -16.0, "FT4"),
            "JT65" or "JT9" or "Q65" or "FST4" or "WSPR" or "FST4W" => (2500.0, -24.0, "JT"),
            "MSK144" => (2500.0, -8.0, "MSK"),
            "CW" => (500.0, 0.0, "CW"),
            "RTTY" or "PSK31" or "PSK" or "PSK63" or "OLIVIA" => (2500.0, -5.0, "DIG"),
            _ => (null, null, "FONIA"),
        };

    private readonly record struct Clave(
        double OrigenLat,
        double OrigenLon,
        double DestinoLat,
        double DestinoLon,
        double FrecuenciaMhz,
        string Grupo);
}
