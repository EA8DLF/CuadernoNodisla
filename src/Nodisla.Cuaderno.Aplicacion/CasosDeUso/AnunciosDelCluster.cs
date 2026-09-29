using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>Quien esta oyendo una estacion y desde donde.</summary>
/// <param name="Anunciante">Indicativo de quien lo anuncia.</param>
/// <param name="Continente">Continente desde el que se oye, si se sabe.</param>
/// <param name="Decibelios">Relacion senal-ruido que informa, si la da.</param>
/// <param name="EsDeEscuchaAutomatica">El anuncio lo puso una maquina, no una persona.</param>
/// <param name="RecibidoUtc">Cuando llego su anuncio.</param>
public sealed record QuienLoOye(
    Indicativo Anunciante,
    string? Continente,
    int? Decibelios,
    bool EsDeEscuchaAutomatica,
    DateTimeOffset RecibidoUtc);

/// <summary>
/// Un anuncio del cluster con todas sus repeticiones ya juntas.
/// </summary>
/// <param name="Spot">El anuncio mas reciente, que es el que manda.</param>
/// <param name="QuienesLoOyen">Todos los que lo han anunciado dentro de la ventana.</param>
/// <param name="PrimeroUtc">Cuando se anuncio por primera vez.</param>
public sealed record AnuncioDelCluster(
    Spot Spot,
    IReadOnlyList<QuienLoOye> QuienesLoOyen,
    DateTimeOffset PrimeroUtc)
{
    /// <summary>Cuantos lo estan oyendo.</summary>
    public int Veces => QuienesLoOyen.Count;

    /// <summary>La estacion se ha anunciado mas de una vez en la ventana.</summary>
    public bool EsRepetido => QuienesLoOyen.Count > 1;

    /// <summary>Continentes distintos desde los que se oye, en orden de llegada.</summary>
    public IReadOnlyList<string> Continentes =>
    [
        .. QuienesLoOyen
            .Select(q => q.Continente)
            .Where(c => c is { Length: > 0 })
            .Select(c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    /// <summary>Solo lo han anunciado estaciones automaticas de escucha.</summary>
    public bool SoloEscuchaAutomatica => QuienesLoOyen.Count > 0 && QuienesLoOyen.All(q => q.EsDeEscuchaAutomatica);

    /// <summary>Lo ha anunciado alguna persona, no solo maquinas.</summary>
    public bool AnunciadoPorPersona => QuienesLoOyen.Any(q => !q.EsDeEscuchaAutomatica);

    /// <summary>El ultimo que lo anuncio era una estacion automatica.</summary>
    public bool ElUltimoEsAutomatico => Spot.EsDeEscuchaAutomatica;
}

/// <summary>Lo que identifica a una estacion anunciada, para saber si un anuncio se repite.</summary>
/// <param name="Indicativo">Estacion anunciada.</param>
/// <param name="Banda">Banda en la que se la oye.</param>
/// <param name="Modo">Modo principal del anuncio.</param>
public readonly record struct ClaveDeAnuncio(string Indicativo, string Banda, string Modo)
{
    /// <summary>Saca la clave de un anuncio.</summary>
    /// <param name="spot">Anuncio recibido.</param>
    public static ClaveDeAnuncio De(Spot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);

        return new ClaveDeAnuncio(
            spot.Indicativo.Valor,
            spot.Banda.Nombre ?? string.Empty,
            spot.ModoAnunciado.EsVacio ? string.Empty : spot.ModoAnunciado.Principal);
    }
}

/// <summary>
/// Junta las repeticiones del mismo anuncio dentro de una ventana de tiempo.
/// </summary>
/// <remarks>
/// <para>
/// Un nodo bien conectado repite el mismo anuncio desde varias rutas, y las redes de escucha
/// automatica anuncian la misma estacion decenas de veces por minuto. Pintarlos todos deja la
/// lista inservible: la estacion que interesa queda enterrada bajo cien copias de si misma.
/// </para>
/// <para>
/// La repeticion <b>no se tira, se actualiza</b>. Que diez estaciones de tres continentes esten
/// oyendo a la misma es justo lo que el operador quiere saber —significa que esta entrando
/// bien—, y tirar el repetido perderia ese dato.
/// </para>
/// <para>
/// Si el anuncio repetido trae una frecuencia distinta o un modo distinto, no es el mismo
/// anuncio: la estacion se ha movido, y eso es un anuncio nuevo.
/// </para>
/// <para>
/// Esta decision es del cuaderno y no de la fuente, por eso vive aqui: la fuente solo dice lo
/// que ha llegado por el hilo.
/// </para>
/// </remarks>
public sealed class JuntaDeRepetidos
{
    /// <summary>Ventana con la que se trabaja si no se pide otra.</summary>
    public static readonly TimeSpan VentanaPorOmision = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Diferencia de frecuencia por debajo de la cual se considera la misma estacion.
    /// </summary>
    /// <remarks>
    /// Dos estaciones que oyen a la misma no leen el mismo dial al hercio: cada receptor tiene
    /// su calibracion y cada operador redondea a su manera. Medio kilohercio cubre esa
    /// dispersion y sigue distinguiendo a dos estaciones distintas, que nunca se ponen tan
    /// cerca ni en telegrafia.
    /// </remarks>
    public const decimal ToleranciaEnKilohercios = 0.5m;

    /// <summary>Estaciones que se recuerdan como mucho, para que la memoria no crezca sin fin.</summary>
    public const int AnunciosVivos = 2_000;

    private readonly Dictionary<ClaveDeAnuncio, Vivo> _vivos = [];
    private readonly object _cerrojo = new();

    /// <summary>Monta la junta con una ventana concreta.</summary>
    /// <param name="ventana">Cuanto tiempo se considera repetido un anuncio.</param>
    public JuntaDeRepetidos(TimeSpan? ventana = null) =>
        Ventana = ventana is { Ticks: > 0 } v ? v : VentanaPorOmision;

    /// <summary>Cuanto tiempo se considera repetido el mismo anuncio.</summary>
    public TimeSpan Ventana { get; }

    /// <summary>Anuncios que se estan siguiendo ahora mismo.</summary>
    public int Siguiendo
    {
        get { lock (_cerrojo) return _vivos.Count; }
    }

    /// <summary>
    /// Mete un anuncio y devuelve como queda, con sus repeticiones juntas.
    /// </summary>
    /// <param name="spot">Anuncio recibido, ya marcado con lo que sabe el cuaderno.</param>
    /// <returns>El anuncio con todos los que lo estan oyendo.</returns>
    public AnuncioDelCluster Juntar(Spot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);

        var clave = ClaveDeAnuncio.De(spot);
        var quien = new QuienLoOye(
            spot.Anunciante,
            spot.Continente,
            spot.Decibelios,
            spot.EsDeEscuchaAutomatica,
            spot.RecibidoUtc);

        lock (_cerrojo)
        {
            Limpiar(spot.RecibidoUtc);

            if (_vivos.TryGetValue(clave, out var vivo) && EsElMismo(vivo, spot))
            {
                vivo.Ultimo = spot;

                // Un mismo anunciante que repite no cuenta dos veces: lo que interesa es
                // cuantas estaciones distintas la estan oyendo, no cuantos mensajes llegaron.
                var yaEstaba = vivo.Quienes.FindIndex(
                    q => string.Equals(q.Anunciante.Valor, quien.Anunciante.Valor, StringComparison.Ordinal));

                if (yaEstaba >= 0) vivo.Quienes[yaEstaba] = quien;
                else vivo.Quienes.Add(quien);

                return new AnuncioDelCluster(spot, [.. vivo.Quienes], vivo.PrimeroUtc);
            }

            // O es nuevo, o la estacion se ha movido de frecuencia: en los dos casos empieza
            // de cero y lo anterior se olvida.
            _vivos[clave] = new Vivo(spot, spot.RecibidoUtc) { Quienes = { quien } };
            return new AnuncioDelCluster(spot, [quien], spot.RecibidoUtc);
        }
    }

    /// <summary>Olvida todo lo que se estaba siguiendo.</summary>
    public void Vaciar()
    {
        lock (_cerrojo) _vivos.Clear();
    }

    /// <summary>
    /// El anuncio que llega es el mismo que se estaba siguiendo.
    /// </summary>
    /// <remarks>
    /// La clave ya garantiza mismo indicativo, misma banda y mismo modo. Lo que queda por
    /// mirar es la frecuencia: si se ha movido de verdad, la estacion ha cambiado de sitio y
    /// eso es un anuncio nuevo, no una repeticion.
    /// </remarks>
    private bool EsElMismo(Vivo vivo, Spot spot)
    {
        if (spot.RecibidoUtc - vivo.Ultimo.RecibidoUtc > Ventana) return false;

        var salto = Math.Abs(spot.Frecuencia.Kilohercios - vivo.Ultimo.Frecuencia.Kilohercios);
        return salto <= ToleranciaEnKilohercios;
    }

    /// <summary>Tira lo que ya se ha salido de la ventana.</summary>
    private void Limpiar(DateTimeOffset ahora)
    {
        if (_vivos.Count == 0) return;

        var caducados = _vivos
            .Where(p => ahora - p.Value.Ultimo.RecibidoUtc > Ventana)
            .Select(p => p.Key)
            .ToList();

        foreach (var clave in caducados) _vivos.Remove(clave);

        // Red de seguridad para un cluster desbocado: si aun quedan demasiados, se sueltan
        // los mas viejos. Sin esto, una noche entera conectado se come la memoria.
        if (_vivos.Count <= AnunciosVivos) return;

        foreach (var clave in _vivos
                     .OrderBy(p => p.Value.Ultimo.RecibidoUtc)
                     .Take(_vivos.Count - AnunciosVivos)
                     .Select(p => p.Key)
                     .ToList())
        {
            _vivos.Remove(clave);
        }
    }

    /// <summary>Un anuncio que se esta siguiendo, con todos los que lo han informado.</summary>
    private sealed class Vivo(Spot ultimo, DateTimeOffset primeroUtc)
    {
        public Spot Ultimo { get; set; } = ultimo;

        public DateTimeOffset PrimeroUtc { get; } = primeroUtc;

        public List<QuienLoOye> Quienes { get; } = [];
    }
}
