namespace Nodisla.Cuaderno.Diplomas.Calculo;

/// <summary>
/// Reparte los modos ADIF en los tres tipos de emision que usan los diplomas: telegrafia,
/// fonia y digitales.
/// </summary>
/// <remarks>
/// Los catalogos de diplomas solo distinguen <c>CW</c>, <c>PHONE</c> y <c>DIGITAL</c>, asi que
/// los modos de imagen (SSTV, ATV, FAX) caen del lado digital, que es donde los pone tambien el
/// programa original. La clasificacion se hace sobre el <b>modo principal</b> de ADIF: el
/// submodo no cambia el tipo de emision.
/// </remarks>
public static class TipoDeEmision
{
    /// <summary>Codigo del tipo de emision de telegrafia.</summary>
    public const string Cw = "CW";

    /// <summary>Codigo del tipo de emision de fonia.</summary>
    public const string Fonia = "PHONE";

    /// <summary>Codigo del tipo de emision digital.</summary>
    public const string Digital = "DIGITAL";

    /// <summary>Modos principales de ADIF que cuentan como telegrafia.</summary>
    public static IReadOnlyList<string> ModosDeCw { get; } = ["CW"];

    /// <summary>Modos principales de ADIF que cuentan como fonia.</summary>
    public static IReadOnlyList<string> ModosDeFonia { get; } =
        ["AM", "FM", "SSB", "DIGITALVOICE", "VOI"];

    /// <summary>Dice si el codigo es uno de los tres tipos de emision conocidos.</summary>
    /// <param name="codigo">Codigo a comprobar.</param>
    /// <returns>Cierto si el motor sabe traducirlo a una condicion sobre el modo.</returns>
    public static bool EsConocido(string? codigo) =>
        codigo is not null &&
        (codigo.Equals(Cw, StringComparison.OrdinalIgnoreCase) ||
         codigo.Equals(Fonia, StringComparison.OrdinalIgnoreCase) ||
         codigo.Equals(Digital, StringComparison.OrdinalIgnoreCase));

    /// <summary>Tipo de emision de un modo principal de ADIF.</summary>
    /// <param name="modoPrincipal">Modo principal, por ejemplo <c>SSB</c>.</param>
    /// <returns><c>CW</c>, <c>PHONE</c> o <c>DIGITAL</c>.</returns>
    public static string De(string? modoPrincipal)
    {
        if (string.IsNullOrWhiteSpace(modoPrincipal)) return Digital;
        foreach (var m in ModosDeCw)
        {
            if (m.Equals(modoPrincipal, StringComparison.OrdinalIgnoreCase)) return Cw;
        }
        foreach (var m in ModosDeFonia)
        {
            if (m.Equals(modoPrincipal, StringComparison.OrdinalIgnoreCase)) return Fonia;
        }
        return Digital;
    }
}
