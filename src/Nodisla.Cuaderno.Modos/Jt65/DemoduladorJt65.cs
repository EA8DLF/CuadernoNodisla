using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Jt65;

/// <summary>Lo que se midio de una candidata al mirarla de cerca.</summary>
/// <param name="TonoBaseHz">Frecuencia afinada del tono de sincronismo.</param>
/// <param name="ComienzoSegundos">Instante afinado en que empieza la senal, desde el principio de la ventana.</param>
/// <param name="Sincronismo">Puntuacion del sincronismo tras afinar (uno es ruido).</param>
/// <param name="SenalSobreRuido">Potencia de la senal partida por la del ruido en una casilla de 2,69 Hz.</param>
/// <param name="Potencias">
/// 63 × 64 potencias, en unidades de ruido: para cada posicion de la palabra Reed-Solomon
/// (paridad primero), la potencia de cada uno de los 64 simbolos posibles. Es lo que come el
/// decodificador blando.
/// </param>
public sealed record MedidaJt65(double TonoBaseHz, double ComienzoSegundos, double Sincronismo, double SenalSobreRuido, float[] Potencias);

/// <summary>
/// Mira de cerca una candidata: afina su instante y su frecuencia y mide los 64 tonos de cada
/// simbolo de datos.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bajar a banda base.</b> La senal se multiplica por una exponencial compleja a la
/// frecuencia del tono de sincronismo, con lo que ese tono queda en cero hercios y los de datos
/// en multiplos exactos del espaciado. A partir de ahi cada simbolo es una transformada de 4096
/// puntos y el tono <c>i</c> cae justo en la casilla <c>i</c> (o <c>2i</c>, <c>4i</c> en los
/// submodos B y C), sin fugas entre casillas: es el filtro adaptado de un tono que dura justo
/// un simbolo.
/// </para>
/// <para>
/// <b>Afinar.</b> La busqueda gruesa deja el arranque a medio simbolo y la frecuencia a media
/// casilla. Antes de medir se afinan las dos cosas maximizando la misma puntuacion de
/// sincronismo del buscador, pero calculada sobre la banda base resumida en bloques de 64
/// muestras: para el instante basta con sumar los bloques de cada simbolo (el tono de
/// sincronismo esta en cero hercios), y para la frecuencia se giran los bloques unas decimas
/// de hercio arriba y abajo. Sale a seis milisegundos y a cinco centesimas de hercio.
/// </para>
/// <para>
/// <b>El ruido.</b> Las potencias se dan en unidades de la potencia media del ruido en una
/// casilla, que se estima con la mediana de todas las casillas de todos los simbolos: en ruido
/// blanco la potencia de una casilla es exponencial y su mediana es ln 2 veces la media, y la
/// mediana no se inmuta porque haya otras senales en la ventana.
/// </para>
/// </remarks>
public sealed class DemoduladorJt65
{
    private readonly float[] _audio;
    private readonly ParametrosJt65 _parametros;

