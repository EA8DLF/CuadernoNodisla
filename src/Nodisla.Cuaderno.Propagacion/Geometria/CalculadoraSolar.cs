using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Propagacion.Geometria;

/// <summary>Orto, ocaso y mediodia solar en un punto de la Tierra.</summary>
/// <param name="AmaneceUtc">Hora del orto, o nulo si ese dia no lo hay.</param>
/// <param name="AnocheceUtc">Hora del ocaso, o nulo si ese dia no lo hay.</param>
/// <param name="MediodiaSolarUtc">Paso del Sol por el meridiano del lugar.</param>
/// <param name="Motivo">Por que faltan el orto y el ocaso, cuando faltan.</param>
public sealed record EventosSolares(
    DateTimeOffset? AmaneceUtc,
    DateTimeOffset? AnocheceUtc,
    DateTimeOffset MediodiaSolarUtc,
    MotivoSinEventoSolar Motivo);

/// <summary>
/// Posicion del Sol: declinacion, ecuacion del tiempo, altura sobre el horizonte, orto y ocaso.
/// </summary>
/// <remarks>
/// <para>
/// Las formulas son las del algoritmo solar del NOAA (Solar Calculation Details, NOAA Global
/// Monitoring Laboratory), que resume a su vez el Astronomical Algorithms de Jean Meeus. Valen
/// con error de menos de un minuto entre 1800 y 2100, de sobra para trabajo de radio.
/// </para>
/// <para>
/// El orto y el ocaso se toman con el centro del disco solar a -0,833 grados, que es lo que
/// publica todo el mundo: medio diametro del Sol mas la refraccion media de la atmosfera.
/// </para>
/// <para>
/// <b>Lo importante:</b> por encima de los circulos polares hay dias sin orto y dias sin ocaso.
/// En esos casos estas funciones devuelven nulo. No se inventa una hora: una hora inventada en
/// un cuaderno acaba en una decision equivocada.
/// </para>
/// </remarks>
public static class CalculadoraSolar
{
    /// <summary>Altura del centro del Sol en el orto y el ocaso, en grados.</summary>
    public const double AlturaDelOrtoGrados = -0.833;

    /// <summary>Altura por debajo de la cual empieza el crepusculo civil, en grados.</summary>
    public const double AlturaCrepusculoCivilGrados = -6.0;

    private const double Rad = Math.PI / 180.0;
    private const double Deg = 180.0 / Math.PI;

    /// <summary>Dia juliano del momento dado.</summary>
    /// <param name="momentoUtc">Momento que se convierte.</param>
    public static double DiaJuliano(DateTimeOffset momentoUtc)
    {
        var u = momentoUtc.ToUniversalTime();
        // 2440587,5 es el dia juliano del 1 de enero de 1970 a las 00:00 UTC.
        return u.UtcDateTime.Subtract(DateTime.UnixEpoch).TotalDays + 2440587.5;
    }

    /// <summary>Declinacion aparente del Sol en grados para el momento dado.</summary>
    /// <param name="momentoUtc">Momento para el que se calcula.</param>
    public static double DeclinacionGrados(DateTimeOffset momentoUtc) =>
        Declinacion(SiglosJulianos(DiaJuliano(momentoUtc)));

    /// <summary>
    /// Ecuacion del tiempo en minutos: cuanto adelanta o atrasa el Sol verdadero al Sol medio.
    /// </summary>
    /// <param name="momentoUtc">Momento para el que se calcula.</param>
    public static double EcuacionDelTiempoMinutos(DateTimeOffset momentoUtc) =>
        EcuacionDelTiempo(SiglosJulianos(DiaJuliano(momentoUtc)));

    /// <summary>
    /// Altura del Sol sobre el horizonte en grados, ya corregida de refraccion atmosferica.
    /// </summary>
    /// <param name="punto">Punto de observacion.</param>
    /// <param name="momentoUtc">Momento para el que se calcula.</param>
    public static double AlturaSolarGrados(Coordenada punto, DateTimeOffset momentoUtc)
    {
        var t = SiglosJulianos(DiaJuliano(momentoUtc));
        var declinacion = Declinacion(t);
        var ecuacion = EcuacionDelTiempo(t);

        var alturaGeometrica = AlturaGeometrica(punto, momentoUtc, declinacion, ecuacion);
        return alturaGeometrica + RefraccionGrados(alturaGeometrica);
    }

    /// <summary>
    /// Altura del Sol sobre el horizonte en grados sin corregir de refraccion, que es la que
    /// define el orto y el ocaso publicados.
    /// </summary>
    /// <param name="punto">Punto de observacion.</param>
    /// <param name="momentoUtc">Momento para el que se calcula.</param>
    public static double AlturaGeometricaGrados(Coordenada punto, DateTimeOffset momentoUtc)
    {
        var t = SiglosJulianos(DiaJuliano(momentoUtc));
        return AlturaGeometrica(punto, momentoUtc, Declinacion(t), EcuacionDelTiempo(t));
    }

