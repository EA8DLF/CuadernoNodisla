using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Jt9;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>
/// Mide cuantos mensajes recupera el decodificador de JT9 en cada franja de relacion senal-ruido.
/// </summary>
/// <remarks>
/// El procedimiento es el mismo que el del banco de FT8: senal sintetica de nivel conocido,
/// ruido blanco gaussiano hasta dejarla en la relacion pedida (referida a 2500 Hz), y a contar.
/// La senal se sintetiza directamente a la frecuencia de analisis (12000 muestras por segundo)
/// y se coloca en el segundo 1 mas un desfase al azar, que es donde la pone el protocolo.
/// </remarks>
public static class BancoJt9
{
    /// <summary>Mensajes de la gramatica de JT9 (la misma que JT65) con los que se mide.</summary>
    public static readonly string[] Mensajes =
    [
        "CQ EA8DLF IL18",
        "EA8DLF EA1ABC IN80",
        "EA1ABC EA8DLF -07",
        "CQ K1ABC FN42",
        "K1ABC EA8DLF R-15",
        "EA8DLF K1ABC RRR",
        "CQ DX EA8DLF IL18",
        "JA1XYZ EA8DLF 73",
    ];

    /// <summary>Recorre las franjas de relacion senal-ruido y devuelve las cifras.</summary>
    /// <param name="decodificador">Decodificador a medir, con sus mandos ya puestos.</param>
    /// <param name="desde">Relacion mas alta, en decibelios.</param>
    /// <param name="hasta">Relacion mas baja, en decibelios.</param>
    /// <param name="paso">Cuanto se baja en cada franja (negativo).</param>
    /// <param name="ventanasPorFranja">Ventanas que se prueban en cada franja.</param>
    /// <param name="semilla">Semilla, para que la medida se repita exactamente igual.</param>
    /// <param name="alAcabarFranja">Aviso por franja, para ir viendo el progreso.</param>
    public static List<FranjaDeModoLento> Recorrer(
        DecodificadorJt9 decodificador,
        double desde = -16,
        double hasta = -30,
        double paso = -1,
        int ventanasPorFranja = 12,
        int semilla = 20260927,
        Action<FranjaDeModoLento>? alAcabarFranja = null)
    {
        ArgumentNullException.ThrowIfNull(decodificador);
        var codificador = new CodificadorJt9();

        var franjas = new List<FranjaDeModoLento>();
        for (var db = desde; db >= hasta - 1e-9; db += paso)
        {
            int aciertos = 0, falsos = 0, rechazadas = 0;
            double sumaDeErrores = 0, coste = 0;

            for (var v = 0; v < ventanasPorFranja; v++)
            {
                var azar = new Random(semilla + (int)(db * 1000) + (v * 7919));
                var texto = Mensajes[azar.Next(Mensajes.Length)];
                var tono = 500 + (azar.NextDouble() * 2000);
                var desfase = (azar.NextDouble() - 0.5) * 1.0;

                if (!codificador.TryCodificar(texto, out var tonos, out var motivo))
                    throw new InvalidOperationException(motivo);
                var ventana = Ventana(tonos, tono, desfase, db, azar);

                ResultadoDeVentanaJt9 resultado = null!;
                coste += Medidas.TiempoDeProceso.De(() => resultado = decodificador.Decodificar(ventana, DateTimeOffset.UnixEpoch));

                rechazadas += resultado.Rechazadas;
                var acertada = resultado.Decodificaciones.FirstOrDefault(d => d.Texto == texto);
                if (acertada is not null)
                {
                    aciertos++;
                    sumaDeErrores += acertada.Decibelios - db;
                }
                falsos += resultado.Decodificaciones.Count(d => d.Texto != texto);
            }

            var franja = new FranjaDeModoLento(db, ventanasPorFranja, aciertos, falsos, rechazadas,
                aciertos == 0 ? 0 : sumaDeErrores / aciertos, coste / ventanasPorFranja);
            franjas.Add(franja);
            alAcabarFranja?.Invoke(franja);
        }
        return franjas;
    }

