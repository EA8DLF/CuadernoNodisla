using System.Globalization;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>Un extremo de audio del sistema: por donde entra o sale el sonido.</summary>
/// <param name="Identificador">
/// Identificador del extremo tal y como lo pide Windows para abrirlo, por ejemplo
/// <c>{0.0.1.00000000}.{guid}</c>.
/// </param>
/// <param name="Nombre">Nombre que ve el operador, por ejemplo <c>Micrófono</c>.</param>
/// <param name="Descripcion">De que dispositivo es, por ejemplo <c>USB Audio Device</c>.</param>
/// <param name="EsEntrada">Verdadero si es entrada (lo que sale del equipo hacia el ordenador).</param>
/// <param name="Activo">El dispositivo esta conectado y disponible.</param>
public sealed record ExtremoDeAudio(
    string Identificador,
    string Nombre,
    string? Descripcion,
    bool EsEntrada,
    bool Activo);

/// <summary>El audio que trae el equipo por USB.</summary>
/// <param name="ContenedorUsb">
/// Identificador del contenedor USB. Es lo que une el cacharro fisico con sus extremos de audio.
/// </param>
/// <param name="Entradas">Por donde llega al ordenador lo que recibe el equipo.</param>
/// <param name="Salidas">Por donde se le manda audio al equipo para transmitir.</param>
public sealed record AudioDelEquipo(
    Guid ContenedorUsb,
    IReadOnlyList<ExtremoDeAudio> Entradas,
    IReadOnlyList<ExtremoDeAudio> Salidas);

