using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Propagacion.Geometria;

/// <summary>
/// Todo lo que se sabe de un trayecto en un instante: geometria, Sol en los dos extremos y
/// paso gris.
/// </summary>
/// <param name="Origen">Punto de partida.</param>
/// <param name="Destino">Punto de llegada.</param>
/// <param name="MomentoUtc">Instante al que se refiere todo lo demas.</param>
/// <param name="DistanciaKm">Distancia por el camino corto.</param>
/// <param name="RumboCorto">Rumbo del camino corto, en grados.</param>
/// <param name="RumboLargo">Rumbo del camino largo, en grados.</param>
/// <param name="SolEnOrigen">Orto, ocaso y mediodia en el origen ese dia.</param>
/// <param name="SolEnDestino">Orto, ocaso y mediodia en el destino ese dia.</param>
/// <param name="AlturaSolarOrigenGrados">Altura del Sol en el origen ahora mismo.</param>
/// <param name="AlturaSolarDestinoGrados">Altura del Sol en el destino ahora mismo.</param>
/// <param name="CruzaElTerminador">
/// El camino corto tiene un extremo de dia y el otro de noche, asi que corta la linea del dia y
/// la noche en algun punto. No basta para que haya paso gris.
/// </param>
/// <param name="EnPasoGris">Los dos extremos estan en la franja del amanecer o del atardecer.</param>
/// <param name="FraccionEnOscuridad">Parte del camino corto que va por la zona de noche, de 0 a 1.</param>
public sealed record AnalisisDeTrayecto(
    Coordenada Origen,
    Coordenada Destino,
    DateTimeOffset MomentoUtc,
    double DistanciaKm,
    double RumboCorto,
    double RumboLargo,
    EventosSolares SolEnOrigen,
    EventosSolares SolEnDestino,
    double AlturaSolarOrigenGrados,
    double AlturaSolarDestinoGrados,
    bool CruzaElTerminador,
    bool EnPasoGris,
    double FraccionEnOscuridad)
{
    /// <summary>Convierte el analisis al <see cref="Trayecto"/> que espera la aplicacion.</summary>
    public Trayecto AContrato() => new(
        Origen,
        Destino,
        DistanciaKm,
        RumboCorto,
        RumboLargo,
        SolEnDestino.AmaneceUtc,
        SolEnDestino.AnocheceUtc,
        EnPasoGris)
    {
        // Dia polar y noche polar dejan las dos horas en nulo y son lo contrario: hay que decir
        // cual de los dos es.
        MotivoSinEventos = SolEnDestino.Motivo,
    };
}

/// <summary>
/// Geometria de un trayecto de radio: distancia, rumbos, puntos intermedios y paso gris.
/// </summary>
/// <remarks>
/// <para>
/// La distancia y los rumbos salen de <see cref="Geodesia"/>, que ya los tiene. Aqui se anade lo
/// que hace falta para propagacion: interpolar el camino, saber donde esta el Sol en cada
/// extremo y decidir si hay paso gris.
/// </para>
/// <para>
/// <b>Que se entiende por paso gris.</b> El puerto lo llama <i>cruzar la linea del amanecer o el
/// atardecer</i>. Tomarlo al pie de la letra no serviria: un trayecto Canarias-Japon corta el
/// terminador practicamente todo el dia, y eso no es paso gris. Lo que abre las bandas bajas es
/// que el terminador pase por los <b>dos</b> extremos a la vez, que es cuando la capa D ya se ha
/// disuelto en ambos y la F todavia aguanta. Por eso <c>EnPasoGris</c> exige que el Sol este en
/// la franja del horizonte en el origen y en el destino. El corte geometrico, mas laxo, se
/// publica aparte en <c>CruzaElTerminador</c>.
/// </para>
/// <para>
/// Casos que rompen las formulas ingenuas y que aqui estan cubiertos: el mismo punto como origen
/// y destino, el cruce del antimeridiano (la interpolacion va por vectores unitarios, que no
/// saben de meridianos) y las latitudes polares, donde puede no haber ni orto ni ocaso y se
/// devuelve nulo.
/// </para>
/// </remarks>
public static class GeometriaDeTrayecto
{
    /// <summary>
    /// Media anchura de la franja del paso gris, en grados de altura solar. Seis grados son unos
    /// treinta minutos de reloj a latitudes medias, que es la ventana que se aprovecha.
    /// </summary>
    public const double FranjaDePasoGrisGrados = 6.0;

