using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Concursos.Macros;

/// <summary>Una macro: un texto con sustituciones que se dispara con una tecla.</summary>
/// <param name="Tecla">Tecla que la lanza, por ejemplo <c>F1</c>.</param>
/// <param name="Nombre">Como se llama en el boton.</param>
/// <param name="Texto">El texto con sus etiquetas.</param>
/// <remarks>
/// No dice por que medio sale. La misma macro vale para el manipulador de telegrafia, para
/// un fichero de voz y para una trama digital, y quien la dispara decide con que.
/// </remarks>
public sealed record Macro(string Tecla, string Nombre, string Texto);

/// <summary>Un juego de macros: las doce teclas de una forma de operar.</summary>
/// <remarks>
/// Hay mas de un juego porque no se llama igual en un concurso que en un dia normal, ni en
/// telegrafia que en fonia. El juego se elige, no se reescriben las macros.
/// </remarks>
public sealed class JuegoDeMacros
{
    private readonly Dictionary<string, Macro> _porTecla;

    /// <summary>Crea un juego de macros.</summary>
    /// <param name="nombre">Como se llama el juego.</param>
    /// <param name="macros">Las macros que lo forman.</param>
    public JuegoDeMacros(string nombre, IEnumerable<Macro> macros)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nombre);
        ArgumentNullException.ThrowIfNull(macros);
        Nombre = nombre;
        Macros = macros.ToArray();
        _porTecla = new Dictionary<string, Macro>(Macros.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var macro in Macros) _porTecla[macro.Tecla] = macro;
    }

    /// <summary>Nombre del juego.</summary>
    public string Nombre { get; }

    /// <summary>Las macros, en el orden en que se declararon.</summary>
    public IReadOnlyList<Macro> Macros { get; }

    /// <summary>Busca la macro de una tecla.</summary>
    /// <param name="tecla">Tecla pulsada, por ejemplo <c>F3</c>.</param>
    /// <returns>La macro, o nulo si esa tecla no tiene ninguna.</returns>
    public Macro? De(string? tecla) =>
        tecla is { Length: > 0 } && _porTecla.TryGetValue(tecla, out var macro) ? macro : null;

    /// <summary>
    /// El juego de partida para trabajar en un concurso.
    /// </summary>
    /// <remarks>
    /// Son las cinco de siempre: llamada, contestar, intercambio, repetir y despedida. Estan
    /// escritas con las etiquetas propias para que sirvan de ejemplo al operador cuando abra
    /// el editor de macros. Se arma en cada consulta para que los nombres salgan en el idioma
    /// en uso.
    /// </remarks>
    public static JuegoDeMacros Concurso => new(Textos.T("Servicios.Concursos.Juego.Concurso"), [
        new Macro("F1", "CQ", "CQ TEST <MICALL> <MICALL> TEST"),
        new Macro("F2", Textos.T("Servicios.Concursos.Macro.Intercambio"), "<CALL> <RST> <INTERCAMBIO>"),
        new Macro("F3", Textos.T("Servicios.Concursos.Macro.Confirmar"), "TU <MICALL>"),
        new Macro("F4", Textos.T("Servicios.Concursos.Macro.MiIndicativo"), "<MICALL>"),
        new Macro("F5", Textos.T("Servicios.Concursos.Macro.SuIndicativo"), "<CALL>"),
        new Macro("F6", Textos.T("Servicios.Concursos.Macro.Repetir"), "AGN AGN"),
        new Macro("F7", Textos.T("Servicios.Concursos.Macro.PedirIndicativo"), "CALL?"),
        new Macro("F8", Textos.T("Servicios.Concursos.Macro.PedirIntercambio"), "NR?"),
    ]);

    /// <summary>El juego de partida para un contacto normal, fuera de concurso.</summary>
    public static JuegoDeMacros Normal => new(Textos.T("Servicios.Concursos.Juego.Normal"), [
        new Macro("F1", "CQ", "CQ CQ DE <MICALL> <MICALL> PSE K"),
        new Macro("F2", Textos.T("Servicios.Concursos.Macro.Contestar"), "<CALL> DE <MICALL> K"),
        new Macro("F3", Textos.T("Servicios.Concursos.Macro.Informe"), "<CALL> DE <MICALL> UR RST <RST> <RST> NAME <MINOMBRE> QTH <MIQTH> BK"),
        new Macro("F4", Textos.T("Servicios.Concursos.Macro.Despedida"), "<CALL> DE <MICALL> 73 GL SK"),
        new Macro("F5", Textos.T("Servicios.Concursos.Macro.MiIndicativo"), "<MICALL>"),
        new Macro("F6", Textos.T("Servicios.Concursos.Macro.Localizador"), "MY LOC <MILOCATOR>"),
    ]);
}
