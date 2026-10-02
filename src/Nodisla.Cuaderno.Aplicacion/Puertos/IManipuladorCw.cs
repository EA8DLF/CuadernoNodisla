namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>
/// Un equipo que sabe mandar telegrafia con su propio manipulador interno, por CAT.
/// </summary>
/// <remarks>
/// <para>
/// <b>La telegrafia la genera la radio, no el ordenador.</b> No hay audio del PC por medio: el
/// texto se le pasa al manipulador del equipo (en el FT-710, memoria de texto <c>KM</c> y
/// reproduccion <c>KY</c>; en ICOM, la orden CI-V <c>17</c>; con Hamlib, <c>send_morse</c>).
/// </para>
/// <para>
/// <b>Manipular solo vale dentro de una transmision pedida al vigilante del PTT.</b> Los
/// controles rechazan <see cref="ManipularAsync"/> si no hay PTT pedido: asi el tope de tiempo,
/// el latido y el boton de panico del vigilante cubren tambien la telegrafia. Y al bajar el PTT
/// por cualquier camino, el control para antes el manipulador.
/// </para>
/// </remarks>
public interface IManipuladorCw
{
    /// <summary>
    /// Por que este equipo no puede mandar telegrafia ahora mismo, o nulo si puede.
    /// </summary>
    /// <remarks>
    /// Se mira cada vez: un equipo desconectado no puede y uno cuyo manual no se ha comprobado
    /// tampoco. El texto va en el idioma del programa, para enseñarlo tal cual junto al boton.
    /// </remarks>
    string? PorQueNoManipula { get; }

    /// <summary>Caracteres que admite una sola orden del manipulador (50 en el FT-710, 30 en ICOM).</summary>
    int LetrasPorOrden { get; }

    /// <summary>Velocidad mas baja que admite el manipulador del equipo, en palabras por minuto.</summary>
    int WpmMinima { get; }

    /// <summary>Velocidad mas alta que admite el manipulador del equipo, en palabras por minuto.</summary>
    int WpmMaxima { get; }

    /// <summary>Pone la velocidad del manipulador del equipo.</summary>
    /// <param name="wpm">Palabras por minuto; se acota a lo que admite el equipo.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la orden.</returns>
    Task PonerVelocidadAsync(int wpm, CancellationToken ct = default);

    /// <summary>
    /// Manda un trozo de texto al manipulador del equipo. Vuelve en cuanto la orden ha salido;
    /// la radio sigue manipulando sola.
    /// </summary>
    /// <param name="texto">
    /// Como mucho <see cref="LetrasPorOrden"/> caracteres. Los prosignos van entre angulos
    /// (<c>&lt;AR&gt;</c>, <c>&lt;SK&gt;</c>...): cada control los traduce a lo que entiende su
    /// equipo y quita lo que el equipo no sabe manipular.
    /// </param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la orden.</returns>
    /// <exception cref="InvalidOperationException">
    /// Si no hay una transmision pedida al vigilante del PTT, o si el equipo no puede.
    /// </exception>
    Task ManipularAsync(string texto, CancellationToken ct = default);

    /// <summary>Para el manipulador del equipo en el acto, a mitad de lo que este mandando.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la orden.</returns>
    Task PararManipuladorAsync(CancellationToken ct = default);

    /// <summary>
    /// Deja el equipo como estaba antes de manipular (en el FT-710, la memoria del manipulador
    /// que se uso para el texto). Se llama con el PTT ya abajo.
    /// </summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea.</returns>
    Task TerminarAsync(CancellationToken ct = default) => Task.CompletedTask;
}
