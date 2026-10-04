namespace Nodisla.Cuaderno.Modos.Rtty;

/// <summary>Un tramo de línea RTTY: marca o espacio, y cuánto dura.</summary>
public readonly record struct TramoRtty(bool Marca, double Segundos);

/// <summary>
/// Genera RTTY: el audio FSK de un texto, con los tiempos y el desplazamiento que se le pida.
/// </summary>
/// <remarks>
/// Función pura, igual de espíritu que <see cref="Cw.SintetizadorCw"/>: convierte texto en
/// muestras. <b>No transmite</b>: no toca hardware ni PTT. Sacar este audio por la antena de
/// verdad es cosa de quien llame a esto (ver <c>Ui.Telegrafia.Rtty</c>), que lo hace por el mismo
/// camino que FT8/FT4 — <c>IVigilantePtt</c> y la salida de audio real.
/// </remarks>
public static class GeneradorRtty
{
    /// <summary>
    /// Los códigos Baudot de un texto, con los cambios de juego (LETRAS/CIFRAS) que haga falta
    /// intercalar. Pasa el texto a mayúsculas.
    /// </summary>
    /// <param name="texto">El texto a mandar.</param>
    /// <param name="juegoInicial">En qué juego se supone que está el receptor al empezar.</param>
    /// <exception cref="FormatException">Si el texto trae un carácter que Baudot no tiene.</exception>
    public static IReadOnlyList<byte> Codigos(string texto, JuegoBaudot juegoInicial = JuegoBaudot.Letras)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var juego = juegoInicial;
        var codigos = new List<byte>();
        foreach (var c in texto.ToUpperInvariant())
        {
            var requerido = TablaBaudot.JuegoRequerido(c, juego)
                ?? throw new FormatException($"El carácter '{c}' no se puede mandar en RTTY: no está en la tabla Baudot.");
            if (requerido != juego)
            {
                codigos.Add(requerido == JuegoBaudot.Cifras ? TablaBaudot.CambioACifras : TablaBaudot.CambioALetras);
                juego = requerido;
            }

            codigos.Add(TablaBaudot.Codigo(c, juego)!.Value);

            // USOS: un espacio en CIFRAS deja al receptor en LETRAS (ver TablaBaudot.Decodificar).
            // Si lo que sigue vuelve a ser CIFRAS hace falta un cambio nuevo, o el receptor leería
            // el código siguiente como si fuera LETRAS.
            if (c == ' ' && juego == JuegoBaudot.Cifras) juego = JuegoBaudot.Letras;
        }

