using System.Globalization;
using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Cw;
using Xunit.Abstractions;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Pasa el banco de telegrafía y deja las cifras escritas en el repositorio
/// (<c>resultados-cw.md</c>, solo desde una compilación de publicación).
/// </summary>
/// <remarks>
/// Lo que no se negocia: con ruido puro no sale ni un carácter. Lo demás son guardas contra
/// regresiones, con margen, no objetivos.
/// </remarks>
public class BancoCwPruebas(ITestOutputHelper salida)
{
#if DEBUG
    private const string Configuracion = "de depuración (Debug)";
    private const int SegundosDeRuido = 120;
    private static readonly double[] Relaciones = [0, -6, -10];
#else
    private const string Configuracion = "de publicación (Release)";
    private const int SegundosDeRuido = 1800;
    private static readonly double[] Relaciones = [10, 0, -4, -6, -8, -10, -12, -14];
#endif

    private static readonly double[] Velocidades = [12, 20, 30, 40];

    [Fact]
    public void ElBancoDeCwSePasaYSeApunta()
    {
        var reloj = System.Diagnostics.Stopwatch.StartNew();
        var porVelocidad = new List<FranjaCw>();
        var semilla = 1;
        foreach (var wpm in Velocidades)
            foreach (var db in Relaciones)
                porVelocidad.Add(BancoCw.Medir(BancoCw.Escenario.Limpia, wpm, db, semilla++));

        var dificiles = new List<FranjaCw>();
        foreach (var escenario in new[] { BancoCw.Escenario.Mano, BancoCw.Escenario.Qsb, BancoCw.Escenario.Qrm, BancoCw.Escenario.CambioDeVelocidad })
            foreach (var db in Relaciones.Where(d => d >= -10))
                dificiles.Add(BancoCw.Medir(escenario, 20, db, semilla++));

        var fijo = new List<FranjaCw>();
        foreach (var db in Relaciones.Where(d => d <= 0))
        {
            // Mismo audio que en automático, pero con el tono fijado a mano a 700 Hz exactos.
            fijo.Add(MedirConTonoFijo(20, db, semilla++));
        }

        var ruido = RuidoPuro(SegundosDeRuido);
        reloj.Stop();

        var informe = Componer(porVelocidad, dificiles, fijo, ruido, reloj.Elapsed);
        salida.WriteLine(informe);
#if !DEBUG
        Escribir(informe);
#endif

        ruido.Should().Be(0, "con ruido puro no puede salir texto");
        // Que nunca escriba más de lo que se envió: más del 100 % de CER es texto basura.
        porVelocidad.Concat(dificiles).Concat(fijo).Should().OnlyContain(f => f.Cer <= 101, "lo que no se lee no se inventa");

        // Guardas contra regresiones, con margen sobre lo medido (ver resultados-cw.md).
        porVelocidad.Where(f => f.Decibelios >= 0).Should().OnlyContain(f => f.Cer < 10, "de 0 dB para arriba se copia casi limpio");
        porVelocidad.Where(f => Math.Abs(f.Decibelios + 4) < 0.1 && f.Wpm <= 30).Should().OnlyContain(f => f.Cer < 15);
        dificiles.Where(f => f.Decibelios >= 10).Should().OnlyContain(f => f.Cer < 25, "mano, QSB, QRM y cambios de velocidad con buena señal");
        dificiles.Where(f => f.Decibelios >= 0 && !f.Escenario.StartsWith("QSB", StringComparison.Ordinal)).Should().OnlyContain(f => f.Cer < 20);
    }

    /// <summary>Caracteres que salen de <paramref name="segundos"/> de ruido blanco, con todos los canales.</summary>
    public static int RuidoPuro(int segundos)
    {
        var azar = new Random(4242);
        var total = 0;
        var trozo = 60;
        for (var hecho = 0; hecho < segundos; hecho += trozo)
        {
            var audio = new float[BancoCw.Frecuencia * trozo];
            GeneradorDeSenal.AnadirRuido(audio, 0.045, 0, BancoCw.Frecuencia, azar);
            var textos = BancoCw.DecodificarTodo(audio, BancoCw.Frecuencia, new OpcionesCw { CanalesMaximos = 8 });
            total += textos.Values.Sum(t => t.Replace(" ", string.Empty, StringComparison.Ordinal).Length);
        }

        return total;
    }

