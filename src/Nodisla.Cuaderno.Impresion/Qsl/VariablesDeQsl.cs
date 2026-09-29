using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Impresion.Qsl;

/// <summary>Lo que la tarjeta dice de mi estacion cuando el contacto no lo trae.</summary>
/// <param name="Indicativo">Mi indicativo.</param>
/// <param name="Localizador">Mi localizador.</param>
/// <param name="Nombre">Mi nombre.</param>
/// <param name="Qth">Poblacion, isla o lo que se quiera poner.</param>
/// <param name="Equipo">Equipo.</param>
/// <param name="Antena">Antena.</param>
/// <param name="Cq">Zona CQ.</param>
/// <param name="Itu">Zona ITU.</param>
public sealed record DatosDeMiEstacion(
    string Indicativo,
    string Localizador,
    string? Nombre,
    string? Qth,
    string? Equipo = null,
    string? Antena = null,
    int? Cq = null,
    int? Itu = null)
{
    /// <summary>Sin datos.</summary>
    public static DatosDeMiEstacion Vacios { get; } = new(string.Empty, string.Empty, null, null);

    /// <summary>Saca los datos de un perfil de estacion.</summary>
    /// <param name="estacion">El perfil; nulo da <see cref="Vacios"/>.</param>
    /// <returns>Los datos.</returns>
    public static DatosDeMiEstacion Desde(Estacion? estacion)
    {
        if (estacion is null) return Vacios;
        var qth = string.Join(", ", new[] { estacion.MyCity, estacion.MyState }
            .Where(t => !string.IsNullOrWhiteSpace(t)));
        return new DatosDeMiEstacion(
            estacion.StationCallsign.Valor ?? string.Empty,
            estacion.MyGridsquare.Valor ?? string.Empty,
            estacion.MyName,
            qth.Length == 0 ? null : qth,
            estacion.MyRig,
            estacion.MyAntenna,
            estacion.MyCqZone,
            estacion.MyItuZone);
    }
}

/// <summary>
/// Las variables que se pueden escribir en los campos de la tarjeta y en el correo.
/// </summary>
/// <remarks>
/// Se escriben entre llaves y sin distinguir mayusculas: <c>{indicativo}</c>, <c>{Fecha}</c>.
/// Una variable que no existe se deja tal cual, con sus llaves, para que el error se vea en la
/// vista previa en vez de desaparecer en silencio.
/// </remarks>
public static class VariablesDeQsl
{
    /// <summary>Las variables que hay, con lo que ponen, para la ayuda y el editor.</summary>
    public static IReadOnlyList<(string Nombre, string Descripcion)> Conocidas { get; } =
    [
        ("miindicativo", "Mi indicativo"),
        ("milocalizador", "Mi localizador"),
        ("minombre", "Mi nombre"),
        ("miqth", "Mi QTH (población, provincia)"),
        ("miequipo", "Mi equipo"),
        ("miantena", "Mi antena"),
        ("micq", "Mi zona CQ"),
        ("miitu", "Mi zona ITU"),
        ("indicativo", "Indicativo del corresponsal"),
        ("nombre", "Nombre del corresponsal"),
        ("fecha", "Fecha del contacto, AAAA-MM-DD (UTC)"),
        ("hora", "Hora del contacto, HH:MM (UTC)"),
        ("banda", "Banda (20m, 2m…)"),
        ("frecuencia", "Frecuencia en MHz"),
        ("modo", "Modo (SSB, FT8…)"),
        ("rst", "Informe enviado"),
        ("rstrecibido", "Informe recibido"),
        ("potencia", "Mi potencia en W"),
        ("satelite", "Satélite, si lo hubo"),
        ("pse", "«PSE QSL» o «TNX QSL» según si ya llegó la suya"),
        ("mensaje", "Mensaje QSL del contacto"),
        ("contactos", "Lista de todos los contactos del correo (solo en el correo)"),
    ];

