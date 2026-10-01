namespace Nodisla.Cuaderno.Radio.Ptt;

/// <summary>Por donde se sube el PTT.</summary>
/// <remarks>
/// <para>
/// Lo normal con un equipo moderno es por CAT: la misma conexion que lee la frecuencia manda
/// la orden de transmitir. Las lineas de control del puerto serie siguen haciendo falta para
/// los montajes que llevan una interfaz de audio con su optoacoplador colgado de <c>RTS</c> o
/// de <c>DTR</c>, que es como esta hecha media estacion de este pais.
/// </para>
/// <para>
/// <b>Importa para algo mas que subir:</b> si el PTT va por linea, bajarlo es bajar la linea, y
/// mandar <c>TX0;</c> por CAT no devuelve el equipo a recepcion. Por eso la via elegida cambia
/// tambien las vias de suelta de emergencia.
/// </para>
/// </remarks>
public enum ViaDePtt
{
    /// <summary>Por CAT, con la orden del fabricante. Es lo de partida.</summary>
    Cat,

    /// <summary>Levantando la linea <c>RTS</c> del puerto serie.</summary>
    Rts,

    /// <summary>Levantando la linea <c>DTR</c> del puerto serie.</summary>
    Dtr,
}
