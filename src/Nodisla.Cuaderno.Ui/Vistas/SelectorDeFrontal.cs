using System.Windows;

namespace Nodisla.Cuaderno.Ui.Vistas;

/// <summary>
/// Crea el frontal dibujado que corresponde a un modelo, por el nombre de su clase.
/// </summary>
/// <remarks>
/// <para>
/// El nombre sale de <c>ModeloDeEquipo.Frontal</c> (via <c>VistaModeloEquipo.NombreDelFrontal</c>).
/// Se busca una clase con ese nombre, derivada de <see cref="FrameworkElement"/> y con
/// constructor sin parametros, primero en <c>Nodisla.Cuaderno.Ui.Vistas.Frontales</c> y luego en
/// <c>Nodisla.Cuaderno.Ui.Vistas</c> (donde vive el del FT-710).
/// </para>
/// <para>
/// Si no existe, se pone <c>FrontalGenerico</c>; y si tampoco existe todavia, el del FT-710, que
/// es lo que habia antes de haber mas modelos. Los dibujos heredan el <c>DataContext</c>, que es
/// el <c>VistaModeloEquipo</c>.
/// </para>
/// </remarks>
public static class SelectorDeFrontal
{
    /// <summary>Espacio de nombres de los frontales dibujados.</summary>
    public const string EspacioDeLosFrontales = "Nodisla.Cuaderno.Ui.Vistas.Frontales";

    /// <summary>Nombre del frontal para los modelos sin dibujo propio.</summary>
    public const string Generico = "FrontalGenerico";

    /// <summary>Busca la clase del frontal.</summary>
    /// <param name="nombre">Nombre de la clase (<c>FrontalIc7300</c>).</param>
    /// <returns>El tipo, o nulo.</returns>
    public static Type? Buscar(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return null;
        var ensamblado = typeof(SelectorDeFrontal).Assembly;
        foreach (var espacio in new[] { EspacioDeLosFrontales, "Nodisla.Cuaderno.Ui.Vistas" })
        {
            var tipo = ensamblado.GetType($"{espacio}.{nombre}", throwOnError: false);
            if (tipo is not null && typeof(FrameworkElement).IsAssignableFrom(tipo) && tipo.GetConstructor(Type.EmptyTypes) is not null)
            {
                return tipo;
            }
        }

        return null;
    }

    /// <summary>El tipo que se pondra de verdad para ese nombre, con las reservas aplicadas.</summary>
    /// <param name="nombre">Nombre pedido.</param>
    /// <returns>El tipo del frontal.</returns>
    public static Type Resolver(string? nombre) => Buscar(nombre) ?? Buscar(Generico) ?? typeof(FrontalFt710);

    /// <summary>Crea el frontal.</summary>
    /// <param name="nombre">Nombre pedido.</param>
    /// <returns>El frontal.</returns>
    public static FrameworkElement Crear(string? nombre) => (FrameworkElement)Activator.CreateInstance(Resolver(nombre))!;
}
