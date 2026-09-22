using System.Globalization;
using System.Xml.Linq;

namespace Nodisla.Cuaderno.Servicios.Xml;

/// <summary>
/// Lectura de XML sin atarse al espacio de nombres.
/// </summary>
/// <remarks>
/// QRZ.com y HamQTH declaran un espacio de nombres propio y lo han cambiado alguna vez (QRZ va
/// por la version 1.34 del suyo y lo versiona en la direccion). Buscar por nombre local evita
/// que una revision del servicio deje al programa sin leer nada sin dar ni un error.
/// </remarks>
public static class LecturaXml
{
    /// <summary>Primer elemento hijo con ese nombre local, sin mirar el espacio de nombres.</summary>
    /// <param name="padre">Elemento donde buscar.</param>
    /// <param name="nombre">Nombre local del hijo.</param>
    public static XElement? Hijo(XElement? padre, string nombre) =>
        padre?.Elements().FirstOrDefault(
            e => string.Equals(e.Name.LocalName, nombre, StringComparison.OrdinalIgnoreCase));

    /// <summary>Primer descendiente con ese nombre local, sin mirar el espacio de nombres.</summary>
    /// <param name="raiz">Elemento donde buscar.</param>
    /// <param name="nombre">Nombre local del descendiente.</param>
    public static XElement? Descendiente(XElement? raiz, string nombre) =>
        raiz?.Descendants().FirstOrDefault(
            e => string.Equals(e.Name.LocalName, nombre, StringComparison.OrdinalIgnoreCase));

    /// <summary>Texto de un hijo, o nulo si no esta o esta vacio.</summary>
    /// <param name="padre">Elemento donde buscar.</param>
    /// <param name="nombre">Nombre local del hijo.</param>
    public static string? Texto(XElement? padre, string nombre)
    {
        var valor = Hijo(padre, nombre)?.Value?.Trim();
        return string.IsNullOrEmpty(valor) ? null : valor;
    }

    /// <summary>Entero de un hijo, o nulo si no esta o no es un numero.</summary>
    /// <param name="padre">Elemento donde buscar.</param>
    /// <param name="nombre">Nombre local del hijo.</param>
    public static int? Entero(XElement? padre, string nombre) =>
        int.TryParse(Texto(padre, nombre), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    /// <summary>Numero con decimales de un hijo, en cultura invariante.</summary>
    /// <param name="padre">Elemento donde buscar.</param>
    /// <param name="nombre">Nombre local del hijo.</param>
    public static double? Decimal(XElement? padre, string nombre) =>
        double.TryParse(Texto(padre, nombre), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;

    /// <summary>Direccion de un hijo, o nulo si no es una direccion absoluta valida.</summary>
    /// <param name="padre">Elemento donde buscar.</param>
    /// <param name="nombre">Nombre local del hijo.</param>
    public static Uri? Direccion(XElement? padre, string nombre)
    {
        var texto = Texto(padre, nombre);
        return texto is not null && Uri.TryCreate(texto, UriKind.Absolute, out var uri) ? uri : null;
    }
}
