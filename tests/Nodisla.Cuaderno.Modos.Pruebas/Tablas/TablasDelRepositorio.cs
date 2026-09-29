using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Pruebas.Tablas;

/// <summary>
/// Localiza los ficheros de tablas del repositorio para las pruebas de los modos nuevos.
/// </summary>
/// <remarks>
/// Las pruebas tienen que pasar por las tablas de verdad, no por los codigos de pruebas, y los
/// ficheros viven en <c>src/Nodisla.Cuaderno.Modos/Tablas</c>. Se buscan subiendo desde la
/// carpeta de la prueba hasta encontrar el proyecto, igual que hace el banco para escribir sus
/// resultados. Si no se encuentran, las pruebas que dependen de ellas fallan: un codigo de
/// pruebas que pasara en silencio seria justo lo que no se quiere.
/// </remarks>
public static class TablasDelRepositorio
{
    private static readonly Lazy<string> Carpeta = new(Buscar);

    private static readonly Lazy<TablaLdpc> LazyMsk144 = new(() => Cargar("tablas-msk144.txt", 128, 90));
    private static readonly Lazy<TablaLdpc> LazyMsk40 = new(() => Cargar("tablas-msk40.txt", 32, 16));
    private static readonly Lazy<TablaLdpc> LazyFst4 = new(() => Cargar("tablas-fst4.txt", 240, 101));
    private static readonly Lazy<TablaLdpc> LazyFst4w = new(() => Cargar("tablas-fst4w.txt", 240, 74));

    /// <summary>Ruta completa de un fichero de tablas del repositorio.</summary>
    public static string Ruta(string nombre) => Path.Combine(Carpeta.Value, nombre);

    /// <summary>La tabla LDPC(128,90) de MSK144.</summary>
    public static TablaLdpc Msk144 => LazyMsk144.Value;

    /// <summary>La tabla LDPC(32,16) de los mensajes cortos de MSK144.</summary>
    public static TablaLdpc Msk40 => LazyMsk40.Value;

    /// <summary>La tabla LDPC(240,101) de FST4.</summary>
    public static TablaLdpc Fst4 => LazyFst4.Value;

    /// <summary>La tabla LDPC(240,74) de FST4W.</summary>
    public static TablaLdpc Fst4w => LazyFst4w.Value;

    private static TablaLdpc Cargar(string nombre, int longitud, int bits)
    {
        var tabla = TablaLdpc.Cargar(nombre, longitud, bits, Ruta(nombre));
        if (!tabla.EsElCodigoReal)
            throw new InvalidOperationException($"No se pudo cargar la tabla real {nombre}: {tabla.Procedencia}");
        return tabla;
    }

    private static string Buscar()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null)
        {
            var candidata = Path.Combine(carpeta.FullName, "src", "Nodisla.Cuaderno.Modos", "Tablas");
            if (Directory.Exists(candidata)) return candidata;
            carpeta = carpeta.Parent;
        }
        // Ultimo recurso: junto al programa, que es donde van en la instalacion.
        return AppContext.BaseDirectory;
    }
}
