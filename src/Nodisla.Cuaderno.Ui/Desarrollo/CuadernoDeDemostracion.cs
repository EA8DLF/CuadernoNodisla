using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Genera un cuaderno de mentira con decenas de miles de contactos. Sirve para comprobar que
/// la rejilla sigue yendo fluida y que la paginacion y los filtros hacen su trabajo.
/// </summary>
public static class CuadernoDeDemostracion
{
    private static readonly string[] Prefijos =
    [
        "EA1", "EA2", "EA3", "EA5", "EA7", "EA8", "EB8", "F5", "F6", "DL1", "DL5", "DJ2", "G3", "G4", "M0",
        "I2", "IK4", "IZ8", "PA3", "ON4", "OK1", "OK2", "SP5", "SM0", "LA9", "OH2", "OZ1", "S57", "9A2",
        "K1", "K5", "W3", "W9", "N4", "KB8", "VE3", "VE7", "PY2", "LU5", "CE3", "JA1", "JH7", "VK2", "VK6",
        "ZS6", "CT1", "CT3", "CU2", "EI4", "GM0", "GW4", "UA3", "UR5", "YO3", "LZ1", "SV1", "TA1", "4X4",
    ];

    private static readonly string[] Sufijos =
    [
        "ABC", "ADX", "AFG", "BCD", "BHK", "CDE", "CQR", "DEF", "DLF", "EFG", "FGH", "GHI", "HIJ", "IJK",
        "JKL", "KLM", "LMN", "MNO", "NOP", "OPQ", "PQR", "QRS", "RST", "STU", "TUV", "UVW", "VWX", "WXY",
    ];

    private static readonly string[] Nombres =
    [
        "Antonio", "Manuel", "Pedro", "Lucía", "Carmen", "Hans", "Klaus", "Pierre", "Marie", "John",
        "Peter", "Giulia", "Marco", "Jan", "Piotr", "Olav", "Mika", "Sergey", "Takeshi", "Bruce",
    ];

    private static readonly string[] Localidades =
    [
        "Santa Cruz", "Madrid", "Barcelona", "Sevilla", "Bilbao", "Múnich", "Berlín", "París", "Lyon",
        "Londres", "Mánchester", "Roma", "Milán", "Ámsterdam", "Varsovia", "Oslo", "Helsinki", "Moscú",
        "Tokio", "Sídney", "Nueva York", "Chicago", "Toronto", "São Paulo", "Buenos Aires", "Ciudad del Cabo",
    ];

    private static readonly (string Banda, decimal Inicio, decimal Fin)[] Trozos =
    [
        ("160m", 1.81m, 1.99m),
        ("80m", 3.51m, 3.79m),
        ("40m", 7.01m, 7.19m),
        ("30m", 10.11m, 10.14m),
        ("20m", 14.01m, 14.34m),
        ("17m", 18.07m, 18.16m),
        ("15m", 21.01m, 21.44m),
        ("12m", 24.90m, 24.98m),
        ("10m", 28.01m, 29.60m),
        ("6m", 50.05m, 51.00m),
        ("2m", 144.05m, 145.50m),
        ("70cm", 432.10m, 433.50m),
    ];

    private static readonly string[] Modos = ["SSB", "CW", "FT8", "FT4", "RTTY", "PSK31", "FM", "JS8", "SSTV"];

    /// <summary>Construye los contactos de demostracion. La semilla fija hace que siempre salgan iguales.</summary>
    public static IReadOnlyList<Qso> Generar(int cuantos = 20_000, int semilla = 8_1968)
    {
        var azar = new Random(semilla);
        var lista = new List<Qso>(cuantos);
        var instante = DateTimeOffset.UtcNow.AddMinutes(-10);

        for (var i = 0; i < cuantos; i++)
        {
            var (banda, inicio, fin) = Trozos[azar.Next(Trozos.Length)];
            var mhz = decimal.Round(inicio + ((fin - inicio) * (decimal)azar.NextDouble()), 5);
            var modo = Modos[azar.Next(Modos.Length)];
            var call = $"{Prefijos[azar.Next(Prefijos.Length)]}{Sufijos[azar.Next(Sufijos.Length)]}";
            var m = Modo.Parse(modo);

            instante = instante.AddSeconds(-azar.Next(120, 2400));

            var qso = new Qso
            {
                Call = Indicativo.Crudo(call),
                Band = Banda.Parse(banda),
                Mode = m,
                Freq = Frecuencia.DesdeMegahercios(mhz),
                InicioUtc = instante,
                FinUtc = instante.AddMinutes(azar.Next(1, 12)),
                RstSent = InformeDe(m, azar),
                RstRcvd = InformeDe(m, azar),
                Name = Nombres[azar.Next(Nombres.Length)],
                Qth = Localidades[azar.Next(Localidades.Length)],
                Gridsquare = DondeCae(Indicativo.Crudo(call), instante, azar),
                StationCallsign = Indicativo.Parse("EA8DLF"),
                Operator = "EA8DLF",
                MyGridsquare = Locator.Parse("IL18SN"),
                EstacionId = 1,
                TxPwr = 100,
                Origen = "demostración",
                Comentario = azar.Next(5) == 0 ? "Buena señal, QSB suave." : null,
            };

            if (azar.Next(3) == 0)
            {
                qso.Confirmaciones.Add(new QsoConfirmacion
                {
                    Medio = MedioDeConfirmacion.Lotw,
                    Recibido = EstadoDeConfirmacion.Confirmado,
                    RecibidoUtc = instante.AddDays(azar.Next(1, 60)),
                });
            }

            lista.Add(qso);
        }

        return lista;
    }

    /// <summary>
    /// Localizador del corresponsal, sacado de la entidad DXCC de su indicativo.
    /// </summary>
    /// <remarks>
    /// Repartir los contactos al azar por todo el globo daba un cuaderno imposible —la mitad
    /// de los contactos en mitad del oceano— y, sobre el mapa, una reja de puntos que no
    /// decia nada. Situar cada indicativo en su pais, con unos grados de dispersion, da un
    /// cuaderno que se parece al de verdad: apelotonado en Europa, disperso en el Pacifico.
    /// Es tambien lo que pone a prueba la agrupacion del mapa, que es para lo que existe.
    /// </remarks>
    private static Locator DondeCae(Indicativo indicativo, DateTimeOffset cuando, Random azar)
    {
        var entidad = Resolutor
            .Resolver(indicativo, DateOnly.FromDateTime(cuando.UtcDateTime))
            .Entidad;

        if (entidad is null) return Locator.Vacio;

        // Dos grados largos de dispersion alrededor del centro del pais: lo justo para que
        // dos estaciones del mismo prefijo no caigan exactamente en el mismo punto.
        var latitud = Math.Clamp(entidad.Latitud + ((azar.NextDouble() - 0.5) * 4.0), -89, 89);
        var longitud = entidad.Longitud + ((azar.NextDouble() - 0.5) * 6.0);

        if (longitud > 180) longitud -= 360;
        if (longitud < -180) longitud += 360;

        return Locator.DesdeCoordenadas(latitud, longitud);
    }

    private static readonly ResolutorDxcc Resolutor = ResolutorDxcc.Predeterminado;

    private static Informe InformeDe(Modo modo, Random azar) => modo.UsaInformeEnDecibelios
        ? Informe.DesdeDecibelios(azar.Next(-24, 12))
        : Informe.PorOmisionPara(modo);
}
