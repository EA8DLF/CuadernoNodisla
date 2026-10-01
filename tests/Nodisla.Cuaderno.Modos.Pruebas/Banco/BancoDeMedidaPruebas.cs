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
    private static readonly TablasDelProtocolo Tablas = TablasDelProtocolo.Cargar();

    // Se carga la tabla de verdad, no la de pruebas: estas comprobaciones tienen que
    // recorrer el mismo camino que recorrerá el módem cuando esté escuchando la banda.

    /// <summary>Como se compilo lo que se esta midiendo, que cambia los tiempos por cinco.</summary>
#if DEBUG
    private const string Configuracion = "de depuración (Debug)";

    /// <summary>
    /// Ventanas por franja en depuracion.
    /// </summary>
    /// <remarks>
    /// En depuracion el banco no publica nada —el informe solo se escribe desde publicacion— y
    /// en cambio se lleva varios minutos de la serie de pruebas, que es la que se pasa veinte
    /// veces al dia mientras se trabaja. Con cuatro ventanas por franja se sigue comprobando lo
    /// unico que no se negocia, que es que no salga ni un mensaje falso, y la serie deja de
    /// estar dominada por esto. La curva fina se saca en publicacion.
    /// </remarks>
    private const int VentanasPorFranja = 4;
#else
    private const string Configuracion = "de publicación (Release)";
    private const int VentanasPorFranja = 12;
