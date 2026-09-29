using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Servicios.Pruebas.Dobles;

/// <summary>Contactos de juguete para las pruebas.</summary>
public static class CuadernoDePrueba
{
    /// <summary>Crea un contacto con lo imprescindible para emparejar.</summary>
    public static Qso Contacto(
        string indicativo,
        string banda,
        string modo,
        string cuando,
        long id = 0,
        string? submodo = null)
    {
        Modo.TryParse(modo, submodo, out var m);
        Banda.TryParse(banda, out var b);
        return new Qso
        {
            Id = id,
            Call = Indicativo.Crudo(indicativo),
            Band = b,
            Mode = m,
            InicioUtc = DateTimeOffset.Parse(cuando, System.Globalization.CultureInfo.InvariantCulture)
                .ToUniversalTime(),
            StationCallsign = Indicativo.Crudo("EA8DLF"),
            RstSent = Informe.Parse("59"),
            RstRcvd = Informe.Parse("59"),
            Freq = Frecuencia.DesdeMegahercios(14.074m),
        };
    }
}
