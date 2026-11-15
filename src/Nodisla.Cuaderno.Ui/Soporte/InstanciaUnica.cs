using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>
/// Una sola copia del programa por carpeta de datos.
/// </summary>
/// <remarks>
/// <para>
/// Dos copias abiertas a la vez se pelean por todo lo que es unico: el puerto COM de la radio,
/// la tarjeta de sonido, <c>ajustes.json</c>, la cola de subidas y el fichero de registro. En
/// los registros hay arranques dobles con ocho segundos de diferencia (un doble clic de mas) y
/// detras «ajustes.json.nuevo … being used by another process» y «No se ha podido guardar la
/// cola de subidas»; de ahi salian tambien los ficheros <c>cuaderno-AAAAMMDD_001.log</c>.
/// </para>
/// <para>
/// La primera copia crea un mutex con nombre y lo tiene hasta que se cierra. La segunda lo
/// encuentra creado, le pide a la primera que se ponga delante y sale <b>sin tocar nada</b>:
/// ni registro, ni ajustes, ni base de datos, ni puertos.
/// </para>
/// <para>
/// <b>Quien no entra en el reparto.</b> El modo de demostracion (<c>CUADERNO_SIMULADO</c>) no
/// abre puertos ni el cuaderno, y es con el que se hacen las capturas de la ayuda y las
/// verificaciones mientras el operador tiene abierto el programa de verdad: no coge cerrojo.
/// Y con <c>CUADERNO_CARPETA</c> el cerrojo es el de esa carpeta, no el del operador.
/// </para>
/// <para>
/// El mutex no se «adquiere»: basta con que exista. Asi no depende del hilo que lo creo, y es
/// lo unico que mira el instalador (<c>AppMutex</c> en <c>instalador/CuadernoNodisla.iss</c>,
/// que tiene que llevar <see cref="NombreDelPrograma"/> tal cual).
/// </para>
/// </remarks>
public sealed class InstanciaUnica : IDisposable
{
    /// <summary>
    /// Nombre del mutex del programa instalado. <b>Es el mismo que <c>AppMutex</c> en el
    /// instalador</b>: si se cambia aqui, se cambia alli.
    /// </summary>
    public const string NombreDelPrograma = "NODISLA.CuadernoNodisla.InstanciaUnica";

    private readonly Mutex _cerrojo;
    private readonly EventWaitHandle _llamada;
    private readonly RegisteredWaitHandle? _escucha;
    private Action? _alLlamar;
    private bool _liberada;

    private InstanciaUnica(Mutex cerrojo, EventWaitHandle llamada)
    {
        _cerrojo = cerrojo;
        _llamada = llamada;
        _escucha = ThreadPool.RegisterWaitForSingleObject(
            _llamada,
            (_, _) => Volatile.Read(ref _alLlamar)?.Invoke(),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// El nombre del cerrojo que le toca a este arranque, o nulo si no lleva ninguno.
    /// </summary>
    /// <param name="carpetaForzada">Valor de <c>CUADERNO_CARPETA</c>, si esta puesta.</param>
    /// <param name="simulado">Si esta puesta <c>CUADERNO_SIMULADO</c>.</param>
    public static string? NombreDelCerrojo(string? carpetaForzada, bool simulado)
    {
        if (simulado) return null;
        if (string.IsNullOrEmpty(carpetaForzada)) return NombreDelPrograma;

        // Una carpeta de verificacion tiene su propio cerrojo: no estorba al programa del
        // operador, pero dos copias sobre la misma carpeta siguen sin poder abrirse.
        var normalizada = Path.GetFullPath(carpetaForzada)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var huella = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizada)))[..16];
        return $"{NombreDelPrograma}.{huella}";
    }

    /// <summary>
    /// Intenta ser la primera copia. Devuelve nulo si ya hay otra con ese nombre.
    /// </summary>
    /// <param name="nombre">Nombre del cerrojo, de <see cref="NombreDelCerrojo"/>.</param>
    public static InstanciaUnica? Tomar(string nombre)
    {
        ArgumentException.ThrowIfNullOrEmpty(nombre);

        Mutex cerrojo;
        bool esLaPrimera;
        try
        {
            cerrojo = new Mutex(initiallyOwned: false, nombre, out esLaPrimera);
        }
        catch (UnauthorizedAccessException)
        {
            // Existe y no se deja abrir: lo creo una copia lanzada como administrador.
            return null;
        }

        if (!esLaPrimera)
        {
            cerrojo.Dispose();
            return null;
        }

        var llamada = new EventWaitHandle(false, EventResetMode.AutoReset, NombreDeLaLlamada(nombre));
        return new InstanciaUnica(cerrojo, llamada);
    }

    /// <summary>
    /// Desde la segunda copia: le pide a la primera que se ponga delante.
    /// </summary>
    /// <param name="nombre">El mismo nombre de cerrojo que no se pudo tomar.</param>
    /// <returns>Verdadero si habia a quien llamar.</returns>
    public static bool LlamarALaPrimera(string nombre)
    {
        ArgumentException.ThrowIfNullOrEmpty(nombre);

        if (!EventWaitHandle.TryOpenExisting(NombreDeLaLlamada(nombre), out var llamada)) return false;
        using (llamada)
        {
            // Windows solo deja ponerse delante a quien tiene el permiso, y lo tiene esta copia
            // (la acaba de lanzar el operador), no la primera. Se le cede.
            AllowSetForegroundWindow(CualquierProceso);
            return llamada.Set();
        }
    }

    /// <summary>
    /// Que hacer cuando otra copia llama. Se ejecuta en un hilo del grupo, no en el de interfaz.
    /// </summary>
    /// <param name="alLlamar">Lo que se hace al recibir la llamada.</param>
    public void AlLlamarOtraCopia(Action alLlamar) => Volatile.Write(ref _alLlamar, alLlamar);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_liberada) return;
        _liberada = true;
        _escucha?.Unregister(null);
        _llamada.Dispose();
        _cerrojo.Dispose();
    }

    private static string NombreDeLaLlamada(string nombre) => nombre + ".AlFrente";

    private const int CualquierProceso = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int idDelProceso);
}
