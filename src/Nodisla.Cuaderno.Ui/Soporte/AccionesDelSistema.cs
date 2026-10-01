using System.Diagnostics;
using System.Reflection;
using System.Windows;
using Nodisla.Cuaderno.Servicios.Actualizaciones;
using Nodisla.Cuaderno.Ui.Vistas;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>
/// Lo que el aviso de versiones y el informe de fallos le piden a Windows: preguntar, abrir el
/// navegador, copiar al portapapeles, lanzar el instalador y cerrar el programa.
/// </summary>
/// <remarks>
/// Todo pasa por aqui para que los modelos de vista se puedan probar sin abrir ni una ventana:
/// las pruebas ponen sus propias funciones y miran que se llamaron.
/// </remarks>
public sealed class AccionesDelSistema
{
    /// <summary>Pregunta si/no (titulo, detalle, texto del boton). Cierto si acepta.</summary>
    public Func<string, string, string, bool> Confirmar { get; init; } = ConfirmarConVentana;

    /// <summary>Abre una direccion en el navegador predeterminado.</summary>
    public Action<Uri> AbrirEnNavegador { get; init; } = direccion =>
        Process.Start(new ProcessStartInfo(direccion.AbsoluteUri) { UseShellExecute = true });

    /// <summary>Copia texto al portapapeles.</summary>
    public Action<string> CopiarAlPortapapeles { get; init; } = texto => Clipboard.SetText(texto);

    /// <summary>Lanza el instalador ya verificado.</summary>
    /// <remarks>
    /// <c>/SP-</c> quita la pregunta inicial de Inno Setup (ya se pregunto aqui) y
    /// <c>/CLOSEAPPLICATIONS</c> le pide que cierre lo que tenga abiertos los ficheros, por si el
    /// programa tarda en salir.
    /// </remarks>
    public Action<string> LanzarInstalador { get; init; } = ruta =>
        Process.Start(new ProcessStartInfo(ruta, "/SP- /CLOSEAPPLICATIONS") { UseShellExecute = true });

    /// <summary>
    /// Cierra el programa de forma ordenada: cerrando la ventana principal, que es la que guarda
    /// el estado de los paneles y suelta el equipo al cerrarse.
    /// </summary>
    public Action CerrarPrograma { get; init; } = () =>
    {
        var aplicacion = Application.Current;
        if (aplicacion?.MainWindow is { } ventana) ventana.Close();
        else aplicacion?.Shutdown();
    };

    private static bool ConfirmarConVentana(string titulo, string detalle, string aceptar)
    {
        var dialogo = new VentanaDeConfirmacion
        {
            Titulo = titulo,
            Detalle = detalle,
            TextoDeAceptar = aceptar,
        };
        if (Application.Current?.MainWindow is { IsLoaded: true } principal) dialogo.Owner = principal;
        return dialogo.ShowDialog() == true;
    }
}

/// <summary>La version de este ejecutable.</summary>
public static class VersionInstalada
{
    /// <summary>
    /// La version informativa del ensamblado (sale de <c>&lt;Version&gt;</c> en
    /// <c>Directory.Build.props</c>), sin el sufijo de compilacion.
    /// </summary>
    public static VersionSemantica Actual { get; } = Leer();

    private static VersionSemantica Leer()
    {
        var ensamblado = typeof(App).Assembly;
        var informativa = ensamblado.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (VersionSemantica.TryAnalizar(informativa, out var version)) return version!;

        var numero = ensamblado.GetName().Version;
        return VersionSemantica.Analizar(numero is null ? "0.0.0" : $"{numero.Major}.{numero.Minor}.{Math.Max(0, numero.Build)}");
    }
}