#endif

    [Fact]
    public void ElBancoDeMedidaSePasaYSeApunta()
    {
        var ft8 = BancoDeMedida.Recorrer(Tablas, ModoDelModem.Ft8, desde: 0, hasta: -24, paso: -3, ventanasPorFranja: VentanasPorFranja);
        var ft4 = BancoDeMedida.Recorrer(Tablas, ModoDelModem.Ft4, desde: 0, hasta: -21, paso: -3, ventanasPorFranja: VentanasPorFranja);

        // Barrido fino alrededor del filo, de decibelio en decibelio y con mas ventanas. El
        // barrido grueso va de tres en tres, y ahi la diferencia entre «saca todo» y «no saca
        // nada» cabe entera en un solo escalon: sin esto no se puede decir donde esta el limite
        // ni notar si un cambio lo mueve medio decibelio.
        var filo = BancoDeMedida.Recorrer(
            Tablas, ModoDelModem.Ft8, desde: -16, hasta: -23, paso: -1, ventanasPorFranja: VentanasPorFranja * 2);

        var informe = Componer(ft8, ft4, filo);
        salida.WriteLine(informe);

        // El informe que queda en el repositorio se escribe solo desde una compilacion de
        // publicacion. Si no, pasar las pruebas en depuracion —que es lo normal mientras se
        // trabaja— dejaria apuntados unos tiempos cinco veces peores que los de verdad, y la
        // proxima persona que lo lea creeria que el modem no cabe en su ventana.
#if !DEBUG
        Escribir(informe);
#endif

        // Ninguna franja puede inventar un mensaje. Es la condicion que no se negocia.
        ft8.Sum(f => f.Falsos).Should().Be(0, "un mensaje falso mete un contacto que nunca existió");
        ft4.Sum(f => f.Falsos).Should().Be(0, "un mensaje falso mete un contacto que nunca existió");

        // Con senal holgada tiene que salir practicamente todo.
        ft8.First(f => Math.Abs(f.Decibelios) < 0.1).Porcentaje.Should().BeGreaterThan(95);
        ft4.First(f => Math.Abs(f.Decibelios) < 0.1).Porcentaje.Should().BeGreaterThan(95);

        // Y por debajo del ruido tiene que seguir sacando cosas, que es la gracia del modo.
        ft8.First(f => Math.Abs(f.Decibelios + 12) < 0.1).Porcentaje.Should().BeGreaterThan(90,
            "a −12 dB un decodificador sano con el código de verdad recupera prácticamente todo");
        ft8.First(f => Math.Abs(f.Decibelios + 18) < 0.1).Porcentaje.Should().BeGreaterThan(70,
            "−18 dB es terreno normal de FT8, no un caso extremo");
    }

    private static string Componer(
        IReadOnlyList<FranjaDelBanco> ft8, IReadOnlyList<FranjaDelBanco> ft4, IReadOnlyList<FranjaDelBanco> filo)
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
        sb.AppendLine("Las cifras son con el **código corrector LDPC(174,91) de verdad**, el mismo que usa todo");
        sb.AppendLine("el mundo en FT8 y FT4; su tabla está en `src/Nodisla.Cuaderno.Modos/Tablas/` con su");
        sb.AppendLine("procedencia y su licencia, y el fichero trae además la matriz generadora, que no se usa");
        sb.AppendLine("para codificar sino para cruzarla con la de paridad en cada arranque. Lo que se mide");
        sb.AppendLine("aquí es, por tanto, la sensibilidad real del módem contra ruido blanco.");
        sb.AppendLine();
        sb.AppendLine("La **recuperación profunda** está puesta y marcada aparte. Es la que hace casi todo el");
        sb.AppendLine("trabajo en el filo: entre −21 y −19 dB, la mitad larga de lo que sale viene de ella.");
        sb.AppendLine("Viene con dos frenos, los dos con su número sacado de medir y no de opinar: sólo se");
        sb.AppendLine("intenta en candidatas con sincronismo de verdad, y la palabra reconstruida tiene que");
        sb.AppendLine("parecerse a lo que oyó el demodulador. Sin esos frenos salían **doce mensajes inventados");
        sb.AppendLine("por cada mil ventanas**; con ellos puestos, ninguno.");
        sb.AppendLine();
        sb.AppendLine("Hay una columna que merece más atención de la que parece: **Rechazos del CRC**. Son las");
        sb.AppendLine("palabras que llegaron al sello y no cuadraron. No son un fallo —el sistema funcionando—");
        sb.AppendLine("pero sí son la medida del **riesgo latente**: cada una es una tirada de una entre 16.384");
        sb.AppendLine("de colarse. Mirar sólo la columna de falsos engaña, porque puede salir cero por suerte;");
        sb.AppendLine("ésta dice cuántas veces se ha tentado a la suerte. Si un cambio la dispara, el cambio es");
        sb.AppendLine("peligroso aunque esa tarde no haya salido ningún falso.");
        sb.AppendLine();
        sb.AppendLine("La columna que manda es **Falsos**: tiene que ser cero en todas las franjas. Un mensaje");
        sb.AppendLine("falso mete en el cuaderno un contacto que nunca existió y contamina los diplomas para");
        sb.AppendLine("siempre; perder decodificaciones no deja rastro.");
        sb.AppendLine();
        sb.AppendLine("La columna **ms de CPU/ventana** es tiempo de **procesador**, no de reloj: cuenta los");
        sb.AppendLine("ciclos que el sistema apunta a este proceso, así que no se estira porque la máquina");
        sb.AppendLine("esté ocupada con otra cosa. Aun así solo vale si se dice cómo se compiló, porque entre");
        sb.AppendLine("depuración y publicación hay un factor de cinco; estas cifras son de una compilación");
        sb.AppendLine(CultureInfo.GetCultureInfo("es-ES"),
            $"**{Configuracion}**. Para hacerse una idea: FT8 da 15.000 ms por ventana y FT4 da 7.500, así");
        sb.AppendLine("que aquí se está usando en torno al 3 % del hueco disponible.");
        sb.AppendLine();
        sb.Append(BancoDeMedida.Tabla("FT8", ft8));
        sb.Append(BancoDeMedida.Tabla("FT4", ft4));
        sb.AppendLine("### FT8 en el filo, de decibelio en decibelio");
        sb.AppendLine();
        sb.AppendLine("Donde el módem deja de sacar mensajes. El doble de ventanas por franja, porque aquí es");
        sb.AppendLine("donde un cambio se nota y donde el azar engaña más.");
        sb.AppendLine();
        sb.Append(BancoDeMedida.Tabla("FT8, −16 a −23 dB", filo));
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
