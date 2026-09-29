using System.IO.Ports;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Nodisla.Cuaderno.Radio;

/// <summary>Un puerto serie de la maquina, con el nombre del cacharro que hay detras.</summary>
/// <param name="Nombre">Nombre del puerto, por ejemplo <c>COM3</c>.</param>
/// <param name="Descripcion">
/// Como se llama el dispositivo en el administrador de dispositivos, o nulo si no se ha podido
/// averiguar.
/// </param>
public sealed record PuertoSerieDeLaMaquina(string Nombre, string? Descripcion)
{
    /// <summary>Como se escribe en un desplegable.</summary>
    public string ParaElDesplegable =>
        string.IsNullOrWhiteSpace(Descripcion) ? Nombre : $"{Nombre} · {Descripcion}";
}

/// <summary>
/// Los puertos serie de la maquina, con su descripcion.
/// </summary>
/// <remarks>
/// <para>
/// El nombre a secas no sirve para elegir: el FT-710 se presenta con un CP2105 <b>doble</b> y
/// aparecen dos puertos seguidos, el <i>Enhanced</i> —que es el bueno, a 115200— y el
/// <i>Standard</i>. Entre «COM3» y «COM4» no hay forma de saber cual es cual; entre
/// «CP2105 Dual USB to UART: Enhanced COM Port» y «... Standard COM Port», si.
/// </para>
/// <para>
/// La descripcion es la misma que ensena <c>Win32_PnPEntity</c> de WMI, pero se lee del
/// registro —de donde la saca el propio WMI— para no arrastrar el paquete
/// <c>System.Management</c> solo por esto. Si el registro no deja leer, se devuelven los
/// nombres a pelo: quedarse sin lista por no poder poner la descripcion seria peor.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class PuertosSerieDelSistema
{
    /// <summary>Donde vive el arbol de dispositivos de Windows.</summary>
    private const string RamaDeDispositivos = @"SYSTEM\CurrentControlSet\Enum";

    /// <summary>Lista los puertos serie de la maquina, ordenados por numero.</summary>
    /// <returns>Los puertos, con su descripcion cuando se ha podido averiguar.</returns>
    public static IReadOnlyList<PuertoSerieDeLaMaquina> Listar()
    {
        var descripciones = LeerDescripciones();

        return SerialPort.GetPortNames()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(nombre => new PuertoSerieDeLaMaquina(
                nombre,
                descripciones.TryGetValue(nombre, out var descripcion) ? descripcion : null))
            .OrderBy(puerto => NumeroDe(puerto.Nombre))
            .ThenBy(puerto => puerto.Nombre, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Ordena <c>COM10</c> despues de <c>COM9</c> y no antes.
    /// </summary>
    /// <param name="nombre">Nombre del puerto.</param>
    /// <returns>El numero del puerto, o un tope si no se puede leer.</returns>
    private static int NumeroDe(string nombre) =>
        int.TryParse(nombre.AsSpan(nombre.TakeWhile(char.IsLetter).Count()), out var numero)
            ? numero
            : int.MaxValue;

    /// <summary>
    /// Recorre el arbol de dispositivos buscando cuales tienen un puerto COM asignado.
    /// </summary>
    /// <remarks>
    /// El arbol es <c>Enum\&lt;enumerador&gt;\&lt;dispositivo&gt;\&lt;instancia&gt;</c>, y la
    /// instancia que tiene puerto serie lleva un <c>Device Parameters\PortName</c>. Cualquier
    /// rama que no deje leerse se salta en silencio: en una maquina con varios usuarios hay
    /// ramas sin permiso, y eso no puede tumbar la lista.
    /// </remarks>
    private static Dictionary<string, string> LeerDescripciones()
    {
        var descripciones = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var enumeradores = Registry.LocalMachine.OpenSubKey(RamaDeDispositivos);
            if (enumeradores is null) return descripciones;

            foreach (var nombreEnumerador in enumeradores.GetSubKeyNames())
            {
                using var enumerador = Abrir(enumeradores, nombreEnumerador);
                if (enumerador is null) continue;

                foreach (var nombreDispositivo in enumerador.GetSubKeyNames())
                {
                    using var dispositivo = Abrir(enumerador, nombreDispositivo);
                    if (dispositivo is null) continue;

                    foreach (var nombreInstancia in dispositivo.GetSubKeyNames())
                    {
                        using var instancia = Abrir(dispositivo, nombreInstancia);
                        if (instancia is null) continue;

                        AnotarSiTienePuerto(instancia, descripciones);
                    }
                }
            }
        }
        catch (Exception)
        {
            // Sin descripciones, pero con lista. Ver el comentario de la clase.
        }

        return descripciones;
    }

    private static void AnotarSiTienePuerto(RegistryKey instancia, Dictionary<string, string> descripciones)
    {
        using var parametros = Abrir(instancia, "Device Parameters");
        if (parametros?.GetValue("PortName") is not string puerto || puerto.Length == 0) return;

        var descripcion = Nombrar(instancia);
        if (descripcion is { Length: > 0 }) descripciones[puerto] = descripcion;
    }

    /// <summary>
    /// Como se llama el dispositivo para el operador.
    /// </summary>
    /// <remarks>
    /// Se prefiere <c>FriendlyName</c>, que es lo que se lee en el administrador de
    /// dispositivos. Si no esta, se usa <c>DeviceDesc</c>, que viene con la forma
    /// <c>@fichero.inf,%clave%;Texto de verdad</c>: lo util es lo de detras del punto y coma.
    /// </remarks>
    private static string? Nombrar(RegistryKey instancia)
    {
        if (instancia.GetValue("FriendlyName") is string amable && amable.Length > 0)
        {
            return QuitarElPuertoDelFinal(amable);
        }

        if (instancia.GetValue("DeviceDesc") is not string descripcion || descripcion.Length == 0) return null;

        var puntoYComa = descripcion.LastIndexOf(';');
        var limpia = puntoYComa >= 0 ? descripcion[(puntoYComa + 1)..] : descripcion;
        return QuitarElPuertoDelFinal(limpia);
    }

    /// <summary>
    /// Quita el «(COM3)» del final del nombre.
    /// </summary>
    /// <remarks>
    /// El puerto ya va delante en la lista; repetirlo detras solo gasta sitio en un desplegable
    /// donde lo que hay que leer es «Enhanced» o «Standard».
    /// </remarks>
    private static string QuitarElPuertoDelFinal(string nombre)
    {
        var abre = nombre.LastIndexOf("(COM", StringComparison.OrdinalIgnoreCase);
        return abre > 0 ? nombre[..abre].TrimEnd() : nombre.Trim();
    }

    private static RegistryKey? Abrir(RegistryKey padre, string nombre)
    {
        try
        {
            return padre.OpenSubKey(nombre);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
