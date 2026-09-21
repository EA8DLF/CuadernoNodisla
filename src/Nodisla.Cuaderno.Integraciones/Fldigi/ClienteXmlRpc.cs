using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Nodisla.Cuaderno.Integraciones.Fldigi;

/// <summary>Fallo que devuelve el otro lado en una llamada XML-RPC.</summary>
public sealed class ErrorXmlRpc : Exception
{
    /// <summary>Crea el error con el codigo y el texto que dio el servidor.</summary>
    public ErrorXmlRpc(int codigo, string mensaje) : base(mensaje) => Codigo = codigo;

    /// <summary>Crea el error a partir de otro fallo.</summary>
    public ErrorXmlRpc(string mensaje, Exception interior) : base(mensaje, interior) => Codigo = 0;

    /// <summary>Codigo de fallo. Cero si el fallo no viene del servidor.</summary>
    public int Codigo { get; }
}

/// <summary>
/// Cliente de XML-RPC escrito a mano, lo justo para hablar con FLDigi.
/// </summary>
/// <remarks>
/// La libreria que usa Log4OM (<c>CookComputing.XmlRpcV2</c>) esta abandonada y es de .NET
/// Framework, asi que no sirve. XML-RPC es XML sobre HTTP y cabe en un fichero: una peticion
/// <c>methodCall</c>, una respuesta <c>methodResponse</c> y media docena de tipos. Solo se
/// implementa lo que FLDigi usa.
/// </remarks>
public sealed class ClienteXmlRpc : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _propio;

    /// <summary>Crea el cliente contra una direccion.</summary>
    /// <param name="direccion">Direccion del servidor, por ejemplo <c>http://127.0.0.1:7362/RPC2</c>.</param>
    /// <param name="espera">Tiempo maximo de cada llamada.</param>
    /// <param name="http">
    /// Cliente HTTP a usar. Si no se da, se crea uno propio y se cierra con este objeto.
    /// </param>
    public ClienteXmlRpc(Uri direccion, TimeSpan espera, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(direccion);
        Direccion = direccion;
        _propio = http is null;
        _http = http ?? new HttpClient { Timeout = espera };
    }

    /// <summary>Direccion del servidor.</summary>
    public Uri Direccion { get; }

    /// <summary>Llama a un metodo y devuelve el valor de la respuesta, si lo hay.</summary>
    /// <param name="metodo">Nombre del metodo, por ejemplo <c>main.get_frequency</c>.</param>
    /// <param name="parametros">
    /// Parametros. Se admiten <c>string</c>, <c>int</c>, <c>double</c>, <c>bool</c> y
    /// <c>byte[]</c>, que es lo que usa FLDigi.
    /// </param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task<object?> LlamarAsync(
        string metodo,
        IReadOnlyList<object?>? parametros = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(metodo);

        var peticion = ArmarPeticion(metodo, parametros);
        using var contenido = new StringContent(peticion, Encoding.UTF8, "text/xml");

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _http.PostAsync(Direccion, contenido, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ErrorXmlRpc($"No se pudo hablar con {Direccion}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ErrorXmlRpc($"{Direccion} no contestó a tiempo.", ex);
        }

        using (respuesta)
        {
            var texto = await respuesta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!respuesta.IsSuccessStatusCode)
            {
                throw new ErrorXmlRpc((int)respuesta.StatusCode, $"{Direccion} contestó {(int)respuesta.StatusCode}.");
            }
            return LeerRespuesta(texto);
        }
    }

    /// <summary>Arma el XML de una llamada.</summary>
    /// <remarks>
    /// Es publico para poder probarlo por separado; <b>no forma parte de la interfaz de
    /// uso</b> del cliente, que es <see cref="LlamarAsync"/>. No construyas encima.
    /// </remarks>
    public static string ArmarPeticion(string metodo, IReadOnlyList<object?>? parametros)
    {
        var lista = new XElement("params");
        if (parametros is not null)
        {
            foreach (var p in parametros)
            {
                lista.Add(new XElement("param", EscribirValor(p)));
            }
        }

        var llamada = new XElement("methodCall",
            new XElement("methodName", metodo),
            lista);

        // Sin declaracion de codificacion: FLDigi manda ASCII y acepta UTF-8 sin rechistar.
        return "<?xml version=\"1.0\"?>" + llamada.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement EscribirValor(object? valor) => valor switch
    {
        null => new XElement("value", new XElement("string", string.Empty)),
        string s => new XElement("value", new XElement("string", s)),
        bool b => new XElement("value", new XElement("boolean", b ? "1" : "0")),
        int i => new XElement("value", new XElement("i4", i.ToString(CultureInfo.InvariantCulture))),
        long l => new XElement("value", new XElement("i4", l.ToString(CultureInfo.InvariantCulture))),
        double d => new XElement("value", new XElement("double", d.ToString("0.############", CultureInfo.InvariantCulture))),
        decimal m => new XElement("value", new XElement("double", m.ToString("0.############", CultureInfo.InvariantCulture))),
        byte[] bytes => new XElement("value", new XElement("base64", Convert.ToBase64String(bytes))),
        _ => throw new ErrorXmlRpc(0, $"XML-RPC: no sé cómo escribir un {valor.GetType().Name}."),
    };

    /// <summary>Lee el XML de una respuesta y devuelve su valor, o lanza si trae un fallo.</summary>
    internal static object? LeerRespuesta(string xml)
    {
        XDocument documento;
        try
        {
            documento = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new ErrorXmlRpc("La respuesta XML-RPC no es XML válido.", ex);
        }

        var raiz = documento.Root;
        if (raiz is null || raiz.Name.LocalName != "methodResponse")
        {
            throw new ErrorXmlRpc(0, "La respuesta no es un methodResponse.");
        }

        var fallo = raiz.Element("fault");
        if (fallo is not null)
        {
            var datos = LeerValor(fallo.Element("value")) as IReadOnlyDictionary<string, object?>;
            var codigo = datos is not null && datos.TryGetValue("faultCode", out var c) && c is int n ? n : 0;
            var mensaje = datos is not null && datos.TryGetValue("faultString", out var s) ? s?.ToString() : null;
            throw new ErrorXmlRpc(codigo, mensaje ?? "El servidor XML-RPC devolvió un fallo sin texto.");
        }

        var valor = raiz.Element("params")?.Element("param")?.Element("value");
        return valor is null ? null : LeerValor(valor);
    }

    /// <summary>Convierte un elemento <c>value</c> en el objeto que representa.</summary>
    private static object? LeerValor(XElement? valor)
    {
        if (valor is null) return null;

        var tipo = valor.Elements().FirstOrDefault();
        if (tipo is null) return valor.Value;

        switch (tipo.Name.LocalName)
        {
            case "string":
                return tipo.Value;

            case "int":
            case "i4":
            case "i8":
                return int.TryParse(tipo.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                    ? i
                    : 0;

            case "double":
                return double.TryParse(tipo.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                    ? d
                    : 0d;

            case "boolean":
                return tipo.Value.Trim() is "1" or "true";

            case "base64":
                try
                {
                    return Convert.FromBase64String(tipo.Value.Trim());
                }
                catch (FormatException)
                {
                    return Array.Empty<byte>();
                }

            case "array":
                var elementos = tipo.Element("data")?.Elements("value") ?? [];
                return elementos.Select(LeerValor).ToArray();

            case "struct":
                var campos = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var miembro in tipo.Elements("member"))
                {
                    var nombre = miembro.Element("name")?.Value;
                    if (nombre is null) continue;
                    campos[nombre] = LeerValor(miembro.Element("value"));
                }
                return campos;

            case "nil":
                return null;

            default:
                return tipo.Value;
        }
    }

    /// <summary>Cierra el cliente HTTP si es propio.</summary>
    public void Dispose()
    {
        if (_propio) _http.Dispose();
    }
}
