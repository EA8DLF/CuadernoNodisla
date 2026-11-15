using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace Nodisla.Cuaderno.Servicios.Ficheros;

/// <summary>
/// Escritura y lectura de los ficheros pequenos del programa (ajustes, cola de subidas) sin
/// perder nunca lo que habia.
/// </summary>
/// <remarks>
/// <para>
/// Antes cada uno escribia por su cuenta en un temporal de nombre fijo (<c>ajustes.json.nuevo</c>)
/// y sin cerrojo. Dos guardados a la vez —o dos programas abiertos— se pisaban el temporal: en
/// los registros de noviembre de 2026 hay «ajustes.json.nuevo … being used by another
/// process» y «No se ha podido guardar la cola de subidas», y el cambio del operador se
/// perdia con un simple aviso.
/// </para>
/// <para>
/// Aqui hay <b>un cerrojo por fichero</b> para todo el proceso, el temporal lleva un nombre
/// que no se repite, el reemplazo es atomico y, si Windows tiene el fichero cogido un momento
/// (el antivirus, el indexador), se reintenta unas pocas veces antes de rendirse.
/// </para>
/// <para>
/// Y una regla mas: <b>un fichero que no se ha podido leer no se machaca</b>. Quien no puede
/// leerlo lo aparta con <see cref="ApartarIlegible"/>; si ni siquiera eso sale, queda apuntado
/// y la siguiente escritura lo aparta antes de escribir, o falla sin tocarlo.
/// </para>
/// </remarks>
public static class FicheroSeguro
{
    /// <summary>Lo que se anade al nombre del fichero apartado, antes de la fecha.</summary>
    public const string MarcaDeDanado = ".dañado-";

    private static readonly UTF8Encoding Utf8SinBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly ConcurrentDictionary<string, object> Cerrojos =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, byte> PendientesDeApartar =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Esperas entre intentos cuando Windows tiene el fichero cogido.</summary>
    private static readonly TimeSpan[] Esperas =
    [
        TimeSpan.FromMilliseconds(40),
        TimeSpan.FromMilliseconds(120),
        TimeSpan.FromMilliseconds(360),
    ];

    /// <summary>El cerrojo de un fichero: el mismo para todo el proceso.</summary>
    /// <param name="ruta">Ruta del fichero.</param>
    public static object CerrojoDe(string ruta) =>
        Cerrojos.GetOrAdd(Path.GetFullPath(ruta), _ => new object());

    /// <summary>
    /// Escribe el texto en UTF-8 sin BOM: primero en un temporal de nombre unico y luego
    /// reemplazando el bueno de una vez. Lanza si al final no se ha podido.
    /// </summary>
    /// <param name="ruta">Fichero de destino.</param>
    /// <param name="texto">Contenido.</param>
    /// <param name="esperar">Como esperar entre intentos; sustituible en las pruebas.</param>
    public static void Escribir(string ruta, string texto, Action<TimeSpan>? esperar = null)
    {
        ArgumentNullException.ThrowIfNull(ruta);
        ArgumentNullException.ThrowIfNull(texto);
        ruta = Path.GetFullPath(ruta);

        lock (CerrojoDe(ruta))
        {
            var carpeta = Path.GetDirectoryName(ruta);
            if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

            // Lo que no se pudo leer ni apartar se aparta ahora, antes de escribir encima. Si
            // tampoco se puede, se lanza y el fichero del operador se queda como estaba.
            if (PendientesDeApartar.ContainsKey(ruta))
            {
                if (File.Exists(ruta)) Mover(ruta, NombreDeApartado(ruta, DateTimeOffset.Now), esperar);
                PendientesDeApartar.TryRemove(ruta, out _);
            }

            var temporal = $"{ruta}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var flujo = new FileStream(temporal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var octetos = Utf8SinBom.GetBytes(texto);
                    flujo.Write(octetos, 0, octetos.Length);

                    // Al disco de verdad antes de reemplazar: un corte de luz no debe dejar
                    // el fichero bueno cambiado por uno vacio.
                    flujo.Flush(flushToDisk: true);
                }

                Reintentar(() => File.Move(temporal, ruta, overwrite: true), esperar);
            }
            finally
            {
                try
                {
                    if (File.Exists(temporal)) File.Delete(temporal);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Un temporal huerfano no hace dano: lleva nombre unico y no se vuelve a usar.
                }
            }
        }
    }

    /// <summary>
    /// Lee el fichero entero, con o sin BOM, reintentando si Windows lo tiene cogido un momento.
    /// </summary>
    /// <param name="ruta">Fichero a leer.</param>
    /// <param name="esperar">Como esperar entre intentos; sustituible en las pruebas.</param>
    public static string Leer(string ruta, Action<TimeSpan>? esperar = null)
    {
        ArgumentNullException.ThrowIfNull(ruta);
        lock (CerrojoDe(ruta))
        {
            string texto = string.Empty;
            Reintentar(() => texto = File.ReadAllText(ruta), esperar);
            return texto;
        }
    }

    /// <summary>
    /// Aparta un fichero que no se ha podido leer, con la fecha en el nombre
    /// (<c>ajustes.json.dañado-20261115-123907</c>), para que la siguiente escritura no lo pise.
    /// </summary>
    /// <param name="ruta">Fichero ilegible.</param>
    /// <param name="ahora">Momento que va en el nombre; el actual si no se da.</param>
    /// <returns>
    /// La ruta del fichero apartado, o nulo si no se ha podido apartar. En ese caso queda
    /// apuntado y <see cref="Escribir"/> lo apartara —o fallara— antes de escribir encima.
    /// </returns>
    public static string? ApartarIlegible(string ruta, DateTimeOffset? ahora = null)
    {
        ArgumentNullException.ThrowIfNull(ruta);
        ruta = Path.GetFullPath(ruta);

        lock (CerrojoDe(ruta))
        {
            try
            {
                if (!File.Exists(ruta)) return null;
                var destino = NombreDeApartado(ruta, ahora ?? DateTimeOffset.Now);
                Mover(ruta, destino, esperar: null);
                PendientesDeApartar.TryRemove(ruta, out _);
                return destino;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PendientesDeApartar[ruta] = 0;
                return null;
            }
        }
    }

    /// <summary>
    /// Apunta que el fichero no se ha podido leer: la siguiente escritura lo apartara antes.
    /// </summary>
    /// <param name="ruta">Fichero que no se ha podido leer.</param>
    public static void MarcarIlegible(string ruta)
    {
        ArgumentNullException.ThrowIfNull(ruta);
        PendientesDeApartar[Path.GetFullPath(ruta)] = 0;
    }

    private static string NombreDeApartado(string ruta, DateTimeOffset ahora)
    {
        var marca = ahora.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var destino = ruta + MarcaDeDanado + marca;
        for (var n = 2; File.Exists(destino); n++)
        {
            destino = $"{ruta}{MarcaDeDanado}{marca}-{n}";
        }

        return destino;
    }

    private static void Mover(string origen, string destino, Action<TimeSpan>? esperar) =>
        Reintentar(() => File.Move(origen, destino, overwrite: false), esperar);

    private static void Reintentar(Action accion, Action<TimeSpan>? esperar)
    {
        esperar ??= Thread.Sleep;
        for (var intento = 0; ; intento++)
        {
            try
            {
                accion();
                return;
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException
                && ex is not FileNotFoundException and not DirectoryNotFoundException
                && intento < Esperas.Length)
            {
                esperar(Esperas[intento]);
            }
        }
    }
}
