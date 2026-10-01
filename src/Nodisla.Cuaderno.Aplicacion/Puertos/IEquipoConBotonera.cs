using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Las teclas de modo de la botonera, las que tiene cualquier equipo de HF.</summary>
public enum TeclaDeModo
{
    /// <summary>Banda lateral inferior.</summary>
    Lsb,
    /// <summary>Banda lateral superior.</summary>
    Usb,
    /// <summary>Telegrafia.</summary>
    Cw,
    /// <summary>Amplitud modulada.</summary>
    Am,
    /// <summary>Frecuencia modulada.</summary>
    Fm,
    /// <summary>Datos: la banda lateral de datos que toque en esa frecuencia.</summary>
    Datos,
}

/// <summary>Una tecla de banda del equipo: la que va a su pila de banda.</summary>
/// <param name="Codigo">Numero de la banda en la orden del equipo (en el FT-710, <c>BS</c>).</param>
/// <param name="Rotulo">Lo que pone la tecla: <c>7</c>, <c>14</c>, <c>GEN</c>.</param>
/// <param name="Banda">Banda ADIF que cubre, o vacia en cobertura general.</param>
/// <param name="Descripcion">Para la ayuda emergente: «40 m (7 MHz)».</param>
public sealed record TeclaDeBanda(int Codigo, string Rotulo, Banda Banda, string Descripcion)
{
    /// <summary>La tecla es la de cobertura general (recepcion fuera de las bandas).</summary>
    public bool EsCoberturaGeneral => Banda.EsVacia;
}

/// <summary>
/// Equipo con las teclas de banda, modo y memoria que se pulsan desde la botonera lateral.
/// </summary>
/// <remarks>
/// Todo va al VFO <b>activo</b>, como en la radio: la tecla BAND recupera la ultima frecuencia y
/// modo usados en esa banda (la pila de banda del equipo), no el principio de la banda.
/// Ninguna de estas ordenes transmite.
/// </remarks>
public interface IEquipoConBotonera
{
    /// <summary>Las teclas de banda que tiene este equipo, en orden.</summary>
    IReadOnlyList<TeclaDeBanda> TeclasDeBanda { get; }

    /// <summary>Pulsa una tecla de banda: el VFO activo va a la pila de esa banda.</summary>
    /// <param name="tecla">Tecla pulsada.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task IrABandaAsync(TeclaDeBanda tecla, CancellationToken ct = default);

    /// <summary>Pulsa una tecla de modo: cambia el modo del VFO activo.</summary>
    /// <param name="modo">Tecla pulsada.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task PonerModoDeTeclaAsync(TeclaDeModo modo, CancellationToken ct = default);
}
