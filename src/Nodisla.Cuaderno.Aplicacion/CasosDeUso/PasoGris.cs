using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>
/// La linea que separa el dia de la noche sobre el mapa, y el punto de la Tierra que en ese
/// instante tiene el Sol justo encima.
/// </summary>
/// <remarks>
/// El paso gris es la franja de amanecer y anochecer. Es lo que mas se mira de un mapa de
/// radioaficionado: durante esos minutos la absorcion de la capa D se desploma y las bandas
/// bajas abren a sitios que el resto del dia no se oyen. Por eso la linea se dibuja para la
/// hora que se le pida —no solo para ahora— y por eso se mueve sola con el reloj.
///
/// Las formulas son las del almanaque astronomico de bajo coste: valen al minuto largo, que
/// para saber si una banda esta a punto de abrir sobra. No sirven para apuntar un telescopio.
/// </remarks>
public static class PasoGris
{
    /// <summary>Puntos con los que se dibuja la linea si no se pide otra cosa: uno cada dos grados.</summary>
    public const int PuntosPorOmision = 181;

    /// <summary>Con menos puntos la linea se ve como una cadena de rectas, no como una curva.</summary>
    public const int PuntosMinimos = 24;

    /// <summary>
    /// Grados de altura del Sol bajo el horizonte que todavia cuentan como paso gris.
    /// Es el crepusculo civil, los mismos seis grados que usan los mapas del oficio.
    /// </summary>
    public const double GradosDeCrepusculo = 6.0;

    /// <summary>
    /// Punto de la Tierra que tiene el Sol en la vertical en ese instante.
    /// </summary>
    /// <param name="instante">Momento, en la escala que sea; se pasa a UTC.</param>
    /// <returns>La latitud es la declinacion solar y la longitud, la del mediodia.</returns>
    public static Coordenada SubpuntoSolar(DateTimeOffset instante)
    {
        var (declinacion, ascension, diasJulianos) = PosicionSolar(instante);

        // Tiempo sidereo medio en Greenwich: dice que meridiano mira ahora al punto Aries.
        var sidereo = Normalizar360(280.46061837 + (360.98564736629 * diasJulianos));
        var longitud = NormalizarGrados(ascension - sidereo);

        return new Coordenada(declinacion, longitud);
    }

    /// <summary>
    /// Altura del Sol sobre el horizonte de un punto, en grados. Negativa de noche.
    /// </summary>
    /// <param name="punto">Punto de la Tierra.</param>
    /// <param name="instante">Momento a considerar.</param>
    /// <returns>Grados sobre el horizonte, sin corregir la refraccion.</returns>
    public static double AlturaDelSol(Coordenada punto, DateTimeOffset instante)
    {
        var sol = SubpuntoSolar(instante);

        var lat = ARadianes(punto.Latitud);
        var dec = ARadianes(sol.Latitud);
        var angulo = ARadianes(NormalizarGrados(punto.Longitud - sol.Longitud));

        var seno = (Math.Sin(lat) * Math.Sin(dec))
                   + (Math.Cos(lat) * Math.Cos(dec) * Math.Cos(angulo));

        return AGrados(Math.Asin(Math.Clamp(seno, -1.0, 1.0)));
    }

    /// <summary>En ese punto y a esa hora es de dia.</summary>
    /// <param name="punto">Punto de la Tierra.</param>
    /// <param name="instante">Momento a considerar.</param>
    /// <returns>Cierto si el Sol esta por encima del horizonte.</returns>
    public static bool EsDeDia(Coordenada punto, DateTimeOffset instante) =>
        AlturaDelSol(punto, instante) > 0;

    /// <summary>El punto esta en el paso gris: el Sol ronda el horizonte.</summary>
    /// <param name="punto">Punto de la Tierra.</param>
    /// <param name="instante">Momento a considerar.</param>
    /// <returns>Cierto si el Sol esta a menos de seis grados del horizonte, arriba o abajo.</returns>
    public static bool EstaEnPasoGris(Coordenada punto, DateTimeOffset instante) =>
        Math.Abs(AlturaDelSol(punto, instante)) <= GradosDeCrepusculo;

    /// <summary>
    /// La linea de dia y noche, de oeste a este.
    /// </summary>
    /// <param name="instante">Momento a considerar.</param>
    /// <param name="puntos">Cuantos puntos se calculan, de −180° a +180° inclusive.</param>
    /// <param name="gradosBajoElHorizonte">
    /// Cuantos grados por debajo del horizonte se traza. Cero es la linea exacta de dia y
    /// noche; seis, el borde exterior del paso gris.
    /// </param>
    /// <returns>Un punto por cada paso de longitud, en orden creciente.</returns>
    public static IReadOnlyList<Coordenada> Linea(
        DateTimeOffset instante,
        int puntos = PuntosPorOmision,
        double gradosBajoElHorizonte = 0.0)
    {
        var cuantos = Math.Max(PuntosMinimos, puntos);
        var sol = SubpuntoSolar(instante);

        var dec = ARadianes(sol.Latitud);
        var altura = ARadianes(-gradosBajoElHorizonte);

        // La declinacion pasa por cero dos veces al ano. Justo ahi la tangente se anula y la
        // linea es un meridiano exacto; se empuja lo justo para no dividir entre cero.
        var tangente = Math.Tan(dec);
        if (Math.Abs(tangente) < 1e-9) tangente = 1e-9 * (tangente < 0 ? -1 : 1);

        var salida = new List<Coordenada>(cuantos);
        for (var i = 0; i < cuantos; i++)
        {
            var longitud = -180.0 + (360.0 * i / (cuantos - 1));
            var angulo = ARadianes(NormalizarGrados(longitud - sol.Longitud));

            // Despeje de la altura del Sol para la latitud: donde el Sol esta a la altura
            // pedida, la latitud cumple tan(lat) = (sen h − sen δ·cos H) / (cos δ·cos H·...).
            // Con h = 0 queda la forma corta de toda la vida: tan(lat) = −cos H / tan δ.
            var latitud = Math.Abs(gradosBajoElHorizonte) < 1e-9
                ? AGrados(Math.Atan(-Math.Cos(angulo) / tangente))
                : LatitudConAltura(angulo, dec, altura);

            salida.Add(new Coordenada(Math.Clamp(latitud, -89.999, 89.999), longitud));
        }

        return salida;
    }

