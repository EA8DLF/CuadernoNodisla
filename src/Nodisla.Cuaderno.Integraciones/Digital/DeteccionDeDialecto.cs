using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Averigua con que programa se esta hablando y que se le puede pedir.
/// </summary>
/// <remarks>
/// El identificador del mensaje es la pista principal y la unica que llega desde el primer
/// latido. Los tipos 50 y 51 son prueba concluyente de JTDX, porque WSJT-X no los tiene, y un
/// mensaje de estado que se acaba justo donde JTDX lo acaba es un indicio razonable. Todo
/// junto basta: una instancia se identifica por su identificador y puede haber varias a la
/// vez, cada una con su dialecto.
/// </remarks>
public static class DeteccionDeDialecto
{
    /// <summary>Deduce el dialecto del identificador que manda el propio programa.</summary>
    /// <param name="identificador">Campo <c>Id</c> del mensaje.</param>
    public static DialectoDigital PorIdentificador(string? identificador)
    {
        if (string.IsNullOrWhiteSpace(identificador)) return DialectoDigital.Desconocido;
        var id = identificador.Trim();

        // El orden importa: hay forks que anteponen su nombre al de WSJT-X.
        if (Contiene(id, "JTDX")) return DialectoDigital.Jtdx;
        if (Contiene(id, "MSHV")) return DialectoDigital.Mshv;
        if (Contiene(id, "JS8")) return DialectoDigital.Js8Call;
        if (Contiene(id, "WSJT")) return DialectoDigital.WsjtX;
        return DialectoDigital.Desconocido;
    }

    private static bool Contiene(string texto, string aguja) =>
        texto.Contains(aguja, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Capacidades de cada dialecto.
    /// </summary>
    /// <remarks>
    /// WSJT-X y JTDX estan comprobados contra el <c>NetworkMessage.hpp</c> de cada uno: JTDX
    /// nunca implemento el resaltado de indicativos (tipo 13) ni los mensajes de configuracion
    /// (14 y 15), y a cambio tiene dos propios (50 y 51) que WSJT-X no conoce. Enviarle a un
    /// programa un mensaje que no tiene no da error: simplemente no pasa nada, que es peor. Por
    /// eso la interfaz pregunta antes y desactiva el boton.
    ///
    /// Lo de MSHV y JS8Call <b>no esta verificado contra fuente</b>: no se encontro documento
    /// autoritativo de sus divergencias, asi que aqui se les supone solo lo comun del
    /// protocolo. Quien lo compruebe algun dia que corrija esta tabla y quite este parrafo.
    /// Un dialecto desconocido no recibe ninguna orden: mejor un boton apagado que un
    /// datagrama que nadie sabe como va a sentar al otro lado.
    /// </remarks>
    /// <param name="dialecto">Programa del que se pregunta.</param>
    public static CapacidadesDigitales De(DialectoDigital dialecto) => dialecto switch
    {
        DialectoDigital.WsjtX => new CapacidadesDigitales(true, true, false, false, true),
        DialectoDigital.Jtdx => new CapacidadesDigitales(true, false, true, true, false),
        DialectoDigital.Mshv => new CapacidadesDigitales(true, false, false, false, false),
        DialectoDigital.Js8Call => new CapacidadesDigitales(true, false, false, false, false),
        _ => CapacidadesDigitales.Desconocidas,
    };

    /// <summary>Nombre del dialecto para mostrarlo al operador.</summary>
    /// <param name="dialecto">Programa del que se pregunta.</param>
    public static string Nombre(DialectoDigital dialecto) => dialecto switch
    {
        DialectoDigital.WsjtX => "WSJT-X",
        DialectoDigital.Jtdx => "JTDX",
        DialectoDigital.Mshv => "MSHV",
        DialectoDigital.Js8Call => "JS8Call",
        _ => "desconocido",
    };

    /// <summary>
    /// Nombre del modo especial de operacion de WSJT-X, que viaja como un numero.
    /// </summary>
    /// <remarks>
    /// El cero significa «ninguno» y por eso devuelve nulo: en el estado, nulo quiere decir
    /// que no hay modo especial o que el programa no lo informa, y las dos cosas se muestran
    /// igual. JTDX no tiene este campo en absoluto.
    /// </remarks>
    /// <param name="codigo">Valor del campo tal y como llega.</param>
    public static string? ModoEspecial(byte codigo) => codigo switch
    {
        0 => null,
        1 => "NA VHF",
        2 => "EU VHF",
        3 => "Field Day",
        4 => "RTTY Roundup",
        5 => "WW Digi",
        6 => "Fox",
        7 => "Hound",
        8 => "ARRL Digi",
        _ => $"modo especial {codigo}",
    };
}