    private static FranjaCw MedirConTonoFijo(double wpm, double db, int semilla)
    {
        var azar = new Random(semilla);
        int caracteres = 0, errores = 0;
        double medida = 0;
        foreach (var texto in BancoCw.Textos)
        {
            var audio = BancoCw.Senal(BancoCw.Escenario.Limpia, texto, wpm, db, 700, azar);
            var (leido, w) = BancoCw.Decodificar(audio, BancoCw.Frecuencia, new OpcionesCw { CanalesMaximos = 1 }, 700);
            var esperado = BancoCw.Normalizar(texto);
            caracteres += BancoCw.Longitud(esperado);
            errores += BancoCw.Distancia(esperado, BancoCw.Normalizar(leido));
            medida += w;
        }

        return new FranjaCw("Tono fijo 700 Hz", wpm, db, caracteres, errores, medida / BancoCw.Textos.Length);
    }

    private static string Componer(
        IReadOnlyList<FranjaCw> porVelocidad,
        IReadOnlyList<FranjaCw> dificiles,
        IReadOnlyList<FranjaCw> fijo,
        int ruido,
        TimeSpan tiempo)
    {
        var c = CultureInfo.GetCultureInfo("es-ES");
        var sb = new StringBuilder();
        sb.AppendLine("# Banco de medida del decodificador de CW");
        sb.AppendLine();
        sb.AppendLine(c, $"Generado por `BancoCwPruebas` el {DateTime.UtcNow:yyyy-MM-dd} (UTC). **No editar a mano.**");
        sb.AppendLine();
        sb.AppendLine("Telegrafía sintética (`SintetizadorCw`, rampas de 5 ms) sobre ruido blanco gaussiano. La relación");
        sb.AppendLine("señal-ruido es la potencia **con la llave abajo** frente al ruido en 2500 Hz, como en el resto de");
        sb.AppendLine("bancos del módem; la columna de 500 Hz suma 7 dB (la referencia habitual en telegrafía).");
        sb.AppendLine();
        sb.AppendLine("Cada franja pasa los cuatro textos del banco (contacto con indicativos, RST, números, puntuación y");
        sb.AppendLine("los prosignos <KN>, <BT>, <SK> y <AR>) con un tono al azar entre 600 y 800 Hz y el decodificador en");
        sb.AppendLine("**automático** (busca el tono solo) y con los ajustes de fábrica: filtro de 100 Hz, sensibilidad 6,");
        sb.AppendLine("de 5 a 60 WPM. El **CER** es la distancia de edición entre lo enviado y lo leído (espacios");
        sb.AppendLine("incluidos, cada prosigno cuenta uno) entre los caracteres enviados. Semilla fija.");
        sb.AppendLine();
        sb.AppendLine(c, $"Compilación **{Configuracion}**; el banco entero tardó {tiempo.TotalSeconds:0} s.");
        sb.AppendLine();
        sb.Append(BancoCw.Tabla("Manipulador electrónico, por velocidad y relación señal-ruido", porVelocidad));
        sb.Append(BancoCw.Tabla("Condiciones difíciles, a 20 WPM", dificiles));
        sb.Append(BancoCw.Tabla("Tono fijado a mano (pitch del equipo), a 20 WPM", fijo));
        sb.AppendLine("### Ruido puro");
        sb.AppendLine();
        sb.AppendLine(c, $"{SegundosDeRuido / 60} minutos de ruido blanco sin señal, con ocho canales a la vez: **{ruido} caracteres** escritos.");
        sb.AppendLine();
        sb.AppendLine("### Grabaciones reales");
        sb.AppendLine();
        sb.AppendLine("En el repositorio no hay ninguna grabación de telegrafía real. Se usa la de FT8 de 20 m");
        sb.AppendLine("(`Aire/ft8-20m-2026-09-27-151745.wav`) como prueba de que el audio real de otro modo no escribe");
        sb.AppendLine("texto basura (`DecodificadorCwPruebas`).");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void Escribir(string informe)
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Nodisla.Cuaderno.Modos.Pruebas.csproj")))
            carpeta = carpeta.Parent;
        if (carpeta is null) return;
        File.WriteAllText(Path.Combine(carpeta.FullName, "Banco", "resultados-cw.md"), informe, new UTF8Encoding(false));
    }
}
