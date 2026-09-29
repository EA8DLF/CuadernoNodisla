using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Dominio.Pruebas.Entidades;

/// <summary>
/// Constructores rapidos para las pruebas de fusion, para que cada prueba diga solo lo suyo.
/// </summary>
internal static class AyudaDeFusion
{
    /// <summary>Instante del contacto de referencia, el mismo para las dos copias.</summary>
    public static readonly DateTimeOffset Instante = new(2024, 5, 18, 21, 14, 37, TimeSpan.Zero);

    /// <summary>Contacto minimo con la clave natural completa; lo demas queda en blanco.</summary>
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
        InicioUtc = inicioUtc ?? Instante,
    };

    /// <summary>Contacto sin nada, ni siquiera clave natural: el hueco de las pruebas de relleno.</summary>
    public static Qso Vacio() => new();

    /// <summary>Confirmacion suelta de un medio.</summary>
    public static QsoConfirmacion Confirmacion(
        MedioDeConfirmacion medio,
        EstadoDeConfirmacion enviado = EstadoDeConfirmacion.Ninguno,
        EstadoDeConfirmacion recibido = EstadoDeConfirmacion.Ninguno,
        DateTimeOffset? enviadoUtc = null,
        DateTimeOffset? recibidoUtc = null,
        ViaDeEnvio via = ViaDeEnvio.Ninguna,
        string? nota = null) => new()
        {
            Medio = medio,
            Enviado = enviado,
            Recibido = recibido,
            EnviadoUtc = enviadoUtc,
            RecibidoUtc = recibidoUtc,
            Via = via,
            Nota = nota,
        };

    /// <summary>Anade una confirmacion al contacto y devuelve el contacto, para encadenar.</summary>
    public static Qso Con(this Qso qso, QsoConfirmacion confirmacion)
    {
        ArgumentNullException.ThrowIfNull(qso);
        qso.Confirmaciones.Add(confirmacion);
        return qso;
    }

    /// <summary>Copia profunda de lo que la fusion mira, para poder fundir en los dos sentidos.</summary>
    public static Qso Clon(this Qso original)
    {
        ArgumentNullException.ThrowIfNull(original);

        var copia = new Qso
        {
            Call = original.Call,
            Band = original.Band,
            Mode = original.Mode,
            Freq = original.Freq,
            InicioUtc = original.InicioUtc,
        };

        foreach (var c in original.Confirmaciones)
        {
            copia.Confirmaciones.Add(Confirmacion(
                c.Medio, c.Enviado, c.Recibido, c.EnviadoUtc, c.RecibidoUtc, c.Via, c.Nota));
        }

        return copia;
    }

    /// <summary>Confirmaciones en forma comparable: medio, estados y fechas, ordenadas por medio.</summary>
    public static IReadOnlyList<(MedioDeConfirmacion Medio,
        EstadoDeConfirmacion Enviado,
        EstadoDeConfirmacion Recibido,
        DateTimeOffset? EnviadoUtc,
        DateTimeOffset? RecibidoUtc)> Retrato(this Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);
        return qso.Confirmaciones
            .OrderBy(c => c.Medio)
            .Select(c => (c.Medio, c.Enviado, c.Recibido, c.EnviadoUtc, c.RecibidoUtc))
            .ToList();
    }
}