    /// <summary>Los valores de las variables para un contacto.</summary>
    /// <param name="qso">El contacto; nulo deja en blanco las del contacto.</param>
    /// <param name="yo">Mi estacion, para lo que el contacto no traiga.</param>
    /// <returns>Nombre de variable en minusculas y su valor.</returns>
    /// <remarks>
    /// <b>Lo que dice el contacto manda sobre el perfil.</b> Si el QSO se hizo desde otro perfil
    /// (portable, otra isla), la tarjeta tiene que llevar el indicativo y el localizador de ESE
    /// contacto, no los del perfil activo hoy.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> Para(Qso? qso, DatosDeMiEstacion? yo)
    {
        yo ??= DatosDeMiEstacion.Vacios;
        var ci = CultureInfo.InvariantCulture;
        var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["miindicativo"] = Primero(qso?.StationCallsign.Valor, yo.Indicativo),
            ["milocalizador"] = Primero(qso?.MyGridsquare.Valor, yo.Localizador),
            ["minombre"] = Primero(qso?.MyName, yo.Nombre),
            ["miqth"] = Primero(qso?.MyCity, yo.Qth),
            ["miequipo"] = Primero(qso?.MyRig, yo.Equipo),
            ["miantena"] = Primero(qso?.MyAntenna, yo.Antena),
            ["micq"] = (qso?.MyCqZone ?? yo.Cq)?.ToString(ci) ?? string.Empty,
            ["miitu"] = (qso?.MyItuZone ?? yo.Itu)?.ToString(ci) ?? string.Empty,
        };

        if (qso is null)
        {
            foreach (var clave in new[] { "indicativo", "nombre", "fecha", "hora", "banda", "frecuencia", "modo", "rst", "rstrecibido", "potencia", "satelite", "pse", "mensaje" })
            {
                v[clave] = string.Empty;
            }

            return v;
        }

        var utc = qso.InicioUtc.ToUniversalTime();
        var banda = qso.Band.EsVacia
            ? (qso.Freq.EsCero ? string.Empty : Dominio.Valores.Banda.DesdeFrecuencia(qso.Freq).Nombre)
            : qso.Band.Nombre;
        var papel = qso.Confirmaciones.FirstOrDefault(c => c.Medio == MedioDeConfirmacion.Papel);

        v["indicativo"] = qso.Call.Valor ?? string.Empty;
        v["nombre"] = qso.Name ?? string.Empty;
        v["fecha"] = utc.ToString("yyyy-MM-dd", ci);
        v["hora"] = utc.ToString("HH:mm", ci);
        v["banda"] = banda ?? string.Empty;
        v["frecuencia"] = qso.Freq.EsCero ? string.Empty : qso.Freq.Megahercios.ToString("0.000", ci);
        v["modo"] = qso.Mode.EsVacio ? string.Empty : qso.Mode.NombreUsual;
        v["rst"] = qso.RstSent.EsVacio ? string.Empty : qso.RstSent.Texto;
        v["rstrecibido"] = qso.RstRcvd.EsVacio ? string.Empty : qso.RstRcvd.Texto;
        v["potencia"] = qso.TxPwr is { } w ? w.ToString("0.#", ci) : string.Empty;
        v["satelite"] = qso.SatName ?? string.Empty;
        v["pse"] = papel is { EstaConfirmada: true } ? "TNX QSL" : "PSE QSL";
        v["mensaje"] = qso.QslMsg ?? string.Empty;
        return v;
    }

    /// <summary>Cambia las variables de un texto por sus valores.</summary>
    /// <param name="plantilla">El texto con variables entre llaves.</param>
    /// <param name="valores">Los valores, de <see cref="Para"/>.</param>
    /// <returns>El texto ya relleno.</returns>
    public static string Sustituir(string? plantilla, IReadOnlyDictionary<string, string> valores)
    {
        ArgumentNullException.ThrowIfNull(valores);
        if (string.IsNullOrEmpty(plantilla)) return string.Empty;

        var salida = new StringBuilder(plantilla.Length + 16);
        var i = 0;
        while (i < plantilla.Length)
        {
            var abre = plantilla.IndexOf('{', i);
            if (abre < 0)
            {
                salida.Append(plantilla, i, plantilla.Length - i);
                break;
            }

            var cierra = plantilla.IndexOf('}', abre + 1);
            if (cierra < 0)
            {
                salida.Append(plantilla, i, plantilla.Length - i);
                break;
            }

            salida.Append(plantilla, i, abre - i);
            var nombre = plantilla.Substring(abre + 1, cierra - abre - 1).Trim();
            if (valores.TryGetValue(nombre, out var valor))
            {
                salida.Append(valor);
            }
            else
            {
                salida.Append(plantilla, abre, cierra - abre + 1);
            }

            i = cierra + 1;
        }

        return salida.ToString();
    }

    private static string Primero(string? delContacto, string? delPerfil) =>
        !string.IsNullOrWhiteSpace(delContacto) ? delContacto.Trim()
        : delPerfil?.Trim() ?? string.Empty;
}
