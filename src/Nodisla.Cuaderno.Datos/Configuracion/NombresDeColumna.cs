using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Nodisla.Cuaderno.Datos.Configuracion;

/// <summary>
/// Pone nombres de tabla y de columna en minusculas con guion bajo, que es como los define el
/// modelo de datos y como los escribe Log4OM.
/// </summary>
/// <remarks>
/// Se hace en una pasada sobre el modelo en vez de con un <c>HasColumnName</c> por propiedad:
/// son mas de cien columnas y cualquier campo nuevo del dominio quedaria mal nombrado si
/// alguien olvidase la linea correspondiente. Las excepciones estan en <see cref="Excepciones"/>.
/// </remarks>
public static class NombresDeColumna
{
    /// <summary>Nombre de la propiedad sombra que lleva el submodo ADIF.</summary>
    public const string PropiedadSubmodo = "Submodo";

    /// <summary>Columnas cuyo nombre no sale de traducir el nombre de la propiedad.</summary>
    private static readonly Dictionary<string, string> Excepciones = new(StringComparer.Ordinal)
    {
        ["Qso.InicioUtc"] = "qso_inicio_utc",
        ["Qso.FinUtc"] = "qso_fin_utc",
        ["Qso.Comentario"] = "comment",
        ["Qso.Notas"] = "notes",
        ["Qso.Submodo"] = "submode",
        ["QsoConfirmacion.Medio"] = "servicio",
        ["QsoConfirmacion.Enviado"] = "enviada",
        ["QsoConfirmacion.Recibido"] = "recibida",
        ["QsoConfirmacion.EnviadoUtc"] = "fecha_envio_utc",
        ["QsoConfirmacion.RecibidoUtc"] = "fecha_recep_utc",
        ["QsoConfirmacion.Via"] = "via_envio",
        ["QsoReferencia.Tipo"] = "award_code",
        ["QsoReferencia.NombrePrograma"] = "programa",
        ["QsoReferencia.Codigo"] = "referencia",
        ["QsoReferencia.Lado"] = "propia",
        ["QsoCampoExtra.Nombre"] = "campo",
    };

    /// <summary>
    /// Columnas de texto que se comparan sin distinguir mayusculas. Es lo que permite buscar
    /// <c>ea8dlf</c> y encontrar <c>EA8DLF</c> usando el indice, sin funciones por medio.
    /// </summary>
    private static readonly HashSet<string> SinDistinguirMayusculas = new(StringComparer.Ordinal)
    {
        "call", "band", "mode", "submode", "band_rx", "gridsquare", "gridsquare_ext",
        "cont", "country", "pfx", "state", "cnty", "region", "darc_dok", "eq_call",
        "contacted_op", "qsl_via", "station_callsign", "operator", "owner_callsign",
        "my_gridsquare", "my_state", "my_cnty", "my_country", "my_iota", "contest_id",
        "sat_name", "sat_mode", "prop_mode", "origen", "nombre_perfil", "servicio",
        "award_code", "referencia", "programa", "campo",
    };

    /// <summary>Renombra tablas y columnas de todo el modelo.</summary>
    /// <param name="modelo">Constructor del modelo de EF Core.</param>
    public static void Aplicar(ModelBuilder modelo)
    {
        ArgumentNullException.ThrowIfNull(modelo);

        foreach (var entidad in modelo.Model.GetEntityTypes())
        {
            var nombreEntidad = entidad.ClrType.Name;
            entidad.SetTableName(ADenominacionDeTabla(nombreEntidad));

            foreach (var propiedad in entidad.GetProperties())
            {
                var clave = $"{nombreEntidad}.{propiedad.Name}";
                var columna = Excepciones.TryGetValue(clave, out var excepcion)
                    ? excepcion
                    : AGuionBajo(propiedad.Name);

                propiedad.SetColumnName(columna);

                if (SinDistinguirMayusculas.Contains(columna))
                {
                    propiedad.SetCollation("NOCASE");
                }
            }
        }
    }

    private static string ADenominacionDeTabla(string nombreEntidad) => AGuionBajo(nombreEntidad);

    /// <summary>Convierte <c>MyCqZone</c> en <c>my_cq_zone</c>.</summary>
    private static string AGuionBajo(string nombre)
    {
        var destino = new StringBuilder(nombre.Length + 8);
        for (var i = 0; i < nombre.Length; i++)
        {
            var c = nombre[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(nombre[i - 1]) || char.IsDigit(nombre[i - 1])))
            {
                destino.Append('_');
            }
            destino.Append(char.ToLowerInvariant(c));
        }
        return destino.ToString();
    }
}
