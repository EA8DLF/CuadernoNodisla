using System.Globalization;
using System.Xml.Linq;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>
/// Lee la lista de clusters de Log4OM (<c>%APPDATA%\Log4OM2\cluster.xml</c>).
/// </summary>
/// <remarks>
/// Son casi doscientos nodos con su direccion, su puerto y su guion de arranque, ya
/// depurados por el uso. Importarlos ahorra al operador teclear la lista entera y evita
/// inventarse puertos: el 7300 es lo habitual, pero hay nodos en el 23, en el 8000 y en el
/// 7373. Se lee con tolerancia: un nodo mal escrito se salta, no tumba la importacion.
/// </remarks>
public static class ListaDeClustersLog4Om
{
    /// <summary>Ruta del fichero de Log4OM en este equipo.</summary>
    public static string RutaHabitual => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Log4OM2",
        "cluster.xml");

    /// <summary>Lee la lista del fichero dado.</summary>
    /// <param name="ruta">Ruta del <c>cluster.xml</c>.</param>
    /// <param name="indicativo">Indicativo con el que se accedera a los nodos.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public static async Task<IReadOnlyList<OpcionesCluster>> LeerAsync(
        string ruta,
        Indicativo indicativo,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruta);

        await using var flujo = new FileStream(
            ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true);
        var documento = await XDocument.LoadAsync(flujo, LoadOptions.None, ct).ConfigureAwait(false);
        return Leer(documento, indicativo);
    }

    /// <summary>Lee la lista de un documento ya cargado.</summary>
    public static IReadOnlyList<OpcionesCluster> Leer(XDocument documento, Indicativo indicativo)
    {
        ArgumentNullException.ThrowIfNull(documento);
        if (documento.Root is null) return [];

        var lista = new List<OpcionesCluster>();
        foreach (var nodo in documento.Root.Elements("SingleCluster"))
        {
            var opciones = LeerNodo(nodo, indicativo);
            if (opciones is not null) lista.Add(opciones);
        }
        return lista;
    }

    private static OpcionesCluster? LeerNodo(XElement nodo, Indicativo indicativo)
    {
        var servidor = Texto(nodo, "HostAddress");
        if (string.IsNullOrWhiteSpace(servidor)) return null;

        var nombre = Texto(nodo, "ClusterName");
        if (string.IsNullOrWhiteSpace(nombre)) nombre = servidor;

        var puerto = Entero(Texto(nodo, "Port")) ?? 7300;
        if (puerto is <= 0 or > 65535) puerto = 7300;

        var ordenes = nodo.Element("Commands")?.Elements("string")
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .ToArray();

        // Log4OM guarda un indicativo por nodo; si esta vacio, vale el de la estacion.
        var propio = Texto(nodo, "Callsign");
        var suyo = !string.IsNullOrWhiteSpace(propio) && Indicativo.TryParse(propio, out var i) ? i : indicativo;

        return new OpcionesCluster
        {
            Nombre = nombre,
            Servidor = servidor.Trim(),
            Puerto = puerto,
            Indicativo = suyo,
            Sufijo = Vacio(Texto(nodo, "Ssid")),
            Contrasena = Vacio(Texto(nodo, "Password")),
            GuionDeArranque = ordenes is { Length: > 0 } ? ordenes : OpcionesCluster.GuionPredeterminado,
        };
    }

    private static string Texto(XElement nodo, string nombre) => nodo.Element(nombre)?.Value ?? string.Empty;

    private static string? Vacio(string v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static int? Entero(string v) =>
        int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
