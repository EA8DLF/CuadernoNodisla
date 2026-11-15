using Serilog;

namespace Nodisla.Cuaderno.Ui.Soporte;

/// <summary>
/// Lo ultimo que se puede hacer con un fallo que nadie ha atendido: dejarlo escrito.
/// </summary>
/// <remarks>
/// Un fallo en un hilo que no es el de interfaz (el lector del puerto serie, el del audio, un
/// temporizador) no pasa por <c>DispatcherUnhandledException</c>: tumba el proceso sin mas. El
/// vigilante de PTT se engancha al mismo aviso para soltar el PTT; esto no lo sustituye ni lo
/// estorba, solo hace que el motivo quede en el registro.
/// </remarks>
public static class RedDeFallos
{
    /// <summary>Apunta un fallo sin atender del dominio de aplicacion.</summary>
    /// <param name="registro">Donde se apunta.</param>
    /// <param name="fallo">Lo que llega en <c>UnhandledExceptionEventArgs.ExceptionObject</c>.</param>
    /// <param name="terminaElProceso">Si el entorno va a terminar el proceso.</param>
    public static void ApuntarFalloSinAtender(ILogger registro, object? fallo, bool terminaElProceso)
    {
        ArgumentNullException.ThrowIfNull(registro);

        if (fallo is Exception ex)
        {
            registro.Fatal(ex, "Fallo sin atender fuera de la interfaz. Termina el proceso: {Termina}.", terminaElProceso);
        }
        else
        {
            registro.Fatal(
                "Fallo sin atender fuera de la interfaz ({Fallo}). Termina el proceso: {Termina}.",
                fallo, terminaElProceso);
        }
    }

    /// <summary>Apunta una tarea disparada y olvidada que ha fallado.</summary>
    /// <param name="registro">Donde se apunta.</param>
    /// <param name="fallo">El fallo de la tarea.</param>
    public static void ApuntarTareaOlvidada(ILogger registro, Exception fallo)
    {
        ArgumentNullException.ThrowIfNull(registro);
        registro.Error(fallo, "Tarea sin observar fallida (disparada y olvidada).");
    }
}
