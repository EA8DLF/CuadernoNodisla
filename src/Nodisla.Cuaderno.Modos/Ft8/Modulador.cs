namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>
/// Convierte una secuencia de tonos en el audio que se manda al equipo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que no basta con encadenar senos.</b> Si se pasara de un tono al siguiente de golpe,
/// el salto brusco ensuciaria varios kilohercios a cada lado: una estacion sola estropearia la
/// banda a decenas de corresponsales. La solucion que usa FT8 es suavizar el <b>cambio de
/// frecuencia</b> con una campana de Gauss, de modo que la frecuencia se desliza de un tono al
/// otro en vez de saltar. Eso es lo que significan las siglas GFSK.
/// </para>
/// <para>
/// El suavizado se reparte a lo largo de tres simbolos, asi que cada simbolo se solapa con su
/// anterior y su siguiente. A cambio de esa borrosidad la senal cabe holgadamente en 50 Hz y no
/// molesta a nadie. La fase se lleva acumulada muestra a muestra —nunca se recalcula— para que
/// no haya ni un solo salto en los doce segundos y medio que dura el mensaje.
/// </para>
/// <para>
/// El primer y el ultimo simbolo llevan ademas una subida y una bajada de volumen de un octavo
/// de simbolo. Sin ellas, encender y apagar la senal daria un chasquido que tambien se oye en
/// las frecuencias vecinas.
/// </para>
/// </remarks>
public static class Modulador
{
    /// <summary>
    /// Constante del filtro gaussiano: pi por la raiz de dos partido por el logaritmo de dos.
    /// </summary>
    /// <remarks>
    /// Sale de exigir que el filtro tenga su caida de tres decibelios justo donde dice el
    /// parametro de ancho de banda del modo. No es un numero ajustable.
    /// </remarks>
    private const double ConstanteDelFiltro = 5.336446256636997;

