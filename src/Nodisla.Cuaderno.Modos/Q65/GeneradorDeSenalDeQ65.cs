namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// Convierte los 85 tonos de Q65 en audio, y fabrica ventanas con ruido para el banco.
/// </summary>
/// <remarks>
/// <para>
/// Q65 es MFSK sin suavizado de frecuencia: cada simbolo es un tono puro que dura exactamente
/// lo suyo, y lo unico que se cuida es que la fase no de saltos entre un tono y el siguiente.
/// Como los tonos estan separados justo la inversa de la duracion del simbolo (por el
/// multiplicador del submodo), al final de cada simbolo la fase de todos los tonos coincide y
/// el cambio sale limpio por si solo; aun asi la fase se lleva acumulada muestra a muestra.
/// </para>
/// <para>
/// El primer y el ultimo simbolo llevan una subida y una bajada de volumen en coseno alzado de
/// un octavo de simbolo. Encender o apagar de golpe daria un chasquido que se oye en las
/// frecuencias vecinas.
/// </para>
/// </remarks>
public static class GeneradorDeSenalDeQ65
{
    /// <summary>Ancho de banda de referencia de los informes, como en FT8: 2500 Hz.</summary>
    public const double AnchoDeBandaDeReferencia = 2500.0;

    /// <summary>Sintetiza el audio de una trama de tonos, sin silencio delante ni detras.</summary>
    /// <param name="parametros">Submodo y periodo.</param>
    /// <param name="tonos">Los 85 tonos, con el sincronismo intercalado.</param>
    /// <param name="tonoBaseHz">Frecuencia del tono cero.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo de la salida.</param>
    /// <param name="amplitud">Amplitud de pico, de 0 a 1.</param>
    public static float[] Sintetizar(ParametrosDeQ65 parametros, ReadOnlySpan<byte> tonos, double tonoBaseHz, int frecuenciaDeMuestreo, double amplitud = 0.5)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        if (tonos.Length != TablasDeQ65.SimbolosDeLaTrama)
            throw new ArgumentException($"Hacen falta {TablasDeQ65.SimbolosDeLaTrama} tonos y llegan {tonos.Length}.", nameof(tonos));
        if (frecuenciaDeMuestreo <= 0) throw new ArgumentOutOfRangeException(nameof(frecuenciaDeMuestreo));
        if (amplitud is <= 0 or > 1) throw new ArgumentOutOfRangeException(nameof(amplitud));

        var muestrasPorSimbolo = parametros.DuracionDeSimboloSegundos * frecuenciaDeMuestreo;
        var total = (int)Math.Round(muestrasPorSimbolo * tonos.Length);
        var rampa = Math.Max(1, (int)Math.Round(muestrasPorSimbolo / 8));
        var salida = new float[total];

        double fase = 0;
        for (var n = 0; n < total; n++)
        {
            var simbolo = Math.Min(tonos.Length - 1, (int)(n / muestrasPorSimbolo));
            var frecuencia = tonoBaseHz + (tonos[simbolo] * parametros.EspaciadoDeTonosHz);
            fase += 2 * Math.PI * frecuencia / frecuenciaDeMuestreo;
            if (fase > 2 * Math.PI) fase -= 2 * Math.PI;

            var envolvente = 1.0;
            if (n < rampa) envolvente = 0.5 * (1 - Math.Cos(Math.PI * n / rampa));
            else if (n >= total - rampa) envolvente = 0.5 * (1 - Math.Cos(Math.PI * (total - 1 - n) / rampa));

            salida[n] = (float)(amplitud * envolvente * Math.Sin(fase));
        }
        return salida;
    }

    /// <summary>
    /// Fabrica una ventana entera con una senal a la relacion senal-ruido pedida, referida a
    /// 2500 Hz, con ruido blanco gaussiano. La misma cuenta que en el banco de FT8.
    /// </summary>
    /// <param name="parametros">Submodo y periodo.</param>
    /// <param name="tonos">Tonos de la trama.</param>
    /// <param name="tonoBaseHz">Frecuencia del tono cero.</param>
    /// <param name="desfaseSegundos">Adelanto o retraso respecto al comienzo nominal.</param>
    /// <param name="decibelios">Relacion senal-ruido referida a 2500 Hz.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo de la ventana.</param>
    /// <param name="azar">Generador de numeros al azar, para que se pueda repetir.</param>
    public static float[] Ventana(
        ParametrosDeQ65 parametros,
        ReadOnlySpan<byte> tonos,
        double tonoBaseHz,
        double desfaseSegundos,
        double decibelios,
        int frecuenciaDeMuestreo,
        Random azar)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        ArgumentNullException.ThrowIfNull(azar);
        var ventana = new float[(int)Math.Round((double)parametros.PeriodoSegundos * frecuenciaDeMuestreo)];
        const double Amplitud = 0.35;
        var senal = Sintetizar(parametros, tonos, tonoBaseHz, frecuenciaDeMuestreo, Amplitud);
        var comienzo = (int)Math.Round((parametros.ComienzoNominalSegundos + desfaseSegundos) * frecuenciaDeMuestreo);
        for (var i = 0; i < senal.Length; i++)
        {
            var j = comienzo + i;
            if (j >= 0 && j < ventana.Length) ventana[j] += senal[i];
        }
        AnadirRuido(ventana, Amplitud * Amplitud / 2, decibelios, frecuenciaDeMuestreo, azar);
        return ventana;
    }

    /// <summary>Solo ruido blanco, al nivel que tendria una ventana con senal a la relacion indicada.</summary>
    public static float[] SoloRuido(ParametrosDeQ65 parametros, int frecuenciaDeMuestreo, Random azar)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        var ventana = new float[(int)Math.Round((double)parametros.PeriodoSegundos * frecuenciaDeMuestreo)];
        AnadirRuido(ventana, 0.35 * 0.35 / 2, -20, frecuenciaDeMuestreo, azar);
        return ventana;
    }

    /// <summary>Anade ruido blanco a una ventana hasta dejarla en la relacion pedida.</summary>
    public static void AnadirRuido(float[] ventana, double potenciaDeLaSenal, double decibelios, int frecuenciaDeMuestreo, Random azar)
    {
        ArgumentNullException.ThrowIfNull(ventana);
        ArgumentNullException.ThrowIfNull(azar);
        var razon = Math.Pow(10, decibelios / 10);
        var anchoTotal = frecuenciaDeMuestreo / 2.0;
        var desviacion = Math.Sqrt(potenciaDeLaSenal * anchoTotal / (AnchoDeBandaDeReferencia * razon));
        for (var i = 0; i < ventana.Length; i++)
        {
            var u1 = 1.0 - azar.NextDouble();
            var u2 = azar.NextDouble();
            ventana[i] += (float)(desviacion * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}
