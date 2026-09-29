using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Fst4;
using Nodisla.Cuaderno.Modos.Ft8;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>Como fue una franja del banco de FST4.</summary>
/// <param name="Decibelios">Relacion senal-ruido, en 2500 Hz.</param>
/// <param name="Intentos">Ventanas probadas.</param>
/// <param name="Aciertos">Ventanas en que salio el mensaje emitido.</param>
/// <param name="Falsos">Mensajes que salieron y no eran el emitido. <b>Tiene que ser cero.</b></param>
/// <param name="PorRecuperacionProfunda">Aciertos que salieron por la recuperacion profunda.</param>
/// <param name="RechazadosPorElCrc">Palabras validas que el CRC tiro.</param>
/// <param name="ErrorDelInforme">Diferencia media entre el informe medido y el real.</param>
/// <param name="MilisegundosPorVentana">Coste medio, en milisegundos de procesador.</param>
public sealed record FranjaFst4(
    double Decibelios,
    int Intentos,
    int Aciertos,
    int Falsos,
    int PorRecuperacionProfunda,
    int RechazadosPorElCrc,
    double ErrorDelInforme,
    double MilisegundosPorVentana)
{
    /// <summary>Porcentaje recuperado.</summary>
    public double Porcentaje => Intentos == 0 ? 0 : 100.0 * Aciertos / Intentos;
}

/// <summary>
/// Mide cuantos mensajes de FST4 y FST4W recupera el decodificador en cada franja de relacion
/// senal-ruido, para cada periodo.
/// </summary>
public static class BancoFst4
{
    private static readonly string[] MensajesFst4 =
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

    private static readonly string[] MensajesFst4w =
    [
        "EA8DLF IL18 37",
        "K1ABC FN42 37",
        "EA1ABC IN80 30",
        "G4JNT IO90 23",
        "VK3XYZ QF22 40",
        "JA1XYZ PM95 33",
    ];

    private const int Fs = 12000;

    /// <summary>Recorre las franjas para un periodo.</summary>
    public static List<FranjaFst4> Recorrer(
        DecodificadorFst4 decodificador,
        CodificadorFst4 codificador,
        double desde,
        double hasta,
        double paso,
        int ventanasPorFranja,
        int semilla = 20260930)
    {
        ArgumentNullException.ThrowIfNull(decodificador);
        ArgumentNullException.ThrowIfNull(codificador);
        var p = decodificador.Parametros;
        var mensajes = decodificador.EsFst4w ? MensajesFst4w : MensajesFst4;
        var franjas = new List<FranjaFst4>();
        for (var db = desde; db >= hasta - 1e-9; db += paso)
        {
            int aciertos = 0, falsos = 0, profundas = 0, rechazadas = 0;
            double errores = 0, coste = 0;
            for (var v = 0; v < ventanasPorFranja; v++)
            {
                var azar = new Random(semilla + (int)(db * 1000) + (p.PeriodoSegundos * 31) + (v * 7919));
                var texto = mensajes[azar.Next(mensajes.Length)];
                var tono = decodificador.FrecuenciaMinima + 50 + (azar.NextDouble() * (decodificador.FrecuenciaMaxima - decodificador.FrecuenciaMinima - 100 - p.AnchoDeBandaHz));
                var desfase = (azar.NextDouble() - 0.5) * Math.Min(1.0, p.DuracionDeSimboloSegundos * 2);

                var ventana = Ventana(codificador, p, texto, tono, desfase, db, azar);
                ResultadoFst4 resultado = null!;
                coste += Medidas.TiempoDeProceso.De(() =>
                    resultado = decodificador.Decodificar(ventana, Fs, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos()));

                rechazadas += resultado.RechazadasPorElCrc;
                var acertada = resultado.Decodificaciones.FirstOrDefault(d => d.Texto == texto);
                if (acertada is not null)
                {
                    aciertos++;
                    errores += acertada.Decibelios - db;
                    if (acertada.EsRecuperacionProfunda) profundas++;
                }
                falsos += resultado.Decodificaciones.Count(d => d.Texto != texto);
            }
            franjas.Add(new FranjaFst4(db, ventanasPorFranja, aciertos, falsos, profundas, rechazadas,
                aciertos == 0 ? 0 : errores / aciertos, coste / ventanasPorFranja));
        }
        return franjas;
    }

    /// <summary>Ventanas de ruido puro, para contar lo que se inventa.</summary>
    public static (int Mensajes, int PalabrasValidas, double MilisegundosPorVentana) RuidoPuro(DecodificadorFst4 decodificador, int ventanas, int semilla = 20261001)
    {
        ArgumentNullException.ThrowIfNull(decodificador);
        int mensajes = 0, validas = 0;
        double coste = 0;
        var p = decodificador.Parametros;
        for (var v = 0; v < ventanas; v++)
        {
            var ventana = new float[p.PeriodoSegundos * Fs];
            GeneradorDeSenal.AnadirRuido(ventana, potenciaDeLaSenal: 0.01, decibelios: 0, Fs, new Random(semilla + (v * 104729)));
            ResultadoFst4 resultado = null!;
            coste += Medidas.TiempoDeProceso.De(() =>
                resultado = decodificador.Decodificar(ventana, Fs, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos()));
            mensajes += resultado.Decodificaciones.Count;
            validas += resultado.PalabrasValidas;
        }
        return (mensajes, validas, ventanas == 0 ? 0 : coste / ventanas);
    }

    /// <summary>Una ventana del periodo con la senal en su instante nominal mas el desfase.</summary>
    public static float[] Ventana(CodificadorFst4 codificador, ParametrosFst4 p, string texto, double tonoHz, double desfaseSegundos, double decibelios, Random azar)
    {
        ArgumentNullException.ThrowIfNull(codificador);
        ArgumentNullException.ThrowIfNull(p);
        if (!codificador.TryCodificar(texto, out var tonos, out var motivo)) throw new InvalidOperationException(motivo);
        var senal = ModuladorFst4.Sintetizar(p, tonos, tonoHz, Fs, amplitud: 0.35);
        var ventana = new float[p.PeriodoSegundos * Fs];
        var comienzo = (int)Math.Round((p.ComienzoNominalSegundos + desfaseSegundos) * Fs);
        for (var i = 0; i < senal.Length; i++)
        {
            var j = comienzo + i;
            if (j >= 0 && j < ventana.Length) ventana[j] += senal[i];
        }
        GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(senal), decibelios, Fs, azar);
        return ventana;
    }

    /// <summary>Escribe las cifras en una tabla legible.</summary>
    public static string Tabla(string titulo, IReadOnlyList<FranjaFst4> franjas)
    {
        ArgumentNullException.ThrowIfNull(franjas);
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.Append("### ").AppendLine(titulo).AppendLine();
        sb.AppendLine("| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var f in franjas)
            sb.AppendLine(string.Format(c,
                "| {0:0} | {1} | {2} | {3:0.0} | {4} | {5} | {6} | {7:+0.0;-0.0;0,0} | {8:0} |",
                f.Decibelios, f.Intentos, f.Aciertos, f.Porcentaje, f.Falsos, f.PorRecuperacionProfunda, f.RechazadosPorElCrc,
                f.ErrorDelInforme, f.MilisegundosPorVentana));
        sb.AppendLine();
        return sb.ToString();
    }
}
