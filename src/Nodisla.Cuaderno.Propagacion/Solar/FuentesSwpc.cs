using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Nodisla.Cuaderno.Propagacion.Solar;

/// <summary>Lo que se saca del boletin <c>wwv.txt</c> del SWPC.</summary>
/// <param name="FlujoSolar">Flujo solar a 10,7 cm del dia anterior.</param>
/// <param name="FlujoMedidoUtc">Momento al que se refiere ese flujo.</param>
/// <param name="IndiceA">Indice A planetario estimado del dia anterior.</param>
/// <param name="AMedidoUtc">Momento al que se refiere ese indice A.</param>
/// <param name="IndiceK">Ultimo indice K planetario estimado.</param>
/// <param name="KMedidoUtc">Momento al que se refiere ese indice K.</param>
/// <param name="Tormenta">El boletin habla de tormenta geomagnetica observada.</param>
/// <param name="EmitidoUtc">Momento en que el SWPC emitio el boletin.</param>
public sealed record LecturaWwv(
    double? FlujoSolar,
    DateTimeOffset? FlujoMedidoUtc,
    double? IndiceA,
    DateTimeOffset? AMedidoUtc,
    double? IndiceK,
    DateTimeOffset? KMedidoUtc,
    bool Tormenta,
    DateTimeOffset EmitidoUtc);

/// <summary>Ultima linea util de <c>daily-solar-indices.txt</c>.</summary>
/// <param name="DiaUtc">Dia UTC al que corresponden los valores.</param>
/// <param name="FlujoSolar">Flujo solar a 10,7 cm, o nulo si falta.</param>
/// <param name="Manchas">Numero de manchas SESC, o nulo si falta.</param>
public sealed record LecturaDiariaSolar(DateTimeOffset DiaUtc, double? FlujoSolar, double? Manchas);

/// <summary>Un valor del viento solar con su momento.</summary>
/// <param name="Valor">Valor medido, o nulo si el resumen viene vacio.</param>
/// <param name="MedidoUtc">Momento de la medida.</param>
public sealed record LecturaInstantanea(double? Valor, DateTimeOffset MedidoUtc);

/// <summary>
/// Direcciones y analizadores de los boletines del Space Weather Prediction Center del NOAA.
/// </summary>
/// <remarks>
/// <para>
/// Las tres primeras direcciones son las mismas que usa Log4OM en su <c>config.ini</c>, seccion
/// <c>[SOLAR DATA]</c>; se comprobo el 22 de septiembre de 2026 que siguen respondiendo. Las dos
/// del viento solar no estan en Log4OM y se anaden aqui: son los resumenes del SWPC, que
/// devuelven un solo valor con su hora y pesan unas decenas de bytes.
/// </para>
/// <para>
/// Todo el analisis esta en metodos estaticos puros, sin red, para poder fijarlo con pruebas
/// contra el texto real de cada boletin.
/// </para>
/// </remarks>
public static partial class FuentesSwpc
{
    /// <summary>Boletin geofisico: flujo solar, indice A e indice K en texto plano.</summary>
    public const string UrlBoletinGeofisico = "https://services.swpc.noaa.gov/text/wwv.txt";

    /// <summary>Indices solares diarios de los ultimos treinta dias: flujo y manchas.</summary>
    public const string UrlIndicesSolaresDiarios =
        "https://services.swpc.noaa.gov/text/daily-solar-indices.txt";

    /// <summary>Indices geomagneticos diarios de los ultimos treinta dias.</summary>
    public const string UrlIndicesGeomagneticosDiarios =
        "https://services.swpc.noaa.gov/text/daily-geomagnetic-indices.txt";

    /// <summary>Resumen de la velocidad del viento solar medida por el satelite DSCOVR.</summary>
    public const string UrlVientoSolar =
        "https://services.swpc.noaa.gov/products/summary/solar-wind-speed.json";

