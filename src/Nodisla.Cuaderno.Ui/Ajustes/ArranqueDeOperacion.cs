namespace Nodisla.Cuaderno.Ui.Ajustes;

/// <summary>
/// Si los paneles de operacion se conectan solos al arrancar.
/// </summary>
/// <remarks>
/// Mientras detras hay puertos simulados vale cierto: asi la pantalla de operacion se ve
/// funcionando desde el primer arranque, sin tener que pulsar tres botones para comprobar
/// nada.
///
/// <b>Con los puertos de verdad esto pasa a falso.</b> Conectar solo con un equipo real
/// significa abrir un puerto serie y empezar a mandar ordenes CAT a una radio que puede estar
/// haciendo otra cosa; eso lo decide el operador, no el programa.
/// </remarks>
/// <param name="ConectarSolo">Los paneles se conectan sin que nadie lo pida.</param>
public sealed record ArranqueDeOperacion(bool ConectarSolo)
{
    /// <summary>Lo que vale mientras los puertos son los simulados.</summary>
    public static ArranqueDeOperacion ConPuertosSimulados { get; } = new(ConectarSolo: true);

    /// <summary>Lo que valdra cuando detras haya una radio de verdad.</summary>
    public static ArranqueDeOperacion ConPuertosReales { get; } = new(ConectarSolo: false);
}
