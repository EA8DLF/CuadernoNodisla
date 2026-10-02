using Nodisla.Cuaderno.Audio.Procesado;

namespace Nodisla.Cuaderno.Audio.Fonia;

/// <summary>
/// Un camino de audio en vivo: lo que entra por un dispositivo sale por otro.
/// </summary>
/// <remarks>
/// <para>
/// Es la pieza de la fonia por el ordenador. Hay dos, iguales por dentro: una lleva la
/// recepcion del codec del equipo a los altavoces del PC y otra lleva el microfono del PC a la
/// entrada de audio del equipo.
/// </para>
/// <para>
/// La de transmision <b>no transmite por si sola</b>: meter audio en el codec no sube el PTT.
/// Quien la usa para salir al aire es <see cref="ControlDeFonia"/>, que pide la antena al
/// vigilante y late solo mientras este camino dice que el audio avanza.
/// </para>
/// </remarks>
public interface IPuenteDeAudio : IAsyncDisposable
{
    /// <summary>Hay un camino abierto.</summary>
    bool EstaAbierto { get; }

    /// <summary>Ganancia lineal que se aplica al audio: 1 lo deja igual.</summary>
    float Ganancia { get; set; }

    /// <summary>
    /// El camino sigue abierto pero lo que sale es silencio.
    /// </summary>
    /// <remarks>
    /// El medidor sigue marcando lo que entra: sirve para ver que hay audio aunque no suene.
    /// </remarks>
    bool Silenciado { get; set; }

    /// <summary>Pico reciente de lo que entra, ya con la ganancia, de 0 a 1.</summary>
    double Nivel { get; }

    /// <summary>Hace menos de un segundo que el audio llego al tope y se recorto.</summary>
    bool Saturando { get; }

    /// <summary>
    /// Ultimo momento en que el audio avanzo <b>por los dos extremos</b>: llego muestra de la
    /// entrada y la salida pidio muestras. Nulo si no hay camino abierto.
    /// </summary>
    /// <remarks>
    /// Es el dato que hace honesto el latido del PTT en fonia: si el microfono deja de dar
    /// muestras o la tarjeta del equipo se cuelga, deja de avanzar y se deja de latir.
    /// </remarks>
    DateTimeOffset? UltimoAvanceUtc { get; }

    /// <summary>
    /// Paso que ve cada bloque tal como llega, antes de la ganancia: el grabador de la
    /// recepcion, o el voice keyer en la transmision, que cambia la voz por el mensaje.
    /// </summary>
    IProcesadorDeAudio? AntesDeLaGanancia { get; set; }

    /// <summary>
    /// Paso que trabaja despues de la ganancia y antes del silencio: el reductor y el limitador
    /// de la escucha, o el procesado del microfono. El medidor mide lo que sale de aqui.
    /// </summary>
    IProcesadorDeAudio? TrasLaGanancia { get; set; }

    /// <summary>Salta si el camino se rompe solo: se desenchufo algo o fallo el controlador.</summary>
    event EventHandler<Exception>? Fallo;

    /// <summary>Abre el camino entre los dos dispositivos.</summary>
    /// <param name="idEntrada">Dispositivo de captura.</param>
    /// <param name="idSalida">Dispositivo de reproduccion.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task AbrirAsync(string idEntrada, string idSalida, CancellationToken ct = default);

    /// <summary>Cierra el camino. No falla si ya estaba cerrado.</summary>
    Task CerrarAsync();
}
