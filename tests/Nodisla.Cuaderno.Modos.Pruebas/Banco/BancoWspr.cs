using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>Como fue una franja de relacion senal-ruido en WSPR.</summary>
/// <param name="Decibelios">Relacion senal-ruido de las senales de la franja, referida a 2500 Hz.</param>
/// <param name="Intentos">Ventanas probadas.</param>
/// <param name="Aciertos">Ventanas en que salio el mensaje emitido.</param>
/// <param name="Falsos">Mensajes que salieron y no eran el emitido. <b>Tiene que ser cero.</b></param>
/// <param name="CaminosDeFano">Veces que Fano llego al final del arbol.</param>
/// <param name="RechazadosPorLaGramatica">Caminos completos que no formaban un mensaje valido.</param>
/// <param name="RechazadosPorLaCoincidencia">Mensajes validos que no se parecian a lo recibido.</param>
/// <param name="CoincidenciaMinima">La coincidencia mas baja de un acierto, para saber el margen del umbral.</param>
/// <param name="ErrorDelInforme">Diferencia media entre la relacion medida y la de verdad.</param>
/// <param name="MilisegundosPorVentana">Milisegundos de procesador por ventana, de media.</param>
public sealed record FranjaWspr(
    double Decibelios,
    int Intentos,
    int Aciertos,
    int Falsos,
    int CaminosDeFano,
    int RechazadosPorLaGramatica,
    int RechazadosPorLaCoincidencia,
    double CoincidenciaMinima,
    double ErrorDelInforme,
    double MilisegundosPorVentana)
{
    /// <summary>Porcentaje de mensajes recuperados.</summary>
    public double Porcentaje => Intentos == 0 ? 0 : 100.0 * Aciertos / Intentos;
}

/// <summary>
/// Mide cuantos mensajes de WSPR recupera el decodificador en cada franja de relacion senal-ruido.
/// </summary>
/// <remarks>
/// El mismo procedimiento que el banco de FT8: senal sintetica de nivel conocido, ruido blanco
/// gaussiano hasta dejarla en la relacion pedida (referida a 2500 Hz, como los informes de
/// WSPR), y a contar. Cada ventana lleva un mensaje al azar, en una frecuencia al azar dentro
/// de la banda de 200 Hz, con un desfase al azar de ±1,5 s y una deriva al azar de ±1 Hz.
/// </remarks>
public static class BancoWspr
{
    private static readonly string[] Mensajes =
    [
        "EA8DLF IL18 37",
        "K1ABC FN42 37",
        "EA1ABC IN80 30",
        "G4JNT IO90 23",
        "VK3XYZ QF22 40",
        "PJ4/K1ABC 37",
        "EA8DLF/P 20",
        "JA1XYZ PM95 33",
    ];

    /// <summary>
    /// Recorre las franjas y devuelve las cifras.
    /// </summary>
    /// <param name="decodificador">Decodificador con el que medir.</param>
    /// <param name="desde">Relacion mas alta, en decibelios.</param>
    /// <param name="hasta">Relacion mas baja.</param>
    /// <param name="paso">Cuanto se baja en cada franja.</param>
    /// <param name="ventanasPorFranja">Ventanas por franja.</param>
    /// <param name="semilla">Semilla, para que la medida se repita igual.</param>
    /// <param name="conDeriva">Si las senales llevan deriva de frecuencia al azar.</param>
    public static List<FranjaWspr> Recorrer(
        DecodificadorWspr decodificador,
        double desde = -20,
        double hasta = -34,
        double paso = -2,
        int ventanasPorFranja = 20,
        int semilla = 20260926,
        bool conDeriva = true)
    {
        ArgumentNullException.ThrowIfNull(decodificador);
        var franjas = new List<FranjaWspr>();
        for (var db = desde; db >= hasta - 1e-9; db += paso)
        {
            int aciertos = 0, falsos = 0, caminos = 0, sinGramatica = 0, sinCoincidencia = 0;
            double sumaDeErrores = 0, coste = 0, coincidenciaMinima = 1;

            for (var v = 0; v < ventanasPorFranja; v++)
            {
                var azar = new Random(semilla + (int)(db * 1000) + (v * 7919));
                var texto = Mensajes[azar.Next(Mensajes.Length)];
                var frecuencia = 1400 + (azar.NextDouble() * 200);
                var desfase = (azar.NextDouble() - 0.5) * 3.0;
                var deriva = conDeriva ? (azar.NextDouble() - 0.5) * 2.0 : 0;

                var ventana = Ventana(texto, frecuencia, desfase, deriva, db, azar);
                ResultadoWspr resultado = null!;
                coste += Medidas.TiempoDeProceso.De(() =>
                    resultado = decodificador.Decodificar(ventana, DateTimeOffset.UnixEpoch));

                caminos += resultado.CaminosDeFano;
                sinGramatica += resultado.RechazadasPorLaGramatica;
                sinCoincidencia += resultado.RechazadasPorLaCoincidencia;
                var acertada = resultado.Decodificaciones.FirstOrDefault(d => d.Texto == texto);
                if (acertada is not null)
                {
                    aciertos++;
                    sumaDeErrores += acertada.Decibelios - db;
                    var detalle = resultado.Detalles.First(d => d.Texto == texto && d.Aceptado);
                    coincidenciaMinima = Math.Min(coincidenciaMinima, detalle.Coincidencia);
                }
                falsos += resultado.Decodificaciones.Count(d => d.Texto != texto);
            }

            franjas.Add(new FranjaWspr(
                db, ventanasPorFranja, aciertos, falsos, caminos, sinGramatica, sinCoincidencia,
                aciertos == 0 ? 0 : coincidenciaMinima,
                aciertos == 0 ? 0 : sumaDeErrores / aciertos,
                coste / ventanasPorFranja));
        }
        return franjas;
    }

