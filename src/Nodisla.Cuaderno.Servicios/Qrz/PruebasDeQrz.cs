using System.Text.RegularExpressions;

namespace Nodisla.Cuaderno.Servicios.Qrz;

/// <summary>Lo que ha salido de probar una credencial de QRZ.com.</summary>
public enum VeredictoDeQrz
{
    /// <summary>QRZ la acepta.</summary>
    Correcto,

    /// <summary>No hay nada guardado que probar.</summary>
    SinDato,

    /// <summary>QRZ no reconoce la clave, o el usuario y la contrasena.</summary>
    ClaveRechazada,

    /// <summary>La clave se reconoce pero no tiene permiso: falta la suscripcion.</summary>
    SinSuscripcion,

    /// <summary>QRZ tiene la cuenta bloqueada por intentos fallidos.</summary>
    Bloqueada,

    /// <summary>No se ha podido llegar a QRZ.</summary>
    SinRed,

    /// <summary>QRZ ha contestado algo que no se entiende.</summary>
    RespuestaNoEntendida,
}

/// <summary>Resultado de probar la clave de API del libro de QRZ.com.</summary>
/// <param name="Veredicto">Que ha pasado.</param>
/// <param name="Mensaje">Lo mismo, dicho para el operador en su idioma.</param>
public sealed record PruebaDelCuadernoQrz(VeredictoDeQrz Veredicto, string Mensaje)
{
    /// <summary>Indicativo del libro al que da acceso la clave, si QRZ lo dice.</summary>
    public string? Indicativo { get; init; }

    /// <summary>Nombre del libro, si QRZ lo dice.</summary>
    public string? Libro { get; init; }

    /// <summary>Contactos que tiene el libro, si QRZ lo dice.</summary>
    public int? Contactos { get; init; }

    /// <summary>Contactos confirmados, si QRZ lo dice.</summary>
    public int? Confirmados { get; init; }
}

/// <summary>Resultado de probar el usuario y la contrasena de QRZ.com (consulta de fichas).</summary>
/// <param name="Veredicto">Que ha pasado.</param>
/// <param name="Mensaje">Lo mismo, dicho para el operador en su idioma.</param>
public sealed record PruebaDeLaFichaQrz(VeredictoDeQrz Veredicto, string Mensaje)
{
    /// <summary>Aviso de QRZ al abrir la sesion (suscripcion parcial, por ejemplo).</summary>
    public string? Aviso { get; init; }

    /// <summary>Hasta cuando esta bloqueada la cuenta, si lo esta.</summary>
    public DateTimeOffset? BloqueadaHasta { get; init; }
}

/// <summary>
/// La forma de la clave de API del libro de QRZ.com.
/// </summary>
/// <remarks>
/// La guia oficial la llama «opaque API ACCESS KEY» y no fija su formato, pero el ejemplo que
/// da es <c>ABCD-0A0B-1C1D-2E2F</c> y la pagina del usuario anade que algunos programas no
/// dejan escribir los guiones y que entonces se quitan. Con eso basta para <b>avisar</b> de
/// que se ha pegado en la casilla que no era; nunca para impedir guardar.
/// </remarks>
public static partial class FormatoDeClaveQrz
{
    /// <summary>Indica si el texto tiene la forma de una clave de API del libro.</summary>
    /// <param name="texto">Lo tecleado o pegado.</param>
    public static bool PareceClaveDeApi(string? texto) =>
        !string.IsNullOrWhiteSpace(texto) && Forma().IsMatch(texto.Trim());

    [GeneratedRegex("^[0-9A-Fa-f]{4}(-[0-9A-Fa-f]{4}){3}$|^[0-9A-Fa-f]{16}$")]
    private static partial Regex Forma();
}
