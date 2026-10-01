using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Satelites.Orbital;

/// <summary>
/// La estacion desde la que se mira: su coordenada y su altura sobre el elipsoide.
/// </summary>
/// <param name="Coordenada">Latitud y longitud en grados decimales.</param>
/// <param name="AlturaMetros">Altura sobre el elipsoide, en metros.</param>
/// <remarks>
/// La altura importa menos de lo que parece para el azimut pero cuenta en la elevacion de los
/// pasos rasantes, que son justo los que se discuten. Se pide en metros porque es como la da
/// el GPS y como la guarda el cuaderno.
/// </remarks>
public readonly record struct Observador(Coordenada Coordenada, double AlturaMetros = 0.0)
{
    private const double SemiejeMayorKm = 6378.137;
    private const double Aplanamiento = 1.0 / 298.257223563;

    /// <summary>Construye el observador a partir de un localizador Maidenhead.</summary>
    /// <param name="locator">Localizador; se toma el centro de la casilla.</param>
    /// <param name="alturaMetros">Altura sobre el elipsoide, en metros.</param>
    /// <remarks>
    /// Se apoya en <see cref="Coordenada.Desde(Locator)"/> del dominio, que ya resuelve el
    /// centro de la casilla. Un localizador de seis caracteres deja una incertidumbre de unos
    /// tres kilometros: sobra para apuntar, y para pasos rasantes conviene dar la coordenada.
    /// </remarks>
    public static Observador DesdeLocator(Locator locator, double alturaMetros = 0.0) =>
        new(Coordenada.Desde(locator), alturaMetros);

    /// <summary>Posicion de la estacion en coordenadas fijas a la Tierra, en kilometros.</summary>
    /// <remarks>
    /// Se usa el elipsoide WGS-84 y no una esfera: en latitudes medias la diferencia entre el
    /// radio esferico y el geodesico llega a veintiun kilometros, que en un paso rasante es la
    /// diferencia entre ver el satelite y no verlo.
    /// </remarks>
    public Vector3 PosicionFija()
    {
        var lat = Coordenada.Latitud * Tiempos.GradosARadianes;
        var lon = Coordenada.Longitud * Tiempos.GradosARadianes;
        var alturaKm = AlturaMetros / 1000.0;

        var e2 = Aplanamiento * (2.0 - Aplanamiento);
        var sinLat = Math.Sin(lat);
        var n = SemiejeMayorKm / Math.Sqrt(1.0 - (e2 * sinLat * sinLat));

        return new Vector3(
            (n + alturaKm) * Math.Cos(lat) * Math.Cos(lon),
            (n + alturaKm) * Math.Cos(lat) * Math.Sin(lon),
            ((n * (1.0 - e2)) + alturaKm) * sinLat);
    }
}

/// <summary>Lo que se ve del satelite desde la estacion en un instante.</summary>
/// <param name="AzimutGrados">Azimut desde el norte verdadero, de 0 a 360.</param>
/// <param name="ElevacionGrados">Elevacion sobre el horizonte; negativa si esta debajo.</param>
/// <param name="DistanciaKm">Distancia en linea recta hasta el satelite.</param>
/// <param name="VelocidadRadialKmS">
/// Velocidad de alejamiento en km/s: positiva si se aleja, negativa si se acerca.
/// Es la que manda en el desplazamiento Doppler.
/// </param>
public readonly record struct VistaDesdeTierra(
    double AzimutGrados,
    double ElevacionGrados,
    double DistanciaKm,
    double VelocidadRadialKmS)
{
    /// <summary>El satelite esta por encima del horizonte geometrico.</summary>
    public bool SobreElHorizonte => ElevacionGrados > 0.0;
}

/// <summary>Convierte la posicion del satelite en azimut, elevacion y distancia.</summary>
public static class Topocentrico
{
    /// <summary>Calcula lo que ve la estacion a partir del estado en TEME.</summary>
    /// <param name="estado">Posicion y velocidad del satelite en TEME.</param>
    /// <param name="observador">La estacion.</param>
    /// <returns>Azimut, elevacion, distancia y velocidad radial.</returns>
    public static VistaDesdeTierra Mirar(EstadoTeme estado, Observador observador)
    {
        var sidereo = Tiempos.TiempoSidereoGreenwich(Tiempos.DiaJuliano(estado.Instante));
        var posicionFija = Tiempos.TemeAFijo(estado.Posicion, sidereo);
        var velocidadFija = Tiempos.VelocidadTemeAFija(estado.Velocidad, posicionFija, sidereo);

        return MirarFijo(posicionFija, velocidadFija, observador);
    }

