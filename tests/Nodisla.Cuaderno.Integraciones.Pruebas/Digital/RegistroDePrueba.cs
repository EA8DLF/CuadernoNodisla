using Microsoft.Extensions.Logging;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Digital;

/// <summary>Registro que se queda con lo que se escribe, para poder comprobarlo.</summary>
internal sealed class RegistroDePrueba : ILogger
{
    private readonly List<string> _lineas = [];

    /// <summary>Todo lo registrado, ya formateado.</summary>
    public IReadOnlyList<string> Lineas
    {
        get { lock (_lineas) return _lineas.ToArray(); }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_lineas) _lineas.Add(formatter(state, exception));
    }
}
