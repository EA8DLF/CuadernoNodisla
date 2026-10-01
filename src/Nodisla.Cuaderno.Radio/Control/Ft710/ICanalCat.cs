namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>
/// Canal por el que se le habla a un equipo Yaesu: ordenes de texto terminadas en punto y coma.
/// </summary>
/// <remarks>
/// Se separa del puerto serie para poder probar todo contra un equipo de mentira. Las pruebas
/// del control usan un canal por TCP contra un servidor que contesta como el FT-710 de la
/// captura; asi no hace falta que la radio de Jose este encendida para que las pruebas pasen.
/// </remarks>
public interface ICanalCat : IAsyncDisposable
{
    /// <summary>El canal esta abierto.</summary>
    bool Abierto { get; }

    /// <summary>Como llamar a este canal en el registro, por ejemplo <c>COM3 a 115200</c>.</summary>
    string Descripcion { get; }

    /// <summary>Abre el canal.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la apertura.</returns>
    Task AbrirAsync(CancellationToken ct = default);

    /// <summary>
    /// Manda una orden y espera la respuesta.
    /// </summary>
    /// <param name="orden">Orden con su punto y coma, por ejemplo <c>FA;</c>.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>
    /// La respuesta sin el punto y coma final, <c>?</c> si el equipo dice que no admite la
    /// orden, o nulo si no contesta nada.
    /// </returns>
    Task<string?> PreguntarAsync(string orden, CancellationToken ct = default);

    /// <summary>Manda una orden sin esperar respuesta.</summary>
    /// <param name="orden">Orden con su punto y coma.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea del envio.</returns>
    Task MandarAsync(string orden, CancellationToken ct = default);

    /// <summary>
    /// Manda una orden bloqueando, para el cierre del proceso y para bajar el PTT de urgencia.
    /// </summary>
    /// <param name="orden">Orden con su punto y coma.</param>
    void MandarSincrono(string orden);

    /// <summary>Cierra el canal.</summary>
    void Cerrar();

    /// <summary>
    /// El canal puede accionar las lineas de control del puerto serie (<c>RTS</c>, <c>DTR</c>).
    /// </summary>
    /// <remarks>
    /// Solo lo puede un canal por puerto serie de verdad. Un canal por TCP —el que usan las
    /// pruebas y el que habla con un equipo en red— no tiene lineas que levantar, y decirlo
    /// aqui evita que el control ofrezca un PTT por linea que no existe.
    /// </remarks>
    bool PuedeAccionarLineas => false;

    /// <summary>
    /// Sube o baja la linea de control que hace de PTT.
    /// </summary>
    /// <param name="transmitir">Verdadero para levantar la linea.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la orden.</returns>
    Task PonerLineaDePttAsync(bool transmitir, CancellationToken ct = default) =>
        Task.FromException(new NotSupportedException(
            $"El canal {Descripcion} no tiene líneas de control que accionar."));

    /// <summary>
    /// Baja la linea de PTT bloqueando, para el cierre del proceso.
    /// </summary>
    /// <param name="transmitir">Verdadero para levantar la linea.</param>
    void PonerLineaDePttSincrono(bool transmitir) =>
        throw new NotSupportedException($"El canal {Descripcion} no tiene líneas de control que accionar.");
}

/// <summary>Constantes del protocolo CAT de Yaesu.</summary>
public static class Cat
{
    /// <summary>Lo que contesta el equipo cuando no admite una orden.</summary>
    public const string NoAdmitido = "?";

    /// <summary>Caracter que cierra toda orden y toda respuesta.</summary>
    public const char Fin = ';';
}
