using System.Windows;
using System.Windows.Threading;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Toca las teclas de la pantalla del frontal (CENTER, 3DSS, MULTI, EXPAND, SPAN±, SPEED±, y
/// AUDIO para abrir la entrada de audio) para el
/// retrato de comprobacion. Ademas, la sintonia desde el analizador: <c>IR:14074000</c>,
/// <c>CLIC:0.25</c> (sitio del clic, de 0 a 1) y <c>RUEDA:+3</c>.
/// </summary>
/// <remarks>
/// Se activa con <c>CUADERNO_TECLAS_ANALIZADOR</c>, una lista separada por barras
/// (<c>3DSS|SPAN+</c>), y <b>solo con los puertos simulados</b>: una variable de entorno olvidada
/// no puede mandar ordenes a la radio de verdad.
/// </remarks>
public static class TeclasDelAnalizadorDePrueba
{
    /// <summary>Programa las pulsaciones, si se han pedido.</summary>
    /// <param name="ventana">Ventana principal (su modelo lleva el equipo).</param>
    /// <param name="antes">Cuanto esperar antes de la primera.</param>
    public static void ProgramarSiSePide(Window ventana, TimeSpan antes)
    {
        ArgumentNullException.ThrowIfNull(ventana);
        if (Environment.GetEnvironmentVariable("CUADERNO_TECLAS_ANALIZADOR") is not { Length: > 0 } lista) return;
        if (!ConfiguracionDeServicios.ConPuertosSimulados) return;

        var cola = new Queue<string>(lista.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var reloj = new DispatcherTimer { Interval = antes };
        reloj.Tick += (_, _) =>
        {
            reloj.Interval = TimeSpan.FromMilliseconds(600);
            if (ventana.DataContext is not VistaModeloPrincipal { Equipo: { } equipo } principal)
            {
                reloj.Stop();
                return;
            }

            Log.Information(
                "Prueba: equipo {Ajuste}; dibujo {Rotulo} 3DSS={TresD} EXPAND={Ampliado}",
                equipo.AjusteDelAnalizador,
                principal.Analizador.Rotulo,
                principal.Analizador.EnTresD,
                principal.Analizador.Ampliado);
            if (cola.Count == 0)
            {
                reloj.Stop();
                return;
            }

            // MULTI es de la pantalla del programa; AUDIO pulsa «Abrir el audio» del aviso de MULTI.
            var tecla = cola.Dequeue();

            // CLIC:0.25 pulsa en ese sitio del analizador (0 izquierda, 1 derecha); RUEDA:+3 gira
            // la rueda encima. Lo mismo que el raton, por el mismo camino.
            if (tecla.StartsWith("CLIC:", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(tecla[5..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var donde))
            {
                _ = principal.Analizador.ClicAsync(donde);
                Log.Information("Prueba: clic en el analizador en {Donde}", donde);
                return;
            }

            // IR:14074000 lleva el VFO activo ahi, por el camino del clic (sin ajustar al paso).
            if (tecla.StartsWith("IR:", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(tecla[3..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var hz))
            {
                _ = principal.Analizador.Sintonia?.IrAAsync(hz);
                Log.Information("Prueba: sintonia del analizador a {Hz}", hz);
                return;
            }

            if (tecla.StartsWith("RUEDA:", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(tecla[6..], System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var muescas))
            {
                _ = principal.Analizador.RuedaAsync(muescas);
                Log.Information("Prueba: rueda en el analizador {Muescas}", muescas);
                return;
            }

            System.Windows.Input.ICommand orden = tecla switch
            {
                "MULTI" => principal.Analizador.AlternarMultipleCommand,
                "AUDIO" => principal.Modem.EscucharCommand,
                "SIN AUDIO" => principal.Modem.PararCommand,
                _ => equipo.TeclaDelAnalizadorCommand,
            };
            var puede = orden.CanExecute(tecla);
            if (puede) orden.Execute(tecla);
            Log.Information("Prueba: tecla del analizador {Tecla} ({Resultado})", tecla, puede ? "pulsada" : "apagada");
        };
        reloj.Start();
    }
}