    /// <summary>Pasa ventanas de ruido puro, sin senal ninguna, y cuenta lo que sale.</summary>
    public static TandaDeRuidoDeModoLento RuidoPuro(DecodificadorJt9 decodificador, int ventanas, int semilla = 777)
    {
        ArgumentNullException.ThrowIfNull(decodificador);
        int falsos = 0, examinadas = 0, rechazadas = 0;
        double coste = 0;
        const int largo = ParametrosJt9.MuestrasDeLaVentana;
        for (var v = 0; v < ventanas; v++)
        {
            var azar = new Random(semilla + (v * 104729));
            var ventana = new float[largo];
            GeneradorDeSenal.AnadirRuido(ventana, 0.01, 0, ParametrosJt9.FrecuenciaDeAnalisis, azar);
            ResultadoDeVentanaJt9 resultado = null!;
            coste += Medidas.TiempoDeProceso.De(() => resultado = decodificador.Decodificar(ventana, DateTimeOffset.UnixEpoch));
            falsos += resultado.Decodificaciones.Count;
            examinadas += resultado.Examinadas;
            rechazadas += resultado.Rechazadas;
        }
        return new TandaDeRuidoDeModoLento(ventanas, falsos, examinadas, rechazadas, ventanas == 0 ? 0 : coste / ventanas);
    }

    /// <summary>Fabrica una ventana de JT9 con una senal a la relacion pedida.</summary>
    public static float[] Ventana(ReadOnlySpan<byte> tonos, double tonoHz, double desfaseSegundos, double decibelios, Random azar)
    {
        const int Frecuencia = ParametrosJt9.FrecuenciaDeAnalisis;
        const double Amplitud = 0.35;
        var senal = ModuladorJt9.Sintetizar(tonos, tonoHz, Frecuencia, Amplitud);
        var ventana = new float[ParametrosJt9.MuestrasDeLaVentana];
        var comienzo = (int)Math.Round((ParametrosJt9.ComienzoNominalSegundos + desfaseSegundos) * Frecuencia);
        for (var i = 0; i < senal.Length; i++)
        {
            var j = comienzo + i;
            if (j >= 0 && j < ventana.Length) ventana[j] += senal[i];
        }
        GeneradorDeSenal.AnadirRuido(ventana, GeneradorDeSenal.PotenciaMedia(senal), decibelios, Frecuencia, azar);
        return ventana;
    }

    /// <summary>Escribe las cifras en una tabla legible.</summary>
    public static string Tabla(string titulo, IReadOnlyList<FranjaDeModoLento> franjas)
    {
        ArgumentNullException.ThrowIfNull(franjas);
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.Append("### ").AppendLine(titulo).AppendLine();
        sb.AppendLine("| S/R (dB) | Ventanas | Recuperados | % | Falsos | Caminos rechazados | Error del informe (dB) | ms de CPU/ventana |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var f in franjas)
            sb.AppendLine(string.Format(c,
                "| {0:0} | {1} | {2} | {3:0.0} | {4} | {5} | {6:+0.0;-0.0;0,0} | {7:0} |",
                f.Decibelios, f.Intentos, f.Aciertos, f.Porcentaje, f.Falsos, f.Rechazadas,
                f.ErrorDelInforme, f.MilisegundosPorVentana));
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Escribe la tanda de ruido puro.</summary>
    public static string Tabla(string titulo, TandaDeRuidoDeModoLento tanda)
    {
        ArgumentNullException.ThrowIfNull(tanda);
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.Append("### ").AppendLine(titulo).AppendLine();
        sb.AppendLine("| Ventanas de ruido | Falsos | Candidatas examinadas | Caminos rechazados | ms de CPU/ventana |");
        sb.AppendLine("|---:|---:|---:|---:|---:|");
        sb.AppendLine(string.Format(c, "| {0} | {1} | {2} | {3} | {4:0} |",
            tanda.Ventanas, tanda.Falsos, tanda.Examinadas, tanda.Rechazadas, tanda.MilisegundosPorVentana));
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>Deja un informe junto a las pruebas, para que entre en el control de versiones.</summary>
    public static void Escribir(string nombreDelFichero, string informe)
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "Nodisla.Cuaderno.Modos.Pruebas.csproj")))
            carpeta = carpeta.Parent;
        // Compilado fuera del arbol (carpeta de salida propia): se acepta la ruta por variable de entorno.
        var destinoForzado = Environment.GetEnvironmentVariable("NODISLA_BANCO_DESTINO");
        if (carpeta is null && !string.IsNullOrEmpty(destinoForzado)) carpeta = new DirectoryInfo(destinoForzado);
        if (carpeta is null || !carpeta.Exists) return;

        var destino = Path.Combine(carpeta.FullName, "Banco", nombreDelFichero);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllText(destino, informe, new UTF8Encoding(false));
    }

    /// <summary>Ventanas por franja: las que diga la variable de entorno, o las de la configuracion.</summary>
    public static int VentanasPorFranja(int porOmision)
    {
        var texto = Environment.GetEnvironmentVariable("NODISLA_BANCO_VENTANAS");
        return int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : porOmision;
    }
}
