using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Jt9;

/// <summary>Un sitio de la ventana donde parece sonar el sincronismo de JT9.</summary>
/// <param name="MedioSimbolo">Medio simbolo (3456 muestras) en que empezaria la senal, contado desde el principio de la ventana; puede ser negativo.</param>
/// <param name="Casilla">Casilla del espectrograma (de 0,73 Hz) donde caeria el tono de sincronismo.</param>
/// <param name="Puntuacion">Potencia media en los intervalos de sincronismo partida por la de los intervalos de datos, en la misma casilla. Uno es ruido.</param>
public readonly record struct CandidataJt9(int MedioSimbolo, int Casilla, double Puntuacion);

/// <summary>
/// Espectrograma de la ventana con la resolucion que necesita el sincronismo de JT9: un bloque
/// cada medio simbolo, casillas de 0,73 Hz (menos de la mitad del espaciado de tonos).
/// </summary>
/// <remarks>
/// Cada bloque es la transformada de un simbolo entero (6912 muestras) rellenado con ceros
/// hasta 16384. Con 6912 muestras el tono cae entre casillas si se usa una transformada de
/// 8192; con 16384 la peor desviacion es de 0,37 Hz y se pierde menos de un decibelio en la
/// busqueda.
/// </remarks>
public sealed class EspectrogramaJt9
{
    /// <summary>Muestras entre bloques: medio simbolo.</summary>
    public const int Salto = ParametrosJt9.MuestrasPorSimbolo / 2;

    /// <summary>Longitud de la transformada.</summary>
    public const int LongitudFft = 16384;

    /// <summary>Anchura de una casilla, en hercios.</summary>
    public const double CasillaHz = (double)ParametrosJt9.FrecuenciaDeAnalisis / LongitudFft;

    private readonly float[] _potencias;

    private EspectrogramaJt9(float[] potencias, int bloques, int casillas)
    {
        _potencias = potencias;
        Bloques = bloques;
        Casillas = casillas;
    }

    /// <summary>Bloques (columnas) del espectrograma.</summary>
    public int Bloques { get; }

    /// <summary>Casillas (filas) guardadas por bloque.</summary>
    public int Casillas { get; }

    /// <summary>Potencia en un bloque y una casilla; cero fuera de rango.</summary>
    public float Potencia(int bloque, int casilla) =>
        bloque < 0 || bloque >= Bloques || casilla < 0 || casilla >= Casillas ? 0f : _potencias[(bloque * Casillas) + casilla];

    /// <summary>Casilla que corresponde a una frecuencia.</summary>
    public static int CasillaDe(double hz) => (int)Math.Round(hz / CasillaHz);

    /// <summary>Calcula el espectrograma de una ventana a 12000 muestras por segundo.</summary>
    public static EspectrogramaJt9 Calcular(ReadOnlySpan<float> audio, double frecuenciaMaximaHz = 3200)
    {
        var bloques = audio.Length < ParametrosJt9.MuestrasPorSimbolo ? 0 : ((audio.Length - ParametrosJt9.MuestrasPorSimbolo) / Salto) + 1;
        var casillas = Math.Min((LongitudFft / 2) + 1, CasillaDe(frecuenciaMaximaHz) + 1);
        var potencias = new float[bloques * casillas];
        var real = new float[LongitudFft];
        var imaginaria = new float[LongitudFft];

        for (var b = 0; b < bloques; b++)
        {
            Array.Clear(real);
            Array.Clear(imaginaria);
            audio.Slice(b * Salto, ParametrosJt9.MuestrasPorSimbolo).CopyTo(real);
            Fft.Transformar(real, imaginaria);
            var fila = potencias.AsSpan(b * casillas, casillas);
            for (var c = 0; c < casillas; c++)
                fila[c] = (real[c] * real[c]) + (imaginaria[c] * imaginaria[c]);
        }
        return new EspectrogramaJt9(potencias, bloques, casillas);
    }
}

