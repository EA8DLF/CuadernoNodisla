using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>Un nodo de cluster de los que se ofrecen hechos.</summary>
/// <param name="Nombre">Como se llama en la lista.</param>
/// <param name="Servidor">Maquina a la que conectarse.</param>
/// <param name="Puerto">Puerto de Telnet.</param>
/// <param name="Nota">Para que sirve o que tiene de particular.</param>
/// <param name="Comprobado">
/// Se ha visto contestar en ese puerto (sin entrar: solo abrir y leer el saludo). Los que no,
/// se ofrecen marcados «sin comprobar».
/// </param>
/// <param name="EsSkimmer">Red de escucha automatica (RBN): todo lo que trae es de maquina.</param>
public sealed record NodoConocido(
    string Nombre,
    string Servidor,
    int Puerto,
    string Nota,
    bool Comprobado = true,
    bool EsSkimmer = false)
{
    /// <summary>Como se escribe en un desplegable.</summary>
    public string ParaElDesplegable => Comprobado
        ? $"{Nombre} — {Servidor}:{Puerto}"
        : $"{Nombre} — {Servidor}:{Puerto} ({Textos.T("Servicios.Cluster.SinComprobar")})";

    /// <summary>
    /// Guion de arranque que conviene a este nodo.
    /// </summary>
    /// <remarks>
    /// La red de escucha automatica no es un DXSpider: no tiene anuncios guardados que pedir
    /// con <c>SH/DX</c>, asi que solo se entra con el indicativo.
    /// </remarks>
    public IReadOnlyList<string> GuionRecomendado => EsSkimmer
        ? [OpcionesCluster.MarcaIndicativo]
        : OpcionesCluster.GuionPredeterminado;
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
/// <para>
/// <b>Comprobacion del 2 de octubre de 2026</b>: se abrio cada puerto y se leyo el saludo,
/// sin mandar ningun indicativo. Contestaron todos los marcados como comprobados. Los dos
/// espanoles no resolvian en el DNS ese dia y DL9GTB no contestaba: siguen en la lista
/// porque son conocidos, pero salen marcados «sin comprobar».
/// </para>
/// </remarks>
public static class NodosConocidos
{
    /// <summary>La lista, con espanoles primero, despues Europa, America y las redes de escucha.</summary>
    public static IReadOnlyList<NodoConocido> Todos { get; } =
    [
        new("EA4RCH (España)", "cluster.ea4rch.es", 7300,
            Textos.T("Servicios.Cluster.Nodo.Ea4rch"), Comprobado: false),
        new("EA7URC (España)", "ea7urc.ddns.net", 7300,
            Textos.T("Servicios.Cluster.Nodo.Ea7urc"), Comprobado: false),
        new("DB0SUE (Alemania)", "db0sue.de", 8000,
            Textos.T("Servicios.Cluster.Nodo.Db0sue")),
        new("ON0DXK (Bélgica)", "on0dxk.dyndns.org", 8000,
            Textos.T("Servicios.Cluster.Nodo.On0dxk")),
        new("GB7TLH (Reino Unido)", "gb7djk.dxcluster.net", 7300,
            Textos.T("Servicios.Cluster.Nodo.Gb7tlh")),
        new("DL9GTB (Alemania)", "cluster.dl9gtb.de", 8000,
            Textos.T("Servicios.Cluster.Nodo.Dl9gtb"), Comprobado: false),
        new("DXFun (internacional)", "dxfun.com", 8000,
            Textos.T("Servicios.Cluster.Nodo.Dxfun")),
        new("HamQTH (internacional)", "hamqth.com", 7300,
            Textos.T("Servicios.Cluster.Nodo.Hamqth")),
        new("VE7CC (CC Cluster)", "ve7cc.net", 23,
            Textos.T("Servicios.Cluster.Nodo.Ve7cc")),
        new("NC7J (Estados Unidos)", "dxc.nc7j.com", 7373,
            Textos.T("Servicios.Cluster.Nodo.Nc7j")),
        new("W3LPL (Estados Unidos)", "w3lpl.net", 7373,
            Textos.T("Servicios.Cluster.Nodo.W3lpl")),
        new("K3LR (Estados Unidos)", "dx.k3lr.com", 23,
            Textos.T("Servicios.Cluster.Nodo.K3lr")),
        new("W1NR (Estados Unidos)", "dxc.w1nr.net", 23,
            Textos.T("Servicios.Cluster.Nodo.W1nr")),
        new("AE5E (CC Cluster)", "dxspots.com", 23,
            Textos.T("Servicios.Cluster.Nodo.Ae5e")),
        new("RBN · CW y RTTY", "telnet.reversebeacon.net", 7000,
            Textos.T("Servicios.Cluster.Nodo.Rbn"), EsSkimmer: true),
        new("RBN · FT8 y FT4", "telnet.reversebeacon.net", 7001,
            Textos.T("Servicios.Cluster.Nodo.RbnDigital"), EsSkimmer: true),
    ];

    /// <summary>El nodo conocido que hay en esa maquina y ese puerto, o nulo.</summary>
    /// <param name="servidor">Maquina.</param>
    /// <param name="puerto">Puerto.</param>
    /// <returns>El nodo, o nulo si no es ninguno de la lista.</returns>
    public static NodoConocido? Buscar(string? servidor, int puerto) =>
        Todos.FirstOrDefault(n =>
            string.Equals(n.Servidor, servidor?.Trim(), StringComparison.OrdinalIgnoreCase) && n.Puerto == puerto);
}
