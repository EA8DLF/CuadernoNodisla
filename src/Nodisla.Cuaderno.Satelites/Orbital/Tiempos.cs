namespace Nodisla.Cuaderno.Satelites.Orbital;

/// <summary>Un vector de tres componentes en kilometros o en kilometros por segundo.</summary>
/// <param name="X">Primera componente.</param>
/// <param name="Y">Segunda componente.</param>
/// <param name="Z">Tercera componente.</param>
public readonly record struct Vector3(double X, double Y, double Z)
{
    /// <summary>Modulo del vector.</summary>
    public double Modulo => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

    /// <summary>Producto escalar con otro vector.</summary>
    /// <param name="otro">El otro vector.</param>
    public double Escalar(Vector3 otro) => (X * otro.X) + (Y * otro.Y) + (Z * otro.Z);

    /// <summary>Resta de vectores.</summary>
    public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Suma de vectores.</summary>
    public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

/// <summary>
/// Conversiones de tiempo y de sistema de referencia que necesita el propagador.
/// </summary>
/// <remarks>
/// SGP4 entrega la posicion en el sistema TEME (equinoccio verdadero y ecuador medio de la
/// fecha), que no es el sistema en el que esta la estacion. Sin girar por el tiempo sidereo
/// el azimut sale mal por decenas de grados: es el fallo mas comun de quien escribe un
/// seguidor de satelites por primera vez.
/// </remarks>
public static class Tiempos
{
    /// <summary>Dos veces pi, que aparece en casi todas las cuentas de aqui.</summary>
    public const double DosPi = 2.0 * Math.PI;

    /// <summary>Grados a radianes.</summary>
    public const double GradosARadianes = Math.PI / 180.0;

    /// <summary>Radianes a grados.</summary>
    public const double RadianesAGrados = 180.0 / Math.PI;

    /// <summary>Velocidad angular de la Tierra en radianes por segundo.</summary>
    /// <remarks>
    /// Es la velocidad sidereo, no la de un dia solar: la Tierra da una vuelta sobre si misma
    /// en 23 h 56 min 4 s. Usar 86400 segundos aqui mete un error de velocidad radial que se
    /// nota en la correccion Doppler.
    /// </remarks>
    public const double VelocidadAngularTierra = 7.292115e-5;

    /// <summary>Dia juliano correspondiente a un instante UTC.</summary>
    /// <param name="instante">Instante que se convierte.</param>
    public static double DiaJuliano(DateTimeOffset instante)
    {
        var utc = instante.ToUniversalTime();
        var anio = utc.Year;
        var mes = utc.Month;
        double dia = utc.Day;
        var fraccion = ((utc.Hour * 3600.0) + (utc.Minute * 60.0) + utc.Second
                        + (utc.Millisecond / 1000.0)) / 86400.0;

        if (mes <= 2)
        {
            anio -= 1;
            mes += 12;
        }

        var a = anio / 100;
        var b = 2 - a + (a / 4);

        return Math.Floor(365.25 * (anio + 4716))
               + Math.Floor(30.6001 * (mes + 1))
               + dia + b - 1524.5 + fraccion;
    }

    /// <summary>Instante UTC correspondiente a un dia juliano.</summary>
    /// <param name="diaJuliano">Dia juliano.</param>
    public static DateTimeOffset DesdeDiaJuliano(double diaJuliano)
    {
        // Se usa el dia juliano del 1 de enero de 0001 como origen para no perder precision
        // en la parte entera, que es grande.
        var ticks = (diaJuliano - 1721425.5) * TimeSpan.TicksPerDay;
        return new DateTimeOffset(new DateTime((long)Math.Round(ticks), DateTimeKind.Utc));
    }

    /// <summary>
    /// Tiempo sidereo medio de Greenwich en radianes, formula del IAU de 1982.
    /// </summary>
    /// <param name="diaJuliano">Dia juliano UT1 (se usa UTC, la diferencia es menor de un segundo).</param>
    /// <remarks>
    /// Es la misma expresion que usa el codigo de referencia de Vallado. Se escribe aqui y no
    /// se toma de otra parte porque el resultado tiene que casar con SGP4 hasta el ultimo
    /// decimal para que las pruebas de verificacion valgan de algo.
    /// </remarks>
    public static double TiempoSidereoGreenwich(double diaJuliano)
    {
        var tut1 = (diaJuliano - 2451545.0) / 36525.0;
        var temp = (-6.2e-6 * tut1 * tut1 * tut1)
                   + (0.093104 * tut1 * tut1)
                   + (((876600.0 * 3600.0) + 8640184.812866) * tut1)
                   + 67310.54841;

        temp = Normalizar(temp * GradosARadianes / 240.0);
        return temp;
    }

    /// <summary>Deja un angulo en radianes dentro del intervalo de cero a dos pi.</summary>
    /// <param name="radianes">Angulo de entrada.</param>
    public static double Normalizar(double radianes)
    {
        var r = radianes % DosPi;
        return r < 0 ? r + DosPi : r;
    }

    /// <summary>Deja un angulo en grados dentro del intervalo de cero a 360.</summary>
    /// <param name="grados">Angulo de entrada.</param>
    public static double NormalizarGrados(double grados)
    {
        var g = grados % 360.0;
        return g < 0 ? g + 360.0 : g;
    }

    /// <summary>
    /// Gira la posicion de TEME a coordenadas fijas a la Tierra, sin movimiento del polo.
    /// </summary>
    /// <param name="posicionTeme">Posicion en TEME, en kilometros.</param>
    /// <param name="tiempoSidereo">Tiempo sidereo de Greenwich en radianes.</param>
    /// <remarks>
    /// Se desprecia el movimiento del polo a proposito: son decimas de segundo de arco, del
    /// orden de diez metros sobre la superficie, y exigiria descargar los parametros de la IERS
    /// todas las semanas. Para apuntar una antena de aficionado sobra.
    /// </remarks>
    public static Vector3 TemeAFijo(Vector3 posicionTeme, double tiempoSidereo)
    {
        var c = Math.Cos(tiempoSidereo);
        var s = Math.Sin(tiempoSidereo);
        return new Vector3(
            (posicionTeme.X * c) + (posicionTeme.Y * s),
            (-posicionTeme.X * s) + (posicionTeme.Y * c),
            posicionTeme.Z);
    }

    /// <summary>
    /// Gira la velocidad de TEME a coordenadas fijas, descontando el giro de la propia Tierra.
    /// </summary>
    /// <param name="velocidadTeme">Velocidad en TEME, en km/s.</param>
    /// <param name="posicionFija">Posicion ya girada, en kilometros.</param>
    /// <param name="tiempoSidereo">Tiempo sidereo de Greenwich en radianes.</param>
    /// <remarks>
    /// El termino que se resta es la velocidad de arrastre del sistema que gira. Olvidarlo deja
    /// una velocidad radial equivocada en cientos de metros por segundo y una correccion Doppler
    /// que no sirve.
    /// </remarks>
    public static Vector3 VelocidadTemeAFija(
        Vector3 velocidadTeme,
        Vector3 posicionFija,
        double tiempoSidereo)
    {
        var c = Math.Cos(tiempoSidereo);
        var s = Math.Sin(tiempoSidereo);
        var girada = new Vector3(
            (velocidadTeme.X * c) + (velocidadTeme.Y * s),
            (-velocidadTeme.X * s) + (velocidadTeme.Y * c),
            velocidadTeme.Z);

        return new Vector3(
            girada.X + (VelocidadAngularTierra * posicionFija.Y),
            girada.Y - (VelocidadAngularTierra * posicionFija.X),
            girada.Z);
    }
}
