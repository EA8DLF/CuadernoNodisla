using System.Runtime.Versioning;
using NAudio.CoreAudioApi;

namespace Nodisla.Cuaderno.Audio.Fonia;

/// <summary>Los dispositivos que Windows tiene por omision para comunicaciones.</summary>
[SupportedOSPlatform("windows")]
public static class DispositivosPorOmision
{
    /// <summary>
    /// Identificador del dispositivo por omision de comunicaciones, o nulo si no hay.
    /// </summary>
    /// <param name="deEntrada">Verdadero para el microfono, falso para los altavoces.</param>
    /// <remarks>Solo se consulta el nombre del extremo: no se abre nada.</remarks>
    public static string? Id(bool deEntrada)
    {
        try
        {
            using var enumerador = new MMDeviceEnumerator();
            var sentido = deEntrada ? DataFlow.Capture : DataFlow.Render;
            if (!enumerador.HasDefaultAudioEndpoint(sentido, Role.Communications)) return null;
            using var dispositivo = enumerador.GetDefaultAudioEndpoint(sentido, Role.Communications);
            return dispositivo.ID;
        }
        catch
        {
            return null;
        }
    }
}