    /// <summary>
    /// La zona que esta a oscuras, ya cerrada como poligono para poder pintarla.
    /// </summary>
    /// <remarks>
    /// El poligono se cierra por el polo que esta en noche permanente: el sur cuando el Sol
    /// tira al norte y al reves. Sin ese cierre la sombra saldria por el hemisferio que no
    /// toca durante medio ano, que es el fallo clasico de los mapas de paso gris.
    /// </remarks>
    /// <param name="instante">Momento a considerar.</param>
    /// <param name="puntos">Puntos de la linea de dia y noche.</param>
    /// <returns>El contorno cerrado de la zona nocturna, en sentido de recorrido continuo.</returns>
    public static IReadOnlyList<Coordenada> ZonaNocturna(
        DateTimeOffset instante,
        int puntos = PuntosPorOmision)
    {
        var linea = Linea(instante, puntos);
        var sol = SubpuntoSolar(instante);

        // Con el Sol al norte, el polo norte tiene dia permanente y el sur, noche.
        var poloDeNoche = sol.Latitud >= 0 ? -90.0 : 90.0;

        var contorno = new List<Coordenada>(linea.Count + 3);
        contorno.AddRange(linea);
        contorno.Add(new Coordenada(poloDeNoche, linea[^1].Longitud));
        contorno.Add(new Coordenada(poloDeNoche, linea[0].Longitud));
        contorno.Add(linea[0]);

        return contorno;
    }

    /// <summary>
    /// Declinacion y ascension recta del Sol, y los dias transcurridos desde J2000.
    /// </summary>
    private static (double Declinacion, double Ascension, double DiasJulianos) PosicionSolar(
        DateTimeOffset instante)
    {
        var utc = instante.ToUniversalTime().UtcDateTime;

        // Dias desde el mediodia del 1 de enero de 2000 en tiempo terrestre, que para esta
        // precision se puede confundir con UTC sin remordimiento.
        var dias = (utc - new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc)).TotalDays;

        var longitudMedia = Normalizar360(280.460 + (0.9856474 * dias));
        var anomalia = ARadianes(Normalizar360(357.528 + (0.9856003 * dias)));

        // Longitud eclíptica: la media mas la correccion por lo ovalada que es la orbita.
        var ecliptica = ARadianes(longitudMedia
            + (1.915 * Math.Sin(anomalia))
            + (0.020 * Math.Sin(2 * anomalia)));

        var oblicuidad = ARadianes(23.439 - (0.0000004 * dias));

        var declinacion = Math.Asin(Math.Sin(oblicuidad) * Math.Sin(ecliptica));
        var ascension = Math.Atan2(
            Math.Cos(oblicuidad) * Math.Sin(ecliptica),
            Math.Cos(ecliptica));

        return (AGrados(declinacion), Normalizar360(AGrados(ascension)), dias);
    }

    /// <summary>
    /// Latitud a la que el Sol se ve a la altura pedida, para un angulo horario dado.
    /// </summary>
    private static double LatitudConAltura(double angulo, double declinacion, double altura)
    {
        // sen h = sen φ·sen δ + cos φ·cos δ·cos H se resuelve escribiendo el lado derecho
        // como R·sen(φ + ψ): entonces φ = arcsen(sen h / R) − ψ y se acabo la trigonometria.
        var a = Math.Sin(declinacion);
        var b = Math.Cos(declinacion) * Math.Cos(angulo);

        var radio = Math.Sqrt((a * a) + (b * b));
        if (radio < 1e-12) return 0.0;

        var desfase = Math.Atan2(b, a);
        var razon = Math.Clamp(Math.Sin(altura) / radio, -1.0, 1.0);

        return AGrados(Math.Asin(razon) - desfase);
    }

    private static double ARadianes(double grados) => grados * Math.PI / 180.0;

    private static double AGrados(double radianes) => radianes * 180.0 / Math.PI;

    /// <summary>Lleva un angulo al intervalo de 0 a 360 grados.</summary>
    private static double Normalizar360(double grados)
    {
        var resto = grados % 360.0;
        return resto < 0 ? resto + 360.0 : resto;
    }

    /// <summary>Lleva una longitud al intervalo de −180 a +180 grados.</summary>
    private static double NormalizarGrados(double grados)
    {
        var resto = Normalizar360(grados);
        return resto > 180.0 ? resto - 360.0 : resto;
    }
}
