using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Cw;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>Una franja del banco de telegrafía.</summary>
/// <param name="Escenario">Qué se le hace a la señal.</param>
/// <param name="Wpm">Velocidad de la señal.</param>
/// <param name="Decibelios">Relación señal-ruido en 2500 Hz (potencia con la llave abajo).</param>
/// <param name="Caracteres">Caracteres esperados (sin contar espacios repetidos).</param>
/// <param name="Errores">Distancia de edición entre lo esperado y lo leído.</param>
/// <param name="WpmMedida">Velocidad que estimó el decodificador al final (media de las tiradas).</param>
public sealed record FranjaCw(string Escenario, double Wpm, double Decibelios, int Caracteres, int Errores, double WpmMedida)
{
    /// <summary>Tasa de error de caracteres, en tanto por ciento.</summary>
    public double Cer => Caracteres == 0 ? 0 : 100.0 * Errores / Caracteres;
}

/// <summary>
/// El banco de medida del decodificador de telegrafía: CW sintética sobre ruido blanco gaussiano,
/// con la mano, el QSB y el QRM que se le pidan, y la tasa de error de caracteres de lo leído.
/// </summary>
public static class BancoCw
{
    /// <summary>Muestras por segundo del banco (la tarjeta de verdad va a 48 kHz y se diezma a esto).</summary>
    public const int Frecuencia = 8000;

    /// <summary>Los textos del banco: trozos de contacto con indicativos, números, puntuación y prosignos.</summary>
    public static readonly string[] Textos =
    [
        "CQ CQ CQ DE EA8DLF EA8DLF K",
        "DL1ABC DE EA8DLF GM UR RST 599 5NN NAME LUIS QTH VILLA HW? <KN>",
        "EA8DLF DE G4XYZ TNX FER CALL UR 579 <BT> RIG FT710 ANT DIPOLE 73 <SK>",
        "QRZ? TEST 1234567890 / . , ? <AR>",
    ];

    /// <summary>Lo que se le hace a la señal en una franja.</summary>
    public enum Escenario
    {
        /// <summary>Manipulador electrónico, señal limpia de ruido aparte.</summary>
        Limpia,
        /// <summary>Mano humana: duraciones con un 15 % de desorden y rayas de 3,4 puntos.</summary>
        Mano,
        /// <summary>Desvanecimiento de 15 dB a 0,2 Hz.</summary>
        Qsb,
        /// <summary>Otra telegrafía 150 Hz por encima, 6 dB más fuerte, a otra velocidad.</summary>
        Qrm,
        /// <summary>La velocidad pasa de la nominal a 1,6 veces a mitad de mensaje.</summary>
        CambioDeVelocidad,
    }

    /// <summary>Pasa todos los textos en una franja.</summary>
    public static FranjaCw Medir(Escenario escenario, double wpm, double decibelios, int semilla, OpcionesCw? opciones = null, double? tonoFijo = null)
    {
        var azar = new Random(semilla);
        int caracteres = 0, errores = 0;
        double wpmMedida = 0;
        foreach (var texto in Textos)
        {
            var tono = 600 + (azar.NextDouble() * 200);
            var audio = Senal(escenario, texto, wpm, decibelios, tono, azar);
            // Con QRM más fuerte el automático se va, con razón, a la señal fuerte: quien quiere
            // la débil la elige con un clic en el espectro, que es fijar el tono.
            var fijo = escenario == Escenario.Qrm ? tono : tonoFijo;
            var (leido, wpmFinal) = Decodificar(audio, Frecuencia, opciones, fijo);
            var esperado = Normalizar(texto);
            caracteres += Longitud(esperado);
            errores += Distancia(esperado, Normalizar(leido));
            wpmMedida += wpmFinal;
        }

        return new FranjaCw(Nombre(escenario), wpm, decibelios, caracteres, errores, wpmMedida / Textos.Length);
    }

    /// <summary>El audio de un texto en un escenario, ruido incluido.</summary>
    public static float[] Senal(Escenario escenario, string texto, double wpm, double decibelios, double tonoHz, Random azar)
    {
        const double amplitud = 0.3;
        var manera = escenario == Escenario.Mano
            ? new ManeraDeManipular(wpm, Desorden: 0.15, RazonDeRaya: 3.4)
            : new ManeraDeManipular(wpm);
        Func<double, double>? ganancia = escenario == Escenario.Qsb ? SintetizadorCw.Qsb(15, 0.2, azar.NextDouble() * 6) : null;
        Func<int, int, double>? velocidad = escenario == Escenario.CambioDeVelocidad ? (s, total) => s < total / 2 ? wpm : wpm * 1.6 : null;

        var audio = SintetizadorCw.Generar(texto, tonoHz, Frecuencia, manera, amplitud, azar, 1.5, 1.5, ganancia, velocidad);

        if (escenario == Escenario.Qrm)
        {
            var otra = SintetizadorCw.Generar(
                "TEST DE OH2BH OH2BH TEST DE OH2BH OH2BH TEST DE OH2BH",
                tonoHz + 150,
                Frecuencia,
                new ManeraDeManipular(wpm * 1.3),
                amplitud * 2,
                azar,
                0.2,
                0);
            for (var i = 0; i < audio.Length; i++) audio[i] += otra[i % otra.Length];
        }

        GeneradorDeSenal.AnadirRuido(audio, amplitud * amplitud / 2, decibelios, Frecuencia, azar);
        return audio;
    }

