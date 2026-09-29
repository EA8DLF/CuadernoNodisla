using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Modos.Jt9;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Pasa el banco de medida de JT9 y deja las cifras escritas en el repositorio.
/// </summary>
/// <remarks>
/// Igual que el banco de FT8: la prueba comprueba poco (que no salga ni un falso y que con
/// senal holgada salga todo) y sobre todo <b>publica</b> la curva en <c>resultados-jt9.md</c>.
/// El informe solo se escribe desde una compilacion de publicacion y con la tanda completa
/// (50 ventanas por franja, variable <c>NODISLA_BANCO_VENTANAS=50</c>); la serie normal de
/// pruebas pasa una tanda corta que no toca el fichero.
/// </remarks>
public class BancoJt9Pruebas(ITestOutputHelper salida)
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
    public void ElBancoDeJt9SePasaYSeApunta()
    {
        var ventanas = BancoJt9.VentanasPorFranja(VentanasPorOmision);
        var ruido = ventanas >= VentanasParaPublicar ? 300 : VentanasDeRuidoPorOmision;
        var decodificador = new DecodificadorJt9();

        var franjas = BancoJt9.Recorrer(decodificador, desde: -16, hasta: -30, paso: -1, ventanasPorFranja: ventanas,
            alAcabarFranja: f => salida.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{f.Decibelios,4:0} dB: {f.Aciertos}/{f.Intentos} falsos {f.Falsos} rechazadas {f.Rechazadas} {f.MilisegundosPorVentana:0} ms")));
        var tanda = BancoJt9.RuidoPuro(decodificador, ruido);

        var informe = Componer(franjas, tanda, ventanas);
        salida.WriteLine(informe);

#if !DEBUG
        if (ventanas >= VentanasParaPublicar) BancoJt9.Escribir("resultados-jt9.md", informe);
#endif

        franjas.Sum(f => f.Falsos).Should().Be(0, "un mensaje falso mete un contacto que nunca existió");
        tanda.Falsos.Should().Be(0, "con ruido puro no hay nada que decodificar");
        franjas.First(f => Math.Abs(f.Decibelios + 16) < 0.1).Porcentaje.Should().Be(100, "a −16 dB JT9 tiene que sacar todo");
        franjas.First(f => Math.Abs(f.Decibelios + 22) < 0.1).Porcentaje.Should().BeGreaterThan(90, "−22 dB es terreno normal de JT9");
    }

    private static string Componer(IReadOnlyList<FranjaDeModoLento> franjas, TandaDeRuidoDeModoLento tanda, int ventanas)
    {
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.AppendLine("# Banco de medida de JT9");
        sb.AppendLine();
        sb.AppendLine(c, $"Generado por `BancoJt9Pruebas` el {DateTime.UtcNow:yyyy-MM-dd} (UTC). **No editar a mano.**");
        sb.AppendLine();
        sb.AppendLine("Señal sintética de JT9 con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz.");
        sb.AppendLine("Cada ventana lleva un mensaje al azar entre ocho, con el tono de sincronismo al azar entre 500");
        sb.AppendLine("y 2500 Hz y un desfase al azar de ±0,5 s respecto al segundo 1. La semilla es fija, así que la");
        sb.AppendLine("medida se repite igual en cualquier máquina.");
        sb.AppendLine();
        sb.AppendLine("El decodificador es el propio: sincronismo por los 16 símbolos del tono 0, demodulación no coherente");
        sb.AppendLine("de los 9 tonos con valores blandos por bit (razón de verosimilitudes de la FSK no coherente),");
        sb.AppendLine("desentrelazado y **decodificación secuencial de Fano** del código convolucional K=32 r=1/2, que se");
        sb.AppendLine("comparte con WSPR en `Convolucional/`. No hay CRC en JT9: lo que impide inventar un mensaje es que");
        sb.AppendLine("Fano llegue al final con la cola de 31 ceros casando, que los 72 bits tengan gramática y que el");
        sb.AppendLine("mensaje recodificado se parezca a lo recibido (coincidencia mínima), medido con ruido puro.");
        sb.AppendLine();
        sb.AppendLine("La columna **Caminos rechazados** cuenta los caminos completos de Fano que se tiraron por no tener");
        sb.AppendLine("gramática o por no parecerse a lo recibido: cada uno era un mensaje falso en potencia. La columna que");
        sb.AppendLine("manda es **Falsos**: tiene que ser cero en todas las franjas y en la tanda de ruido puro.");
        sb.AppendLine();
        sb.AppendLine("**ms de CPU/ventana** es tiempo de procesador, no de reloj; la ventana de JT9 dura 60.000 ms y el");
        sb.AppendLine(c, $"decodificador dispone de unos 10.000 tras el final de la señal. Compilación **{Configuracion}**,");
        sb.AppendLine(c, $"{ventanas} ventanas por franja.");
        sb.AppendLine();
        sb.Append(BancoJt9.Tabla("JT9, de −16 a −30 dB", franjas));
        sb.Append(BancoJt9.Tabla("Ruido puro, sin señal", tanda));
        return sb.ToString();
    }
}
