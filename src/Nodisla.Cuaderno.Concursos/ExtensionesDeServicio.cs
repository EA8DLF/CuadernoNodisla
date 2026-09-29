using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nodisla.Cuaderno.Concursos.Cabrillo;
using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Concursos.Macros;
using Nodisla.Cuaderno.Concursos.Telegrafia;

namespace Nodisla.Cuaderno.Concursos;

/// <summary>Registro del modulo de concursos en el contenedor de servicios.</summary>
public static class ExtensionesDeServicio
{
    /// <summary>
    /// Registra el catalogo de concursos, el exportador Cabrillo, el motor de macros y el
    /// manipulador de telegrafia.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El catalogo va como unico porque son doscientos y pico registros que no cambian en
    /// ejecucion. La sesion de concurso <b>no</b> se registra: la abre y la cierra el operador,
    /// y quien la crea es la interfaz cuando el operador dice que empieza un concurso.
    /// </para>
    /// <para>
    /// <b>El manipulador se registra sin salida de audio.</b> Asi, mientras nadie conecte la
    /// emision vigilada, el programa no puede transmitir telegrafia ni por accidente. Para
    /// darle voz hay que llamar a esta extension pasando <paramref name="emision"/>, y lo que
    /// se pasa es la emision vigilada del modulo de audio, que es la que pasa por
    /// <c>IVigilantePtt</c>.
    /// </para>
    /// </remarks>
    /// <param name="servicios">Coleccion de servicios.</param>
    /// <param name="opciones">Ajustes del manipulador; si es nulo, los de costumbre.</param>
    /// <param name="emision">
    /// Quien saca las muestras por la antena. Si es nulo, el manipulador genera audio pero no
    /// transmite.
    /// </param>
    /// <returns>La misma coleccion, para encadenar.</returns>
    public static IServiceCollection AnadirConcursos(
        this IServiceCollection servicios,
        OpcionesDeTelegrafia? opciones = null,
        Func<IServiceProvider, EmisionVigiladaDeMuestras>? emision = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.TryAddSingleton(_ => CatalogoDeConcursos.Predeterminado);
        servicios.TryAddSingleton<EscritorCabrillo>();
        servicios.TryAddSingleton<MotorDeMacros>();
        servicios.TryAddSingleton(opciones ?? new OpcionesDeTelegrafia());

        servicios.TryAddSingleton<ManipuladorPorAudio>(s => new ManipuladorPorAudio(
            emision?.Invoke(s),
            s.GetRequiredService<OpcionesDeTelegrafia>(),
            s.GetService<Microsoft.Extensions.Logging.ILogger<ManipuladorPorAudio>>()));
        servicios.TryAddSingleton<IManipuladorDeTelegrafia>(s => s.GetRequiredService<ManipuladorPorAudio>());

        return servicios;
    }
}
