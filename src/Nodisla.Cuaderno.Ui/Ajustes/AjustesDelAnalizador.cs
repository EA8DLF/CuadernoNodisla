using Nodisla.Cuaderno.Ui.Recursos;

namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>
/// Como se pinta y como se maneja el analizador de la propia radio (no toca nada de la radio).
/// </summary>
public sealed class AjustesDelAnalizador
{
    /// <summary>Se ponen los spots del cluster encima del analizador.</summary>
    public bool SpotsEncima { get; set; } = true;

    /// <summary>Carriles de rotulos de spots (1 a 4).</summary>
    public int CarrilesDeSpots { get; set; } = 3;

    /// <summary>Un clic en el analizador sintoniza (y la rueda mueve el VFO).</summary>
    public bool ClicParaSintonizar { get; set; } = true;

    /// <summary>Paso del clic y de la rueda, en hercios; 0, el que toque por el ancho que se ve.</summary>
    public int PasoHz { get; set; }

    /// <summary>El negro de la cascada sigue al suelo de ruido (AGC de la cascada).</summary>
    public bool SueloAutomatico { get; set; } = true;

    /// <summary>Sin AGC, el nivel de la radio (0-255) que sale negro.</summary>
    public int NivelBajo { get; set; } = PintorDelAnalizador.NivelBajoDeFabrica;

    /// <summary>Puntos de la escala de la radio entre el negro y el color mas vivo.</summary>
    public int Contraste { get; set; } = PintorDelAnalizador.ContrasteDeFabrica;

    /// <summary>Brillo del ruido, en puntos (-20 a 20).</summary>
    public int Brillo { get; set; }

    /// <summary>Colores de la cascada, por su nombre (<see cref="PaletaDelAnalizador"/>).</summary>
    public string Paleta { get; set; } = nameof(PaletaDelAnalizador.Radio);

    /// <summary>Se marcan los picos de la traza.</summary>
    public bool MarcarPicos { get; set; }

    /// <summary>Al resintonizar, la cascada se corre con la frecuencia.</summary>
    public bool DesplazarCascada { get; set; } = true;

    /// <summary>Pasos que se ofrecen (0 = automático).</summary>
    public static IReadOnlyList<int> Pasos { get; } = [0, 10, 50, 100, 500, 1_000, 5_000, 10_000];

    /// <summary>Recorta lo que venga fuera de rango de un fichero editado a mano.</summary>
    /// <returns>Los mismos ajustes, ya acotados.</returns>
    public AjustesDelAnalizador Acotar()
    {
        CarrilesDeSpots = Math.Clamp(CarrilesDeSpots, 1, 4);
        if (!Pasos.Contains(PasoHz)) PasoHz = 0;
        NivelBajo = Math.Clamp(NivelBajo, 0, 250);
        Contraste = Math.Clamp(Contraste, 20, 150);
        Brillo = Math.Clamp(Brillo, -20, 20);
        Paleta = PaletasDelAnalizador.DesdeNombre(Paleta).ToString();
        return this;
    }
}
