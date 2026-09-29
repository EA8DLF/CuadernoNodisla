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
/// <para>
/// <b>El atajo que lo hace rapido.</b> Las frecuencias que se usan de verdad estan en razon
/// sencilla: de 48000 a 12800 son quince cuartos, de 48000 a 21333,33 son nueve cuartos. Cuando
/// la razon es asi, los instantes en que hay que medir se repiten ciclicamente cada pocas
/// muestras, de modo que solo existen unos pocos juegos de pesos distintos. Se calculan una vez
/// y despues remuestrear es sumar y multiplicar. Si la razon no fuera sencilla —una grabacion a
/// una frecuencia rara— se cae a una tabla fina interpolada, que es algo mas lenta pero vale
/// para cualquier cosa.
/// </para>
/// </remarks>
public static class Remuestreador
{
    /// <summary>Muestras de nucleo a cada lado. Mas nucleo es mas fiel y mas lento.</summary>
    private const int MitadDelNucleo = 24;

    /// <summary>Pesos que tiene un juego completo.</summary>
    private const int PesosPorJuego = (2 * MitadDelNucleo) + 1;

    /// <summary>Juegos de pesos distintos que se aceptan antes de rendirse y usar la tabla.</summary>
    private const int MaximoDeFases = 4096;

    /// <summary>Puntos por muestra con los que se tabula el nucleo cuando la razon no es sencilla.</summary>
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
        var salida = new float[(int)(entrada.Length / razon)];

        if (TryRazonSencilla(frecuenciaDeEntrada, frecuenciaDeSalida, out var arriba, out var abajo))
            PorFases(entrada, salida, arriba, abajo, corte);
        else
            PorTabla(entrada, salida, razon, corte);

        return salida;
    }

    /// <summary>
    /// Busca si las dos frecuencias estan en una razon de numeros pequenos.
    /// </summary>
    /// <remarks>
    /// Se prueban denominadores crecientes y se acepta el primero que reproduzca la razon con
    /// error despreciable. Las frecuencias del modem (48000 a 12800 y a 64000/3) salen a la
    /// primera, con denominadores de cuatro y tres.
    /// </remarks>
    private static bool TryRazonSencilla(double entrada, double salida, out int arriba, out int abajo)
    {
        var razon = entrada / salida;
        for (abajo = 1; abajo <= MaximoDeFases; abajo++)
        {
            var candidato = razon * abajo;
            arriba = (int)Math.Round(candidato);
            if (arriba > 0 && Math.Abs(candidato - arriba) < 1e-9) return true;
        }
        arriba = 0;
        abajo = 0;
        return false;
    }

    /// <summary>Remuestrea con juegos de pesos precalculados, uno por fase del ciclo.</summary>
    private static void PorFases(ReadOnlySpan<float> entrada, float[] salida, int arriba, int abajo, double corte)
    {
        // La muestra de salida n cae en la posicion n*arriba/abajo de la entrada: su parte entera
        // dice de que muestra se parte y el resto de la division dice que juego de pesos toca.
        var pesos = new float[abajo * PesosPorJuego];
        for (var fase = 0; fase < abajo; fase++)
        {
            var desplazamiento = (double)fase / abajo;
            for (var k = -MitadDelNucleo; k <= MitadDelNucleo; k++)
                pesos[(fase * PesosPorJuego) + k + MitadDelNucleo] = (float)Nucleo(desplazamiento - k, corte);
        }

        // Los pesos de cada fase se normalizan para que un tramo de valor constante salga con ese
        // mismo valor: el recorte del nucleo hace que no sumen exactamente uno.
        for (var fase = 0; fase < abajo; fase++)
        {
            double suma = 0;
            for (var i = 0; i < PesosPorJuego; i++) suma += pesos[(fase * PesosPorJuego) + i];
            if (suma <= 1e-12) continue;
            var escala = (float)(1.0 / suma);
            for (var i = 0; i < PesosPorJuego; i++) pesos[(fase * PesosPorJuego) + i] *= escala;
        }

        for (var n = 0; n < salida.Length; n++)
        {
            var avance = (long)n * arriba;
            var centro = (int)(avance / abajo);
            var juego = (int)(avance % abajo) * PesosPorJuego;

            var primera = centro - MitadDelNucleo;
            if (primera >= 0 && primera + PesosPorJuego <= entrada.Length)
            {
                // Camino de dentro: no hace falta comprobar los bordes en cada peso.
                var trozo = entrada.Slice(primera, PesosPorJuego);
                float suma = 0;
                for (var i = 0; i < PesosPorJuego; i++) suma += trozo[i] * pesos[juego + i];
                salida[n] = suma;
            }
            else
            {
                float suma = 0, peso = 0;
                for (var i = 0; i < PesosPorJuego; i++)
                {
                    var indice = primera + i;
                    if (indice < 0 || indice >= entrada.Length) continue;
                    suma += entrada[indice] * pesos[juego + i];
                    peso += pesos[juego + i];
                }
                salida[n] = peso > 1e-9f ? suma / peso : 0f;
            }
        }
    }

    /// <summary>Remuestrea interpolando en una tabla fina, para razones que no son sencillas.</summary>
    private static void PorTabla(ReadOnlySpan<float> entrada, float[] salida, double razon, double corte)
    {
        var puntos = (MitadDelNucleo * PuntosPorMuestra) + 2;
        var tabla = new double[puntos];
        for (var i = 0; i < puntos; i++) tabla[i] = Nucleo((double)i / PuntosPorMuestra, corte);

        for (var n = 0; n < salida.Length; n++)
        {
            var posicion = n * razon;
            var centro = (int)Math.Floor(posicion);
            double suma = 0, peso = 0;

            for (var k = -MitadDelNucleo; k <= MitadDelNucleo; k++)
            {
                var indice = centro + k;
                if (indice < 0 || indice >= entrada.Length) continue;
                var h = EnLaTabla(tabla, posicion - indice);
                suma += entrada[indice] * h;
                peso += h;
            }
            salida[n] = peso > 1e-12 ? (float)(suma / peso) : 0f;
        }
    }

    private static double EnLaTabla(double[] tabla, double distancia)
    {
        var x = Math.Abs(distancia) * PuntosPorMuestra;
        var i = (int)x;
        if (i >= tabla.Length - 1) return 0;
        return tabla[i] + ((tabla[i + 1] - tabla[i]) * (x - i));
    }

    /// <summary>Valor del nucleo de reconstruccion a esa distancia de la muestra.</summary>
    private static double Nucleo(double distancia, double corte)
    {
        var x = Math.Abs(distancia);
        if (x >= MitadDelNucleo) return 0;
        return 2 * corte * SenoCardinal(2 * corte * x) * Blackman(x / MitadDelNucleo);
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
