using System.Windows;
using System.Windows.Threading;
using Nodisla.Cuaderno.Ui.VistaModelos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Toca las teclas de la pantalla del frontal (CENTER, 3DSS, MULTI, EXPAND, SPAN±, SPEED±, y
/// AUDIO para abrir la entrada de audio) para el
/// retrato de comprobacion.
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
