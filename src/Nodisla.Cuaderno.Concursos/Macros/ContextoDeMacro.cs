using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Macros;

/// <summary>
/// Lo que sabe el programa en el momento de disparar una macro.
/// </summary>
/// <remarks>
/// El contexto no sabe si lo que se va a hacer con el texto es manipularlo en telegrafia,
/// reproducir un fichero de voz o meterlo en una trama digital. Esa ignorancia es
/// deliberada: es lo que permite que la misma macro valga para los tres medios.
/// </remarks>
public sealed record ContextoDeMacro
{
    /// <summary>Indicativo con el que se opera.</summary>
    public Indicativo MiIndicativo { get; init; }

    /// <summary>Indicativo del corresponsal, aunque este a medias.</summary>
    public string? Corresponsal { get; init; }

    /// <summary>Nombre del corresponsal.</summary>
    public string? Nombre { get; init; }

    /// <summary>Localidad del corresponsal.</summary>
    public string? Qth { get; init; }

    /// <summary>Mi localidad.</summary>
    public string? MiQth { get; init; }

    /// <summary>Mi nombre.</summary>
    public string? MiNombre { get; init; }

    /// <summary>Localizador del corresponsal.</summary>
    public Locator Locator { get; init; }

    /// <summary>Mi localizador.</summary>
    public Locator MiLocator { get; init; }

    /// <summary>Informe que se envia.</summary>
    public Informe InformeEnviado { get; init; }

    /// <summary>Informe que se ha recibido.</summary>
    public Informe InformeRecibido { get; init; }

    /// <summary>Numero de serie que toca enviar.</summary>
    public int? Serie { get; init; }

    /// <summary>Numero de serie recibido.</summary>
    public int? SerieRecibida { get; init; }

    /// <summary>Intercambio completo que se envia.</summary>
    public string? Intercambio { get; init; }

    /// <summary>Intercambio recibido.</summary>
    public string? IntercambioRecibido { get; init; }

    /// <summary>Banda en la que se esta.</summary>
    public Banda Banda { get; init; }

    /// <summary>Modo en el que se esta.</summary>
    public Modo Modo { get; init; }

    /// <summary>Frecuencia del equipo.</summary>
    public Frecuencia Frecuencia { get; init; }

    /// <summary>Identificador del concurso en marcha.</summary>
    public string? Concurso { get; init; }

    /// <summary>Momento que usan <c>&lt;HORA&gt;</c> y <c>&lt;FECHA&gt;</c>.</summary>
    /// <remarks>
    /// Se pasa en vez de leer el reloj dentro del motor para que las macros se puedan probar
    /// sin depender de la hora a la que corran las pruebas.
    /// </remarks>
    public DateTimeOffset AhoraUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Sustituciones propias del operador, por si quiere una que el programa no trae.
    /// </summary>
    public IReadOnlyDictionary<string, string> Propias { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
