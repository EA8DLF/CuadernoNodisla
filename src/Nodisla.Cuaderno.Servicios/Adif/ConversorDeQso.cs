using System.Globalization;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Servicios.Adif;

/// <summary>
/// Convierte un contacto del cuaderno en campos ADIF, para enviarlo a un servicio.
/// </summary>
/// <remarks>
/// La conversion se hace siempre contra una <b>lista blanca de campos</b> y nunca volcando el
/// contacto entero. eQSL rechaza el registro completo en cuanto ve un campo que no conoce, y
/// Club Log y QRZ tampoco ganan nada con los campos internos del cuaderno. Cada servicio
/// declara su lista y esta clase solo rellena los que sabe calcular.
/// </remarks>
public static class ConversorDeQso
{
    /// <summary>Proyecta el contacto sobre los campos que el servicio acepta.</summary>
    /// <param name="qso">Contacto del cuaderno.</param>
    /// <param name="campos">Campos ADIF admitidos por el servicio, en mayusculas.</param>
    public static IReadOnlyDictionary<string, string> Proyectar(
        Qso qso, IReadOnlyCollection<string> campos)
    {
        ArgumentNullException.ThrowIfNull(qso);
        ArgumentNullException.ThrowIfNull(campos);

        var todos = Todos(qso);
        var salida = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var campo in campos)
        {
            if (todos.TryGetValue(campo, out var valor) && !string.IsNullOrEmpty(valor))
            {
                salida[campo] = valor;
            }
        }
        return salida;
    }

    /// <summary>
    /// Todos los campos ADIF que se saben deducir del contacto. Es el conjunto del que cada
    /// servicio elige; nadie lo manda entero.
    /// </summary>
    /// <param name="qso">Contacto del cuaderno.</param>
    public static IReadOnlyDictionary<string, string> Todos(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);
        var inicio = qso.InicioUtc.UtcDateTime;
        var c = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CALL"] = qso.Call.Valor,
            ["QSO_DATE"] = inicio.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            ["TIME_ON"] = inicio.ToString("HHmmss", CultureInfo.InvariantCulture),
        };

        if (qso.FinUtc is { } fin)
        {
            c["QSO_DATE_OFF"] = fin.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            c["TIME_OFF"] = fin.UtcDateTime.ToString("HHmmss", CultureInfo.InvariantCulture);
        }

        if (!qso.Band.EsVacia) c["BAND"] = qso.Band.Nombre;
        if (!qso.BandRx.EsVacia) c["BAND_RX"] = qso.BandRx.Nombre;
        if (!qso.Freq.EsCero) c["FREQ"] = qso.Freq.AAdif();
        if (qso.FreqRx is { } rx && !rx.EsCero) c["FREQ_RX"] = rx.AAdif();

        if (!qso.Mode.EsVacio)
        {
            c["MODE"] = qso.Mode.Principal;
            if (!string.IsNullOrEmpty(qso.Mode.Submodo)) c["SUBMODE"] = qso.Mode.Submodo;
        }

        if (!qso.RstSent.EsVacio) c["RST_SENT"] = qso.RstSent.Texto;
        if (!qso.RstRcvd.EsVacio) c["RST_RCVD"] = qso.RstRcvd.Texto;

        Poner(c, "PROP_MODE", qso.PropMode);
        Poner(c, "SAT_NAME", qso.SatName);
        Poner(c, "SAT_MODE", qso.SatMode);

        if (!qso.StationCallsign.EsVacio) c["STATION_CALLSIGN"] = qso.StationCallsign.Valor;
        Poner(c, "OPERATOR", qso.Operator);
        Poner(c, "OWNER_CALLSIGN", qso.OwnerCallsign);
        if (!qso.MyGridsquare.EsVacio) c["MY_GRIDSQUARE"] = qso.MyGridsquare.Valor;
        Poner(c, "MY_CITY", qso.MyCity);
        Poner(c, "MY_STATE", qso.MyState);
        Poner(c, "MY_CNTY", qso.MyCnty);
        if (qso.MyDxcc is { } myDxcc) c["MY_DXCC"] = myDxcc.ToString(CultureInfo.InvariantCulture);
        if (qso.MyCqZone is { } myCq) c["MY_CQ_ZONE"] = myCq.ToString(CultureInfo.InvariantCulture);
        if (qso.MyItuZone is { } myItu) c["MY_ITU_ZONE"] = myItu.ToString(CultureInfo.InvariantCulture);

        if (!qso.Gridsquare.EsVacio) c["GRIDSQUARE"] = qso.Gridsquare.Valor;
        Poner(c, "NAME", qso.Name);
        Poner(c, "QTH", qso.Qth);
        Poner(c, "STATE", qso.State);
        Poner(c, "CNTY", qso.Cnty);
        Poner(c, "COUNTRY", qso.Country);
        Poner(c, "CONT", qso.Cont);
        if (qso.Dxcc > 0) c["DXCC"] = qso.Dxcc.ToString(CultureInfo.InvariantCulture);
        if (qso.Cqz is { } cqz) c["CQZ"] = cqz.ToString(CultureInfo.InvariantCulture);
        if (qso.Ituz is { } ituz) c["ITUZ"] = ituz.ToString(CultureInfo.InvariantCulture);
        Poner(c, "IOTA", qso.IotaIslandId);
        Poner(c, "QSL_VIA", qso.QslVia);
        Poner(c, "QSLMSG", qso.QslMsg);
        Poner(c, "COMMENT", qso.Comentario);
        Poner(c, "CONTEST_ID", qso.ContestId);
        if (qso.TxPwr is { } pwr)
        {
            c["TX_PWR"] = pwr.ToString("0.###", CultureInfo.InvariantCulture);
        }

        return c;
    }

    private static void Poner(IDictionary<string, string> destino, string campo, string? valor)
    {
        if (!string.IsNullOrWhiteSpace(valor)) destino[campo] = valor.Trim();
    }
}
