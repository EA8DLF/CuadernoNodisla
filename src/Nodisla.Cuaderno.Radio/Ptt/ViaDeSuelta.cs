namespace Nodisla.Cuaderno.Radio.Ptt;

/// <summary>
/// Una manera concreta de bajar el PTT.
/// </summary>
/// <remarks>
/// El vigilante prueba las vias una por una, de la mas fina a la mas bruta, hasta que una
/// funciona. Existen porque la via normal puede estar rota justo cuando mas falta hace: el
/// socket colgado, el proceso del demonio muerto o el objeto COM caido.
/// </remarks>
/// <param name="Nombre">Como se llama la via en el registro.</param>
/// <param name="SoltarAsync">Baja el PTT por esta via. Debe lanzar si no lo consigue.</param>
/// <param name="SoltarSincrono">
/// Version bloqueante para el cierre del proceso, donde no se puede confiar en el planificador
/// de tareas. Si es nula, en el cierre se usa <see cref="SoltarAsync"/> con espera acotada.
/// </param>
public sealed record ViaDeSuelta(
    string Nombre,
    Func<CancellationToken, Task> SoltarAsync,
    Action? SoltarSincrono = null);

/// <summary>
/// Lo implementa el control de equipo que sabe bajar el PTT por mas de un camino.
/// </summary>
public interface ISueltaDeEmergenciaPtt
{
    /// <summary>Vias de suelta, ordenadas de la mas fiable a la mas bruta.</summary>
    IReadOnlyList<ViaDeSuelta> ViasDeSuelta { get; }
}
