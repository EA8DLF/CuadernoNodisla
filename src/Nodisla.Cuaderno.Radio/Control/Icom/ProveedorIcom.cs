using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Modelos;

namespace Nodisla.Cuaderno.Radio.Control.Icom;

/// <summary>
/// Los ICOM del catalogo: los identifica por CI-V (<c>19 00</c>) y crea su control.
/// </summary>
/// <remarks>
/// Lo encuentra solo <see cref="CatalogoDeModelos"/> por reflexion. Todos los modelos van con
/// <see cref="ModeloDeEquipo.ProbadoConRadio"/> en falso: programados segun el manual.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class ProveedorIcom : IProveedorDeModelos
{
    /// <summary>Lo que se espera la respuesta a <c>19 00</c> al buscar.</summary>
    public static TimeSpan EsperaDeIdentificacion { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <inheritdoc />
    public ProtocoloCat Protocolo => ProtocoloCat.IcomCiv;

    /// <inheritdoc />
    public int OrdenDeDeteccion => 10;

    /// <inheritdoc />
    public IReadOnlyList<ModeloDeEquipo> Modelos { get; } = ModelosIcom.Todos.Select(p => p.Modelo).ToList();

    /// <inheritdoc />
    public IControlEquipo Crear(ModeloDeEquipo modelo, string puerto, int baudios, OpcionesDeRadio opciones, ILogger? registro)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        ArgumentNullException.ThrowIfNull(opciones);
        var perfil = ModelosIcom.De(modelo)
                     ?? throw new ArgumentException($"{modelo.NombreCompleto} no es un ICOM de este proveedor.", nameof(modelo));
        var direccion = opciones.DireccionCiv ?? perfil.Direccion;
        var cat = opciones.Cat;
        var canal = new CanalSerieCiv(puerto, baudios, direccion, cat.EsperaDeOrden, registro, cat.ViaDePtt);
        return new ControlIcom(canal, perfil, cat, registro, baudios);
    }

    /// <inheritdoc />
    public async Task<EquipoIdentificado?> IdentificarAsync(
        string puerto,
        IReadOnlyList<int> velocidades,
        OpcionesDeRadio opciones,
        ILogger? registro,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        var aProbar = velocidades is { Count: > 0 }
            ? velocidades
            : ModelosIcom.Todos.SelectMany(p => p.Modelo.Velocidades).Distinct().ToList();

        foreach (var baudios in aProbar)
        {
            ct.ThrowIfCancellationRequested();
            var hallado = await IdentificarAsync(
                direccion => new CanalSerieCiv(puerto, baudios, direccion, EsperaDeIdentificacion, registro),
                opciones.DireccionCiv,
                registro,
                ct).ConfigureAwait(false);
            if (hallado is { } d)
            {
                return new EquipoIdentificado(puerto, baudios, ProtocoloCat.IcomCiv, d.ToString("X2"), ModeloPorDireccion(d, opciones));
            }
        }

        return null;
    }

    /// <summary>
    /// Pregunta <c>19 00</c> a la direccion de difusion y, si nadie contesta, a la de cada modelo.
    /// Solo consultas; cierra el canal al acabar.
    /// </summary>
    /// <param name="crearCanal">Crea un canal para una direccion.</param>
    /// <param name="direccionDelOperador">Direccion puesta a mano en los ajustes, si la hay.</param>
    /// <param name="registro">Donde anotar.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La direccion CI-V que contesto, o nulo.</returns>
    public static async Task<byte?> IdentificarAsync(
        Func<byte, CanalCiv> crearCanal,
        byte? direccionDelOperador,
        ILogger? registro,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(crearCanal);
        registro ??= NullLogger.Instance;
        List<byte> direcciones = [TramaCiv.Difusion];
        if (direccionDelOperador is { } propia) direcciones.Add(propia);
        direcciones.AddRange(ModelosIcom.Todos.Select(p => p.Direccion));

        foreach (var direccion in direcciones.Distinct())
        {
            await using var canal = crearCanal(direccion);
            try
            {
                await canal.AbrirAsync(ct).ConfigureAwait(false);
                var r = await canal.PreguntarAsync([0x19, 0x00], ct).ConfigureAwait(false);
                if (r is not null && r.EmpiezaPor([0x19, 0x00]))
                {
                    var dicha = r.DatosTras(2) is [var d, ..] ? d : r.Origen;
                    registro.LogInformation("ICOM en {Canal}: direccion CI-V {Direccion:X2}.", canal.Descripcion, dicha);
                    return dicha;
                }
            }
            catch (CanalNoDisponibleException)
            {
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                registro.LogDebug(ex, "Fallo al buscar un ICOM en {Canal}.", canal.Descripcion);
            }
            finally
            {
                canal.Cerrar();
            }
        }

        return null;
    }

    private static ModeloDeEquipo? ModeloPorDireccion(byte direccion, OpcionesDeRadio opciones)
    {
        var porFabrica = CatalogoDeModelos.PorDireccionCiv(direccion);
        if (porFabrica is not null) return porFabrica;

        // Direccion cambiada por el operador: vale el modelo que haya elegido en los ajustes.
        return opciones.DireccionCiv == direccion && CatalogoDeModelos.Buscar(opciones.Modelo) is { Protocolo: ProtocoloCat.IcomCiv } elegido
            ? elegido
            : null;
    }
}
