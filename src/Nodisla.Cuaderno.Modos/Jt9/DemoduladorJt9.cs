using Nodisla.Cuaderno.Modos.ReedSolomon;

namespace Nodisla.Cuaderno.Modos.Jt9;

/// <summary>Lo que se midio de una candidata de JT9 al mirarla de cerca.</summary>
/// <param name="TonoBaseHz">Frecuencia afinada del tono de sincronismo.</param>
/// <param name="ComienzoSegundos">Instante afinado en que empieza la senal, desde el principio de la ventana.</param>
/// <param name="Sincronismo">Puntuacion del sincronismo tras afinar: potencia del tono 0 en los intervalos de sincronismo partida por la de los de datos. Uno es ruido.</param>
/// <param name="SenalSobreRuido">Potencia de la senal partida por la del ruido en una casilla de 1,74 Hz.</param>
/// <param name="Potencias">85 × 9 potencias, simbolo a simbolo y tono a tono, en unidades de ruido.</param>
public sealed record MedidaJt9(double TonoBaseHz, double ComienzoSegundos, double Sincronismo, double SenalSobreRuido, float[] Potencias)
{
    /// <summary>
    /// Valores blandos de los 207 bits (69 simbolos × 3, el ultimo de relleno), por posicion
    /// de la secuencia entrelazada: 0 es un cero seguro, 255 un uno seguro, 128 ni idea.
    /// </summary>
    /// <remarks>
    /// Para cada simbolo de datos se calcula la verosimilitud de cada uno de los ocho tonos
    /// (<c>ln I0(2·sqrt(s·z))</c>, la estadistica de la FSK no coherente) y de ahi, para cada
    /// bit, el logaritmo de la razon entre la suma de las verosimilitudes de los cuatro tonos
    /// que lo llevan a uno y los cuatro que lo llevan a cero. Es una razon de verosimilitudes de
    /// verdad, en nats, y se pasa a la escala de Fano multiplicandola por
    /// <paramref name="unidadesPorNat"/> y centrandola en 128: la tabla de metricas del
    /// decodificador de Fano, con su media y su desviacion por omision (30 y 30), interpreta
    /// justamente cada 15 unidades como un nat. No se normaliza por el valor eficaz a proposito:
    /// con senal debil las razones son pequenas y el decodificador tiene que saberlo, que si no
    /// se le hace creer que los bits dudosos son seguros y se pierde en el arbol.
    /// </remarks>
    /// <param name="unidadesPorNat">Unidades de la escala de 0 a 255 por cada nat de la razon de verosimilitudes.</param>
    public byte[] ValoresBlandos(double unidadesPorNat)
    {
        var s = Math.Max(0.3, SenalSobreRuido);
        var raizDeS = Math.Sqrt(s);
        var crudos = new double[(3 * TablasJt9.SimbolosDeDatos) + 1];
        Span<double> l = stackalloc double[8];

        for (var m = 0; m < TablasJt9.SimbolosDeDatos; m++)
        {
            var k = TablasJt9.PosicionesDeDatos[m];
            var fila = Potencias.AsSpan(k * TablasJt9.Tonos, TablasJt9.Tonos);
            // l[v]: verosimilitud del simbolo v, que suena en el tono 1 + Gray[v].
            for (var v = 0; v < 8; v++)
                l[v] = DecodificadorBlando.LogBesselI0(2 * raizDeS * Math.Sqrt(Math.Max(0, fila[1 + TablasJt9.Gray[v]])));
            for (var b = 0; b < 3; b++)
            {
                var mascara = 1 << (2 - b);
                double unos = double.NegativeInfinity, ceros = double.NegativeInfinity;
                for (var v = 0; v < 8; v++)
                {
                    if ((v & mascara) != 0) unos = SumaLog(unos, l[v]); else ceros = SumaLog(ceros, l[v]);
                }
                crudos[(3 * m) + b] = unos - ceros;
            }
        }

        var blandos = new byte[crudos.Length];
        for (var i = 0; i < crudos.Length; i++)
            blandos[i] = (byte)Math.Clamp(128 + (int)Math.Round(crudos[i] * unidadesPorNat), 0, 255);
        blandos[^1] = 128;
        return blandos;
    }

    private static double SumaLog(double a, double b)
    {
        if (double.IsNegativeInfinity(a)) return b;
        var mayor = Math.Max(a, b);
        return mayor + Math.Log(Math.Exp(a - mayor) + Math.Exp(b - mayor));
    }
}

