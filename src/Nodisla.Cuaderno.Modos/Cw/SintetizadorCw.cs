namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>Cómo manipula quien transmite: velocidad y «fist».</summary>
/// <param name="Wpm">Palabras por minuto (PARIS): el punto dura 1200/WPM ms.</param>
/// <param name="Desorden">
/// Desviación típica relativa de cada duración (0 = manipulador electrónico perfecto; 0,15 = una
/// mano a la llave vertical).
/// </param>
/// <param name="RazonDeRaya">Duración de la raya en puntos (3 de norma; a mano, de 2,5 a 4).</param>
/// <param name="EspacioEntreCaracteres">Espacio entre caracteres en puntos (3 de norma).</param>
/// <param name="EspacioEntrePalabras">Espacio entre palabras en puntos (7 de norma).</param>
/// <param name="Peso">Lo que se alarga cada marca, en puntos, a costa del hueco que la sigue.</param>
/// <param name="RampaMs">Subida y bajada de cada marca (coseno alzado), contra los «clics».</param>
public sealed record ManeraDeManipular(
    double Wpm,
    double Desorden = 0,
    double RazonDeRaya = 3,
    double EspacioEntreCaracteres = 3,
    double EspacioEntrePalabras = 7,
    double Peso = 0,
    double RampaMs = 5)
{
    /// <summary>Duración del punto, en segundos.</summary>
    public double Punto => 1.2 / Wpm;
}

/// <summary>Un tramo del manipulador: llave abajo (marca) o arriba (espacio), y cuánto dura.</summary>
public readonly record struct TramoDeLlave(bool Marca, double Segundos);

/// <summary>
/// Genera telegrafía: el audio de un texto en Morse, con la mano que se le pida.
/// </summary>
/// <remarks>
/// Sirve para el banco de medida y para el audio simulado de la aplicación. <b>No transmite</b>:
/// devuelve muestras y ningún camino del programa las lleva a la salida de audio del equipo.
/// </remarks>
public static class SintetizadorCw
{
    /// <summary>Los tramos de llave de un texto.</summary>
    /// <param name="texto">Texto con prosignos entre ángulos («CQ DE EA8DLF &lt;AR&gt;»).</param>
    /// <param name="manera">Velocidad y fist.</param>
    /// <param name="azar">Azar para el desorden de la mano; nulo = sin desorden.</param>
    /// <param name="cambioDeVelocidad">
    /// Velocidad en función del símbolo (índice, total) para probar cambios a mitad; nulo = fija.
    /// </param>
    public static IReadOnlyList<TramoDeLlave> Tramos(
        string texto,
        ManeraDeManipular manera,
        Random? azar = null,
        Func<int, int, double>? cambioDeVelocidad = null)
    {
        ArgumentNullException.ThrowIfNull(manera);
        var simbolos = TablaMorse.Simbolos(texto);
        var tramos = new List<TramoDeLlave>();

        for (var s = 0; s < simbolos.Count; s++)
        {
            var simbolo = simbolos[s];
            var wpm = cambioDeVelocidad?.Invoke(s, simbolos.Count) ?? manera.Wpm;
            var punto = 1.2 / wpm;

            if (simbolo == " ")
            {
                // Lo que falta del espacio de palabra tras el de carácter ya puesto.
                Hueco(tramos, (manera.EspacioEntrePalabras - manera.EspacioEntreCaracteres) * punto, manera, azar);
                continue;
            }

            var codigo = TablaMorse.Codificar(simbolo)!;
            for (var e = 0; e < codigo.Length; e++)
            {
                var largo = codigo[e] == '-' ? manera.RazonDeRaya : 1.0;
                tramos.Add(new TramoDeLlave(true, Mano((largo + manera.Peso) * punto, manera, azar)));
                var hueco = e < codigo.Length - 1 ? 1.0 : manera.EspacioEntreCaracteres;
                Hueco(tramos, (hueco - manera.Peso) * punto, manera, azar);
            }
        }

        return tramos;
    }

