namespace Nodisla.Cuaderno.Servicios.Actualizaciones;

/// <summary>Decide si toca preguntar a GitHub al arrancar: como mucho una vez al dia.</summary>
public static class PlanDeComprobacion
{
    /// <summary>Tiempo minimo entre dos comprobaciones automaticas.</summary>
    public static readonly TimeSpan Intervalo = TimeSpan.FromDays(1);

    /// <summary>Si toca comprobar ahora.</summary>
    /// <param name="ultima">Ultima comprobacion automatica, o nula si nunca se hizo.</param>
    /// <param name="ahora">Hora actual (del reloj inyectado).</param>
    /// <remarks>
    /// Una ultima comprobacion <b>en el futuro</b> (el reloj del PC iba adelantado y se
    /// corrigio) cuenta como que toca: si no, el aviso quedaria mudo hasta alcanzar esa fecha.
    /// </remarks>
    public static bool Toca(DateTimeOffset? ultima, DateTimeOffset ahora) =>
        ultima is not { } u || u > ahora || ahora - u >= Intervalo;
}
