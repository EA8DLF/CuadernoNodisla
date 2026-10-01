using Nodisla.Cuaderno.Radio.Control.Ft710;

namespace Nodisla.Cuaderno.Radio.Pruebas.Dobles;

/// <summary>
/// Un Yaesu de CAT nuevo en memoria, que contesta lo que le digan como dice su manual CAT.
/// </summary>
/// <remarks>
/// <para>
/// <b>No hay radios de estos modelos:</b> las respuestas salen del manual CAT de cada uno, no
/// de una captura. Lo que no se le haya dicho lo contesta con <c>?;</c>, como el equipo con una
/// orden que no tiene.
/// </para>
/// <para>
/// Las ordenes de escritura cambian la respuesta de su consulta: <c>FA07074000;</c> hace que
/// <c>FA;</c> conteste <c>FA07074000</c>. Se busca la consulta conocida mas larga que sea
/// principio de la orden.
/// </para>
/// </remarks>
internal sealed class YaesuAsciiDeMentira : ICanalCat
{
    private readonly object _candado = new();
    private readonly Dictionary<string, string> _respuestas = new(StringComparer.Ordinal);
    private readonly List<string> _recibidas = [];

    internal YaesuAsciiDeMentira(IDictionary<string, string> respuestas)
    {
        foreach (var (consulta, respuesta) in respuestas)
        {
            _respuestas[consulta] = respuesta;
        }
    }

    public bool Abierto { get; private set; }

    public string Descripcion => "Yaesu de mentira";

    internal IReadOnlyList<string> Recibidas
    {
        get
        {
            lock (_candado) return _recibidas.ToList();
        }
    }

    internal void Responder(string consulta, string respuesta)
    {
        lock (_candado) _respuestas[consulta] = respuesta;
    }

    public Task AbrirAsync(CancellationToken ct = default)
    {
        Abierto = true;
        return Task.CompletedTask;
    }

    public Task<string?> PreguntarAsync(string orden, CancellationToken ct = default)
    {
        lock (_candado)
        {
            _recibidas.Add(orden);
            return Task.FromResult<string?>(_respuestas.TryGetValue(orden, out var r) ? r : Cat.NoAdmitido);
        }
    }

    public Task MandarAsync(string orden, CancellationToken ct = default)
    {
        MandarSincrono(orden);
        return Task.CompletedTask;
    }

    public void MandarSincrono(string orden)
    {
        lock (_candado)
        {
            _recibidas.Add(orden);
            var cuerpo = orden.TrimEnd(Cat.Fin);
            var consulta = _respuestas.Keys
                .Select(k => k.TrimEnd(Cat.Fin))
                .Where(k => cuerpo.Length > k.Length && cuerpo.StartsWith(k, StringComparison.Ordinal))
                .OrderByDescending(k => k.Length)
                .FirstOrDefault();
            if (consulta is not null)
            {
                _respuestas[consulta + Cat.Fin] = cuerpo;
            }
        }
    }

    public void Cerrar() => Abierto = false;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
