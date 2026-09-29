using System.Collections.Concurrent;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Que tiene de nuevo un indicativo que se acaba de oir.</summary>
/// <param name="IndicativoNuevo">Nunca se ha trabajado ese indicativo.</param>
/// <param name="EntidadNueva">Su entidad DXCC no esta en el cuaderno.</param>
/// <param name="NuevaEnBanda">La entidad esta, pero no en esta banda.</param>
/// <param name="NuevaEnModo">La entidad esta, pero no en este modo.</param>
/// <param name="CuadriculaNueva">Su cuadricula de cuatro no esta en el cuaderno.</param>
/// <param name="Entidad">Nombre de la entidad, para decirlo.</param>
/// <param name="QueAporta">Lo que dicen los diplomas, en frases.</param>
public sealed record NovedadDelIndicativo(
    bool IndicativoNuevo,
    bool EntidadNueva,
    bool NuevaEnBanda,
    bool NuevaEnModo,
    bool CuadriculaNueva,
    string Entidad,
    IReadOnlyList<string> QueAporta)
{
    /// <summary>Cuando no se sabe nada: ni nuevo ni viejo.</summary>
    public static NovedadDelIndicativo Desconocida { get; } = new(false, false, false, false, false, string.Empty, []);

    /// <summary>Ya esta en el cuaderno.</summary>
    public bool TrabajadoAntes => !IndicativoNuevo;

    /// <summary>Hay algo que merezca resaltar.</summary>
    public bool HayAlgo => IndicativoNuevo || EntidadNueva || NuevaEnBanda || NuevaEnModo || CuadriculaNueva;
}

/// <summary>
/// Dice, para cada indicativo que se oye, si es nuevo y en que: es lo que pinta los colores de
/// la lista.
/// </summary>
/// <remarks>
/// <para>
/// Se apoya en lo que ya calcula el resto del programa: <see cref="ConsultarTrabajadoAntes"/>
/// para el indicativo, <see cref="RetratoDelIndicativo"/> para la entidad en la banda y el modo,
/// y <see cref="IDiplomas.QueAportaAsync"/> para las frases. Las cuadriculas trabajadas se
/// cargan una vez del cuaderno y se guardan en memoria.
/// </para>
/// <para>
/// Cada indicativo se evalua <b>una vez por banda y modo</b> y se guarda: en una apertura de 20
/// metros se oyen cientos de estaciones y no se puede ir a la base doce veces por cada una en
/// cada ventana. Cuando entra un contacto nuevo en el cuaderno, se vacia la memoria.
/// </para>
/// </remarks>
public sealed class EvaluadorDeNovedad
{
    private readonly ConsultarTrabajadoAntes _trabajadoAntes;
    private readonly RetratoDelIndicativo? _retrato;
    private readonly IResolutorDxcc? _dxcc;
    private readonly IRepositorioQso? _cuaderno;
    private readonly IDiplomas? _diplomas;
    private readonly ConcurrentDictionary<string, NovedadDelIndicativo> _memoria = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _cargaDeCuadriculas = new(1, 1);
    private HashSet<string>? _cuadriculas;

    /// <summary>Monta el evaluador. Todo menos el «trabajado antes» puede faltar.</summary>
    public EvaluadorDeNovedad(
        ConsultarTrabajadoAntes trabajadoAntes,
        RetratoDelIndicativo? retrato = null,
        IResolutorDxcc? dxcc = null,
        IRepositorioQso? cuaderno = null,
        IDiplomas? diplomas = null)
    {
        _trabajadoAntes = trabajadoAntes ?? throw new ArgumentNullException(nameof(trabajadoAntes));
        _retrato = retrato;
        _dxcc = dxcc;
        _cuaderno = cuaderno;
        _diplomas = diplomas;
    }

    /// <summary>Perfil de estacion con el que se cuenta, o nulo para todos.</summary>
    public long? EstacionId { get; set; }

    /// <summary>El cuaderno ha cambiado: lo que se sabia ya no vale.</summary>
    public void Olvidar()
    {
        _memoria.Clear();
        _cuadriculas = null;
    }

