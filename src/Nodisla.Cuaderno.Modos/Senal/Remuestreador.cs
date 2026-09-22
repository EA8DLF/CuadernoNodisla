namespace Nodisla.Cuaderno.Modos.Senal;

/// <summary>
/// Cambia la frecuencia de muestreo de un trozo de audio.
/// </summary>
/// <remarks>
/// <para>
/// La tarjeta de sonido entrega 48000 muestras por segundo y el decodificador trabaja a 12800
/// en FT8 y a 21333,33 en FT4, que son las frecuencias que hacen que un simbolo caiga en una
/// potencia de dos de muestras. Entre medias hace falta esto.
/// </para>
/// <para>
/// El metodo es el clasico: reconstruir la senal continua que habia detras de las muestras y
/// volver a medirla en los instantes nuevos. Reconstruir exige una funcion seno cardinal de
/// longitud infinita, asi que se recorta a unas decenas de muestras y se suaviza el corte con
/// una ventana de Blackman; sin ese suavizado, el recorte mete ondulaciones en la banda de paso
/// que se comen los decibelios que tanto cuesta ganar en las senales debiles.
/// </para>
/// <para>
/// Cuando se baja de frecuencia, la frecuencia de corte se ajusta a la nueva mitad de banda: si
/// no, todo lo que este por encima se doblaria hacia abajo y apareceria como ruido en medio de
/// la senal, que es el error mas dificil de ver despues.
/// </para>
/// </remarks>
public static class Remuestreador
{
    /// <summary>Muestras de nucleo a cada lado. Mas nucleo es mas fiel y mas lento.</summary>
    private const int MitadDelNucleo = 24;

    /// <summary>
    /// Puntos por muestra con los que se tabula el nucleo.
    /// </summary>
    /// <remarks>
    /// El nucleo se calcula una vez en una tabla fina y luego se interpola entre dos puntos. Sin
    /// esta tabla, remuestrear quince segundos de audio serian veintiocho millones de senos y
    /// cosenos, que es lo que tardaba antes: mas que todo el resto del decodificador junto.
    /// Con 256 puntos por muestra el error de la interpolacion queda muy por debajo del ruido de
    /// cuantificacion de la tarjeta de sonido.
    /// </remarks>
    private const int PuntosPorMuestra = 256;

    /// <summary>
    /// Remuestrea un bloque de audio.
    /// </summary>
    /// <param name="entrada">Muestras de partida.</param>
    /// <param name="frecuenciaDeEntrada">Muestras por segundo de partida.</param>
    /// <param name="frecuenciaDeSalida">Muestras por segundo de destino.</param>
    /// <returns>Las muestras a la nueva frecuencia.</returns>
    public static float[] Remuestrear(ReadOnlySpan<float> entrada, double frecuenciaDeEntrada, double frecuenciaDeSalida)
    {
        if (frecuenciaDeEntrada <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeEntrada));
        if (frecuenciaDeSalida <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeSalida));
        if (entrada.Length == 0) return [];

        var razon = frecuenciaDeEntrada / frecuenciaDeSalida;
        if (Math.Abs(razon - 1.0) < 1e-12) return entrada.ToArray();

        // Al bajar de frecuencia hay que recortar antes de decidir, o lo de arriba se dobla.
        var corte = razon > 1 ? 0.5 / razon : 0.5;
        var nucleo = TablaDelNucleo(corte);
        var salida = new float[(int)(entrada.Length / razon)];

        for (var n = 0; n < salida.Length; n++)
        {
            var posicion = n * razon;
            var centro = (int)Math.Floor(posicion);
            double suma = 0, peso = 0;

            for (var k = -MitadDelNucleo; k <= MitadDelNucleo; k++)
            {
                var indice = centro + k;
                if (indice < 0 || indice >= entrada.Length) continue;
                var h = ValorDelNucleo(nucleo, posicion - indice);
                suma += entrada[indice] * h;
                peso += h;
            }

            // Se divide por el peso para que un tramo de valor constante salga con ese mismo
            // valor tambien en los bordes, donde parte del nucleo se queda fuera.
            salida[n] = peso > 1e-12 ? (float)(suma / peso) : 0f;
        }
        return salida;
    }

    /// <summary>Tabula el nucleo de reconstruccion para una frecuencia de corte dada.</summary>
    private static double[] TablaDelNucleo(double corte)
    {
        var puntos = (MitadDelNucleo * PuntosPorMuestra) + 2;
        var tabla = new double[puntos];
        for (var i = 0; i < puntos; i++)
        {
            var x = (double)i / PuntosPorMuestra;
            tabla[i] = 2 * corte * SenoCardinal(2 * corte * x) * Blackman(x / MitadDelNucleo);
        }
        return tabla;
    }

    /// <summary>Lee el nucleo interpolando entre los dos puntos tabulados mas proximos.</summary>
    private static double ValorDelNucleo(double[] tabla, double distancia)
    {
        var x = Math.Abs(distancia) * PuntosPorMuestra;
        var i = (int)x;
        if (i >= tabla.Length - 1) return 0;
        var f = x - i;
        return tabla[i] + ((tabla[i + 1] - tabla[i]) * f);
    }

    private static double SenoCardinal(double x) =>
        Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);

    private static double Blackman(double x)
    {
        if (Math.Abs(x) >= 1) return 0;
        var t = Math.PI * (x + 1) / 2;
        return 0.42 - (0.5 * Math.Cos(2 * t)) + (0.08 * Math.Cos(4 * t));
    }
}
