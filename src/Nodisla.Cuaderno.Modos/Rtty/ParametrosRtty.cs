namespace Nodisla.Cuaderno.Modos.Rtty;

/// <summary>
/// Cómo son los tiempos y los tonos de una señal RTTY: velocidad, desplazamiento y bits de
/// arranque y parada.
/// </summary>
/// <param name="Baudios">
/// Velocidad en baudios. 45,45 por omisión: el «60 WPM» de radioaficionado, con el que se trabaja
/// casi siempre en HF.
/// </param>
/// <param name="DesplazamientoHz">
/// Separación entre el tono de marca y el de espacio. 170 Hz por omisión, el estándar de
/// radioaficionado; 850 Hz es el otro valor habitual (enlaces más rápidos o más ruidosos).
/// </param>
/// <param name="BitsDeParada">
/// Duración del bit de parada, en bits. 1,5 por omisión (lo normal a 45,45 baudios); también vale
/// 1 ó 2. Es solo un tiempo de reposo mínimo antes del siguiente arranque: con mandar uno más largo
/// del pedido no pasa nada, así que el demodulador admite cualquiera de los tres sin que haga falta
/// que el valor configurado coincida con el que usó quien transmite.
/// </param>
/// <param name="Invertido">
/// El tono de marca es el más bajo de los dos en vez del más alto (muchos equipos y programas lo
/// llaman «normal/reversa»; con el filtro de BLU del equipo puesto al revés, se oye invertido).
/// </param>
/// <remarks>
/// <para>
/// <b>El resto de la trama no se configura</b> porque no lo pide el estándar: bit de arranque
/// siempre espacio, 5 bits de datos con el orden LSB primero (el de Baudot de toda la vida) y bit
/// de parada siempre marca. En reposo (nadie transmitiendo) la línea se queda en marca continua.
/// </para>
/// </remarks>
public sealed record ParametrosRtty(
    double Baudios = 45.45,
    double DesplazamientoHz = 170,
    double BitsDeParada = 1.5,
    bool Invertido = false)
{
    /// <summary>Duración de un bit de datos (o del de arranque), en segundos.</summary>
    public double DuracionDelBit => 1.0 / Baudios;

    /// <summary>Duración de la trama completa de un carácter: arranque + 5 datos + parada.</summary>
    public double DuracionDelCaracter => DuracionDelBit * (1 + 5 + BitsDeParada);

    /// <summary>Lo mismo, acotado a valores con sentido.</summary>
    public ParametrosRtty Acotado() => this with
    {
        Baudios = Math.Clamp(Baudios, 40, 100),
        DesplazamientoHz = Math.Clamp(DesplazamientoHz, 85, 900),
        BitsDeParada = BitsDeParada switch { <= 1.0 => 1.0, >= 2.0 => 2.0, _ => 1.5 },
    };
}
