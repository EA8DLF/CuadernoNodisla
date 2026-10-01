using System.Text.RegularExpressions;

namespace Nodisla.Cuaderno.Servicios.Informes;

/// <summary>
/// Quita de un texto los datos personales antes de que salga del ordenador: contrasenas,
/// fichas de acceso, parametros de direcciones, correos, localizadores, coordenadas y el nombre
/// de usuario de Windows en las rutas.
/// </summary>
/// <remarks>
/// <para>
/// Se usa en el informe de fallos, que acaba en una incidencia <b>publica</b> de GitHub. Peca
/// de prudente a proposito: tachar de mas una frecuencia que parecia una coordenada cuesta poco;
/// dejar pasar la contrasena de QRZ.com (que su API lleva en la direccion, y que ya aparecio una
/// vez en el registro) no tiene arreglo.
/// </para>
/// <para>
/// Es idempotente: limpiar dos veces da lo mismo que una.
/// </para>
/// </remarks>
public static partial class LimpiadorDeDatosPersonales
{
    /// <summary>Lo que se pone en lugar de un secreto.</summary>
    public const string Tachado = "***";

    /// <summary>Limpia el texto. Nulo o vacio se devuelve vacio.</summary>
    /// <param name="texto">Texto a limpiar.</param>
    public static string Limpiar(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return string.Empty;

        var limpio = texto;

        // Primero lo que va dentro de direcciones, antes de que las demas reglas las partan.
        limpio = Direccion().Replace(limpio, TacharParametrosDeDireccion);
        limpio = CredencialEnDireccion().Replace(limpio, "${esquema}" + Tachado + "@");

        // Cabeceras y fichas de acceso.
        limpio = Portador().Replace(limpio, "${pre}" + Tachado);
        limpio = FichaDeGitHub().Replace(limpio, Tachado);

        // clave=valor, clave: valor, "clave": "valor".
        limpio = ClaveValorJson().Replace(limpio, "${pre}" + Tachado + "\"");
        limpio = ClaveValor().Replace(limpio, "${pre}" + Tachado);

        limpio = Correo().Replace(limpio, "[correo]");

        // Posicion de la estacion.
        limpio = LocalizadorConRotulo().Replace(limpio, "${pre}[locator]");
        limpio = LocalizadorLargo().Replace(limpio, "[locator]");
        limpio = CoordenadaConRotulo().Replace(limpio, "${pre}[coordenada]");
        limpio = CoordenadaSexagesimal().Replace(limpio, "[coordenada]");
        limpio = ParDeCoordenadas().Replace(limpio, "[coordenadas]");

        // C:\Users\<usuario>\AppData\... -> C:\Users\[usuario]\AppData\...
        limpio = CarpetaDeUsuario().Replace(limpio, "${pre}[usuario]");

        return limpio;
    }

    private static string TacharParametrosDeDireccion(Match direccion)
    {
        var texto = direccion.Value;
        var interrogacion = texto.IndexOf('?', StringComparison.Ordinal);
        if (interrogacion < 0) return texto;

        var consulta = texto[(interrogacion + 1)..];
        var fragmento = string.Empty;
        var almohadilla = consulta.IndexOf('#', StringComparison.Ordinal);
        if (almohadilla >= 0)
        {
            fragmento = consulta[almohadilla..];
            consulta = consulta[..almohadilla];
        }

        // Todos los valores fuera, no solo los que se llaman «password»: la API de QRZ.com usa
        // «s=» para la sesion, y cualquier servicio nuevo puede usar otro nombre.
        var tachada = ParametroDeConsulta().Replace(consulta, m =>
            m.Groups["valor"].Value is "" or Tachado ? m.Value : m.Groups["nombre"].Value + "=" + Tachado);

        return texto[..(interrogacion + 1)] + tachada + fragmento;
    }

    [GeneratedRegex(@"\b(?:https?|ftp|wss?)://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Direccion();

    [GeneratedRegex(@"(?<nombre>[^&;=?#]+)=(?<valor>[^&;#]*)")]
    private static partial Regex ParametroDeConsulta();

    [GeneratedRegex(@"(?<esquema>\b[a-z][a-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@", RegexOptions.IgnoreCase)]
    private static partial Regex CredencialEnDireccion();

    [GeneratedRegex(@"(?<pre>\b(?:Bearer|Basic|token)\s+)[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex Portador();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,})\b")]
    private static partial Regex FichaDeGitHub();

    private const string NombresDeSecreto =
        @"pass(?:word|wd)?|pwd|contrase(?:ñ|n)a|clave|secret[o]?|token|api[_-]?key|apikey|access[_-]?key|session[_-]?key|auth(?:orization)?|credencial(?:es)?|frase[_ -]?de[_ -]?paso|passphrase";

    [GeneratedRegex(@"(?<pre>""(?:" + NombresDeSecreto + @")""\s*:\s*"")[^""]*""", RegexOptions.IgnoreCase)]
    private static partial Regex ClaveValorJson();

    [GeneratedRegex(@"(?<pre>\b(?:" + NombresDeSecreto + @")\b\s*[:=]\s*)(?!\*\*\*|(?:Bearer|Basic|token)\s)[^\s,;&""']+", RegexOptions.IgnoreCase)]
    private static partial Regex ClaveValor();

    [GeneratedRegex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b")]
    private static partial Regex Correo();

    // Con rotulo delante vale tambien el de cuatro caracteres («locator IL18»).
    [GeneratedRegex(
        @"(?<pre>\b(?:locator|localizador|locutor|grid(?:square)?|cuadr[ií]cula|my_gridsquare|qth[_ ]?loc(?:ator)?)\b\s*[:=]?\s*[""']?)[A-R]{2}[0-9]{2}(?:[A-X]{2}(?:[0-9]{2})?)?\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LocalizadorConRotulo();

    // Sin rotulo solo el de seis u ocho: el de cuatro («AB12») se confunde con demasiadas cosas.
    [GeneratedRegex(@"\b[A-Ra-r]{2}[0-9]{2}[A-Xa-x]{2}(?:[0-9]{2})?\b")]
    private static partial Regex LocalizadorLargo();

    [GeneratedRegex(
        @"(?<pre>\b(?:lat(?:itud|itude)?|lon(?:gitud|gitude)?|lng)\b\s*[:=]?\s*)-?\d{1,3}(?:[.,]\d+)?°?",
        RegexOptions.IgnoreCase)]
    private static partial Regex CoordenadaConRotulo();

    [GeneratedRegex(@"-?\d{1,3}°\s*\d{1,2}\s*['′](?:\s*\d{1,2}(?:[.,]\d+)?\s*[""″])?\s*[NSEWO]?")]
    private static partial Regex CoordenadaSexagesimal();

    // Dos decimales con tres o mas cifras tras la coma, separados por coma o punto y coma:
    // «28.1234, -15.4321». Se lleva por delante alguna pareja de frecuencias; se acepta.
    [GeneratedRegex(@"(?<![\d.,])-?\d{1,3}\.\d{3,}\s*[,;]\s*-?\d{1,3}\.\d{3,}(?![\d.])")]
    private static partial Regex ParDeCoordenadas();

    [GeneratedRegex(@"(?<pre>\b[A-Za-z]:[\\/](?:Users|Usuarios|Documents and Settings)[\\/])(?!\[usuario\])[^\\/\r\n""']+", RegexOptions.IgnoreCase)]
    private static partial Regex CarpetaDeUsuario();
}