/// <summary>
/// Mira de cerca una candidata de JT9: afina instante y frecuencia y mide los nueve tonos de
/// cada simbolo.
/// </summary>
/// <remarks>
/// Es el mismo procedimiento que en JT65: se baja la senal a banda base a la frecuencia del
/// tono de sincronismo, se resume en bloques de 64 muestras para afinar el instante y la
/// frecuencia con la puntuacion de sincronismo, y con eso afinado se mide cada simbolo. Como
/// aqui el simbolo mide 6912 muestras, que no es potencia de dos, los nueve tonos se miden por
/// correlacion directa con cada exponencial en vez de con una transformada; son nueve tonos y
/// sale mas barato que rellenar a 8192.
/// </remarks>
public sealed class DemoduladorJt9
{
    private const int Bloque = 64;
    private const int BloquesPorSimbolo = ParametrosJt9.MuestrasPorSimbolo / Bloque;
    private readonly float[] _audio;

    /// <summary>Crea el demodulador para una ventana a 12000 muestras por segundo.</summary>
    public DemoduladorJt9(float[] audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        _audio = audio;
    }

    /// <summary>Afina y mide una candidata.</summary>
    public MedidaJt9 Medir(CandidataJt9 candidata)
    {
        const int N = ParametrosJt9.MuestrasPorSimbolo;
        var comienzo = candidata.MedioSimbolo * EspectrogramaJt9.Salto;
        var frecuencia = candidata.Casilla * EspectrogramaJt9.CasillaHz;

        const int Margen = N;
        var largo = (TablasJt9.Simbolos * N) + (2 * Margen);
        var re = new float[largo];
        var im = new float[largo];
        BajarABandaBase(comienzo - Margen, frecuencia, re, im);
        var bloques = largo / Bloque;
        var bre = new float[bloques];
        var bim = new float[bloques];
        for (var k = 0; k < bloques; k++)
        {
            float sr = 0, si = 0;
            for (var n = 0; n < Bloque; n++) { sr += re[(k * Bloque) + n]; si += im[(k * Bloque) + n]; }
            bre[k] = sr;
            bim[k] = si;
        }

        const int MargenEnBloques = Margen / Bloque;
        var desplazamiento = MejorDesplazamiento(bre, bim, MargenEnBloques, 0.0, -27, 27, 9);
        var desfaseHz = MejorFrecuencia(bre, bim, MargenEnBloques + desplazamiento, -0.5, 0.5, 0.05);
        desplazamiento += MejorDesplazamiento(bre, bim, MargenEnBloques + desplazamiento, desfaseHz, -9, 9, 3);
        desplazamiento += MejorDesplazamiento(bre, bim, MargenEnBloques + desplazamiento, desfaseHz, -3, 3, 1);
        desfaseHz = MejorFrecuencia(bre, bim, MargenEnBloques + desplazamiento, desfaseHz - 0.08, desfaseHz + 0.08, 0.02);

        frecuencia += desfaseHz;
        comienzo += desplazamiento * Bloque;
        var senalRe = new float[TablasJt9.Simbolos * N];
        var senalIm = new float[TablasJt9.Simbolos * N];
        BajarABandaBase(comienzo, frecuencia, senalRe, senalIm);

        // Nueve tonos por simbolo, por correlacion directa: el tono t esta a t espaciados, que
        // son exactamente t ciclos por simbolo.
        var potencias = new float[TablasJt9.Simbolos * TablasJt9.Tonos];
        var cosenos = new double[TablasJt9.Tonos][];
        var senos = new double[TablasJt9.Tonos][];
        for (var t = 0; t < TablasJt9.Tonos; t++)
        {
            cosenos[t] = new double[N];
            senos[t] = new double[N];
            for (var n = 0; n < N; n++)
            {
                var fase = -2 * Math.PI * t * n / N;
                cosenos[t][n] = Math.Cos(fase);
                senos[t][n] = Math.Sin(fase);
            }
        }
        for (var k = 0; k < TablasJt9.Simbolos; k++)
        {
            var desde = k * N;
            for (var t = 0; t < TablasJt9.Tonos; t++)
            {
                double sr = 0, si = 0;
                var c = cosenos[t];
                var s = senos[t];
                for (var n = 0; n < N; n++)
                {
                    var xr = senalRe[desde + n];
                    var xi = senalIm[desde + n];
                    sr += (xr * c[n]) - (xi * s[n]);
                    si += (xr * s[n]) + (xi * c[n]);
                }
                potencias[(k * TablasJt9.Tonos) + t] = (float)((sr * sr) + (si * si));
            }
        }

        // Ruido: el percentil 40 de todas las casillas (una de cada nueve lleva senal, y las
        // que llevan senal estan por arriba).
        var ordenadas = (float[])potencias.Clone();
        Array.Sort(ordenadas);
        var ruido = ordenadas[(int)(ordenadas.Length * 0.4)] / -Math.Log(0.6);
        if (ruido <= 0) ruido = 1e-12;
        for (var i = 0; i < potencias.Length; i++) potencias[i] = (float)(potencias[i] / ruido);

        double enSincronismo = 0, enDatos = 0;
        foreach (var k in TablasJt9.PosicionesDeSincronismo) enSincronismo += potencias[k * TablasJt9.Tonos];
        foreach (var k in TablasJt9.PosicionesDeDatos) enDatos += potencias[k * TablasJt9.Tonos];
        var sincronismo = enDatos <= 0 ? 0 : (enSincronismo / 16) / (enDatos / 69);
        var senalSobreRuido = (enSincronismo / 16) - 1;

        return new MedidaJt9(frecuencia, (double)comienzo / ParametrosJt9.FrecuenciaDeAnalisis, sincronismo, senalSobreRuido, potencias);
    }

