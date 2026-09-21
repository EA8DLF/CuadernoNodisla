using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.OmniRig;
using Nodisla.Cuaderno.Radio.Control.Rigctld;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio;

/// <summary>Como se controla el equipo de radio.</summary>
public sealed class OpcionesDeRadio
{
    /// <summary>
    /// Via de control. De partida, ninguna: el programa arranca sin tocar ningun puerto serie
    /// y es el operador quien decide conectar el equipo.
    /// </summary>
    public ViaDeControl Via { get; set; } = ViaDeControl.Ninguna;

    /// <summary>
    /// Ajustes del CAT nativo, que hoy es el del Yaesu FT-710.
    /// </summary>
    /// <remarks>
    /// El CAT nativo llega a lo que Hamlib no da: filtros, ruido, contorno, muesca, telegrafia,
    /// medidores y el menu interno. A cambio, solo vale para el equipo para el que se ha
    /// escrito; para cualquier otro, la via es <c>rigctld</c>.
    /// </remarks>
    public OpcionesFt710 Ft710 { get; set; } = new();

    /// <summary>Ajustes de la via preferida, <c>rigctld</c>.</summary>
    public OpcionesRigctld Rigctld { get; set; } = new();

    /// <summary>Ajustes de la segunda via, OmniRig.</summary>
    public OpcionesOmniRig OmniRig { get; set; } = new();

    /// <summary>Tiempos del vigilante del PTT.</summary>
    public OpcionesDelVigilante Vigilante { get; set; } = new();
}
