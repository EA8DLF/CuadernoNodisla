using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.Idiomas;

/// <summary>
/// Lleva el idioma elegido a WPF: la cultura de los hilos (fechas y números del código) y el
/// <c>Language</c> de las ventanas (los <c>StringFormat</c> de los enlaces).
/// </summary>
/// <remarks>
/// Hay que fijar las tres cosas —la cultura del hilo, la de los hilos que nazcan después y el
/// idioma de WPF— porque cada una la mira un sitio distinto: los <c>ToString</c> del código miran
/// la del hilo, y los <c>StringFormat</c> de XAML miran el <c>Language</c> del elemento, que por
/// omisión es inglés. La frecuencia y las horas UTC NO siguen nada de esto: van con
/// <c>TextoDeFrecuencia</c> y cultura invariante, que es lo que manda ADIF.
/// </remarks>
public static class IdiomaDeLaInterfaz
{
    private static bool _arrancado;

    /// <summary>Variable de entorno para forzar un idioma sin tocar los ajustes (capturas y pruebas).</summary>
    public const string VariableDeEntorno = "CUADERNO_IDIOMA";

    /// <summary>Pone el idioma de arranque. Una sola vez, antes de crear ninguna ventana.</summary>
    /// <param name="elegido">Lo guardado en los ajustes (nulo = el del sistema).</param>
    public static void Arrancar(string? elegido)
    {
        if (Environment.GetEnvironmentVariable(VariableDeEntorno) is { Length: > 0 } forzado) elegido = forzado;

        Textos.Cambiar(Textos.CulturaPara(Textos.Resolver(elegido)));
        AplicarAHilos(Textos.Cultura);

        if (_arrancado) return;
        _arrancado = true;

        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(Textos.Cultura.IetfLanguageTag)));

        // Las ventanas que se abran después de un cambio nacen con el idioma de arranque (el de
        // los metadatos de arriba, que no se pueden volver a cambiar): se corrigen al cargar.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(AlCargarVentana));

        Textos.IdiomaCambiado += (_, _) => AlCambiar();
    }

    private static void AlCargarVentana(object sender, RoutedEventArgs e)
    {
        if (sender is Window ventana) PonerIdioma(ventana);
    }

    private static void AlCambiar()
    {
        AplicarAHilos(Textos.Cultura);
        if (Application.Current is not { } aplicacion) return;
        foreach (Window ventana in aplicacion.Windows) PonerIdioma(ventana);
    }

    private static void PonerIdioma(FrameworkElement elemento)
    {
        var lengua = XmlLanguage.GetLanguage(Textos.Cultura.IetfLanguageTag);
        if (!Equals(elemento.Language, lengua)) elemento.Language = lengua;
    }

    private static void AplicarAHilos(CultureInfo cultura)
    {
        Thread.CurrentThread.CurrentCulture = cultura;
        Thread.CurrentThread.CurrentUICulture = cultura;
        CultureInfo.DefaultThreadCurrentCulture = cultura;
        CultureInfo.DefaultThreadCurrentUICulture = cultura;
    }
}
