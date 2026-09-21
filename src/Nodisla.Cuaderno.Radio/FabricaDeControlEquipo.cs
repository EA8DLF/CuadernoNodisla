using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Control.OmniRig;
using Nodisla.Cuaderno.Radio.Control.Rigctld;

namespace Nodisla.Cuaderno.Radio;

/// <summary>Crea el control del equipo que toque segun los ajustes.</summary>
[SupportedOSPlatform("windows")]
public static class FabricaDeControlEquipo
{
    /// <summary>
    /// Crea el control de la via indicada en los ajustes.
    /// </summary>
    /// <remarks>
    /// Si se pide el CAT nativo del FT-710 sin decir el puerto, hace falta buscarlo, y buscar
    /// lleva tiempo: para eso esta <see cref="CrearAsync"/>.
    /// </remarks>
    /// <param name="opciones">Ajustes de radio.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    /// <returns>El control del equipo, listo para conectar.</returns>
    public static IControlEquipo Crear(OpcionesDeRadio? opciones = null, ILogger? registro = null)
    {
        var ajustes = opciones ?? new OpcionesDeRadio();

        if (ajustes.Via == ViaDeControl.CatNativo && !string.IsNullOrWhiteSpace(ajustes.Ft710.Puerto))
        {
            var canal = new CanalSerieCat(
                ajustes.Ft710.Puerto,
                ajustes.Ft710.Baudios,
                ajustes.Ft710.EsperaDeOrden,
                registro);
            return new ControlFt710(canal, ajustes.Ft710, registro);
        }

        return ajustes.Via switch
        {
            ViaDeControl.Rigctld => new ControlRigctld(ajustes.Rigctld, registro),
            ViaDeControl.OmniRig => new ControlOmniRig(null, ajustes.OmniRig, registro),
            _ => new ControlNulo(registro),
        };
    }

    /// <summary>
    /// Crea el control y, si hace falta, busca el FT-710 por los puertos serie.
    /// </summary>
    /// <param name="opciones">Ajustes de radio.</param>
    /// <param name="registro">Donde anotar lo que pasa.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>
    /// El control del equipo. Si se pidio el FT-710 y no aparece por ningun puerto, se devuelve
    /// el control sin equipo para que el operador pueda seguir apuntando a mano.
    /// </returns>
    public static async Task<IControlEquipo> CrearAsync(
        OpcionesDeRadio? opciones = null,
        ILogger? registro = null,
        CancellationToken ct = default)
    {
        var ajustes = opciones ?? new OpcionesDeRadio();

        if (ajustes.Via != ViaDeControl.CatNativo || !string.IsNullOrWhiteSpace(ajustes.Ft710.Puerto))
        {
            return Crear(ajustes, registro);
        }

        var equipos = await AutodeteccionFt710.BuscarAsync(registro, ct).ConfigureAwait(false);
        var ft710 = equipos.FirstOrDefault(equipo => equipo.EsFt710);
        if (ft710 is null)
        {
            (registro ?? NullLogger.Instance).LogWarning("No se ha encontrado ningún FT-710 en los puertos serie; se opera sin equipo.");
            return new ControlNulo(registro);
        }

        var canal = new CanalSerieCat(ft710.Puerto, ft710.Baudios, ajustes.Ft710.EsperaDeOrden, registro);
        return new ControlFt710(canal, ajustes.Ft710, registro);
    }
}
