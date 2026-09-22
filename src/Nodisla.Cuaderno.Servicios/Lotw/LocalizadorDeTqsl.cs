using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Nodisla.Cuaderno.Servicios.Lotw;

/// <summary>Encuentra el ejecutable de TQSL en la maquina.</summary>
public interface ILocalizadorDeTqsl
{
    /// <summary>Ruta de <c>tqsl.exe</c>, o nulo si no esta instalado.</summary>
    string? Localizar();
}

/// <summary>
/// Busca <c>tqsl.exe</c> primero donde lo apunta el instalador y despues en las rutas de
/// costumbre.
/// </summary>
/// <remarks>
/// <para>
/// El instalador de Trusted QSL deja la ruta real en
/// <c>HKLM\SOFTWARE\WOW6432Node\TrustedQSL\InstallPath</c>, y hace falta mirarla: hay
/// instalaciones que no estan en «Archivos de programa» sino en otra unidad, y buscar solo por
/// rutas conocidas no daria con ellas.
/// </para>
/// <para>
/// La ruta que salga de aqui es solo la propuesta por omision. Siempre manda
/// <see cref="OpcionesLotw.RutaDeTqsl"/> si el operador la ha puesto a mano, porque TQSL se
/// puede instalar en cualquier sitio y tambien se puede llevar en un disco externo.
/// </para>
/// </remarks>
public sealed class LocalizadorDeTqsl : ILocalizadorDeTqsl
{
    private readonly ILogger _log;
    private readonly Func<string, string, string?> _leerRegistro;
    private readonly Func<string, bool> _existe;

    /// <summary>Clave del registro donde el instalador de TQSL deja su ruta.</summary>
    public const string ClaveDelRegistro = @"SOFTWARE\TrustedQSL";

    /// <summary>
    /// Ramas y vistas en las que buscar esa clave. La vista de 32 bits va primero: TQSL es un
    /// programa de 32 bits y ahi es donde escribe en un Windows de 64.
    /// </summary>
    private static readonly (RegistryHive Raiz, RegistryView Vista)[] RamasDelRegistro =
    [
        (RegistryHive.LocalMachine, RegistryView.Registry32),
        (RegistryHive.LocalMachine, RegistryView.Registry64),
        (RegistryHive.CurrentUser, RegistryView.Registry32),
    ];

    /// <summary>Crea el localizador.</summary>
    /// <param name="log">Registro de trazas.</param>
    /// <param name="leerRegistro">Lector de valores del registro; se sustituye en las pruebas.</param>
    /// <param name="existe">Comprobacion de existencia de fichero; se sustituye en las pruebas.</param>
    public LocalizadorDeTqsl(
        ILogger<LocalizadorDeTqsl>? log = null,
        Func<string, string, string?>? leerRegistro = null,
        Func<string, bool>? existe = null)
    {
        _log = log ?? NullLogger<LocalizadorDeTqsl>.Instance;
        _leerRegistro = leerRegistro ?? LeerDelRegistro;
        _existe = existe ?? File.Exists;
    }

    /// <inheritdoc />
    public string? Localizar()
    {
        var carpeta = _leerRegistro(ClaveDelRegistro, "InstallPath");
        if (!string.IsNullOrWhiteSpace(carpeta))
        {
            var ruta = Path.Combine(carpeta.Trim(), "tqsl.exe");
            if (_existe(ruta))
            {
                _log.LogInformation("TQSL encontrado por el registro en {Ruta}.", ruta);
                return ruta;
            }
        }

        foreach (var ruta in RutasHabituales())
        {
            if (_existe(ruta))
            {
                _log.LogInformation("TQSL encontrado en {Ruta}.", ruta);
                return ruta;
            }
        }

        _log.LogWarning("No se encontró TQSL instalado en esta máquina.");
        return null;
    }

    private static IEnumerable<string> RutasHabituales()
    {
        foreach (var carpeta in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        })
        {
            if (!string.IsNullOrEmpty(carpeta))
            {
                yield return Path.Combine(carpeta, "TrustedQSL", "tqsl.exe");
            }
        }
    }

    /// <summary>
    /// Lee un valor del registro de Windows, probando las vistas de 32 y de 64 bits.
    /// </summary>
    /// <param name="clave">Clave sin la raiz, por ejemplo <c>SOFTWARE\TrustedQSL</c>.</param>
    /// <param name="valor">Nombre del valor.</param>
    [SupportedOSPlatform("windows")]
    public static string? LeerDelRegistro(string clave, string valor)
    {
        foreach (var (raiz, vista) in RamasDelRegistro)
        {
            try
            {
                using var baseDelRegistro = RegistryKey.OpenBaseKey(raiz, vista);
                using var abierta = baseDelRegistro.OpenSubKey(clave);
                if (abierta?.GetValue(valor) is string texto && !string.IsNullOrWhiteSpace(texto))
                {
                    return texto;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException
                                          or UnauthorizedAccessException
                                          or IOException)
            {
                // Sin permiso para leer esa rama; se prueba la siguiente.
            }
        }
        return null;
    }
}
