using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Banco;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Pruebas.Banco;

/// <summary>Como fue una franja de relacion senal-ruido.</summary>
/// <param name="Decibelios">Relacion senal-ruido de las senales de la franja.</param>
/// <param name="Intentos">Ventanas probadas.</param>
/// <param name="Aciertos">Ventanas en que salio el mensaje que se habia emitido.</param>
/// <param name="Falsos">
/// Mensajes que salieron y no eran el emitido. <b>Esta columna tiene que ser cero.</b>
/// </param>
/// <param name="RechazadosPorElCrc">Palabras que el corrector dio por buenas y el CRC tiro.</param>
/// <param name="ErrorDelInforme">Diferencia media entre el informe medido y el de verdad.</param>
/// <param name="MilisegundosPorVentana">
/// Lo que costo cada ventana, de media, en <b>milisegundos de procesador</b>. No es tiempo de
/// reloj: si la maquina esta ocupada con otras cosas, el reloj se estira pero esta cifra no.
/// </param>
public sealed record FranjaDelBanco(
    double Decibelios,
    int Intentos,
    int Aciertos,
    int Falsos,
    int RechazadosPorElCrc,
    double ErrorDelInforme,
    double MilisegundosPorVentana)
{
    /// <summary>Porcentaje de mensajes recuperados.</summary>
    public double Porcentaje => Intentos == 0 ? 0 : 100.0 * Aciertos / Intentos;
}

/// <summary>
/// Mide cuantos mensajes recupera el decodificador en cada franja de relacion senal-ruido.
/// </summary>
/// <remarks>
/// <para>
/// El procedimiento es el que se usa para publicar cifras de cualquier modo digital: se fabrica
/// una senal de nivel conocido, se le anade ruido blanco hasta dejarla en la relacion que se
/// quiere probar, y se cuenta cuantas veces vuelve el mensaje. Repetido muchas veces y en varias
/// franjas, sale la curva de sensibilidad.
/// </para>
/// <para>
/// Se cuentan tres cosas, y la tercera es la que manda:
/// </para>
/// <list type="number">
/// <item><b>Aciertos</b>: volvio el mensaje que se emitio.</item>
/// <item><b>Perdidas</b>: no volvio nada, que con poca senal es lo normal y no es un fallo.</item>
/// <item><b>Falsos</b>: volvio <i>otro</i> mensaje. Un solo falso vale menos que cien perdidas,
/// porque una perdida no deja rastro y un falso mete un contacto que nunca existio.</item>
/// </list>
/// </remarks>
public static class BancoDeMedida
{
    private static readonly string[] Mensajes =
    [
        "CQ EA8DLF IL18",
        "EA8DLF EA1ABC IN80",
        "EA1ABC EA8DLF -07",
        "CQ K1ABC FN42",
        "K1ABC EA8DLF R-15",
        "EA8DLF K1ABC RR73",
        "CQ DX EA8DLF IL18",
        "JA1XYZ EA8DLF 73",
    ];

    /// <summary>
    /// Recorre las franjas de relacion senal-ruido y devuelve las cifras.
    /// </summary>
    /// <param name="tablas">Tablas del protocolo con las que medir.</param>
    /// <param name="modo">FT8 o FT4.</param>
    /// <param name="desde">Relacion mas alta, en decibelios.</param>
    /// <param name="hasta">Relacion mas baja, en decibelios.</param>
    /// <param name="paso">Cuanto se baja en cada franja.</param>
    /// <param name="ventanasPorFranja">Ventanas que se prueban en cada franja.</param>
    /// <param name="semilla">Semilla, para que la medida se pueda repetir exactamente igual.</param>
    public static List<FranjaDelBanco> Recorrer(
        TablasDelProtocolo tablas,
        ModoDelModem modo,
        double desde = 0,
        double hasta = -24,
        double paso = -3,
        int ventanasPorFranja = 20,
        int semilla = 20260922)
    {
        ArgumentNullException.ThrowIfNull(tablas);
        var p = ParametrosDelModo.De(modo);
        var codificador = new Codificador(tablas);
        var decodificador = new Decodificador(tablas);
        const int Frecuencia = 48000;

        var franjas = new List<FranjaDelBanco>();
        for (var db = desde; db >= hasta - 1e-9; db += paso)
        {
            int aciertos = 0, falsos = 0, rechazados = 0;
            double sumaDeErrores = 0;
            double coste = 0;

            for (var v = 0; v < ventanasPorFranja; v++)
            {
                // La semilla depende de la franja y del intento, asi que la medida entera se
                // repite igual en cualquier maquina y cualquier dia.
                var azar = new Random(semilla + (int)(db * 1000) + (v * 7919));
                var texto = Mensajes[azar.Next(Mensajes.Length)];
                var tono = 500 + (azar.NextDouble() * 2000);
                var desfase = (azar.NextDouble() - 0.5) * 0.6;

                if (!codificador.TryCodificar(texto, modo, out var tonos, out var motivo))
                    throw new InvalidOperationException(motivo);

                var ventana = GeneradorDeSenal.Ventana(p, tonos, tono, desfase, db, Frecuencia, azar);
                ResultadoDeVentana resultado = null!;
                coste += Medidas.TiempoDeProceso.De(() => resultado = decodificador.Decodificar(
                    ventana, Frecuencia, modo, DateTimeOffset.UnixEpoch, new CatalogoDeIndicativos()));

                rechazados += resultado.RechazadasPorElCrc;
                var acertada = resultado.Decodificaciones.FirstOrDefault(d => d.Texto == texto);
                if (acertada is not null)
                {
                    aciertos++;
                    sumaDeErrores += acertada.Decibelios - db;
                }
                falsos += resultado.Decodificaciones.Count(d => d.Texto != texto);
            }

            franjas.Add(new FranjaDelBanco(
                db, ventanasPorFranja, aciertos, falsos, rechazados,
                aciertos == 0 ? 0 : sumaDeErrores / aciertos,
                coste / ventanasPorFranja));
        }
        return franjas;
    }

    /// <summary>Escribe las cifras en una tabla legible.</summary>
    /// <param name="titulo">Encabezado de la tabla.</param>
    /// <param name="franjas">Cifras a escribir.</param>
    public static string Tabla(string titulo, IReadOnlyList<FranjaDelBanco> franjas)
    {
        ArgumentNullException.ThrowIfNull(franjas);
        var sb = new StringBuilder();
        var c = CultureInfo.GetCultureInfo("es-ES");
        sb.Append("### ").AppendLine(titulo).AppendLine();
        sb.AppendLine("| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var f in franjas)
            sb.AppendLine(string.Format(c,
                "| {0:0} | {1} | {2} | {3:0.0} | {4} | {5} | {6:+0.0;-0.0;0,0} | {7:0} |",
                f.Decibelios, f.Intentos, f.Aciertos, f.Porcentaje, f.Falsos, f.RechazadosPorElCrc,
                f.ErrorDelInforme, f.MilisegundosPorVentana));
        sb.AppendLine();
        return sb.ToString();
    }
}
