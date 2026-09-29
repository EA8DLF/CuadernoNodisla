using System.Text;
using Nodisla.Cuaderno.Adif;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Adif.Pruebas;

/// <summary>Utilidades comunes a todas las pruebas de ADIF.</summary>
internal static class Ayudas
{
    /// <summary>Lee un ADIF a partir de un texto.</summary>
    public static Task<LecturaAdif> LeerAsync(string adif) => LeerAsync(Encoding.UTF8.GetBytes(adif));

    /// <summary>Lee un ADIF a partir de sus bytes, para poder probar codificaciones raras.</summary>
    public static async Task<LecturaAdif> LeerAsync(byte[] bytes)
    {
        using var flujo = new MemoryStream(bytes);
        return await new LectorAdif().LeerAsync(flujo);
    }

    /// <summary>Exporta contactos y devuelve el texto resultante.</summary>
    public static async Task<string> ExportarAsync(IEnumerable<Qso> qsos, OpcionesAdif? opciones = null)
    {
        using var destino = new MemoryStream();
        await new EscritorAdif().EscribirAsync(Enumerar(qsos), destino, opciones);
        return Encoding.UTF8.GetString(destino.ToArray());
    }

    /// <summary>Exporta contactos y devuelve los bytes, sin suponer codificacion.</summary>
    public static async Task<byte[]> ExportarBytesAsync(IEnumerable<Qso> qsos, OpcionesAdif? opciones = null)
    {
        using var destino = new MemoryStream();
        await new EscritorAdif().EscribirAsync(Enumerar(qsos), destino, opciones);
        return destino.ToArray();
    }

    /// <summary>Convierte una coleccion normal en la secuencia asincrona que pide el contrato.</summary>
#pragma warning disable CS1998 // La secuencia es sincrona a proposito: es una ayuda de pruebas.
    public static async IAsyncEnumerable<Qso> Enumerar(IEnumerable<Qso> qsos)
#pragma warning restore CS1998
    {
        foreach (var q in qsos) yield return q;
    }

    /// <summary>
    /// Devuelve los campos de cada registro tal y como estan en el fichero, sin interpretarlos.
    /// Es la referencia contra la que se comprueba que la ida y vuelta no pierde nada.
    /// </summary>
    public static async Task<List<List<CampoAdif>>> CamposCrudosAsync(Stream origen)
    {
        var registros = new List<List<CampoAdif>>();
        var actual = new List<CampoAdif>();
        var enCabecera = true;
        var analizador = new AnalizadorAdi(origen, (_, _, _) => { });

        while (await analizador.SiguienteAsync(CancellationToken.None) is { } token)
        {
            switch (token.Clase)
            {
                case ClaseDeToken.Campo:
                    actual.Add(token.Campo);
                    break;
                case ClaseDeToken.FinDeCabecera:
                    actual.Clear();
                    enCabecera = false;
                    break;
                case ClaseDeToken.FinDeRegistro:
                    enCabecera = false;
                    registros.Add(actual);
                    actual = [];
                    break;
                default:
                    break;
            }
        }
        if (!enCabecera && actual.Count > 0) registros.Add(actual);
        return registros;
    }

    /// <summary>Campos crudos de un texto ADIF.</summary>
    public static async Task<List<List<CampoAdif>>> CamposCrudosAsync(string adif)
    {
        using var flujo = new MemoryStream(Encoding.UTF8.GetBytes(adif));
        return await CamposCrudosAsync(flujo);
    }

    /// <summary>Campos crudos de un fichero.</summary>
    public static async Task<List<List<CampoAdif>>> CamposCrudosDeFicheroAsync(string ruta)
    {
        await using var flujo = File.OpenRead(ruta);
        return await CamposCrudosAsync(flujo);
    }

    /// <summary>Busca el valor de un campo en un registro crudo.</summary>
    public static string? Valor(this IEnumerable<CampoAdif> campos, string nombre)
    {
        foreach (var c in campos)
        {
            if (string.Equals(c.Nombre, nombre, StringComparison.OrdinalIgnoreCase)) return c.Valor;
        }
        return null;
    }
}

/// <summary>
/// Localiza los respaldos reales de Log4OM que sirven de banco de pruebas principal.
/// </summary>
/// <remarks>
/// Son ficheros del cuaderno de EA8DLF, de solo lectura. Si la maquina que ejecuta las pruebas
/// no los tiene, las pruebas que dependen de ellos se dan por superadas: el resto de la bateria
/// cubre el comportamiento con ficheros sinteticos.
/// </remarks>
internal static class RespaldosReales
{
    /// <summary>Carpeta donde Log4OM deja sus respaldos.</summary>
    public static string Carpeta { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Log4OM2",
        "backup");

    /// <summary>Todos los respaldos disponibles, del mas reciente al mas antiguo.</summary>
    public static IReadOnlyList<string> Todos { get; } = Directory.Exists(Carpeta)
        ? Directory.GetFiles(Carpeta, "*.adi").OrderByDescending(f => f).ToArray()
        : [];

    /// <summary>Respaldo mas reciente, o nulo si esta maquina no tiene ninguno.</summary>
    public static string? MasReciente => Todos.Count > 0 ? Todos[0] : null;

    /// <summary>Hay respaldos con los que probar.</summary>
    public static bool Hay => Todos.Count > 0;
}
