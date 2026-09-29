namespace Nodisla.Cuaderno.Modos.Fst4;

/// <summary>
/// Convierte los 160 tonos de FST4 en audio 4-GFSK.
/// </summary>
/// <remarks>
/// <para>
/// Es la misma modulacion suavizada de FT8 —el cambio de frecuencia entre tonos se reparte con
/// una campana de Gauss de tres simbolos de ancha, con el mismo parametro de ancho de banda— y
/// la fase se lleva acumulada muestra a muestra para que no haya ni un salto. La diferencia es
/// que aqui un simbolo puede durar desde 60 milisegundos hasta once segundos, y a algunas
/// frecuencias de muestreo no cae en un numero entero de muestras: por eso la campana se
/// evalua por tiempo y no por muestras, y vale para cualquier frecuencia de salida.
/// </para>
/// </remarks>
public static class ModuladorFst4
{
    /// <summary>Constante del filtro gaussiano: pi por la raiz de dos partido por el logaritmo de dos.</summary>
    private const double ConstanteDelFiltro = 5.336446256636997;

    /// <summary>Puntos por simbolo con los que se tabula la campana.</summary>
    private const int PuntosPorSimbolo = 256;

    private static readonly double[] Campana = TabularCampana();

    /// <summary>
    /// Sintetiza el audio de una secuencia de tonos.
    /// </summary>
    /// <param name="parametros">Parametros del periodo.</param>
    /// <param name="tonos">Los 160 tonos.</param>
    /// <param name="tonoBaseHz">Frecuencia del tono cero.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo de la salida.</param>
    /// <param name="amplitud">Amplitud de pico, de 0 a 1.</param>
    /// <returns>El audio del mensaje, sin silencio delante ni detras.</returns>
    public static float[] Sintetizar(
        ParametrosFst4 parametros,
        ReadOnlySpan<byte> tonos,
        double tonoBaseHz,
        int frecuenciaDeMuestreo = 48000,
        double amplitud = 0.5)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        if (tonos.Length != ParametrosFst4.SimbolosTotales)
            throw new ArgumentException($"Hacen falta {ParametrosFst4.SimbolosTotales} tonos y llegan {tonos.Length}.", nameof(tonos));
        if (frecuenciaDeMuestreo <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeMuestreo));
        if (amplitud is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(amplitud));

        var muestrasPorSimbolo = parametros.MuestrasPorSimbolo(frecuenciaDeMuestreo);
        var muestras = (int)Math.Round(ParametrosFst4.SimbolosTotales * muestrasPorSimbolo);
        var senal = new float[muestras];

        var avanceBase = 2 * Math.PI * tonoBaseHz / frecuenciaDeMuestreo;
        var avancePorTono = 2 * Math.PI * parametros.EspaciadoDeTonosHz / frecuenciaDeMuestreo;
        var fase = 0.0;
        var n = tonos.Length;

        for (var k = 0; k < muestras; k++)
        {
            // Tiempo en simbolos. Los tres simbolos que aportan a esta muestra son el que le
            // toca y sus dos vecinos; fuera del mensaje se repite el primero y el ultimo tono.
            var t = k / muestrasPorSimbolo;
            var s = (int)Math.Floor(t);
            var desviacion = 0.0;
            for (var d = -1; d <= 1; d++)
            {
                var i = Math.Clamp(s + d, 0, n - 1);
                desviacion += tonos[i] * Valor(t - (s + d));
            }
            senal[k] = (float)(amplitud * Math.Sin(fase));
            fase += avanceBase + (avancePorTono * desviacion);
            if (fase > 2 * Math.PI) fase -= 2 * Math.PI;
        }

        SuavizarExtremos(senal, (int)Math.Round(muestrasPorSimbolo / 8));
        return senal;
    }

    /// <summary>
    /// Valor de la campana para un simbolo, a <paramref name="tau"/> simbolos de su comienzo.
    /// </summary>
    /// <remarks>
    /// La campana esta centrada en el medio del simbolo y se extiende un simbolo a cada lado;
    /// sumada con la del simbolo anterior y el siguiente da siempre uno.
    /// </remarks>
    private static double Valor(double tau)
    {
        // tau va de -1 (un simbolo antes de que empiece) a 2 (un simbolo despues de que acabe).
        var x = (tau + 1) * PuntosPorSimbolo;
        if (x <= 0) return 0;
        if (x >= Campana.Length - 1) return 0;
        var i = (int)x;
        var f = x - i;
        return Campana[i] + (f * (Campana[i + 1] - Campana[i]));
    }

    private static double[] TabularCampana()
    {
        var tabla = new double[(3 * PuntosPorSimbolo) + 1];
        for (var i = 0; i < tabla.Length; i++)
        {
            var t = ((double)i / PuntosPorSimbolo) - 1.5;
            var a = ConstanteDelFiltro * ParametrosFst4.AnchoDeBandaDelFiltro * (t + 0.5);
            var b = ConstanteDelFiltro * ParametrosFst4.AnchoDeBandaDelFiltro * (t - 0.5);
            tabla[i] = (FuncionDeError(a) - FuncionDeError(b)) / 2;
        }
        return tabla;
    }

    private static void SuavizarExtremos(float[] senal, int muestrasDeRampa)
    {
        if (muestrasDeRampa <= 0 || senal.Length < 2 * muestrasDeRampa) return;
        for (var i = 0; i < muestrasDeRampa; i++)
        {
            var envolvente = (float)((1 - Math.Cos(Math.PI * i / muestrasDeRampa)) / 2);
            senal[i] *= envolvente;
            senal[senal.Length - 1 - i] *= envolvente;
        }
    }

    /// <summary>Funcion de error, aproximacion de Abramowitz y Stegun con error menor de una diezmillonesima.</summary>
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
