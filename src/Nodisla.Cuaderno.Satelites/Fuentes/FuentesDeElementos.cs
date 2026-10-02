using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Satelites.Orbital;

namespace Nodisla.Cuaderno.Satelites.Fuentes;

/// <summary>Un sitio de donde se pueden traer elementos orbitales.</summary>
/// <param name="Clave">Nombre corto para los ajustes.</param>
/// <param name="Titulo">Como se llama en pantalla.</param>
/// <param name="Url">Direccion del fichero.</param>
/// <param name="Descripcion">Que trae y para que sirve.</param>
public sealed record FuenteDeElementos(string Clave, string Titulo, string Url, string Descripcion);

/// <summary>
/// De donde se traen los elementos orbitales.
/// </summary>
/// <remarks>
/// <para>
/// <b>Los ficheros clasicos de Celestrak ya no estan.</b> La direccion de toda la vida
/// (<c>celestrak.com/NORAD/elements/amateur.txt</c>) devuelve un 404 desde que Celestrak paso
/// a servir el catalogo por el punto de acceso <c>gp.php</c>. Comprobado el 25-09-2026: el de
/// aficionados y el de estaciones tripuladas responden por <c>gp.php</c>, y el
/// <c>nasabare.txt</c> de AMSAT tambien.
/// </para>
/// <para>
/// Aqui solo estan las direcciones. <b>Nada se descarga por su cuenta</b>: el modulo no toca la
/// red si no se le pide. Los elementos son un dato que caduca y hay que refrescar, pero cuando
/// y cada cuanto lo decide el operador, no el programa.
/// </para>
/// </remarks>
public static class FuentesDeElementos
{
    /// <summary>Elementos de los satelites de aficionado, que es lo que interesa aqui.</summary>
    public static FuenteDeElementos CelestrakAficionado { get; } = new(
        "celestrak-aficionado",
        Textos.T("Servicios.Satelites.Fuente.CelestrakAficionado"),
        "https://celestrak.org/NORAD/elements/gp.php?GROUP=amateur&FORMAT=tle",
        Textos.T("Servicios.Satelites.Fuente.CelestrakAficionadoDescripcion"));

    /// <summary>Elementos de las estaciones tripuladas, para la ISS.</summary>
    public static FuenteDeElementos CelestrakEstaciones { get; } = new(
        "celestrak-estaciones",
        Textos.T("Servicios.Satelites.Fuente.CelestrakEstaciones"),
        "https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=tle",
        Textos.T("Servicios.Satelites.Fuente.CelestrakEstacionesDescripcion"));

    /// <summary>El fichero de AMSAT, que es el que siguen los operadores de satelite.</summary>
    public static FuenteDeElementos Amsat { get; } = new(
        "amsat",
        "AMSAT · nasabare.txt",
        "https://www.amsat.org/tle/current/nasabare.txt",
        Textos.T("Servicios.Satelites.Fuente.AmsatDescripcion"));

    /// <summary>Todas las fuentes conocidas.</summary>
    public static IReadOnlyList<FuenteDeElementos> Todas { get; } =
        [CelestrakAficionado, CelestrakEstaciones, Amsat];
}

/// <summary>De donde han salido los elementos que se estan usando.</summary>
public enum OrigenDeLosElementos
{
    /// <summary>Recien traidos de la red.</summary>
    Red = 0,

    /// <summary>Leidos del fichero guardado la ultima vez que hubo red.</summary>
    Disco,

    /// <summary>Puestos a mano por el operador.</summary>
    Manual,
}

