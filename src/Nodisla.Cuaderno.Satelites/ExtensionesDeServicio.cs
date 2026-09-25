using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nodisla.Cuaderno.Satelites.Catalogo;
using Nodisla.Cuaderno.Satelites.Seguimiento;

namespace Nodisla.Cuaderno.Satelites;

/// <summary>Registro del seguimiento de satelites en el contenedor de servicios.</summary>
public static class ExtensionesDeServicio
{
    /// <summary>Registra el seguidor de satelites y el catalogo.</summary>
    /// <remarks>
    /// El seguidor se registra como <b>unico</b>: guarda los elementos cargados y un propagador
    /// preparado por satelite, y rehacerlos en cada peticion tiraria justo el trabajo que hace
    /// que el seguimiento en vivo salga inmediato.
    /// <para>
    /// La descarga de elementos <b>no</b> se registra aqui a proposito: quien la quiera tiene
    /// que pedir su <see cref="System.Net.Http.HttpClient"/> y llamarla, para que no haya forma
    /// de que el programa salga a la red sin que nadie se lo haya mandado.
    /// </para>
    /// </remarks>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <param name="configurar">Ajustes del seguimiento.</param>
    /// <returns>La misma coleccion, para encadenar.</returns>
    public static IServiceCollection AnadirSatelites(
        this IServiceCollection servicios,
        Action<OpcionesDeSatelites> configurar)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(configurar);

        var opciones = new OpcionesDeSatelites();
        configurar(opciones);
        servicios.TryAddSingleton(opciones);

        servicios.TryAddSingleton(CatalogoDeSatelites.Instancia);
        servicios.TryAddSingleton<SeguidorDeSatelites>();

        return servicios;
    }
}
