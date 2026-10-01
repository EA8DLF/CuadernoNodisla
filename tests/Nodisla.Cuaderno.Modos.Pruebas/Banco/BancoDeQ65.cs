using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Modos.Q65;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>Como fue una franja de relacion senal-ruido en Q65.</summary>
/// <param name="Decibelios">Relacion senal-ruido de las senales de la franja, referida a 2500 Hz.</param>
/// <param name="Intentos">Ventanas probadas.</param>
/// <param name="Aciertos">Ventanas en que salio el mensaje emitido.</param>
/// <param name="Falsos">Mensajes que salieron y no eran el emitido. <b>Tiene que ser cero.</b></param>
/// <param name="ConAp">De los aciertos, cuantos necesitaron la informacion a priori.</param>
/// <param name="RechazadosPorElCrc">Palabras que el codigo cerro y el sello tiro.</param>
/// <param name="RechazadosPorVerosimilitud">Palabras con sello bueno que no se parecian a lo oido.</param>
/// <param name="ErrorDelInforme">Diferencia media entre el informe medido y el de verdad.</param>
/// <param name="MilisegundosPorVentana">Milisegundos de procesador por ventana.</param>
public sealed record FranjaDeQ65(
    double Decibelios,
    int Intentos,
    int Aciertos,
    int Falsos,
    int ConAp,
    int RechazadosPorElCrc,
    int RechazadosPorVerosimilitud,
    double ErrorDelInforme,
    double MilisegundosPorVentana)
{
    /// <summary>Porcentaje de mensajes recuperados.</summary>
    public double Porcentaje => Intentos == 0 ? 0 : 100.0 * Aciertos / Intentos;
}

/// <summary>Como fue una tanda de ventanas de ruido puro.</summary>
/// <param name="Ventanas">Ventanas probadas.</param>
/// <param name="Falsos">Mensajes que salieron. <b>Tiene que ser cero.</b></param>
/// <param name="PalabrasCerradas">Palabras que el codigo dio por validas sobre ruido.</param>
/// <param name="RechazadasPorElCrc">De esas, las que tiro el sello.</param>
/// <param name="RechazadasPorVerosimilitud">Y las que pasaron el sello y tiro la verosimilitud.</param>
/// <param name="VerosimilitudMaxima">La verosimilitud mas alta que alcanzo una palabra rechazada.</param>
public sealed record TandaDeRuidoDeQ65(
    int Ventanas,
    int Falsos,
    int PalabrasCerradas,
    int RechazadasPorElCrc,
    int RechazadasPorVerosimilitud,
    double VerosimilitudMaxima);

/// <summary>
/// Mide la sensibilidad de Q65 por franjas, con y sin informacion a priori, y cuenta los falsos.
/// </summary>
/// <remarks>
/// Mismo procedimiento que el banco de FT8: senal sintetica, ruido blanco gaussiano, relacion
/// senal-ruido referida a 2500 Hz, semilla fija. Aqui se anaden dos cosas que en Q65 importan
/// mas: una tanda de ruido puro con la informacion a priori puesta, que es donde este modo
/// inventa mensajes si se le deja, y una medida del promediado de periodos.
/// </remarks>
public static class BancoDeQ65
{
    private static readonly string[] Mensajes =
    [
        "CQ EA8DLF IL18",
        "EA8DLF K1ABC FN42",
        "K1ABC EA8DLF -07",
        "EA8DLF K1ABC R-15",
        "K1ABC EA8DLF RR73",
        "CQ K1ABC FN42",
        "EA8DLF K1ABC 73",
        "JA1XYZ EA8DLF 73",
    ];

    /// <summary>Recorre las franjas y devuelve las cifras.</summary>
    /// <param name="modo">El submodo y periodo a medir. Se le quita el promediado.</param>
    /// <param name="conAp">Si se le da al modo el indicativo propio y el del corresponsal.</param>
    /// <param name="desde">Relacion mas alta, en decibelios.</param>
    /// <param name="hasta">Relacion mas baja.</param>
    /// <param name="paso">Cuanto se baja en cada franja (negativo).</param>
    /// <param name="ventanasPorFranja">Ventanas por franja.</param>
    /// <param name="semilla">Semilla, para repetir la medida igual.</param>
    public static List<FranjaDeQ65> Recorrer(
        ModoQ65 modo,
        bool conAp,
        double desde,
        double hasta,
        double paso,
        int ventanasPorFranja,
        int semilla = 20260926)
    {
        ArgumentNullException.ThrowIfNull(modo);
        modo.UsarPromediado = false;
        modo.MiIndicativo = conAp ? "EA8DLF" : null;
        modo.IndicativoDx = conAp ? "K1ABC" : null;
        var p = modo.Parametros;
        const int Frecuencia = ParametrosDeQ65.FrecuenciaDeAnalisis;

        var franjas = new List<FranjaDeQ65>();
        for (var db = desde; db >= hasta - 1e-9; db += paso)
        {
            int aciertos = 0, falsos = 0, ap = 0, crc = 0, verosimilitud = 0;
            double errores = 0, coste = 0;
            for (var v = 0; v < ventanasPorFranja; v++)
            {
                var azar = new Random(semilla + (int)(db * 1000) + (v * 7919) + p.PeriodoSegundos);
                var texto = Mensajes[azar.Next(Mensajes.Length)];
                var tono = 500 + (azar.NextDouble() * (Math.Min(2500, p.FrecuenciaMaximaDelTonoBaseHz) - 500));
                var desfase = (azar.NextDouble() - 0.5) * 0.6;
                if (!modo.TryTonos(texto, out var tonos, out var motivo)) throw new InvalidOperationException(motivo);

                var ventana = GeneradorDeSenalDeQ65.Ventana(p, tonos, tono, desfase, db, Frecuencia, azar);
                IReadOnlyList<Aplicacion.Puertos.DecodificacionPropia> salida = [];
                coste += Medidas.TiempoDeProceso.De(() => salida = modo.Decodificar(ventana, DateTimeOffset.UnixEpoch, CancellationToken.None));

                var cuentas = modo.UltimasCuentas;
                crc += cuentas.RechazadasPorElCrc;
                verosimilitud += cuentas.RechazadasPorVerosimilitud;
                var acertada = salida.FirstOrDefault(d => d.Texto == texto);
                if (acertada is not null)
                {
                    aciertos++;
                    ap += cuentas.ConAp;
                    errores += acertada.Decibelios - db;
                }
                falsos += salida.Count(d => d.Texto != texto);
            }
            franjas.Add(new FranjaDeQ65(db, ventanasPorFranja, aciertos, falsos, ap, crc, verosimilitud,
                aciertos == 0 ? 0 : errores / aciertos, coste / ventanasPorFranja));
        }
        return franjas;
    }

