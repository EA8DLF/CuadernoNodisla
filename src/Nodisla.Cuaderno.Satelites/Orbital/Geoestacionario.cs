namespace Nodisla.Cuaderno.Satelites.Orbital;

/// <summary>
/// A donde hay que apuntar para un satelite geoestacionario.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que no pasa por SGP4.</b> Un geoestacionario esta en espacio profundo y el modelo de
/// orbita baja lo rechaza, con razon: haria falta SDP4. Pero para apuntar una antena a un
/// geoestacionario no hace falta ningun propagador. Esta quieto sobre un punto del ecuador, y
/// el azimut y la elevacion salen de geometria pura: dos angulos y una distancia que no cambian
/// con el tiempo.
/// </para>
/// <para>
/// Es justo lo que hace falta para <b>QO-100</b>, que desde Canarias es el satelite mas usado
/// del catalogo y hasta ahora era el unico que el modulo no sabia situar.
/// </para>
/// <para>
/// <b>Lo que esta simplificacion no da.</b> Un geoestacionario no esta del todo quieto: la
/// inclinacion residual le hace dibujar un ocho de unas decimas de grado al cabo del dia. Para
/// una antena de aficionado —una parabola de 60 cm en 10 GHz tiene un haz de unos tres grados—
/// eso no se nota. Para el Doppler de banda estrecha si se notaria, y por eso
/// <see cref="VistaDesdeTierra.VelocidadRadialKmS"/> se devuelve como cero y no como un numero
/// inventado: en QO-100 no se corrige Doppler, se sintoniza contra la baliza.
/// </para>
/// </remarks>
public static class Geoestacionario
{
    /// <summary>Radio de la orbita geoestacionaria desde el centro de la Tierra, en kilometros.</summary>
    /// <remarks>
    /// Sale de igualar el periodo orbital al dia sidereo, no al dia solar: 23 h 56 min 4 s.
    /// Usando el dia solar el radio sale unos cien kilometros largo.
    /// </remarks>
    public const double RadioOrbitaKm = 42164.17;

    /// <summary>Longitud sobre la que esta QO-100, en grados al este.</summary>
    public const double LongitudQo100 = 25.9;

    /// <summary>Calcula a donde apuntar a un geoestacionario desde una estacion.</summary>
    /// <param name="longitudGrados">Longitud del satelite sobre el ecuador, positiva al este.</param>
    /// <param name="observador">La estacion.</param>
    /// <returns>Azimut, elevacion y distancia; la velocidad radial se devuelve como cero.</returns>
    public static VistaDesdeTierra Mirar(double longitudGrados, Observador observador)
    {
        var lon = longitudGrados * Tiempos.GradosARadianes;
        var posicion = new Vector3(
            RadioOrbitaKm * Math.Cos(lon),
            RadioOrbitaKm * Math.Sin(lon),
            0.0);

        return Topocentrico.MirarFijo(posicion, default, observador);
    }

    /// <summary>
    /// Los geoestacionarios de aficionado en servicio, con su longitud sobre el ecuador.
    /// </summary>
    /// <remarks>
    /// Hoy es uno solo. Se deja como diccionario y no como un caso suelto porque el dia que
    /// haya otro —hay proyectos anunciados— se anade una linea y todo lo demas sigue igual.
    /// </remarks>
    public static IReadOnlyDictionary<string, double> EnServicio { get; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["QO-100"] = LongitudQo100,
        };

    /// <summary>Longitud de un geoestacionario de aficionado por su abreviatura.</summary>
    /// <param name="abreviatura">Abreviatura, la de <c>SAT_NAME</c>.</param>
    /// <returns>La longitud en grados al este, o <c>null</c> si no es geoestacionario.</returns>
    public static double? LongitudDe(string? abreviatura) =>
        !string.IsNullOrWhiteSpace(abreviatura) && EnServicio.TryGetValue(abreviatura.Trim(), out var lon)
            ? lon
            : null;

    /// <summary>El satelite se ve desde la estacion, con el margen que se pida.</summary>
    /// <param name="longitudGrados">Longitud del satelite sobre el ecuador.</param>
    /// <param name="observador">La estacion.</param>
    /// <param name="elevacionMinimaGrados">
    /// Elevacion por debajo de la cual no cuenta. Cinco grados por omision: por debajo de ahi
    /// el ruido del suelo y cualquier casa o palmera se comen la senal de 10 GHz.
    /// </param>
    /// <returns><c>true</c> si se ve.</returns>
    public static bool SeVe(
        double longitudGrados,
        Observador observador,
        double elevacionMinimaGrados = 5.0) =>
        Mirar(longitudGrados, observador).ElevacionGrados >= elevacionMinimaGrados;
}
