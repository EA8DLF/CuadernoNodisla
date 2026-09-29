using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Modos.Q65;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Pasa el banco de Q65 y deja las cifras escritas en <c>resultados-q65.md</c>.
/// </summary>
/// <remarks>
/// Como el banco de FT8: en depuracion se pasa con pocas ventanas y solo se comprueba lo que no
/// se negocia (cero falsos); la curva fina y el informe se sacan en publicacion.
/// </remarks>
public class BancoDeQ65Pruebas(ITestOutputHelper salida)
{
    private static readonly TablasDeQ65 Tablas = TablasDeQ65DePruebas.Reales();

#if DEBUG
    private const string Configuracion = "de depuración (Debug)";
    private const int VentanasPorFranja = 2;
    private const int VentanasDeRuido = 3;
    private const int Tandas = 2;
#else
    private const string Configuracion = "de publicación (Release)";
    private const int VentanasPorFranja = 10;
    private const int VentanasDeRuido = 40;
    private const int Tandas = 6;
#endif

    private static ModoQ65 Modo(int periodo, SubmodoDeQ65 submodo = SubmodoDeQ65.A) =>
        new(ParametrosDeQ65.De(periodo, submodo), Tablas);

    [Fact]
    public void ElBancoDeQ65SePasaYSeApunta()
    {
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.AppendLine("# Banco de medida de Q65");
        sb.AppendLine();
        sb.AppendLine(c, $"Generado por `BancoDeQ65Pruebas` el {DateTime.UtcNow:yyyy-MM-dd} (UTC), compilación **{Configuracion}**. **No editar a mano.**");
        sb.AppendLine();
        sb.AppendLine("Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz. Cada");
        sb.AppendLine("ventana lleva un mensaje al azar entre ocho, en un tono base al azar entre 500 y 2500 Hz y");
        sb.AppendLine("con un desfase al azar de ±0,3 s. Semilla fija. Sin promediado (cada ventana es independiente).");
        sb.AppendLine("El código es el **QRA(65,15) de verdad** del protocolo; la tabla y su procedencia están en");
        sb.AppendLine("`src/Nodisla.Cuaderno.Modos/Tablas/tablas-q65.txt` y `LEEME-q65.md`.");
        sb.AppendLine();
        sb.AppendLine("**Con AP** es con el indicativo propio (EA8DLF) y el del corresponsal (K1ABC) dados al modo:");
        sb.AppendLine("se prueba además suponiendo que el mensaje empieza por CQ, por EA8DLF, o por EA8DLF K1ABC.");
        sb.AppendLine("La columna **Con AP** cuenta cuántos de los recuperados necesitaron esa ayuda. **Rechazos por");
        sb.AppendLine("verosimilitud** son palabras con sello bueno que el freno anti-falsos tiró por no parecerse a lo");
        sb.AppendLine("oído; con señal de verdad tiene que ser cero o casi. La columna que manda es **Falsos**.");
        sb.AppendLine();
        sb.AppendLine("Los **ms de CPU/ventana** son tiempo de procesador del proceso, no de reloj. Para situarlos: la");
        sb.AppendLine("ventana de Q65-30A dura 30.000 ms, la de 60A 60.000 y la de 120A 120.000.");
        sb.AppendLine();

        var todas = new List<(string Nombre, List<FranjaDeQ65> Franjas)>();
        foreach (var (periodo, desde, hasta) in new[] { (30, -18.0, -30.0), (60, -18.0, -34.0), (120, -24.0, -36.0) })
        {
            foreach (var conAp in new[] { false, true })
            {
                var modo = Modo(periodo);
                var franjas = BancoDeQ65.Recorrer(modo, conAp, desde, hasta, -2, VentanasPorFranja);
                var nombre = $"{modo.Parametros.Nombre} {(conAp ? "con AP" : "sin AP")}";
                todas.Add((nombre, franjas));
                sb.Append(BancoDeQ65.Tabla(nombre, franjas));
            }
        }

        sb.AppendLine("### Ruido puro");
        sb.AppendLine();
        sb.AppendLine("Ventanas sin señal, con el umbral de sincronismo bajado a 2,5 desviaciones para obligar al");
        sb.AppendLine("corrector a pelear con el ruido en muchas candidatas. **Palabras cerradas** son las que el");
        sb.AppendLine("código dio por válidas sobre ruido; el sello y la verosimilitud tienen que tirarlas todas.");
        sb.AppendLine("**Verosimilitud máxima** es la más alta que alcanzó una palabra rechazada: el umbral (10) tiene");
        sb.AppendLine("que quedar claramente por encima.");
        sb.AppendLine();
        sb.AppendLine("| Modo | AP | Ventanas | Falsos | Palabras cerradas | Rechazos del CRC | Rechazos por verosimilitud | Verosimilitud máxima |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        var ruidos = new List<TandaDeRuidoDeQ65>();
        foreach (var periodo in new[] { 30, 60 })
        {
            foreach (var conAp in new[] { false, true })
            {
                var modo = Modo(periodo);
                var tanda = BancoDeQ65.Ruido(modo, conAp, VentanasDeRuido);
                ruidos.Add(tanda);
                sb.AppendLine(string.Format(c, "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |",
                    modo.Parametros.Nombre, conAp ? "sí" : "no", tanda.Ventanas, tanda.Falsos, tanda.PalabrasCerradas,
                    tanda.RechazadasPorElCrc, tanda.RechazadasPorVerosimilitud,
                    double.IsNegativeInfinity(tanda.VerosimilitudMaxima) ? "—" : tanda.VerosimilitudMaxima.ToString("0.0", c)));
            }
        }
        sb.AppendLine();

        sb.AppendLine("### Promediado de periodos (Q65-60A)");
        sb.AppendLine();
        sb.AppendLine("El mismo mensaje repetido periodo tras periodo a una relación en la que un periodo solo no");
        sb.AppendLine("suele llegar. Se cuenta en cuántas tandas acabó saliendo y cuántos periodos hicieron falta.");
        sb.AppendLine();
        sb.AppendLine("| S/R (dB) | Tandas | Salieron | Periodos medios | Falsos |");
        sb.AppendLine("|---:|---:|---:|---:|---:|");
        var promediados = new List<(int Tandas, int Salieron, double PeriodosMedios, int Falsos)>();
        foreach (var db in new[] { -30.0, -32.0 })
        {
            var r = BancoDeQ65.Promediado(Modo(60), db, Tandas, periodosPorTanda: 6);
            promediados.Add(r);
            sb.AppendLine(string.Format(c, "| {0:0} | {1} | {2} | {3:0.0} | {4} |", db, r.Tandas, r.Salieron, r.PeriodosMedios, r.Falsos));
        }
        sb.AppendLine();

        var informe = sb.ToString();
        salida.WriteLine(informe);
#if !DEBUG
        Escribir(informe);
#endif

        foreach (var (nombre, franjas) in todas)
        {
            franjas.Sum(f => f.Falsos).Should().Be(0, $"{nombre}: un mensaje falso mete un contacto que nunca existió");
            franjas.First().Porcentaje.Should().BeGreaterThan(95, $"{nombre}: con señal holgada tiene que salir todo");
        }
        ruidos.Sum(r => r.Falsos).Should().Be(0, "con ruido puro no puede salir nada, ni con AP");
        promediados.Sum(r => r.Falsos).Should().Be(0);

        // Sensibilidad de referencia: Q65-60A a -28 dB tiene que sacar la mayoria.
        var sesentaSinAp = todas.First(t => t.Nombre == "Q65-60A sin AP").Franjas;
        sesentaSinAp.First(f => Math.Abs(f.Decibelios + 26) < 0.1).Porcentaje.Should().BeGreaterThan(50);
    }

    /// <summary>Deja el informe junto a este fichero fuente, para que entre en el control de versiones.</summary>
    private static void Escribir(string informe, [System.Runtime.CompilerServices.CallerFilePath] string esteFichero = "")
    {
        var destino = Path.Combine(Path.GetDirectoryName(esteFichero)!, "resultados-q65.md");
        File.WriteAllText(destino, informe, new UTF8Encoding(false));
    }
}
