using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Engancha una casilla de contrasena a su <see cref="SecretoDeServicio"/>.
/// </summary>
/// <remarks>
/// <para>
/// La contrasena de un <see cref="PasswordBox"/> no se puede enlazar —a proposito: no es una
/// propiedad de dependencia—, asi que hay que llevarla a mano: lo tecleado va a
/// <see cref="SecretoDeServicio.Nuevo"/>, y cuando el modelo lo vacia (al guardar o al borrar)
/// se vacian tambien los puntos de la casilla. Sin lo segundo parecia que no se habia guardado.
/// </para>
/// <para>
/// Es lo mismo que hace a mano el panel de ajustes con sus tarjetas, puesto en un sitio para
/// que cualquier vista lo use con <c>vistas:EnlaceDeSecreto.Activo="True"</c>.
/// </para>
/// </remarks>
public static class EnlaceDeSecreto
{
    /// <summary>Activa el enlace en una casilla de contrasena.</summary>
    public static readonly DependencyProperty ActivoProperty = DependencyProperty.RegisterAttached(
        "Activo", typeof(bool), typeof(EnlaceDeSecreto), new PropertyMetadata(false, AlCambiar));

    private static readonly DependencyProperty EscuchaProperty = DependencyProperty.RegisterAttached(
        "Escucha", typeof(Escucha), typeof(EnlaceDeSecreto));

    /// <summary>Lee si el enlace esta activo.</summary>
    /// <param name="casilla">La casilla.</param>
    public static bool GetActivo(DependencyObject casilla) => (bool)casilla.GetValue(ActivoProperty);

    /// <summary>Activa o desactiva el enlace.</summary>
    /// <param name="casilla">La casilla.</param>
    /// <param name="valor">Activo o no.</param>
    public static void SetActivo(DependencyObject casilla, bool valor) => casilla.SetValue(ActivoProperty, valor);

    private static void AlCambiar(DependencyObject destino, DependencyPropertyChangedEventArgs e)
    {
        if (destino is not PasswordBox casilla) return;
        if (e.NewValue is true)
        {
            casilla.PasswordChanged += AlEscribir;
            casilla.Loaded += AlMontar;
            casilla.Unloaded += AlDesmontar;
            casilla.DataContextChanged += AlCambiarDeSecreto;
            if (casilla.IsLoaded) Enganchar(casilla);
        }
        else
        {
            casilla.PasswordChanged -= AlEscribir;
            casilla.Loaded -= AlMontar;
            casilla.Unloaded -= AlDesmontar;
            casilla.DataContextChanged -= AlCambiarDeSecreto;
            Soltar(casilla);
        }
    }

    private static void AlEscribir(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox { DataContext: SecretoDeServicio secreto } casilla) secreto.Nuevo = casilla.Password;
    }

    private static void AlMontar(object sender, RoutedEventArgs e) => Enganchar((PasswordBox)sender);

    private static void AlDesmontar(object sender, RoutedEventArgs e) => Soltar((PasswordBox)sender);

    private static void AlCambiarDeSecreto(object sender, DependencyPropertyChangedEventArgs e)
    {
        var casilla = (PasswordBox)sender;
        Soltar(casilla);
        if (casilla.IsLoaded) Enganchar(casilla);
    }

    private static void Enganchar(PasswordBox casilla)
    {
        Soltar(casilla);
        if (casilla.DataContext is not SecretoDeServicio secreto) return;
        var escucha = new Escucha(casilla, secreto);
        secreto.PropertyChanged += escucha.AlCambiar;
        casilla.SetValue(EscuchaProperty, escucha);
    }

    private static void Soltar(PasswordBox casilla)
    {
        if (casilla.GetValue(EscuchaProperty) is not Escucha escucha) return;
        escucha.Secreto.PropertyChanged -= escucha.AlCambiar;
        casilla.ClearValue(EscuchaProperty);
    }

    private sealed class Escucha(PasswordBox casilla, SecretoDeServicio secreto)
    {
        public SecretoDeServicio Secreto => secreto;

        public void AlCambiar(object? remitente, PropertyChangedEventArgs cambio)
        {
            if (cambio.PropertyName == nameof(SecretoDeServicio.Nuevo)
                && secreto.Nuevo.Length == 0
                && casilla.Password.Length > 0)
            {
                casilla.Clear();
            }
        }
    }
}
