namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>RTTY: decodificador y emisor propios (Configuración › Audio y digitales).</summary>
/// <remarks>
/// Lo que el operador deja puesto en la página RTTY (tono, desplazamiento, parada e inversión)
/// sobrevive a cerrar el programa, igual que <see cref="AjustesDeCw"/>.
/// </remarks>
public sealed class AjustesDeRtty
{
    /// <summary>Tono de marca con el que arranca: sirve igual para escuchar y para transmitir (Hz).</summary>
    public double TonoHz { get; set; } = 1500;

    /// <summary>Desplazamiento entre marca y espacio (Hz): 170 u 850.</summary>
    public int DesplazamientoHz { get; set; } = 170;

    /// <summary>Bits de parada: 1, 1,5 o 2.</summary>
    public double BitsDeParada { get; set; } = 1.5;

    /// <summary>El tono de marca es el más bajo de los dos («reversa»).</summary>
    public bool Invertido { get; set; }

    /// <summary>Deja estos ajustes dentro de unos límites con sentido.</summary>
    /// <returns>Los mismos ajustes, ya acotados.</returns>
    public AjustesDeRtty Acotar()
    {
        TonoHz = Math.Clamp(TonoHz, 300, 3000);
        DesplazamientoHz = DesplazamientoHz >= 500 ? 850 : 170;
        BitsDeParada = BitsDeParada switch { <= 1.0 => 1.0, >= 2.0 => 2.0, _ => 1.5 };
        return this;
    }
}
