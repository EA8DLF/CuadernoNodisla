namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>Un sitio del espectrograma donde parece haber una senal de Q65.</summary>
/// <param name="Columna">Columna en la que empieza el primer simbolo.</param>
/// <param name="Casilla">Casilla de frecuencia del tono base (el de sincronismo).</param>
/// <param name="Puntuacion">Cuantas desviaciones tipicas del ruido se sale la correlacion.</param>
public readonly record struct CandidataDeQ65(int Columna, int Casilla, double Puntuacion);

/// <summary>
/// La correlacion con el patron de sincronismo en todos los comienzos y frecuencias posibles.
/// </summary>
/// <remarks>
/// Se guarda entero, y no solo las candidatas, porque para promediar periodos hay que poder
/// sumar el mapa de varios periodos: una senal que en un periodo no destaca del ruido destaca
/// en la suma de tres, y solo entonces se sabe donde medirla.
/// </remarks>
public sealed class MapaDeSincronismo
{
    internal MapaDeSincronismo(int primeraColumna, int columnas, int casillaMinima, int casillas, TablasDeQ65 tablas)
    {
        PrimeraColumna = primeraColumna;
        Columnas = Math.Max(0, columnas);
        CasillaMinima = casillaMinima;
        Casillas = Math.Max(0, casillas);
        Tablas = tablas;
        Puntuaciones = new float[Columnas * Casillas];
    }

    /// <summary>Primera columna de comienzo que se probo.</summary>
    public int PrimeraColumna { get; }

    /// <summary>Columnas de comienzo probadas.</summary>
    public int Columnas { get; }

    /// <summary>Primera casilla de tono base probada.</summary>
    public int CasillaMinima { get; }

    /// <summary>Casillas de tono base probadas.</summary>
    public int Casillas { get; }

    /// <summary>Tablas con las que se calculo.</summary>
    public TablasDeQ65 Tablas { get; }

    /// <summary>Puntuacion de cada sitio, en desviaciones tipicas del ruido de un periodo, sumada si hay varios.</summary>
    public float[] Puntuaciones { get; }

    /// <summary>Periodos sumados.</summary>
    public int Periodos { get; private set; } = 1;

    /// <summary>Suma otro mapa de la misma forma.</summary>
    public void Sumar(MapaDeSincronismo otro)
    {
        ArgumentNullException.ThrowIfNull(otro);
        if (otro.PrimeraColumna != PrimeraColumna || otro.Columnas != Columnas || otro.CasillaMinima != CasillaMinima || otro.Casillas != Casillas)
            throw new ArgumentException("Los mapas no tienen la misma forma.", nameof(otro));
        for (var i = 0; i < Puntuaciones.Length; i++) Puntuaciones[i] += otro.Puntuaciones[i];
        Periodos += otro.Periodos;
    }

    /// <summary>Las candidatas de mejor a peor, sin repetir sitios vecinos.</summary>
    /// <param name="maximo">Cuantas devolver como mucho.</param>
    /// <param name="umbral">Puntuacion minima, en desviaciones tipicas de la suma.</param>
    public List<CandidataDeQ65> Buscar(int maximo, double umbral)
    {
        var escala = 1.0 / Math.Sqrt(Periodos);
        var puntuaciones = new List<CandidataDeQ65>();
        for (var c = 0; c < Columnas; c++)
        {
            for (var b = 0; b < Casillas; b++)
            {
                var puntuacion = Puntuaciones[(c * Casillas) + b] * escala;
                if (puntuacion >= umbral) puntuaciones.Add(new CandidataDeQ65(PrimeraColumna + c, CasillaMinima + b, puntuacion));
            }
        }

        puntuaciones.Sort((a, b) => b.Puntuacion.CompareTo(a.Puntuacion));
        var elegidas = new List<CandidataDeQ65>();
        foreach (var candidata in puntuaciones)
        {
            if (elegidas.Count >= maximo) break;
            var repetida = false;
            foreach (var e in elegidas)
            {
                if (Math.Abs(e.Casilla - candidata.Casilla) <= SincronizadorDeQ65.RadioDeSupresion
                    && Math.Abs(e.Columna - candidata.Columna) <= SincronizadorDeQ65.RadioDeSupresion)
                {
                    repetida = true;
                    break;
                }
            }
            if (!repetida) elegidas.Add(candidata);
        }
        return elegidas;
    }
}