/// <summary>
/// El codec de audio USB que aparece cuando se enciende el FT-710.
/// </summary>
/// <remarks>
/// <para>
/// El equipo se presenta con dos cosas por USB: el CP2105 doble del CAT y un codec de audio
/// C-Media. El codec <b>solo esta cuando la radio esta encendida</b>, asi que sirve para saber
/// si vale la pena abrir el puerto serie o si la radio simplemente esta apagada.
/// </para>
/// <para>
/// Y sobre todo: es por donde el modem digital de la Fase 4 tomara el audio. En esta maquina
/// hay mas de veinte extremos de audio entre altavoces, televisores y microfonos; que el modem
/// tenga que adivinar cual es la radio seria pedir un error. Aqui se dice cual es, emparejando
/// el cacharro USB con sus extremos por el <c>ContainerID</c>, que es el mismo en los dos
/// sitios del registro. Comprobado en la maquina de Jose con el equipo encendido.
/// </para>
/// <para>
/// <b>No hay ningun dispositivo FTDI</b> en esta instalacion: el puente USB-SPI por el que
/// otros programas sacan el analizador de espectro del equipo no existe aqui. La cascada de la
/// Fase 4 habra que calcularla de este audio, no pedirsela al equipo.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class AudioDelFt710
{
    /// <summary>Identificador USB del codec de audio del equipo, un C-Media.</summary>
    public const string IdentificadorUsb = "VID_0D8C&PID_0013";

    private const string RutaDeDispositivosUsb = @"SYSTEM\CurrentControlSet\Enum\USB\";
    private const string RutaDeExtremosDeAudio =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\";

    // Claves de las propiedades de cada extremo, tal y como las guarda Windows.
    private const string PropiedadNombre = "{a45c254e-df1c-4efd-8020-67d146a850e0},2";
    private const string PropiedadDescripcion = "{b3f8fa53-0004-438e-9003-51a46e139bfc},6";
    private const string PropiedadContenedor = "{8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c},2";

    /// <summary>
    /// El codec del equipo esta presente, es decir, la radio esta encendida y enchufada.
    /// </summary>
    /// <returns>Verdadero si el sistema ve el codec de audio del equipo.</returns>
    public static bool EquipoEncendido() => BuscarContenedor() is not null;

    /// <summary>
    /// Busca el audio del equipo y sus extremos de entrada y salida.
    /// </summary>
    /// <returns>Lo encontrado, o nulo si la radio no esta encendida.</returns>
    public static AudioDelEquipo? Buscar()
    {
        var contenedor = BuscarContenedor();
        if (contenedor is null)
        {
            return null;
        }

        var entradas = ExtremosDe("Capture", contenedor.Value, esEntrada: true);
        var salidas = ExtremosDe("Render", contenedor.Value, esEntrada: false);
        return new AudioDelEquipo(contenedor.Value, entradas, salidas);
    }

    private static Guid? BuscarContenedor()
    {
        try
        {
            using var dispositivo = Registry.LocalMachine.OpenSubKey(RutaDeDispositivosUsb + IdentificadorUsb);
            if (dispositivo is null)
            {
                return null;
            }

            foreach (var instancia in dispositivo.GetSubKeyNames())
            {
                using var clave = dispositivo.OpenSubKey(instancia);
                if (clave?.GetValue("ContainerID") is string texto && Guid.TryParse(texto, out var contenedor))
                {
                    return contenedor;
                }
            }
        }
        catch (Exception)
        {
            // Si no se puede mirar el registro, es que no se puede saber: se dice que no hay audio.
        }

        return null;
    }

    private static List<ExtremoDeAudio> ExtremosDe(string tipo, Guid contenedor, bool esEntrada)
    {
        var encontrados = new List<ExtremoDeAudio>();

        try
        {
            using var raiz = Registry.LocalMachine.OpenSubKey(RutaDeExtremosDeAudio + tipo);
            if (raiz is null)
            {
                return encontrados;
            }

            // El prefijo del identificador de extremo: 0 para salida, 1 para entrada.
            var prefijo = esEntrada ? "{0.0.1.00000000}." : "{0.0.0.00000000}.";

            foreach (var nombreDeClave in raiz.GetSubKeyNames())
            {
                using var extremo = raiz.OpenSubKey(nombreDeClave);
                using var propiedades = extremo?.OpenSubKey("Properties");
                if (propiedades is null)
                {
                    continue;
                }

                if (LeerContenedor(propiedades) != contenedor)
                {
                    continue;
                }

                var activo = extremo!.GetValue("DeviceState") is int estado && estado == 1;
                encontrados.Add(new ExtremoDeAudio(
                    Identificador: prefijo + nombreDeClave,
                    Nombre: propiedades.GetValue(PropiedadNombre) as string ?? "sin nombre",
                    Descripcion: propiedades.GetValue(PropiedadDescripcion) as string,
                    EsEntrada: esEntrada,
                    Activo: activo));
            }
        }
        catch (Exception)
        {
            // Idem: sin registro no hay extremos que dar.
        }

        return encontrados;
    }

    /// <summary>
    /// Lee el contenedor de un extremo de audio.
    /// </summary>
    /// <remarks>
    /// Windows lo guarda como un valor binario con cabecera: los ultimos dieciseis bytes son el
    /// identificador, y van como los guarda COM —los tres primeros campos al reves—.
    /// </remarks>
    private static Guid? LeerContenedor(RegistryKey propiedades)
    {
        if (propiedades.GetValue(PropiedadContenedor) is not byte[] crudo || crudo.Length < 16)
        {
            return null;
        }

        var identificador = crudo.AsSpan(crudo.Length - 16);
        return new Guid(identificador);
    }

    /// <summary>Descripcion corta del audio del equipo, para el registro y la interfaz.</summary>
    /// <param name="audio">Lo que devolvio <see cref="Buscar"/>.</param>
    /// <returns>Un texto con lo encontrado.</returns>
    public static string Describir(AudioDelEquipo? audio)
    {
        if (audio is null)
        {
            return "No se ve el códec de audio del equipo: la radio está apagada o desenchufada.";
        }

        var entrada = audio.Entradas.FirstOrDefault();
        var salida = audio.Salidas.FirstOrDefault();
        return string.Create(
            CultureInfo.CurrentCulture,
            $"Audio del equipo: entrada «{entrada?.Nombre ?? "ninguna"}», salida «{salida?.Nombre ?? "ninguna"}».");
    }
}