    /// <summary>
    /// Pasa ventanas de ruido puro y cuenta lo que sale. Tiene que salir nada.
    /// </summary>
    /// <param name="decodificador">Decodificador con el que medir.</param>
    /// <param name="ventanas">Cuantas ventanas.</param>
    /// <param name="semilla">Semilla.</param>
    /// <returns>Mensajes que salieron, caminos completos de Fano, milisegundos de procesador por ventana.</returns>
    public static (int Mensajes, int CaminosDeFano, int SinGramatica, int SinCoincidencia, double MilisegundosPorVentana, List<DetalleWspr> Detalles)
        RuidoPuro(DecodificadorWspr decodificador, int ventanas, int semilla = 20260927)
    {
        ArgumentNullException.ThrowIfNull(decodificador);
        int mensajes = 0, caminos = 0, sinGramatica = 0, sinCoincidencia = 0;
        double coste = 0;
        var detalles = new List<DetalleWspr>();
        var muestras = (int)(ParametrosWspr.PeriodoSegundos * ParametrosWspr.FrecuenciaDeAnalisis);
        for (var v = 0; v < ventanas; v++)
        {
            var azar = new Random(semilla + (v * 104729));
            var ventana = new float[muestras];
            GeneradorDeSenal.AnadirRuido(ventana, potenciaDeLaSenal: 0.01, decibelios: 0, ParametrosWspr.FrecuenciaDeAnalisis, azar);
            ResultadoWspr resultado = null!;
            coste += Medidas.TiempoDeProceso.De(() =>
                resultado = decodificador.Decodificar(ventana, DateTimeOffset.UnixEpoch));
            mensajes += resultado.Decodificaciones.Count;
            caminos += resultado.CaminosDeFano;
            sinGramatica += resultado.RechazadasPorLaGramatica;
            sinCoincidencia += resultado.RechazadasPorLaCoincidencia;
            detalles.AddRange(resultado.Detalles);
        }
        return (mensajes, caminos, sinGramatica, sinCoincidencia, ventanas == 0 ? 0 : coste / ventanas, detalles);
    }

    /// <summary>Fabrica una ventana de dos minutos con un mensaje a la relacion pedida.</summary>
    /// <param name="texto">Mensaje.</param>
    /// <param name="frecuenciaHz">Centro de los tonos.</param>
    /// <param name="desfaseSegundos">Adelanto o retraso respecto al segundo 1.</param>
    /// <param name="derivaHz">Deriva de frecuencia a lo largo de la transmision.</param>
    /// <param name="decibelios">Relacion senal-ruido referida a 2500 Hz.</param>
    /// <param name="azar">Generador para el ruido.</param>
    public static float[] Ventana(string texto, double frecuenciaHz, double desfaseSegundos, double derivaHz, double decibelios, Random azar)
    {
        ArgumentNullException.ThrowIfNull(azar);
        if (!MensajeWspr.TryAnalizar(texto, out var mensaje, out var motivo)) throw new InvalidOperationException(motivo);
        var tonos = CodificadorWspr.Tonos(mensaje);
        const int Fs = ParametrosWspr.FrecuenciaDeAnalisis;
        var senal = ModuladorWspr.Sintetizar(tonos, frecuenciaHz, Fs, amplitud: 0.35, derivaHz);

        var ventana = new float[(int)(ParametrosWspr.PeriodoSegundos * Fs)];
        var comienzo = (int)Math.Round((ParametrosWspr.ComienzoNominalSegundos + desfaseSegundos) * Fs);
        for (var i = 0; i < senal.Length; i++)
        {
            var j = comienzo + i;
            if (j >= 0 && j < ventana.Length) ventana[j] += senal[i];
        }
        GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(senal), decibelios, Fs, azar);
        return ventana;
    }

    /// <summary>Escribe las cifras en una tabla legible.</summary>
    /// <param name="titulo">Encabezado.</param>
    /// <param name="franjas">Cifras.</param>
    public static string Tabla(string titulo, IReadOnlyList<FranjaWspr> franjas)
    {
        ArgumentNullException.ThrowIfNull(franjas);
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.Append("### ").AppendLine(titulo).AppendLine();
        sb.AppendLine("| S/R (dB) | Ventanas | Recuperados | % | Falsos | Caminos de Fano | Sin gramática | Sin coincidencia | Coincidencia mínima | Error del informe (dB) | ms de CPU/ventana |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var f in franjas)
            sb.AppendLine(string.Format(c,
                "| {0:0} | {1} | {2} | {3:0.0} | {4} | {5} | {6} | {7} | {8:0.00} | {9:+0.0;-0.0;0,0} | {10:0} |",
                f.Decibelios, f.Intentos, f.Aciertos, f.Porcentaje, f.Falsos, f.CaminosDeFano,
                f.RechazadosPorLaGramatica, f.RechazadosPorLaCoincidencia, f.CoincidenciaMinima,
                f.ErrorDelInforme, f.MilisegundosPorVentana));
        sb.AppendLine();
        return sb.ToString();
    }
}
