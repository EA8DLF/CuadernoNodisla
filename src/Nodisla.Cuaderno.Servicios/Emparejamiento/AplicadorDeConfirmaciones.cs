using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Servicios.Emparejamiento;

/// <summary>
/// Vuelca una confirmacion descargada sobre el contacto del cuaderno.
/// </summary>
/// <remarks>
/// <para>
/// <b>Bajar no pisa.</b> Una confirmacion solo puede mejorar el estado de un contacto. Si el
/// cuaderno ya lo daba por verificado y el servicio ahora solo dice «recibido», el cuaderno
/// gana: los servicios reescriben su historia con mas alegria que el operador, y perder una
/// verificacion buena por una descarga rutinaria seria un destrozo silencioso.
/// </para>
/// <para>
/// <b>Y no toca el contacto.</b> Ni el indicativo, ni la banda, ni el modo, ni la hora, ni los
/// datos del corresponsal. Lo que el servicio aporta de mas viaja en
/// <see cref="ConfirmacionDescargada.CamposExtra"/> y se le ensena al operador, que decide.
/// </para>
/// </remarks>
public static class AplicadorDeConfirmaciones
{
    /// <summary>
    /// Fuerza de cada estado, para no degradar nunca una confirmacion.
    /// </summary>
    /// <remarks>
    /// Los estados negativos (<see cref="EstadoDeConfirmacion.Rechazado"/>,
    /// <see cref="EstadoDeConfirmacion.Devuelto"/> e <see cref="EstadoDeConfirmacion.Invalido"/>)
    /// valen menos que <see cref="EstadoDeConfirmacion.Ninguno"/> a proposito: son informacion
    /// que el operador ha metido a mano y que una descarga no debe sobrescribir, pero tampoco
    /// deben bloquear una confirmacion buena que llegue despues.
    /// </remarks>
    /// <param name="estado">Estado a puntuar.</param>
    public static int Rango(EstadoDeConfirmacion estado) => estado switch
    {
        EstadoDeConfirmacion.Verificado => 4,
        EstadoDeConfirmacion.Confirmado => 3,
        EstadoDeConfirmacion.Solicitado => 2,
        EstadoDeConfirmacion.Pendiente => 1,
        EstadoDeConfirmacion.Ninguno => 0,
        _ => -1,
    };

    /// <summary>
    /// Aplica la confirmacion al contacto. Devuelve si cambio algo.
    /// </summary>
    /// <param name="qso">Contacto del cuaderno.</param>
    /// <param name="confirmacion">Confirmacion descargada.</param>
    public static bool Aplicar(Qso qso, ConfirmacionDescargada confirmacion)
    {
        ArgumentNullException.ThrowIfNull(qso);
        ArgumentNullException.ThrowIfNull(confirmacion);

        var nuevo = confirmacion.Verificada
            ? EstadoDeConfirmacion.Verificado
            : EstadoDeConfirmacion.Confirmado;

        var fila = qso.Confirmaciones.FirstOrDefault(c => c.Medio == confirmacion.Medio);
        if (fila is null)
        {
            qso.Confirmaciones.Add(new QsoConfirmacion
            {
                Medio = confirmacion.Medio,
                Recibido = nuevo,
                RecibidoUtc = confirmacion.ConfirmadaUtc,
                Via = ViaDeEnvio.Electronico,
            });
            return true;
        }

        var cambio = false;

        if (Rango(nuevo) > Rango(fila.Recibido))
        {
            fila.Recibido = nuevo;
            cambio = true;
        }

        // La fecha solo se rellena si faltaba: la primera noticia de la confirmacion es la
        // buena, y las descargas posteriores del mismo servicio suelen traer la de la descarga.
        if (fila.RecibidoUtc is null && confirmacion.ConfirmadaUtc is { } cuando)
        {
            fila.RecibidoUtc = cuando;
            cambio = true;
        }

        // La via no se toca en una fila que ya existia: la puso el operador y una descarga no
        // tiene por que saber mejor que el por donde vino la tarjeta.
        return cambio;
    }

    /// <summary>
    /// Marca en el contacto que se ha subido al servicio. Tampoco degrada: si ya constaba
    /// confirmado por la otra parte, subirlo otra vez no lo devuelve a «pendiente».
    /// </summary>
    /// <param name="qso">Contacto del cuaderno.</param>
    /// <param name="medio">Servicio al que se subio.</param>
    /// <param name="cuandoUtc">Momento de la subida.</param>
    public static bool MarcarSubido(Qso qso, MedioDeConfirmacion medio, DateTimeOffset cuandoUtc)
    {
        ArgumentNullException.ThrowIfNull(qso);

        var fila = qso.Confirmaciones.FirstOrDefault(c => c.Medio == medio);
        if (fila is null)
        {
            qso.Confirmaciones.Add(new QsoConfirmacion
            {
                Medio = medio,
                Enviado = EstadoDeConfirmacion.Confirmado,
                EnviadoUtc = cuandoUtc,
                Via = ViaDeEnvio.Electronico,
            });
            return true;
        }

        var cambio = false;
        if (Rango(EstadoDeConfirmacion.Confirmado) > Rango(fila.Enviado))
        {
            fila.Enviado = EstadoDeConfirmacion.Confirmado;
            cambio = true;
        }
        if (fila.EnviadoUtc is null)
        {
            fila.EnviadoUtc = cuandoUtc;
            cambio = true;
        }
        if (fila.Via == ViaDeEnvio.Ninguna)
        {
            fila.Via = ViaDeEnvio.Electronico;
            cambio = true;
        }
        return cambio;
    }
}