    /// <summary>Crea el demodulador para una ventana.</summary>
    /// <param name="audio">Ventana a 11025 muestras por segundo.</param>
    /// <param name="parametros">Submodo.</param>
    public DemoduladorJt65(float[] audio, ParametrosJt65 parametros)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(parametros);
        _audio = audio;
        _parametros = parametros;
    }

    /// <summary>Muestras por bloque del resumen con el que se afina: 64 bloques por simbolo.</summary>
    private const int Bloque = 64;

    /// <summary>Afina y mide una candidata.</summary>
    public MedidaJt65 Medir(CandidataJt65 candidata)
    {
        const int N = ParametrosJt65.MuestrasPorSimbolo;
        const int Fs = ParametrosJt65.FrecuenciaDeAnalisis;
        var comienzo = candidata.MedioSimbolo * EspectrogramaJt65.Salto;
        var frecuencia = candidata.Casilla * EspectrogramaJt65.CasillaHz;

        // Banda base con margen de un simbolo a cada lado para poder afinar el instante, y
        // resumida en bloques de 64 muestras: el tono de sincronismo esta en cero hercios (a lo
        // sumo a una casilla de distancia), asi que sumar 64 muestras seguidas no le quita nada
        // y deja el afinado sesenta y cuatro veces mas barato.
        const int Margen = N;
        var largo = (TablasJt65.Simbolos * N) + (2 * Margen);
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

        // Instante a un cuarto de simbolo, frecuencia a una decima de hercio, instante a un
        // bloque (5,8 ms), y la frecuencia otra vez con el instante ya fino.
        const int MargenEnBloques = Margen / Bloque;
        var desplazamiento = MejorDesplazamiento(bre, bim, MargenEnBloques, 0.0, -16, 16, 4);
        var desfaseHz = MejorFrecuencia(bre, bim, MargenEnBloques + desplazamiento, -0.9, 0.9, 0.1);
        desplazamiento += MejorDesplazamiento(bre, bim, MargenEnBloques + desplazamiento, desfaseHz, -4, 4, 1);
        desfaseHz = MejorFrecuencia(bre, bim, MargenEnBloques + desplazamiento, desfaseHz - 0.15, desfaseHz + 0.15, 0.05);

        // Se vuelve a bajar con la frecuencia y el instante afinados para que los tonos caigan clavados.
        frecuencia += desfaseHz;
        comienzo += desplazamiento * Bloque;
        var senalRe = new float[TablasJt65.Simbolos * N];
        var senalIm = new float[TablasJt65.Simbolos * N];
        BajarABandaBase(comienzo, frecuencia, senalRe, senalIm);

        var m = _parametros.EspaciadoEnCasillas;
        var potencias = new float[63 * 64];
        var real = new float[N];
        var imaginaria = new float[N];
        var muestraDeRuido = new float[TablasJt65.Simbolos * (N / 16)];
        var ruidoVisto = 0;
        double enSincronismo = 0;
        double enDatos = 0;
        var datosVistos = 0;

        for (var k = 0; k < TablasJt65.Simbolos; k++)
        {
            senalRe.AsSpan(k * N, N).CopyTo(real);
            senalIm.AsSpan(k * N, N).CopyTo(imaginaria);
            Fft.Transformar(real, imaginaria);
            for (var c = 0; c < N; c++)
            {
                real[c] = (real[c] * real[c]) + (imaginaria[c] * imaginaria[c]);
                if ((c & 15) == 7) muestraDeRuido[ruidoVisto++] = real[c];
            }

            if (TablasJt65.Sincronismo[k] == 1)
            {
                enSincronismo += real[0];
            }
            else
            {
                enDatos += real[0];
                var posicion = TablasJt65.EntrelazadoDesdeElAire[datosVistos++];
                var fila = potencias.AsSpan(posicion * 64, 64);
                for (var s = 0; s < 64; s++)
                    fila[s] = real[(TablasJt65.PrimerTonoDeDatos + TablasJt65.Gray[s]) * m];
            }
        }

        Array.Sort(muestraDeRuido);
        var ruido = muestraDeRuido[muestraDeRuido.Length / 2] / Math.Log(2);
        if (ruido <= 0) ruido = 1e-12;
        for (var i = 0; i < potencias.Length; i++) potencias[i] = (float)(potencias[i] / ruido);

        // Sin ruido ninguno (una senal limpia sintetica) los intervalos de datos no tienen nada
        // en esa casilla y el cociente se dispararia o se anularia; se acota por abajo con el
        // ruido medido, que en cualquier grabacion real es mucho mayor que este suelo.
        var sincronismo = enSincronismo <= 0 ? 0 : enSincronismo / Math.Max(enDatos, 1e-9 * enSincronismo);
        var senalSobreRuido = ((enSincronismo / 63) / ruido) - 1;

        return new MedidaJt65(frecuencia, (double)comienzo / Fs, sincronismo, senalSobreRuido, potencias);
    }

    /// <summary>Multiplica el audio por una exponencial compleja a −frecuencia, desde la muestra dada. Fuera del audio hay ceros.</summary>
    private void BajarABandaBase(int desde, double frecuenciaHz, float[] re, float[] im)
    {
        var paso = -2 * Math.PI * frecuenciaHz / ParametrosJt65.FrecuenciaDeAnalisis;
        // El oscilador se lleva por rotacion incremental y se vuelve a calcular cada 1024
        // muestras para que el redondeo no lo desafine.
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

    private (double Cos, double Sin) _osc;

    /// <summary>
    /// Puntuacion de sincronismo del resumen por bloques con un desplazamiento (en bloques) y
    /// una desviacion de frecuencia dados: potencia del tono de cero hercios en los intervalos
    /// de sincronismo partida por la de los de datos.
    /// </summary>
    private static double Sincronismo(float[] re, float[] im, int desde, double desfaseHz)
    {
        const int BloquesPorSimbolo = ParametrosJt65.MuestrasPorSimbolo / Bloque;
        var paso = -2 * Math.PI * desfaseHz * Bloque / ParametrosJt65.FrecuenciaDeAnalisis;
        double cosPaso = Math.Cos(paso), sinPaso = Math.Sin(paso);
        double enSincronismo = 0, enDatos = 0;
        double c = 1, s = 0;

        for (var k = 0; k < TablasJt65.Simbolos; k++)
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
            if (TablasJt65.Sincronismo[k] == 1) enSincronismo += potencia; else enDatos += potencia;
        }
        return enDatos <= 0 ? 0 : enSincronismo / enDatos;
    }

    private static int MejorDesplazamiento(float[] re, float[] im, int desde, double desfaseHz, int minimo, int maximo, int paso)
    {
        const int BloquesPorSimbolo = ParametrosJt65.MuestrasPorSimbolo / Bloque;
        var mejor = 0;
        var mejorValor = double.MinValue;
        for (var d = minimo; d <= maximo; d += paso)
        {
            if (desde + d < 0 || desde + d + (TablasJt65.Simbolos * BloquesPorSimbolo) > re.Length) continue;
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
