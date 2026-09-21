using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>Como viene el valor de un ajuste del menu interno.</summary>
public enum FormaDeValorDeMenu
{
    /// <summary>Numero con signo, como <c>+00</c>.</summary>
    ConSigno,

    /// <summary>Numero sin signo, como <c>0300</c>.</summary>
    Numero,

    /// <summary>Texto con relleno de espacios, como <c>FT-710      </c>.</summary>
    Texto,
}

/// <summary>Un ajuste del menu interno del equipo.</summary>
/// <param name="Indice">Indice de seis cifras, por ejemplo <c>010101</c>.</param>
/// <param name="Forma">Como viene el valor.</param>
/// <param name="Longitud">Cuantos caracteres ocupa el valor.</param>
/// <param name="ValorEnLaCaptura">Valor que tenia la estacion cuando se mapeo el menu.</param>
public sealed record EntradaDeMenuFt710(
    string Indice,
    FormaDeValorDeMenu Forma,
    int Longitud,
    string ValorEnLaCaptura);

/// <summary>Lo leido de un ajuste del menu.</summary>
/// <param name="Indice">Indice del ajuste.</param>
/// <param name="Valor">Valor tal cual, sin recortar.</param>
/// <param name="Forma">Como venia el valor.</param>
public sealed record LecturaDeMenu(string Indice, string Valor, FormaDeValorDeMenu Forma)
{
    /// <summary>Valor como texto, sin el relleno de espacios.</summary>
    public string Texto => Valor.Trim();

    /// <summary>Valor como numero, si lo es.</summary>
    public int? Numero =>
        int.TryParse(Valor.Trim(), NumberStyles.Integer | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)
            ? n
            : null;
}

/// <summary>
/// El menu interno del FT-710, que es por donde se llega a los ajustes sin orden propia.
/// </summary>
/// <remarks>
/// El mapa viene del recurso <c>Recursos/menu-ft710.json</c>, que se leyo del FT-710 de EA8DLF
/// el 21 de septiembre de 2026 por COM3 a 115200, recorriendo el menu con ordenes <c>EX</c> de
/// <b>solo lectura</b>: no se escribio nada en el equipo. Ninguna entrada esta deducida, aunque
/// el barrido se podo y puede faltar alguna; <see cref="Procedencia"/> lo cuenta entero. El
/// equipo no dice como se llama cada ajuste, solo su indice y su valor: los nombres hay que
/// ponerlos a mano desde el manual.
/// </remarks>
public static class MenuFt710
{
    private const string NombreDelRecurso = "Nodisla.Cuaderno.Radio.Recursos.menu-ft710.json";

    private static readonly Lazy<MapaDeMenuFt710?> DocumentoPerezoso = new(Cargar);

    /// <summary>Ajustes del menu que se encontraron al mapearlo.</summary>
    public static IReadOnlyList<EntradaDeMenuFt710> Mapa => DocumentoPerezoso.Value?.Entradas ?? [];

    /// <summary>
    /// De donde sale el mapa: si se leyo de un equipo de verdad, cuando, por que puerto y con
    /// que metodo.
    /// </summary>
    /// <remarks>
    /// Va con el mapa a proposito. Un mapa de ordenes sin decir de donde sale no se distingue
    /// de una suposicion, y una suposicion no se le manda a un equipo de radio.
    /// </remarks>
    public static MapaDeMenuFt710? Procedencia => DocumentoPerezoso.Value;

    /// <summary>Orden para leer un ajuste del menu.</summary>
    /// <param name="indice">Indice de seis cifras.</param>
    /// <returns>La orden lista para mandar.</returns>
    /// <exception cref="ArgumentException">Si el indice no son seis cifras.</exception>
    public static string OrdenDeLectura(string indice)
    {
        if (indice is not { Length: 6 } || !indice.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("El índice del menú son seis cifras, por ejemplo 010101.", nameof(indice));
        }

        return $"EX{indice}{Cat.Fin}";
    }

    /// <summary>
    /// Analiza la respuesta de un <c>EX</c>, con las tres formas que usa el equipo.
    /// </summary>
    /// <param name="respuesta">Respuesta sin el punto y coma.</param>
    /// <param name="lectura">Lo leido, si la respuesta valia.</param>
    /// <returns>Verdadero si se pudo analizar.</returns>
    public static bool TryAnalizar(string? respuesta, out LecturaDeMenu? lectura)
    {
        lectura = null;
        if (OrdenesFt710.DiceQueNoLoAdmite(respuesta))
        {
            return false;
        }

        var texto = respuesta!;
        if (texto.Length < 9 || !texto.StartsWith("EX", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var indice = texto.Substring(2, 6);
        if (!indice.All(char.IsAsciiDigit))
        {
            return false;
        }

        var valor = texto[8..];
        var forma = Adivinar(valor);
        lectura = new LecturaDeMenu(indice, valor, forma);
        return true;
    }

    /// <summary>Decide de que forma es un valor del menu.</summary>
    /// <param name="valor">Valor tal cual lo manda el equipo.</param>
    /// <returns>La forma del valor.</returns>
    public static FormaDeValorDeMenu Adivinar(string valor)
    {
        if (valor.Length > 0 && (valor[0] == '+' || valor[0] == '-'))
        {
            return FormaDeValorDeMenu.ConSigno;
        }

        var recortado = valor.Trim();
        return recortado.Length > 0 && recortado.All(char.IsAsciiDigit)
            ? FormaDeValorDeMenu.Numero
            : FormaDeValorDeMenu.Texto;
    }

    private static MapaDeMenuFt710? Cargar()
    {
        using var flujo = typeof(MenuFt710).GetTypeInfo().Assembly.GetManifestResourceStream(NombreDelRecurso);
        return flujo is null ? null : JsonSerializer.Deserialize<MapaDeMenuFt710>(flujo, OpcionesDeLectura);
    }

    private static readonly JsonSerializerOptions OpcionesDeLectura = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>Como se guarda el mapa del menu en el recurso.</summary>
/// <param name="Equipo">Equipo del que se saco.</param>
/// <param name="Identificador">Lo que contesta a <c>ID;</c>.</param>
/// <param name="LeidoDelEquipoReal">
/// El mapa se leyo de un equipo de verdad. Si esto fuera falso, el mapa serian suposiciones y
/// no se podria usar para nada serio.
/// </param>
/// <param name="NingunaEntradaEsDeducida">Ninguna entrada esta deducida: todas se leyeron.</param>
/// <param name="FechaDeLectura">Dia en que se leyo.</param>
/// <param name="Estacion">Estacion cuyo equipo se leyo.</param>
/// <param name="Puerto">Puerto y velocidad por los que se leyo.</param>
/// <param name="Firmware">Lo que se sabe del firmware del equipo.</param>
/// <param name="Metodo">Como se leyo.</param>
/// <param name="Cobertura">Hasta donde llego el barrido y que puede faltar.</param>
/// <param name="Entradas">Ajustes encontrados.</param>
public sealed record MapaDeMenuFt710(
    string Equipo,
    string Identificador,
    bool LeidoDelEquipoReal,
    bool NingunaEntradaEsDeducida,
    string FechaDeLectura,
    string Estacion,
    string Puerto,
    string Firmware,
    string Metodo,
    string Cobertura,
    IReadOnlyList<EntradaDeMenuFt710> Entradas);
