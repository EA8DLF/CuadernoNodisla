using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Pruebas;

/// <summary>Constructores rapidos de datos de prueba, para que cada prueba diga solo lo suyo.</summary>
internal static class Ayuda
{
    /// <summary>Perfil de estacion tipico de EA8DLF.</summary>
    public static Estacion Estacion(string nombrePerfil = "Casa", bool predeterminado = true) => new()
    {
        NombrePerfil = nombrePerfil,
        StationCallsign = Indicativo.Parse("EA8DLF"),
        Operator = "EA8DLF",
        MyGridsquare = Locator.Parse("IL18QK"),
        MyCountry = "Canary Islands",
        Predeterminado = predeterminado,
    };

    /// <summary>Contacto minimo valido; cada prueba ajusta lo que le interesa.</summary>
    public static Qso Qso(
        string call = "EA1ABC",
        string modo = "SSB",
        string banda = "20m",
        decimal mhz = 14.2m,
        DateTimeOffset? inicioUtc = null) => new()
    {
        Call = Indicativo.Parse(call),
        Mode = Modo.Parse(modo),
        Band = Banda.Parse(banda),
        Freq = Frecuencia.DesdeMegahercios(mhz),
        InicioUtc = inicioUtc ?? new DateTimeOffset(2026, 3, 12, 18, 30, 0, TimeSpan.Zero),
    };

    /// <summary>Confirmacion suelta de un medio, para las pruebas de fusion.</summary>
    public static QsoConfirmacion Confirmacion(
        MedioDeConfirmacion medio,
        EstadoDeConfirmacion enviado = EstadoDeConfirmacion.Ninguno,
        EstadoDeConfirmacion recibido = EstadoDeConfirmacion.Ninguno,
        DateTimeOffset? enviadoUtc = null,
        DateTimeOffset? recibidoUtc = null) => new()
        {
            Medio = medio,
            Enviado = enviado,
            Recibido = recibido,
            EnviadoUtc = enviadoUtc,
            RecibidoUtc = recibidoUtc,
        };

    /// <summary>Anade una confirmacion al contacto y devuelve el contacto, para encadenar.</summary>
    public static Qso Con(this Qso qso, QsoConfirmacion confirmacion)
    {
        ArgumentNullException.ThrowIfNull(qso);
        qso.Confirmaciones.Add(confirmacion);
        return qso;
    }
}
