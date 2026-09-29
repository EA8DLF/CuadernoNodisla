using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Jt65;

/// <summary>Un sitio de la ventana donde parece sonar el sincronismo de JT65.</summary>
/// <param name="MedioSimbolo">Medio simbolo (2048 muestras) en que empezaria la senal, contado desde el principio de la ventana; puede ser negativo.</param>
/// <param name="Casilla">Casilla del espectrograma (de 1,35 Hz) donde caeria el tono de sincronismo.</param>
/// <param name="Puntuacion">Potencia media en los intervalos de sincronismo partida por la de los intervalos de datos, en la misma casilla. Uno es ruido.</param>
public readonly record struct CandidataJt65(int MedioSimbolo, int Casilla, double Puntuacion);

/// <summary>
/// Espectrograma de la ventana con la resolucion que necesita el sincronismo de JT65: un
/// bloque cada medio simbolo, casillas de media separacion de tono.
/// </summary>
/// <remarks>
/// Cada bloque es la transformada de un simbolo entero (4096 muestras) rellenado con ceros
/// hasta 8192, de modo que las casillas miden 1,35 Hz: la mitad del espaciado de JT65A. Con
/// casillas de un espaciado entero, una senal que cayera justo entre dos perderia casi cuatro
/// decibelios en la busqueda; con media, menos de uno. La ventana es rectangular a proposito:
/// para un tono puro que dura justo el simbolo es el filtro adaptado.
/// </remarks>
public sealed class EspectrogramaJt65
{
    /// <summary>Muestras entre bloques: medio simbolo.</summary>
    public const int Salto = ParametrosJt65.MuestrasPorSimbolo / 2;

    /// <summary>Longitud de la transformada.</summary>
    public const int LongitudFft = 2 * ParametrosJt65.MuestrasPorSimbolo;

    /// <summary>Anchura de una casilla, en hercios.</summary>
    public const double CasillaHz = (double)ParametrosJt65.FrecuenciaDeAnalisis / LongitudFft;

    private readonly float[] _potencias;

    private EspectrogramaJt65(float[] potencias, int bloques, int casillas)
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

    /// <summary>Calcula el espectrograma de una ventana a la frecuencia de analisis.</summary>
    /// <param name="audio">Ventana a 11025 muestras por segundo.</param>
    /// <param name="frecuenciaMaximaHz">Hasta que frecuencia se guardan casillas.</param>
    public static EspectrogramaJt65 Calcular(ReadOnlySpan<float> audio, double frecuenciaMaximaHz = 3600)
    {
        var bloques = audio.Length < ParametrosJt65.MuestrasPorSimbolo ? 0 : ((audio.Length - ParametrosJt65.MuestrasPorSimbolo) / Salto) + 1;
        var casillas = Math.Min((LongitudFft / 2) + 1, CasillaDe(frecuenciaMaximaHz) + 1);
        var potencias = new float[bloques * casillas];
        var real = new float[LongitudFft];
        var imaginaria = new float[LongitudFft];

        for (var b = 0; b < bloques; b++)
        {
            Array.Clear(real);
            Array.Clear(imaginaria);
            audio.Slice(b * Salto, ParametrosJt65.MuestrasPorSimbolo).CopyTo(real);
            Fft.Transformar(real, imaginaria);
            var fila = potencias.AsSpan(b * casillas, casillas);
            for (var c = 0; c < casillas; c++)
                fila[c] = (real[c] * real[c]) + (imaginaria[c] * imaginaria[c]);
        }
        return new EspectrogramaJt65(potencias, bloques, casillas);
    }
}

