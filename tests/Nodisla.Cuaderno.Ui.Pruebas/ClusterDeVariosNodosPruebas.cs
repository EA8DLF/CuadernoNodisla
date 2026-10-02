using System.Collections.Concurrent;
using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Integraciones.Cluster;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El panel del cluster con varios nodos: una fila por estación aunque llegue por varios, el
/// resumen de nodos, el filtro por nodo y las órdenes a un solo nodo. Nodos de mentira en
/// memoria, sin red.
/// </summary>
public sealed class ClusterDeVariosNodosPruebas : IAsyncDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 2, 18, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentDictionary<string, NodoDeMentira> _nodos = new();
    private readonly FuenteDeVariosNodos _fuente;
    private readonly SeguirElCluster _seguir;
    private readonly VistaModeloCluster _cluster;

    public ClusterDeVariosNodosPruebas()
    {
        _fuente = new FuenteDeVariosNodos(
            opciones => _nodos.GetOrAdd(opciones.Clave, _ => new NodoDeMentira(opciones.Nombre)),
            [Opciones("ea4rch", "EA4RCH (España)"), Opciones("dxfun", "DXFun (internacional)"), Opciones("rbn", "RBN · CW y RTTY", skimmer: true)]);
        _seguir = new SeguirElCluster(
            _fuente,
            new ConsultasDeInformeEnMemoria([], ResolutorDxcc.Predeterminado),
            ResolutorDxcc.Predeterminado);
        _cluster = new VistaModeloCluster(_seguir);
    }

    [Fact]
    public async Task El_resumen_dice_cuantos_nodos_hay_y_cuantos_estan_conectados()
    {
        await _fuente.ConectarNodoAsync("ea4rch");
        await _fuente.ConectarNodoAsync("dxfun");
        EsperaALaVentana.Drenar();

        _cluster.HayVariosNodos.Should().BeTrue();
        _cluster.ResumenDeNodos.Should().Be("3 nodos · 2 conectados");
        _cluster.EstadosDeNodos.Select(n => n.EstadoTexto)
            .Should().Equal("Conectado", "Conectado", "Sin conexión");
    }

    [Fact]
    public async Task El_mismo_spot_por_dos_nodos_es_una_fila_con_los_dos_origenes()
    {
        await _fuente.ConectarAsync();
        EsperaALaVentana.Drenar();

        Anunciar("ea4rch", "3Y0J", 14.0250m, "EA1ABC", 0);
        Anunciar("dxfun", "3Y0J", 14.0252m, "EA1ABC", 3);

        var fila = _cluster.Spots.Should().ContainSingle().Subject;
        fila.NodosCortos.Should().Be("EA4RCH DXFun");
        fila.Nodos.Should().Be("EA4RCH (España), DXFun (internacional)");
        fila.Oyen.Should().BeEmpty("es un solo anunciante, llegado por dos nodos");
        fila.Detalle.Should().Contain("2 nodos");
    }

    [Fact]
    public async Task Oyen_suma_los_anunciantes_distintos_de_todos_los_nodos()
    {
        await _fuente.ConectarAsync();
        EsperaALaVentana.Drenar();

        Anunciar("ea4rch", "VP8LP", 7.0050m, "EA1ABC", 0);
        Anunciar("dxfun", "VP8LP", 7.0051m, "F5XYZ", 10);
        Anunciar("dxfun", "VP8LP", 7.0052m, "EA1ABC", 20);

        _cluster.Spots.Should().ContainSingle().Which.Oyen.Should().Be("×2");
    }

    [Fact]
    public async Task Se_puede_filtrar_por_nodo_y_esconder_el_skimmer()
    {
        await _fuente.ConectarAsync();
        EsperaALaVentana.Drenar();

        Anunciar("ea4rch", "3Y0J", 14.0250m, "EA1ABC", 0);
        Anunciar("dxfun", "VP8LP", 7.0050m, "F5XYZ", 5);
        Anunciar("rbn", "ZD8W", 14.0200m, "DL8LAS", 6);

        _cluster.Spots.Should().HaveCount(3);
        _cluster.NodosParaFiltrar.Should().Equal(VistaModeloCluster.Cualquiera, "EA4RCH (España)", "DXFun (internacional)", "RBN · CW y RTTY");

        _cluster.NodoFiltro = "DXFun (internacional)";
        _cluster.Spots.Should().ContainSingle().Which.Indicativo.Should().Be("VP8LP");

        _cluster.NodoFiltro = VistaModeloCluster.Cualquiera;
        _cluster.SinEscuchaAutomatica = true;
        _cluster.Spots.Should().HaveCount(2, "lo del RBN es de escucha automática aunque la línea no lo diga");
        _cluster.Spots.Should().NotContain(f => f.Indicativo == "ZD8W");
    }

    [Fact]
    public async Task La_orden_de_la_consola_va_solo_al_nodo_elegido()
    {
        await _fuente.ConectarAsync();
        EsperaALaVentana.Drenar();

        _cluster.NodoDeEnvio = _cluster.NodosParaEnviar.Single(n => n.Id == "dxfun");
        _cluster.Orden = "DX 14025 3Y0J loud in EA8";
        await _cluster.EnviarOrdenCommand.ExecuteAsync(null);

        _nodos["dxfun"].Enviadas.Should().Equal("DX 14025 3Y0J loud in EA8");
        _nodos["ea4rch"].Enviadas.Should().BeEmpty("nunca se manda duplicado por todos los nodos");
        _nodos["rbn"].Enviadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Sin_elegir_la_orden_va_al_primer_nodo_conectado()
    {
        await _fuente.ConectarNodoAsync("dxfun");
        EsperaALaVentana.Drenar();

        _cluster.NodoDeEnvio!.Id.Should().Be("dxfun", "es el único conectado");
        _cluster.Orden = "SH/WWV";
        await _cluster.EnviarOrdenCommand.ExecuteAsync(null);

        _nodos["dxfun"].Enviadas.Should().Equal("SH/WWV");
        _nodos["ea4rch"].Enviadas.Should().BeEmpty();
    }

    [Fact]
    public async Task La_consola_dice_de_que_nodo_viene_cada_linea()
    {
        await _fuente.ConectarAsync();
        EsperaALaVentana.Drenar();

        _nodos["dxfun"].Decir("Hola desde el nodo");
        EsperaALaVentana.Drenar();

        _cluster.Consola.Should().Contain("[DXFun (internacional)] Hola desde el nodo");
    }

    [Fact]
    public async Task La_fuente_simulada_trae_varios_nodos_y_uno_caido()
    {
        var muestra = FuenteSpotsSimulada.ConNodosDeMuestra(new Ajustes.AjustesDelPrograma());
        muestra.Cluster.Nodos.Should().HaveCountGreaterThan(3);

        await using var simulada = new FuenteDeVariosNodos(
            o => new FuenteSpotsSimulada(o),
            muestra.Cluster.Nodos.Select(n => muestra.Cluster.AOpcionesDeNodo(n, Indicativo.Parse("EA8DLF"), null)));
        await simulada.ConectarAsync();

        simulada.Nodos.Count(n => n.Estado == EstadoDeConexion.Conectado).Should().BeGreaterThanOrEqualTo(3);
        var caido = simulada.Nodos.Single(n => n.Servidor.EndsWith(".invalid", StringComparison.Ordinal));
        caido.Estado.Should().Be(EstadoDeConexion.Reintentando);
        caido.Motivo.Should().NotBeNullOrWhiteSpace();
        simulada.Nodos.Should().Contain(n => n.EsSkimmer);
        simulada.Nodos.Should().Contain(n => !n.Activo && n.Estado == EstadoDeConexion.Desconectado);
        await simulada.DesconectarAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _cluster.Detener();
        await _seguir.DisposeAsync();
    }

    private static OpcionesCluster Opciones(string id, string nombre, bool skimmer = false) => new()
    {
        Id = id,
        Nombre = nombre,
        Servidor = id + ".ejemplo",
        Indicativo = Indicativo.Parse("EA8DLF"),
        EsSkimmer = skimmer,
    };

    private void Anunciar(string nodo, string indicativo, decimal mhz, string anunciante, int segundos)
    {
        _nodos[nodo].Anunciar(new Spot(
            Indicativo.Crudo(indicativo),
            Frecuencia.DesdeMegahercios(mhz),
            Indicativo.Crudo(anunciante),
            null,
            Ahora.AddSeconds(segundos),
            "lo que diga el nodo")
        {
            ModoAnunciado = Modo.Crudo("CW"),
        });

        EsperaALaVentana.Drenar();
    }

    /// <summary>Un nodo en memoria: conecta al momento y apunta lo que se le manda.</summary>
    private sealed class NodoDeMentira(string nombre) : IFuenteSpots
    {
        public event EventHandler<EstadoDeConexion>? EstadoCambiado;
        public event EventHandler<Spot>? SpotRecibido;
        public event EventHandler<string>? LineaRecibida;

        public string Nombre { get; } = nombre;

        public EstadoDeConexion Estado { get; private set; }

        public List<string> Enviadas { get; } = [];

        public void Anunciar(Spot spot) => SpotRecibido?.Invoke(this, spot);

        public void Decir(string linea) => LineaRecibida?.Invoke(this, linea);

        public Task ConectarAsync(CancellationToken ct = default)
        {
            Estado = EstadoDeConexion.Conectado;
            EstadoCambiado?.Invoke(this, Estado);
            return Task.CompletedTask;
        }

        public Task DesconectarAsync(CancellationToken ct = default)
        {
            Estado = EstadoDeConexion.Desconectado;
            EstadoCambiado?.Invoke(this, Estado);
            return Task.CompletedTask;
        }

        public Task EnviarAsync(string orden, CancellationToken ct = default)
        {
            Enviadas.Add(orden);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
