using System.Globalization;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Servicios.Adif;

/// <summary>
/// Saca de un registro ADIF descargado la clave con la que se busca el contacto en el cuaderno.
/// </summary>
/// <remarks>
/// Los servicios devuelven mucho mas de lo que hace falta para emparejar. Lo unico
/// imprescindible es indicativo, banda, modo e instante; el resto se pasa como campos extra
/// para que el operador lo vea, pero <b>no se usa para decidir</b>.
/// </remarks>
public static class LectorDeConfirmaciones
{
    /// <summary>Clave de un registro descargado.</summary>
    /// <param name="Call">Indicativo del corresponsal.</param>
    /// <param name="Band">Banda, tomada de <c>BAND</c> o deducida de <c>FREQ</c>.</param>
    /// <param name="Mode">Modo tal y como lo escribe el servicio, sin normalizar.</param>
    /// <param name="InicioUtc">Instante del contacto en UTC.</param>
    public readonly record struct ClaveDescargada(
        Indicativo Call, Banda Band, string Mode, DateTimeOffset InicioUtc);

    /// <summary>Lee la clave del registro. Devuelve falso si le falta algo imprescindible.</summary>
    /// <param name="registro">Campos del registro ADIF descargado.</param>
    /// <param name="clave">Clave leida.</param>
    public static bool TryLeerClave(
        IReadOnlyDictionary<string, string> registro, out ClaveDescargada clave)
    {
        ArgumentNullException.ThrowIfNull(registro);
        clave = default;

        var textoCall = Campo(registro, "CALL");
        if (string.IsNullOrWhiteSpace(textoCall)) return false;
        // Crudo y no Parse: un indicativo raro del servicio no puede perder la confirmacion.
        var call = Indicativo.Crudo(textoCall);
        if (call.EsVacio) return false;

        if (FechaHora(Campo(registro, "QSO_DATE"), Campo(registro, "TIME_ON")) is not { } inicio)
        {
            return false;
        }

        var band = Banda.Vacia;
        if (Banda.TryParse(Campo(registro, "BAND"), out var leida)) band = leida;
        else if (Frecuencia.TryParseAdif(Campo(registro, "FREQ"), out var f)) band = Banda.DesdeFrecuencia(f);

        var submodo = Campo(registro, "SUBMODE");
        var modo = Campo(registro, "MODE") ?? string.Empty;
        var textoModo = !string.IsNullOrWhiteSpace(submodo) ? submodo! : modo;

        clave = new ClaveDescargada(call, band, textoModo.Trim().ToUpperInvariant(), inicio);
        return true;
    }

    /// <summary>Compone un instante UTC a partir de los campos ADIF de fecha y hora.</summary>
    /// <param name="fecha">Campo <c>QSO_DATE</c>, con forma <c>AAAAMMDD</c>.</param>
    /// <param name="hora">Campo <c>TIME_ON</c>, con forma <c>HHMM</c> o <c>HHMMSS</c>.</param>
    public static DateTimeOffset? FechaHora(string? fecha, string? hora)
    {
        if (string.IsNullOrWhiteSpace(fecha)) return null;
        var f = fecha.Trim();
        if (f.Length != 8
            || !DateTime.TryParseExact(f, "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dia))
        {
            return null;
        }

        var h = (hora ?? string.Empty).Trim();
        var segundos = h.Length switch
        {
            6 when int.TryParse(h[..2], out var hh) && int.TryParse(h[2..4], out var mm)
                   && int.TryParse(h[4..6], out var ss) => (hh * 3600) + (mm * 60) + ss,
            4 when int.TryParse(h[..2], out var hh) && int.TryParse(h[2..4], out var mm)
                   => (hh * 3600) + (mm * 60),
            _ => 0,
        };

        return new DateTimeOffset(dia.AddSeconds(segundos), TimeSpan.Zero);
    }

    /// <summary>Lee una fecha y hora de confirmacion de los campos que use cada servicio.</summary>
    /// <param name="registro">Campos del registro.</param>
    /// <param name="campoFecha">Campo con la fecha, en forma <c>AAAAMMDD</c>.</param>
    /// <param name="campoHora">Campo con la hora, opcional.</param>
    public static DateTimeOffset? FechaDeConfirmacion(
        IReadOnlyDictionary<string, string> registro, string campoFecha, string? campoHora = null)
    {
        ArgumentNullException.ThrowIfNull(registro);
        var hora = campoHora is null ? null : Campo(registro, campoHora);
        return FechaHora(Campo(registro, campoFecha), hora);
    }

    /// <summary>Recoge los campos indicados, si el registro los trae, para ensenarselos al operador.</summary>
    /// <param name="registro">Campos del registro.</param>
    /// <param name="campos">Campos que interesan.</param>
    public static IReadOnlyDictionary<string, string> CamposExtra(
        IReadOnlyDictionary<string, string> registro, IReadOnlyCollection<string> campos)
    {
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(campos);

        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var campo in campos)
        {
            var valor = Campo(registro, campo);
            if (!string.IsNullOrWhiteSpace(valor)) extra[campo] = valor.Trim();
        }
        return extra;
    }

    /// <summary>Devuelve el valor de un campo, o nulo si no esta.</summary>
    /// <param name="registro">Campos del registro.</param>
    /// <param name="campo">Nombre del campo.</param>
    public static string? Campo(IReadOnlyDictionary<string, string> registro, string campo)
    {
        ArgumentNullException.ThrowIfNull(registro);
        return registro.TryGetValue(campo, out var valor) ? valor : null;
    }

    /// <summary>Indica si un campo ADIF de si o no vale «si».</summary>
    /// <param name="registro">Campos del registro.</param>
    /// <param name="campo">Nombre del campo.</param>
    public static bool EsSi(IReadOnlyDictionary<string, string> registro, string campo)
    {
        var valor = Campo(registro, campo);
        return !string.IsNullOrEmpty(valor)
            && (valor[0] is 'Y' or 'y' or 'V' or 'v');
    }
}
