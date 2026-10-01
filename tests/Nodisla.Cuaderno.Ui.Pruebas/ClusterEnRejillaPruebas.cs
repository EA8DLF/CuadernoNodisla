using FluentAssertions;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Ui.Desarrollo;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// El cluster en rejilla: una fila por estacion, banda y modo, y la columna de «mi propagacion
/// hacia ellos». Fuente de anuncios de mentira, indices solares fijos y reloj fijo.
/// </summary>
public sealed class ClusterEnRejillaPruebas : IAsyncDisposable
{
    private static readonly DateTimeOffset Mediodia = new(2026, 9, 22, 13, 5, 0, TimeSpan.Zero);

    private static readonly IndicesSolares SolDeHoy = new(
        150, 8, 2, 110, 400, 0, false, new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero));

    private readonly FuenteDeMentira _fuente = new();
    private readonly PropagacionDeMentira _propagacion = new() { Indices = SolDeHoy };
    private readonly RepositorioQsoEnMemoria _cuaderno = new(
    [
        new Qso
        {
            Call = Indicativo.Crudo("DL1ABC"), Band = Banda.Parse("20m"), Mode = Modo.Crudo("SSB", "USB"),
            InicioUtc = Mediodia.AddDays(-30), Gridsquare = Locator.Parse("JO62QM"),
        },
    ]);

    private readonly SeguirElCluster _seguir;
    private readonly VistaModeloCluster _cluster;

    public ClusterEnRejillaPruebas()
    {
        _seguir = new SeguirElCluster(
            _fuente,
            new ConsultasDeInformeEnMemoria([], ResolutorDxcc.Predeterminado),
            ResolutorDxcc.Predeterminado);
        _cluster = new VistaModeloCluster(
            _seguir, ResolutorDxcc.Predeterminado, _propagacion, _cuaderno, new RelojFijo(Mediodia));
        _cluster.FijarEstacion(Locator.Parse("IL27HX"));
    }

    [Fact]
    public void La_misma_estacion_en_la_misma_banda_y_modo_es_una_sola_fila()
    {
        Anunciar("JA1NUT", 14.0740m, "FT8", "EA1ABC", segundos: 0);
        Anunciar("JA1NUT", 14.0741m, "FT8", "F5XYZ", segundos: 20);
        Anunciar("JA1NUT", 14.0742m, "FT8", "DL9AB", segundos: 40);

        _cluster.Spots.Should().ContainSingle();
        var fila = _cluster.Spots[0];
        fila.Oyen.Should().Be("×3");
        fila.Anunciante.Should().Be("DL9AB", "manda el anuncio más reciente");
        _cluster.Recibidos.Should().Be(3);
    }

    [Fact]
    public void Otra_banda_u_otro_modo_son_otra_fila()
    {
        Anunciar("JA1NUT", 14.0740m, "FT8", "EA1ABC", segundos: 0);
        Anunciar("JA1NUT", 14.0250m, "CW", "EA1ABC", segundos: 10);
        Anunciar("JA1NUT", 21.0740m, "FT8", "EA1ABC", segundos: 20);

        _cluster.Spots.Should().HaveCount(3);
        _cluster.Spots.Select(f => $"{f.Banda} {f.Modo}").Should()
            .BeEquivalentTo(["20m FT8", "20m CW", "15m FT8"]);
        _cluster.Spots.Should().OnlyContain(f => f.Oyen.Length == 0);
    }

    [Fact]
    public void Las_columnas_salen_como_se_leen()
    {
        Anunciar("VK6LC", 21.0235m, "CW", "EA8TL", segundos: 0, comentario: "up 2");

        var fila = _cluster.Spots.Should().ContainSingle().Subject;
        fila.Hora.Should().Be("13:05");
        fila.Indicativo.Should().Be("VK6LC");
        fila.Frecuencia.Trim().Should().Be("21.02350");
        fila.Banda.Should().Be("15m");
        fila.Modo.Should().Be("CW");
        fila.Pais.Should().NotBeNullOrWhiteSpace();
        fila.Continente.Should().Be("OC");
        fila.Novedad.Should().Be("Entidad nueva", "el cuaderno de la prueba está vacío");
        fila.Comentario.Should().Be("up 2");
        fila.Anunciante.Should().Be("EA8TL");
        fila.Propagacion.Should().EndWith("%");
    }

    [Fact]
    public void De_dia_la_banda_baja_sale_peor_que_la_alta_hacia_la_misma_estacion()
    {
        Anunciar("DL1XYZ", 3.7900m, "SSB", "EA1ABC", segundos: 0, localizador: "JO62");
        Anunciar("DL1XYZ", 21.2950m, "SSB", "EA1ABC", segundos: 5, localizador: "JO62");

        var en80 = _cluster.Spots.Single(f => f.Banda == "80m");
        var en15 = _cluster.Spots.Single(f => f.Banda == "15m");

        en80.Prevision!.Fiabilidad.Should().BeLessThan(en15.Prevision!.Fiabilidad);
        en15.NivelDePropagacion.Should().Be(NivelDePropagacion.Abierta);
        en15.PropagacionDetalle.Should().Contain("MUF").And.Contain("km").And.Contain("rumbo").And.Contain("S/R");
        en15.OrigenDeLaPosicion.Should().Contain("anuncio");
    }

    [Fact]
    public void Sin_indices_solares_la_celda_va_vacia_y_dice_por_que()
    {
        _propagacion.Indices = null;
        _cluster.RecalcularPropagacion();

        Anunciar("JA1NUT", 14.0740m, "FT8", "EA1ABC", segundos: 0);

        var fila = _cluster.Spots.Single();
        fila.Propagacion.Should().BeEmpty();
        fila.NivelDePropagacion.Should().Be(NivelDePropagacion.SinDato);
        fila.PropagacionDetalle.Should().Contain("índices solares");
    }

    [Fact]
    public void Sin_mi_localizador_la_celda_va_vacia()
    {
        _cluster.FijarEstacion(Locator.Vacio);

        Anunciar("JA1NUT", 14.0740m, "FT8", "EA1ABC", segundos: 0);

        var fila = _cluster.Spots.Single();
        fila.Propagacion.Should().BeEmpty();
        fila.PropagacionDetalle.Should().Contain("localizador");
    }

    [Fact]
    public void Sin_posicion_de_la_estacion_la_celda_va_vacia()
    {
        // Un indicativo que no casa con ningun prefijo: ni localizador ni entidad.
        Anunciar("Q0ZZZ", 14.0740m, "FT8", "EA1ABC", segundos: 0);

        var fila = _cluster.Spots.Single();
        fila.Propagacion.Should().BeEmpty();
        fila.PropagacionDetalle.Should().Contain("dónde está");
    }

    [Fact]
    public void Sin_localizador_en_el_anuncio_se_toma_el_del_cuaderno()
    {
        Anunciar("DL1ABC", 14.2000m, "SSB", "EA1ABC", segundos: 0);

        var fila = _cluster.Spots.Single();
        EsperarA(() => fila.OrigenDeLaPosicion.Contains("cuaderno", StringComparison.Ordinal));

        fila.OrigenDeLaPosicion.Should().Contain("JO62QM");
        fila.Prevision.Should().NotBeNull();
    }

    [Fact]
    public void Solo_con_propagacion_esconde_lo_cerrado_y_lo_que_no_tiene_prevision()
    {
        Anunciar("DL1XYZ", 21.2950m, "SSB", "EA1ABC", segundos: 0, localizador: "JO62");
        Anunciar("Q0ZZZ", 14.0740m, "FT8", "EA1ABC", segundos: 5);
        _cluster.Spots.Should().HaveCount(2);

        _cluster.SoloConPropagacion = true;
        EsperaALaVentana.Drenar();

        _cluster.Spots.Should().ContainSingle().Which.Indicativo.Should().Be("DL1XYZ");

        _cluster.QuitarFiltro();
        _cluster.Spots.Should().HaveCount(2);
    }

    public async ValueTask DisposeAsync()
    {
        _cluster.Detener();
        await _seguir.DisposeAsync();
    }

    private void Anunciar(
        string indicativo, decimal mhz, string modo, string anunciante, int segundos,
        string? comentario = null, string? localizador = null)
    {
        _fuente.Anunciar(new Spot(
            Indicativo.Crudo(indicativo),
            Frecuencia.DesdeMegahercios(mhz),
            Indicativo.Crudo(anunciante),
            comentario,
            Mediodia.AddSeconds(segundos),
            "prueba")
        {
            ModoAnunciado = Modo.Crudo(modo),
            Locator = localizador is null ? Locator.Vacio : Locator.Parse(localizador),
        });

        // El cluster pasa el anuncio a la rejilla por el hilo de la ventana.
        EsperaALaVentana.Drenar();
    }

    /// <summary>Espera a lo que hace el cuaderno en segundo plano; no mide tiempos.</summary>
    private static void EsperarA(Func<bool> condicion)
    {
        for (var i = 0; i < 200 && !condicion(); i++) Thread.Sleep(10);
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private sealed class FuenteDeMentira : IFuenteSpots
    {
        public event EventHandler<EstadoDeConexion>? EstadoCambiado;
        public event EventHandler<Spot>? SpotRecibido;
        public event EventHandler<string>? LineaRecibida;

        public string Nombre => "Fuente de mentira";
        public EstadoDeConexion Estado => EstadoDeConexion.Conectado;

        public void Anunciar(Spot spot) => SpotRecibido?.Invoke(this, spot);

        public Task ConectarAsync(CancellationToken ct = default)
        {
            EstadoCambiado?.Invoke(this, EstadoDeConexion.Conectado);
            return Task.CompletedTask;
        }

        public Task DesconectarAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task EnviarAsync(string orden, CancellationToken ct = default)
        {
            LineaRecibida?.Invoke(this, orden);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PropagacionDeMentira : IPropagacion
    {
        public IndicesSolares? Indices { get; set; }

        public event EventHandler<IndicesSolares>? IndicesActualizados;

        public string MotorDePrediccion => "de mentira";

        public bool EsAproximacion => true;

        public Task<IndicesSolares?> ActualizarIndicesAsync(CancellationToken ct = default)
        {
            if (Indices is { } i) IndicesActualizados?.Invoke(this, i);
            return Task.FromResult(Indices);
        }

        public Trayecto CalcularTrayecto(Coordenada origen, Coordenada destino, DateTimeOffset momentoUtc) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PrediccionDeBanda>> PredecirAsync(
            Coordenada origen, Coordenada destino, double potenciaVatios, DateTimeOffset momentoUtc,
            CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
