using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Modos.Fst4;
using Nodisla.Cuaderno.Modos.Pruebas.Tablas;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Pasa el banco de FST4 y FST4W y deja las cifras escritas en el repositorio.
/// </summary>
/// <remarks>
/// Los periodos que se miden son los que Jose va a usar —15, 30, 60 y 120 s en FST4 y 120 en
/// FST4W—; los de 300 s en adelante existen y decodifican, pero cada ventana son minutos de
/// audio y no caben en una serie de pruebas. Publica <c>resultados-banco-fst4.md</c> desde una
/// compilacion de publicacion.
/// </remarks>
public class BancoFst4Pruebas(ITestOutputHelper salida)
{
#if DEBUG
    private const string Configuracion = "de depuración (Debug)";
    private const int VentanasPorFranja = 2;
    private const int VentanasDeRuido = 3;
#else
    private const string Configuracion = "de publicación (Release)";
    private const int VentanasPorFranja = 8;
    private const int VentanasDeRuido = 20;
#endif

    [Fact]
    public void ElBancoDeFst4SePasaYSeApunta()
    {
        var codificador = new CodificadorFst4(TablasDelRepositorio.Fst4, esFst4w: false);
        var codificadorW = new CodificadorFst4(TablasDelRepositorio.Fst4w, esFst4w: true);

        var tablas = new List<(string Titulo, List<FranjaFst4> Franjas)>();
        foreach (var (periodo, desde, hasta) in new[] { (15, -12.0, -24.0), (30, -16.0, -28.0), (60, -20.0, -32.0), (120, -22.0, -34.0) })
        {
            var d = new DecodificadorFst4(periodo, TablasDelRepositorio.Fst4, esFst4w: false);
            tablas.Add(($"FST4-{periodo}", BancoFst4.Recorrer(d, codificador, desde, hasta, -2, VentanasPorFranja)));
        }
        var w = new DecodificadorFst4(120, TablasDelRepositorio.Fst4w, esFst4w: true);
        tablas.Add(("FST4W-120", BancoFst4.Recorrer(w, codificadorW, -24, -36, -2, VentanasPorFranja)));

        var ruido15 = BancoFst4.RuidoPuro(new DecodificadorFst4(15, TablasDelRepositorio.Fst4, esFst4w: false), VentanasDeRuido);
        var ruidoW = BancoFst4.RuidoPuro(w, Math.Max(1, VentanasDeRuido / 4));

        var informe = Componer(tablas, ruido15, ruidoW);
        salida.WriteLine(informe);
#if !DEBUG
        Escribir(informe);
#endif

        foreach (var (titulo, franjas) in tablas)
            franjas.Sum(f => f.Falsos).Should().Be(0, $"{titulo}: un mensaje falso mete un contacto que nunca existió");
        ruido15.Mensajes.Should().Be(0);
        ruidoW.Mensajes.Should().Be(0);

        tablas.First(t => t.Titulo == "FST4-15").Franjas.First().Porcentaje.Should().BeGreaterThan(90, "a −12 dB FST4-15 saca todo");
        tablas.First(t => t.Titulo == "FST4-60").Franjas.First(f => Math.Abs(f.Decibelios + 24) < 0.1).Porcentaje.Should().BeGreaterThan(60,
            "−24 dB queda cuatro decibelios por encima del umbral publicado de FST4-60");
    }

    private static string Componer(
        List<(string Titulo, List<FranjaFst4> Franjas)> tablas,
        (int Mensajes, int PalabrasValidas, double MilisegundosPorVentana) ruido15,
        (int Mensajes, int PalabrasValidas, double MilisegundosPorVentana) ruidoW)
    {
        var c = CultureInfo.GetCultureInfo("es-ES");
        var sb = new StringBuilder();
        sb.AppendLine("# Banco de medida de FST4 y FST4W");
        sb.AppendLine();
        sb.AppendLine(c, $"Generado por `BancoFst4Pruebas` el {DateTime.UtcNow:yyyy-MM-dd} (UTC). **No editar a mano.**");
        sb.AppendLine();
        sb.AppendLine("Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz. Cada");
        sb.AppendLine("ventana lleva un mensaje al azar, en una frecuencia al azar dentro de la banda que busca el");
        sb.AppendLine("decodificador (100–3000 Hz hasta 120 s; 1400–1600 en FST4W) y con un desfase al azar de");
        sb.AppendLine("hasta ±0,5 s. Semilla fija.");
        sb.AppendLine();
        sb.AppendLine("Las cifras son con los **códigos LDPC(240,101) y (240,74) de verdad** (tablas en");
        sb.AppendLine("`Tablas/tablas-fst4.txt` y `tablas-fst4w.txt`, de `ft8_lib`, MIT). Umbrales publicados");
        sb.AppendLine("(50 % de decodificación, Quick-Start Guide): FST4-15 −20,7 dB, FST4-30 −24,2, FST4-60 −28,1,");
        sb.AppendLine("FST4-120 −31,3, FST4W-120 −32,8. Sin decodificación *a priori*, que aquí no se usa.");
        sb.AppendLine();
        sb.AppendLine("**Falsos** tiene que ser cero en todas las franjas; el CRC de 24 bits deja una entre diecisiete");
        sb.AppendLine("millones a cada palabra de ruido, así que la columna de rechazos del CRC es el riesgo latente.");
        sb.AppendLine();
        sb.AppendLine(c, $"Tiempos de **procesador** de una compilación **{Configuracion}**.");
        sb.AppendLine();
        foreach (var (titulo, franjas) in tablas) sb.Append(BancoFst4.Tabla(titulo, franjas));
        sb.AppendLine("### Ruido puro");
        sb.AppendLine();
        sb.AppendLine(c, $"FST4-15, {VentanasDeRuido} ventanas sin señal: **{ruido15.Mensajes} mensajes**, {ruido15.PalabrasValidas} palabras válidas del corrector, {ruido15.MilisegundosPorVentana:0} ms de CPU por ventana.");
        sb.AppendLine();
        sb.AppendLine(c, $"FST4W-120, {Math.Max(1, VentanasDeRuido / 4)} ventanas sin señal: **{ruidoW.Mensajes} mensajes**, {ruidoW.PalabrasValidas} palabras válidas, {ruidoW.MilisegundosPorVentana:0} ms de CPU por ventana.");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void Escribir(string informe)
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Nodisla.Cuaderno.Modos.Pruebas.csproj")))
            carpeta = carpeta.Parent;
        if (carpeta is null) return;
        var destino = Path.Combine(carpeta.FullName, "Banco", "resultados-banco-fst4.md");
        File.WriteAllText(destino, informe, new UTF8Encoding(false));
    }
}
