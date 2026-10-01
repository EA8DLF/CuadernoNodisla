using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Servicios.Informes;
using Nodisla.Cuaderno.Ui.Ajustes;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>Lo que se adjunta a un informe de fallo, ya limpio de datos personales.</summary>
public static class DatosDelEntorno
{
    /// <summary>Lineas del registro que se adjuntan como mucho.</summary>
    public const int LineasDeRegistro = 80;

    /// <summary>Bytes del final del fichero que se leen para sacar esas lineas.</summary>
    private const int BytesDelFinal = 64 * 1024;

    /// <summary>
    /// Version, Windows, modelo de radio y modo CAT, en unas lineas de texto.
    /// </summary>
    /// <param name="version">Version instalada.</param>
    /// <param name="ajustes">Ajustes del programa (equipo configurado).</param>
    public static string Describir(string version, AjustesDelPrograma? ajustes)
    {
        var equipo = ajustes?.Equipo;
        var texto = new StringBuilder()
            .Append("Versión: ").AppendLine(version)
            .Append("Windows: ").Append(RuntimeInformation.OSDescription.Trim())
                .Append(" (").Append(RuntimeInformation.OSArchitecture).AppendLine(")")
            .Append(".NET: ").AppendLine(RuntimeInformation.FrameworkDescription)
            .Append("Radio: ").AppendLine(Modelo(equipo?.Modelo))
            .Append("Modo CAT: ").Append(Via(equipo?.Via ?? ViaDeControl.Ninguna));
        return LimpiadorDeDatosPersonales.Limpiar(texto.ToString());
    }

    /// <summary>
    /// Las ultimas lineas del registro del dia (el fichero mas reciente de la carpeta), limpias.
    /// </summary>
    /// <param name="carpetaDeRegistros">Carpeta <c>registros</c> de los datos del programa.</param>
    /// <param name="lineas">Cuantas lineas.</param>
    /// <returns>Las lineas, o una explicacion si no hay registro.</returns>
    public static string UltimasLineasDelRegistro(string carpetaDeRegistros, int lineas = LineasDeRegistro)
    {
        try
        {
            var fichero = new DirectoryInfo(carpetaDeRegistros).Exists
                ? new DirectoryInfo(carpetaDeRegistros).GetFiles("cuaderno-*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
            if (fichero is null) return "(No hay fichero de registro.)";

            // Serilog lo tiene abierto escribiendo: hay que compartirlo para lectura y escritura.
            using var flujo = new FileStream(fichero.FullName, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var desde = Math.Max(0, flujo.Length - BytesDelFinal);
            flujo.Seek(desde, SeekOrigin.Begin);
            using var lector = new StreamReader(flujo, Encoding.UTF8);
            var todas = lector.ReadToEnd().Split('\n');

            // Si se empezo a mitad del fichero, la primera linea esta cortada: fuera.
            var utiles = (desde > 0 ? todas.Skip(1) : todas)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .TakeLast(lineas);
            return LimpiadorDeDatosPersonales.Limpiar(string.Join(Environment.NewLine, utiles));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"(No se ha podido leer el registro: {ex.Message})";
        }
    }

    private static string Modelo(string? modelo) =>
        string.IsNullOrWhiteSpace(modelo) || modelo.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "sin elegir (búsqueda automática)"
            : modelo;

    private static string Via(ViaDeControl via) => via switch
    {
        ViaDeControl.CatNativo => "CAT nativo por puerto serie",
        ViaDeControl.Rigctld => "Hamlib (rigctld)",
        ViaDeControl.Ninguna => "sin control del equipo",
        _ => via.ToString(),
    };
}
