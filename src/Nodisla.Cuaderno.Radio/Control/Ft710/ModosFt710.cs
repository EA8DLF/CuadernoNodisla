namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>
/// Modos del FT-710 tal y como viajan en la orden <c>MD</c>.
/// </summary>
/// <remarks>
/// El codigo es un solo caracter. De la captura del equipo del operador esta confirmado que
/// <c>MD02</c> es banda lateral superior; el resto sigue el juego de ordenes de Yaesu, que es
/// comun a toda la familia.
/// </remarks>
public static class ModosFt710
{
    private static readonly Dictionary<char, string> DelEquipo = new()
    {
        ['1'] = "LSB",
        ['2'] = "USB",
        ['3'] = "CW",
        ['4'] = "FM",
        ['5'] = "AM",
        ['6'] = "RTTY",
        ['7'] = "CW",
        ['8'] = "PKTLSB",
        ['9'] = "RTTY",
        ['A'] = "PKTFM",
        ['B'] = "FM",
        ['C'] = "PKTUSB",
        ['D'] = "AM",
        ['E'] = "PSK",
        ['F'] = "PKTFM",
    };

    private static readonly Dictionary<string, char> AlEquipoPorNombre = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LSB"] = '1',
        ["USB"] = '2',
        ["CW"] = '3',
        ["FM"] = '4',
        ["AM"] = '5',
        ["RTTY"] = '9',
        ["PKTLSB"] = '8',
        ["PKTUSB"] = 'C',
        ["PKTFM"] = 'A',
        ["PSK"] = 'E',
    };

    /// <summary>Pasa el codigo del equipo al nombre de modo al estilo Hamlib.</summary>
    /// <param name="codigo">Caracter que manda el equipo en <c>MD</c>.</param>
    /// <returns>El nombre, o nulo si el codigo no se conoce.</returns>
    public static string? DesdeElEquipo(char codigo) =>
        DelEquipo.TryGetValue(char.ToUpperInvariant(codigo), out var nombre) ? nombre : null;

    /// <summary>Pasa el nombre del modo al codigo que espera el equipo.</summary>
    /// <param name="nombre">Nombre al estilo Hamlib, por ejemplo <c>USB</c>.</param>
    /// <returns>El codigo, o nulo si el equipo no tiene ese modo.</returns>
    public static char? AlEquipo(string nombre) =>
        AlEquipoPorNombre.TryGetValue(nombre, out var codigo) ? codigo : null;
}
