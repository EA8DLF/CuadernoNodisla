using System.Globalization;
using System.Text.RegularExpressions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>
/// Un anuncio de cluster ya analizado: el <see cref="Spot"/> del contrato y lo poco que el
/// contrato no recoge.
/// </summary>
/// <param name="Spot">El anuncio, listo para el cuaderno.</param>
/// <param name="Linea">La linea tal y como llego, por si hay que enseniarla o registrarla.</param>
public sealed record AnuncioDeCluster(Spot Spot, string Linea)
{
    /// <summary>
    /// Hora que da el nodo, tal cual viene, por ejemplo <c>1234Z</c>.
    /// </summary>
    /// <remarks>
    /// No es la hora del anuncio para el cuaderno —esa es <c>Spot.RecibidoUtc</c>, que es la
    /// de nuestro reloj— sino la que puso el nodo. Sirve para cotejar cuando un nodo va
    /// atrasado o reenvia anuncios viejos.
    /// </remarks>
    public string? HoraDelCluster { get; init; }

    /// <summary>
    /// Continente que algunos nodos anaden detras de la hora, por ejemplo <c>EU</c>.
    /// </summary>
    /// <remarks>
    /// Es la idea que tiene el nodo, no la nuestra: el continente que vale es el que sale de
    /// resolver el indicativo, y ese ya va en el <see cref="Spot"/>. Se guarda para poder
    /// comparar cuando un anuncio parece raro. Si lo que el nodo pone ahi es un localizador,
    /// no llega aqui: se convierte en <c>Spot.Locator</c>.
    /// </remarks>
    public string? ContinenteDelNodo { get; init; }
}

/// <summary>
/// Analiza las lineas que escupe un cluster de DX y saca de ellas los anuncios.
/// </summary>
/// <remarks>
/// No hay un formato de anuncio, hay varios. El clasico de DXSpider y AR-Cluster va en
/// columnas fijas; los skimmers (Reverse Beacon Network) meten decibelios, velocidad y el
/// motivo de la escucha; y hay nodos que anaden el continente o el locator detras de la
/// hora, que ponen la hora con tres cifras, que se saltan los dos puntos del anunciante o
/// que escriben la frecuencia con coma decimal.
///
/// Todo lo que no sea un anuncio (los avisos <c>WWV</c>, los mensajes de otros operadores,
/// las respuestas a las ordenes) se devuelve como nulo para que el cluster lo saque por
/// <see cref="IFuenteSpots.LineaRecibida"/>: el operador quiere verlo, no perderlo.
///
/// La trampa clasica es la frecuencia: <b>los clusters la dan en kilohercios</b>. Leerla
/// como megahercios se equivoca en tres ordenes de magnitud y manda al operador a otra
/// banda.
/// </remarks>
public sealed partial class AnalizadorSpot
{
    private readonly IResolutorDxcc _resolutor;

