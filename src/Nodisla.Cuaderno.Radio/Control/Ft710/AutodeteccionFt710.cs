using System.IO.Ports;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        ? $"Yaesu FT-710 en {Puerto} a {Baudios} baudios"
        : $"Equipo Yaesu ID{Identificador} en {Puerto} a {Baudios} baudios";
}

/// <summary>
/// Busca el equipo por los puertos serie de la maquina.
/// </summary>
/// <remarks>
/// El equipo se identifica <b>por lo que contesta a <c>ID;</c></b>, jamas por el numero de
/// puerto. En la maquina de Jose hay un puerto que no es la radio y que a 115200 escupe su
/// propio registro de depuracion: cualquier busqueda que se fie del puerto, o que de por bueno
/// lo primero que llegue, acabaria hablandole a ese cacharro. Por eso la respuesta tiene que
/// encajar exactamente con <c>ID</c> y cuatro cifras.
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
        var identificador = await ProbarAsync(puerto, baudios, anotador, ct).ConfigureAwait(false);
        return identificador is null ? null : new EquipoEncontrado(puerto, baudios, identificador);
    }

    /// <summary>
    /// Recorre los puertos serie buscando equipos Yaesu.
    /// </summary>
    /// <param name="registro">Donde anotar la busqueda.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Los equipos encontrados, los FT-710 primero.</returns>
    public static async Task<IReadOnlyList<EquipoEncontrado>> BuscarAsync(
        ILogger? registro = null,
        CancellationToken ct = default)
    {
        var anotador = registro ?? NullLogger.Instance;
        var encontrados = new List<EquipoEncontrado>();

        foreach (var puerto in SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).Order())
        {
            foreach (var baudios in Velocidades)
            {
                ct.ThrowIfCancellationRequested();

                var identificador = await ProbarAsync(puerto, baudios, anotador, ct).ConfigureAwait(false);
                if (identificador is null)
                {
                    continue;
                }

                var equipo = new EquipoEncontrado(puerto, baudios, identificador);
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

    private static async Task<string?> ProbarAsync(
        string puerto,
        int baudios,
        ILogger registro,
        CancellationToken ct)
    {
        await using var canal = new CanalSerieCat(puerto, baudios, TimeSpan.FromMilliseconds(400), registro);
        try
        {
            await canal.AbrirAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            registro.LogDebug(ex, "No se pudo abrir {Puerto} a {Baudios}.", puerto, baudios);
            return null;
        }

        // Dos intentos: el primero puede llegar mezclado con lo que hubiera en el puerto.
        for (var intento = 0; intento < 2; intento++)
        {
            string? respuesta;
            try
            {
                respuesta = await canal.PreguntarAsync("ID;", ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                registro.LogDebug(ex, "{Puerto} a {Baudios} no contesta a ID;.", puerto, baudios);
                return null;
            }

            if (respuesta is null)
            {
                continue;
            }

            var encaje = PatronDeIdentificador().Match(respuesta.Trim());
            if (encaje.Success)
            {
                return encaje.Groups[1].Value;
            }

            registro.LogDebug(
                "{Puerto} a {Baudios} contestó algo que no es un identificador Yaesu; no es la radio.",
                puerto,
                baudios);
        }

        return null;
    }

    [GeneratedRegex(@"^ID(\d{4})$", RegexOptions.CultureInvariant)]
    private static partial Regex PatronDeIdentificador();
}
