using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.CoreAudioApi;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;

namespace Nodisla.Cuaderno.Audio.Dispositivos;

/// <summary>
/// Los dispositivos de sonido del sistema, diciendo cual es el del equipo.
/// </summary>
/// <remarks>
/// <para>
/// En esta maquina hay mas de veinte dispositivos de sonido entre placa base, tarjeta grafica,
/// televisores, cascos y cables virtuales. Pedirle al operador que adivine cual es la radio es
/// pedir un error, y el error se paga transmitiendo por donde no toca.
/// </para>
/// <para>
/// Por eso no se mira el nombre: se le pregunta a <see cref="AudioDelFt710"/>, que empareja el
/// cacharro USB con sus extremos de audio por el identificador de contenedor. Ese codigo vive
/// en el modulo de radio, que es quien sabe de equipos, y aqui se reutiliza tal cual.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class CatalogoDeDispositivos
{
    /// <summary>Lista los dispositivos de captura.</summary>
    /// <param name="registro">Donde se anota lo que pasa; si es nulo, no se anota nada.</param>
    /// <returns>Los dispositivos de entrada activos.</returns>
    public static IReadOnlyList<DispositivoDeAudio> Entradas(ILogger? registro = null) =>
        Listar(DataFlow.Capture, esDeEntrada: true, registro);

    /// <summary>Lista los dispositivos de reproduccion.</summary>
    /// <param name="registro">Donde se anota lo que pasa; si es nulo, no se anota nada.</param>
    /// <returns>Los dispositivos de salida activos.</returns>
    public static IReadOnlyList<DispositivoDeAudio> Salidas(ILogger? registro = null) =>
        Listar(DataFlow.Render, esDeEntrada: false, registro);

    /// <summary>
    /// Identificadores de los extremos de audio que son del equipo de radio.
    /// </summary>
    /// <param name="esDeEntrada">Verdadero para los de entrada, falso para los de salida.</param>
    /// <returns>Los identificadores; vacio si la radio esta apagada.</returns>
    public static IReadOnlySet<string> IdentificadoresDelEquipo(bool esDeEntrada)
    {
        var audio = AudioDelFt710.Buscar();
        var extremos = esDeEntrada ? audio?.Entradas : audio?.Salidas;

        return extremos is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(extremos.Select(extremo => extremo.Identificador), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Monta la lista de dispositivos marcando los que son del equipo.
    /// </summary>
    /// <param name="encontrados">Identificador y nombre de cada dispositivo del sistema.</param>
    /// <param name="idsDelEquipo">Identificadores que resultaron ser del equipo.</param>
    /// <param name="esDeEntrada">Verdadero si son dispositivos de captura.</param>
    /// <returns>La lista lista para el operador, con el del equipo el primero.</returns>
    /// <remarks>
    /// Esta parte esta separada de Windows a proposito: se puede probar sin tarjeta de sonido.
    /// </remarks>
    public static IReadOnlyList<DispositivoDeAudio> Componer(
        IEnumerable<(string Id, string Nombre)> encontrados,
        IReadOnlySet<string> idsDelEquipo,
        bool esDeEntrada)
    {
        ArgumentNullException.ThrowIfNull(encontrados);
        ArgumentNullException.ThrowIfNull(idsDelEquipo);

        return encontrados
            .Select(dispositivo => new DispositivoDeAudio(
                dispositivo.Id,
                dispositivo.Nombre,
                esDeEntrada,
                idsDelEquipo.Contains(dispositivo.Id)))

            // El del equipo primero: es el que el operador quiere el noventa y nueve por ciento
            // de las veces, y asi no tiene que buscarlo entre veinte.
            .OrderByDescending(dispositivo => dispositivo.EsDelEquipo)
            .ThenBy(dispositivo => dispositivo.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<DispositivoDeAudio> Listar(DataFlow sentido, bool esDeEntrada, ILogger? registro)
    {
        var anotador = registro ?? NullLogger.Instance;

        try
        {
            using var enumerador = new MMDeviceEnumerator();
            var encontrados = new List<(string, string)>();

            foreach (var dispositivo in enumerador.EnumerateAudioEndPoints(sentido, DeviceState.Active))
            {
                using (dispositivo)
                {
                    encontrados.Add((dispositivo.ID, dispositivo.FriendlyName));
                }
            }

            return Componer(encontrados, IdentificadoresDelEquipo(esDeEntrada), esDeEntrada);
        }
        catch (Exception fallo)
        {
            anotador.LogError(fallo, "No se han podido listar los dispositivos de sonido.");
            return Array.Empty<DispositivoDeAudio>();
        }
    }
}
