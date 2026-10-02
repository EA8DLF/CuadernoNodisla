using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Cuaderno en memoria para poder arrancar la ventana mientras la capa de datos real no esta.
/// Cuando llegue, lo unico que cambia es el registro de servicios.
/// </summary>
public sealed class RepositorioQsoEnMemoria : IRepositorioQso
{
    private readonly object _cerrojo = new();
    private readonly List<Qso> _qsos = [];
    private readonly Dictionary<string, List<Qso>> _porIndicativo = new(StringComparer.OrdinalIgnoreCase);
    private long _siguienteId = 1;

    /// <summary>Crea el cuaderno con los contactos de demostracion que se le pasen.</summary>
    public RepositorioQsoEnMemoria(IEnumerable<Qso>? siembra = null)
    {
        if (siembra is null) return;
        foreach (var qso in siembra) Insertar(qso);
    }

    /// <inheritdoc />
    public Task<Qso?> ObtenerAsync(long id, CancellationToken ct = default) =>
        EnSegundoPlano(() => { lock (_cerrojo) { return _qsos.FirstOrDefault(q => q.Id == id); } }, ct);

    /// <inheritdoc />
    public Task<Qso?> ObtenerPorUuidAsync(Guid uuid, CancellationToken ct = default) =>
        EnSegundoPlano(() => { lock (_cerrojo) { return _qsos.FirstOrDefault(q => q.Uuid == uuid); } }, ct);

    /// <inheritdoc />
    public Task<Pagina<Qso>> BuscarAsync(CriterioQso criterio, int desplazamiento, int limite, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(criterio);
        return EnSegundoPlano(() =>
        {
            lock (_cerrojo)
            {
                var filtrados = Filtrar(criterio).ToList();
                filtrados.Sort(Comparador(criterio));
                var pagina = filtrados.Skip(Math.Max(0, desplazamiento)).Take(Math.Max(1, limite)).ToList();
                return new Pagina<Qso>(pagina, filtrados.Count, Math.Max(0, desplazamiento));
            }
        }, ct);
    }

