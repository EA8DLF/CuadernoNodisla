using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>Lo que se saca de mirar una candidata de cerca.</summary>
/// <param name="PuntuacionDeSincronismo">Cuanto destaca el patron de Costas tras afinar.</param>
/// <param name="PotenciaDeLaSenal">Potencia media del tono ganador en los simbolos de sincronismo.</param>
/// <param name="PotenciaDelRuido">Potencia media de los tonos perdedores, que es el ruido de su banda.</param>
/// <param name="TonoBaseHz">Frecuencia afinada del tono cero.</param>
/// <param name="ComienzoEnSegundos">Instante afinado en que empieza la senal dentro del trozo analizado.</param>
public readonly record struct MedidaDeCandidata(
    double PuntuacionDeSincronismo,
    double PotenciaDeLaSenal,
    double PotenciaDelRuido,
    double TonoBaseHz,
    double ComienzoEnSegundos);

/// <summary>
/// Convierte una candidata en grados de confianza sobre cada uno de los 174 bits.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que confianzas y no bits.</b> Lo facil seria mirar que tono suena mas fuerte en cada
/// simbolo y apuntar sus tres bits. Eso se llama decision dura y tira a la basura la mitad de
/// la informacion: no distingue entre «suena el tono 5, clarisimo» y «puede que el 5, o puede
/// que el 4». El corrector de errores saca mucho mas partido si se le dice <i>cuanto</i> de
/// seguro esta cada bit; la diferencia son alrededor de dos decibelios de sensibilidad, que en
/// FT8 es muchisimo.
/// </para>
/// <para>
/// <b>Como se mide la confianza de un bit.</b> Cada simbolo puede ser uno de ocho tonos, y cada
/// tono lleva tres bits. Para el primer bit, cuatro tonos dirian cero y cuatro dirian uno. Se
/// coge el mas fuerte de los que dirian cero, el mas fuerte de los que dirian uno, y se restan
/// sus logaritmos. Si el primero manda con claridad sale un numero positivo grande; si estan
/// empatados sale casi cero, que es la forma de decir «no lo se».
/// </para>
/// <para>
/// <b>Afinado.</b> La busqueda deja la candidata con una precision de medio tono en frecuencia y
/// medio simbolo en tiempo. Antes de demodular se prueban unos cuantos ajustes alrededor y se
/// escoge el que mejor hace sonar el sincronismo. Sin eso se pierde casi un decibelio.
/// </para>
/// </remarks>
public sealed class Demodulador
{
    private readonly AnalisisDeVentana _analisis;
    private readonly float[] _bandaReal;
    private readonly float[] _bandaImaginaria;
    private readonly float[] _potenciaPorTono;
    private readonly float[] _trabajoReal;
    private readonly float[] _trabajoImaginaria;

    /// <summary>Prepara el demodulador para una ventana ya analizada.</summary>
    /// <param name="analisis">Ventana analizada.</param>
    public Demodulador(AnalisisDeVentana analisis)
    {
        ArgumentNullException.ThrowIfNull(analisis);
        _analisis = analisis;
        _bandaReal = new float[analisis.MuestrasDeLaBandaBase];
        _bandaImaginaria = new float[analisis.MuestrasDeLaBandaBase];
        _potenciaPorTono = new float[analisis.Parametros.SimbolosTotales * analisis.Parametros.Tonos];
        _trabajoReal = new float[analisis.MuestrasPorSimboloEnBase];
        _trabajoImaginaria = new float[analisis.MuestrasPorSimboloEnBase];
    }

    /// <summary>Confianzas del ultimo <see cref="Medir"/>, una por bit emitido.</summary>
    public float[] Confianzas { get; private set; } = [];

    /// <summary>Tonos que mas sonaron en el ultimo <see cref="Medir"/>, uno por simbolo.</summary>
    public byte[] TonosMasProbables { get; private set; } = [];

