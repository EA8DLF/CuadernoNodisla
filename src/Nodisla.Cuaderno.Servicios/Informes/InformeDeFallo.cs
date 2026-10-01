using System.Text;
using Nodisla.Cuaderno.Servicios.Actualizaciones;

namespace Nodisla.Cuaderno.Servicios.Informes;

/// <summary>Lo que el operador cuenta de un fallo, y lo que se adjunta.</summary>
public sealed record DatosDeInforme
{
    /// <summary>Titulo de la incidencia.</summary>
    public string Titulo { get; init; } = string.Empty;

    /// <summary>Que paso.</summary>
    public string QuePaso { get; init; } = string.Empty;

    /// <summary>Que esperaba que pasara.</summary>
    public string QueEsperaba { get; init; } = string.Empty;

    /// <summary>Pasos para reproducirlo.</summary>
    public string Pasos { get; init; } = string.Empty;

    /// <summary>Entorno (version, Windows, radio, CAT), ya revisado por el operador.</summary>
    public string Entorno { get; init; } = string.Empty;

    /// <summary>Ultimas lineas del registro, ya revisadas. Vacio si no se adjuntan.</summary>
    public string Registro { get; init; } = string.Empty;
}

/// <summary>La direccion que se abre en el navegador.</summary>
/// <param name="Direccion">Direccion de «nueva incidencia» con titulo y cuerpo.</param>
/// <param name="CuerpoCompleto">El cuerpo entero, limpio.</param>
/// <param name="CuerpoEnElPortapapeles">
/// El cuerpo no cabia en la direccion: la direccion lleva solo un recordatorio y el cuerpo
/// entero hay que pegarlo desde el portapapeles.
/// </param>
public sealed record EnvioDeInforme(Uri Direccion, string CuerpoCompleto, bool CuerpoEnElPortapapeles);

/// <summary>
/// Monta el cuerpo de la incidencia y la direccion de GitHub que la abre ya rellena.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sin ficha de acceso.</b> No se publica nada desde el programa: se abre en el navegador
/// la pagina de «nueva incidencia» con el titulo y el cuerpo puestos, y el operador la envia
/// con su propia cuenta de GitHub despues de leerla.
/// </para>
/// <para>
/// GitHub corta las direcciones de mas de unos 8.000 caracteres. Si el cuerpo no cabe, la
/// direccion lleva solo el titulo y un recordatorio, y el cuerpo se copia al portapapeles.
/// </para>
/// </remarks>
public static class InformeDeFallo
{
    /// <summary>Largo maximo de la direccion completa.</summary>
    public const int LargoMaximoDeDireccion = 7500;

    /// <summary>Largo maximo del titulo.</summary>
    public const int LargoMaximoDeTitulo = 200;

    /// <summary>Lo que va en el cuerpo cuando el completo no cabe.</summary>
    public const string AvisoDePortapapeles =
        "El informe completo no cabía en el enlace y se ha copiado al portapapeles. " +
        "Bórrese este párrafo y péguese aquí (Ctrl+V).";

    /// <summary>Monta el cuerpo en Markdown, ya limpio de datos personales.</summary>
    /// <param name="datos">Lo que cuenta el operador y lo adjunto.</param>
    public static string Cuerpo(DatosDeInforme datos)
    {
        ArgumentNullException.ThrowIfNull(datos);

        var cuerpo = new StringBuilder();
        Seccion(cuerpo, "Qué pasó", datos.QuePaso);
        Seccion(cuerpo, "Qué esperaba", datos.QueEsperaba);
        Seccion(cuerpo, "Pasos para reproducirlo", datos.Pasos);
        Seccion(cuerpo, "Entorno", datos.Entorno);

        if (!string.IsNullOrWhiteSpace(datos.Registro))
        {
            cuerpo.AppendLine("### Últimas líneas del registro").AppendLine();
            cuerpo.AppendLine("<details><summary>Registro</summary>").AppendLine();
            // Cuatro acentos graves: el registro puede traer tres seguidos y cerrar el bloque.
            cuerpo.AppendLine("````text");
            cuerpo.AppendLine(datos.Registro.TrimEnd());
            cuerpo.AppendLine("````").AppendLine();
            cuerpo.AppendLine("</details>").AppendLine();
        }

        cuerpo.Append("_Enviado desde «Reportar un fallo» de Cuaderno NODISLA._");
        return LimpiadorDeDatosPersonales.Limpiar(cuerpo.ToString());
    }

    /// <summary>Titulo limpio y acotado.</summary>
    /// <param name="titulo">Titulo escrito por el operador.</param>
    public static string Titulo(string? titulo)
    {
        var limpio = LimpiadorDeDatosPersonales.Limpiar(titulo).ReplaceLineEndings(" ").Trim();
        if (limpio.Length == 0) limpio = "Fallo sin título";
        return limpio.Length > LargoMaximoDeTitulo ? limpio[..LargoMaximoDeTitulo] : limpio;
    }

    /// <summary>Monta la direccion de «nueva incidencia».</summary>
    /// <param name="datos">Lo que cuenta el operador y lo adjunto.</param>
    /// <param name="repositorio">Repositorio; por omision el de Cuaderno NODISLA.</param>
    /// <param name="largoMaximo">Largo maximo de la direccion.</param>
    public static EnvioDeInforme Preparar(
        DatosDeInforme datos,
        RepositorioPublico? repositorio = null,
        int largoMaximo = LargoMaximoDeDireccion)
    {
        ArgumentNullException.ThrowIfNull(datos);
        repositorio ??= RepositorioPublico.CuadernoNodisla;

        var titulo = Titulo(datos.Titulo);
        var cuerpo = Cuerpo(datos);

        var completa = Direccion(repositorio, titulo, cuerpo);
        if (completa.Length <= largoMaximo)
        {
            return new EnvioDeInforme(new Uri(completa), cuerpo, CuerpoEnElPortapapeles: false);
        }

        var corta = Direccion(repositorio, titulo, AvisoDePortapapeles);
        return new EnvioDeInforme(new Uri(corta), cuerpo, CuerpoEnElPortapapeles: true);
    }

    private static string Direccion(RepositorioPublico repositorio, string titulo, string cuerpo) =>
        $"{repositorio.NuevaIncidencia}?title={Uri.EscapeDataString(titulo)}&body={Uri.EscapeDataString(cuerpo)}";

    private static void Seccion(StringBuilder cuerpo, string rotulo, string? texto)
    {
        cuerpo.Append("### ").AppendLine(rotulo).AppendLine();
        cuerpo.AppendLine(string.IsNullOrWhiteSpace(texto) ? "_Sin indicar._" : texto.Trim());
        cuerpo.AppendLine();
    }
}
