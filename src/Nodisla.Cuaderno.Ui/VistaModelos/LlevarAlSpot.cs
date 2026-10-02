using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Serilog;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>
/// Ir a un spot: el doble clic de la lista del cluster y del bandmap, el boton «Ir a este spot»
/// y el clic en un rotulo del analizador pasan TODOS por aqui.
/// </summary>
/// <remarks>
/// <para>
/// Lleva el equipo a la frecuencia y al modo del spot (en el VFO activo, que es lo que hace
/// <see cref="IControlEquipo.PonerFrecuenciaAsync"/>) y prepara el contacto nuevo.
/// </para>
/// <para>
/// <b>Transmitiendo no se hace nada</b>: cambiar de frecuencia con la portadora puesta es
/// salirse de frecuencia en el aire. <b>Sin equipo</b> se rellena el contacto nuevo y el mapa,
/// como siempre (para quien apunta a mano sin CAT), sin mandar nada a la radio. En los dos casos
/// se dice en <paramref name="avisar"/>, que en la ventana es el aviso de la barra del equipo,
/// a la vista en Operar y en Digital.
/// </para>
/// </remarks>
/// <param name="control">El control del equipo.</param>
/// <param name="conectado">Hay equipo conectado.</param>
/// <param name="transmitiendo">El equipo esta transmitiendo.</param>
/// <param name="preparar">Pone el indicativo, la frecuencia y el modo en el contacto nuevo y traza el mapa.</param>
/// <param name="avisar">Dice en pantalla por que no se ha ido.</param>
public sealed class LlevarAlSpot(
    IControlEquipo control,
    Func<bool> conectado,
    Func<bool> transmitiendo,
    Action<FilaDeSpot> preparar,
    Action<string> avisar)
{
    /// <summary>Va al spot, si se puede.</summary>
    /// <param name="fila">El spot.</param>
    /// <returns>Cierto si se ha llevado la radio; falso si no (y se ha dicho por que).</returns>
    public async Task<bool> IrAsync(FilaDeSpot fila)
    {
        ArgumentNullException.ThrowIfNull(fila);

        if (transmitiendo())
        {
            var motivo = Textos.T("Cabina.Spot.NoTransmitiendo");
            avisar(motivo);
            Log.Information("No se va al spot de {Indicativo}: {Motivo}", fila.Indicativo, motivo);
            return false;
        }

        preparar(fila);

        // Sin equipo se apunta a mano: el contacto y el mapa si, la radio no.
        if (!conectado())
        {
            avisar(Textos.T("Cabina.Spot.SinEquipo"));
            return false;
        }

        try
        {
            await control.PonerFrecuenciaAsync(fila.Spot.Frecuencia).ConfigureAwait(true);

            if (Dominio.Valores.Modo.TryParse(fila.Modo, null, out var modo))
            {
                await control.PonerModoAsync(modo).ConfigureAwait(true);

                // Cambiar de modo corre el dial en el FT-710 (visto en la radio el 29-09-2026: un
                // spot de FT8 en 14.074.000 quedaba en 14.074.700 al pasar a DATA-U). Se vuelve a
                // poner la frecuencia del spot, que es la que manda.
                await control.PonerFrecuenciaAsync(fila.Spot.Frecuencia).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se ha podido llevar el equipo al spot de {Indicativo}.", fila.Indicativo);
        }

        return true;
    }
}
