using System.Windows;

namespace Nodisla.Cuaderno.Ui.Recursos;

/// <summary>
/// Cambio de tema. Los dos diccionarios declaran exactamente las mismas claves, asi que basta
/// con sustituir el primero de la lista y todo lo que use <c>DynamicResource</c> se entera solo.
/// </summary>
public static class Temas
{
    private static readonly Uri Claro = new("Recursos/Tema.Claro.xaml", UriKind.Relative);
    private static readonly Uri Oscuro = new("Recursos/Tema.Oscuro.xaml", UriKind.Relative);

    /// <summary>Aplica el tema oscuro o el claro a toda la aplicacion.</summary>
    public static void Aplicar(bool oscuro)
    {
        var aplicacion = Application.Current;
        if (aplicacion is null) return;

        var diccionarios = aplicacion.Resources.MergedDictionaries;
        if (diccionarios.Count == 0) return;

        diccionarios[0] = new ResourceDictionary { Source = oscuro ? Oscuro : Claro };
    }
}
