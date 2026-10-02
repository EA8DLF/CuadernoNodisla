using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Ui.Idiomas;

/// <summary>
/// Un texto del programa en XAML: <c>Text="{loc:Texto Comun.Cerrar}"</c>, con
/// <c>xmlns:loc="clr-namespace:Nodisla.Cuaderno.Ui.Idiomas"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Devuelve un enlace a <see cref="FuenteDeTextos"/>, así que el texto cambia solo cuando el
/// operador cambia de idioma. Con <see cref="Valor"/> (y <see cref="Valor2"/>) rellena los huecos
/// del texto con datos enlazados: un texto «{0:N0} contactos» con <c>Valor={Binding Total}</c>
/// escribe «1.795 contactos», y «1,795 QSOs» en inglés.
/// </para>
/// <para>
/// Donde no cabe un enlace —una propiedad normal de un objeto que no es de WPF— devuelve el
/// texto tal cual, y ese ya no cambia en caliente.
/// </para>
/// </remarks>
[MarkupExtensionReturnType(typeof(object))]
public sealed class Texto : MarkupExtension
{
    /// <summary>Para el XAML.</summary>
    public Texto()
    {
    }

    /// <summary>El texto de una clave.</summary>
    /// <param name="clave">Clave con su apartado delante.</param>
    public Texto(string clave)
    {
        Clave = clave;
    }

    /// <summary>Clave del texto, con su apartado delante (<c>Comun.Cerrar</c>).</summary>
    [ConstructorArgument("clave")]
    public string Clave { get; set; } = string.Empty;

    /// <summary>Lo que va en el hueco <c>{0}</c>, enlazado.</summary>
    public BindingBase? Valor { get; set; }

    /// <summary>Lo que va en el hueco <c>{1}</c>, enlazado.</summary>
    public BindingBase? Valor2 { get; set; }

    /// <summary>El enlace al texto de una clave, para montarlo desde código.</summary>
    /// <param name="clave">Clave del texto.</param>
    /// <returns>Un enlace de solo lectura.</returns>
    public static Binding Enlace(string clave) => new($"[{clave}]")
    {
        Source = FuenteDeTextos.Instancia,
        Mode = BindingMode.OneWay,
    };

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var objetivo = serviceProvider?.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;

        // Dentro de una plantilla todavía no hay control: se devuelve la propia extensión y WPF
        // vuelve a llamar al crear cada copia de la plantilla.
        if (objetivo?.TargetObject is { } o && o.GetType().FullName == "System.Windows.SharedDp") return this;

        BindingBase enlace = Valor is null && Valor2 is null ? Enlace(Clave) : Compuesto();

        if (objetivo?.TargetObject is Setter or DataTrigger or Trigger or Condition) return enlace;
        if (objetivo?.TargetObject is DependencyObject && objetivo.TargetProperty is DependencyProperty)
        {
            return enlace.ProvideValue(serviceProvider!);
        }

        return Valor is null && Valor2 is null ? Textos.T(Clave) : this;
    }

    private MultiBinding Compuesto()
    {
        var compuesto = new MultiBinding { Converter = Rellenar.Instancia, Mode = BindingMode.OneWay };
        compuesto.Bindings.Add(Enlace(Clave));
        if (Valor is not null) compuesto.Bindings.Add(Valor);
        if (Valor2 is not null) compuesto.Bindings.Add(Valor2);
        return compuesto;
    }

    /// <summary>Rellena los huecos del texto con los valores, en la cultura del programa.</summary>
    private sealed class Rellenar : IMultiValueConverter
    {
        public static readonly Rellenar Instancia = new();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 0 || values[0] is not string formato) return string.Empty;
            var resto = values.Skip(1)
                .Select(v => v == DependencyProperty.UnsetValue || v == BindingOperations.DisconnectedSource ? null : v)
                .ToArray();
            try
            {
                return string.Format(Textos.Cultura, formato, resto);
            }
            catch (FormatException)
            {
                return formato;
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
