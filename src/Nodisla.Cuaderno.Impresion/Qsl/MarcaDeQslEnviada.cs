using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Impresion.Qsl;

/// <summary>Apunta en el contacto que su tarjeta ya ha salido por correo electronico.</summary>
public static class MarcaDeQslEnviada
{
    /// <summary>
    /// Deja el contacto con <c>QSL_SENT=Y</c>, <c>QSL_SDATE</c> de hoy y <c>QSL_SENT_VIA=E</c>.
    /// </summary>
    /// <param name="qso">El contacto; se modifica.</param>
    /// <param name="cuando">Momento del envio.</param>
    /// <remarks>
    /// Son los campos de la tarjeta QSL de ADIF (medio «papel» en el programa), con la via
    /// electronica. No se toca lo recibido: que yo mande mi tarjeta no confirma el contacto.
    /// </remarks>
    public static void Marcar(Qso qso, DateTimeOffset cuando)
    {
        ArgumentNullException.ThrowIfNull(qso);
        var tarjeta = qso.Confirmaciones.FirstOrDefault(c => c.Medio == MedioDeConfirmacion.Papel);
        if (tarjeta is null)
        {
            tarjeta = new QsoConfirmacion { QsoId = qso.Id, Medio = MedioDeConfirmacion.Papel };
            qso.Confirmaciones.Add(tarjeta);
        }

        tarjeta.Enviado = EstadoDeConfirmacion.Confirmado;
        tarjeta.EnviadoUtc = cuando.ToUniversalTime();
        tarjeta.Via = ViaDeEnvio.Electronico;
    }

    /// <summary>La tarjeta de este contacto ya consta como enviada, por la via que sea.</summary>
    /// <param name="qso">El contacto.</param>
    /// <returns>Si ya se mando.</returns>
    public static bool YaEnviada(Qso qso)
    {
        ArgumentNullException.ThrowIfNull(qso);
        return qso.Confirmaciones.Any(c => c.Medio == MedioDeConfirmacion.Papel
            && c.Enviado is EstadoDeConfirmacion.Confirmado or EstadoDeConfirmacion.Verificado);
    }
}