/// <summary>
/// Busca en la ventana los sitios donde suena el sincronismo de JT65.
/// </summary>
/// <remarks>
/// <para>
/// El tono de sincronismo suena en 63 de los 126 intervalos, segun un patron pseudoaleatorio
/// conocido, y en los otros 63 esa frecuencia esta vacia (los tonos de datos empiezan dos
/// espaciados mas arriba). Asi que para cada instante de arranque y cada frecuencia se compara
/// la potencia media en los intervalos donde deberia sonar con la de los intervalos donde no:
/// con ruido solo, el cociente es uno; con senal, sube. Es una medida que no depende del nivel
/// ni del ruido de alrededor, y con 63 intervalos por lado es muy estable: en ruido puro sus
/// maximos no pasan de dos, y una senal en el filo de la decodificacion (−25 dB) da mas de tres.
/// </para>
/// <para>
/// Se devuelven solo los maximos locales: una senal fuerte puntua tambien en los bloques y
/// casillas vecinos, y sin esa criba la lista serian veinte versiones de la misma estacion.
/// </para>
/// </remarks>
public static class SincronizadorJt65
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
    public const double PuntuacionMinima = 1.7;

    /// <summary>
    /// Recorre la ventana y devuelve las candidatas ordenadas de mejor a peor.
    /// </summary>
    public static List<CandidataJt65> Buscar(
        EspectrogramaJt65 espectrograma,
        ParametrosJt65 parametros,
        int maximo = 20,
        double frecuenciaMinima = FrecuenciaMinimaPorOmision,
        double frecuenciaMaxima = FrecuenciaMaximaPorOmision,
        double desfaseMinimo = DesfaseMinimoPorOmision,
        double desfaseMaximo = DesfaseMaximoPorOmision)
    {
        ArgumentNullException.ThrowIfNull(espectrograma);
        ArgumentNullException.ThrowIfNull(parametros);

        var sincronismo = TablasJt65.PosicionesDeSincronismo;
        var datos = TablasJt65.PosicionesDeDatos;

        var primerBloque = (int)Math.Floor((ParametrosJt65.ComienzoNominalSegundos + desfaseMinimo) * ParametrosJt65.FrecuenciaDeAnalisis / EspectrogramaJt65.Salto);
        var ultimoBloque = (int)Math.Ceiling((ParametrosJt65.ComienzoNominalSegundos + desfaseMaximo) * ParametrosJt65.FrecuenciaDeAnalisis / EspectrogramaJt65.Salto);
        var primeraCasilla = Math.Max(1, EspectrogramaJt65.CasillaDe(frecuenciaMinima));
        var anchoEnCasillas = 2 * parametros.EspaciadoEnCasillas * (TablasJt65.TonosTotales - 1);
        var ultimaCasilla = Math.Min(espectrograma.Casillas - anchoEnCasillas - 1, EspectrogramaJt65.CasillaDe(frecuenciaMaxima));
        if (ultimaCasilla < primeraCasilla || espectrograma.Bloques == 0) return [];

        var alto = ultimoBloque - primerBloque + 1;
        var ancho = ultimaCasilla - primeraCasilla + 1;
        var puntuaciones = new double[alto * ancho];

        for (var b = primerBloque; b <= ultimoBloque; b++)
            for (var c = primeraCasilla; c <= ultimaCasilla; c++)
                puntuaciones[((b - primerBloque) * ancho) + c - primeraCasilla] = Puntuar(espectrograma, b, c, sincronismo, datos);

        var candidatas = new List<CandidataJt65>();
        for (var b = 0; b < alto; b++)
            for (var c = 0; c < ancho; c++)
            {
                var valor = puntuaciones[(b * ancho) + c];
                if (valor < PuntuacionMinima) continue;
                if (!EsMaximoLocal(puntuaciones, alto, ancho, b, c)) continue;
                candidatas.Add(new CandidataJt65(b + primerBloque, c + primeraCasilla, valor));
            }

        candidatas.Sort(static (x, y) => y.Puntuacion.CompareTo(x.Puntuacion));
        if (candidatas.Count > maximo) candidatas.RemoveRange(maximo, candidatas.Count - maximo);
        return candidatas;
    }

    /// <summary>Potencia media en los intervalos de sincronismo partida por la de los de datos.</summary>
    public static double Puntuar(EspectrogramaJt65 espectrograma, int bloque, int casilla, int[] sincronismo, int[] datos)
    {
        double enSincronismo = 0, enDatos = 0;
        int cuantosSincronismo = 0, cuantosDatos = 0;
        foreach (var j in sincronismo)
        {
            var b = bloque + (2 * j);
            if (b < 0 || b >= espectrograma.Bloques) continue;
            enSincronismo += espectrograma.Potencia(b, casilla);
            cuantosSincronismo++;
        }
        foreach (var j in datos)
        {
            var b = bloque + (2 * j);
            if (b < 0 || b >= espectrograma.Bloques) continue;
            enDatos += espectrograma.Potencia(b, casilla);
            cuantosDatos++;
        }
        // Con menos de 50 intervalos por lado la senal esta demasiado cortada para fiarse.
        if (cuantosSincronismo < 50 || cuantosDatos < 50 || enDatos <= 0) return 0;
        return (enSincronismo / cuantosSincronismo) / (enDatos / cuantosDatos);
    }

    private static bool EsMaximoLocal(double[] puntuaciones, int alto, int ancho, int b, int c)
    {
        var valor = puntuaciones[(b * ancho) + c];
        for (var db = -1; db <= 1; db++)
            for (var dc = -2; dc <= 2; dc++)
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
