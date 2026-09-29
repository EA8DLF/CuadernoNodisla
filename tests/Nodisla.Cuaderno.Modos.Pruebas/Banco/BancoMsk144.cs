using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Msk144;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>Como fue una franja del banco de MSK144.</summary>
/// <param name="Decibelios">Relacion senal-ruido de los pings, en 2500 Hz.</param>
/// <param name="Tramas">Tramas que duraba cada ping (72 ms cada una).</param>
/// <param name="Intentos">Ventanas probadas.</param>
/// <param name="Aciertos">Ventanas en que salio el mensaje emitido.</param>
/// <param name="Falsos">Mensajes que salieron y no eran el emitido. <b>Tiene que ser cero.</b></param>
/// <param name="PalabrasValidas">Palabras que el corrector dio por buenas.</param>
/// <param name="Rechazadas">De esas, las que tiraron los frenos y el CRC.</param>
/// <param name="PorPromediado">Aciertos que solo salieron sumando tramas.</param>
/// <param name="ErrorDelInforme">Diferencia media entre el informe medido y el real.</param>
/// <param name="MilisegundosPorVentana">Coste medio, en milisegundos de procesador.</param>
public sealed record FranjaMsk144(
    double Decibelios,
    int Tramas,
    int Intentos,
    int Aciertos,
    int Falsos,
    int PalabrasValidas,
    int Rechazadas,
    int PorPromediado,
    double ErrorDelInforme,
    double MilisegundosPorVentana)
{
    /// <summary>Porcentaje de pings recuperados.</summary>
    public double Porcentaje => Intentos == 0 ? 0 : 100.0 * Aciertos / Intentos;
}

/// <summary>
/// Mide cuantos pings de MSK144 recupera el decodificador, por relacion senal-ruido y por
/// duracion del ping.
/// </summary>
/// <remarks>
/// Cada ventana de 15 segundos lleva un solo ping —tantas tramas seguidas como se pida, con
/// un mensaje al azar entre ocho— en un instante al azar y con la portadora desviada hasta
/// ±80 Hz, sobre ruido blanco gaussiano. El ping tiene envolvente rectangular con dos
/// milisegundos de rampa; los de verdad decaen, pero para medir el decodificador vale.
/// </remarks>
public static class BancoMsk144
{
    private static readonly string[] Mensajes =
    [
        "CQ EA8DLF IL18",
        "EA8DLF EA1ABC IN80",
        "EA1ABC EA8DLF -07",
        "CQ K1ABC FN42",
        "K1ABC EA8DLF R-15",
        "EA8DLF K1ABC RR73",
        "CQ DX EA8DLF IL18",
        "JA1XYZ EA8DLF 73",
    ];

    private const int Fs = ParametrosMsk144.FrecuenciaDeAnalisis;

    /// <summary>Recorre las franjas de relacion senal-ruido con pings de una duracion fija.</summary>
    public static List<FranjaMsk144> PorRelacion(
        DecodificadorMsk144 decodificador,
        CodificadorMsk144 codificador,
        int tramas,
        double desde,
        double hasta,
        double paso,
        int ventanasPorFranja,
        int semilla = 20260927)
    {
        var franjas = new List<FranjaMsk144>();
        for (var db = desde; db >= hasta - 1e-9; db += paso)
            franjas.Add(Franja(decodificador, codificador, tramas, db, ventanasPorFranja, semilla));
        return franjas;
    }

    /// <summary>Recorre duraciones de ping a una relacion senal-ruido fija.</summary>
    public static List<FranjaMsk144> PorDuracion(
        DecodificadorMsk144 decodificador,
        CodificadorMsk144 codificador,
        double decibelios,
        IEnumerable<int> tramas,
        int ventanasPorFranja,
        int semilla = 20260928)
    {
        var franjas = new List<FranjaMsk144>();
        foreach (var t in tramas)
            franjas.Add(Franja(decodificador, codificador, t, decibelios, ventanasPorFranja, semilla));
        return franjas;
    }