/// <summary>Unos elementos con su procedencia, para poder decir siempre de donde salieron.</summary>
/// <param name="Elementos">Los juegos leidos.</param>
/// <param name="ObtenidoUtc">Cuando se trajeron.</param>
/// <param name="Origen">Si vienen de la red, del disco o de la mano.</param>
/// <param name="Fuente">Quien los publica.</param>
/// <param name="TextoOriginal">
/// El fichero tal y como llego. Se conserva para poder guardarlo en disco sin volver a
/// escribirlo: rehacer un TLE desde los campos leidos pierde los digitos de control y las
/// columnas exactas, y despues nadie sabe si el fichero guardado es el que se descargo.
/// </param>
public sealed record LecturaDeElementos(
    IReadOnlyList<ElementosOrbitales> Elementos,
    DateTimeOffset ObtenidoUtc,
    OrigenDeLosElementos Origen,
    string Fuente,
    string TextoOriginal = "")
{
    /// <summary>La epoca mas antigua del lote, que es la que manda para avisar.</summary>
    public DateTimeOffset? EpocaMasAntigua =>
        Elementos.Count == 0 ? null : Elementos.Min(e => e.Epoca);

    /// <summary>Frase para el operador sobre la procedencia y la edad del lote.</summary>
    /// <param name="ahoraUtc">Momento actual.</param>
    /// <param name="frescura">Plazo dentro del cual se dan por buenos.</param>
    public string Describir(DateTimeOffset ahoraUtc, TimeSpan frescura)
    {
        if (Elementos.Count == 0)
        {
            return Textos.F("Servicios.Satelites.SinElementosDe", Fuente);
        }

        var antigua = EpocaMasAntigua!.Value;
        var edad = ElementosOrbitales.TextoDeEdad(ahoraUtc - antigua);
        var cuantos = Elementos.Count == 1
            ? Textos.T("Servicios.Satelites.UnSatelite")
            : Textos.F("Servicios.Satelites.NSatelites", Elementos.Count);

        return ahoraUtc - antigua <= frescura
            ? Textos.F("Servicios.Satelites.LoteFresco", cuantos, Fuente, edad)
            : Textos.F("Servicios.Satelites.LoteViejo", cuantos, Fuente, edad);
    }
}

/// <summary>
/// Trae elementos orbitales de la red, pero solo cuando se le manda.
/// </summary>
/// <remarks>
/// No hay temporizador ni descarga al arrancar. La descarga es una accion del operador, igual
/// que subir un cuaderno a LoTW: el programa no sale a internet por su cuenta.
/// </remarks>
public sealed class DescargaDeElementos(HttpClient cliente, ILogger<DescargaDeElementos>? registro = null)
{
    private readonly HttpClient _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
    private readonly ILogger _registro = registro ?? (ILogger)NullLogger<DescargaDeElementos>.Instance;

    /// <summary>Trae los elementos de una fuente.</summary>
    /// <param name="fuente">La fuente.</param>
    /// <param name="cancelacion">Para poder abortar.</param>
    /// <returns>Los elementos leidos con su procedencia.</returns>
    public async Task<LecturaDeElementos> TraerAsync(
        FuenteDeElementos fuente,
        CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(fuente);

        _registro.LogInformation("Descargando elementos orbitales de {Fuente}.", fuente.Titulo);
        var texto = await _cliente.GetStringAsync(fuente.Url, cancelacion).ConfigureAwait(false);
        var elementos = LectorDeElementos.Leer(texto);

        _registro.LogInformation(
            "{Cuantos} satélites leídos de {Fuente}.", elementos.Count, fuente.Titulo);

        return new LecturaDeElementos(
            elementos, DateTimeOffset.UtcNow, OrigenDeLosElementos.Red, fuente.Titulo, texto);
    }

    /// <summary>Guarda el fichero descargado tal cual, para poder trabajar sin red.</summary>
    /// <param name="lectura">El lote.</param>
    /// <param name="ruta">Fichero donde se guarda.</param>
    /// <param name="cancelacion">Para poder abortar.</param>
    public static async Task GuardarAsync(
        LecturaDeElementos lectura,
        string ruta,
        CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(lectura);

        var carpeta = Path.GetDirectoryName(ruta);
        if (!string.IsNullOrEmpty(carpeta))
        {
            Directory.CreateDirectory(carpeta);
        }

        await File.WriteAllTextAsync(ruta, lectura.TextoOriginal, cancelacion).ConfigureAwait(false);
    }

    /// <summary>Lee de disco un fichero de elementos guardado antes.</summary>
    /// <param name="ruta">Fichero.</param>
    /// <param name="fuente">Nombre de la fuente de la que salio, para poder citarla.</param>
    /// <param name="cancelacion">Para poder abortar.</param>
    /// <returns>Los elementos leidos, marcados como venidos del disco.</returns>
    public static async Task<LecturaDeElementos> LeerDeDiscoAsync(
        string ruta,
        string fuente,
        CancellationToken cancelacion = default)
    {
        var texto = await File.ReadAllTextAsync(ruta, cancelacion).ConfigureAwait(false);
        var obtenido = new DateTimeOffset(File.GetLastWriteTimeUtc(ruta), TimeSpan.Zero);
        return new LecturaDeElementos(
            LectorDeElementos.Leer(texto), obtenido, OrigenDeLosElementos.Disco, fuente, texto);
    }
}
