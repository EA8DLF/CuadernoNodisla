using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Modos.Msk144;
using Nodisla.Cuaderno.Modos.Pruebas.Tablas;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Pasa el banco de MSK144 y deja las cifras escritas en el repositorio.
/// </summary>
/// <remarks>
/// Como el de FT8: publica <c>resultados-banco-msk144.md</c> desde una compilacion de
/// publicacion, y comprueba lo que no se negocia —cero falsos, tambien con ruido puro— mas una
/// condicion floja de que el decodificador saca lo que tiene que sacar.
/// </remarks>
public class BancoMsk144Pruebas(ITestOutputHelper salida)
{
#if DEBUG
    private const string Configuracion = "de depuración (Debug)";
    private const int VentanasPorFranja = 3;
    private const int VentanasDeRuido = 6;
#else
    private const string Configuracion = "de publicación (Release)";
    private const int VentanasPorFranja = 12;
    private const int VentanasDeRuido = 60;
#endif

    [Fact]
    public void ElBancoDeMsk144SePasaYSeApunta()
    {
        var codificador = new CodificadorMsk144(TablasDelRepositorio.Msk144, TablasDelRepositorio.Msk40);
        var decodificador = new DecodificadorMsk144(TablasDelRepositorio.Msk144, TablasDelRepositorio.Msk40);
        decodificador.RecordarPar("EA1ABC", "EA8DLF");

        var unaTrama = BancoMsk144.PorRelacion(decodificador, codificador, tramas: 1, desde: 6, hasta: -6, paso: -2, VentanasPorFranja);
        var sieteTramas = BancoMsk144.PorRelacion(decodificador, codificador, tramas: 7, desde: 0, hasta: -12, paso: -2, VentanasPorFranja);
        var porDuracionA0 = BancoMsk144.PorDuracion(decodificador, codificador, decibelios: 0, [1, 2, 3, 5, 7, 14], VentanasPorFranja);
        var porDuracionA6 = BancoMsk144.PorDuracion(decodificador, codificador, decibelios: -6, [1, 2, 3, 5, 7, 14], VentanasPorFranja);
        var ruido = BancoMsk144.RuidoPuro(decodificador, VentanasDeRuido);

        var informe = Componer(unaTrama, sieteTramas, porDuracionA0, porDuracionA6, ruido);
        salida.WriteLine(informe);
#if !DEBUG
        Escribir(informe);
#endif

        foreach (var franjas in new[] { unaTrama, sieteTramas, porDuracionA0, porDuracionA6 })
            franjas.Sum(f => f.Falsos).Should().Be(0, "un mensaje falso mete un contacto que nunca existió");
        ruido.Mensajes.Should().Be(0, "con ruido puro no puede salir nada");

        // Guarda contra regresiones, no objetivo: con una trama sola el decodificador aun queda
        // unos 3 dB por detras del articulo (ver resultados-banco-msk144.md). Pendiente de afinar.
        unaTrama.First(f => Math.Abs(f.Decibelios - 6) < 0.1).Porcentaje.Should().BeGreaterThan(60);
        sieteTramas.First(f => Math.Abs(f.Decibelios + 4) < 0.1).Porcentaje.Should().BeGreaterThan(50,
            "a −4 dB con siete tramas el promediado tiene que sacar la mayoría");
    }

    private static string Componer(
        IReadOnlyList<FranjaMsk144> unaTrama,
        IReadOnlyList<FranjaMsk144> sieteTramas,
        IReadOnlyList<FranjaMsk144> porDuracionA0,
        IReadOnlyList<FranjaMsk144> porDuracionA6,
        (int Mensajes, int PalabrasValidas, int Rechazadas, double MilisegundosPorVentana) ruido)
    {
        var c = CultureInfo.GetCultureInfo("es-ES");
        var sb = new StringBuilder();
        sb.AppendLine("# Banco de medida de MSK144");
        sb.AppendLine();
        sb.AppendLine(c, $"Generado por `BancoMsk144Pruebas` el {DateTime.UtcNow:yyyy-MM-dd} (UTC). **No editar a mano.**");
        sb.AppendLine();
        sb.AppendLine("Pings sintéticos sobre ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz. Cada");
        sb.AppendLine("ventana de 15 s lleva un solo ping de tantas tramas de 72 ms como diga la columna, con un");
        sb.AppendLine("mensaje al azar entre ocho, en un instante al azar y con la portadora desviada hasta ±80 Hz.");
        sb.AppendLine("Semilla fija: la medida se repite igual en cualquier máquina.");
        sb.AppendLine();
        sb.AppendLine("Las cifras son con el **código LDPC(128,90) de verdad** (tabla en `Tablas/tablas-msk144.txt`,");
        sb.AppendLine("con su procedencia) y con el (32,16) de los mensajes cortos. El decodificador demodula");
        sb.AppendLine("coherentemente y suma en fase hasta siete tramas consecutivas; la columna «Sólo por");
        sb.AppendLine("promediado» cuenta los pings que no salieron con una trama sola.");
        sb.AppendLine();
        sb.AppendLine("El artículo del protocolo (QEX jul/ago 2017) da, para una trama sola con sincronismo perfecto,");
        sb.AppendLine("casi el 100 % a 0 dB y el 40 % a −1,5 dB; el promediado de N tramas mueve la curva 10·log N");
        sb.AppendLine("hacia abajo (8,5 dB con siete). Eso es lo que hay que comparar con estas tablas.");
        sb.AppendLine();
        sb.AppendLine("**Falsos** tiene que ser cero en todas las franjas, y con ruido puro no puede salir nada. La");
        sb.AppendLine("columna «Palabras válidas» es el riesgo latente: cada una es una tirada del CRC de 13 bits, y");
        sb.AppendLine("por eso hay un freno de errores duros antes del CRC (ver `ErroresDurosMaximos`).");
        sb.AppendLine();
        sb.AppendLine(c, $"Tiempos de **procesador** de una compilación **{Configuracion}**; la ventana entera son 15.000 ms.");
        sb.AppendLine();
        sb.Append(BancoMsk144.Tabla("Ping de una trama (72 ms), por relación señal-ruido", unaTrama));
        sb.Append(BancoMsk144.Tabla("Ping de siete tramas (504 ms), por relación señal-ruido", sieteTramas));
        sb.Append(BancoMsk144.Tabla("A 0 dB, por duración del ping", porDuracionA0));
        sb.Append(BancoMsk144.Tabla("A −6 dB, por duración del ping", porDuracionA6));
        sb.AppendLine("### Ruido puro");
        sb.AppendLine();
        sb.AppendLine(c, $"{VentanasDeRuido} ventanas de 15 s sin señal: **{ruido.Mensajes} mensajes**, {ruido.PalabrasValidas} palabras válidas del corrector, {ruido.Rechazadas} rechazadas por los frenos y el CRC, {ruido.MilisegundosPorVentana:0} ms de CPU por ventana.");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void Escribir(string informe)
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Nodisla.Cuaderno.Modos.Pruebas.csproj")))
            carpeta = carpeta.Parent;
        if (carpeta is null) return;
        var destino = Path.Combine(carpeta.FullName, "Banco", "resultados-banco-msk144.md");
        File.WriteAllText(destino, informe, new UTF8Encoding(false));
    }
}