/// <summary>
/// Busca en la ventana los sitios donde suena el sincronismo de JT9.
/// </summary>
/// <remarks>
/// <para>
/// Es la misma idea que en JT65: el tono 0 suena en 16 intervalos conocidos y esta vacio en
/// los otros 69, asi que se compara la potencia media en unos y otros para cada instante y
/// cada frecuencia. Con solo 16 intervalos de sincronismo la medida es mas ruidosa que en
/// JT65, y por eso se completa con un segundo termino: en los intervalos de datos tiene que
/// haber energia en <i>alguno</i> de los ocho tonos de datos. Se suma la potencia del tono mas
/// fuerte de cada intervalo de datos, se le resta lo que da el ruido solo (el maximo de ocho
/// casillas de ruido, que es 2,7 veces la media) y se normaliza por la media de las nueve
/// casillas. Con ruido las dos partes dan cerca de cero; con senal, ambas suben.
/// </para>
/// </remarks>
public static class SincronizadorJt9
{
    /// <summary>Frecuencia mas baja del tono de sincronismo que se busca.</summary>
    public const double FrecuenciaMinimaPorOmision = 200;

    /// <summary>Frecuencia mas alta del tono de sincronismo que se busca.</summary>
    public const double FrecuenciaMaximaPorOmision = 3000;

    /// <summary>Adelanto maximo respecto al segundo 1 que se busca, en segundos.</summary>
    public const double DesfaseMinimoPorOmision = -1.5;

    /// <summary>Retraso maximo respecto al segundo 1 que se busca, en segundos.</summary>
    public const double DesfaseMaximoPorOmision = 5.0;

    /// <summary>Puntuacion minima para considerar un sitio: por debajo es ruido.</summary>
    public const double PuntuacionMinima = 1.0;

    /// <summary>Casillas de la transformada por tono, aproximadas (1,736 / 0,732).</summary>
    private const double CasillasPorTono = ParametrosJt9.EspaciadoDeTonosHz / EspectrogramaJt9.CasillaHz;

    /// <summary>Recorre la ventana y devuelve las candidatas ordenadas de mejor a peor.</summary>
    public static List<CandidataJt9> Buscar(
        EspectrogramaJt9 espectrograma,
        int maximo = 30,
        double frecuenciaMinima = FrecuenciaMinimaPorOmision,
        double frecuenciaMaxima = FrecuenciaMaximaPorOmision,
        double desfaseMinimo = DesfaseMinimoPorOmision,
        double desfaseMaximo = DesfaseMaximoPorOmision)
    {
        ArgumentNullException.ThrowIfNull(espectrograma);

        var primerBloque = (int)Math.Floor((ParametrosJt9.ComienzoNominalSegundos + desfaseMinimo) * ParametrosJt9.FrecuenciaDeAnalisis / EspectrogramaJt9.Salto);
        var ultimoBloque = (int)Math.Ceiling((ParametrosJt9.ComienzoNominalSegundos + desfaseMaximo) * ParametrosJt9.FrecuenciaDeAnalisis / EspectrogramaJt9.Salto);
        var primeraCasilla = Math.Max(1, EspectrogramaJt9.CasillaDe(frecuenciaMinima));
        var anchoEnCasillas = (int)Math.Ceiling(CasillasPorTono * TablasJt9.Tonos) + 1;
        var ultimaCasilla = Math.Min(espectrograma.Casillas - anchoEnCasillas - 1, EspectrogramaJt9.CasillaDe(frecuenciaMaxima));
        if (ultimaCasilla < primeraCasilla || espectrograma.Bloques == 0) return [];

        var alto = ultimoBloque - primerBloque + 1;
        var ancho = ultimaCasilla - primeraCasilla + 1;
        var puntuaciones = new double[alto * ancho];
        var casillasDeTono = new int[TablasJt9.Tonos];
        for (var t = 0; t < TablasJt9.Tonos; t++) casillasDeTono[t] = (int)Math.Round(t * CasillasPorTono);

        for (var b = primerBloque; b <= ultimoBloque; b++)
            for (var c = primeraCasilla; c <= ultimaCasilla; c++)
                puntuaciones[((b - primerBloque) * ancho) + c - primeraCasilla] = Puntuar(espectrograma, b, c, casillasDeTono);

        var candidatas = new List<CandidataJt9>();
        for (var b = 0; b < alto; b++)
            for (var c = 0; c < ancho; c++)
            {
                var valor = puntuaciones[(b * ancho) + c];
                if (valor < PuntuacionMinima) continue;
                if (!EsMaximoLocal(puntuaciones, alto, ancho, b, c)) continue;
                candidatas.Add(new CandidataJt9(b + primerBloque, c + primeraCasilla, valor));
            }

        candidatas.Sort(static (x, y) => y.Puntuacion.CompareTo(x.Puntuacion));
        if (candidatas.Count > maximo) candidatas.RemoveRange(maximo, candidatas.Count - maximo);
        return candidatas;
    }

