namespace Nodisla.Cuaderno.Radio.Control.Icom;

/// <summary>
/// Codigos de modo CI-V (ordenes <c>01</c>, <c>04</c>, <c>06</c>, <c>26</c>) y su nombre al estilo Hamlib.
/// </summary>
/// <remarks>
/// Tabla «Operating mode» de los manuales: 00 LSB, 01 USB, 02 AM, 03 CW, 04 RTTY, 05 FM, 06 WFM,
/// 07 CW-R, 08 RTTY-R, 12 PSK, 13 PSK-R (IC-7610/IC-7851), 17 DV (IC-705/IC-9700/IC-7100), 22 DD
/// (IC-9700). El modo de datos (USB-D...) no es un codigo aparte: es <c>1A 06</c> encendido.
/// </remarks>
public static class ModosIcom
{
    private static readonly (byte Codigo, string Nombre)[] Tabla =
    [
        (0x00, "LSB"), (0x01, "USB"), (0x02, "AM"), (0x03, "CW"), (0x04, "RTTY"), (0x05, "FM"),
        (0x06, "WFM"), (0x07, "CWR"), (0x08, "RTTYR"), (0x12, "PSK"), (0x13, "PSKR"),
        (0x17, "DSTAR"), (0x22, "DD"),
    ];

    /// <summary>Nombre del modo a partir del codigo y del modo de datos.</summary>
    /// <param name="codigo">Codigo CI-V.</param>
    /// <param name="datos">El modo de datos (<c>1A 06</c>) esta encendido.</param>
    /// <returns>El nombre (<c>USB</c>, <c>PKTUSB</c>...), o nulo si el codigo no se conoce.</returns>
    public static string? DesdeElEquipo(byte codigo, bool datos)
    {
        var nombre = Tabla.FirstOrDefault(t => t.Codigo == codigo).Nombre;
        if (nombre is null) return null;
        return datos && nombre is "LSB" or "USB" or "AM" or "FM" ? "PKT" + nombre : nombre;
    }

    /// <summary>Codigo CI-V y modo de datos para un nombre de modo.</summary>
    /// <param name="nombre">Nombre (<c>USB</c>, <c>PKTUSB</c>, <c>CW</c>...).</param>
    /// <returns>El codigo y si va con datos, o nulo si el equipo no lo tiene.</returns>
    public static (byte Codigo, bool Datos)? AlEquipo(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        var limpio = nombre.Trim().ToUpperInvariant();
        var datos = false;
        if (limpio.StartsWith("PKT", StringComparison.Ordinal))
        {
            datos = true;
            limpio = limpio[3..];
        }
        else if (limpio is "DATA-U" or "DIGU")
        {
            (datos, limpio) = (true, "USB");
        }
        else if (limpio is "DATA-L" or "DIGL")
        {
            (datos, limpio) = (true, "LSB");
        }

        limpio = limpio switch
        {
            "CW-R" => "CWR",
            "RTTY-R" => "RTTYR",
            "DV" => "DSTAR",
            _ => limpio,
        };

        foreach (var (codigo, n) in Tabla)
        {
            if (n == limpio) return (codigo, datos);
        }

        return null;
    }
}