    /// <summary>
    /// Afina la candidata, la demodula y deja las confianzas listas para el corrector.
    /// </summary>
    /// <param name="candidata">Candidata que devolvio el sincronizador.</param>
    /// <param name="segundosDelPrimerMuestreo">
    /// Instante, respecto al comienzo de la ventana, al que corresponde la primera muestra del
    /// audio analizado. Sirve para poder decir el desfase de verdad.
    /// </param>
    public MedidaDeCandidata Medir(Candidata candidata, double segundosDelPrimerMuestreo = 0)
    {
        var p = _analisis.Parametros;
        var muestrasPorSimbolo = _analisis.MuestrasPorSimboloEnBase;
        var frecuenciaCandidata = _analisis.FrecuenciaDe(candidata.Casilla);

        // Afinado en frecuencia: se prueban medias casillas a un lado y a otro. Cada prueba
        // exige volver a recortar la banda, asi que se hacen pocas y bien elegidas.
        var pasoFino = _analisis.HzPorCasilla / 2;
        var mejorPuntuacion = double.NegativeInfinity;
        var mejorFrecuencia = frecuenciaCandidata;
        var mejorComienzo = candidata.Bloque * p.Tonos;

        for (var df = -1; df <= 1; df++)
        {
            var frecuencia = frecuenciaCandidata + (df * pasoFino);
            if (frecuencia <= 0) continue;
            _analisis.ExtraerBandaBase(frecuencia, _bandaReal, _bandaImaginaria);

            // Afinado en tiempo: el bloque del espectrograma es medio simbolo, asi que se
            // prueban desplazamientos de un cuarto de simbolo a cada lado.
            var paso = Math.Max(1, muestrasPorSimbolo / 4);
            for (var dt = -1; dt <= 1; dt++)
            {
                var comienzo = (candidata.Bloque * p.Tonos) + (dt * paso);
                if (comienzo < 0) continue;
                var puntuacion = PuntuarSincronismo(comienzo);
                if (puntuacion <= mejorPuntuacion) continue;
                mejorPuntuacion = puntuacion;
                mejorFrecuencia = frecuencia;
                mejorComienzo = comienzo;
            }
        }

        // Se vuelve a recortar con lo mejor encontrado, porque la ultima prueba pudo no serlo.
        _analisis.ExtraerBandaBase(mejorFrecuencia, _bandaReal, _bandaImaginaria);
        MedirTodosLosSimbolos(mejorComienzo);
        var (senal, ruido) = MedirNiveles();
        CalcularConfianzas();

        var muestrasPorSegundo = muestrasPorSimbolo * p.EspaciadoDeTonosHz;
        var comienzoEnSegundos = segundosDelPrimerMuestreo + (mejorComienzo / muestrasPorSegundo);

        return new MedidaDeCandidata(mejorPuntuacion, senal, ruido, mejorFrecuencia, comienzoEnSegundos);
    }

    /// <summary>Mide la potencia de cada tono en cada simbolo.</summary>
    private void MedirTodosLosSimbolos(int comienzo)
    {
        var p = _analisis.Parametros;
        var muestrasPorSimbolo = _analisis.MuestrasPorSimboloEnBase;
        for (var s = 0; s < p.SimbolosTotales; s++)
            MedirUnSimbolo(comienzo + (s * muestrasPorSimbolo), _potenciaPorTono.AsSpan(s * p.Tonos, p.Tonos));
    }

    /// <summary>
    /// Mide la potencia de cada tono en un simbolo.
    /// </summary>
    /// <remarks>
    /// La senal en banda base tiene justo dos muestras por tono, asi que una transformada de
    /// ocho o dieciseis puntos —segun el modo— coloca cada tono en una casilla exacta. Es la
    /// transformada mas pequena posible que separa los tonos sin mezclarlos, y por eso el
    /// decodificador es rapido.
    /// </remarks>
    private void MedirUnSimbolo(int desde, Span<float> potencias)
    {
        var p = _analisis.Parametros;
        var n = _analisis.MuestrasPorSimboloEnBase;
        var total = _bandaReal.Length;

        for (var i = 0; i < n; i++)
        {
            // El recorte de banda hace la senal circular; el modulo evita salirse por los bordes.
            var j = ((desde + i) % total + total) % total;
            _trabajoReal[i] = _bandaReal[j];
            _trabajoImaginaria[i] = _bandaImaginaria[j];
        }
        Fft.Transformar(_trabajoReal, _trabajoImaginaria);

        for (var t = 0; t < p.Tonos; t++)
        {
            var c = t + AnalisisDeVentana.MargenEnTonos;
            potencias[t] = (_trabajoReal[c] * _trabajoReal[c]) + (_trabajoImaginaria[c] * _trabajoImaginaria[c]);
        }
    }

    private double PuntuarSincronismo(int comienzo)
    {
        var p = _analisis.Parametros;
        var muestrasPorSimbolo = _analisis.MuestrasPorSimboloEnBase;
        Span<float> potencias = stackalloc float[p.Tonos];
        double enElPatron = 0, enTodos = 0;

        for (var g = 0; g < p.PosicionesDeCostas.Length; g++)
        {
            var inicio = p.PosicionesDeCostas[g];
            var grupo = p.GruposDeCostas[g];
            for (var k = 0; k < grupo.Length; k++)
            {
                MedirUnSimbolo(comienzo + ((inicio + k) * muestrasPorSimbolo), potencias);
                enElPatron += potencias[grupo[k]];
                for (var t = 0; t < p.Tonos; t++) enTodos += potencias[t];
            }
        }
        return enTodos <= 0 ? 0 : enElPatron * p.Tonos / enTodos;
    }

