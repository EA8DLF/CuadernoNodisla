using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Pasa el banco de medida y deja las cifras escritas en el repositorio.
/// </summary>
/// <remarks>
/// <para>
/// Esta prueba no solo comprueba, tambien <b>publica</b>: reescribe el fichero de resultados
/// para que quede en el control de versiones. Asi, cuando alguien toque el decodificador, el
/// propio cambio del fichero ensena si mejoro o empeoro y en que franjas, sin tener que fiarse
/// de la memoria de nadie.
/// </para>
/// <para>
/// Las condiciones que se comprueban son deliberadamente flojas. Lo que se quiere cazar es una
/// regresion gorda —que alguien rompa el sincronismo y el modem deje de sacar nada— no afinar
/// decimas. Las decimas se miran en la tabla.
/// </para>
/// </remarks>
public class BancoDeMedidaPruebas(ITestOutputHelper salida)
{
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.DePruebas();

    /// <summary>Como se compilo lo que se esta midiendo, que cambia los tiempos por cinco.</summary>
#if DEBUG
    private const string Configuracion = "de depuración (Debug)";
#else
    private const string Configuracion = "de publicación (Release)";
#endif

    [Fact]
    public void ElBancoDeMedidaSePasaYSeApunta()
    {
        var ft8 = BancoDeMedida.Recorrer(Tablas, ModoDelModem.Ft8, desde: 0, hasta: -24, paso: -3, ventanasPorFranja: 12);
        var ft4 = BancoDeMedida.Recorrer(Tablas, ModoDelModem.Ft4, desde: 0, hasta: -21, paso: -3, ventanasPorFranja: 12);

        var informe = Componer(ft8, ft4);
        salida.WriteLine(informe);
        Escribir(informe);

        // Ninguna franja puede inventar un mensaje. Es la condicion que no se negocia.
        ft8.Sum(f => f.Falsos).Should().Be(0, "un mensaje falso mete un contacto que nunca existió");
        ft4.Sum(f => f.Falsos).Should().Be(0, "un mensaje falso mete un contacto que nunca existió");

        // Con senal holgada tiene que salir practicamente todo.
        ft8.First(f => Math.Abs(f.Decibelios) < 0.1).Porcentaje.Should().BeGreaterThan(95);
        ft4.First(f => Math.Abs(f.Decibelios) < 0.1).Porcentaje.Should().BeGreaterThan(95);

        // Y por debajo del ruido tiene que seguir sacando cosas, que es la gracia del modo.
        ft8.First(f => Math.Abs(f.Decibelios + 12) < 0.1).Porcentaje.Should().BeGreaterThan(70,
            "a −12 dB un decodificador sano recupera casi todo");
    }

    private static string Componer(IReadOnlyList<FranjaDelBanco> ft8, IReadOnlyList<FranjaDelBanco> ft4)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Banco de medida del módem propio");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.GetCultureInfo("es-ES"),
            $"Generado por `BancoDeMedidaPruebas` el {DateTime.UtcNow:yyyy-MM-dd} (UTC). **No editar a mano.**");
        sb.AppendLine();
        sb.AppendLine("Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz,");
        sb.AppendLine("igual que los informes de FT8. Cada ventana lleva un mensaje al azar entre ocho, en una");
        sb.AppendLine("frecuencia al azar entre 500 y 2500 Hz y con un desfase al azar de ±0,3 s. La semilla es");
        sb.AppendLine("fija, así que la medida se repite igual en cualquier máquina.");
        sb.AppendLine();
        sb.AppendLine("> **Aviso importante.** Estas cifras se han obtenido con el **código corrector de");
        sb.AppendLine("> pruebas**, no con el de FT8 de verdad: la tabla del LDPC(174,91) todavía no está en el");
        sb.AppendLine("> repositorio. El código de pruebas tiene las mismas dimensiones y una densidad parecida,");
        sb.AppendLine("> así que la curva es representativa de toda la cadena —modulación, sincronismo,");
        sb.AppendLine("> demodulación y corrección— pero **no es la sensibilidad final**. Al poner la tabla");
        sb.AppendLine("> real hay que volver a pasar el banco y sustituir esta tabla.");
        sb.AppendLine();
        sb.AppendLine("La columna que manda es **Falsos**: tiene que ser cero en todas las franjas. Un mensaje");
        sb.AppendLine("falso mete en el cuaderno un contacto que nunca existió y contamina los diplomas para");
        sb.AppendLine("siempre; perder decodificaciones no deja rastro.");
        sb.AppendLine();
        sb.AppendLine("La columna **ms/ventana** solo vale si se dice cómo se compiló, porque entre una");
        sb.AppendLine("compilación de depuración y una de publicación hay un factor de cinco. Estas cifras son");
        sb.AppendLine(CultureInfo.GetCultureInfo("es-ES"),
            $"de una compilación **{Configuracion}**. Para hacerse una idea: FT8 da 15.000 ms por ventana y");
        sb.AppendLine("FT4 da 7.500, así que aquí se está usando en torno al 3 % del hueco disponible.");
        sb.AppendLine();
        sb.Append(BancoDeMedida.Tabla("FT8", ft8));
        sb.Append(BancoDeMedida.Tabla("FT4", ft4));
        return sb.ToString();
    }

    /// <summary>Deja el informe junto a las pruebas, para que entre en el control de versiones.</summary>
    private static void Escribir(string informe)
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Nodisla.Cuaderno.Modos.Pruebas.csproj")))
            carpeta = carpeta.Parent;
        if (carpeta is null) return;

        var destino = Path.Combine(carpeta.FullName, "Banco", "resultados-banco.md");
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllText(destino, informe, new UTF8Encoding(false));
    }
}
