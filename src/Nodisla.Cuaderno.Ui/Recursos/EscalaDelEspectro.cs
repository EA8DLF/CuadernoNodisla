namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>
/// Lo que va de un borde al otro del analizador: de que frecuencia a que frecuencia, para
/// pasar de un punto de la pantalla a hercios y al reves.
/// </summary>
/// <param name="InicioHz">Frecuencia del borde izquierdo.</param>
/// <param name="FinHz">Frecuencia del borde derecho.</param>
public readonly record struct EscalaDelEspectro(double InicioHz, double FinHz)
{
    /// <summary>Pasos que se ofrecen para el clic y la rueda, de menor a mayor.</summary>
    public static IReadOnlyList<long> PasosRedondos { get; } = [10, 50, 100, 500, 1_000, 5_000, 10_000];

    /// <summary>Hay escala con que trabajar.</summary>
    public bool Valida => FinHz > InicioHz && InicioHz > 0;

    /// <summary>Ancho en hercios.</summary>
    public double AnchoHz => FinHz - InicioHz;

    /// <summary>La frecuencia de un punto.</summary>
    /// <param name="x">Distancia desde el borde izquierdo, en puntos.</param>
    /// <param name="ancho">Ancho del analizador en puntos.</param>
    /// <returns>Hercios.</returns>
    public double HzEn(double x, double ancho) =>
        ancho <= 0 ? InicioHz : InicioHz + (Math.Clamp(x, 0, ancho) / ancho * AnchoHz);

    /// <summary>Donde cae una frecuencia, en puntos desde el borde izquierdo.</summary>
    /// <param name="hz">Hercios.</param>
    /// <param name="ancho">Ancho del analizador en puntos.</param>
    /// <returns>Puntos; fuera de 0…ancho si la frecuencia no se ve.</returns>
    public double XDe(double hz, double ancho) =>
        AnchoHz <= 0 ? 0 : (hz - InicioHz) / AnchoHz * ancho;

    /// <summary>La frecuencia se ve en el analizador.</summary>
    /// <param name="hz">Hercios.</param>
    /// <returns>Cierto si cae entre los dos bordes.</returns>
    public bool Contiene(double hz) => hz >= InicioHz && hz <= FinHz;

    /// <summary>
    /// El paso que toca para el ancho que se ve: el redondo más pequeño que no sea más fino
    /// que lo que mide un punto de la traza (850 puntos). Con 200 kHz, 500 Hz; con 20 kHz, 50 Hz.
    /// </summary>
    /// <param name="puntos">Puntos de la traza.</param>
    /// <returns>Hercios del paso.</returns>
    public long PasoAutomatico(int puntos = PintorDelAnalizador.Ancho)
    {
        var porPunto = puntos <= 0 ? AnchoHz : AnchoHz / puntos;
        foreach (var paso in PasosRedondos)
        {
            if (paso >= porPunto) return paso;
        }

        return PasosRedondos[^1];
    }

    /// <summary>Lleva una frecuencia al paso más cercano (como el clic de Thetis).</summary>
    /// <param name="hz">Hercios.</param>
    /// <param name="paso">Paso en hercios; 0 o menos, sin ajustar.</param>
    /// <returns>Hercios ajustados.</returns>
    public static long AjustarAlPaso(double hz, long paso) =>
        paso <= 0 ? (long)Math.Round(hz) : (long)Math.Round(hz / paso, MidpointRounding.AwayFromZero) * paso;
}
