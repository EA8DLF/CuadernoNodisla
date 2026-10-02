using Nodisla.Cuaderno.Modos.Cw;

namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>El decodificador de telegrafía (Configuración › Audio y digitales).</summary>
/// <remarks>Solo escucha: nada de aquí transmite.</remarks>
public sealed class AjustesDeCw
{
    /// <summary>Tono con que arranca y al que se fija sin pitch del equipo (Hz).</summary>
    public int TonoPorOmisionHz { get; set; } = 700;

    /// <summary>Ancho del filtro de cada señal (Hz).</summary>
    public int AnchoDelFiltroHz { get; set; } = 100;

    /// <summary>Sensibilidad de 1 a 10.</summary>
    public int Sensibilidad { get; set; } = 6;

    /// <summary>Velocidad mínima que se sigue (WPM).</summary>
    public int WpmMinima { get; set; } = 5;

    /// <summary>Velocidad máxima que se sigue (WPM).</summary>
    public int WpmMaxima { get; set; } = 60;

    /// <summary>Señales que se leen a la vez (1 = solo la principal).</summary>
    public int Senales { get; set; } = 4;

    /// <summary>En automático, segundos sin señal antes de volver a buscar.</summary>
    public int SegundosSinSenal { get; set; } = 5;

    /// <summary>Deja estos ajustes dentro de unos límites con sentido.</summary>
    /// <returns>Los mismos ajustes, ya acotados.</returns>
    public AjustesDeCw Acotar()
    {
        TonoPorOmisionHz = Math.Clamp(TonoPorOmisionHz, 300, 1200);
        AnchoDelFiltroHz = Math.Clamp(AnchoDelFiltroHz, 30, 500);
        Sensibilidad = Math.Clamp(Sensibilidad, 1, 10);
        WpmMinima = Math.Clamp(WpmMinima, 5, 55);
        WpmMaxima = Math.Clamp(WpmMaxima, WpmMinima + 5, 60);
        Senales = Math.Clamp(Senales, 1, 8);
        SegundosSinSenal = Math.Clamp(SegundosSinSenal, 1, 60);
        return this;
    }

    /// <summary>Las opciones del motor que salen de estos ajustes.</summary>
    public OpcionesCw Opciones() => new OpcionesCw
    {
        TonoPorOmisionHz = TonoPorOmisionHz,
        AnchoDelFiltroHz = AnchoDelFiltroHz,
        Sensibilidad = Sensibilidad,
        WpmMinima = WpmMinima,
        WpmMaxima = WpmMaxima,
        CanalesMaximos = Senales,
        SegundosSinSenalParaBuscar = SegundosSinSenal,
    }.Acotada();
}
