using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Modelos;

namespace Nodisla.Cuaderno.Radio.Control.Yaesu;

/// <summary>
/// CAT antiguo de Yaesu (bloques de 5 bytes): FT-817/817ND, FT-818, FT-857/857D, FT-897/897D.
/// </summary>
/// <remarks>
/// Este CAT no tiene orden de identificacion: la deteccion automatica no lo prueba y el modelo
/// lo elige el operador. <see cref="IdentificarAsync"/> solo dice que al otro lado hay «un Yaesu
/// de CAT antiguo» si contesta a la lectura de frecuencia y modo (orden 03, una consulta).
/// Fuentes: manuales de operacion FT-817ND (E13771011), FT-818ND (E13772004) y FT-857D
/// (EH007M108) de yaesu.com; el FT-897D usa el mismo juego de ordenes que el FT-857D.
/// </remarks>
public sealed class ProveedorYaesuBinario : IProveedorDeModelos
{
    private const string Nota =
        "Programado según el manual de operación (CAT System Programming), sin probar con la radio. Confirmar con la radio: "
        + "velocidad del menú CAT RATE, 2 bits de parada, que el medidor S (E7) sale en los 4 bits bajos y que DIG/PKT se "
        + "leen como 0A/0C. Split, clarificador y bloqueo no se pueden leer por CAT.";

    private static CapacidadesDelModelo Capacidades() =>
        new(DosVfos: false, DosReceptores: false, Analizador: false, Sintonizador: false, Memorias: 0,
            Mandos: true, Encendido: false, HerciosMinimo: 100_000, HerciosMaximo: 470_000_000);

    private static ModeloDeEquipo Modelo(string clave, string nombre) =>
        new($"yaesu-{clave}", Fabricante.Yaesu, nombre, ProtocoloCat.YaesuBinario, null, null,
            [4800, 9600, 38400], Capacidades(), null, false, Nota);

    /// <summary>FT-817 / FT-817ND.</summary>
    public static ModeloDeEquipo Ft817 { get; } = Modelo("ft817", "FT-817");

    /// <summary>FT-818 / FT-818ND.</summary>
    public static ModeloDeEquipo Ft818 { get; } = Modelo("ft818", "FT-818");

    /// <summary>FT-857 / FT-857D.</summary>
    public static ModeloDeEquipo Ft857 { get; } = Modelo("ft857", "FT-857");

    /// <summary>FT-897 / FT-897D.</summary>
    public static ModeloDeEquipo Ft897 { get; } = Modelo("ft897", "FT-897");

    /// <inheritdoc />
    public ProtocoloCat Protocolo => ProtocoloCat.YaesuBinario;

    /// <inheritdoc />
    public int OrdenDeDeteccion => 100;

    /// <inheritdoc />
    public IReadOnlyList<ModeloDeEquipo> Modelos { get; } = [Ft817, Ft818, Ft857, Ft897];

    /// <inheritdoc />
    public IControlEquipo Crear(ModeloDeEquipo modelo, string puerto, int baudios, OpcionesDeRadio opciones, ILogger? registro)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        var canal = new CanalSerieBinario(puerto, baudios, opciones.Cat.EsperaDeOrden, registro, opciones.Cat.ViaDePtt);
        return new ControlYaesuBinario(canal, modelo, opciones.Cat, registro);
    }

    /// <inheritdoc />
    public async Task<EquipoIdentificado?> IdentificarAsync(
        string puerto,
        IReadOnlyList<int> velocidades,
        OpcionesDeRadio opciones,
        ILogger? registro,
        CancellationToken ct)
    {
        foreach (var baudios in velocidades.Count > 0 ? velocidades : [4800, 9600, 38400])
        {
            await using var canal = new CanalSerieBinario(puerto, baudios, TimeSpan.FromMilliseconds(400), registro);
            try
            {
                await canal.AbrirAsync(ct).ConfigureAwait(false);
                var leido = await canal.PreguntarAsync(ControlYaesuBinario.Bloque(ControlYaesuBinario.Orden.LeerFrecuenciaYModo), 5, ct).ConfigureAwait(false);
                if (leido is not null && ControlYaesuBinario.DesdeBcd(leido) is > 0)
                {
                    return new EquipoIdentificado(puerto, baudios, ProtocoloCat.YaesuBinario, "CAT antiguo", null);
                }
            }
            catch (CanalNoDisponibleException)
            {
                return null;
            }
        }

        return null;
    }
}
