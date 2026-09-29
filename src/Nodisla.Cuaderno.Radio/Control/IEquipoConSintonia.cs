namespace Nodisla.Cuaderno.Radio.Control;

/// <summary>
/// Un equipo cuyo acoplador de antena se puede lanzar desde el programa (TUNE mantenido).
/// </summary>
/// <remarks>
/// La sintonia emite portadora, asi que va siempre dentro de una transmision pedida al
/// vigilante del PTT: se llama a <see cref="PrepararSintonia"/>, se pide antena (el control
/// manda su orden de sintonia en vez de la de PTT) y se espera con
/// <see cref="EsperarFinDeSintoniaAsync"/> latiendo al vigilante.
/// </remarks>
public interface IEquipoConSintonia
{
    /// <summary>La siguiente transmision vigilada sera una sintonia del acoplador.</summary>
    void PrepararSintonia();

    /// <summary>Espera a que el acoplador termine, latiendo mientras tanto.</summary>
    /// <param name="latir">Latido al vigilante.</param>
    /// <param name="tope">Lo mas que se espera.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Verdadero si se le vio terminar dentro del tope.</returns>
    Task<bool> EsperarFinDeSintoniaAsync(Action latir, TimeSpan tope, CancellationToken ct = default);

    /// <summary>
    /// El equipo dice por CAT cuando termina. Si no, se suelta a un tiempo fijo y
    /// <see cref="EsperarFinDeSintoniaAsync"/> devuelve falso sin que sea un fallo.
    /// </summary>
    bool VeElFinDeLaSintonia => true;
}