    /// <summary>Separa lo que es senal de lo que es ruido usando los simbolos de sincronismo.</summary>
    /// <remarks>
    /// En un simbolo de sincronismo se sabe de antemano que tono deberia sonar. Lo que se mide
    /// en ese tono es senal mas ruido; lo que se mide en los otros siete es solo ruido. Restando
    /// se obtienen los dos niveles sin necesidad de un trozo de banda vacio que medir aparte.
    /// </remarks>
    private (double Senal, double Ruido) MedirNiveles()
    {
        var p = _analisis.Parametros;
        double conSenal = 0, sinSenal = 0;
        int cuantosCon = 0, cuantosSin = 0;

        for (var s = 0; s < p.SimbolosTotales; s++)
        {
            if (!p.EsSimboloDeSincronismo(s, out var tonoBueno)) continue;
            for (var t = 0; t < p.Tonos; t++)
            {
                var potencia = _potenciaPorTono[(s * p.Tonos) + t];
                if (t == tonoBueno) { conSenal += potencia; cuantosCon++; }
                else { sinSenal += potencia; cuantosSin++; }
            }
        }

        var ruido = cuantosSin > 0 ? sinSenal / cuantosSin : 0;
        var senal = cuantosCon > 0 ? Math.Max(0, (conSenal / cuantosCon) - ruido) : 0;
        return (senal, ruido);
    }

    /// <summary>Pasa de potencias por tono a una confianza por bit.</summary>
    private void CalcularConfianzas()
    {
        var p = _analisis.Parametros;
        var bits = p.SimbolosDeDatos * p.BitsPorSimbolo;
        if (Confianzas.Length != bits)
        {
            Confianzas = new float[bits];
            TonosMasProbables = new byte[p.SimbolosTotales];
        }

        for (var s = 0; s < p.SimbolosTotales; s++)
        {
            var mejor = 0;
            for (var t = 1; t < p.Tonos; t++)
                if (_potenciaPorTono[(s * p.Tonos) + t] > _potenciaPorTono[(s * p.Tonos) + mejor]) mejor = t;
            TonosMasProbables[s] = (byte)mejor;
        }

        var posiciones = p.PosicionesDeDatos;
        var bitsPorSimbolo = p.BitsPorSimbolo;
        // Suelo para no calcular el logaritmo de cero cuando un tono cae justo en un nulo.
        const float Suelo = 1e-20f;

        for (var s = 0; s < posiciones.Length; s++)
        {
            var baseTonos = posiciones[s] * p.Tonos;
            for (var b = 0; b < bitsPorSimbolo; b++)
            {
                var peso = 1 << (bitsPorSimbolo - 1 - b);
                float mejorCero = 0, mejorUno = 0;
                for (var t = 0; t < p.Tonos; t++)
                {
                    var valor = p.MapaDeGrayInverso[t];
                    var potencia = _potenciaPorTono[baseTonos + t];
                    if ((valor & peso) == 0) { if (potencia > mejorCero) mejorCero = potencia; }
                    else { if (potencia > mejorUno) mejorUno = potencia; }
                }
                Confianzas[(s * bitsPorSimbolo) + b] = MathF.Log(mejorCero + Suelo) - MathF.Log(mejorUno + Suelo);
            }
        }

        Normalizar(Confianzas);
    }

    /// <summary>
    /// Deja las confianzas en una escala conocida.
    /// </summary>
    /// <remarks>
    /// El corrector trabaja igual de bien con cualquier escala —solo mira cual es mayor que
    /// cual— pero tiene un tope por encima del cual deja de creerse las cosas. Llevando la
    /// media al mismo sitio siempre, ese tope significa lo mismo en una senal fuerte que en una
    /// debil, y el decodificador se comporta igual en los dos casos.
    /// </remarks>
    private static void Normalizar(float[] confianzas)
    {
        double suma = 0;
        foreach (var v in confianzas) suma += Math.Abs(v);
        var media = suma / confianzas.Length;
        if (media < 1e-9) return;
        var escala = (float)(2.0 / media);
        for (var i = 0; i < confianzas.Length; i++) confianzas[i] *= escala;
    }
}