    /// <summary>
    /// Sintetiza el audio de una secuencia de tonos.
    /// </summary>
    /// <param name="parametros">Parametros del modo.</param>
    /// <param name="tonos">Un tono por simbolo, con el sincronismo ya intercalado.</param>
    /// <param name="tonoBaseHz">Frecuencia del tono cero dentro del audio, por ejemplo 1500 Hz.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del audio de salida.</param>
    /// <param name="amplitud">Amplitud de pico, de 0 a 1.</param>
    /// <returns>El audio del mensaje, sin silencio delante ni detras.</returns>
    public static float[] Sintetizar(
        ParametrosDelModo parametros,
        ReadOnlySpan<byte> tonos,
        double tonoBaseHz,
        int frecuenciaDeMuestreo = 48000,
        double amplitud = 0.5)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        if (tonos.Length != parametros.SimbolosTotales)
            throw new ArgumentException($"Hacen falta {parametros.SimbolosTotales} tonos y llegan {tonos.Length}.", nameof(tonos));
        if (frecuenciaDeMuestreo <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeMuestreo));
        if (amplitud is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(amplitud));

        var muestrasPorSimbolo = parametros.MuestrasPorSimbolo(frecuenciaDeMuestreo);
        var muestras = parametros.SimbolosTotales * muestrasPorSimbolo;
        var pulso = PulsoGaussiano(muestrasPorSimbolo, parametros.AnchoDeBandaDelFiltro);

        // Avance de fase por muestra. Se parte del tono base y se le va sumando la aportacion
        // de cada simbolo, ensanchada por la campana. Sobran dos simbolos de margen porque la
        // campana del primero empieza antes del principio y la del ultimo acaba despues.
        var margen = muestrasPorSimbolo;
        var avance = new double[muestras + (2 * margen)];
        var avanceDelTonoBase = 2 * Math.PI * tonoBaseHz / frecuenciaDeMuestreo;
        Array.Fill(avance, avanceDelTonoBase);

        // Un tono entero de separacion equivale a este avance por muestra. Como la separacion
        // entre tonos es exactamente la inversa de la duracion del simbolo, sale esta cuenta
        // tan limpia, que ademas garantiza continuidad de fase entre simbolos.
        var avancePorTono = 2 * Math.PI / muestrasPorSimbolo;

        for (var i = 0; i < parametros.SimbolosTotales; i++)
        {
            var desde = i * muestrasPorSimbolo;
            for (var j = 0; j < 3 * muestrasPorSimbolo; j++)
                avance[desde + j] += avancePorTono * tonos[i] * pulso[j];
        }

        // Los dos medios simbolos de los extremos se completan repitiendo el primer y el ultimo
        // tono: si no, la senal empezaria y acabaria deslizandose hacia el tono cero.
        for (var j = 0; j < 2 * margen; j++)
        {
            avance[j] += avancePorTono * tonos[0] * pulso[j + muestrasPorSimbolo];
            avance[j + muestras] += avancePorTono * tonos[parametros.SimbolosTotales - 1] * pulso[j];
        }

        var senal = new float[muestras];
        var fase = 0.0;
        for (var k = 0; k < muestras; k++)
        {
            senal[k] = (float)(amplitud * Math.Sin(fase));
            fase += avance[k + margen];
            if (fase > 2 * Math.PI) fase -= 2 * Math.PI;
        }

        SuavizarExtremos(senal, muestrasPorSimbolo / 8);
        return senal;
    }

    /// <summary>
    /// La campana con la que se suaviza cada cambio de tono, de tres simbolos de ancha.
    /// </summary>
    /// <remarks>
    /// Cada valor es la fraccion del salto de frecuencia que ya se ha recorrido en ese instante.
    /// Empieza casi en cero, sube en forma de ese y acaba casi en uno; sumada a la campana del
    /// simbolo anterior da siempre uno, que es lo que hace que la fase no se descuadre.
    /// </remarks>
    private static double[] PulsoGaussiano(int muestrasPorSimbolo, double anchoDeBanda)
    {
        var pulso = new double[3 * muestrasPorSimbolo];
        for (var i = 0; i < pulso.Length; i++)
        {
            var t = ((double)i / muestrasPorSimbolo) - 1.5;
            var a = ConstanteDelFiltro * anchoDeBanda * (t + 0.5);
            var b = ConstanteDelFiltro * anchoDeBanda * (t - 0.5);
            pulso[i] = (FuncionDeError(a) - FuncionDeError(b)) / 2;
        }
        return pulso;
    }

    private static void SuavizarExtremos(float[] senal, int muestrasDeRampa)
    {
        if (muestrasDeRampa <= 0) return;
        for (var i = 0; i < muestrasDeRampa; i++)
        {
            // Medio coseno: empieza en cero con pendiente cero, que es lo que no chasquea.
            var envolvente = (float)((1 - Math.Cos(Math.PI * i / muestrasDeRampa)) / 2);
            senal[i] *= envolvente;
            senal[senal.Length - 1 - i] *= envolvente;
        }
    }

    /// <summary>
    /// Funcion de error, que .NET no trae.
    /// </summary>
    /// <remarks>
    /// Se usa la aproximacion racional clasica de Abramowitz y Stegun, con un error menor de
    /// una diezmillonesima. Es de sobra: esto acaba multiplicando un salto de frecuencia de
    /// 6,25 Hz, asi que ese error ni se mide.
    /// </remarks>
    private static double FuncionDeError(double x)
    {
        const double A1 = 0.254829592, A2 = -0.284496736, A3 = 1.421413741, A4 = -1.453152027, A5 = 1.061405429;
        const double P = 0.3275911;

        var signo = x < 0 ? -1 : 1;
        x = Math.Abs(x);
        var t = 1.0 / (1.0 + (P * x));
        var y = 1.0 - ((((((((A5 * t) + A4) * t) + A3) * t) + A2) * t) + A1) * t * Math.Exp(-x * x);
        return signo * y;
    }
}