    /// <summary>Crea el analizador.</summary>
    /// <param name="fuente">Nombre de la fuente que se copia en cada anuncio.</param>
    /// <param name="resolutor">
    /// Resolutor DXCC con el que rellenar la entidad, el pais y el continente. Si no se da,
    /// se usa el del recurso incrustado.
    /// </param>
    public AnalizadorSpot(string fuente, IResolutorDxcc? resolutor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fuente);
        Fuente = fuente;
        _resolutor = resolutor ?? ResolutorDxcc.Predeterminado;
    }

    /// <summary>Nombre de la fuente que se copia en cada anuncio.</summary>
    public string Fuente { get; }

    /// <summary>
    /// Analiza una linea. Devuelve nulo si la linea no es un anuncio.
    /// </summary>
    /// <param name="linea">Linea recibida del cluster, ya sin saltos ni controles.</param>
    /// <param name="recibidoUtc">Momento en que llego. Si no se da, se toma el de ahora.</param>
    public AnuncioDeCluster? Analizar(string? linea, DateTimeOffset? recibidoUtc = null)
    {
        if (string.IsNullOrWhiteSpace(linea)) return null;

        var m = Anuncio().Match(linea.Trim());
        if (!m.Success) return null;

        if (!Indicativo.TryParse(m.Groups["dx"].Value, out var anunciado)) return null;

        var frecuencia = LeerFrecuencia(m.Groups["khz"].Value);
        if (frecuencia is null) return null;

        var (anunciante, automatica) = LeerAnunciante(m.Groups["anunciante"].Value);
        var (comentario, hora, cola) = PartirElResto(m.Groups["resto"].Value);
        var lectura = VocabularioCluster.Leer(comentario);
        var locator = VocabularioCluster.BuscarLocator(cola);
        var cuando = recibidoUtc ?? DateTimeOffset.UtcNow;

        var spot = new Spot(anunciado, frecuencia.Value, anunciante, comentario, cuando, Fuente)
        {
            ModoAnunciado = lectura.Modo,
            Locator = locator,
            Referencias = lectura.Referencias,
            Decibelios = lectura.Decibelios,
            PalabrasPorMinuto = lectura.PalabrasPorMinuto,
            EsDeEscuchaAutomatica =
                automatica || lectura.PalabrasPorMinuto is not null || lectura.Decibelios is not null,
        };
        spot = ConEntidad(spot, cuando);

        return new AnuncioDeCluster(spot, linea)
        {
            HoraDelCluster = hora,
            ContinenteDelNodo = locator.EsVacio ? cola : null,
        };
    }

    /// <summary>Rellena la entidad DXCC, el pais y el continente del anuncio.</summary>
    /// <remarks>
    /// <c>EsNuevoEnBandaYModo</c> y <c>EsEntidadNueva</c> no se tocan: los rellena el
    /// cuaderno, que es quien sabe lo que hay trabajado.
    /// </remarks>
    private Spot ConEntidad(Spot spot, DateTimeOffset cuando)
    {
        var resultado = _resolutor.Resolver(spot.Indicativo, DateOnly.FromDateTime(cuando.UtcDateTime));
        if (resultado.Entidad is null) return spot;

        return spot with
        {
            Dxcc = resultado.Entidad.Numero,
            Pais = resultado.Entidad.NombreParaMostrar,
            Continente = resultado.Continente ?? resultado.Entidad.Continente,
        };
    }

    /// <summary>
    /// Lee la frecuencia de un anuncio, que viene en kilohercios.
    /// </summary>
    /// <remarks>
    /// Si el numero leido en kilohercios no cae en ninguna banda pero leido en megahercios
    /// si, se toma como megahercios: hay pasarelas web y nodos caseros que los mezclan, y
    /// mas vale entenderlos que tirar el anuncio.
    ///
    /// Es publico para poder probarlo por separado; <b>no forma parte de la interfaz de
    /// uso</b> del analizador, que es <see cref="Analizar"/>. No construyas encima.
    /// </remarks>
    public static Frecuencia? LeerFrecuencia(string texto)
    {
        var limpio = texto.Replace(',', '.');
        if (!decimal.TryParse(limpio, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor)) return null;
        if (valor <= 0) return null;

        var enKhz = Frecuencia.DesdeKilohercios(valor);
        if (!Banda.DesdeFrecuencia(enKhz).EsVacia) return enKhz;

        var enMhz = Frecuencia.DesdeMegahercios(valor);
        return !Banda.DesdeFrecuencia(enMhz).EsVacia ? enMhz : enKhz;
    }

    /// <summary>
    /// Lee el indicativo de quien anuncia, quitandole el SSID del nodo.
    /// </summary>
    /// <remarks>
    /// Las estaciones de escucha automatica se identifican con <c>-#</c> detras del
    /// indicativo; los nodos, con un numero o una letra. El sufijo no es parte del indicativo
    /// y hay que quitarlo antes de normalizarlo, porque <c>Indicativo</c> convierte los
    /// guiones en barras.
    ///
    /// Es publico para poder probarlo por separado; <b>no forma parte de la interfaz de
    /// uso</b> del analizador, que es <see cref="Analizar"/>. No construyas encima.
    /// </remarks>
    public static (Indicativo Anunciante, bool DeEscuchaAutomatica) LeerAnunciante(string texto)
    {
        var bruto = texto.Trim().TrimEnd(':');
        var automatica = false;

        var guion = bruto.LastIndexOf('-');
        if (guion > 0)
        {
            var sufijo = bruto[(guion + 1)..];
            automatica = sufijo is "#";
            bruto = bruto[..guion];
        }

        return (Indicativo.TryParse(bruto, out var i) ? i : Indicativo.Crudo(bruto), automatica);
    }

    /// <summary>
    /// Parte lo que hay detras del indicativo anunciado en comentario, hora y cola.
    /// </summary>
    /// <remarks>
    /// La hora es lo unico que va siempre en el mismo sitio (al final), asi que se busca
    /// desde atras: lo de delante es el comentario y lo de detras, cuando lo hay, es el
    /// continente o el locator que anaden algunos nodos.
    ///
    /// Es publico para poder probarlo por separado; <b>no forma parte de la interfaz de
    /// uso</b> del analizador, que es <see cref="Analizar"/>. No construyas encima.
    /// </remarks>
    public static (string? Comentario, string? Hora, string? Cola) PartirElResto(string resto)
    {
        if (string.IsNullOrWhiteSpace(resto)) return (null, null, null);

        var horas = Hora().Matches(resto);
        if (horas.Count == 0)
        {
            var solo = resto.Trim();
            return (solo.Length == 0 ? null : solo, null, null);
        }

        var ultima = horas[^1];
        var comentario = resto[..ultima.Index].Trim();
        var cola = resto[(ultima.Index + ultima.Length)..].Trim();
        return (
            comentario.Length == 0 ? null : comentario,
            ultima.Value.ToUpperInvariant(),
            cola.Length == 0 ? null : cola);
    }

    /// <summary>
    /// Cabecera de un anuncio: quien lo manda, la frecuencia en kilohercios y a quien oye.
    /// Los dos puntos detras del anunciante son opcionales porque no todos los nodos los
    /// ponen.
    /// </summary>
    [GeneratedRegex(
        @"^DX\s+de\s+(?<anunciante>[A-Za-z0-9/#@_-]{3,20})\s*:?\s+(?<khz>\d{1,10}(?:[.,]\d{1,4})?)\s+(?<dx>[A-Za-z0-9/]{3,24})(?:\s+(?<resto>.*))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Anuncio();

    /// <summary>Hora del cluster: cuatro cifras y una zeta, aunque algunos nodos dan tres.</summary>
    [GeneratedRegex(@"\b\d{3,4}Z\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Hora();
}