    private static FranjaMsk144 Franja(DecodificadorMsk144 decodificador, CodificadorMsk144 codificador, int tramas, double db, int ventanas, int semilla)
    {
        int aciertos = 0, falsos = 0, validas = 0, rechazadas = 0, promediadas = 0;
        double errores = 0, coste = 0;
        for (var v = 0; v < ventanas; v++)
        {
            var azar = new Random(semilla + (int)(db * 1000) + (tramas * 104729) + (v * 7919));
            var texto = Mensajes[azar.Next(Mensajes.Length)];
            var portadora = 1500 + ((azar.NextDouble() - 0.5) * 160);
            var segundo = 0.2 + (azar.NextDouble() * (14.0 - (tramas * ParametrosMsk144.DuracionDeTramaSegundos)));

            var ventana = Ventana(codificador, texto, portadora, segundo, tramas, db, azar);
            ResultadoMsk144 resultado = null!;
            coste += Medidas.TiempoDeProceso.De(() =>
                resultado = decodificador.Decodificar(ventana, Fs, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos()));

            validas += resultado.PalabrasValidas;
            rechazadas += resultado.RechazadasPorElCrc;
            var acertada = resultado.Decodificaciones.FirstOrDefault(d => d.Texto == texto);
            if (acertada is not null)
            {
                aciertos++;
                errores += acertada.Decibelios - db;
                if (resultado.PorPromediado > 0 && resultado.Detalles.Where(d => d.Aceptada && d.Texto == texto).All(d => d.Tramas > 1)) promediadas++;
            }
            falsos += resultado.Decodificaciones.Count(d => d.Texto != texto);
        }
        return new FranjaMsk144(db, tramas, ventanas, aciertos, falsos, validas, rechazadas, promediadas,
            aciertos == 0 ? 0 : errores / aciertos, coste / ventanas);
    }

    /// <summary>Ventanas de ruido puro, para contar lo que se inventa.</summary>
    public static (int Mensajes, int PalabrasValidas, int Rechazadas, double MilisegundosPorVentana) RuidoPuro(DecodificadorMsk144 decodificador, int ventanas, int semilla = 20260929)
    {
        int mensajes = 0, validas = 0, rechazadas = 0;
        double coste = 0;
        for (var v = 0; v < ventanas; v++)
        {
            var ventana = new float[15 * Fs];
            GeneradorDeSenal.AnadirRuido(ventana, potenciaDeLaSenal: 0.01, decibelios: 0, Fs, new Random(semilla + (v * 104729)));
            ResultadoMsk144 resultado = null!;
            coste += Medidas.TiempoDeProceso.De(() =>
                resultado = decodificador.Decodificar(ventana, Fs, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos()));
            mensajes += resultado.Decodificaciones.Count;
            validas += resultado.PalabrasValidas;
            rechazadas += resultado.RechazadasPorElCrc;
        }
        return (mensajes, validas, rechazadas, ventanas == 0 ? 0 : coste / ventanas);
    }

    /// <summary>Una ventana de 15 s con un ping de tantas tramas.</summary>
    public static float[] Ventana(CodificadorMsk144 codificador, string texto, double portadoraHz, double segundo, int tramas, double decibelios, Random azar)
    {
        ArgumentNullException.ThrowIfNull(codificador);
        if (!codificador.TryCodificar(texto, out var trama, out var motivo)) throw new InvalidOperationException(motivo);
        var senal = ModuladorMsk.Sintetizar(trama, portadoraHz, Fs, tramas, amplitud: 0.35);
        var ventana = new float[15 * Fs];
        var comienzo = (int)Math.Round(segundo * Fs);
        for (var i = 0; i < senal.Length; i++)
        {
            var j = comienzo + i;
            if (j >= 0 && j < ventana.Length) ventana[j] += senal[i];
        }
        GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(senal), decibelios, Fs, azar);
        return ventana;
    }

    /// <summary>Escribe las cifras en una tabla legible.</summary>
    public static string Tabla(string titulo, IReadOnlyList<FranjaMsk144> franjas)
    {
        ArgumentNullException.ThrowIfNull(franjas);
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.Append("### ").AppendLine(titulo).AppendLine();
        sb.AppendLine("| S/R (dB) | Tramas del ping | Ventanas | Recuperados | % | Falsos | Palabras válidas | Rechazadas | Sólo por promediado | Error del informe (dB) | ms de CPU/ventana |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var f in franjas)
            sb.AppendLine(string.Format(c,
                "| {0:0} | {1} | {2} | {3} | {4:0.0} | {5} | {6} | {7} | {8} | {9:+0.0;-0.0;0,0} | {10:0} |",
                f.Decibelios, f.Tramas, f.Intentos, f.Aciertos, f.Porcentaje, f.Falsos, f.PalabrasValidas, f.Rechazadas,
                f.PorPromediado, f.ErrorDelInforme, f.MilisegundosPorVentana));
        sb.AppendLine();
        return sb.ToString();
    }
}
