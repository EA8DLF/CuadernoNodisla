using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nodisla.Cuaderno.Radio.Pruebas.Dobles;

/// <summary>
/// Registro que se lo queda todo, para poder comprobar que nada se traga en silencio.
/// </summary>
internal sealed class RegistroDeMentira : ILogger
{
    private readonly ConcurrentQueue<(LogLevel Nivel, string Mensaje, Exception? Error)> _apuntes = new();

    /// <summary>Todo lo anotado.</summary>
    internal IReadOnlyCollection<(LogLevel Nivel, string Mensaje, Exception? Error)> Apuntes => _apuntes;

    /// <summary>Apuntes de un nivel concreto.</summary>
    /// <param name="nivel">Nivel buscado.</param>
    /// <returns>Los mensajes de ese nivel.</returns>
    internal IReadOnlyList<string> De(LogLevel nivel) =>
        _apuntes.Where(apunte => apunte.Nivel == nivel).Select(apunte => apunte.Mensaje).ToList();

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        _apuntes.Enqueue((logLevel, formatter(state, exception), exception));
    }
}
