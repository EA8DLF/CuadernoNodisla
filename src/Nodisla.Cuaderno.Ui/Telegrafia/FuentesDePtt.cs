using Microsoft.Extensions.DependencyInjection;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Telegrafia;

/// <summary>
/// Da de alta en el vigilante del PTT las fuentes que pueden pedir transmitir: el MOX del
/// frontal, el modem digital, la fonia y la telegrafia (macros, texto y secuencia).
/// </summary>
/// <remarks>
/// Tras un corte de seguridad (ROE, tope, latido) el vigilante no deja volver a transmitir
/// hasta que todas digan que estan sueltas: el MOX abajo, el «Tx habilitado» del modem quitado,
/// la fonia sin PTT y la telegrafia sin enviar ni automatico.
/// </remarks>
public static class FuentesDePtt
{
    private static readonly List<IDisposable> Altas = [];

    /// <summary>Da de alta las fuentes que haya en el contenedor.</summary>
    /// <param name="proveedor">El contenedor.</param>
    public static void Conectar(IServiceProvider proveedor)
    {
        ArgumentNullException.ThrowIfNull(proveedor);
        if (proveedor.GetService<IVigilantePtt>() is not { } vigilante) return;
        Conectar(
            vigilante,
            proveedor.GetService<VistaModeloEquipo>(),
            proveedor.GetService<VistaModeloModemPropio>(),
            proveedor.GetService<VistaModeloFonia>(),
            proveedor.GetService<VistaModeloTransmisionCw>(),
            proveedor.GetService<VistaModeloRtty>());
    }

    /// <summary>Da de alta las fuentes que se le den.</summary>
    /// <param name="vigilante">El vigilante.</param>
    /// <param name="equipo">El frontal (MOX).</param>
    /// <param name="modem">El modem propio.</param>
    /// <param name="fonia">La fonia.</param>
    /// <param name="telegrafia">La transmision de CW.</param>
    /// <param name="rtty">La transmision de RTTY.</param>
    /// <returns>Las altas, para darlas de baja (las pruebas).</returns>
    public static IReadOnlyList<IDisposable> Conectar(
        IVigilantePtt vigilante,
        VistaModeloEquipo? equipo,
        VistaModeloModemPropio? modem,
        VistaModeloFonia? fonia,
        VistaModeloTransmisionCw? telegrafia,
        VistaModeloRtty? rtty = null)
    {
        ArgumentNullException.ThrowIfNull(vigilante);
        var altas = new List<IDisposable>();
        if (equipo is not null) altas.Add(vigilante.RegistrarFuente("MOX", () => equipo.EnMox));
        if (modem is not null) altas.Add(vigilante.RegistrarFuente("Módem", () => modem.TxHabilitado));
        if (fonia is not null) altas.Add(vigilante.RegistrarFuente("Fonía", () => fonia.Transmitiendo));
        if (telegrafia is not null)
        {
            altas.Add(vigilante.RegistrarFuente("Telegrafía", () => telegrafia.Automatico || telegrafia.Emisor.Enviando));
        }

        if (rtty is not null) altas.Add(vigilante.RegistrarFuente("RTTY", () => rtty.Transmitiendo));

        lock (Altas) Altas.AddRange(altas);
        return altas;
    }
}