    /// <summary>Resumen del campo magnetico interplanetario, con la componente Bz.</summary>
    public const string UrlCampoMagnetico =
        "https://services.swpc.noaa.gov/products/summary/solar-wind-mag-field.json";

    /// <summary>
    /// Hora UTC a la que se toma la medida oficial del flujo solar en Penticton, el paso del Sol
    /// por el meridiano del observatorio.
    /// </summary>
    private static readonly TimeSpan HoraDelFlujoSolar = TimeSpan.FromHours(20);

    /// <summary>A partir de este indice K se considera que hay tormenta geomagnetica (nivel G1).</summary>
    public const double IndiceKDeTormenta = 5.0;

    /// <summary>Analiza el boletin geofisico <c>wwv.txt</c>.</summary>
    /// <param name="texto">Contenido del boletin tal cual lo sirve el SWPC.</param>
    /// <returns>Lo que se haya podido leer, o nulo si el texto no es un boletin.</returns>
    public static LecturaWwv? LeerBoletinGeofisico(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var emision = ExpresionEmision().Match(texto);
        if (!emision.Success)
        {
            return null;
        }

        var anio = int.Parse(emision.Groups[1].Value, CultureInfo.InvariantCulture);
        var mesEmision = Mes(emision.Groups[2].Value);
        var diaEmision = int.Parse(emision.Groups[3].Value, CultureInfo.InvariantCulture);
        if (mesEmision is null)
        {
            return null;
        }

        var emitido = new DateTimeOffset(
            anio,
            mesEmision.Value,
            diaEmision,
            int.Parse(emision.Groups[4].Value, CultureInfo.InvariantCulture),
            int.Parse(emision.Groups[5].Value, CultureInfo.InvariantCulture),
            0,
            TimeSpan.Zero);

        var diaDeLosIndices = FechaDelBoletin(ExpresionDiaDeIndices().Match(texto), emitido);

        double? flujo = null;
        double? indiceA = null;
        var flujoYA = ExpresionFlujoYA().Match(texto);
        if (flujoYA.Success)
        {
            flujo = double.Parse(flujoYA.Groups[1].Value, CultureInfo.InvariantCulture);
            indiceA = double.Parse(flujoYA.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        double? indiceK = null;
        DateTimeOffset? kMedido = null;
        var k = ExpresionIndiceK().Match(texto);
        if (k.Success)
        {
            indiceK = double.Parse(k.Groups[5].Value, CultureInfo.InvariantCulture);
            var diaDelK = FechaDelBoletin(k, emitido, diaGrupo: 3, mesGrupo: 4);
            if (diaDelK is not null)
            {
                var hora = int.Parse(k.Groups[1].Value, CultureInfo.InvariantCulture);
                var minuto = int.Parse(k.Groups[2].Value, CultureInfo.InvariantCulture);
                kMedido = diaDelK.Value.AddHours(hora).AddMinutes(minuto);
            }
        }

        // El flujo solar se mide al mediodia de Penticton; el indice A resume el dia UTC entero,
        // asi que no esta cerrado hasta que el dia acaba. Ninguno de los dos puede ser posterior
        // a la emision del boletin: si sale posterior, se recorta.
        DateTimeOffset? flujoMedido = null;
        DateTimeOffset? aMedido = null;
        if (diaDeLosIndices is not null)
        {
            flujoMedido = Recortar(diaDeLosIndices.Value + HoraDelFlujoSolar, emitido);
            aMedido = Recortar(diaDeLosIndices.Value.AddDays(1), emitido);
        }

        var tormenta = (indiceK is not null && indiceK >= IndiceKDeTormenta) || HablaDeTormenta(texto);

        return new LecturaWwv(
            flujo,
            flujoMedido,
            indiceA,
            aMedido,
            indiceK,
            kMedido,
            tormenta,
            emitido);
    }

    /// <summary>
    /// Analiza <c>daily-solar-indices.txt</c> y devuelve la ultima fila con algun dato bueno.
    /// </summary>
    /// <param name="texto">Contenido del fichero tal cual lo sirve el SWPC.</param>
    /// <returns>La fila mas reciente utilizable, o nulo si no hay ninguna.</returns>
    public static LecturaDiariaSolar? LeerIndicesSolaresDiarios(string? texto)
    {
        foreach (var fila in FilasDeDatos(texto))
        {
            var campos = fila.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (campos.Length < 6 || !FechaDeFila(campos, out var dia))
            {
                continue;
            }

            var flujo = ValorODesaparecido(campos[3]);
            var manchas = ValorODesaparecido(campos[4]);
            if (flujo is null && manchas is null)
            {
                continue;
            }

            return new LecturaDiariaSolar(dia, flujo, manchas);
        }

        return null;
    }

    /// <summary>
    /// Analiza <c>daily-geomagnetic-indices.txt</c> y devuelve el indice A planetario de la
    /// ultima fila que lo tenga.
    /// </summary>
    /// <param name="texto">Contenido del fichero tal cual lo sirve el SWPC.</param>
    /// <returns>El dia y su indice A planetario, o nulo si no hay ninguno utilizable.</returns>
    public static LecturaInstantanea? LeerIndiceAPlanetario(string? texto)
    {
        foreach (var fila in FilasDeDatos(texto))
        {
            var campos = fila.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!FechaDeFila(campos, out var dia))
            {
                continue;
            }

            // Los indices K vienen en columnas de dos caracteres sin separador garantizado: en
            // cuanto hay huecos, el fichero escribe cosas como "0-1-1-1" y partir por espacios
            // desordena las columnas. Lo unico que no se pega nunca es el bloque final de ocho
            // Kp con decimales, asi que el indice A planetario es el campo justo anterior.
            var primerKp = Array.FindIndex(campos, 3, c => c.Contains('.', StringComparison.Ordinal));
            if (primerKp <= 3)
            {
                continue;
            }

            var indiceA = ValorODesaparecido(campos[primerKp - 1]);
            if (indiceA is null)
            {
                continue;
            }

            return new LecturaInstantanea(indiceA, dia.AddDays(1));
        }

        return null;
    }

    /// <summary>Analiza el resumen de la velocidad del viento solar.</summary>
    /// <param name="json">Cuerpo de la respuesta.</param>
    /// <returns>La velocidad en kilometros por segundo con su hora, o nulo si no se entiende.</returns>
    public static LecturaInstantanea? LeerVientoSolar(string? json) =>
        LeerResumen(json, "proton_speed");

    /// <summary>Analiza el resumen del campo magnetico interplanetario.</summary>
    /// <param name="json">Cuerpo de la respuesta.</param>
    /// <returns>La componente Bz en nanoteslas con su hora, o nulo si no se entiende.</returns>
    public static LecturaInstantanea? LeerCampoBz(string? json) => LeerResumen(json, "bz_gsm");

    /// <summary>
    /// Lee un resumen del SWPC, que siempre es una lista con un solo objeto: el valor y su hora.
    /// </summary>
    private static LecturaInstantanea? LeerResumen(string? json, string campo)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var documento = JsonDocument.Parse(json);
            var raiz = documento.RootElement;
            var objeto = raiz.ValueKind switch
            {
                JsonValueKind.Array when raiz.GetArrayLength() > 0 => raiz[0],
                JsonValueKind.Object => raiz,
                _ => default,
            };

            if (objeto.ValueKind != JsonValueKind.Object
                || !objeto.TryGetProperty("time_tag", out var hora)
                || !DateTimeOffset.TryParse(
                    hora.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var medido))
            {
                return null;
            }

            double? valor = null;
            if (objeto.TryGetProperty(campo, out var propiedad))
            {
                valor = propiedad.ValueKind switch
                {
                    JsonValueKind.Number => propiedad.GetDouble(),
                    JsonValueKind.String when double.TryParse(
                        propiedad.GetString(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var leido) => leido,
                    _ => null,
                };
            }

            return new LecturaInstantanea(valor, medido);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Filas de datos de un fichero del SWPC, de la mas reciente a la mas antigua.</summary>
    private static IEnumerable<string> FilasDeDatos(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            yield break;
        }

        var lineas = texto.Split('\n');
        for (var i = lineas.Length - 1; i >= 0; i--)
        {
            var linea = lineas[i].Trim();
            if (linea.Length == 0 || linea[0] is '#' or ':')
            {
                continue;
            }

            yield return linea;
        }
    }

    private static bool FechaDeFila(string[] campos, out DateTimeOffset dia)
    {
        dia = default;
        if (campos.Length < 3
            || !int.TryParse(campos[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var anio)
            || !int.TryParse(campos[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mes)
            || !int.TryParse(campos[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeroDia)
            || anio < 1900 || mes is < 1 or > 12 || numeroDia is < 1 or > 31)
        {
            return false;
        }

        dia = new DateTimeOffset(anio, mes, numeroDia, 0, 0, 0, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// Convierte un campo del SWPC a numero. Los huecos se marcan con -1, con 999 o con asterisco.
    /// </summary>
    private static double? ValorODesaparecido(string campo)
    {
        if (!double.TryParse(campo, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor))
        {
            return null;
        }

        return valor < 0 || valor >= 999 ? null : valor;
    }

    /// <summary>
    /// Fecha de un dia que el boletin nombra sin ano, tomando el ano de la emision y retrocediendo
    /// uno si sale mas de quince dias en el futuro, que es lo que pasa en el cambio de ano.
    /// </summary>
    private static DateTimeOffset? FechaDelBoletin(
        Match coincidencia,
        DateTimeOffset emitido,
        int diaGrupo = 1,
        int mesGrupo = 2)
    {
        if (!coincidencia.Success)
        {
            return null;
        }

        var mes = Mes(coincidencia.Groups[mesGrupo].Value);
        if (mes is null
            || !int.TryParse(
                coincidencia.Groups[diaGrupo].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var dia))
        {
            return null;
        }

        var fecha = new DateTimeOffset(emitido.Year, mes.Value, dia, 0, 0, 0, TimeSpan.Zero);
        if (fecha > emitido.AddDays(15))
        {
            fecha = fecha.AddYears(-1);
        }

        return fecha;
    }

    private static DateTimeOffset Recortar(DateTimeOffset momento, DateTimeOffset tope) =>
        momento > tope ? tope : momento;

    private static bool HablaDeTormenta(string texto)
    {
        // El boletin dice literalmente que no ha habido tormentas cuando no las ha habido; en
        // cuanto las hay las nombra por su nivel G. Se mira el nivel G, que no aparece nunca en
        // la frase negativa.
        return ExpresionNivelG().IsMatch(texto);
    }

    private static int? Mes(string nombre)
    {
        if (DateTime.TryParseExact(
                nombre,
                ["MMM", "MMMM"],
                CultureInfo.GetCultureInfo("en-US"),
                DateTimeStyles.None,
                out var fecha))
        {
            return fecha.Month;
        }

        return null;
    }

    [GeneratedRegex(@":Issued:\s*(\d{4})\s+([A-Za-z]+)\s+(\d{1,2})\s+(\d{2})(\d{2})\s*UTC", RegexOptions.IgnoreCase)]
    private static partial Regex ExpresionEmision();

    [GeneratedRegex(@"indices\s+for\s+(\d{1,2})\s+([A-Za-z]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpresionDiaDeIndices();

    [GeneratedRegex(@"Solar\s+flux\s+(\d+(?:\.\d+)?)\s+and\s+estimated\s+planetary\s+A-index\s+(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpresionFlujoYA();

    [GeneratedRegex(@"K-index\s+at\s+(\d{2})(\d{2})\s+UTC\s+on\s+(\d{1,2})\s+([A-Za-z]+)\s+was\s+(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex ExpresionIndiceK();

    [GeneratedRegex(@"\bG[1-5]\b")]
    private static partial Regex ExpresionNivelG();
}