        return codigos;
    }

    /// <summary>
    /// Los tramos de línea (marca/espacio) de un texto: por cada código, arranque (espacio, 1
    /// bit), 5 bits de datos con el orden LSB primero y parada (marca, <see cref="ParametrosRtty.BitsDeParada"/> bits).
    /// </summary>
    public static IReadOnlyList<TramoRtty> Tramos(
        string texto, ParametrosRtty? parametros = null, JuegoBaudot juegoInicial = JuegoBaudot.Letras)
    {
        var p = (parametros ?? new ParametrosRtty()).Acotado();
        var codigos = Codigos(texto, juegoInicial);
        var bit = p.DuracionDelBit;
        var tramos = new List<TramoRtty>(codigos.Count * 7);
        foreach (var codigo in codigos)
        {
            tramos.Add(new TramoRtty(false, bit)); // arranque: siempre espacio
            for (var b = 0; b < 5; b++)
            {
                var marca = ((codigo >> b) & 1) == 1; // LSB primero: el estándar Baudot
                tramos.Add(new TramoRtty(marca, bit));
            }

            tramos.Add(new TramoRtty(true, bit * p.BitsDeParada)); // parada: siempre marca
        }

        return tramos;
    }

    /// <summary>
    /// El audio de un texto: FSK de fase continua entre el tono de marca y el de espacio, a la
    /// amplitud y velocidad pedidas.
    /// </summary>
    /// <param name="texto">Texto a mandar.</param>
    /// <param name="tonoDeMarcaHz">Tono de marca dentro del ancho de banda de audio.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    /// <param name="parametros">Baudios, desplazamiento, parada e inversión; nulo = los de serie.</param>
    /// <param name="amplitud">Amplitud de pico.</param>
    /// <param name="silencioDelante">Segundos de reposo (marca continua) antes del primer carácter.</param>
    /// <param name="silencioDetras">Segundos de reposo tras el último.</param>
    /// <param name="juegoInicial">En qué juego se supone que está el receptor al empezar.</param>
    /// <remarks>
    /// <para>
    /// <b>Fase continua.</b> La frecuencia instantánea se integra muestra a muestra en vez de
    /// generar cada tramo con su propio seno desde fase cero: así la amplitud nunca da un salto al
    /// cambiar de tono (eso es, de por sí, lo que evita el clic de una FSK mal hecha).
    /// </para>
    /// <para>
    /// <b>La rampa.</b> Además, el salto de frecuencia en cada borde de bit se suaviza con una
    /// media móvil de <see cref="RampaMs"/>: sin ella el espectro de la señal se ensancha de golpe
    /// en cada transición («key clicks», el mismo problema que las rayas de CW sin envolvente) y
    /// se mete en el canal del vecino. Dos milisegundos son muchos menos que el bit más corto (22
    /// ms a 45,45 baudios) así que no deforma la temporización.
    /// </para>
    /// </remarks>
    public static float[] Generar(
        string texto,
        double tonoDeMarcaHz,
        int frecuenciaDeMuestreo,
        ParametrosRtty? parametros = null,
        double amplitud = 0.5,
        double silencioDelante = 0.2,
        double silencioDetras = 0.2,
        JuegoBaudot juegoInicial = JuegoBaudot.Letras)
    {
        var p = (parametros ?? new ParametrosRtty()).Acotado();
        var tramos = Tramos(texto, p, juegoInicial);
        var tonoDeEspacioHz = p.Invertido ? tonoDeMarcaHz + p.DesplazamientoHz : tonoDeMarcaHz - p.DesplazamientoHz;

        var total = silencioDelante + tramos.Sum(t => t.Segundos) + silencioDetras;
        var n = (int)Math.Ceiling(total * frecuenciaDeMuestreo);
        var objetivo = new double[n];
        for (var i = 0; i < n; i++) objetivo[i] = tonoDeMarcaHz; // en reposo: marca continua

        var t0 = silencioDelante;
        foreach (var tramo in tramos)
        {
            var desde = (int)Math.Round(t0 * frecuenciaDeMuestreo);
            var hasta = Math.Min(n, (int)Math.Round((t0 + tramo.Segundos) * frecuenciaDeMuestreo));
            var f = tramo.Marca ? tonoDeMarcaHz : tonoDeEspacioHz;
            for (var i = desde; i < hasta; i++) objetivo[i] = f;
            t0 += tramo.Segundos;
        }

        var rampaMuestras = Math.Max(1, (int)Math.Round(RampaMs / 1000 * frecuenciaDeMuestreo));
        var suave = Suavizar(objetivo, rampaMuestras);

        var salida = new float[n];
        double fase = 0;
        var pasoPorHz = 2 * Math.PI / frecuenciaDeMuestreo;
        for (var i = 0; i < n; i++)
        {
            fase += suave[i] * pasoPorHz;
            salida[i] = (float)(amplitud * Math.Sin(fase));
        }

        return salida;
    }

    /// <summary>Suavizado de los bordes de bit, en milisegundos.</summary>
    public const double RampaMs = 2.0;

    /// <summary>Media móvil centrada, O(n) con suma corrida.</summary>
    private static double[] Suavizar(double[] x, int ventana)
    {
        if (ventana <= 1) return x;
        var mitad = ventana / 2;
        var salida = new double[x.Length];
        double suma = 0;
        var n = 0;
        for (var i = -mitad; i < x.Length; i++)
        {
            var entra = i + mitad;
            if (entra >= 0 && entra < x.Length) { suma += x[entra]; n++; }
            var sale = i - mitad - 1;
            if (sale >= 0 && sale < x.Length) { suma -= x[sale]; n--; }
            if (i >= 0) salida[i] = suma / n;
        }

        return salida;
    }
}