    /// <summary>Pasa ventanas de ruido puro y cuenta lo que sale, que tiene que ser nada.</summary>
    public static TandaDeRuidoDeQ65 Ruido(ModoQ65 modo, bool conAp, int ventanas, int semilla = 2026)
    {
        ArgumentNullException.ThrowIfNull(modo);
        modo.UsarPromediado = false;
        modo.MiIndicativo = conAp ? "EA8DLF" : null;
        modo.IndicativoDx = conAp ? "K1ABC" : null;
        // Con ruido puro el sincronismo no manda, asi que se fuerza a probar candidatas para
        // que el corrector y la informacion a priori se enfrenten de verdad al ruido.
        var umbral = modo.UmbralDeSincronismo;
        modo.UmbralDeSincronismo = 2.5;
        try
        {
            int falsos = 0, cerradas = 0, crc = 0, verosimilitud = 0;
            var maxima = double.NegativeInfinity;
            for (var v = 0; v < ventanas; v++)
            {
                var ventana = GeneradorDeSenalDeQ65.SoloRuido(modo.Parametros, ParametrosDeQ65.FrecuenciaDeAnalisis, new Random(semilla + v));
                var salida = modo.Decodificar(ventana, DateTimeOffset.UnixEpoch, CancellationToken.None);
                falsos += salida.Count;
                var c = modo.UltimasCuentas;
                cerradas += c.PalabrasValidas;
                crc += c.RechazadasPorElCrc;
                verosimilitud += c.RechazadasPorVerosimilitud;
                if (c.RechazadasPorVerosimilitud > 0) maxima = Math.Max(maxima, c.VerosimilitudMaximaRechazada);
            }
            return new TandaDeRuidoDeQ65(ventanas, falsos, cerradas, crc, verosimilitud, maxima);
        }
        finally
        {
            modo.UmbralDeSincronismo = umbral;
        }
    }

    /// <summary>
    /// Mide el promediado: el mismo mensaje repetido en periodos seguidos a una relacion en la
    /// que un periodo solo no llega. Devuelve en cuantas tandas salio y cuantos periodos hicieron falta.
    /// </summary>
    public static (int Tandas, int Salieron, double PeriodosMedios, int Falsos) Promediado(
        ModoQ65 modo, double decibelios, int tandas, int periodosPorTanda, int semilla = 4242)
    {
        ArgumentNullException.ThrowIfNull(modo);
        modo.UsarPromediado = true;
        modo.MiIndicativo = null;
        modo.IndicativoDx = null;
        int salieron = 0, falsos = 0, sumaDePeriodos = 0;
        for (var t = 0; t < tandas; t++)
        {
            modo.LimpiarPromedio();
            var azar = new Random(semilla + t);
            var texto = Mensajes[azar.Next(Mensajes.Length)];
            modo.TryTonos(texto, out var tonos, out _);
            var tono = 800 + (azar.NextDouble() * 800);
            for (var periodo = 1; periodo <= periodosPorTanda; periodo++)
            {
                var ventana = GeneradorDeSenalDeQ65.Ventana(modo.Parametros, tonos, tono, 0.1, decibelios, ParametrosDeQ65.FrecuenciaDeAnalisis, azar);
                var salida = modo.Decodificar(ventana, DateTimeOffset.UnixEpoch.AddSeconds(periodo * modo.Parametros.PeriodoSegundos), CancellationToken.None);
                falsos += salida.Count(d => d.Texto != texto);
                if (salida.Any(d => d.Texto == texto))
                {
                    salieron++;
                    sumaDePeriodos += periodo;
                    break;
                }
            }
        }
        return (tandas, salieron, salieron == 0 ? 0 : (double)sumaDePeriodos / salieron, falsos);
    }

    /// <summary>Escribe las cifras de un barrido en una tabla legible.</summary>
    public static string Tabla(string titulo, IReadOnlyList<FranjaDeQ65> franjas)
    {
        ArgumentNullException.ThrowIfNull(franjas);
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.Append("### ").AppendLine(titulo).AppendLine();
        sb.AppendLine("| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var f in franjas)
            sb.AppendLine(string.Format(c,
                "| {0:0} | {1} | {2} | {3:0.0} | {4} | {5} | {6} | {7} | {8:+0.0;-0.0;0,0} | {9:0} |",
                f.Decibelios, f.Intentos, f.Aciertos, f.Porcentaje, f.Falsos, f.ConAp, f.RechazadosPorElCrc,
                f.RechazadosPorVerosimilitud, f.ErrorDelInforme, f.MilisegundosPorVentana));
        sb.AppendLine();
        return sb.ToString();
    }
}
