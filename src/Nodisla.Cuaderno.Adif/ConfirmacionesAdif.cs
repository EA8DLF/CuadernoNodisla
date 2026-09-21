using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif;

/// <summary>
/// Traduccion entre los campos de confirmacion de ADIF y la tabla de confirmaciones del modelo.
/// </summary>
/// <remarks>
/// ADIF reparte lo mismo en tres familias de campos con nombres distintos: la QSL de papel usa
/// <c>QSL_SENT</c> y <c>QSL_RCVD</c>, los servicios electronicos anteponen su nombre
/// (<c>LOTW_QSL_SENT</c>) y las pasarelas de subida hablan de <c>QSO_UPLOAD_STATUS</c>. Aqui se
/// describe cada via una sola vez y el resto del codigo trabaja con la lista de confirmaciones.
/// </remarks>
internal static class ConfirmacionesAdif
{
    /// <summary>Nombres de los campos ADIF que describen una via de confirmacion.</summary>
    /// <param name="Medio">Via del modelo.</param>
    /// <param name="Enviado">Campo del estado de lo enviado.</param>
    /// <param name="Recibido">Campo del estado de lo recibido.</param>
    /// <param name="FechaEnviado">Campo de la fecha de envio, si la via la tiene.</param>
    /// <param name="FechaRecibido">Campo de la fecha de recepcion, si la via la tiene.</param>
    /// <param name="ViaEnviado">Campo de la via de envio, solo para la QSL de papel.</param>
    /// <param name="ViaRecibido">Campo de la via de recepcion, solo para la QSL de papel.</param>
    /// <param name="EsSubida">La via usa la enumeracion de estado de subida, no la de QSL.</param>
    internal sealed record Descriptor(
        MedioDeConfirmacion Medio,
        string Enviado,
        string Recibido,
        string? FechaEnviado,
        string? FechaRecibido,
        string? ViaEnviado,
        string? ViaRecibido,
        bool EsSubida);

    /// <summary>Todas las vias que se saben leer y escribir, en el orden en que se exportan.</summary>
    public static IReadOnlyList<Descriptor> Descriptores { get; } =
    [
        new(MedioDeConfirmacion.Papel, "QSL_SENT", "QSL_RCVD", "QSLSDATE", "QSLRDATE", "QSL_SENT_VIA", "QSL_RCVD_VIA", false),
        new(MedioDeConfirmacion.Eqsl, "EQSL_QSL_SENT", "EQSL_QSL_RCVD", "EQSL_QSLSDATE", "EQSL_QSLRDATE", null, null, false),
        new(MedioDeConfirmacion.Lotw, "LOTW_QSL_SENT", "LOTW_QSL_RCVD", "LOTW_QSLSDATE", "LOTW_QSLRDATE", null, null, false),
        new(MedioDeConfirmacion.ClubLog, "CLUBLOG_QSO_UPLOAD_STATUS", "CLUBLOG_QSO_DOWNLOAD_STATUS", "CLUBLOG_QSO_UPLOAD_DATE", "CLUBLOG_QSO_DOWNLOAD_DATE", null, null, true),
        new(MedioDeConfirmacion.QrzCom, "QRZCOM_QSO_UPLOAD_STATUS", "QRZCOM_QSO_DOWNLOAD_STATUS", "QRZCOM_QSO_UPLOAD_DATE", "QRZCOM_QSO_DOWNLOAD_DATE", null, null, true),
        new(MedioDeConfirmacion.HrdLog, "HRDLOG_QSO_UPLOAD_STATUS", "HRDLOG_QSO_DOWNLOAD_STATUS", "HRDLOG_QSO_UPLOAD_DATE", "HRDLOG_QSO_DOWNLOAD_DATE", null, null, true),
        new(MedioDeConfirmacion.HamQth, "HAMQTH_QSO_UPLOAD_STATUS", "HAMQTH_QSO_DOWNLOAD_STATUS", "HAMQTH_QSO_UPLOAD_DATE", "HAMQTH_QSO_DOWNLOAD_DATE", null, null, true),
    ];

    /// <summary>Indice por nombre de campo, para saber de un vistazo si un campo es de confirmacion.</summary>
    public static IReadOnlyDictionary<string, Descriptor> PorCampo { get; } = ConstruirIndice();