    /// <summary>Decodifica un audio entero y devuelve lo que escribió el canal principal.</summary>
    public static (string Texto, double Wpm) Decodificar(float[] audio, int frecuencia, OpcionesCw? opciones = null, double? tonoFijo = null)
    {
        var decodificador = new DecodificadorCw(opciones ?? new OpcionesCw { CanalesMaximos = 1 });
        if (tonoFijo is { } f) decodificador.FijarTono(f);
        var texto = new StringBuilder();
        decodificador.TextoDecodificado += (_, t) =>
        {
            if (t.EsPrincipal) texto.Append(t.Texto);
        };

        // En bloques de 20 ms, como llega de la tarjeta.
        var bloque = frecuencia / 50;
        for (var i = 0; i < audio.Length; i += bloque)
            decodificador.Alimentar(audio.AsSpan(i, Math.Min(bloque, audio.Length - i)), frecuencia);
        decodificador.Vaciar();
        return (texto.ToString(), decodificador.Estado.Principal?.Wpm ?? 0);
    }

    /// <summary>Todos los textos de todos los canales, por canal.</summary>
    public static Dictionary<int, string> DecodificarTodo(float[] audio, int frecuencia, OpcionesCw opciones)
    {
        var decodificador = new DecodificadorCw(opciones);
        var textos = new Dictionary<int, StringBuilder>();
        decodificador.TextoDecodificado += (_, t) =>
        {
            if (!textos.TryGetValue(t.Canal, out var sb)) textos[t.Canal] = sb = new StringBuilder();
            sb.Append(t.Texto);
        };

        var bloque = frecuencia / 50;
        for (var i = 0; i < audio.Length; i += bloque)
            decodificador.Alimentar(audio.AsSpan(i, Math.Min(bloque, audio.Length - i)), frecuencia);
        decodificador.Vaciar();
        return textos.ToDictionary(p => p.Key, p => p.Value.ToString());
    }

    /// <summary>Mayúsculas, un solo espacio entre palabras y sin espacios en los bordes.</summary>
    public static string Normalizar(string texto) =>
        string.Join(' ', texto.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Distancia de Levenshtein, contando cada prosigno como un carácter.</summary>
    public static int Distancia(string a, string b)
    {
        var x = Simbolos(a);
        var y = Simbolos(b);
        var previa = new int[y.Count + 1];
        var actual = new int[y.Count + 1];
        for (var j = 0; j <= y.Count; j++) previa[j] = j;
        for (var i = 1; i <= x.Count; i++)
        {
            actual[0] = i;
            for (var j = 1; j <= y.Count; j++)
            {
                var coste = x[i - 1] == y[j - 1] ? 0 : 1;
                actual[j] = Math.Min(Math.Min(actual[j - 1] + 1, previa[j] + 1), previa[j - 1] + coste);
            }

            (previa, actual) = (actual, previa);
        }

        return previa[y.Count];
    }

    /// <summary>Caracteres de un texto, con cada prosigno («&lt;AR&gt;») como uno solo.</summary>
    public static List<string> Simbolos(string texto)
    {
        var lista = new List<string>();
        for (var i = 0; i < texto.Length; i++)
        {
            if (texto[i] == '<')
            {
                var cierre = texto.IndexOf('>', i);
                if (cierre > i)
                {
                    lista.Add(texto[i..(cierre + 1)]);
                    i = cierre;
                    continue;
                }
            }

            lista.Add(texto[i].ToString());
        }

        return lista;
    }

    /// <summary>Longitud de un texto contando cada prosigno como uno.</summary>
    public static int Longitud(string texto) => Simbolos(texto).Count;

    /// <summary>La tabla de un conjunto de franjas en markdown.</summary>
    public static string Tabla(string titulo, IEnumerable<FranjaCw> franjas)
    {
        var c = CultureInfo.GetCultureInfo("es-ES");
        var sb = new StringBuilder();
        sb.AppendLine(c, $"### {titulo}");
        sb.AppendLine();
        sb.AppendLine("| Escenario | WPM | S/R en 2500 Hz (dB) | S/R en 500 Hz (dB) | Caracteres | Errores | CER (%) | WPM medida |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var f in franjas)
        {
            sb.AppendLine(c, $"| {f.Escenario} | {f.Wpm:0} | {f.Decibelios:0} | {f.Decibelios + 7:0} | {f.Caracteres} | {f.Errores} | {f.Cer:0.0} | {f.WpmMedida:0} |");
        }

        sb.AppendLine();
        return sb.ToString();
    }

    private static string Nombre(Escenario e) => e switch
    {
        Escenario.Limpia => "Manipulador",
        Escenario.Mano => "Mano (15 %)",
        Escenario.Qsb => "QSB 15 dB",
        Escenario.Qrm => "QRM a +150 Hz",
        Escenario.CambioDeVelocidad => "Cambio ×1,6",
        _ => e.ToString(),
    };
}
