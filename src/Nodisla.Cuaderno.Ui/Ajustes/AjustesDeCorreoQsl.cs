using System.IO;
using System.Text.Json;
using Nodisla.Cuaderno.Servicios.Correo;
using Nodisla.Cuaderno.Ui.Qsl;

namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>
/// El correo saliente y el texto con el que se mandan las QSL. Sin contraseña: esa va cifrada al
/// almacen de credenciales (clave <c>smtp.contrasena</c>).
/// </summary>
/// <remarks>
/// Va en su propio fichero, <c>qsl\correo.json</c> en la carpeta de datos, y no en
/// <c>ajustes.json</c>: es una pieza aparte que se puede copiar o borrar sin tocar el resto.
/// </remarks>
public sealed class AjustesDeCorreoQsl
{
    /// <summary>Asunto de omision.</summary>
    public const string AsuntoPorOmision = "QSL {miindicativo} - {indicativo} {fecha} {banda} {modo}";

    /// <summary>Texto de omision.</summary>
    public const string TextoPorOmision =
        "Dear {nombre} ({indicativo}),\n\n" +
        "Thank you for our QSO on {fecha} at {hora} UTC, {banda} {modo}.\n" +
        "Please find attached my QSL card.\n\n" +
        "Gracias por el contacto. Adjunto mi tarjeta QSL.\n\n" +
        "73 de {miindicativo}";

    private static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    /// <summary>El servidor.</summary>
    public ConfiguracionSmtp Smtp { get; set; } = new();

    /// <summary>Asunto, con variables.</summary>
    public string Asunto { get; set; } = AsuntoPorOmision;

    /// <summary>Texto, con variables.</summary>
    public string Texto { get; set; } = TextoPorOmision;

    /// <summary>Formato de la tarjeta adjunta.</summary>
    public FormatoDeImagen Formato { get; set; } = FormatoDeImagen.Jpg;

    /// <summary>Ruta del fichero dentro de la carpeta de datos.</summary>
    /// <param name="carpetaDeDatos">Carpeta de datos.</param>
    /// <returns>La ruta.</returns>
    public static string Ruta(string carpetaDeDatos) => Path.Combine(carpetaDeDatos, "qsl", "correo.json");

    /// <summary>Lee los ajustes; si no hay o estan rotos, los de omision.</summary>
    /// <param name="carpetaDeDatos">Carpeta de datos.</param>
    /// <returns>Los ajustes.</returns>
    public static AjustesDeCorreoQsl Leer(string carpetaDeDatos)
    {
        var ruta = Ruta(carpetaDeDatos);
        try
        {
            if (File.Exists(ruta) && JsonSerializer.Deserialize<AjustesDeCorreoQsl>(File.ReadAllText(ruta), Opciones) is { } leidos)
            {
                leidos.Smtp ??= new ConfiguracionSmtp();
                leidos.Asunto ??= AsuntoPorOmision;
                leidos.Texto ??= TextoPorOmision;
                return leidos;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Serilog.Log.Warning(ex, "No se han podido leer los ajustes de correo de las QSL; se usan los de omisión.");
        }

        return new AjustesDeCorreoQsl();
    }

    /// <summary>Guarda los ajustes.</summary>
    /// <param name="carpetaDeDatos">Carpeta de datos.</param>
    public void Guardar(string carpetaDeDatos)
    {
        var ruta = Ruta(carpetaDeDatos);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(this, Opciones));
        File.Move(temporal, ruta, overwrite: true);
    }
}
