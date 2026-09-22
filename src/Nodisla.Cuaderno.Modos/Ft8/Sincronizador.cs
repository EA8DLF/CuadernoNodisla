using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>Un sitio de la ventana donde parece haber una senal.</summary>
/// <param name="Bloque">Medio simbolo en que empezaria la senal.</param>
/// <param name="Casilla">Casilla del espectrograma donde caeria su tono cero.</param>
/// <param name="Puntuacion">Cuanto destaca el sincronismo sobre lo que hay alrededor.</param>
public readonly record struct Candidata(int Bloque, int Casilla, double Puntuacion);

/// <summary>
/// Busca en la ventana los sitios donde suena el sincronismo.
/// </summary>
/// <remarks>
/// <para>
/// <b>El problema.</b> En una ventana de FT8 caben unas cuarenta senales a la vez, cada una en
/// una frecuencia distinta dentro de los tres kilohercios del audio, y ninguna avisa de donde
/// esta ni de cuando empieza. Hay que encontrarlas antes de poder demodular nada.
/// </para>
/// <para>
/// <b>La solucion.</b> Todas llevan el mismo faro: los grupos de Costas, veintiun simbolos de
/// tonos conocidos repartidos por el mensaje. Se recorre el espectrograma probando cada
/// combinacion de instante y frecuencia, y en cada una se mira si en las casillas donde
/// deberian sonar esos veintiun tonos hay mas energia que en las de al lado. Donde la hay, hay
/// una senal.
/// </para>
/// <para>
/// La puntuacion se divide por la energia media de todos los tonos de esos mismos instantes.
/// Asi una senal debil en una zona tranquila puntua igual que una fuerte en una zona con
/// ruido, que es lo que interesa: lo que se busca no es la senal mas potente sino la que
/// <i>mas se parece</i> al patron.
/// </para>
/// <para>
/// El barrido devuelve solo los maximos locales, no todo lo que pase de un umbral: una senal
/// fuerte puntua bien tambien en las casillas de alrededor, y sin esa criba las primeras
/// posiciones de la lista serian cuarenta variantes de la misma estacion.
/// </para>
/// </remarks>
public static class Sincronizador
{
    /// <summary>Frecuencia mas baja en la que se busca, en hercios de audio.</summary>
    public const double FrecuenciaMinimaPorOmision = 200;

    /// <summary>Frecuencia mas alta en la que se busca, en hercios de audio.</summary>
    public const double FrecuenciaMaximaPorOmision = 3000;

    /// <summary>
    /// Recorre la ventana y devuelve las candidatas ordenadas de mejor a peor.
    /// </summary>
    /// <param name="analisis">Ventana ya analizada.</param>
    /// <param name="maximo">Cuantas candidatas devolver como mucho.</param>
    /// <param name="frecuenciaMinima">Limite bajo de la busqueda, en hercios.</param>
    /// <param name="frecuenciaMaxima">Limite alto de la busqueda, en hercios.</param>
    public static List<Candidata> Buscar(
        AnalisisDeVentana analisis,
        int maximo = 200,
        double frecuenciaMinima = FrecuenciaMinimaPorOmision,
        double frecuenciaMaxima = FrecuenciaMaximaPorOmision)
    {
        ArgumentNullException.ThrowIfNull(analisis);
        var p = analisis.Parametros;

        var primeraCasilla = Math.Max(0, analisis.CasillaDe(frecuenciaMinima));
        var ultimaCasilla = Math.Min(
            analisis.CasillasPorBloque - (AnalisisDeVentana.CasillasPorTono * (p.Tonos - 1)) - 1,
            analisis.CasillaDe(frecuenciaMaxima));
        var ultimoBloque = analisis.Bloques - (2 * p.SimbolosTotales);
        if (ultimoBloque < 0 || ultimaCasilla < primeraCasilla) return [];

        var anchoDeCasillas = ultimaCasilla - primeraCasilla + 1;
        var altoDeBloques = ultimoBloque + 1;
        var puntuaciones = new double[altoDeBloques * anchoDeCasillas];

        for (var b = 0; b <= ultimoBloque; b++)
            for (var c = primeraCasilla; c <= ultimaCasilla; c++)
                puntuaciones[((b * anchoDeCasillas) + c - primeraCasilla)] = Puntuar(analisis, b, c);

        var candidatas = new List<Candidata>();
        for (var b = 0; b <= ultimoBloque; b++)
            for (var c = primeraCasilla; c <= ultimaCasilla; c++)
            {
                var valor = puntuaciones[(b * anchoDeCasillas) + c - primeraCasilla];
                // Una senal de verdad reparte su energia en el patron; por debajo de uno no hay
                // ni eso, es ruido que da la casualidad de parecerse un poco.
                if (valor <= 1.0) continue;
                if (!EsMaximoLocal(puntuaciones, altoDeBloques, anchoDeCasillas, b, c - primeraCasilla)) continue;
                candidatas.Add(new Candidata(b, c, valor));
            }

        candidatas.Sort(static (x, y) => y.Puntuacion.CompareTo(x.Puntuacion));
        if (candidatas.Count > maximo) candidatas.RemoveRange(maximo, candidatas.Count - maximo);
        return candidatas;
    }

    /// <summary>Cuanto destaca el sincronismo en ese instante y esa frecuencia.</summary>
    private static double Puntuar(AnalisisDeVentana analisis, int bloque, int casilla)
    {
        var p = analisis.Parametros;
        double enElPatron = 0, enTodos = 0;
        var cuantos = 0;

        for (var g = 0; g < p.PosicionesDeCostas.Length; g++)
        {
            var inicio = p.PosicionesDeCostas[g];
            var grupo = p.GruposDeCostas[g];
            for (var k = 0; k < grupo.Length; k++)
            {
                // Cada simbolo ocupa dos bloques del espectrograma; se mira el primero.
                var b = bloque + (2 * (inicio + k));
                enElPatron += analisis.Potencia(b, casilla + (AnalisisDeVentana.CasillasPorTono * grupo[k]));
                for (var t = 0; t < p.Tonos; t++)
                    enTodos += analisis.Potencia(b, casilla + (AnalisisDeVentana.CasillasPorTono * t));
                cuantos++;
            }
        }

        if (cuantos == 0 || enTodos <= 0) return 0;
        // Si solo hubiera ruido, la energia se repartiria por igual entre los tonos y esto
        // daria uno. Cuanto mas suba de uno, mas se parece al patron.
        return enElPatron * p.Tonos / enTodos;
    }

    private static bool EsMaximoLocal(double[] puntuaciones, int alto, int ancho, int b, int c)
    {
        var valor = puntuaciones[(b * ancho) + c];
        for (var db = -1; db <= 1; db++)
            for (var dc = -1; dc <= 1; dc++)
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