/// <summary>
/// Busca senales de Q65 por la correlacion con el patron de sincronismo.
/// </summary>
/// <remarks>
/// <para>
/// El tono de sincronismo aparece en 22 de las 85 posiciones de la trama, siempre las mismas,
/// y en las otras 63 nunca, porque los datos van en los tonos de arriba. Asi que para cada
/// comienzo posible y cada frecuencia posible se suma la energia del tono base en las 22
/// posiciones de sincronismo y se le resta la parte proporcional de la energia de ese mismo
/// tono en las 63 de datos. Con ruido la diferencia es cero de media; con una senal, es la
/// energia de 22 simbolos; y con una portadora fija, que enciende las 85 posiciones por igual,
/// la resta la anula. Eso ultimo es lo que hace que un silbido no se cuele como candidata.
/// </para>
/// <para>
/// La puntuacion se da en desviaciones tipicas del ruido para que el umbral signifique lo
/// mismo en todos los submodos y periodos.
/// </para>
/// </remarks>
public static class SincronizadorDeQ65
{
    /// <summary>Casillas y columnas alrededor de una candidata que se consideran la misma senal.</summary>
    public const int RadioDeSupresion = 3;

    /// <summary>Calcula el mapa de sincronismo de una ventana.</summary>
    public static MapaDeSincronismo Mapa(EspectrogramaDeQ65 espectrograma, TablasDeQ65 tablas)
    {
        ArgumentNullException.ThrowIfNull(espectrograma);
        ArgumentNullException.ThrowIfNull(tablas);
        var p = espectrograma.Parametros;
        const int Trama = TablasDeQ65.SimbolosDeLaTrama;
        const int Sinc = TablasDeQ65.SimbolosDeSincronismo;
        const int Datos = Trama - Sinc;

        var segundosPorColumna = (double)p.Salto / ParametrosDeQ65.FrecuenciaDeAnalisis;
        var primera = (int)Math.Ceiling((p.ComienzoNominalSegundos + p.DesfaseMinimoSegundos) / segundosPorColumna);
        var ultima = (int)Math.Floor((p.ComienzoNominalSegundos + p.DesfaseMaximoSegundos) / segundosPorColumna);
        primera = Math.Max(primera, 0);
        ultima = Math.Min(ultima, espectrograma.Columnas - 1 - ((Trama - 1) * ParametrosDeQ65.ColumnasPorSimbolo));

        var casillaMinima = espectrograma.CasillaMinima;
        // El tono base no puede subir mas alla de donde el tono 64 siga dentro del espectrograma.
        var casillaMaxima = Math.Min(
            espectrograma.CasillaMaxima - (MensajeDeQ65.Tonos - 1) * p.CasillasPorTono,
            espectrograma.CasillaDe(p.FrecuenciaMaximaDelTonoBaseHz));
        var mapa = new MapaDeSincronismo(primera, ultima - primera + 1, casillaMinima, casillaMaxima - casillaMinima + 1, tablas);
        if (ultima < primera || casillaMaxima < casillaMinima) return mapa;

        var esSincronismo = new bool[Trama];
        foreach (var s in tablas.PosicionesDeSincronismo) esSincronismo[s] = true;

        // Varianza de la resta con ruido puro: 22 sumandos mas 63 escalados por (22/63)^2.
        var proporcion = (double)Sinc / Datos;
        var escala = 1.0 / (espectrograma.RuidoDeFondo * Math.Sqrt(Sinc + (Sinc * proporcion)));

        var anchura = mapa.Casillas;
        var sumaSinc = new double[anchura];
        var sumaDatos = new double[anchura];
        for (var c0 = primera; c0 <= ultima; c0++)
        {
            Array.Clear(sumaSinc);
            Array.Clear(sumaDatos);
            for (var i = 0; i < Trama; i++)
            {
                var columna = c0 + (i * ParametrosDeQ65.ColumnasPorSimbolo);
                var destino = esSincronismo[i] ? sumaSinc : sumaDatos;
                for (var b = 0; b < anchura; b++)
                    destino[b] += espectrograma.Energia(columna, casillaMinima + b);
            }
            var fila = (c0 - primera) * anchura;
            for (var b = 0; b < anchura; b++)
                mapa.Puntuaciones[fila + b] = (float)((sumaSinc[b] - (proporcion * sumaDatos[b])) * escala);
        }
        return mapa;
    }

    /// <summary>Devuelve las candidatas de una ventana, de mejor a peor.</summary>
    public static List<CandidataDeQ65> Buscar(EspectrogramaDeQ65 espectrograma, TablasDeQ65 tablas, int maximo, double umbral) =>
        Mapa(espectrograma, tablas).Buscar(maximo, umbral);
}
