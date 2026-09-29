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

    /// <summary>
    /// Modelo del CAT nativo: clave del catalogo (<c>yaesu-ft710</c>, <c>icom-ic7300</c>...) o
    /// <c>auto</c> (y nulo, que es lo que traen los ajustes de antes de haber mas modelos) para
    /// buscarlo: Yaesu por <c>ID;</c>, ICOM por la orden CI-V <c>19 00</c>.
    /// </summary>
    /// <remarks>
    /// Los ajustes de la conexion (puerto, velocidad, espera, via del PTT) siguen en
    /// <see cref="Ft710"/> por historia —se guardan asi desde el principio— y valen para
    /// cualquier modelo: ver <see cref="Cat"/>.
    /// </remarks>
    public string? Modelo { get; set; }

    /// <summary>
    /// Direccion CI-V para los ICOM, si el operador la ha cambiado en el menu del equipo. Nula =
    /// la de fabrica del modelo.
    /// </summary>
    public byte? DireccionCiv { get; set; }

    /// <summary>Ajustes de la conexion CAT de cualquier modelo (el mismo objeto que <see cref="Ft710"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public OpcionesFt710 Cat => Ft710;

    /// <summary>Ajustes de la via preferida, <c>rigctld</c>.</summary>
    public OpcionesRigctld Rigctld { get; set; } = new();

    /// <summary>Ajustes de la segunda via, OmniRig.</summary>
    public OpcionesOmniRig OmniRig { get; set; } = new();

    /// <summary>Tiempos del vigilante del PTT.</summary>
    public OpcionesDelVigilante Vigilante { get; set; } = new();
}
