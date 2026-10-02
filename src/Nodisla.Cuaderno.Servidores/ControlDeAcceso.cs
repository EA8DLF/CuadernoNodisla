using System.Net;
using System.Net.Sockets;

namespace Nodisla.Cuaderno.Servidores;

/// <summary>
/// Quien puede entrar a los servidores y en que direccion se escucha.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>De fabrica solo se escucha en 127.0.0.1: desde fuera del PC ni se ve el puerto.</item>
/// <item>Con «abrir a la red local» se escucha en todas las interfaces, pero solo entran el
/// propio PC y las IP o redes de la lista. Lista vacia: solo el propio PC.</item>
/// </list>
/// </remarks>
public sealed class ControlDeAcceso
{
    private readonly bool _abierto;
    private readonly List<(IPAddress Red, int Bits)> _permitidas = [];

    /// <summary>Crea el control a partir de los ajustes.</summary>
    /// <param name="opciones">Los ajustes.</param>
    public ControlDeAcceso(OpcionesDeServidores opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        _abierto = opciones.AbrirALaRedLocal;
        foreach (var entrada in opciones.IpsPermitidas)
        {
            if (Analizar(entrada) is { } red) _permitidas.Add(red);
        }
    }

    /// <summary>Direccion en la que se escucha: 127.0.0.1, o todas si se abre a la red local.</summary>
    public IPAddress DireccionDeEscucha => _abierto ? IPAddress.Any : IPAddress.Loopback;

    /// <summary>Si una IP puede entrar.</summary>
    /// <param name="ip">La IP del cliente.</param>
    /// <returns>Verdadero si entra.</returns>
    public bool Admite(IPAddress? ip)
    {
        if (ip is null) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (!_abierto) return false;
        return _permitidas.Exists(p => EnLaRed(ip, p.Red, p.Bits));
    }

    /// <summary>Si un texto es una IP o una red (CIDR) valida para la lista.</summary>
    /// <param name="entrada">El texto.</param>
    /// <returns>Verdadero si vale.</returns>
    public static bool EntradaValida(string? entrada) => Analizar(entrada) is not null;

    private static (IPAddress, int)? Analizar(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada)) return null;
        var partes = entrada.Trim().Split('/');
        if (partes.Length > 2 || !IPAddress.TryParse(partes[0], out var ip)) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var maximo = ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        var bits = maximo;
        if (partes.Length == 2 && (!int.TryParse(partes[1], out bits) || bits < 8 || bits > maximo)) return null;

        // Nada de «0.0.0.0/0» ni redes enormes: abrir el PTT a Internet no se hace ni por error.
        if (ip.AddressFamily == AddressFamily.InterNetwork && bits < 16) return null;
        return (ip, bits);
    }

    private static bool EnLaRed(IPAddress ip, IPAddress red, int bits)
    {
        if (ip.AddressFamily != red.AddressFamily) return false;
        var a = ip.GetAddressBytes();
        var b = red.GetAddressBytes();
        var enteros = bits / 8;
        for (var i = 0; i < enteros; i++)
        {
            if (a[i] != b[i]) return false;
        }

        var resto = bits % 8;
        if (resto == 0) return true;
        var mascara = (byte)(0xFF << (8 - resto));
        return (a[enteros] & mascara) == (b[enteros] & mascara);
    }
}
