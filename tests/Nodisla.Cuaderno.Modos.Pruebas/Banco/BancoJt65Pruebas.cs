using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Modos.Jt65;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Pasa el banco de medida de JT65 y deja las cifras escritas en el repositorio.
/// </summary>
/// <remarks>
/// Igual que el banco de FT8: la prueba comprueba poco (que no salga ni un falso y que con
/// senal holgada salga todo) y sobre todo <b>publica</b> la curva en <c>resultados-jt65.md</c>.
/// El informe solo se escribe desde una compilacion de publicacion y con la tanda completa
/// (50 ventanas por franja, variable <c>NODISLA_BANCO_VENTANAS=50</c>); la serie normal de
/// pruebas pasa una tanda corta que no toca el fichero.
/// </remarks>
public class BancoJt65Pruebas(ITestOutputHelper salida)
{
#if DEBUG
    private const string Configuracion = "de depuración (Debug)";
    private const int VentanasPorOmision = 2;
    private const int VentanasDeRuidoPorOmision = 4;
#else
    private const string Configuracion = "de publicación (Release)";
    private const int VentanasPorOmision = 8;
    private const int VentanasDeRuidoPorOmision = 20;
#endif

    /// <summary>Ventanas por franja a partir de las cuales el informe se considera completo y se escribe.</summary>
    private const int VentanasParaPublicar = 50;

    [Fact]
    public void ElBancoDeJt65SePasaYSeApunta()
    {
        var ventanas = BancoJt65.VentanasPorFranja(VentanasPorOmision);
        var ruido = ventanas >= VentanasParaPublicar ? 300 : VentanasDeRuidoPorOmision;
        var decodificador = new DecodificadorJt65();

        var franjas = BancoJt65.Recorrer(decodificador, desde: -16, hasta: -30, paso: -1, ventanasPorFranja: ventanas,
            alAcabarFranja: f => salida.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{f.Decibelios,4:0} dB: {f.Aciertos}/{f.Intentos} falsos {f.Falsos} rechazadas {f.Rechazadas} {f.MilisegundosPorVentana:0} ms")));
        var tanda = BancoJt65.RuidoPuro(decodificador, ruido);

        var informe = Componer(franjas, tanda, ventanas);
        salida.WriteLine(informe);

#if !DEBUG
        if (ventanas >= VentanasParaPublicar) BancoJt65.Escribir("resultados-jt65.md", informe);
#endif

        franjas.Sum(f => f.Falsos).Should().Be(0, "un mensaje falso mete un contacto que nunca existió");
        tanda.Falsos.Should().Be(0, "con ruido puro no hay nada que decodificar");
        franjas.First(f => Math.Abs(f.Decibelios + 16) < 0.1).Porcentaje.Should().Be(100, "a −16 dB JT65 tiene que sacar todo");
        franjas.First(f => Math.Abs(f.Decibelios + 22) < 0.1).Porcentaje.Should().BeGreaterThan(90, "−22 dB es terreno normal de JT65");
    }

    private static string Componer(IReadOnlyList<FranjaDeModoLento> franjas, TandaDeRuidoDeModoLento tanda, int ventanas)
    {
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.AppendLine("# Banco de medida de JT65");
        sb.AppendLine();
        sb.AppendLine(c, $"Generado por `BancoJt65Pruebas` el {DateTime.UtcNow:yyyy-MM-dd} (UTC). **No editar a mano.**");
        sb.AppendLine();
        sb.AppendLine("Señal sintética de JT65A con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz.");
        sb.AppendLine("Cada ventana lleva un mensaje al azar entre ocho, con el tono de sincronismo al azar entre 500");
        sb.AppendLine("y 2500 Hz y un desfase al azar de ±0,5 s respecto al segundo 1. La semilla es fija, así que la");
        sb.AppendLine("medida se repite igual en cualquier máquina.");
        sb.AppendLine();
        sb.AppendLine("El decodificador es el propio: sincronismo por correlación del vector pseudoaleatorio, demodulación");
        sb.AppendLine("no coherente de los 64 tonos por símbolo y **Reed-Solomon (63,12) con decisión blanda** —borrados");
        sb.AppendLine("probabilísticos y Berlekamp-Massey repetido, siguiendo la idea de Franke y Taylor (QEX, 2016) con");
        sb.AppendLine("implementación, reparto de borrados y métrica propios—. No hay CRC en JT65: lo que impide inventar");
        sb.AppendLine("un mensaje es el criterio de aceptación del decodificador blando (distancia blanda máxima y");
        sb.AppendLine("correcciones máximas), medido con ruido puro.");
        sb.AppendLine();
        sb.AppendLine("La columna **Palabras rechazadas** cuenta las palabras de código que salieron de algún intento y no");
        sb.AppendLine("pasaron el criterio: cada una era un mensaje falso en potencia. La columna que manda es **Falsos**:");
        sb.AppendLine("tiene que ser cero en todas las franjas y en la tanda de ruido puro.");
        sb.AppendLine();
        sb.AppendLine("**ms de CPU/ventana** es tiempo de procesador, no de reloj; la ventana de JT65 dura 60.000 ms y el");
        sb.AppendLine(c, $"decodificador dispone de unos 12.000 tras el final de la señal. Compilación **{Configuracion}**,");
        sb.AppendLine(c, $"{ventanas} ventanas por franja.");
        sb.AppendLine();
        sb.Append(BancoJt65.Tabla("JT65A, de −16 a −30 dB", franjas));
        sb.Append(BancoJt65.Tabla("Ruido puro, sin señal", tanda));
        return sb.ToString();
    }
}
