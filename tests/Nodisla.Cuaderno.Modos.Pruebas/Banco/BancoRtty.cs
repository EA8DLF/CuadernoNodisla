using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Rtty;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// El banco de medida de RTTY: audio sintético con <see cref="GeneradorRtty"/> sobre ruido blanco
/// gaussiano, demodulado con <see cref="CanalRtty"/>, para medir hasta qué relación señal-ruido
/// decodifica y comprobar que el ruido puro no inventa texto.
/// </summary>
public static class BancoRtty
{
    /// <summary>Muestras por segundo del banco.</summary>
    public const int Frecuencia = 8000;

    /// <summary>El audio de un texto, con ruido a la relación señal-ruido pedida (ver <see cref="GeneradorDeSenal"/>).</summary>
    public static float[] Senal(
        string texto, double tonoDeMarcaHz, int frecuencia, ParametrosRtty parametros, double decibelios, Random azar, double amplitud = 0.3)
    {
        var audio = GeneradorRtty.Generar(texto, tonoDeMarcaHz, frecuencia, parametros, amplitud, silencioDelante: 0.3, silencioDetras: 0.3);
        GeneradorDeSenal.AnadirRuido(audio, amplitud * amplitud / 2, decibelios, frecuencia, azar);
        return audio;
    }

    /// <summary>Pasa un audio entero por el canal y devuelve lo escrito y si llegó a engancharse.</summary>
    public static (string Texto, bool Enganchado) Decodificar(
        float[] audio, int frecuencia, double tonoDeMarcaHz, ParametrosRtty parametros, double anchoDelFiltroHz = 45)
    {
        var canal = new CanalRtty(frecuencia, tonoDeMarcaHz, parametros, anchoDelFiltroHz);
        var texto = new System.Text.StringBuilder();
        canal.Texto += t => texto.Append(t);
        foreach (var x in audio) canal.Anadir(x);
        return (texto.ToString(), canal.Enganchado);
    }

    /// <summary>Distancia de Levenshtein entre dos textos, para medir el parecido de lo leído.</summary>
    public static int Distancia(string a, string b)
    {
        var previa = new int[b.Length + 1];
        var actual = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previa[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            actual[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var coste = a[i - 1] == b[j - 1] ? 0 : 1;
                actual[j] = Math.Min(Math.Min(actual[j - 1] + 1, previa[j] + 1), previa[j - 1] + coste);
            }

            (previa, actual) = (actual, previa);
        }

        return previa[b.Length];
    }
}