    /// <summary>
    /// Calcula lo que ve la estacion a partir de una posicion ya en coordenadas fijas.
    /// </summary>
    /// <param name="posicionFija">Posicion del satelite en el sistema fijo a la Tierra, en km.</param>
    /// <param name="velocidadFija">Velocidad en ese mismo sistema, en km/s.</param>
    /// <param name="observador">La estacion.</param>
    /// <returns>Azimut, elevacion, distancia y velocidad radial.</returns>
    /// <remarks>
    /// Se separa de <see cref="Mirar"/> para que la puedan usar tambien los satelites
    /// geoestacionarios, cuya posicion no sale de SGP4 sino de su longitud sobre el ecuador.
    /// </remarks>
    public static VistaDesdeTierra MirarFijo(
        Vector3 posicionFija,
        Vector3 velocidadFija,
        Observador observador)
    {
        var sitio = observador.PosicionFija();
        var rho = posicionFija - sitio;

        var lat = observador.Coordenada.Latitud * Tiempos.GradosARadianes;
        var lon = observador.Coordenada.Longitud * Tiempos.GradosARadianes;
        var sinLat = Math.Sin(lat);
        var cosLat = Math.Cos(lat);
        var sinLon = Math.Sin(lon);
        var cosLon = Math.Cos(lon);

        // Sistema sur-este-cenit, el de toda la vida para apuntar antenas.
        var sur = (sinLat * cosLon * rho.X) + (sinLat * sinLon * rho.Y) - (cosLat * rho.Z);
        var este = (-sinLon * rho.X) + (cosLon * rho.Y);
        var cenit = (cosLat * cosLon * rho.X) + (cosLat * sinLon * rho.Y) + (sinLat * rho.Z);

        var distancia = rho.Modulo;

        // El cociente se acota antes del arcoseno. En la vertical exacta y en el punto
        // diametralmente opuesto, el redondeo de coma flotante lo saca un pelo de [-1, 1] y
        // Math.Asin devuelve NaN: una elevacion que no es un numero se propaga a todo lo demas
        // y el fallo aparece lejos de aqui.
        var seno = distancia > 0 ? Math.Clamp(cenit / distancia, -1.0, 1.0) : 0.0;
        var elevacion = Math.Asin(seno) * Tiempos.RadianesAGrados;
        var azimut = Tiempos.NormalizarGrados(Math.Atan2(este, -sur) * Tiempos.RadianesAGrados);

        // La estacion no se mueve en el sistema fijo, asi que la derivada de la distancia es
        // la proyeccion de la velocidad del satelite sobre la visual.
        var radial = distancia > 0 ? rho.Escalar(velocidadFija) / distancia : 0.0;

        return new VistaDesdeTierra(azimut, elevacion, distancia, radial);
    }

    /// <summary>Punto de la superficie sobre el que esta el satelite, y su altura.</summary>
    /// <param name="estado">Posicion del satelite en TEME.</param>
    /// <returns>Coordenada del subpunto y altura sobre el elipsoide en kilometros.</returns>
    /// <remarks>
    /// La latitud geodesica se saca por iteracion; converge en tres o cuatro vueltas para
    /// cualquier orbita. Se devuelve una <see cref="Coordenada"/> del dominio para que el mapa
    /// y la geodesia existentes la puedan usar sin conversiones.
    /// </remarks>
    public static (Coordenada Subpunto, double AlturaKm) Subpunto(EstadoTeme estado)
    {
        var sidereo = Tiempos.TiempoSidereoGreenwich(Tiempos.DiaJuliano(estado.Instante));
        var r = Tiempos.TemeAFijo(estado.Posicion, sidereo);

        const double a = 6378.137;
        const double f = 1.0 / 298.257223563;
        var e2 = f * (2.0 - f);

        var lon = Math.Atan2(r.Y, r.X);
        var p = Math.Sqrt((r.X * r.X) + (r.Y * r.Y));
        var lat = Math.Atan2(r.Z, p);

        for (var i = 0; i < 8; i++)
        {
            var sinLat = Math.Sin(lat);
            var n = a / Math.Sqrt(1.0 - (e2 * sinLat * sinLat));
            // Altura por la forma estable, sin dividir entre el coseno: en los pasos polares
            // el coseno se acerca a cero y la formula directa se desmadra.
            var altura = (p * Math.Cos(lat)) + (r.Z * sinLat) - (a * Math.Sqrt(1.0 - (e2 * sinLat * sinLat)));
            var nueva = Math.Atan2(r.Z, p * (1.0 - (e2 * n / (n + altura))));
            var cambio = Math.Abs(nueva - lat);
            lat = nueva;
            if (cambio < 1e-12)
            {
                break;
            }
        }

        var sinFinal = Math.Sin(lat);
        var alturaFinal = (p * Math.Cos(lat)) + (r.Z * sinFinal)
                          - (a * Math.Sqrt(1.0 - (e2 * sinFinal * sinFinal)));
        var grados = new Coordenada(lat * Tiempos.RadianesAGrados, lon * Tiempos.RadianesAGrados);
        return (grados, alturaFinal);
    }
}
