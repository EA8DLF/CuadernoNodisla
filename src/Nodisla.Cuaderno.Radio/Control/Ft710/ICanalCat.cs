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
}

/// <summary>Constantes del protocolo CAT de Yaesu.</summary>
public static class Cat
{
    /// <summary>Lo que contesta el equipo cuando no admite una orden.</summary>
    public const string NoAdmitido = "?";

    /// <summary>Caracter que cierra toda orden y toda respuesta.</summary>
    public const char Fin = ';';
}