    /// <summary>El Sol esta por encima del horizonte en ese punto y ese momento.</summary>
    /// <param name="punto">Punto de observacion.</param>
    /// <param name="momentoUtc">Momento para el que se calcula.</param>
    /// <remarks>
    /// Usa la altura geometrica y el mismo umbral que el orto, para que decir "es de dia" y
    /// calcular la hora del amanecer no puedan contradecirse.
    /// </remarks>
    public static bool EsDeDia(Coordenada punto, DateTimeOffset momentoUtc) =>
        AlturaGeometricaGrados(punto, momentoUtc) > AlturaDelOrtoGrados;

    private static double AlturaGeometrica(
        Coordenada punto,
        DateTimeOffset momentoUtc,
        double declinacion,
        double ecuacion)
    {
        var minutosUtc = momentoUtc.ToUniversalTime().UtcDateTime.TimeOfDay.TotalMinutes;
        // Hora solar verdadera del lugar, en minutos desde la medianoche solar.
        var horaSolar = minutosUtc + ecuacion + (4.0 * punto.Longitud);
        horaSolar = ((horaSolar % 1440.0) + 1440.0) % 1440.0;
        var anguloHorario = (horaSolar / 4.0) - 180.0;

        var lat = punto.Latitud * Rad;
        var dec = declinacion * Rad;
        var coseno = (Math.Sin(lat) * Math.Sin(dec))
                     + (Math.Cos(lat) * Math.Cos(dec) * Math.Cos(anguloHorario * Rad));
        coseno = Math.Clamp(coseno, -1.0, 1.0);
        return 90.0 - (Math.Acos(coseno) * Deg);
    }

    /// <summary>
    /// Orto, ocaso y mediodia solar del dia UTC al que pertenece <paramref name="momentoUtc"/>.
    /// </summary>
    /// <param name="punto">Punto de observacion.</param>
    /// <param name="momentoUtc">Cualquier instante del dia que interesa.</param>
    /// <param name="alturaGrados">
    /// Altura del Sol que define el suceso. Por omision, la del orto y el ocaso publicados; con
    /// -6 se obtiene el crepusculo civil.
    /// </param>
    /// <returns>Las horas, o nulos con el motivo cuando ese dia el Sol no sale o no se pone.</returns>
    public static EventosSolares Calcular(
        Coordenada punto,
        DateTimeOffset momentoUtc,
        double alturaGrados = AlturaDelOrtoGrados)
    {
        var dia = momentoUtc.ToUniversalTime().UtcDateTime.Date;
        var medianoche = new DateTimeOffset(dia, TimeSpan.Zero);

        // El mediodia solar se refina una vez: la ecuacion del tiempo cambia poco en un dia,
        // pero con la segunda pasada el error baja de un minuto a menos de un segundo.
        var mediodia = medianoche.AddMinutes(MediodiaSolarMinutos(punto, medianoche.AddHours(12)));
        mediodia = medianoche.AddMinutes(MediodiaSolarMinutos(punto, mediodia));

        // Al mediodia solar el Sol esta en su punto mas alto del dia. Si ni asi llega a la altura
        // pedida, ese dia no sale.
        if (AlturaGeometricaGrados(punto, mediodia) < alturaGrados)
        {
            return new EventosSolares(null, null, mediodia, MotivoSinEventoSolar.NochePolar);
        }

        var orto = BuscarCruce(punto, mediodia, alturaGrados, haciaAtras: true);
        var ocaso = BuscarCruce(punto, mediodia, alturaGrados, haciaAtras: false);

        if (orto is null || ocaso is null)
        {
            // El Sol no baja de esa altura en las doce horas a un lado ni al otro del mediodia.
            return new EventosSolares(null, null, mediodia, MotivoSinEventoSolar.DiaPolar);
        }

        return new EventosSolares(orto, ocaso, mediodia, MotivoSinEventoSolar.NoAplica);
    }

    /// <summary>
    /// Busca el momento en que el Sol cruza una altura, saliendo del mediodia solar hacia atras
    /// (el orto) o hacia delante (el ocaso).
    /// </summary>
    /// <remarks>
    /// Se hace por barrido y biseccion sobre la altura de verdad, no despejando el angulo horario.
    /// Cerca de los circulos polares la declinacion cambia lo bastante en las horas que van del
    /// mediodia al ocaso como para que el despeje se equivoque en varios minutos; buscando el
    /// cruce de verdad, el error queda por debajo del segundo en cualquier latitud.
    /// </remarks>
    private static DateTimeOffset? BuscarCruce(
        Coordenada punto,
        DateTimeOffset mediodia,
        double alturaGrados,
        bool haciaAtras)
    {
        const int PasoMinutos = 10;
        const int PasosDeMedioDia = 12 * 60 / PasoMinutos;
        var signo = haciaAtras ? -1 : 1;

        var anterior = mediodia;
        for (var paso = 1; paso <= PasosDeMedioDia; paso++)
        {
            var actual = mediodia.AddMinutes(signo * paso * PasoMinutos);
            if (AlturaGeometricaGrados(punto, actual) < alturaGrados)
            {
                return Bisecar(punto, actual, anterior, alturaGrados);
            }

            anterior = actual;
        }

        return null;
    }

