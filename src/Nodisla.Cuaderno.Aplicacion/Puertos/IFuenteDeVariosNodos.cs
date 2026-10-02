namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Como esta, ahora mismo, uno de los nodos de cluster a los que se conecta.</summary>
/// <param name="Id">Identificador del nodo, el mismo que en los ajustes.</param>
/// <param name="Nombre">Nombre con el que se ve en pantalla.</param>
/// <param name="Servidor">Maquina del nodo.</param>
/// <param name="Puerto">Puerto de Telnet.</param>
/// <param name="Activo">Se conecta al conectar el cluster.</param>
/// <param name="EsSkimmer">Es una red de escucha automatica (RBN): todo lo que trae es de maquina.</param>
/// <param name="Estado">Estado de la conexion.</param>
/// <param name="Motivo">Ultimo error, cuando lo hay: por que no conecta o por que se cayo.</param>
/// <param name="SpotsPorMinuto">Anuncios recibidos en el ultimo minuto.</param>
/// <param name="SpotsRecibidos">Anuncios recibidos desde que se conecto.</param>
public sealed record EstadoDeNodo(
    string Id,
    string Nombre,
    string Servidor,
    int Puerto,
    bool Activo,
    bool EsSkimmer,
    EstadoDeConexion Estado,
    string? Motivo,
    int SpotsPorMinuto,
    long SpotsRecibidos);

/// <summary>
/// Fuente de anuncios que junta varios nodos de cluster conectados a la vez.
/// </summary>
/// <remarks>
/// <para>
/// Por fuera es una fuente mas: los anuncios de todos los nodos salen por el mismo
/// <see cref="IFuenteSpots.SpotRecibido"/>, cada uno con el nombre de su nodo en
/// <see cref="Spot.Fuente"/>. Juntar los repetidos es cosa del cuaderno, no de la fuente.
/// </para>
/// <para>
/// <b>Las ordenes nunca se mandan a todos.</b> <see cref="IFuenteSpots.EnviarAsync"/> va a un
/// solo nodo —el primero conectado— y <see cref="EnviarANodoAsync"/> al que se diga. Un spot
/// propio mandado por tres nodos de la misma red saldria tres veces en el mundo entero.
/// </para>
/// </remarks>
public interface IFuenteDeVariosNodos : IFuenteSpots
{
    /// <summary>Estado de cada nodo, en el orden de los ajustes.</summary>
    IReadOnlyList<EstadoDeNodo> Nodos { get; }

    /// <summary>Salta cuando cambia algo de algun nodo: estado, motivo o ritmo de anuncios.</summary>
    event EventHandler? NodosCambiaron;

    /// <summary>Conecta un nodo concreto, este activo o no.</summary>
    /// <param name="id">Identificador del nodo.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task ConectarNodoAsync(string id, CancellationToken ct = default);

    /// <summary>Desconecta un nodo concreto sin tocar los demas.</summary>
    /// <param name="id">Identificador del nodo.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task DesconectarNodoAsync(string id, CancellationToken ct = default);

    /// <summary>Manda una orden a un solo nodo.</summary>
    /// <param name="id">Identificador del nodo.</param>
    /// <param name="orden">Orden en crudo, como se teclea en la consola del nodo.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task EnviarANodoAsync(string id, string orden, CancellationToken ct = default);
}

/// <summary>Una fuente que sabe decir por que ha fallado la ultima vez.</summary>
public interface IFuenteConDiagnostico
{
    /// <summary>Ultimo error, o nulo si la ultima conexion fue bien.</summary>
    string? UltimoError { get; }
}