    /// <summary>Por debajo de esta distancia se considera que los dos puntos son el mismo.</summary>
    public const double DistanciaMinimaKm = 0.001;

    private const double Rad = Math.PI / 180.0;
    private const double Deg = 180.0 / Math.PI;

    /// <summary>Numero de puntos en los que se muestrea el camino para ver si cruza el terminador.</summary>
    private const int MuestrasDelCamino = 64;

    /// <summary>Calcula el <see cref="Trayecto"/> que espera la aplicacion.</summary>
    /// <param name="origen">Punto de partida.</param>
    /// <param name="destino">Punto de llegada.</param>
    /// <param name="momentoUtc">Instante para el que se calcula el paso gris.</param>
    public static Trayecto Calcular(Coordenada origen, Coordenada destino, DateTimeOffset momentoUtc) =>
        Analizar(origen, destino, momentoUtc).AContrato();

    /// <summary>Analiza el trayecto entero, con el Sol en los dos extremos.</summary>
    /// <param name="origen">Punto de partida.</param>
    /// <param name="destino">Punto de llegada.</param>
    /// <param name="momentoUtc">Instante para el que se calcula.</param>
    /// <param name="franjaGrados">Media anchura de la franja del paso gris, en grados de altura solar.</param>
    public static AnalisisDeTrayecto Analizar(
        Coordenada origen,
        Coordenada destino,
        DateTimeOffset momentoUtc,
        double franjaGrados = FranjaDePasoGrisGrados)
    {
        var distancia = Geodesia.DistanciaKm(origen, destino);
        var mismoPunto = distancia < DistanciaMinimaKm;

        // Con origen y destino en el mismo sitio no hay rumbo: cualquiera valdria. Se declara
        // cero en vez de dejar que el arcotangente de cero partido cero decida por su cuenta.
        var rumboCorto = mismoPunto ? 0.0 : Geodesia.RumboGrados(origen, destino);
        var rumboLargo = mismoPunto ? 180.0 : Geodesia.RumboLargoGrados(origen, destino);

        var solOrigen = CalculadoraSolar.Calcular(origen, momentoUtc);
        var solDestino = CalculadoraSolar.Calcular(destino, momentoUtc);
        var alturaOrigen = CalculadoraSolar.AlturaSolarGrados(origen, momentoUtc);
        var alturaDestino = CalculadoraSolar.AlturaSolarGrados(destino, momentoUtc);

        var enFranjaOrigen = Math.Abs(alturaOrigen) <= franjaGrados;
        var enFranjaDestino = Math.Abs(alturaDestino) <= franjaGrados;
        var enPasoGris = !mismoPunto && enFranjaOrigen && enFranjaDestino;

        var (cruza, fraccionNoche) = RecorrerElCamino(origen, destino, momentoUtc, mismoPunto);

        return new AnalisisDeTrayecto(
            origen,
            destino,
            momentoUtc,
            distancia,
            rumboCorto,
            rumboLargo,
            solOrigen,
            solDestino,
            alturaOrigen,
            alturaDestino,
            cruza,
            enPasoGris,
            fraccionNoche);
    }

    /// <summary>
    /// Punto del camino corto a una fraccion dada del recorrido, de 0 (origen) a 1 (destino).
    /// </summary>
    /// <param name="origen">Punto de partida.</param>
    /// <param name="destino">Punto de llegada.</param>
    /// <param name="fraccion">Parte del camino ya recorrida, de 0 a 1.</param>
    /// <remarks>
    /// Va por interpolacion esferica sobre vectores unitarios, de modo que el antimeridiano no
    /// es un caso especial: no hay longitudes que sumar ni restar.
    /// </remarks>
    public static Coordenada PuntoIntermedio(Coordenada origen, Coordenada destino, double fraccion)
    {
        var f = Math.Clamp(fraccion, 0.0, 1.0);
        var (x1, y1, z1) = AVector(origen);
        var (x2, y2, z2) = AVector(destino);

        var coseno = Math.Clamp((x1 * x2) + (y1 * y2) + (z1 * z2), -1.0, 1.0);
        var angulo = Math.Acos(coseno);
        if (angulo < 1e-9)
        {
            return origen;
        }

        var seno = Math.Sin(angulo);
        var a = Math.Sin((1.0 - f) * angulo) / seno;
        var b = Math.Sin(f * angulo) / seno;
        var x = (a * x1) + (b * x2);
        var y = (a * y1) + (b * y2);
        var z = (a * z1) + (b * z2);

        var latitud = Math.Atan2(z, Math.Sqrt((x * x) + (y * y))) * Deg;
        var longitud = Math.Atan2(y, x) * Deg;
        return new Coordenada(latitud, NormalizarLongitud(longitud));
    }

