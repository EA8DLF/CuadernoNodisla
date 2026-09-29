using System.Windows;
using System.Windows.Threading;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Espera a que el hilo de ventana de las pruebas haya procesado lo que tenga encolado.
/// </summary>
/// <remarks>
/// <para>
/// Los modelos de vista pasan los avisos de la radio, del cluster y del modem al hilo de la
/// ventana (<c>Hilo.EnLaVentana</c>). Sin <see cref="Application"/> eso ocurre en el acto; pero
/// en cuanto una prueba con ventana crea la aplicacion —ver <c>HiloDeVentana</c>—, los avisos
/// se encolan en su hilo y llegan despues. Segun el orden en que xUnit reparta las clases, una
/// prueba sin ventana podia mirar antes de que llegaran: en paralelo pasaba y en serie no.
/// </para>
/// <para>
/// Aqui no se mide ningun tiempo: se pide al despachador una tarea vacia con la prioridad mas
/// baja, que solo se ejecuta cuando ya ha hecho todo lo anterior. Dos veces, porque lo que se
/// procesa en la primera puede encolar algo mas.
/// </para>
/// </remarks>
internal static class EsperaALaVentana
{
    private static Dispatcher? Ajeno
    {
        get
        {
            var despachador = Application.Current?.Dispatcher;
            return despachador is null || despachador.CheckAccess() || despachador.HasShutdownStarted ? null : despachador;
        }
    }

    /// <summary>Espera, sin bloquear, a que la ventana lo haya procesado todo.</summary>
    public static async Task DrenarAsync()
    {
        if (Ajeno is not { } despachador) return;
        await despachador.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await despachador.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Lo mismo, para las pruebas que no son asincronas.</summary>
    public static void Drenar()
    {
        if (Ajeno is not { } despachador) return;
        despachador.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        despachador.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
}