    /// <summary>
    /// El audio de un texto: un tono manipulado, con silencio delante y detrás.
    /// </summary>
    /// <param name="texto">Texto a manipular.</param>
    /// <param name="tonoHz">Tono de audio.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo.</param>
    /// <param name="manera">Velocidad y fist.</param>
    /// <param name="amplitud">Amplitud de pico del tono con la llave abajo.</param>
    /// <param name="azar">Azar del fist; nulo = perfecto.</param>
    /// <param name="silencioDelante">Segundos de silencio antes de la primera marca.</param>
    /// <param name="silencioDetras">Segundos de silencio tras la última.</param>
    /// <param name="ganancia">Ganancia en función del tiempo en segundos (QSB); nulo = 1.</param>
    /// <param name="cambioDeVelocidad">Velocidad por símbolo; nulo = la de <paramref name="manera"/>.</param>
    public static float[] Generar(
        string texto,
        double tonoHz,
        int frecuenciaDeMuestreo,
        ManeraDeManipular manera,
        double amplitud = 0.5,
        Random? azar = null,
        double silencioDelante = 0.5,
        double silencioDetras = 0.5,
        Func<double, double>? ganancia = null,
        Func<int, int, double>? cambioDeVelocidad = null)
    {
        ArgumentNullException.ThrowIfNull(manera);
        var tramos = Tramos(texto, manera, azar, cambioDeVelocidad);
        var envolvente = Envolvente(tramos, frecuenciaDeMuestreo, manera.RampaMs, silencioDelante, silencioDetras);
        var salida = new float[envolvente.Length];
        var fase = azar?.NextDouble() * 2 * Math.PI ?? 0;
        var paso = 2 * Math.PI * tonoHz / frecuenciaDeMuestreo;
        for (var i = 0; i < salida.Length; i++)
        {
            var g = ganancia?.Invoke(i / (double)frecuenciaDeMuestreo) ?? 1.0;
            salida[i] = (float)(amplitud * g * envolvente[i] * Math.Sin(fase + (paso * i)));
        }

        return salida;
    }

    /// <summary>La envolvente de llave (0 a 1) de unos tramos, con rampas de coseno alzado.</summary>
    public static float[] Envolvente(
        IReadOnlyList<TramoDeLlave> tramos,
        int frecuenciaDeMuestreo,
        double rampaMs,
        double silencioDelante,
        double silencioDetras)
    {
        ArgumentNullException.ThrowIfNull(tramos);
        var total = silencioDelante + tramos.Sum(t => t.Segundos) + silencioDetras;
        var envolvente = new float[(int)Math.Ceiling(total * frecuenciaDeMuestreo)];
        var rampa = Math.Max(1, (int)Math.Round(rampaMs / 1000 * frecuenciaDeMuestreo));
        var t0 = silencioDelante;
        foreach (var tramo in tramos)
        {
            if (tramo.Marca)
            {
                var desde = (int)Math.Round(t0 * frecuenciaDeMuestreo);
                var hasta = Math.Min(envolvente.Length, (int)Math.Round((t0 + tramo.Segundos) * frecuenciaDeMuestreo));
                var r = Math.Min(rampa, Math.Max(1, (hasta - desde) / 2));
                for (var i = desde; i < hasta; i++)
                {
                    var dentro = Math.Min(i - desde, hasta - 1 - i);
                    envolvente[i] = dentro >= r ? 1f : (float)(0.5 - (0.5 * Math.Cos(Math.PI * (dentro + 0.5) / r)));
                }
            }

            t0 += tramo.Segundos;
        }

        return envolvente;
    }

    /// <summary>QSB: desvanecimiento senoidal de <paramref name="profundidadDb"/> dB a <paramref name="hercios"/> Hz.</summary>
    public static Func<double, double> Qsb(double profundidadDb, double hercios, double fase = 0) =>
        t => Math.Pow(10, -profundidadDb / 20 * (0.5 - (0.5 * Math.Cos((2 * Math.PI * hercios * t) + fase))));

    private static void Hueco(List<TramoDeLlave> tramos, double segundos, ManeraDeManipular manera, Random? azar)
    {
        var s = Mano(segundos, manera, azar);
        if (tramos.Count > 0 && !tramos[^1].Marca)
            tramos[^1] = new TramoDeLlave(false, tramos[^1].Segundos + s);
        else
            tramos.Add(new TramoDeLlave(false, s));
    }

    private static double Mano(double segundos, ManeraDeManipular manera, Random? azar)
    {
        if (azar is null || manera.Desorden <= 0) return segundos;
        var u1 = 1.0 - azar.NextDouble();
        var u2 = azar.NextDouble();
        var normal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        return segundos * Math.Clamp(1 + (manera.Desorden * normal), 0.4, 1.8);
    }
}
