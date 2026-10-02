using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Recursos;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Lo que el analizador puede pedirle a la radio: ir a una frecuencia (clic), mover el VFO por
/// pasos (rueda) e ir a un spot (clic en su rotulo).
/// </summary>
/// <remarks>
/// <para>
/// <b>Por el mismo camino que el doble clic del cluster.</b> Ir a un spot llama a lo mismo que la
/// lista de spots y el bandmap (<see cref="VistaModeloPrincipal.IrAlSpotAsync"/>): frecuencia y
/// modo al VFO activo y el indicativo al contacto nuevo. Las frecuencias sueltas van por
/// <see cref="IControlEquipo.PonerFrecuenciaAsync"/>, que en el FT-710 pregunta a la radio cual
/// es el VFO activo en ese momento (<c>VS;</c>) y escribe <c>FA</c> o <c>FB</c>: lo arreglado
/// cuando el spot iba al VFO equivocado tras pulsar A/B en la radio.
/// </para>
/// <para>
/// <b>Nada mientras se transmite</b>: cambiar de frecuencia en el aire es salirse de la
/// frecuencia con la portadora puesta. Tampoco sin equipo conectado.
/// </para>
/// <para>
/// La rueda puede dar muchas muescas seguidas: no se encolan ordenes, solo se recuerda la ultima
/// frecuencia pedida y se manda en cuanto termina la anterior. Las muescas se suman sobre la
/// ultima pedida (y no sobre la que dice la traza, que llega con retraso).
/// </para>
/// </remarks>
public sealed class SintoniaDelAnalizador
{
    /// <summary>Lo que se sigue sumando sobre la ultima frecuencia pedida en vez de sobre la de la radio.</summary>
    public const int MemoriaDeLaRuedaMs = 1500;

    private readonly IControlEquipo _control;
    private readonly Func<bool> _conectado;
    private readonly Func<bool> _transmitiendo;
    private readonly Func<FilaDeSpot, Task> _irAlSpot;
    private readonly Func<long> _milisegundos;
    private long? _pendiente;
    private bool _ocupado;
    private long _momentoDeLaUltima = long.MinValue / 2;

    /// <summary>Monta la sintonia.</summary>
    /// <param name="control">El control del equipo (el mismo del resto del programa).</param>
    /// <param name="conectado">Hay equipo conectado.</param>
    /// <param name="transmitiendo">El equipo esta transmitiendo.</param>
    /// <param name="irAlSpot">Lo mismo que hace el doble clic de la lista de spots.</param>
    /// <param name="milisegundos">Reloj; nulo, el del sistema (para pruebas).</param>
    public SintoniaDelAnalizador(
        IControlEquipo control,
        Func<bool> conectado,
        Func<bool> transmitiendo,
        Func<FilaDeSpot, Task> irAlSpot,
        Func<long>? milisegundos = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(conectado);
        ArgumentNullException.ThrowIfNull(transmitiendo);
        ArgumentNullException.ThrowIfNull(irAlSpot);
        _control = control;
        _conectado = conectado;
        _transmitiendo = transmitiendo;
        _irAlSpot = irAlSpot;
        _milisegundos = milisegundos ?? (() => Environment.TickCount64);
    }

    /// <summary>Ultima frecuencia pedida, o nula si no se ha pedido ninguna.</summary>
    public long? UltimaPedidaHz { get; private set; }

    /// <summary>Se le pueden mandar frecuencias: equipo conectado y sin transmitir.</summary>
    public bool SePuede => _conectado() && !_transmitiendo();

    /// <summary>Lleva el VFO activo a una frecuencia.</summary>
    /// <param name="hz">Hercios, ya ajustados al paso.</param>
    /// <returns>Cierto si se ha pedido; falso si no se podia (transmitiendo o sin equipo).</returns>
    public async Task<bool> IrAAsync(long hz)
    {
        if (hz <= 0 || !SePuede) return false;

        UltimaPedidaHz = hz;
        _momentoDeLaUltima = _milisegundos();
        _pendiente = hz;
        if (_ocupado) return true;

        _ocupado = true;
        try
        {
            while (_pendiente is { } siguiente)
            {
                _pendiente = null;

                // Puede haber empezado a transmitir mientras se mandaba la anterior.
                if (!SePuede) break;
                await _control.PonerFrecuenciaAsync(Frecuencia.DesdeHercios(siguiente)).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido sintonizar desde el analizador.");
            return false;
        }
        finally
        {
            _ocupado = false;
            _pendiente = null;
        }

        return true;
    }

    /// <summary>Mueve el VFO activo unas muescas (la rueda del raton).</summary>
    /// <param name="muescas">Positivas suben.</param>
    /// <param name="pasoHz">Hercios por muesca.</param>
    /// <param name="vfoHz">Donde esta el VFO segun la radio.</param>
    /// <returns>Cierto si se ha pedido.</returns>
    public Task<bool> MoverAsync(int muescas, long pasoHz, long vfoHz)
    {
        if (muescas == 0 || pasoHz <= 0) return Task.FromResult(false);
        var reciente = UltimaPedidaHz is not null && _milisegundos() - _momentoDeLaUltima < MemoriaDeLaRuedaMs;
        var desde = reciente ? UltimaPedidaHz!.Value : vfoHz;
        if (desde <= 0) return Task.FromResult(false);
        return IrAAsync(EscalaDelEspectro.AjustarAlPaso(desde + ((double)muescas * pasoHz), pasoHz));
    }

    /// <summary>Va a un spot como el doble clic de la lista de spots.</summary>
    /// <param name="fila">El spot.</param>
    /// <returns>Cierto si se ha ido; falso si se estaba transmitiendo.</returns>
    public async Task<bool> IrAlSpotAsync(FilaDeSpot fila)
    {
        ArgumentNullException.ThrowIfNull(fila);
        if (_transmitiendo()) return false;
        UltimaPedidaHz = fila.Spot.Frecuencia.Hercios;
        _momentoDeLaUltima = _milisegundos();
        await _irAlSpot(fila).ConfigureAwait(true);
        return true;
    }
}