    private (double Cos, double Sin) _osc;

    private void BajarABandaBase(int desde, double frecuenciaHz, float[] re, float[] im)
    {
        var paso = -2 * Math.PI * frecuenciaHz / ParametrosJt9.FrecuenciaDeAnalisis;
        double cosPaso = Math.Cos(paso), sinPaso = Math.Sin(paso);
        for (var i = 0; i < re.Length; i++)
        {
            var n = desde + i;
            if ((i & 1023) == 0)
            {
                var fase = paso * n;
                _osc = (Math.Cos(fase), Math.Sin(fase));
            }
            var x = n >= 0 && n < _audio.Length ? _audio[n] : 0f;
            re[i] = (float)(x * _osc.Cos);
            im[i] = (float)(x * _osc.Sin);
            _osc = ((_osc.Cos * cosPaso) - (_osc.Sin * sinPaso), (_osc.Cos * sinPaso) + (_osc.Sin * cosPaso));
        }
    }

    /// <summary>
    /// Puntuacion de sincronismo del resumen por bloques: potencia del tono 0 en los intervalos
    /// de sincronismo partida por la de los de datos, con un desplazamiento (en bloques) y una
    /// desviacion de frecuencia dados.
    /// </summary>
    private static double Sincronismo(float[] re, float[] im, int desde, double desfaseHz)
    {
        var paso = -2 * Math.PI * desfaseHz * Bloque / ParametrosJt9.FrecuenciaDeAnalisis;
        double cosPaso = Math.Cos(paso), sinPaso = Math.Sin(paso);
        double enSincronismo = 0, enDatos = 0;
        double c = 1, s = 0;
        for (var k = 0; k < TablasJt9.Simbolos; k++)
        {
            var inicio = desde + (k * BloquesPorSimbolo);
            double sr = 0, si = 0;
            for (var n = 0; n < BloquesPorSimbolo; n++)
            {
                var xr = re[inicio + n];
                var xi = im[inicio + n];
                sr += (xr * c) - (xi * s);
                si += (xr * s) + (xi * c);
                var c2 = (c * cosPaso) - (s * sinPaso);
                s = (c * sinPaso) + (s * cosPaso);
                c = c2;
            }
            var potencia = (sr * sr) + (si * si);
            if (TablasJt9.Sincronismo[k] == 1) enSincronismo += potencia; else enDatos += potencia;
        }
        return enDatos <= 0 ? 0 : (enSincronismo / 16) / (enDatos / 69);
    }

    private static int MejorDesplazamiento(float[] re, float[] im, int desde, double desfaseHz, int minimo, int maximo, int paso)
    {
        var mejor = 0;
        var mejorValor = double.MinValue;
        for (var d = minimo; d <= maximo; d += paso)
        {
            if (desde + d < 0 || desde + d + (TablasJt9.Simbolos * BloquesPorSimbolo) > re.Length) continue;
            var valor = Sincronismo(re, im, desde + d, desfaseHz);
            if (valor > mejorValor) { mejorValor = valor; mejor = d; }
        }
        return mejor;
    }

    private static double MejorFrecuencia(float[] re, float[] im, int desde, double minimo, double maximo, double paso)
    {
        var mejor = 0.0;
        var mejorValor = double.MinValue;
        for (var f = minimo; f <= maximo + 1e-9; f += paso)
        {
            var valor = Sincronismo(re, im, desde, f);
            if (valor > mejorValor) { mejorValor = valor; mejor = f; }
        }
        return mejor;
    }
}
