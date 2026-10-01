namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>Un nodo de cluster de los que se ofrecen hechos.</summary>
/// <param name="Nombre">Como se llama en la lista.</param>
/// <param name="Servidor">Maquina a la que conectarse.</param>
/// <param name="Puerto">Puerto de Telnet.</param>
/// <param name="Nota">Para que sirve o que tiene de particular.</param>
public sealed record NodoConocido(string Nombre, string Servidor, int Puerto, string Nota)
{
    /// <summary>Como se escribe en un desplegable.</summary>
    public string ParaElDesplegable => $"{Nombre} — {Servidor}:{Puerto}";
}

/// <summary>
/// Nodos de cluster que se ofrecen hechos, para no tener que teclearlos.
/// </summary>
/// <remarks>
/// <para>
/// No es una lista cerrada ni pretende serlo: el operador escribe el suyo y se acabo. Esta
/// para que el primer arranque no sea un formulario en blanco, que es donde se abandona.
/// </para>
/// <para>
/// Van primero los de casa. Desde Canarias, un nodo espanol responde antes y trae mas
/// anuncios de EA que uno americano, y eso se nota en la lista de spots.
/// </para>
/// </remarks>
public static class NodosConocidos
{
    /// <summary>La lista, con espanoles primero.</summary>
    public static IReadOnlyList<NodoConocido> Todos { get; } =
    [
        new("EA4RCH (España)", "cluster.ea4rch.es", 7300,
            "Nodo español del Radio Club Henares. Es el que viene puesto."),
        new("EA7URC (España)", "ea7urc.ddns.net", 7300,
            "Otro nodo español, por si el primero no contesta."),
        new("DXFun (internacional)", "dxfun.com", 8000,
            "Nodo internacional muy concurrido, con mucho tráfico de DX."),
        new("VE7CC (CC Cluster)", "ve7cc.net", 23,
            "CC Cluster: admite órdenes de filtrado muy finas (set/filter)."),
        new("NC7J (Estados Unidos)", "dxc.nc7j.com", 7373,
            "Nodo americano, bueno para ver qué se oye al otro lado del charco."),
        new("DL9GTB (Alemania)", "cluster.dl9gtb.de", 8000,
            "Nodo alemán, con mucho anuncio de Europa."),
        new("Reverse Beacon Network", "telnet.reversebeacon.net", 7000,
            "No son personas: son estaciones de escucha automática. Mucho volumen; conviene filtrar."),
    ];
}