    /// <summary>Evalua un indicativo en una banda y un modo. No lanza: ante un fallo, «desconocida».</summary>
    public async Task<NovedadDelIndicativo> EvaluarAsync(
        Indicativo indicativo, Locator locator, Banda banda, Modo modo, CancellationToken ct = default)
    {
        if (indicativo.EsVacio) return NovedadDelIndicativo.Desconocida;

        var clave = $"{indicativo.Valor}|{banda.Nombre}|{modo.NombreUsual}|{Cuatro(locator)}";
        if (_memoria.TryGetValue(clave, out var sabida)) return sabida;

        try
        {
            var novedad = await CalcularAsync(indicativo, locator, banda, modo, ct).ConfigureAwait(false);
            _memoria[clave] = novedad;
            return novedad;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return NovedadDelIndicativo.Desconocida;
        }
    }

    private async Task<NovedadDelIndicativo> CalcularAsync(
        Indicativo indicativo, Locator locator, Banda banda, Modo modo, CancellationToken ct)
    {
        var visto = await _trabajadoAntes.EjecutarAsync(indicativo, ct).ConfigureAwait(false);
        var indicativoNuevo = !visto.TrabajadoAntes;

        var entidadNueva = false;
        var nuevaEnBanda = false;
        var nuevaEnModo = false;
        var entidad = string.Empty;

        if (_retrato is not null && _dxcc is not null)
        {
            var resuelto = _dxcc.Resolver(indicativo, DateOnly.FromDateTime(DateTime.UtcNow));
            var numero = resuelto.Entidad?.Numero ?? 0;
            entidad = resuelto.Entidad?.NombreParaMostrar ?? string.Empty;

            if (numero > 0)
            {
                var retrato = await _retrato.ArmarAsync(indicativo, numero, banda, modo, EstacionId, ct).ConfigureAwait(false);

                entidadNueva = NoTrabajado(retrato, EjeDeNovedad.Pais);
                nuevaEnBanda = !entidadNueva && NoTrabajado(retrato, EjeDeNovedad.Banda);
                nuevaEnModo = !entidadNueva && NoTrabajado(retrato, EjeDeNovedad.Modo);
            }
        }

        var cuadriculaNueva = false;
        var cuatro = Cuatro(locator);
        if (cuatro.Length == 4)
        {
            var trabajadas = await CuadriculasAsync(ct).ConfigureAwait(false);
            cuadriculaNueva = trabajadas is not null && !trabajadas.Contains(cuatro);
        }

        IReadOnlyList<string> aporta = [];
        if (_diplomas is not null)
        {
            aporta = await _diplomas.QueAportaAsync(indicativo, banda, modo, ct).ConfigureAwait(false);
        }

        return new NovedadDelIndicativo(indicativoNuevo, entidadNueva, nuevaEnBanda, nuevaEnModo, cuadriculaNueva, entidad, aporta);
    }

    private static bool NoTrabajado(Retrato retrato, EjeDeNovedad eje)
    {
        var casilla = retrato.Novedad.FirstOrDefault(c => c.Eje == eje);
        return casilla is { Aplica: true, Trabajado: false };
    }

    private async Task<HashSet<string>?> CuadriculasAsync(CancellationToken ct)
    {
        if (_cuadriculas is not null) return _cuadriculas;
        if (_cuaderno is null) return null;

        await _cargaDeCuadriculas.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cuadriculas is not null) return _cuadriculas;

            var conjunto = new HashSet<string>(StringComparer.Ordinal);
            const int pagina = 500;
            var desde = 0;
            while (true)
            {
                var trozo = await _cuaderno.BuscarAsync(new CriterioQso { EstacionId = EstacionId }, desde, pagina, ct).ConfigureAwait(false);
                foreach (var qso in trozo.Elementos)
                {
                    var g = Cuatro(qso.Gridsquare);
                    if (g.Length == 4) conjunto.Add(g);
                }

                desde += trozo.Elementos.Count;
                if (trozo.Elementos.Count < pagina || desde >= trozo.TotalFiltrado) break;
            }

            _cuadriculas = conjunto;
            return conjunto;
        }
        finally
        {
            _cargaDeCuadriculas.Release();
        }
    }

    private static string Cuatro(Locator locator) =>
        locator.EsVacio || locator.Valor.Length < 4 ? string.Empty : locator.Valor[..4].ToUpperInvariant();
}
