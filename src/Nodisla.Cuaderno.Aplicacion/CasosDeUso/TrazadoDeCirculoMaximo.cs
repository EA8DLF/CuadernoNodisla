using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Cual de los dos caminos de la circunferencia maxima se esta trazando.</summary>
public enum CaminoDelTrayecto
{
    /// <summary>El arco menor, por el que normalmente entra la senal.</summary>
    Corto,

    /// <summary>El arco mayor, el que da la vuelta por el otro lado del mundo.</summary>
    Largo,
}

/// <summary>
/// Traza el camino que sigue la senal entre dos puntos de la Tierra.
/// </summary>
/// <remarks>
/// Sobre un mapa plano el camino de la radio no es una recta sino un arco, y el que mas se
/// nota es el largo: hacia las antipodas el corto y el largo se parecen poco y el operador
/// gira la antena a uno o a otro. Por eso se trazan los dos.
///
/// El calculo se hace en vectores unitarios y no en grados: interpolar latitudes y longitudes
/// da curvas falsas cerca de los polos, y ahi es justo donde pasan los caminos interesantes
/// desde Canarias hacia Japon o Australia.
/// </remarks>
public static class TrazadoDeCirculoMaximo
{
    /// <summary>Puntos con los que se dibuja un trayecto si no se pide otra cosa.</summary>
    public const int PuntosPorOmision = 96;

    /// <summary>Menos puntos que esto no dibujan una curva, sino una recta quebrada.</summary>
    public const int PuntosMinimos = 8;

    private const double Epsilon = 1e-12;

    /// <summary>
    /// Devuelve los puntos por los que pasa el camino pedido, del origen al destino.
    /// </summary>
    /// <param name="origen">Punto de salida, normalmente la estacion propia.</param>
    /// <param name="destino">Punto de llegada.</param>
    /// <param name="camino">Arco corto o arco largo.</param>
    /// <param name="puntos">Cuantos puntos se calculan, extremos incluidos.</param>
    /// <returns>La lista de puntos, siempre con el origen el primero y el destino el ultimo.</returns>
    public static IReadOnlyList<Coordenada> Trazar(
        Coordenada origen,
        Coordenada destino,
        CaminoDelTrayecto camino = CaminoDelTrayecto.Corto,
        int puntos = PuntosPorOmision)
    {
        var cuantos = Math.Max(PuntosMinimos, puntos);

        var a = AVector(origen);
        var b = AVector(destino);

        var coseno = Math.Clamp(Producto(a, b), -1.0, 1.0);
        var separacion = Math.Acos(coseno);

        // Mismo punto: por el lado corto no hay nada que dibujar. Por el largo si: se da la
        // vuelta entera al planeta, y esa linea es justamente la que el operador quiere ver.
        if (separacion < Epsilon && camino == CaminoDelTrayecto.Corto)
        {
            return [origen, destino];
        }

        var tangente = Tangente(a, b, separacion);
        var recorrido = camino == CaminoDelTrayecto.Corto
            ? separacion
            : (2 * Math.PI) - separacion;

        if (camino == CaminoDelTrayecto.Largo)
        {
            tangente = (-tangente.X, -tangente.Y, -tangente.Z);
        }

        var salida = new List<Coordenada>(cuantos);
        for (var i = 0; i < cuantos; i++)
        {
            var avance = recorrido * i / (cuantos - 1);
            var seno = Math.Sin(avance);
            var cos = Math.Cos(avance);

            salida.Add(ACoordenada((
                (a.X * cos) + (tangente.X * seno),
                (a.Y * cos) + (tangente.Y * seno),
                (a.Z * cos) + (tangente.Z * seno))));
        }

        // Los extremos se ponen tal cual: el redondeo del seno y del coseno los deja a
        // millonesimas de grado, y en pantalla eso despega la linea de su marca.
        salida[0] = origen;
        salida[^1] = destino;
        return salida;
    }