    /// <summary>
    /// Puntos de control del trayecto: el centro de cada salto, que es donde se evalua la
    /// ionosfera.
    /// </summary>
    /// <param name="origen">Punto de partida.</param>
    /// <param name="destino">Punto de llegada.</param>
    /// <param name="saltos">Numero de saltos en que se reparte el camino.</param>
    public static IReadOnlyList<Coordenada> PuntosDeControl(
        Coordenada origen,
        Coordenada destino,
        int saltos)
    {
        var n = Math.Max(1, saltos);
        var puntos = new Coordenada[n];
        for (var i = 0; i < n; i++)
        {
            puntos[i] = PuntoIntermedio(origen, destino, (i + 0.5) / n);
        }

        return puntos;
    }

    /// <summary>
    /// Latitud geomagnetica aproximada, con el polo norte magnetico en 80,7 N y 72,7 W.
    /// </summary>
    /// <param name="punto">Punto del que se quiere la latitud geomagnetica.</param>
    /// <remarks>
    /// Es un dipolo centrado, no el campo real: sirve para decidir si un trayecto va por zona
    /// auroral, no para navegar. La posicion del polo es la del IGRF de 2020 redondeada.
    /// </remarks>
    public static double LatitudGeomagneticaGrados(Coordenada punto)
    {
        const double latitudDelPolo = 80.7 * Rad;
        const double longitudDelPolo = -72.7 * Rad;

        var lat = punto.Latitud * Rad;
        var lon = punto.Longitud * Rad;
        var seno = (Math.Sin(lat) * Math.Sin(latitudDelPolo))
                   + (Math.Cos(lat) * Math.Cos(latitudDelPolo) * Math.Cos(lon - longitudDelPolo));
        return Math.Asin(Math.Clamp(seno, -1.0, 1.0)) * Deg;
    }

    /// <summary>
    /// Recorre el camino corto muestreandolo y devuelve si cambia del dia a la noche y que parte
    /// va a oscuras.
    /// </summary>
    private static (bool Cruza, double FraccionEnOscuridad) RecorrerElCamino(
        Coordenada origen,
        Coordenada destino,
        DateTimeOffset momentoUtc,
        bool mismoPunto)
    {
        if (mismoPunto)
        {
            return (false, CalculadoraSolar.EsDeDia(origen, momentoUtc) ? 0.0 : 1.0);
        }

        var deDia = 0;
        var deNoche = 0;
        for (var i = 0; i < MuestrasDelCamino; i++)
        {
            var punto = PuntoIntermedio(origen, destino, (i + 0.5) / MuestrasDelCamino);
            if (CalculadoraSolar.EsDeDia(punto, momentoUtc))
            {
                deDia++;
            }
            else
            {
                deNoche++;
            }
        }

        var cruza = deDia > 0 && deNoche > 0;
        return (cruza, (double)deNoche / MuestrasDelCamino);
    }

    private static (double X, double Y, double Z) AVector(Coordenada punto)
    {
        var lat = punto.Latitud * Rad;
        var lon = punto.Longitud * Rad;
        var coseno = Math.Cos(lat);
        return (coseno * Math.Cos(lon), coseno * Math.Sin(lon), Math.Sin(lat));
    }

    private static double NormalizarLongitud(double grados)
    {
        var l = ((grados + 180.0) % 360.0 + 360.0) % 360.0;
        return l - 180.0;
    }
}