    /// <summary>Afina el cruce entre un momento por debajo de la altura y otro por encima.</summary>
    private static DateTimeOffset Bisecar(
        Coordenada punto,
        DateTimeOffset debajo,
        DateTimeOffset encima,
        double alturaGrados)
    {
        // Treinta pasadas sobre diez minutos dejan el resultado en menos de una milesima de
        // segundo, mucho mas fino de lo que hace falta y mas barato que cualquier iteracion.
        for (var i = 0; i < 30; i++)
        {
            var medio = debajo + ((encima - debajo) / 2);
            if (AlturaGeometricaGrados(punto, medio) < alturaGrados)
            {
                debajo = medio;
            }
            else
            {
                encima = medio;
            }
        }

        return debajo + ((encima - debajo) / 2);
    }

    private static double MediodiaSolarMinutos(Coordenada punto, DateTimeOffset referencia)
    {
        var ecuacion = EcuacionDelTiempo(SiglosJulianos(DiaJuliano(referencia)));
        return 720.0 - (4.0 * punto.Longitud) - ecuacion;
    }

    /// <summary>Siglos julianos desde J2000,0.</summary>
    private static double SiglosJulianos(double diaJuliano) => (diaJuliano - 2451545.0) / 36525.0;

    /// <summary>Declinacion aparente del Sol en grados.</summary>
    private static double Declinacion(double t)
    {
        var lambda = LongitudAparente(t) * Rad;
        var epsilon = ObicuidadCorregida(t) * Rad;
        return Math.Asin(Math.Sin(epsilon) * Math.Sin(lambda)) * Deg;
    }

    /// <summary>Longitud media geometrica del Sol, en grados.</summary>
    private static double LongitudMedia(double t)
    {
        var l = 280.46646 + (t * (36000.76983 + (t * 0.0003032)));
        return ((l % 360.0) + 360.0) % 360.0;
    }

    /// <summary>Anomalia media del Sol, en grados.</summary>
    private static double AnomaliaMedia(double t) =>
        357.52911 + (t * (35999.05029 - (0.0001537 * t)));

    /// <summary>Excentricidad de la orbita terrestre.</summary>
    private static double Excentricidad(double t) =>
        0.016708634 - (t * (0.000042037 + (0.0000001267 * t)));

    /// <summary>Ecuacion del centro del Sol, en grados.</summary>
    private static double EcuacionDelCentro(double t)
    {
        var m = AnomaliaMedia(t) * Rad;
        return (Math.Sin(m) * (1.914602 - (t * (0.004817 + (0.000014 * t)))))
               + (Math.Sin(2 * m) * (0.019993 - (0.000101 * t)))
               + (Math.Sin(3 * m) * 0.000289);
    }

    /// <summary>Longitud aparente del Sol, en grados.</summary>
    private static double LongitudAparente(double t)
    {
        var verdadera = LongitudMedia(t) + EcuacionDelCentro(t);
        var omega = 125.04 - (1934.136 * t);
        return verdadera - 0.00569 - (0.00478 * Math.Sin(omega * Rad));
    }

    /// <summary>Oblicuidad media de la ecliptica, en grados.</summary>
    private static double ObicuidadMedia(double t) =>
        23.0 + ((26.0 + ((21.448 - (t * (46.815 + (t * (0.00059 - (t * 0.001813)))))) / 60.0)) / 60.0);

    /// <summary>Oblicuidad de la ecliptica corregida de nutacion, en grados.</summary>
    private static double ObicuidadCorregida(double t)
    {
        var omega = 125.04 - (1934.136 * t);
        return ObicuidadMedia(t) + (0.00256 * Math.Cos(omega * Rad));
    }

    /// <summary>Ecuacion del tiempo, en minutos.</summary>
    private static double EcuacionDelTiempo(double t)
    {
        var epsilon = ObicuidadCorregida(t) * Rad;
        var l0 = LongitudMedia(t) * Rad;
        var e = Excentricidad(t);
        var m = AnomaliaMedia(t) * Rad;

        var y = Math.Pow(Math.Tan(epsilon / 2.0), 2);
        var ecuacion = (y * Math.Sin(2 * l0))
                       - (2.0 * e * Math.Sin(m))
                       + (4.0 * e * y * Math.Sin(m) * Math.Cos(2 * l0))
                       - (0.5 * y * y * Math.Sin(4 * l0))
                       - (1.25 * e * e * Math.Sin(2 * m));
        return ecuacion * 4.0 * Deg;
    }

    /// <summary>
    /// Refraccion atmosferica en grados segun la formula de Saemundsson, la que usa el NOAA.
    /// Por debajo de -1 grado se deja de aplicar: alli ya no significa nada.
    /// </summary>
    private static double RefraccionGrados(double alturaGeometricaGrados)
    {
        if (alturaGeometricaGrados < -1.0)
        {
            return 0.0;
        }

        var h = alturaGeometricaGrados;
        var refraccionMinutos = 1.02 / Math.Tan((h + (10.3 / (h + 5.11))) * Rad);
        return refraccionMinutos / 60.0;
    }
}
