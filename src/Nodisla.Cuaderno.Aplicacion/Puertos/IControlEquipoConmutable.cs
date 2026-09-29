namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>
/// Control de equipo que puede cambiar de via sin cerrar el programa.
/// </summary>
/// <remarks>
/// <para>
/// Existe por un motivo muy concreto: el control del equipo se registra <b>una sola vez</b> al
/// arrancar, y todo lo que hay delante —el panel del equipo, el frontal dibujado, la barra, el
/// vigilante del PTT— se engancha a ese objeto. Sin un intermediario, cambiar la via de control
/// en los ajustes obligaria a cerrar y volver a abrir el cuaderno, que es justo lo que no puede
/// pasar mientras se opera.
/// </para>
/// <para>
/// El intermediario delega todo en el control vigente y sabe sustituirlo: <b>baja el PTT y
/// desconecta el anterior antes de poner el nuevo</b>. Quien esta delante no se entera del
/// cambio mas que por los eventos que ya escuchaba, mas
/// <see cref="ControlCambiado"/> para lo que hay que volver a preguntar —si el equipo es
/// avanzado, si tiene dos VFO, como se llama—.
/// </para>
/// </remarks>
public interface IControlEquipoConmutable : IControlEquipo
{
    /// <summary>
    /// Control que hay ahora mismo detras.
    /// </summary>
    /// <remarks>
    /// Hay que preguntarle a <b>este</b>, y no al intermediario, si el equipo es avanzado o si
    /// tiene dos VFO: el intermediario no puede implementar esos contratos porque entonces
    /// diria que si aunque detras no haya mas que el control sin equipo.
    /// </remarks>
    IControlEquipo Actual { get; }

    /// <summary>Salta cuando se ha sustituido el control, diciendo cual es el nuevo.</summary>
    event EventHandler<IControlEquipo>? ControlCambiado;

    /// <summary>
    /// Pone otro control en lugar del que hay.
    /// </summary>
    /// <remarks>
    /// Baja el PTT del que se va y lo desconecta antes de sustituirlo. Quien llama deberia
    /// haber soltado antes el PTT por <see cref="IVigilantePtt"/>, para que el vigilante se
    /// entere de que ya no hay nada en antena.
    /// </remarks>
    /// <param name="nuevo">Control que pasa a estar puesto.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la sustitucion.</returns>
    Task SustituirAsync(IControlEquipo nuevo, CancellationToken ct = default);
}
