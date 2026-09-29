using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Servicios.Credenciales;

/// <summary>
/// Almacen de secretos cifrado con la proteccion de datos del usuario de Windows (DPAPI).
/// </summary>
/// <remarks>
/// <para>
/// Cada secreto se cifra por separado con <see cref="DataProtectionScope.CurrentUser"/> y una
/// entropia adicional derivada de la clave, de modo que copiar el fichero a otra cuenta o a
/// otra maquina no sirve de nada: solo el usuario que lo escribio puede descifrarlo.
/// </para>
/// <para>
/// El fichero es un texto de lineas <c>clave=base64</c>. Se guarda cifrado entrada a entrada y
/// no en bloque a proposito: asi un secreto corrupto no se lleva por delante a los demas.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class AlmacenDeCredencialesDpapi : IAlmacenDeCredenciales
{
    private readonly string _ruta;
    private readonly ILogger _log;
    private readonly Dictionary<string, string> _entradas = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cerrojo = new();
    private bool _cargado;

    /// <summary>Carpeta por omision donde vive el fichero de credenciales.</summary>
    public static string RutaPorOmision => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Cuaderno NODISLA",
        "credenciales.dat");

    /// <summary>Crea el almacen.</summary>
    /// <param name="ruta">Fichero donde guardar los secretos cifrados; nulo para el de por omision.</param>
    /// <param name="log">Registro de trazas.</param>
    public AlmacenDeCredencialesDpapi(string? ruta = null, ILogger<AlmacenDeCredencialesDpapi>? log = null)
    {
        _ruta = ruta ?? RutaPorOmision;
        _log = log ?? NullLogger<AlmacenDeCredencialesDpapi>.Instance;
    }

    /// <inheritdoc />
    public string? Leer(string clave)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);
        lock (_cerrojo)
        {
            Cargar();
            if (!_entradas.TryGetValue(clave, out var cifrado)) return null;
            try
            {
                var claro = ProtectedData.Unprotect(
                    Convert.FromBase64String(cifrado), Entropia(clave), DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(claro);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                // Nunca se registra el valor, solo la clave.
                _log.LogWarning(ex, "No se pudo descifrar la credencial {Clave}; se ignora.", clave);
                return null;
            }
        }
    }

    /// <inheritdoc />
    public void Guardar(string clave, string secreto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);
        ArgumentNullException.ThrowIfNull(secreto);
        lock (_cerrojo)
        {
            Cargar();
            var cifrado = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secreto), Entropia(clave), DataProtectionScope.CurrentUser);
            _entradas[clave] = Convert.ToBase64String(cifrado);
            Volcar();
            _log.LogInformation("Credencial {Clave} guardada cifrada.", clave);
        }
    }

    /// <inheritdoc />
    public void Borrar(string clave)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);
        lock (_cerrojo)
        {
            Cargar();
            if (_entradas.Remove(clave)) Volcar();
        }
    }

    /// <inheritdoc />
    public bool Existe(string clave)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);
        lock (_cerrojo)
        {
            Cargar();
            return _entradas.ContainsKey(clave);
        }
    }

    /// <summary>
    /// Entropia adicional atada a la clave: dos secretos distintos no comparten cifrado, asi que
    /// intercambiar dos lineas del fichero no convierte una contrasena en otra.
    /// </summary>
    private static byte[] Entropia(string clave) =>
        SHA256.HashData(Encoding.UTF8.GetBytes("Cuaderno NODISLA/" + clave));

    private void Cargar()
    {
        if (_cargado) return;
        _cargado = true;
        if (!File.Exists(_ruta)) return;

        foreach (var linea in File.ReadAllLines(_ruta, Encoding.UTF8))
        {
            if (linea.Length == 0 || linea[0] == '#') continue;
            var corte = linea.IndexOf('=');
            if (corte <= 0) continue;
            _entradas[linea[..corte]] = linea[(corte + 1)..];
        }
    }

    private void Volcar()
    {
        var carpeta = Path.GetDirectoryName(_ruta);
        if (!string.IsNullOrEmpty(carpeta)) Directory.CreateDirectory(carpeta);

        var sb = new StringBuilder();
        sb.AppendLine("# Credenciales de Cuaderno NODISLA cifradas con DPAPI del usuario.");
        sb.AppendLine("# Solo las puede descifrar la cuenta de Windows que las escribio.");
        foreach (var (clave, valor) in _entradas) sb.Append(clave).Append('=').AppendLine(valor);

        // Escritura atomica: si el programa muere a mitad, el fichero bueno sigue entero.
        var temporal = _ruta + ".tmp";
        File.WriteAllText(temporal, sb.ToString(), Encoding.UTF8);
        File.Move(temporal, _ruta, overwrite: true);
    }
}

/// <summary>
/// Almacen de secretos en memoria. Existe para las pruebas y para arrancar el programa en una
/// maquina sin DPAPI; <b>no persiste nada</b> y no debe usarse en produccion.
/// </summary>
public sealed class AlmacenDeCredencialesEnMemoria : IAlmacenDeCredenciales
{
    private readonly Dictionary<string, string> _entradas = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string? Leer(string clave) => _entradas.GetValueOrDefault(clave);

    /// <inheritdoc />
    public void Guardar(string clave, string secreto) => _entradas[clave] = secreto;

    /// <inheritdoc />
    public void Borrar(string clave) => _entradas.Remove(clave);

    /// <inheritdoc />
    public bool Existe(string clave) => _entradas.ContainsKey(clave);
}
