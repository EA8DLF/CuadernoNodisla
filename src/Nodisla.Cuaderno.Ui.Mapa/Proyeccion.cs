using Mapsui.Projections;
using NetTopologySuite.Geometries;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Mapa;

/// <summary>
/// Paso de grados a las coordenadas planas del mapa.
/// </summary>
/// <remarks>
/// Todos los mapas de mosaicos del mundo usan la proyeccion de Mercator esferica, y esa
/// proyeccion manda el polo al infinito: a 90° de latitud la cuenta se va a un numero
/// imposible y el dibujo desaparece entero, no solo el punto que se sale. Por eso aqui se
/// recorta la latitud antes de proyectar nada. El limite es el mismo que usan todos.
/// </remarks>
internal static class Proyeccion
{
    /// <summary>Latitud maxima que admite la proyeccion de Mercator.</summary>
    public const double LatitudMaxima = 85.0511;

    /// <summary>Proyecta un punto de la Tierra al plano del mapa.</summary>
    public static MPunto A(Coordenada punto)
    {
        var latitud = Math.Clamp(punto.Latitud, -LatitudMaxima, LatitudMaxima);
        var longitud = Math.Clamp(punto.Longitud, -180.0, 180.0);

        var (x, y) = SphericalMercator.FromLonLat(longitud, latitud);
        return new MPunto(x, y);
    }

    /// <summary>Devuelve un punto del plano del mapa a grados.</summary>
    public static Coordenada Desde(double x, double y)
    {
        var (longitud, latitud) = SphericalMercator.ToLonLat(x, y);
        return new Coordenada(latitud, longitud);
    }

    /// <summary>Convierte una lista de puntos en la cadena de coordenadas que entiende la geometria.</summary>
    public static NetTopologySuite.Geometries.Coordinate[] ACadena(IReadOnlyList<Coordenada> puntos)
    {
        var cadena = new NetTopologySuite.Geometries.Coordinate[puntos.Count];
        for (var i = 0; i < puntos.Count; i++)
        {
            var proyectado = A(puntos[i]);
            cadena[i] = new NetTopologySuite.Geometries.Coordinate(proyectado.X, proyectado.Y);
        }

        return cadena;
    }

    /// <summary>Construye una linea del mapa a partir de puntos en grados.</summary>
    /// <returns>La linea, o nulo si no hay puntos suficientes para dibujarla.</returns>
    public static LineString? ALinea(IReadOnlyList<Coordenada> puntos) =>
        puntos.Count < 2 ? null : new LineString(ACadena(puntos));

    /// <summary>Construye un poligono cerrado a partir de puntos en grados.</summary>
    /// <returns>El poligono, o nulo si el contorno no da para cerrarlo.</returns>
    public static Polygon? APoligono(IReadOnlyList<Coordenada> contorno)
    {
        if (contorno.Count < 3) return null;

        var cadena = ACadena(contorno);

        // Un anillo tiene que cerrarse sobre si mismo; si el que lo pidio no lo cerro, se
        // cierra aqui en vez de fallar.
        if (!cadena[0].Equals2D(cadena[^1]))
        {
            var cerrada = new NetTopologySuite.Geometries.Coordinate[cadena.Length + 1];
            Array.Copy(cadena, cerrada, cadena.Length);
            cerrada[^1] = cadena[0].Copy();
            cadena = cerrada;
        }

        return cadena.Length < 4 ? null : new Polygon(new LinearRing(cadena));
    }
}

/// <summary>Un punto del plano del mapa. Existe para no sacar los tipos de Mapsui de aqui.</summary>
/// <param name="X">Coordenada este-oeste en metros de Mercator.</param>
/// <param name="Y">Coordenada norte-sur en metros de Mercator.</param>
internal readonly record struct MPunto(double X, double Y);
