using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;
using Nodisla.Cuaderno.Radio.Modelos;

namespace Nodisla.Cuaderno.Radio.Control.Yaesu;

/// <summary>
/// CAT nuevo de Yaesu (ASCII con <c>;</c>): FT-710 y el resto de la familia. Todos con el mismo
/// control (<see cref="ControlFt710"/>) y su <see cref="PerfilYaesu"/>.
/// </summary>
public sealed class ProveedorYaesuAscii : IProveedorDeModelos
{
    /// <inheritdoc />
    public ProtocoloCat Protocolo => ProtocoloCat.YaesuAscii;

    /// <inheritdoc />
    public int OrdenDeDeteccion => 0;

    /// <inheritdoc />
    public IReadOnlyList<ModeloDeEquipo> Modelos { get; } = PerfilesYaesu.Todos.Select(p => p.Modelo).ToList();

    /// <inheritdoc />
    public IControlEquipo Crear(ModeloDeEquipo modelo, string puerto, int baudios, OpcionesDeRadio opciones, ILogger? registro)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        var perfil = PerfilesYaesu.De(modelo)
            ?? throw new ArgumentException($"{modelo} no es un Yaesu de CAT nuevo.", nameof(modelo));
        var ajustes = opciones.Cat;
        var canal = new CanalSerieCat(puerto, baudios, ajustes.EsperaDeOrden, registro, ajustes.ViaDePtt);
        return new ControlFt710(canal, ajustes, registro, perfil);
    }

    /// <inheritdoc />
    /// <remarks>Solo <c>ID;</c>, que es una consulta.</remarks>
    public async Task<EquipoIdentificado?> IdentificarAsync(
        string puerto,
        IReadOnlyList<int> velocidades,
        OpcionesDeRadio opciones,
        ILogger? registro,
        CancellationToken ct)
    {
        var aProbar = velocidades.Count > 0 ? velocidades : AutodeteccionFt710.Velocidades;
        foreach (var baudios in aProbar)
        {
            ct.ThrowIfCancellationRequested();
            var encontrado = await AutodeteccionFt710.ComprobarAsync(puerto, baudios, registro, ct).ConfigureAwait(false);
            if (encontrado is not null)
            {
                return new EquipoIdentificado(
                    encontrado.Puerto,
                    encontrado.Baudios,
                    ProtocoloCat.YaesuAscii,
                    encontrado.Identificador,
                    PerfilesYaesu.PorIdentificador(encontrado.Identificador)?.Modelo);
            }
        }

        return null;
    }
}
