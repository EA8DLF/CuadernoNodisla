using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Pulsa teclas en la ventana sin tocar el teclado de nadie, para comprobar los atajos.
/// </summary>
/// <remarks>
/// Se activa con <c>CUADERNO_TECLAS</c>, una lista separada por barras: <c>F3|Ctrl+D5|Escape</c>.
/// Cada tecla se entrega como la entregaria Windows —primero el aviso previo y despues la
/// pulsacion— al elemento que tenga el foco, o a la ventana si no lo tiene nadie; asi pasan
/// por los mismos atajos de la ventana y de los paneles. Tras cada una se apunta en el registro
/// en que pestaña esta la ventana y quien tiene el foco. Solo sirve con la ventana apartada:
/// no mueve el raton ni roba el teclado.
/// </remarks>
public static class TeclasDePrueba
{
    /// <summary>Programa la pulsacion de las teclas pedidas, si se han pedido.</summary>
    /// <param name="ventana">Ventana que recibe las teclas.</param>
    /// <param name="antes">Cuanto esperar antes de la primera.</param>
    public static void ProgramarSiSePide(Window ventana, TimeSpan antes)
    {
        if (Environment.GetEnvironmentVariable("CUADERNO_TECLAS") is not { Length: > 0 } lista) return;

        var teclas = lista.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cola = new Queue<string>(teclas);
        var reloj = new DispatcherTimer { Interval = antes };
        reloj.Tick += (_, _) =>
        {
            reloj.Interval = TimeSpan.FromMilliseconds(700);
            Log.Information("Foco ahora en {Foco}.", DescribirFoco(ventana));
            if (cola.Count == 0)
            {
                reloj.Stop();
                return;
            }

            var texto = cola.Dequeue();
            try
            {
                Pulsar(ventana, texto);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se ha podido pulsar {Tecla}.", texto);
            }

            // La pestaña cambia en el acto; el foco puede tardar un pase del despachador, asi
            // que se mira justo antes de la tecla siguiente (700 ms despues).
            Log.Information(
                "Tecla {Tecla}: pestaña {Pestana}; foco antes de la tecla, en {Foco}.",
                texto,
                (ventana.DataContext as VistaModeloPrincipal)?.IndiceDeLaPestana,
                DescribirFoco(ventana));
        };
        reloj.Start();
    }

    private static void Pulsar(Window ventana, string texto)
    {
        var partes = texto.Split('+');
        var modificadores = ModifierKeys.None;
        foreach (var m in partes[..^1])
        {
            modificadores |= m.ToUpperInvariant() switch
            {
                "CTRL" => ModifierKeys.Control,
                "ALT" => ModifierKeys.Alt,
                "MAYUS" or "SHIFT" => ModifierKeys.Shift,
                _ => ModifierKeys.None,
            };
        }

        var tecla = Enum.Parse<Key>(partes[^1], ignoreCase: true);
        var fuente = PresentationSource.FromVisual(ventana)
                     ?? throw new InvalidOperationException("La ventana no tiene fuente de presentacion.");
        // La ventana apartada no se activa, asi que el foco que vale es el logico de la ventana.
        var destino = FocusManager.GetFocusedElement(ventana) ?? ventana;

        // Los atajos con Ctrl miran Keyboard.Modifiers, que sale del teclado de verdad. Para
        // los que llevan modificador se comprueba el atajo de la ventana a mano.
        if (modificadores != ModifierKeys.None)
        {
            foreach (var enlace in ventana.InputBindings.OfType<KeyBinding>())
            {
                if (enlace.Key == tecla && enlace.Modifiers == modificadores
                    && enlace.Command?.CanExecute(enlace.CommandParameter) == true)
                {
                    enlace.Command.Execute(enlace.CommandParameter);
                    return;
                }
            }

            Log.Warning("Ningun atajo de la ventana atiende {Tecla}.", texto);
            return;
        }

        var previa = new KeyEventArgs(Keyboard.PrimaryDevice, fuente, 0, tecla)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        destino.RaiseEvent(previa);
        if (previa.Handled) return;

        var pulsacion = new KeyEventArgs(Keyboard.PrimaryDevice, fuente, 0, tecla)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        };
        destino.RaiseEvent(pulsacion);
    }

    private static string DescribirFoco(Window ventana) => FocusManager.GetFocusedElement(ventana) switch
    {
        FrameworkElement { Name.Length: > 0 } fe => $"{fe.GetType().Name}#{fe.Name}",
        null => "nadie",
        var otro => otro.GetType().Name,
    };
}
