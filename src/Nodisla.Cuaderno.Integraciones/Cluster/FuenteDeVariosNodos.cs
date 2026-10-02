using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Integraciones.Cluster;

/// <summary>
/// Varios nodos de cluster conectados a la vez, vistos desde fuera como una sola fuente.
/// </summary>
/// <remarks>
/// <para>
/// Cada nodo es una fuente de las de siempre —un <see cref="ClusterTelnet"/>, o la simulada—
/// con su conexion, su acceso y sus reintentos. Que uno se caiga no toca a los demas: cada
/// uno reconecta por su cuenta con su propia espera creciente.
/// </para>
/// <para>
/// Aqui no se juntan los repetidos: eso es una decision del cuaderno y la toma
/// <c>JuntaDeRepetidos</c>. Lo que si se hace es poner a cada anuncio el nombre de su nodo
/// en <see cref="Spot.Fuente"/>, para que el cuaderno sepa por donde llego cada copia, y
/// marcar como de maquina todo lo que trae un nodo de escucha automatica.
/// </para>
/// <para>
/// <b>Las ordenes van a un solo nodo.</b> Un «DX» mandado por todos los nodos a la vez
/// saldria repetido en toda la red.
/// </para>
/// </remarks>
public sealed class FuenteDeVariosNodos : IFuenteDeVariosNodos
{
    /// <summary>Cada cuanto, como mucho, se avisa de que han llegado anuncios.</summary>
    private static readonly TimeSpan AvisoDeRitmo = TimeSpan.FromSeconds(2);

    private readonly Func<OpcionesCluster, IFuenteSpots> _fabrica;
    private readonly TimeProvider _reloj;
    private readonly SemaphoreSlim _puerta = new(1, 1);
    private readonly object _cerrojo = new();

    private IReadOnlyList<Nodo> _nodos = [];
    private EstadoDeConexion _estado = EstadoDeConexion.Desconectado;
    private DateTimeOffset _ultimoAvisoDeRitmo = DateTimeOffset.MinValue;
    private bool _enMarcha;
    private bool _desechado;

    /// <summary>Monta la fuente con los nodos dados, sin conectar ninguno.</summary>
    /// <param name="fabrica">Como se crea la fuente de cada nodo.</param>
    /// <param name="nodos">Nodos de partida, en el orden en que se ensenan.</param>
    /// <param name="reloj">Reloj; en las pruebas, uno que se mueve a mano.</param>
    public FuenteDeVariosNodos(
        Func<OpcionesCluster, IFuenteSpots> fabrica,
        IEnumerable<OpcionesCluster>? nodos = null,
        TimeProvider? reloj = null)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _reloj = reloj ?? TimeProvider.System;

