using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control;

/// <summary>
/// El canal no existe o no se puede abrir: el puerto no esta, esta cogido por otro programa o
/// el cacharro se ha desenchufado.
/// </summary>
public sealed class CanalNoDisponibleException : Exception
{
    /// <summary>Crea la excepcion.</summary>
    /// <param name="canal">Canal que no se pudo abrir.</param>
    /// <param name="causa">Lo que fallo por debajo.</param>
    public CanalNoDisponibleException(string canal, Exception? causa = null)
        : base(Textos.F("Servicios.Radio.CanalNoDisponible", canal), causa) =>
        Canal = canal;

    /// <summary>Canal que no se pudo abrir.</summary>
    public string Canal { get; }
}

/// <summary>
/// El canal se abrio pero al otro lado no contesta nadie.
/// </summary>
/// <remarks>
/// Es lo que pasa con el equipo apagado: el puerto del cable USB sigue existiendo y se abre sin
/// problema, pero nadie responde a <c>ID;</c>. No es lo mismo que un puerto que no existe, y la
/// interfaz tiene que decirlo de otra manera: aqui basta con encender la radio.
/// </remarks>
public sealed class EquipoNoContestaException : Exception
{
    /// <summary>Crea la excepcion.</summary>
    /// <param name="canal">Canal por el que no contesta nadie.</param>
    public EquipoNoContestaException(string canal)
        : base(Textos.F("Servicios.Radio.NoContesta", canal)) =>
        Canal = canal;

    /// <summary>Canal por el que no contesta nadie.</summary>
    public string Canal { get; }
}

/// <summary>
/// Lo implementa el control que sabe avisar de que ha perdido al equipo.
/// </summary>
/// <remarks>
/// Existe por una razon muy concreta: si el equipo se apaga o se desenchufa mientras se
/// transmite, el vigilante del PTT no puede quedarse creyendo que sigue en antena. El control
/// avisa, el vigilante suelta.
/// </remarks>
public interface IAvisaDePerdidaDeComunicacion
{
    /// <summary>Salta cuando se pierde la comunicacion con el equipo, diciendo por que.</summary>
    event EventHandler<string>? ComunicacionPerdida;
}
