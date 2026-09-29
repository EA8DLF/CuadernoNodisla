namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>Como se escucha a los programas de modos digitales.</summary>
public sealed class OpcionesPuenteDigital
{
    /// <summary>
    /// Puerto UDP de escucha. El 2237 es el que traen puesto de fabrica WSJT-X, JTDX, MSHV y
    /// JS8Call, asi que con dejarlo como esta suele bastar.
    /// </summary>
    public int Puerto { get; set; } = 2237;

    /// <summary>Direccion local por la que escuchar. Vacia significa todas.</summary>
    public string DireccionDeEscucha { get; set; } = "0.0.0.0";

    /// <summary>
    /// Grupo multicast al que unirse, cuando el programa emite en multicast en vez de a una
    /// direccion concreta. Nulo para escuchar solo unicast y difusion.
    /// </summary>
    public string? GrupoMulticast { get; set; }

    /// <summary>Silencio tras el cual se da una instancia por perdida.</summary>
    /// <remarks>
    /// El latido va cada quince segundos, asi que dos minutos son ocho latidos perdidos: da
    /// margen de sobra para una red con hipo sin dejar instancias fantasma en la lista.
    /// </remarks>
    public TimeSpan Caducidad { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Cada cuanto se repasa la lista de instancias buscando las que han callado.</summary>
    public TimeSpan Repaso { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Nombre con el que el cuaderno se identifica ante los programas.</summary>
    public string Identificador { get; set; } = "Cuaderno NODISLA";

    /// <summary>Tamano del buffer de recepcion. Un datagrama del protocolo no llega a tanto.</summary>
    public int TamanoDeBuffer { get; set; } = 64 * 1024;
}