    /// <inheritdoc />
    public Task<long> AnadirAsync(Qso qso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qso);
        return EnSegundoPlano(() => { lock (_cerrojo) { return Insertar(qso); } }, ct);
    }

    /// <inheritdoc />
    public Task<ResultadoDeLote> AnadirLoteAsync(
        IEnumerable<Qso> qsos,
        bool omitirDuplicados = true,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qsos);
        return EnSegundoPlano(() =>
        {
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            lock (_cerrojo)
            {
                var anadidos = 0;
                var omitidos = 0;

                foreach (var q in qsos)
                {
                    var duplicado = BuscarDuplicado(q);
                    if (duplicado is not null)
                    {
                        if (!omitirDuplicados)
                        {
                            throw new InvalidOperationException(
                                Textos.F("Dialogos.Simulado.QsoDuplicado", q.Call.Valor));
                        }
                        omitidos++;
                        continue;
                    }

                    Insertar(q);
                    anadidos++;
                }

                return new ResultadoDeLote(anadidos, omitidos, reloj.Elapsed);
            }
        }, ct);
    }

    /// <inheritdoc />
    public Task ActualizarAsync(Qso qso, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(qso);
        return EnSegundoPlano<object?>(() =>
        {
            lock (_cerrojo)
            {
                var i = _qsos.FindIndex(q => q.Id == qso.Id);
                if (i >= 0)
                {
                    QuitarDelIndice(_qsos[i]);
                    _qsos[i] = qso;
                    AnadirAlIndice(qso);
                }
                return null;
            }
        }, ct);
    }

    /// <inheritdoc />
    public Task EliminarAsync(long id, CancellationToken ct = default) =>
        EnSegundoPlano<object?>(() =>
        {
            lock (_cerrojo)
            {
                var i = _qsos.FindIndex(q => q.Id == id);
                if (i >= 0) { QuitarDelIndice(_qsos[i]); _qsos.RemoveAt(i); }
                return null;
            }
        }, ct);

    /// <inheritdoc />
    public Task<Qso?> BuscarDuplicadoAsync(Qso candidato, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidato);
        return EnSegundoPlano(() => { lock (_cerrojo) { return BuscarDuplicado(candidato); } }, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Qso>> TrabajadoAntesAsync(
        Indicativo indicativo,
        int maximo = 50,
        CancellationToken ct = default) =>
        EnSegundoPlano(() =>
        {
            lock (_cerrojo)
            {
                if (!_porIndicativo.TryGetValue(indicativo.Valor, out var lista))
                {
                    return (IReadOnlyList<Qso>)[];
                }

                // El contrato pide del mas reciente al mas antiguo, y con tope.
                IReadOnlyList<Qso> resultado = lista
                    .OrderByDescending(q => q.InicioUtc)
                    .ThenByDescending(q => q.Id)
                    .Take(Math.Max(1, maximo))
                    .ToList();
                return resultado;
            }
        }, ct);

    private Qso? BuscarDuplicado(Qso candidato)
    {
        if (!_porIndicativo.TryGetValue(candidato.Call.Valor, out var lista)) return null;
        var clave = candidato.ClaveNatural;
        return lista.FirstOrDefault(q => q.Id != candidato.Id && q.ClaveNatural == clave);
    }

    /// <inheritdoc />
    public Task<int> ContarAsync(CancellationToken ct = default) =>
        EnSegundoPlano(() => { lock (_cerrojo) { return _qsos.Count; } }, ct);

    private long Insertar(Qso qso)
    {
        qso.Id = _siguienteId++;
        _qsos.Add(qso);
        AnadirAlIndice(qso);
        return qso.Id;
    }

    private void AnadirAlIndice(Qso qso)
    {
        if (qso.Call.EsVacio) return;
        if (!_porIndicativo.TryGetValue(qso.Call.Valor, out var lista))
        {
            lista = [];
            _porIndicativo[qso.Call.Valor] = lista;
        }
        lista.Add(qso);
    }

    private void QuitarDelIndice(Qso qso)
    {
        if (!qso.Call.EsVacio && _porIndicativo.TryGetValue(qso.Call.Valor, out var lista)) lista.Remove(qso);
    }

    private IEnumerable<Qso> Filtrar(CriterioQso criterio)
    {
        IEnumerable<Qso> consulta = _qsos;

        if (!string.IsNullOrWhiteSpace(criterio.Texto))
        {
            var t = criterio.Texto.Trim();
            consulta = consulta.Where(q =>
                q.Call.Valor.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                Contiene(q.Name, t) || Contiene(q.Qth, t) || Contiene(q.Comentario, t) ||
                Contiene(q.Notas, t) || Contiene(q.Country, t) || Contiene(q.Gridsquare.Valor, t));
        }

        if (!string.IsNullOrWhiteSpace(criterio.Call))
        {
            var c = Indicativo.Normalizar(criterio.Call).TrimEnd(Comodin);
            consulta = consulta.Where(q => q.Call.Valor.StartsWith(c, StringComparison.OrdinalIgnoreCase));
        }

        if (criterio.Band is { } banda && !banda.EsVacia) consulta = consulta.Where(q => q.Band == banda);

        if (!string.IsNullOrWhiteSpace(criterio.Mode))
        {
            consulta = consulta.Where(q =>
                string.Equals(q.Mode.Principal, criterio.Mode, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(q.Mode.Submodo, criterio.Mode, StringComparison.OrdinalIgnoreCase));
        }

        if (criterio.Dxcc is { } dxcc) consulta = consulta.Where(q => q.Dxcc == dxcc);
        if (criterio.DesdeUtc is { } desde) consulta = consulta.Where(q => q.InicioUtc >= desde);
        if (criterio.HastaUtc is { } hasta) consulta = consulta.Where(q => q.InicioUtc <= hasta);
        if (criterio.EstacionId is { } estacion) consulta = consulta.Where(q => q.EstacionId == estacion);
        if (criterio.ConfirmadoPor is { } medio)
        {
            consulta = consulta.Where(q => q.Confirmaciones.Any(c => c.Medio == medio));
        }

        return consulta;
    }

    /// <summary>Caracter con el que el operador abrevia un indicativo al filtrar.</summary>
    private const char Comodin = '*';

    /// <summary>
    /// Orden determinista: el campo pedido y, como desempate, siempre el <c>Id</c> en el mismo
    /// sentido. Sin ese desempate, dos contactos del mismo segundo se repiten o se saltan al
    /// pasar de pagina, tal y como exige el contrato de <see cref="IRepositorioQso"/>.
    /// </summary>
    private static Comparison<Qso> Comparador(CriterioQso criterio)
    {
        Comparison<Qso> porCampo = criterio.OrdenarPor switch
        {
            CampoDeOrden.Indicativo => (a, b) => string.CompareOrdinal(a.Call.Valor, b.Call.Valor),
            CampoDeOrden.Banda => (a, b) => IndiceDeBanda(a.Band).CompareTo(IndiceDeBanda(b.Band)),
            CampoDeOrden.Modo => (a, b) => string.CompareOrdinal(a.Mode.NombreUsual, b.Mode.NombreUsual),
            CampoDeOrden.Frecuencia => (a, b) => a.Freq.CompareTo(b.Freq),
            CampoDeOrden.Nombre => (a, b) => string.CompareOrdinal(a.Name, b.Name),
            CampoDeOrden.Qth => (a, b) => string.CompareOrdinal(a.Qth, b.Qth),
            CampoDeOrden.Dxcc => (a, b) => a.Dxcc.CompareTo(b.Dxcc),
            _ => (a, b) => a.InicioUtc.CompareTo(b.InicioUtc),
        };

        Comparison<Qso> ascendente = (a, b) =>
        {
            var c = porCampo(a, b);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        };

        return criterio.Descendente ? (a, b) => ascendente(b, a) : ascendente;
    }

    /// <summary>Posicion de la banda en la tabla ADIF, para que 6m no quede entre 60m y 70cm.</summary>
    private static int IndiceDeBanda(Banda banda)
    {
        if (banda.EsVacia) return -1;
        for (var i = 0; i < Banda.Todas.Count; i++)
        {
            if (Banda.Todas[i] == banda) return i;
        }
        return int.MaxValue;
    }

    private static bool Contiene(string? campo, string texto) =>
        campo is not null && campo.Contains(texto, StringComparison.OrdinalIgnoreCase);

    /// <summary>Saca el trabajo del hilo de la interfaz, como hara la base de datos real.</summary>
    private static Task<T> EnSegundoPlano<T>(Func<T> trabajo, CancellationToken ct) => Task.Run(trabajo, ct);
}