        var lista = new List<Nodo>();
        foreach (var opciones in nodos ?? [])
        {
            if (lista.Exists(n => n.Clave == opciones.Clave)) continue;
            lista.Add(Crear(opciones));
        }
        _nodos = lista;
    }

    /// <inheritdoc />
    public string Nombre
    {
        get
        {
            var nodos = _nodos;
            return nodos.Count == 1 ? nodos[0].Opciones.Nombre : Textos.T("Servicios.Cluster.VariosNodos");
        }
    }

    /// <inheritdoc />
    public EstadoDeConexion Estado => _estado;

    /// <inheritdoc />
    public IReadOnlyList<EstadoDeNodo> Nodos
    {
        get
        {
            var ahora = _reloj.GetUtcNow();
            return [.. _nodos.Select(n => n.Foto(ahora))];
        }
    }

    /// <summary>Opciones de cada nodo, en orden. Para las pruebas y el diagnostico.</summary>
    public IReadOnlyList<OpcionesCluster> Opciones => [.. _nodos.Select(n => n.Opciones)];

    /// <inheritdoc />
    public event EventHandler<EstadoDeConexion>? EstadoCambiado;

    /// <inheritdoc />
    public event EventHandler<Spot>? SpotRecibido;

    /// <inheritdoc />
    public event EventHandler<string>? LineaRecibida;

    /// <inheritdoc />
    public event EventHandler? NodosCambiaron;

    /// <inheritdoc />
    /// <remarks>
    /// Conecta todos los nodos activos a la vez y vuelve cuando ha terminado el primer intento
    /// de cada uno. Los que fallan se quedan reintentando por su cuenta.
    /// </remarks>
    public async Task ConectarAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        _enMarcha = true;

        var activos = _nodos.Where(n => n.Opciones.Activo).ToList();
        await Task.WhenAll(activos.Select(n => ConectarConCuidadoAsync(n, ct))).ConfigureAwait(false);
        Recalcular();
    }

    /// <inheritdoc />
    public async Task DesconectarAsync(CancellationToken ct = default)
    {
        _enMarcha = false;
        await Task.WhenAll(_nodos.Select(n => DesconectarConCuidadoAsync(n, ct))).ConfigureAwait(false);
        Recalcular();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Va al primer nodo conectado de la lista, y solo a ese. Para elegir otro esta
    /// <see cref="EnviarANodoAsync"/>.
    /// </remarks>
    public Task EnviarAsync(string orden, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orden);

        var nodo = _nodos.FirstOrDefault(n => n.Fuente.Estado == EstadoDeConexion.Conectado)
            ?? throw new InvalidOperationException(Textos.T("Servicios.Cluster.NingunNodoConectado"));
        return nodo.Fuente.EnviarAsync(orden, ct);
    }

    /// <inheritdoc />
    public Task ConectarNodoAsync(string id, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        var nodo = Buscar(id);
        return ConectarYRecalcularAsync(nodo, ct);
    }

    /// <inheritdoc />
    public async Task DesconectarNodoAsync(string id, CancellationToken ct = default)
    {
        var nodo = Buscar(id);
        await DesconectarConCuidadoAsync(nodo, ct).ConfigureAwait(false);
        Recalcular();
    }

    /// <inheritdoc />
    public Task EnviarANodoAsync(string id, string orden, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orden);
        return Buscar(id).Fuente.EnviarAsync(orden, ct);
    }

    /// <summary>
    /// Mete un spot que no viene de ningun nodo (los que mandan los programas externos por TCI)
    /// para que siga el mismo camino que los demas hasta el bandmap.
    /// </summary>
    /// <param name="spot">El spot; su <see cref="Spot.Fuente"/> dice de donde viene.</param>
    public void Inyectar(Spot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);
        if (_desechado) return;
        SpotRecibido?.Invoke(this, spot);
    }

    /// <summary>
    /// Pone la lista de nodos nueva sin cerrar el programa ni tirar lo que no ha cambiado.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Un nodo que sigue igual sigue conectado: cambiar el puerto de otro no es motivo para
    /// cortarle a este la sesion. El que ha cambiado de maquina, de acceso o de guion se cierra
    /// y se abre otra vez. El que ya no esta se cierra y se suelta.
    /// </para>
    /// <para>
    /// Si el cluster estaba en marcha, los nodos nuevos o recien activados se conectan solos, de
    /// fondo: aplicar no espera a que un nodo lento conteste.
    /// </para>
    /// </remarks>
    /// <param name="nodos">Nodos que tiene que haber, en el orden en que se ensenan.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task AplicarAsync(IEnumerable<OpcionesCluster> nodos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(nodos);
        ObjectDisposedException.ThrowIf(_desechado, this);

        var pedidos = new List<OpcionesCluster>();
        foreach (var opciones in nodos)
        {
            if (pedidos.Exists(p => p.Clave == opciones.Clave)) continue;
            pedidos.Add(opciones);
        }

        var cerrar = new List<Nodo>();
        var conectar = new List<Nodo>();

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var anteriores = _nodos;
            var nuevos = new List<Nodo>();

            foreach (var opciones in pedidos)
            {
                var antes = anteriores.FirstOrDefault(n => n.Clave == opciones.Clave);
                if (antes is not null && antes.Opciones.Firma == opciones.Firma)
                {
                    var estabaActivo = antes.Opciones.Activo;
                    antes.Opciones = opciones;
                    nuevos.Add(antes);

                    if (_enMarcha && opciones.Activo && !estabaActivo) conectar.Add(antes);
                    if (_enMarcha && !opciones.Activo && estabaActivo) cerrar.Add(antes);
                    continue;
                }

                var nodo = Crear(opciones);
                nuevos.Add(nodo);

                var estabaConectado = antes is not null && antes.Fuente.Estado != EstadoDeConexion.Desconectado;
                if (antes is not null) cerrar.Add(antes);
                if (opciones.Activo && (_enMarcha || estabaConectado)) conectar.Add(nodo);
            }

            cerrar.AddRange(anteriores.Where(a => !nuevos.Contains(a)));
            _nodos = nuevos;
        }
        finally
        {
            _puerta.Release();
        }

        foreach (var nodo in cerrar)
        {
            if (_nodos.Contains(nodo))
            {
                await DesconectarConCuidadoAsync(nodo, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await SoltarAsync(nodo).ConfigureAwait(false);
            }
        }

        foreach (var nodo in conectar) _ = ConectarYRecalcularAsync(nodo, CancellationToken.None);

        Recalcular();
        NodosCambiaron?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_desechado) return;
        _desechado = true;

        var nodos = _nodos;
        _nodos = [];
        foreach (var nodo in nodos) await SoltarAsync(nodo).ConfigureAwait(false);
        _puerta.Dispose();
    }

    private Nodo Buscar(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _nodos.FirstOrDefault(n => n.Clave == id)
            ?? throw new InvalidOperationException(Textos.F("Servicios.Cluster.NodoDesconocido", id));
    }

    private Nodo Crear(OpcionesCluster opciones)
    {
        var nodo = new Nodo(opciones, _fabrica(opciones));
        nodo.Fuente.SpotRecibido += nodo.AlSpot = (_, spot) => AlLlegarUnSpot(nodo, spot);
        nodo.Fuente.LineaRecibida += nodo.AlLinea = (_, linea) => AlLlegarUnaLinea(nodo, linea);
        nodo.Fuente.EstadoCambiado += nodo.AlEstado = (_, _) => AlCambiarElEstado();
        return nodo;
    }

    private async Task ConectarYRecalcularAsync(Nodo nodo, CancellationToken ct)
    {
        await ConectarConCuidadoAsync(nodo, ct).ConfigureAwait(false);
        Recalcular();
    }

    /// <summary>Conecta un nodo sin que su fallo tumbe a los demas.</summary>
    private async Task ConectarConCuidadoAsync(Nodo nodo, CancellationToken ct)
    {
        try
        {
            await nodo.Fuente.ConectarAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            nodo.ErrorPropio = ex.Message;
            AlLlegarUnaLinea(nodo, Textos.F("Servicios.Cluster.ConexionPerdida", nodo.Opciones.Nombre, ex.Message));
        }
    }

    private static async Task DesconectarConCuidadoAsync(Nodo nodo, CancellationToken ct)
    {
        try
        {
            await nodo.Fuente.DesconectarAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Un nodo que no deja despedirse no puede impedir cerrar los demas.
        }
    }

    /// <summary>Cierra un nodo, lo desengancha y lo suelta.</summary>
    private async Task SoltarAsync(Nodo nodo)
    {
        nodo.Fuente.SpotRecibido -= nodo.AlSpot;
        nodo.Fuente.LineaRecibida -= nodo.AlLinea;
        nodo.Fuente.EstadoCambiado -= nodo.AlEstado;

        await DesconectarConCuidadoAsync(nodo, CancellationToken.None).ConfigureAwait(false);
        try
        {
            await nodo.Fuente.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Idem.
        }
    }

    private void AlLlegarUnSpot(Nodo nodo, Spot spot)
    {
        var ahora = _reloj.GetUtcNow();
        nodo.Apuntar(ahora);

        var nombre = nodo.Opciones.Nombre;
        var marcado = spot;
        if (!string.Equals(spot.Fuente, nombre, StringComparison.Ordinal)) marcado = marcado with { Fuente = nombre };
        if (nodo.Opciones.EsSkimmer && !marcado.EsDeEscuchaAutomatica) marcado = marcado with { EsDeEscuchaAutomatica = true };

        SpotRecibido?.Invoke(this, marcado);

        // El ritmo de anuncios se ensena en vivo, pero sin avisar con cada uno: un nodo de
        // escucha automatica manda varios por segundo.
        bool avisar;
        lock (_cerrojo)
        {
            avisar = ahora - _ultimoAvisoDeRitmo >= AvisoDeRitmo;
            if (avisar) _ultimoAvisoDeRitmo = ahora;
        }
        if (avisar) NodosCambiaron?.Invoke(this, EventArgs.Empty);
    }

    private void AlLlegarUnaLinea(Nodo nodo, string linea)
    {
        // Con mas de un nodo, cada linea dice de cual viene: si no, la consola es un revoltijo.
        var texto = _nodos.Count > 1 ? $"[{nodo.Opciones.Nombre}] {linea}" : linea;
        LineaRecibida?.Invoke(this, texto);
    }

    private void AlCambiarElEstado()
    {
        Recalcular();
        NodosCambiaron?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Saca el estado de conjunto: conectado si lo esta alguno.
    /// </summary>
    /// <remarks>
    /// Con un nodo dentro ya llegan anuncios, y eso es lo que el operador quiere saber de un
    /// vistazo. El detalle de cada uno esta en la lista de nodos.
    /// </remarks>
    private void Recalcular()
    {
        var estados = _nodos.Select(n => n.Fuente.Estado).ToList();
        var nuevo =
            estados.Contains(EstadoDeConexion.Conectado) ? EstadoDeConexion.Conectado
            : estados.Contains(EstadoDeConexion.Conectando) ? EstadoDeConexion.Conectando
            : estados.Contains(EstadoDeConexion.Reintentando) ? EstadoDeConexion.Reintentando
            : estados.Contains(EstadoDeConexion.Fallido) ? EstadoDeConexion.Fallido
            : EstadoDeConexion.Desconectado;

        bool cambia;
        lock (_cerrojo)
        {
            cambia = nuevo != _estado;
            _estado = nuevo;
        }

        if (cambia) EstadoCambiado?.Invoke(this, nuevo);
    }

    /// <summary>Un nodo de la lista, con su fuente y lo que se lleva contado.</summary>
    private sealed class Nodo(OpcionesCluster opciones, IFuenteSpots fuente)
    {
        private static readonly TimeSpan Minuto = TimeSpan.FromMinutes(1);

        private readonly Queue<DateTimeOffset> _recientes = new();
        private long _recibidos;

        public OpcionesCluster Opciones { get; set; } = opciones;

        public IFuenteSpots Fuente { get; } = fuente;

        public string Clave => Opciones.Clave;

        public string? ErrorPropio { get; set; }

        public EventHandler<Spot>? AlSpot { get; set; }

        public EventHandler<string>? AlLinea { get; set; }

        public EventHandler<EstadoDeConexion>? AlEstado { get; set; }

        public void Apuntar(DateTimeOffset ahora)
        {
            lock (_recientes)
            {
                _recibidos++;
                _recientes.Enqueue(ahora);
                Purgar(ahora);
            }
        }

        public EstadoDeNodo Foto(DateTimeOffset ahora)
        {
            int ritmo;
            long recibidos;
            lock (_recientes)
            {
                Purgar(ahora);
                ritmo = _recientes.Count;
                recibidos = _recibidos;
            }

            var estado = Fuente.Estado;
            var motivo = estado == EstadoDeConexion.Conectado
                ? null
                : (Fuente as IFuenteConDiagnostico)?.UltimoError ?? ErrorPropio;

            return new EstadoDeNodo(
                Clave,
                Opciones.Nombre,
                Opciones.Servidor,
                Opciones.Puerto,
                Opciones.Activo,
                Opciones.EsSkimmer,
                estado,
                motivo,
                ritmo,
                recibidos);
        }

        private void Purgar(DateTimeOffset ahora)
        {
            while (_recientes.Count > 0 && ahora - _recientes.Peek() > Minuto) _recientes.Dequeue();
        }
    }
}
