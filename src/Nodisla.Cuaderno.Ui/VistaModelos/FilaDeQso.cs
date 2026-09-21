using System.Globalization;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Una fila del cuaderno, ya formateada para la rejilla. La rejilla no toca el dominio: asi
/// las celdas no calculan nada al pintarse y el desplazamiento no se resiente.
/// </summary>
public sealed class FilaDeQso
{
    /// <summary>Crea la fila a partir del contacto.</summary>
    public FilaDeQso(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);
        Qso = qso;

        var inicio = qso.InicioUtc.UtcDateTime;
        Fecha = inicio.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
        Hora = inicio.ToString("HH:mm", CultureInfo.InvariantCulture);
        Indicativo = qso.Call.Valor;
        Banda = qso.Band.Nombre;
        Modo = qso.Mode.NombreUsual;
        // La frecuencia va siempre con punto decimal, no con el de la cultura espanola.
        Frecuencia = TextoDeFrecuencia.Escribir(qso.Freq);
        InformeEnviado = qso.RstSent.Texto;
        InformeRecibido = qso.RstRcvd.Texto;
        Nombre = qso.Name ?? string.Empty;
        Qth = qso.Qth ?? string.Empty;
        Localizador = qso.Gridsquare.Valor;
        Comentario = qso.Comentario ?? string.Empty;
        Estacion = qso.StationCallsign.Valor;

        // Si el corresponsal declaro una distancia en el fichero, esa manda sobre la calculada.
        Distancia = (qso.Distance ?? qso.DistanciaKm) is { } km
            ? km.ToString("N0", CultureInfo.CurrentCulture) + " km"
            : string.Empty;

        Confirmado = qso.Confirmaciones.Any(c => c.EstaVerificada) ? "Verificado"
            : qso.Confirmaciones.Any(c => c.EstaConfirmada) ? "Sí"
            : string.Empty;

        Antena = FormatoDeAntena(qso);
        Propagacion = FormatoDePropagacion(qso);
        Swl = qso.Swl ? "Sí" : string.Empty;
        Completado = TextoDeCompletado(qso.QsoComplete);
        Casual = qso.QsoRandom is { } casual ? (casual ? "Sí" : "No") : string.Empty;
        MensajeQsl = qso.QslMsg ?? string.Empty;
        MiNombre = qso.MyName ?? string.Empty;
        Iota = qso.IotaIslandId ?? string.Empty;
    }

    /// <summary>Contacto del dominio que hay detras de la fila.</summary>
    public Qso Qso { get; }

    /// <summary>Identificador del contacto.</summary>
    public long Id => Qso.Id;

    /// <summary>Fecha del contacto en UTC.</summary>
    public string Fecha { get; }

    /// <summary>Hora del contacto en UTC.</summary>
    public string Hora { get; }

    /// <summary>Indicativo del corresponsal.</summary>
    public string Indicativo { get; }

    /// <summary>Banda del contacto.</summary>
    public string Banda { get; }

    /// <summary>Modo del contacto, con el nombre que usa el operador.</summary>
    public string Modo { get; }

    /// <summary>Frecuencia en megahercios.</summary>
    public string Frecuencia { get; }

    /// <summary>Informe enviado.</summary>
    public string InformeEnviado { get; }

    /// <summary>Informe recibido.</summary>
    public string InformeRecibido { get; }

    /// <summary>Nombre del corresponsal.</summary>
    public string Nombre { get; }

    /// <summary>Localidad del corresponsal.</summary>
    public string Qth { get; }

    /// <summary>Localizador del corresponsal.</summary>
    public string Localizador { get; }

    /// <summary>Comentario del contacto.</summary>
    public string Comentario { get; }

    /// <summary>Indicativo con el que se transmitio.</summary>
    public string Estacion { get; }

    /// <summary>Distancia del contacto.</summary>
    public string Distancia { get; }

    /// <summary>Marca si el contacto esta confirmado o verificado por alguna via.</summary>
    public string Confirmado { get; }

    /// <summary>Azimut y elevacion de la antena, cuando se anotaron.</summary>
    public string Antena { get; }

    /// <summary>Indices de propagacion del momento: A, K y flujo solar.</summary>
    public string Propagacion { get; }

    /// <summary>Marca los contactos con radioescuchas.</summary>
    public string Swl { get; }

    /// <summary>Si el contacto se llego a completar.</summary>
    public string Completado { get; }

    /// <summary>Si el contacto fue casual y no concertado.</summary>
    public string Casual { get; }

    /// <summary>Mensaje que se imprime en la tarjeta QSL.</summary>
    public string MensajeQsl { get; }

    /// <summary>Mi nombre tal y como se declaro en el contacto.</summary>
    public string MiNombre { get; }

    /// <summary>Referencia de isla IOTA del corresponsal.</summary>
    public string Iota { get; }

    private static string FormatoDeAntena(Qso qso)
    {
        var azimut = qso.AntAz ?? qso.RumboGrados;
        if (azimut is null && qso.AntEl is null) return string.Empty;

        var partes = new List<string>(2);
        if (azimut is { } az) partes.Add($"{az.ToString("N0", CultureInfo.CurrentCulture)}°");
        if (qso.AntEl is { } el) partes.Add($"el. {el.ToString("N0", CultureInfo.CurrentCulture)}°");
        return string.Join(" · ", partes);
    }

    private static string FormatoDePropagacion(Qso qso)
    {
        var partes = new List<string>(3);
        if (qso.AIndex is { } a) partes.Add($"A {a.ToString("N0", CultureInfo.CurrentCulture)}");
        if (qso.KIndex is { } k) partes.Add($"K {k.ToString("N0", CultureInfo.CurrentCulture)}");
        if (qso.Sfi is { } sfi) partes.Add($"SFI {sfi.ToString("N0", CultureInfo.CurrentCulture)}");
        return string.Join(" · ", partes);
    }

    /// <summary>Traduce el codigo <c>QSO_COMPLETE</c> de ADIF, que no es un si o un no.</summary>
    private static string TextoDeCompletado(string? codigo) => codigo?.Trim().ToUpperInvariant() switch
    {
        "Y" => "Sí",
        "N" => "No",
        "NIL" => "No hubo",
        "?" => "Dudoso",
        null or "" => string.Empty,
        _ => codigo!,
    };
}
