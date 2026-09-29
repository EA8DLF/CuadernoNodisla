using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Modos.Wspr;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Pasa el banco de medida de WSPR y deja las cifras escritas en el repositorio.
/// </summary>
/// <remarks>
/// Igual que el de FT8: en publicacion reescribe <c>resultados-wspr.md</c>; en depuracion solo
/// comprueba lo que no se negocia, con pocas ventanas, para no lastrar la serie de pruebas.
/// </remarks>
public class BancoWsprPruebas(ITestOutputHelper salida)
{
#if DEBUG
    private const string Configuracion = "de depuración (Debug)";
    private const int VentanasPorFranja = 3;
    private const int VentanasDeRuido = 10;
#else
    private const string Configuracion = "de publicación (Release)";
    private const int VentanasPorFranja = 50;
    private const int VentanasDeRuido = 200;
#endif

    [Fact]
    public void ElBancoDeWsprSePasaYSeApunta()
    {
        var decodificador = new DecodificadorWspr();
        var franjas = BancoWspr.Recorrer(decodificador, desde: -20, hasta: -34, paso: -2, ventanasPorFranja: VentanasPorFranja);
        var ruido = BancoWspr.RuidoPuro(decodificador, VentanasDeRuido);

        var informe = Componer(decodificador, franjas, ruido);
        salida.WriteLine(informe);
#if !DEBUG
        Escribir(informe);
#endif

        franjas.Sum(f => f.Falsos).Should().Be(0, "un WSPR falso es un spot de propagación mentira");
        ruido.Mensajes.Should().Be(0, "del ruido puro no puede salir nada");
        franjas.First(f => Math.Abs(f.Decibelios + 20) < 0.1).Porcentaje.Should().BeGreaterThan(95);
        franjas.First(f => Math.Abs(f.Decibelios + 26) < 0.1).Porcentaje.Should().BeGreaterThan(80,
            "−26 dB es terreno normal de WSPR, no un caso extremo");
    }

    private static string Componer(
        DecodificadorWspr decodificador,
        IReadOnlyList<FranjaWspr> franjas,
        (int Mensajes, int CaminosDeFano, int SinGramatica, int SinCoincidencia, double MilisegundosPorVentana, List<DetalleWspr> Detalles) ruido)
    {
        var c = CultureInfo.GetCultureInfo("es-ES");
        var sb = new StringBuilder();
        sb.AppendLine("# Banco de medida de WSPR");
        sb.AppendLine();
        sb.AppendLine(c, $"Generado por `BancoWsprPruebas` el {DateTime.UtcNow:yyyy-MM-dd} (UTC). **No editar a mano.**");
        sb.AppendLine();
        sb.AppendLine("Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz, como");
        sb.AppendLine("en los informes de WSPR. Cada ventana de dos minutos lleva un mensaje al azar entre ocho");
        sb.AppendLine("(seis de tipo 1, dos de tipo 2), en una frecuencia al azar entre 1400 y 1600 Hz, con un");
        sb.AppendLine("desfase al azar de ±1,5 s y una deriva al azar de ±1 Hz a lo largo de la transmisión. La");
        sb.AppendLine("semilla es fija: la medida se repite igual en cualquier máquina.");
        sb.AppendLine();
        sb.AppendLine("El decodificador es el propio: sincronismo por correlación del vector de 162 en frecuencia,");
        sb.AppendLine("tiempo y deriva; demodulación no coherente de los cuatro tonos sobre una señal estrecha de");
        sb.AppendLine("23,4 muestras por segundo; y **decodificación secuencial de Fano** del código convolucional");
        sb.AppendLine("K=32 (`Convolucional/`), con la cola de 31 ceros forzada. La sensibilidad publicada del");
        sb.AppendLine("modo es de unos −28 dB; aquí se ve dónde queda éste.");
        sb.AppendLine();
        sb.AppendLine("Las columnas que vigilan el riesgo: **Caminos de Fano** son las veces que el decodificador");
        sb.AppendLine("secuencial llegó al final del árbol; **Sin gramática**, cuántos de esos caminos no formaban un");
        sb.AppendLine("mensaje válido (indicativo, localizador, potencia); **Sin coincidencia**, cuántos mensajes");
        sb.AppendLine("válidos se tiraron porque, recodificados, no se parecían a lo recibido. **Coincidencia mínima**");
        sb.AppendLine(c, $"es la más baja de un acierto en la franja; el umbral está en {decodificador.CoincidenciaMinima:0.00}, y el margen");
        sb.AppendLine("entre las dos cifras es lo que separa un decodificador que no inventa de uno que sí.");
        sb.AppendLine();
        sb.AppendLine("La columna que manda es **Falsos**: cero en todas las franjas y cero en el ruido puro. Un WSPR");
        sb.AppendLine("falso mete un spot de propagación que nunca existió.");
        sb.AppendLine();
        sb.AppendLine("La columna **ms de CPU/ventana** es tiempo de procesador, no de reloj, de una compilación");
        sb.AppendLine(c, $"**{Configuracion}**. El hueco disponible son 120.000 ms por ventana.");
        sb.AppendLine();
        sb.Append(BancoWspr.Tabla("WSPR, con deriva de ±1 Hz", franjas));
        sb.AppendLine("### Ruido puro");
        sb.AppendLine();
        sb.AppendLine(c, $"{VentanasDeRuido} ventanas de ruido blanco sin ninguna señal: **{ruido.Mensajes} mensajes**, ");
        sb.AppendLine(c, $"{ruido.CaminosDeFano} caminos completos de Fano, {ruido.SinGramatica} sin gramática, ");
        sb.AppendLine(c, $"{ruido.SinCoincidencia} sin coincidencia, {ruido.MilisegundosPorVentana:0} ms de CPU por ventana.");
        if (ruido.Detalles.Count > 0)
        {
            var peor = ruido.Detalles.Where(d => d.Texto.Length > 0).Select(d => d.Coincidencia).DefaultIfEmpty(0).Max();
            sb.AppendLine(c, $"Coincidencia más alta de un camino con gramática válida sacado del ruido: {peor:0.00}.");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static void Escribir(string informe)
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Nodisla.Cuaderno.Modos.Pruebas.csproj")))
            carpeta = carpeta.Parent;
        if (carpeta is null) return;
        var destino = Path.Combine(carpeta.FullName, "Banco", "resultados-wspr.md");
        File.WriteAllText(destino, informe, new UTF8Encoding(false));
    }
}
