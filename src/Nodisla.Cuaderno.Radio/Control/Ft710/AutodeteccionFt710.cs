using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>Un equipo Yaesu encontrado al buscar por los puertos serie.</summary>
/// <param name="Puerto">Puerto donde contesto, por ejemplo <c>COM3</c>.</param>
/// <param name="Baudios">Velocidad a la que contesto.</param>
/// <param name="Identificador">Lo que contesto a <c>ID;</c>, por ejemplo <c>0800</c>.</param>
public sealed record EquipoEncontrado(string Puerto, int Baudios, string Identificador)
{
    /// <summary>El equipo encontrado es un FT-710.</summary>
    public bool EsFt710 => Identificador == ControlFt710.IdentificadorFt710;

    /// <summary>Nombre para enseñarselo al operador.</summary>
    public string Descripcion => EsFt710
        ? Textos.F("Servicios.Radio.EquipoEnPuerto", "Yaesu FT-710", Puerto, Baudios)
        : Textos.F("Servicios.Radio.YaesuIdEnPuerto", Identificador, Puerto, Baudios);
}

/// <summary>Lo que ha dado de si probar un puerto a una velocidad.</summary>
/// <param name="SeAbrio">
/// El puerto se pudo abrir. Falso si no existe o esta cogido —por otro programa o por la propia
/// conexion del Cuaderno—: entonces <b>no se sabe</b> si el equipo esta ahi, que no es lo mismo
/// que «no contesta».
/// </param>
/// <param name="Identificador">Las cuatro cifras de <c>ID;</c>, o nulo si no contesto un Yaesu.</param>
public sealed record ResultadoDeSonda(bool SeAbrio, string? Identificador)
{
    /// <summary>El puerto no se pudo abrir.</summary>
    public static ResultadoDeSonda NoSeAbre { get; } = new(false, null);

    /// <summary>Se abrio y no contesto ningun Yaesu.</summary>
    public static ResultadoDeSonda NoContesta { get; } = new(true, null);
}

/// <summary>Prueba un puerto a una velocidad. Las pruebas meten aqui uno de mentira.</summary>
/// <param name="puerto">Puerto.</param>
/// <param name="baudios">Velocidad.</param>
/// <param name="ct">Testigo de cancelacion.</param>
/// <returns>Lo que ha pasado.</returns>
public delegate Task<ResultadoDeSonda> SondeadorDePuertos(string puerto, int baudios, CancellationToken ct);

