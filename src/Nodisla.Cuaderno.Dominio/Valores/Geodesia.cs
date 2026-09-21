namespace Nodisla.Cuaderno.Dominio.Valores;

/// <summary>Un punto en la superficie terrestre, en grados decimales.</summary>
public readonly record struct Coordenada(double Latitud, double Longitud)
{
    public static Coordenada Desde(Locator locator)
    {
        var (lat, lon) = locator.ACoordenadas();
        return new Coordenada(lat, lon);
    }

    public Locator ALocator(int precision = 3) => Locator.DesdeCoordenadas(Latitud, Longitud, precision);
}

/// <summary>Calculos de circulo maximo sobre esfera, suficientes para trabajo de radio.</summary>
public static class Geodesia
{
    /// <summary>Radio medio terrestre en kilometros (esfera IUGG).</summary>
    public const double RadioTerrestreKm = 6371.0088;

    /// <summary>Distancia por circulo maximo entre dos puntos, en kilometros.</summary>
    public static double DistanciaKm(Coordenada origen, Coordenada destino)
    {
        var lat1 = Grados.ARadianes(origen.Latitud);
        var lat2 = Grados.ARadianes(destino.Latitud);
        var dLat = lat2 - lat1;
        var dLon = Grados.ARadianes(destino.Longitud - origen.Longitud);

        var a = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * RadioTerrestreKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Rumbo inicial de origen a destino, en grados desde el norte verdadero (0-360).</summary>
    public static double RumboGrados(Coordenada origen, Coordenada destino)
    {
        var lat1 = Grados.ARadianes(origen.Latitud);
        var lat2 = Grados.ARadianes(destino.Latitud);
        var dLon = Grados.ARadianes(destino.Longitud - origen.Longitud);

        var y = Math.Sin(dLon) * Math.Cos(lat2);
        var x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        var rumbo = Grados.DesdeRadianes(Math.Atan2(y, x));
        return (rumbo + 360) % 360;
    }

    /// <summary>Rumbo largo, el del otro lado del mundo.</summary>
    public static double RumboLargoGrados(Coordenada origen, Coordenada destino) =>
        (RumboGrados(origen, destino) + 180) % 360;

    /// <summary>Punto antipoda, util para el camino largo y para los mapas.</summary>
    public static Coordenada Antipoda(Coordenada punto) =>
        new(-punto.Latitud, punto.Longitud > 0 ? punto.Longitud - 180 : punto.Longitud + 180);

    private static class Grados
    {
        public static double ARadianes(double grados) => grados * Math.PI / 180.0;
        public static double DesdeRadianes(double radianes) => radianes * 180.0 / Math.PI;
    }
}
