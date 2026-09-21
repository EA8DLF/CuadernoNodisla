using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.CasosDeUso;

/// <summary>
/// Lo que el operador ha dejado puesto en los filtros del cluster.
/// </summary>
/// <param name="Banda">Banda, o nulo para todas.</param>
/// <param name="Modo">Modo principal, o nulo para todos.</param>
/// <param name="Continente">Continente de dos letras, o nulo para todos.</param>
/// <param name="SoloNuevos">Solo los spots que serian entidad nueva o hueco nuevo.</param>
public sealed record FiltroDeSpots(
    string? Banda = null,
    string? Modo = null,
    string? Continente = null,
    bool SoloNuevos = false)
{
    /// <summary>Lo que se muestra al arrancar: todo.</summary>
    public static FiltroDeSpots Todo { get; } = new();

    /// <summary>
    /// Se esconden los anuncios de las estaciones automaticas de escucha.
    /// </summary>
    /// <remarks>
    /// Una red de escucha automatica anuncia la misma estacion decenas de veces por minuto y
    /// ahoga los anuncios hechos por personas. Poder quitarlas de en medio es la diferencia
    /// entre una lista que se puede leer y un chorro inservible.
    /// </remarks>
    public bool SinEscuchaAutomatica { get; init; }

    /// <summary>Este spot pasa el filtro.</summary>
    /// <param name="spot">Spot recibido.</param>
    /// <returns>Cierto si hay que ensenarlo.</returns>
    public bool Admite(Spot spot)
    {
        ArgumentNullException.ThrowIfNull(spot);

        if (Banda is { Length: > 0 } && !string.Equals(spot.Banda.Nombre, Banda, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Modo is { Length: > 0 } && !string.Equals(ModoDe(spot), Modo, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Continente is { Length: > 0 }
            && !string.Equals(spot.Continente, Continente, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (SinEscuchaAutomatica && spot.EsDeEscuchaAutomatica) return false;

        return !SoloNuevos || spot.EsEntidadNueva || spot.EsNuevoEnBandaYModo;
    }

    /// <summary>
    /// Modo del spot para comparar. Cuando el anuncio no lo dice se deduce del trozo de banda,
    /// que es lo que hace cualquier operador al mirar la frecuencia.
    /// </summary>
    private static string ModoDe(Spot spot) =>
        spot.ModoAnunciado.EsVacio
            ? ModoProbable(spot.Frecuencia)
            : spot.ModoAnunciado.NombreUsual;

    /// <summary>
    /// Modo que cabe esperar en esa frecuencia por el plan de bandas de la IARU.
    /// </summary>
    /// <remarks>
    /// Es una aproximacion deliberada: el cluster no siempre dice el modo, y dejar el spot sin
    /// modo lo saca de todos los filtros. Vale mas acertar casi siempre que no decir nada.
    /// </remarks>
    public static string ModoProbable(Frecuencia frecuencia)
    {
        var khz = (double)frecuencia.Kilohercios;

        // Los trozos de FT8 son los mismos en todas las bandas: el dial mas los tres kilohercios
        // de ancho del modo.
        double[] ft8 = [1840, 3573, 7074, 10136, 14074, 18100, 21074, 24915, 28074, 50313];
        foreach (var dial in ft8)
        {
            if (khz >= dial - 1 && khz <= dial + 4) return "FT8";
        }

        // El nombre corto «Banda» lo ocupa la propiedad del filtro, asi que aqui hay que
        // llamar al tipo del dominio por su nombre completo.
        var banda = Dominio.Valores.Banda.DesdeFrecuencia(frecuencia);
        if (banda.EsVacia) return "SSB";

        var limite = banda.Limite;
        var desde = (double)(limite.Inferior * 1000m);
        var hasta = (double)(limite.Superior * 1000m);
        if (hasta <= desde) return "SSB";

        // El primer quinto de cada banda de HF es telegrafia; el siguiente, modos de datos.
        var parte = (khz - desde) / (hasta - desde);
        return parte switch
        {
            < 0.20 => "CW",
            < 0.30 => "RTTY",
            _ => "SSB",
        };
    }
}

/// <summary>
/// Escucha una fuente de spots y marca cada anuncio con lo que el cuaderno sabe de el.
/// </summary>
/// <remarks>
/// La fuente solo trae lo que dijo el cluster: indicativo, frecuencia y comentario. Lo que
/// convierte un spot en util —«esta entidad no la tienes», «la tienes, pero no en esta banda y
/// este modo»— sale del cuaderno, y por eso lo pone este caso de uso y no la integracion.
///
/// Todo lo que sale de aqui es inmutable y se puede llevar al hilo de la interfaz tal cual.
/// </remarks>
public sealed class SeguirElCluster : IAsyncDisposable
{
    private readonly IFuenteSpots _fuente;
    private readonly IConsultasDeInforme _consultas;
    private readonly IResolutorDxcc _dxcc;
    private readonly JuntaDeRepetidos _repetidos;

    /// <summary>Monta el seguimiento sobre una fuente concreta.</summary>
    /// <param name="fuente">De donde llegan los anuncios.</param>
    /// <param name="consultas">Consultas del cuaderno que dicen si algo es nuevo.</param>
    /// <param name="dxcc">Resolutor de entidades, para saber el pais y el continente.</param>
    /// <param name="ventanaDeRepetidos">
    /// Cuanto tiempo se considera repetido el mismo anuncio. Diez minutos por omision.
    /// </param>
    public SeguirElCluster(
        IFuenteSpots fuente,
        IConsultasDeInforme consultas,
        IResolutorDxcc dxcc,
        TimeSpan? ventanaDeRepetidos = null)
    {
        ArgumentNullException.ThrowIfNull(fuente);
        ArgumentNullException.ThrowIfNull(consultas);
        ArgumentNullException.ThrowIfNull(dxcc);

        _fuente = fuente;
        _consultas = consultas;
        _dxcc = dxcc;
        _repetidos = new JuntaDeRepetidos(ventanaDeRepetidos);

        _fuente.SpotRecibido += AlLlegarUnSpot;
        _fuente.LineaRecibida += AlLlegarUnaLinea;
        _fuente.EstadoCambiado += AlCambiarElEstado;
    }

    /// <summary>
    /// Salta con cada anuncio ya enriquecido con lo que sabe el cuaderno y con sus
    /// repeticiones juntas.
    /// </summary>
    public event EventHandler<AnuncioDelCluster>? AnuncioRecibido;

    /// <summary>Salta con cada linea del cluster que no es un anuncio.</summary>
    public event EventHandler<string>? LineaRecibida;

    /// <summary>Salta cuando cambia el estado de la conexion.</summary>
    public event EventHandler<EstadoDeConexion>? EstadoCambiado;

    /// <summary>Nombre de la fuente, para la pantalla.</summary>
    public string Nombre => _fuente.Nombre;

    /// <summary>Estado de la conexion con la fuente.</summary>
    public EstadoDeConexion Estado => _fuente.Estado;

    /// <summary>Cuanto tiempo se considera repetido el mismo anuncio.</summary>
    public TimeSpan VentanaDeRepetidos => _repetidos.Ventana;

    /// <summary>Olvida las repeticiones que se estaban siguiendo.</summary>
    public void OlvidarRepetidos() => _repetidos.Vaciar();

    /// <summary>Conecta con la fuente y empieza a recibir.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    public Task ConectarAsync(CancellationToken ct = default) => _fuente.ConectarAsync(ct);

    /// <summary>Cierra la conexion con la fuente.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    public Task DesconectarAsync(CancellationToken ct = default) => _fuente.DesconectarAsync(ct);

    /// <summary>Manda una orden en crudo al cluster.</summary>
    /// <param name="orden">Lo que se escribe en la consola del cluster.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public Task EnviarAsync(string orden, CancellationToken ct = default) => _fuente.EnviarAsync(orden, ct);

    /// <summary>
    /// Anade al spot la entidad, el pais, el continente y si seria nuevo.
    /// </summary>
    /// <param name="spot">Spot tal y como llego de la fuente.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El mismo spot con lo que sabe el cuaderno anadido.</returns>
    public async Task<Spot> MarcarAsync(Spot spot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spot);

        var resuelto = _dxcc.Resolver(spot.Indicativo, DateOnly.FromDateTime(spot.RecibidoUtc.UtcDateTime));
        var entidad = resuelto.Entidad;

        var marcado = spot with
        {
            Dxcc = entidad?.Numero ?? spot.Dxcc,
            Pais = entidad?.NombreParaMostrar ?? spot.Pais,
            Continente = resuelto.Continente ?? entidad?.Continente ?? spot.Continente,
            ModoAnunciado = spot.ModoAnunciado.EsVacio
                ? Modo.Crudo(FiltroDeSpots.ModoProbable(spot.Frecuencia))
                : spot.ModoAnunciado,
        };

        if (entidad is null) return marcado;

        var modo = marcado.ModoAnunciado;
        var novedad = await _consultas
            .ConsultarNovedadAsync(entidad.Numero, marcado.Banda, modo, ct)
            .ConfigureAwait(false);

        return marcado with
        {
            EsEntidadNueva = !novedad.Visto,
            EsNuevoEnBandaYModo = !novedad.VistoEnHueco,
        };
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _fuente.SpotRecibido -= AlLlegarUnSpot;
        _fuente.LineaRecibida -= AlLlegarUnaLinea;
        _fuente.EstadoCambiado -= AlCambiarElEstado;
        await _fuente.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Marca el spot y lo reparte.
    /// </summary>
    /// <remarks>
    /// Es un manejador de evento asincrono, de los pocos <c>async void</c> permitidos, y por eso
    /// se traga cualquier excepcion: un spot mal formado no puede tumbar el programa mientras
    /// el operador esta trabajando un pileup.
    /// </remarks>
    private async void AlLlegarUnSpot(object? origen, Spot spot)
    {
        try
        {
            var marcado = await MarcarAsync(spot).ConfigureAwait(false);
            AnuncioRecibido?.Invoke(this, _repetidos.Juntar(marcado));
        }
        catch (OperationCanceledException)
        {
            // La aplicacion se esta cerrando: no hay nada que decir.
        }
        catch (Exception)
        {
            // Si no se ha podido resolver la entidad, el spot vale igual: se ensena en crudo.
            AnuncioRecibido?.Invoke(this, _repetidos.Juntar(spot));
        }
    }

    private void AlLlegarUnaLinea(object? origen, string linea) => LineaRecibida?.Invoke(this, linea);

    private void AlCambiarElEstado(object? origen, EstadoDeConexion estado) =>
        EstadoCambiado?.Invoke(this, estado);
}