/// <summary>
/// Busca el equipo por los puertos serie de la maquina.
/// </summary>
/// <remarks>
/// <para>
/// El equipo se identifica <b>por lo que contesta a <c>ID;</c></b>, jamas por el numero de
/// puerto. En la maquina de Jose hay un puerto que no es la radio y que a 115200 escupe su
/// propio registro de depuracion: cualquier busqueda que se fie del puerto, o que de por bueno
/// lo primero que llegue, acabaria hablandole a ese cacharro. Por eso la respuesta tiene que
/// encajar exactamente con <c>ID</c> y cuatro cifras.
/// </para>
/// <para>
/// <b>El puerto <i>Standard</i> del equipo no se abre al buscar, y si hay un <i>Enhanced</i> solo
/// se mira ese.</b> El manual CAT del FT-710 (2306-C, pags. 1-2) reparte asi los dos puertos del
/// CP2105: <i>Enhanced COM Port</i> (CAT-1), comunicaciones CAT; <i>Standard COM Port</i> (CAT-2),
/// «TX Controls (PTT control, CW Keying, Digital Mode Operation)» o CAT, a 4800 de fabrica. El
/// 15-11-2026 la busqueda, con el <i>Enhanced</i> cogido por la propia conexion del programa, dio
/// por bueno el <i>Standard</i> a 4800 y lo guardo: CAT a medias (48 mandos de 51) por el puerto
/// que maneja la transmision. Y de paso abria a cinco velocidades un puerto que no es la radio.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static partial class AutodeteccionFt710
{
    /// <summary>
    /// Velocidades que se prueban, en orden.
    /// </summary>
    /// <remarks>
    /// El equipo aparecio un dia en COM3 a 115200 y dos dias despues en COM15 a 38400:
    /// <b>cambiaron el puerto y la velocidad a la vez</b>. Por eso se prueban todas las que el
    /// equipo admite y se identifica por <c>ID;</c>, nunca por donde estaba la ultima vez.
    /// </remarks>
    public static IReadOnlyList<int> Velocidades { get; } = [115200, 38400, 19200, 9600, 4800];

    /// <summary>
    /// Comprueba si por un puerto y velocidad concretos contesta un equipo Yaesu.
    /// </summary>
    /// <remarks>
    /// Sirve para probar primero por donde estaba la ultima vez, que es lo rapido. Si no
    /// contesta, hay que barrer: lo guardado es una pista, nunca una certeza.
    /// </remarks>
    /// <param name="puerto">Puerto a probar.</param>
    /// <param name="baudios">Velocidad a probar.</param>
    /// <param name="registro">Donde anotar la comprobacion.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El equipo si contesta, o nulo.</returns>
    public static async Task<EquipoEncontrado?> ComprobarAsync(
        string puerto,
        int baudios,
        ILogger? registro = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(puerto))
        {
            return null;
        }

        var anotador = registro ?? NullLogger.Instance;
        var sonda = await SondearAsync(puerto, baudios, anotador, lineasEnAlto: false, ct).ConfigureAwait(false);
        return sonda.Identificador is null ? null : new EquipoEncontrado(puerto, baudios, sonda.Identificador);
    }

    /// <summary>El sondeador de verdad: abre el puerto, pregunta <c>ID;</c> y lo cierra.</summary>
    /// <param name="registro">Donde anotar.</param>
    /// <param name="lineasEnAlto">
    /// Abrir con <c>DTR</c>/<c>RTS</c> en cierto. Solo cuando el operador ha elegido un modelo
    /// que lo pide (ver <see cref="Yaesu.PerfilYaesu.LineasEnAltoAlAbrir"/>); buscando a ciegas
    /// o con el FT-710, nunca.
    /// </param>
    /// <returns>El sondeador.</returns>
    public static SondeadorDePuertos SondeadorReal(ILogger? registro = null, bool lineasEnAlto = false)
    {
        var anotador = registro ?? NullLogger.Instance;
        return (puerto, baudios, ct) => SondearAsync(puerto, baudios, anotador, lineasEnAlto, ct);
    }

    /// <summary>El puerto es el <i>Standard</i> de un CP2105 de Yaesu: el del control de transmision.</summary>
    /// <param name="puerto">Puerto, con su descripcion del sistema.</param>
    /// <returns>Verdadero si lo es.</returns>
    public static bool EsElPuertoDeTransmision(PuertoSerieDeLaMaquina? puerto) =>
        puerto?.Descripcion?.Contains("Standard COM Port", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>El puerto es el <i>Enhanced</i> de un CP2105 de Yaesu: el del CAT.</summary>
    /// <param name="puerto">Puerto, con su descripcion del sistema.</param>
    /// <returns>Verdadero si lo es.</returns>
    public static bool EsElPuertoDelCat(PuertoSerieDeLaMaquina? puerto) =>
        puerto?.Descripcion?.Contains("Enhanced COM Port", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Los puertos que se abren al buscar, en orden.
    /// </summary>
    /// <remarks>
    /// Si la maquina tiene algun <i>Enhanced COM Port</i>, solo esos: es donde esta el CAT, y no
    /// hay por que abrir puertos de otros aparatos. Si no hay ninguno (otro cable, o Windows no
    /// deja leer las descripciones) se miran todos, como antes, <b>menos los <i>Standard</i></b>.
    /// </remarks>
    /// <param name="puertos">Los puertos de la maquina.</param>
    /// <returns>Los que se van a probar.</returns>
    public static IReadOnlyList<PuertoSerieDeLaMaquina> PuertosCandidatos(IReadOnlyList<PuertoSerieDeLaMaquina> puertos)
    {
        ArgumentNullException.ThrowIfNull(puertos);
        var delCat = puertos.Where(EsElPuertoDelCat).ToList();
        return delCat.Count > 0 ? delCat : puertos.Where(puerto => !EsElPuertoDeTransmision(puerto)).ToList();
    }

    /// <summary>
    /// Recorre los puertos serie buscando equipos Yaesu.
    /// </summary>
    /// <param name="registro">Donde anotar la busqueda.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Los equipos encontrados, los FT-710 primero.</returns>
    public static Task<IReadOnlyList<EquipoEncontrado>> BuscarAsync(
        ILogger? registro = null,
        CancellationToken ct = default) =>
        BuscarEnAsync(PuertosSerieDelSistema.Listar(), SondeadorReal(registro), registro, ct);

    /// <summary>
    /// Recorre unos puertos dados con un sondeador dado. Es <see cref="BuscarAsync"/> sin tocar
    /// la maquina, para poder probarlo.
    /// </summary>
    /// <param name="puertos">Los puertos de la maquina, con su descripcion.</param>
    /// <param name="sondear">Como se prueba cada puerto.</param>
    /// <param name="registro">Donde anotar la busqueda.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Los equipos encontrados, los FT-710 primero.</returns>
    public static async Task<IReadOnlyList<EquipoEncontrado>> BuscarEnAsync(
        IReadOnlyList<PuertoSerieDeLaMaquina> puertos,
        SondeadorDePuertos sondear,
        ILogger? registro = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(puertos);
        ArgumentNullException.ThrowIfNull(sondear);
        var anotador = registro ?? NullLogger.Instance;
        var encontrados = new List<EquipoEncontrado>();
        var candidatos = PuertosCandidatos(puertos);

        foreach (var descartado in puertos.Except(candidatos))
        {
            anotador.LogInformation(
                "{Puerto} ({Descripcion}) no se abre al buscar el equipo: no es el puerto del CAT.",
                descartado.Nombre,
                descartado.Descripcion ?? "sin descripción");
        }

        foreach (var puerto in candidatos)
        {
            foreach (var baudios in Velocidades)
            {
                ct.ThrowIfCancellationRequested();

                var sonda = await sondear(puerto.Nombre, baudios, ct).ConfigureAwait(false);
                if (!sonda.SeAbrio)
                {
                    // Cogido por otro programa (o por la conexion del propio Cuaderno) o ya no
                    // existe. No se insiste a las demas velocidades y, sobre todo, no se toma
                    // por «aqui no esta la radio».
                    anotador.LogWarning(
                        "{Puerto} no se puede abrir (lo tiene otro programa o ya no existe): no se sabe si el equipo está ahí.",
                        puerto.Nombre);
                    break;
                }

                if (sonda.Identificador is null)
                {
                    continue;
                }

                var equipo = new EquipoEncontrado(puerto.Nombre, baudios, sonda.Identificador);
                anotador.LogInformation("Encontrado: {Equipo}.", equipo.Descripcion);
                encontrados.Add(equipo);
                break;
            }
        }

        return encontrados.OrderByDescending(equipo => equipo.EsFt710).ToList();
    }

    /// <summary>
    /// Busca el primer FT-710 y devuelve un control ya montado sobre el.
    /// </summary>
    /// <param name="opciones">Ajustes del control.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El control conectado, o nulo si no hay ningun FT-710 a la vista.</returns>
    public static async Task<ControlFt710?> BuscarYConectarAsync(
        OpcionesFt710? opciones = null,
        ILogger? registro = null,
        CancellationToken ct = default)
    {
        var equipos = await BuscarAsync(registro, ct).ConfigureAwait(false);
        var ft710 = equipos.FirstOrDefault(equipo => equipo.EsFt710);
        if (ft710 is null)
        {
            return null;
        }

        var ajustes = opciones ?? new OpcionesFt710();
        var canal = new CanalSerieCat(ft710.Puerto, ft710.Baudios, ajustes.EsperaDeOrden, registro);
        var control = new ControlFt710(canal, ajustes, registro);
        await control.ConectarAsync(ct).ConfigureAwait(false);
        return control;
    }

    private static async Task<ResultadoDeSonda> SondearAsync(
        string puerto,
        int baudios,
        ILogger registro,
        bool lineasEnAlto,
        CancellationToken ct)
    {
        await using var canal = new CanalSerieCat(
            puerto,
            baudios,
            TimeSpan.FromMilliseconds(400),
            registro,
            lineasEnAlto: lineasEnAlto);
        try
        {
            await canal.AbrirAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            registro.LogDebug(ex, "No se pudo abrir {Puerto} a {Baudios}.", puerto, baudios);
            return ResultadoDeSonda.NoSeAbre;
        }

        // Dos intentos: el primero puede llegar mezclado con lo que hubiera en el puerto.
        for (var intento = 0; intento < 2; intento++)
        {
            string? respuesta;
            try
            {
                respuesta = await canal.PreguntarAsync("ID;", ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registro.LogDebug(ex, "{Puerto} a {Baudios} no contesta a ID;.", puerto, baudios);
                return ResultadoDeSonda.NoContesta;
            }

            if (respuesta is null)
            {
                continue;
            }

            var encaje = PatronDeIdentificador().Match(respuesta.Trim());
            if (encaje.Success)
            {
                return new ResultadoDeSonda(true, encaje.Groups[1].Value);
            }

            registro.LogDebug(
                "{Puerto} a {Baudios} contestó algo que no es un identificador Yaesu; no es la radio.",
                puerto,
                baudios);
        }

        return ResultadoDeSonda.NoContesta;
    }

    [GeneratedRegex(@"^ID(\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronDeIdentificador();
}
