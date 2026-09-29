using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Consultas de informe resueltas en memoria sobre el cuaderno de demostracion.
/// </summary>
/// <remarks>
/// La de verdad las resuelve en una consulta a la base de datos; esta recorre la lista. Con
/// veinte mil contactos de mentira sobra, y permite que el panel de cluster resalte lo que
/// seria nuevo desde el primer arranque, que es lo que hacia falta comprobar.
/// </remarks>
public sealed class ConsultasDeInformeEnMemoria : IConsultasDeInforme
{
    private readonly IReadOnlyList<Qso> _cuaderno;
    private readonly IResolutorDxcc _dxcc;

    private readonly Lazy<HashSet<int>> _entidades;
    private readonly Lazy<HashSet<(int Dxcc, string Banda)>> _entidadesPorBanda;
    private readonly Lazy<HashSet<(int Dxcc, string Banda, string Modo)>> _huecos;

    /// <summary>Monta las consultas sobre una lista de contactos.</summary>
    /// <param name="cuaderno">Contactos sobre los que se responde.</param>
    /// <param name="dxcc">Resolutor de entidades para los contactos que no la traen puesta.</param>
    public ConsultasDeInformeEnMemoria(IReadOnlyList<Qso> cuaderno, IResolutorDxcc dxcc)
    {
        ArgumentNullException.ThrowIfNull(cuaderno);
        ArgumentNullException.ThrowIfNull(dxcc);

        _cuaderno = cuaderno;
        _dxcc = dxcc;

        _entidades = new Lazy<HashSet<int>>(() => [.. Recorrer().Select(t => t.Dxcc)]);
        _entidadesPorBanda = new Lazy<HashSet<(int, string)>>(
            () => [.. Recorrer().Select(t => (t.Dxcc, t.Banda))]);
        _huecos = new Lazy<HashSet<(int, string, string)>>(() => [.. Recorrer()]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ResumenPorBanda>> PorBandaAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ResumenPorBanda> salida =
        [
            .. _cuaderno
                .Where(q => !q.Band.EsVacia)
                .GroupBy(q => q.Band.Nombre, StringComparer.Ordinal)
                .Select(g => new ResumenPorBanda(g.Key, g.Count(), g.Count(EstaConfirmado)))
                .OrderByDescending(r => r.Contactos),
        ];

        return Task.FromResult(salida);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ResumenPorModo>> PorModoAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ResumenPorModo> salida =
        [
            .. _cuaderno
                .Where(q => !q.Mode.EsVacio)
                .GroupBy(q => q.Mode.Principal, StringComparer.Ordinal)
                .Select(g => new ResumenPorModo(g.Key, g.Count(), g.Count(EstaConfirmado)))
                .OrderByDescending(r => r.Contactos),
        ];

        return Task.FromResult(salida);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CasillaDxcc>> MatrizDxccPorBandaAsync(
        MedioDeConfirmacion medio = MedioDeConfirmacion.Lotw,
        CancellationToken ct = default)
    {
        IReadOnlyList<CasillaDxcc> salida =
        [
            .. _cuaderno
                .Select(q => (Qso: q, Dxcc: EntidadDe(q)))
                .Where(t => t.Dxcc > 0 && !t.Qso.Band.EsVacia)
                .GroupBy(t => (t.Dxcc, t.Qso.Band.Nombre))
                .Select(g => new CasillaDxcc(
                    g.Key.Dxcc,
                    g.Key.Nombre,
                    g.Count(),
                    g.Count(t => EstaConfirmadoPor(t.Qso, medio))))
                .OrderBy(c => c.Dxcc).ThenBy(c => c.Band, StringComparer.Ordinal),
        ];

        return Task.FromResult(salida);
    }

    /// <inheritdoc />
    public Task<Novedad> ConsultarNovedadAsync(
        int dxcc,
        Banda banda,
        Modo modo,
        CancellationToken ct = default)
    {
        var nombreDeBanda = banda.Nombre ?? string.Empty;
        var nombreDeModo = modo.EsVacio ? string.Empty : modo.Principal;

        return Task.FromResult(new Novedad(
            Visto: _entidades.Value.Contains(dxcc),
            VistoEnBanda: _entidadesPorBanda.Value.Contains((dxcc, nombreDeBanda)),
            VistoEnHueco: _huecos.Value.Contains((dxcc, nombreDeBanda, nombreDeModo))));
    }

    /// <inheritdoc />
    public Task<TotalesDelCuaderno> TotalesAsync(CancellationToken ct = default)
    {
        if (_cuaderno.Count == 0)
        {
            return Task.FromResult(new TotalesDelCuaderno(0, 0, 0, null, null));
        }

        return Task.FromResult(new TotalesDelCuaderno(
            _cuaderno.Count,
            _cuaderno.Select(q => q.Call.Valor).Distinct(StringComparer.Ordinal).Count(),
            _entidades.Value.Count,
            _cuaderno.Min(q => q.InicioUtc),
            _cuaderno.Max(q => q.InicioUtc)));
    }

    private IEnumerable<(int Dxcc, string Banda, string Modo)> Recorrer()
    {
        foreach (var qso in _cuaderno)
        {
            var dxcc = EntidadDe(qso);
            if (dxcc <= 0) continue;

            yield return (dxcc, qso.Band.Nombre ?? string.Empty, qso.Mode.EsVacio ? string.Empty : qso.Mode.Principal);
        }
    }

    private int EntidadDe(Qso qso) => qso.Dxcc > 0
        ? qso.Dxcc
        : _dxcc.Resolver(qso.Call, DateOnly.FromDateTime(qso.InicioUtc.UtcDateTime)).Entidad?.Numero ?? 0;

    private static bool EstaConfirmado(Qso qso) =>
        qso.Confirmaciones.Any(c => c.Recibido == EstadoDeConfirmacion.Confirmado);

    private static bool EstaConfirmadoPor(Qso qso, MedioDeConfirmacion medio) =>
        qso.Confirmaciones.Any(c => c.Medio == medio && c.Recibido == EstadoDeConfirmacion.Confirmado);
}