    /// <summary>
    /// Puntuacion de un sitio: exceso de potencia del tono 0 en los intervalos de sincronismo
    /// mas exceso del tono mas fuerte en los de datos, en unidades de la potencia media de las
    /// nueve casillas. Cero con ruido.
    /// </summary>
    public static double Puntuar(EspectrogramaJt9 espectrograma, int bloque, int casilla, int[] casillasDeTono)
    {
        ArgumentNullException.ThrowIfNull(espectrograma);
        ArgumentNullException.ThrowIfNull(casillasDeTono);
        // Valor esperado del maximo de ocho casillas de ruido, en unidades de la media: H(8).
        const double MaximoDeRuido = 2.7178571428571425;

        double enSincronismo = 0, maximosDeDatos = 0, total = 0;
        int cuantosSincronismo = 0, cuantosDatos = 0;
        for (var k = 0; k < TablasJt9.Simbolos; k++)
        {
            var b = bloque + (2 * k);
            if (b < 0 || b >= espectrograma.Bloques) continue;
            var p0 = espectrograma.Potencia(b, casilla + casillasDeTono[0]);
            total += p0;
            if (TablasJt9.Sincronismo[k] == 1)
            {
                enSincronismo += p0;
                cuantosSincronismo++;
                for (var t = 1; t < TablasJt9.Tonos; t++) total += espectrograma.Potencia(b, casilla + casillasDeTono[t]);
            }
            else
            {
                var maximo = 0f;
                for (var t = 1; t < TablasJt9.Tonos; t++)
                {
                    var p = espectrograma.Potencia(b, casilla + casillasDeTono[t]);
                    total += p;
                    if (p > maximo) maximo = p;
                }
                maximosDeDatos += maximo;
                cuantosDatos++;
            }
        }
        if (cuantosSincronismo < 13 || cuantosDatos < 55 || total <= 0) return 0;
        var media = total / ((cuantosSincronismo + cuantosDatos) * TablasJt9.Tonos);
        var sincronismo = (enSincronismo / cuantosSincronismo / media) - 1;
        var datos = (maximosDeDatos / cuantosDatos / media) - MaximoDeRuido;
        return sincronismo + datos;
    }

    private static bool EsMaximoLocal(double[] puntuaciones, int alto, int ancho, int b, int c)
    {
        var valor = puntuaciones[(b * ancho) + c];
        for (var db = -1; db <= 1; db++)
            for (var dc = -3; dc <= 3; dc++)
            {
                if (db == 0 && dc == 0) continue;
                var bb = b + db;
                var cc = c + dc;
                if (bb < 0 || bb >= alto || cc < 0 || cc >= ancho) continue;
                if (puntuaciones[(bb * ancho) + cc] > valor) return false;
            }
        return true;
    }
}
