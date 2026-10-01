using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nodisla.Cuaderno.Impresion.Pdf;

namespace Nodisla.Cuaderno.Impresion;

/// <summary>Registro de la impresion de QSL en el contenedor de servicios.</summary>
public static class ExtensionesDeServicio
{
    /// <summary>Registra el generador de etiquetas y tarjetas.</summary>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <returns>La misma coleccion, para encadenar.</returns>
    /// <remarks>
    /// <b>Esta es la unica linea que hay que cambiar para cambiar de biblioteca de PDF.</b> El
    /// generador se registra contra <see cref="IGeneradorDeImpresos"/>; la pantalla, la
    /// seleccion de contactos y el modelo de la etiqueta no saben con que se dibuja.
    /// </remarks>
    public static IServiceCollection AnadirImpresionDeQsl(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddSingleton<IGeneradorDeImpresos, GeneradorDeImpresosPdf>();
        return servicios;
    }
}
