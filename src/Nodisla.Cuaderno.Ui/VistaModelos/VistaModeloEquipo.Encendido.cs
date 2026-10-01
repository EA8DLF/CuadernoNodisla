using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Encender y apagar el equipo con la pulsación larga de LOCK, como la tecla ⏻LOCK de la radio.
/// </summary>
/// <remarks>
/// Pedido y autorizado expresamente por Jose el 29-09-2026. Apagar SIEMPRE pregunta antes: si
/// nadie ha puesto quién pregunte (<see cref="ConfirmarApagado"/>), no se apaga.
/// </remarks>
public sealed partial class VistaModeloEquipo
{
    /// <summary>El equipo de ahora se puede encender y apagar desde el programa.</summary>
    public bool PuedeEncenderOApagar => Real is IEquipoConEncendido;

    /// <summary>LOCK se puede pulsar: con el equipo conectado, o apagado si se puede encender.</summary>
    public bool LockDisponible => Conectado || PuedeEncenderOApagar;

    /// <summary>Lo rellena la ventana: pregunta antes de apagar la radio.</summary>
    public Func<string, bool>? ConfirmarApagado { get; set; }

    /// <summary>Hay un encendido o apagado en marcha.</summary>
    public bool Encendiendo { get; private set; }

    /// <summary>
    /// Pulsación larga de LOCK: apaga el equipo si está conectado (tras confirmar) o lo
    /// enciende si no lo está.
    /// </summary>
    public async Task EncenderOApagarAsync()
    {
        if (Real is not IEquipoConEncendido equipo || Encendiendo) return;

        Encendiendo = true;
        OnPropertyChanged(nameof(Encendiendo));
        try
        {
            if (Conectado)
            {
                if (ConfirmarApagado is not { } preguntar || !preguntar("¿Apagar el FT-710?"))
                {
                    Aviso = ConfirmarApagado is null ? "No se ha apagado: hace falta confirmación." : string.Empty;
                    return;
                }

                _medicion.Stop();
                await equipo.ApagarAsync().ConfigureAwait(true);
                Aviso = "Radio apagada. Pulsación larga de LOCK para encenderla.";
            }
            else
            {
                Aviso = "Encendiendo la radio…";
                var motivo = await equipo.EncenderAsync().ConfigureAwait(true);
                // Mandos, memorias y medidores se rehacen solos al pasar a conectado (Recoger).
                Aviso = motivo ?? "Radio encendida y conectada.";
            }
        }
        catch (Exception ex)
        {
            Aviso = $"No se ha podido {(Conectado ? "apagar" : "encender")} la radio: {ex.Message}";
        }
        finally
        {
            Encendiendo = false;
            OnPropertyChanged(nameof(Encendiendo));
            OnPropertyChanged(nameof(LockDisponible));
        }
    }
}
