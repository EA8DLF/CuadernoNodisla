using System.Windows;
using System.Windows.Threading;

namespace Nodisla.Cuaderno.Ui.Conversores;

/// <summary>
/// Lleva al hilo de la ventana lo que llega de fuera.
/// </summary>
/// <remarks>
/// El equipo, el cluster y los programas de modos digitales avisan desde sus propios hilos:
/// un temporizador, un socket, un puerto serie. Tocar una coleccion enlazada desde ahi
/// revienta la ventana con un fallo que no dice nada util. Todo lo que entra por un evento de
/// un puerto pasa por aqui antes de tocar nada de la interfaz.
/// </remarks>
internal static class Hilo
{
    /// <summary>Hilo de la ventana, si la aplicacion esta viva.</summary>
    private static Dispatcher? Ventana => Application.Current?.Dispatcher;

    /// <summary>Ejecuta la accion en el hilo de la ventana, ya sea ahora o en cuanto pueda.</summary>
    /// <param name="accion">Lo que hay que hacer.</param>
    public static void EnLaVentana(Action accion)
    {
        ArgumentNullException.ThrowIfNull(accion);

        var despachador = Ventana;
        if (despachador is null || despachador.CheckAccess())
        {
            accion();
            return;
        }

        // En segundo plano: lo que llega de la radio no puede adelantarse a lo que el
        // operador esta tecleando.
        _ = despachador.BeginInvoke(DispatcherPriority.Background, accion);
    }
}
