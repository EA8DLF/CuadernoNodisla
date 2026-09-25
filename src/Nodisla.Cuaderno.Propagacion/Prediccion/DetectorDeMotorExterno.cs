namespace Nodisla.Cuaderno.Propagacion.Prediccion;

/// <summary>Motores de prediccion ajenos que se saben usar desde fuera.</summary>
public enum TipoDeMotorExterno
{
    /// <summary>VOACAP, del ITS del Departamento de Comercio de Estados Unidos.</summary>
    Voacap = 0,

    /// <summary>ITURHFProp, la implementacion de referencia de la Recomendacion UIT-R P.533.</summary>
    IturHfProp,
}

/// <summary>Un motor externo encontrado en el disco.</summary>
/// <param name="Tipo">Cual de los dos es.</param>
/// <param name="Ruta">Ejecutable encontrado.</param>
public sealed record MotorExternoEncontrado(TipoDeMotorExterno Tipo, string Ruta);

/// <summary>
/// Busca en el disco un motor de prediccion externo.
/// </summary>
/// <remarks>
/// <para>
/// Los dos que valdrian son de libre distribucion: VOACAP es de dominio publico y lo publica el
/// Institute for Telecommunication Sciences, y ITURHFProp es la implementacion de referencia de
/// la Recomendacion UIT-R P.533 y esta en el repositorio del Grupo de Estudio 3 de la UIT. Con
/// cualquiera de los dos instalado, lo correcto seria lanzarlo como proceso y leer su salida.
/// </para>
/// <para>
/// ITURHFProp ya no se busca por aqui: vive dentro del proyecto y lo encuentra
/// <see cref="MotorIturHfProp.Localizar"/>. Lo que queda de esta clase es la busqueda de un
/// <b>VOACAP</b> instalado en el sistema, que no se ejecuta: no hay ninguno en esta maquina
/// contra el que comprobar un analizador de su salida, y escribirlo a ciegas daria exactamente
/// lo que el puerto prohibe, numeros con pinta de calculados que nadie ha validado. Se detecta
/// y se avisa, por si algun dia interesa contrastar los dos modelos.
/// </para>
/// </remarks>
public static class DetectorDeMotorExterno
{
    /// <summary>Ejecutables de VOACAP, por orden de preferencia.</summary>
    private static readonly string[] EjecutablesVoacap = ["voacapw.exe", "voacapl.exe", "voacapl"];

    /// <summary>Ejecutables de ITURHFProp.</summary>
    private static readonly string[] EjecutablesIturHfProp = ["ITURHFProp.exe", "ITURHFProp", "iturhfprop"];

    /// <summary>Carpetas donde estos programas se instalan normalmente.</summary>
    public static IReadOnlyList<string> CarpetasHabituales { get; } =
    [
        Path.Combine("C:", "itshfbc", "bin_win"),
        Path.Combine("C:", "itshfbc"),
        Path.Combine("D:", "itshfbc", "bin_win"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "itshfbc", "bin_win"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "itshfbc", "bin_win"),
        "/usr/local/bin",
        "/usr/bin",
    ];

    /// <summary>Busca un motor externo.</summary>
    /// <param name="rutaIndicada">Ejecutable que el operador haya configurado a mano.</param>
    /// <param name="carpetas">Carpetas donde mirar. Por omision, las habituales.</param>
    /// <param name="existe">
    /// Como se comprueba si un fichero esta. Se puede cambiar en las pruebas para no tocar el disco.
    /// </param>
    /// <returns>Lo que se haya encontrado, o nulo si no hay ninguno.</returns>
    public static MotorExternoEncontrado? Buscar(
        string? rutaIndicada = null,
        IReadOnlyList<string>? carpetas = null,
        Func<string, bool>? existe = null)
    {
        var hay = existe ?? File.Exists;

        if (!string.IsNullOrWhiteSpace(rutaIndicada) && hay(rutaIndicada))
        {
            return new MotorExternoEncontrado(TipoDesdeNombre(Path.GetFileName(rutaIndicada)), rutaIndicada);
        }

        foreach (var carpeta in carpetas ?? CarpetasHabituales)
        {
            foreach (var nombre in EjecutablesVoacap)
            {
                var ruta = Path.Combine(carpeta, nombre);
                if (hay(ruta))
                {
                    return new MotorExternoEncontrado(TipoDeMotorExterno.Voacap, ruta);
                }
            }

            foreach (var nombre in EjecutablesIturHfProp)
            {
                var ruta = Path.Combine(carpeta, nombre);
                if (hay(ruta))
                {
                    return new MotorExternoEncontrado(TipoDeMotorExterno.IturHfProp, ruta);
                }
            }
        }

        return null;
    }

    private static TipoDeMotorExterno TipoDesdeNombre(string nombre) =>
        nombre.StartsWith("ITURHFProp", StringComparison.OrdinalIgnoreCase)
        || nombre.StartsWith("iturhfprop", StringComparison.OrdinalIgnoreCase)
            ? TipoDeMotorExterno.IturHfProp
            : TipoDeMotorExterno.Voacap;
}