    /// <summary>
    /// Parte un trayecto por el antimeridiano.
    /// </summary>
    /// <remarks>
    /// En un mapa plano, un camino que cruza la linea de cambio de fecha salta de +180 a −180
    /// y el dibujo se convierte en una raya horizontal que atraviesa el mundo entero. Partirlo
    /// en tramos es la unica manera de que se vea lo que de verdad pasa.
    /// </remarks>
    /// <param name="trazo">Puntos del trayecto, en orden.</param>
    /// <returns>Uno o varios tramos; ninguno cruza el antimeridiano.</returns>
    public static IReadOnlyList<IReadOnlyList<Coordenada>> PartirPorElAntimeridiano(
        IReadOnlyList<Coordenada> trazo)
    {
        ArgumentNullException.ThrowIfNull(trazo);
        if (trazo.Count < 2) return [trazo];

        var tramos = new List<IReadOnlyList<Coordenada>>();
        var tramo = new List<Coordenada> { trazo[0] };

        for (var i = 1; i < trazo.Count; i++)
        {
            var anterior = trazo[i - 1];
            var actual = trazo[i];

            // Un salto de mas de media vuelta entre dos puntos seguidos solo puede ser el
            // corte del mapa, porque el paso entre puntos es siempre mucho menor.
            if (Math.Abs(actual.Longitud - anterior.Longitud) > 180.0)
            {
                var (bordeDeSalida, bordeDeEntrada) = CorteEnElBorde(anterior, actual);
                tramo.Add(bordeDeSalida);
                tramos.Add(tramo);
                tramo = [bordeDeEntrada];
            }

            tramo.Add(actual);
        }

        tramos.Add(tramo);
        return tramos;
    }

    /// <summary>
    /// Los dos puntos del borde donde el trayecto sale del mapa y vuelve a entrar, con la
    /// latitud interpolada entre los dos puntos que se saltan el antimeridiano.
    /// </summary>
    private static (Coordenada Salida, Coordenada Entrada) CorteEnElBorde(
        Coordenada anterior,
        Coordenada actual)
    {
        var haciaElEste = actual.Longitud < anterior.Longitud;
        var bordeDeSalida = haciaElEste ? 180.0 : -180.0;
        var bordeDeEntrada = -bordeDeSalida;

        var recorrido = haciaElEste
            ? (actual.Longitud + 360.0) - anterior.Longitud
            : (actual.Longitud - 360.0) - anterior.Longitud;

        var parte = Math.Abs(recorrido) < Epsilon
            ? 0.5
            : (bordeDeSalida - anterior.Longitud) / recorrido;

        var latitud = anterior.Latitud + ((actual.Latitud - anterior.Latitud) * parte);

        return (new Coordenada(latitud, bordeDeSalida), new Coordenada(latitud, bordeDeEntrada));
    }

    /// <summary>
    /// Vector unitario perpendicular al origen dentro del plano del circulo maximo, apuntando
    /// hacia el destino. Con el, el arco se recorre girando: origen·cos t + tangente·sen t.
    /// </summary>
    private static (double X, double Y, double Z) Tangente(
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b,
        double separacion)
    {
        // Antipodas o punto repetido: el plano del circulo no esta determinado, porque hay
        // infinitos que pasan por los dos. Se elige uno cualquiera que contenga al origen.
        if (separacion < Epsilon || Math.Abs(separacion - Math.PI) < Epsilon)
        {
            var eje = Math.Abs(a.Z) < 0.9 ? (0.0, 0.0, 1.0) : (1.0, 0.0, 0.0);
            return Normalizar(Restar(eje, Escalar(a, Producto(a, eje))));
        }

        return Normalizar(Restar(b, Escalar(a, Producto(a, b))));
    }

    private static (double X, double Y, double Z) AVector(Coordenada punto)
    {
        var lat = punto.Latitud * Math.PI / 180.0;
        var lon = punto.Longitud * Math.PI / 180.0;
        var cosLat = Math.Cos(lat);
        return (cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
    }

    private static Coordenada ACoordenada((double X, double Y, double Z) v)
    {
        var lat = Math.Atan2(v.Z, Math.Sqrt((v.X * v.X) + (v.Y * v.Y))) * 180.0 / Math.PI;
        var lon = Math.Atan2(v.Y, v.X) * 180.0 / Math.PI;
        return new Coordenada(lat, lon);
    }

    private static double Producto((double X, double Y, double Z) a, (double X, double Y, double Z) b) =>
        (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static (double X, double Y, double Z) Restar(
        (double X, double Y, double Z) a,
        (double X, double Y, double Z) b) =>
        (a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    private static (double X, double Y, double Z) Escalar((double X, double Y, double Z) a, double f) =>
        (a.X * f, a.Y * f, a.Z * f);

    private static (double X, double Y, double Z) Normalizar((double X, double Y, double Z) a)
    {
        var largo = Math.Sqrt(Producto(a, a));
        return largo < Epsilon ? (1.0, 0.0, 0.0) : (a.X / largo, a.Y / largo, a.Z / largo);
    }
}