    private static Dictionary<string, Descriptor> ConstruirIndice()
    {
        var indice = new Dictionary<string, Descriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Descriptores)
        {
            foreach (var campo in new[] { d.Enviado, d.Recibido, d.FechaEnviado, d.FechaRecibido, d.ViaEnviado, d.ViaRecibido })
            {
                if (campo is not null) indice[campo] = d;
            }
        }
        return indice;
    }

    /// <summary>
    /// Traduce la letra de estado de ADIF. La enumeracion de las QSL admite <c>Y N R Q I V</c>;
    /// la de las subidas, <c>Y N M</c>.
    /// </summary>
    public static bool TryLeerEstado(string? texto, bool esSubida, out EstadoDeConfirmacion estado)
    {
        estado = EstadoDeConfirmacion.Ninguno;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var c = char.ToUpperInvariant(texto.Trim()[0]);
        if (esSubida)
        {
            estado = c switch
            {
                'Y' => EstadoDeConfirmacion.Confirmado,
                'N' => EstadoDeConfirmacion.Ninguno,
                'M' => EstadoDeConfirmacion.Pendiente,
                _ => EstadoDeConfirmacion.Ninguno,
            };
            return c is 'Y' or 'N' or 'M';
        }
        estado = c switch
        {
            'Y' => EstadoDeConfirmacion.Confirmado,
            'V' => EstadoDeConfirmacion.Verificado,
            'N' => EstadoDeConfirmacion.Ninguno,
            'R' => EstadoDeConfirmacion.Solicitado,
            'Q' => EstadoDeConfirmacion.Pendiente,
            'I' => EstadoDeConfirmacion.Rechazado,
            _ => EstadoDeConfirmacion.Ninguno,
        };
        return c is 'Y' or 'V' or 'N' or 'R' or 'Q' or 'I';
    }

    /// <summary>
    /// Letra ADIF que corresponde a un estado. La enumeracion de las subidas no tiene una letra
    /// para lo verificado, asi que ahi se escribe como confirmado.
    /// </summary>
    public static string EscribirEstado(EstadoDeConfirmacion estado, bool esSubida) => esSubida
        ? estado switch
        {
            EstadoDeConfirmacion.Confirmado or EstadoDeConfirmacion.Verificado => "Y",
            EstadoDeConfirmacion.Pendiente => "M",
            _ => "N",
        }
        : estado switch
        {
            EstadoDeConfirmacion.Confirmado => "Y",
            EstadoDeConfirmacion.Verificado => "V",
            EstadoDeConfirmacion.Solicitado => "R",
            EstadoDeConfirmacion.Pendiente => "Q",
            EstadoDeConfirmacion.Rechazado => "I",
            EstadoDeConfirmacion.Invalido => "I",
            EstadoDeConfirmacion.Devuelto => "N",
            _ => "N",
        };

    /// <summary>Traduce la letra de via de envio de una tarjeta.</summary>
    public static bool TryLeerVia(string? texto, out ViaDeEnvio via)
    {
        via = ViaDeEnvio.Ninguna;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        via = char.ToUpperInvariant(texto.Trim()[0]) switch
        {
            'B' => ViaDeEnvio.Buro,
            'D' => ViaDeEnvio.Directo,
            'M' => ViaDeEnvio.Gestor,
            'E' => ViaDeEnvio.Electronico,
            _ => ViaDeEnvio.Ninguna,
        };
        return via != ViaDeEnvio.Ninguna;
    }

    /// <summary>Letra ADIF que corresponde a una via de envio.</summary>
    public static string? EscribirVia(ViaDeEnvio via) => via switch
    {
        ViaDeEnvio.Buro => "B",
        ViaDeEnvio.Directo => "D",
        ViaDeEnvio.Gestor => "M",
        ViaDeEnvio.Electronico => "E",
        _ => null,
    };

    /// <summary>Busca la confirmacion de una via, creandola la primera vez que hace falta.</summary>
    public static QsoConfirmacion Obtener(Qso qso, MedioDeConfirmacion medio)
    {
        foreach (var c in qso.Confirmaciones)
        {
            if (c.Medio == medio) return c;
        }
        var nueva = new QsoConfirmacion { Medio = medio };
        qso.Confirmaciones.Add(nueva);
        return nueva;
    }
}
