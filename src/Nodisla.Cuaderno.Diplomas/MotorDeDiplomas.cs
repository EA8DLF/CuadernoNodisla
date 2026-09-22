using System.Data.Common;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Diplomas.Cache;
using Nodisla.Cuaderno.Diplomas.Calculo;
using Nodisla.Cuaderno.Diplomas.Catalogo;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Diplomas;

/// <summary>
/// El motor de diplomas.
/// </summary>
/// <remarks>
/// <para>
/// El catalogo (87 diplomas, 390 variantes y 553.064 referencias) viaja incrustado en el
/// ensamblado y se compila una sola vez a una base SQLite indexada, que se adjunta a la conexion
/// del cuaderno con el alias <c>cat</c>. El progreso se resuelve con consultas agregadas contra
/// las tablas hijas <c>qso_confirmacion</c> y <c>qso_referencia</c>, no recorriendo el cuaderno.
/// </para>
/// <para>
/// Lo que se cachea es el resultado; la verdad sigue estando en la base. La cache se tira en
/// cuanto la aplicacion avisa de un cambio y, por si el cambio viene de fuera, tambien cuando la
/// marca de agua del cuaderno no coincide con la que se uso para calcular.
/// </para>
/// <para>
/// Un diploma mal calculado es peor que no calcularlo. Cuando el motor no puede aplicar una
/// regla tal cual, cuenta de menos y deja dicho por que en
/// <see cref="ProgresoDeDiploma.PorQueNoEsFirme"/> y en el diario.
/// </para>
/// </remarks>
/// <param name="opciones">Configuracion del motor.</param>
/// <param name="fabrica">De donde salen las conexiones al cuaderno.</param>
/// <param name="resolutor">Resolutor de entidades DXCC del dominio.</param>
/// <param name="registro">Diario de la aplicacion.</param>
public sealed class MotorDeDiplomas(
    OpcionesDeDiplomas opciones,
    IFabricaDeConexion fabrica,
    IResolutorDxcc resolutor,
    ILogger<MotorDeDiplomas>? registro = null) : IDiplomas, INotificadorDeDiplomas, IAsyncDisposable
{
    private readonly ILogger _registro = registro ?? NullLogger<MotorDeDiplomas>.Instance;
    private readonly SemaphoreSlim _puerta = new(1, 1);

    private readonly Dictionary<string, ProgresoDeDiploma> _progresos =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ConjuntoDeVariante> _conjuntos =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IReadOnlyList<int>> _largosDePrefijo =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _columnasDelCuaderno = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, bool> _universosVacios =
        new(StringComparer.OrdinalIgnoreCase);

    private DbConnection? _conexion;
    private CatalogoDeDiplomas? _catalogo;
    private MarcaDeAgua _marca = MarcaDeAgua.Ninguna;
    private DateTimeOffset _comprobadaUtc = DateTimeOffset.MinValue;
    private volatile bool _invalidado = true;
    private List<string>? _misDiplomas;

    /// <summary>Cifras de la ultima compilacion del catalogo, si hubo que compilarlo.</summary>
    public ResumenDelCatalogo? UltimaCompilacion { get; private set; }

    // ── catalogo ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Deja el motor listo: lee el recurso, compila el catalogo si hace falta y abre la conexion
    /// con el cuaderno. Conviene llamarlo al arrancar para que la primera consulta del operador
    /// no pague la compilacion.
    /// </summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>La tarea de la preparacion.</returns>
    public async Task PrepararAsync(CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AsegurarCatalogoSinPuertaAsync(ct).ConfigureAwait(false);
            await AsegurarConexionAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Diploma>> CatalogoAsync(CancellationToken ct = default)
    {
        var catalogo = await AsegurarCatalogoAsync(ct).ConfigureAwait(false);
        return catalogo.Diplomas.Values
            .OrderBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase)
            .Select(ADiploma)
            .ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VarianteDeDiploma>> VariantesAsync(
        string codigo, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);

        var catalogo = await AsegurarCatalogoAsync(ct).ConfigureAwait(false);
        if (!catalogo.Diplomas.TryGetValue(codigo, out var premio) ||
            !catalogo.Variantes.TryGetValue(codigo, out var variantes))
        {
            return [];
        }

        return variantes
            .OrderBy(v => v.Variante, StringComparer.OrdinalIgnoreCase)
            .Select(v => AVariante(premio, v))
            .ToList();
    }

    /// <summary>
    /// Dice por que la cifra de una variante no es firme, o nulo si lo es.
    /// </summary>
    /// <remarks>
    /// Es lo mismo que viaja en <see cref="ProgresoDeDiploma.PorQueNoEsFirme"/>. Se deja
    /// accesible aparte para poder preguntarlo sin calcular el progreso.
    /// </remarks>
    /// <param name="codigo">Diploma.</param>
    /// <param name="variante">Variante.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El motivo, en castellano, o nulo.</returns>
    public async Task<string?> PorQueNoEsFirmeAsync(
        string codigo, string variante, CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AsegurarConexionAsync(ct).ConfigureAwait(false);
            var reglas = await BuscarReglasSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false);
            return reglas is null
                ? "El diploma o la variante no están en el catálogo."
                : PorQueNoEsFirme(reglas);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <summary>
    /// Por que la cifra no es firme. Nulo significa que se puede usar para pedir el diploma.
    /// </summary>
    private string? PorQueNoEsFirme(ReglasDeVariante reglas)
    {
        if (!reglas.Premio.Calculable)
        {
            return reglas.Premio.MotivoNoCalculable is { } motivo
                ? $"No se calcula: {motivo}."
                : "No se calcula: el diploma cuenta por algo que el cuaderno no guarda.";
        }

        if (FaltaAlgunaColumna(reglas.Premio) is { } columna)
        {
            return $"No se calcula: este cuaderno todavía no guarda la columna «{columna}», " +
                   "que es de donde sale la referencia de este diploma.";
        }

        var avisos = new List<string>(reglas.Avisos);

        if (reglas.Premio.ValidaElGestor && reglas.Premio.MediosValidos.Count == 0)
        {
            avisos.Add(
                "Este diploma lo valida su gestor con su propia base de datos, que el cuaderno " +
                "no puede consultar: se cuenta lo confirmado por cualquier vía, que es una cota " +
                "inferior de lo que en realidad tienes.");
        }

        if (ConsultasDeProgreso.TieneUniverso(reglas) && UniversoVacio(reglas.Premio.Codigo))
        {
            avisos.Add(
                "El catálogo no trae ninguna referencia de este diploma, así que el progreso " +
                "sale a cero aunque lo tengas trabajado.");
        }

        return avisos.Count == 0 ? null : string.Join(" ", avisos);
    }

    /// <summary>Primera columna que el diploma necesita y el cuaderno no tiene, o nulo.</summary>
    private string? FaltaAlgunaColumna(PremioDelCatalogo premio)
    {
        if (_columnasDelCuaderno.Count == 0) return null;

        foreach (var columna in ConsultasDeProgreso.ColumnasQueNecesita(premio))
        {
            if (!_columnasDelCuaderno.Contains(columna)) return columna;
        }
        return null;
    }

    private bool UniversoVacio(string codigo) =>
        _universosVacios.TryGetValue(codigo, out var vacio) && vacio;

    // ── progreso ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<ProgresoDeDiploma> ProgresoAsync(
        string codigo, string variante, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        ArgumentException.ThrowIfNullOrWhiteSpace(variante);

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AsegurarVigenciaAsync(ct).ConfigureAwait(false);
            return await ProgresoSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ProgresoDeDiploma>> ProgresoDeMisDiplomasAsync(
        CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AsegurarVigenciaAsync(ct).ConfigureAwait(false);

            var resultado = new List<ProgresoDeDiploma>();
            foreach (var clave in MisDiplomas())
            {
                var (codigo, variante) = Partir(clave);
                resultado.Add(await ProgresoSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false));
            }
            return resultado;
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<Pagina<EstadoDeReferencia>> DetalleAsync(
        string codigo,
        string variante,
        int desplazamiento,
        int limite,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        ArgumentException.ThrowIfNullOrWhiteSpace(variante);
        ArgumentOutOfRangeException.ThrowIfNegative(desplazamiento);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limite);

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AsegurarVigenciaAsync(ct).ConfigureAwait(false);

            var reglas = await BuscarReglasSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false);
            if (reglas is null || !SeConsulta(reglas))
            {
                return new Pagina<EstadoDeReferencia>([], 0, desplazamiento);
            }

            var conexion = await AsegurarConexionAsync(ct).ConfigureAwait(false);
            await AjustarPrefijosAsync(reglas, conexion, ct).ConfigureAwait(false);

            var total = await conexion.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    ConsultasDeProgreso.TotalDelDetalle(reglas), cancellationToken: ct))
                .ConfigureAwait(false);

            var filas = await conexion.QueryAsync<FilaDeDetalle>(
                new CommandDefinition(
                    ConsultasDeProgreso.Detalle(reglas, desplazamiento, limite), cancellationToken: ct))
                .ConfigureAwait(false);

            var elementos = filas
                .Select(f => new EstadoDeReferencia(
                    f.Referencia ?? string.Empty,
                    f.Nombre,
                    f.Trabajada != 0,
                    f.Confirmada != 0,
                    f.PrimerQsoId))
                .ToList();

            return new Pagina<EstadoDeReferencia>(elementos, total, desplazamiento);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> QueAportaAsync(
        Indicativo indicativo,
        Banda banda,
        Modo modo,
        CancellationToken ct = default)
    {
        if (indicativo.EsVacio) return [];

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AsegurarVigenciaAsync(ct).ConfigureAwait(false);
            await AsegurarConjuntosAsync(ct).ConfigureAwait(false);

            var identificado = resolutor.Resolver(indicativo, DateOnly.FromDateTime(DateTime.UtcNow));
            var avisos = new List<string>();

            foreach (var conjunto in _conjuntos.Values)
            {
                ct.ThrowIfCancellationRequested();
                if (!conjunto.Reglas.AdmiteHueco(banda, modo)) continue;

                var valor = conjunto.Reglas.Premio.Clase == ClaseDeDiploma.PorIndicativo
                    ? CoincidenciaDeIndicativo.Buscar(conjunto.Patrones, indicativo)
                    : ValorDelCampo(conjunto.Reglas.Premio, indicativo, identificado);
                if (string.IsNullOrEmpty(valor)) continue;

                if (!conjunto.Trabajadas.Contains(valor))
                {
                    avisos.Add(Mensaje(conjunto.Reglas, valor, identificado, nueva: true, banda));
                }
                else if (!conjunto.Confirmadas.Contains(valor))
                {
                    avisos.Add(Mensaje(conjunto.Reglas, valor, identificado, nueva: false, banda));
                }
            }

            return avisos;
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc/>
    public async Task RecalcularAsync(
        IProgress<ProgresoDeSincronizacion>? progreso = null, CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _progresos.Clear();
            _conjuntos.Clear();
            _marca = MarcaDeAgua.Ninguna;
            _comprobadaUtc = DateTimeOffset.MinValue;
            _invalidado = false;

            var conexion = await AsegurarConexionAsync(ct).ConfigureAwait(false);
            _marca = await MarcaDeAgua.LeerAsync(conexion, ct).ConfigureAwait(false);
            _comprobadaUtc = DateTimeOffset.UtcNow;

            var claves = MisDiplomas();
            var hecho = 0;
            foreach (var clave in claves)
            {
                var (codigo, variante) = Partir(clave);
                progreso?.Report(new ProgresoDeSincronizacion("Diplomas", hecho, claves.Count, clave));
                await ProgresoSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false);
                hecho++;
            }

            await AsegurarConjuntosAsync(ct).ConfigureAwait(false);
            progreso?.Report(new ProgresoDeSincronizacion(
                "Diplomas", hecho, claves.Count, "Progreso recalculado."));
        }
        finally
        {
            _puerta.Release();
        }
    }

    // ── avisos de cambio ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public void CuadernoCambiado() => _invalidado = true;

    // ── piezas internas ──────────────────────────────────────────────────────

    /// <summary>
    /// Si el motor puede lanzar la consulta de esta variante sin que reviente. No es lo mismo
    /// que «la cifra es firme»: hay variantes que se consultan y aun asi cuentan de menos.
    /// </summary>
    private bool SeConsulta(ReglasDeVariante reglas) =>
        ConsultasDeProgreso.SePuedeCalcular(reglas) && FaltaAlgunaColumna(reglas.Premio) is null;

    private async Task<ProgresoDeDiploma> ProgresoSinPuertaAsync(
        string codigo, string variante, CancellationToken ct)
    {
        var clave = Clave(codigo, variante);
        if (_progresos.TryGetValue(clave, out var guardado)) return guardado;

        var reglas = await BuscarReglasSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false);
        if (reglas is null)
        {
            return new ProgresoDeDiploma(codigo, variante, 0, 0, null, DateTimeOffset.UtcNow);
        }

        var conexion = await AsegurarConexionAsync(ct).ConfigureAwait(false);
        await MedirUniversoAsync(reglas, conexion, ct).ConfigureAwait(false);

        if (!SeConsulta(reglas))
        {
            var motivo = PorQueNoEsFirme(reglas);
            _registro.LogWarning(
                "El diploma {Codigo}/{Variante} no se calcula: {Motivo}", codigo, variante, motivo);

            var vacio = new ProgresoDeDiploma(
                reglas.Premio.Codigo, reglas.Variante.Variante, 0, 0,
                reglas.Variante.Objetivo, DateTimeOffset.UtcNow)
            {
                PorQueNoEsFirme = motivo,
            };
            _progresos[clave] = vacio;
            return vacio;
        }

        await AjustarPrefijosAsync(reglas, conexion, ct).ConfigureAwait(false);
        var recuento = await conexion.QuerySingleAsync<RecuentoDeVariante>(
            new CommandDefinition(ConsultasDeProgreso.Recuento(reglas), cancellationToken: ct))
            .ConfigureAwait(false);

        var progreso = new ProgresoDeDiploma(
            reglas.Premio.Codigo,
            reglas.Variante.Variante,
            recuento.Trabajadas,
            recuento.Confirmadas,
            reglas.Variante.Objetivo,
            DateTimeOffset.UtcNow)
        {
            PorQueNoEsFirme = PorQueNoEsFirme(reglas),
        };

        _progresos[clave] = progreso;
        return progreso;
    }

    private async Task AsegurarConjuntosAsync(CancellationToken ct)
    {
        if (_conjuntos.Count > 0) return;

        var catalogo = await AsegurarCatalogoSinPuertaAsync(ct).ConfigureAwait(false);
        var conexion = await AsegurarConexionAsync(ct).ConfigureAwait(false);

        foreach (var clave in MisDiplomas())
        {
            var (codigo, variante) = Partir(clave);
            if (!catalogo.Diplomas.TryGetValue(codigo, out var premio)) continue;

            // Solo entran los diplomas que se pueden resolver sabiendo unicamente el
            // indicativo. Un parque POTA o una cima SOTA no se deducen de un indicativo,
            // asi que no tienen nada que decir al teclear.
            if (premio.Clase == ClaseDeDiploma.PorReferencia) continue;
            if (premio.Clase == ClaseDeDiploma.PorCampo && !EsDeducibleDelIndicativo(premio.Campo)) continue;

            var reglas = await BuscarReglasSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false);
            if (reglas is null || !SeConsulta(reglas)) continue;

            await AjustarPrefijosAsync(reglas, conexion, ct).ConfigureAwait(false);

            var trabajadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var confirmadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var filas = await conexion.QueryAsync<FilaAlcanzada>(
                new CommandDefinition(
                    ConsultasDeProgreso.ReferenciasAlcanzadas(reglas), cancellationToken: ct))
                .ConfigureAwait(false);

            foreach (var fila in filas)
            {
                var valor = fila.Valor ?? string.Empty;
                if (valor.Length == 0) continue;
                trabajadas.Add(valor);
                if (fila.Confirmada != 0) confirmadas.Add(valor);
            }

            IReadOnlyList<PatronDeIndicativo> patrones = [];
            if (premio.Clase == ClaseDeDiploma.PorIndicativo)
            {
                var leidos = await conexion.QueryAsync<PatronDeIndicativo>(
                    new CommandDefinition(
                        ConsultasDeProgreso.PatronesDeIndicativo(premio.Codigo), cancellationToken: ct))
                    .ConfigureAwait(false);
                patrones = leidos.ToList();
            }

            _conjuntos[clave] = new ConjuntoDeVariante(reglas, trabajadas, confirmadas, patrones);
        }
    }

    private static bool EsDeducibleDelIndicativo(CampoDeQso campo) => campo is
        CampoDeQso.Dxcc or CampoDeQso.CqZone or CampoDeQso.ItuZone or
        CampoDeQso.Continent or CampoDeQso.Pfx;

    private static string ValorDelCampo(
        PremioDelCatalogo premio, Indicativo indicativo, ResultadoDxcc identificado)
    {
        return premio.Campo switch
        {
            CampoDeQso.Dxcc => identificado.Entidad is { } e
                ? e.Numero.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            CampoDeQso.CqZone => Zona(identificado.ZonaCq ?? identificado.Entidad?.ZonaCq),
            CampoDeQso.ItuZone => Zona(identificado.ZonaItu ?? identificado.Entidad?.ZonaItu),
            CampoDeQso.Continent => identificado.Continente ?? identificado.Entidad?.Continente ?? string.Empty,
            CampoDeQso.Pfx => PrefijoWpx.De(indicativo),
            _ => string.Empty,
        };

        static string Zona(int? zona) =>
            zona is > 0 ? zona.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string Mensaje(
        ReglasDeVariante reglas,
        string valor,
        ResultadoDxcc identificado,
        bool nueva,
        Banda banda)
    {
        var nombre = reglas.Premio.NombreParaMostrar;
        var variante = reglas.Variante.Sintetica ? string.Empty : $" ({reglas.Variante.Variante})";
        var detalle = reglas.Premio.Campo switch
        {
            CampoDeQso.Dxcc => identificado.Entidad?.NombreParaMostrar ?? valor,
            CampoDeQso.CqZone => $"zona CQ {valor}",
            CampoDeQso.ItuZone => $"zona ITU {valor}",
            CampoDeQso.Continent => $"continente {valor}",
            CampoDeQso.Pfx => $"prefijo {valor}",
            _ => valor,
        };

        var hueco = reglas.BandasEfectivas.Count == 1 && !banda.EsVacia ? $" en {banda.Nombre}" : string.Empty;

        return nueva
            ? $"{nombre}{variante}: {detalle} es nuevo{hueco}."
            : $"{nombre}{variante}: {detalle} está trabajado{hueco} pero sin confirmar.";
    }

    /// <summary>
    /// Los diplomas que el operador ha marcado como propios.
    /// </summary>
    /// <remarks>
    /// Si no ha marcado ninguno la respuesta es <b>ninguno</b>, no «todos por si acaso».
    /// Calcular los 87 del catalogo el primer dia es trabajo tirado y, peor, ensena cifras que
    /// el operador no ha pedido y que puede tomar por suyas. Que la interfaz le invite a elegir.
    /// </remarks>
    private IReadOnlyList<string> MisDiplomas() => _misDiplomas ??= [.. opciones.MisDiplomas];

    private async Task<ReglasDeVariante?> BuscarReglasAsync(
        string codigo, string variante, CancellationToken ct)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await BuscarReglasSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    private async Task<ReglasDeVariante?> BuscarReglasSinPuertaAsync(
        string codigo, string variante, CancellationToken ct)
    {
        var catalogo = await AsegurarCatalogoSinPuertaAsync(ct).ConfigureAwait(false);
        if (!catalogo.Diplomas.TryGetValue(codigo, out var premio)) return null;
        if (!catalogo.Variantes.TryGetValue(codigo, out var variantes)) return null;

        var elegida = variantes.FirstOrDefault(
            v => v.Variante.Equals(variante, StringComparison.OrdinalIgnoreCase));
        return elegida is null ? null : ReglasDeVariante.Construir(premio, elegida);
    }

    /// <summary>
    /// Mira una sola vez si el catalogo trae referencias de este diploma. Un universo vacio da
    /// un progreso de cero que parece un dato y no lo es, asi que hay que avisarlo.
    /// </summary>
    private async Task MedirUniversoAsync(
        ReglasDeVariante reglas, DbConnection conexion, CancellationToken ct)
    {
        if (!ConsultasDeProgreso.TieneUniverso(reglas)) return;
        if (_universosVacios.ContainsKey(reglas.Premio.Codigo)) return;

        var total = await conexion.ExecuteScalarAsync<int>(
            new CommandDefinition(ConsultasDeProgreso.TotalDelDetalle(reglas), cancellationToken: ct))
            .ConfigureAwait(false);

        _universosVacios[reglas.Premio.Codigo] = total == 0;
    }

    /// <summary>
    /// Pone en las reglas los largos de prefijo que de verdad tiene el diploma en el catalogo.
    /// Sin esto la consulta genera todas las ramas posibles: sale lo mismo, pero mas lento.
    /// </summary>
    private async Task AjustarPrefijosAsync(
        ReglasDeVariante reglas, DbConnection conexion, CancellationToken ct)
    {
        if (reglas.Premio.Clase != ClaseDeDiploma.PorIndicativo) return;

        if (!_largosDePrefijo.TryGetValue(reglas.Premio.Codigo, out var largos))
        {
            var leidos = await conexion.QueryAsync<int>(
                new CommandDefinition(
                    ConsultasDeProgreso.LargosDePrefijo(reglas.Premio.Codigo), cancellationToken: ct))
                .ConfigureAwait(false);
            largos = leidos.ToList();
            _largosDePrefijo[reglas.Premio.Codigo] = largos;
        }

        reglas.FijarLargosDePrefijo((IReadOnlyCollection<int>)largos);
    }

    private async Task AsegurarVigenciaAsync(CancellationToken ct)
    {
        if (_invalidado)
        {
            _invalidado = false;
            Limpiar();
            _marca = MarcaDeAgua.Ninguna;
            _comprobadaUtc = DateTimeOffset.MinValue;
        }

        var ahora = DateTimeOffset.UtcNow;
        if (ahora - _comprobadaUtc < opciones.IntervaloDeComprobacion) return;

        var conexion = await AsegurarConexionAsync(ct).ConfigureAwait(false);
        var marca = await MarcaDeAgua.LeerAsync(conexion, ct).ConfigureAwait(false);
        _comprobadaUtc = ahora;

        if (marca == _marca) return;

        if (_marca != MarcaDeAgua.Ninguna)
        {
            _registro.LogDebug("El cuaderno ha cambiado por detrás; se tira la caché de diplomas.");
        }

        Limpiar();
        _marca = marca;
    }

    private void Limpiar()
    {
        _progresos.Clear();
        _conjuntos.Clear();
    }

    private async Task<CatalogoDeDiplomas> AsegurarCatalogoAsync(CancellationToken ct)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await AsegurarCatalogoSinPuertaAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    private Task<CatalogoDeDiplomas> AsegurarCatalogoSinPuertaAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _catalogo ??= LectorDelRecurso.LeerCatalogo();
        return Task.FromResult(_catalogo);
    }

    private async Task<DbConnection> AsegurarConexionAsync(CancellationToken ct)
    {
        if (_conexion is not null) return _conexion;

        var catalogo = await AsegurarCatalogoSinPuertaAsync(ct).ConfigureAwait(false);
        var ruta = opciones.RutaDelCatalogoEfectiva(fabrica.RutaDelCuaderno);

        var constructor = new ConstructorDelCatalogo(resolutor);
        UltimaCompilacion = constructor.AsegurarCatalogo(
            ruta, catalogo, opciones.DiplomasIncluidos, ct) ?? UltimaCompilacion;

        var conexion = await fabrica.AbrirAsync(ct).ConfigureAwait(false);

        using (var orden = conexion.CreateCommand())
        {
            orden.CommandText = $"ATTACH DATABASE @ruta AS {ConsultasDeProgreso.AliasDelCatalogo}";
            var parametro = orden.CreateParameter();
            parametro.ParameterName = "@ruta";
            parametro.Value = ruta;
            orden.Parameters.Add(parametro);
            await orden.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // Que columnas trae de verdad el cuaderno: el catalogo puede pedir un campo que este
        // esquema todavia no tenga, y entonces ese diploma no se calcula en vez de reventar.
        _columnasDelCuaderno.Clear();
        var columnas = await conexion.QueryAsync<string>(
            new CommandDefinition("SELECT name FROM pragma_table_info('qso')", cancellationToken: ct))
            .ConfigureAwait(false);
        foreach (var columna in columnas) _columnasDelCuaderno.Add(columna);

        _conexion = conexion;
        return conexion;
    }

    // ── conversiones al contrato ─────────────────────────────────────────────

    private static Diploma ADiploma(PremioDelCatalogo premio) => new(
        premio.Codigo,
        premio.NombreParaMostrar,
        premio.Clase,
        premio.Gestor,
        Uri.TryCreate(premio.Web, UriKind.Absolute, out var web) ? web : null);

    private static VarianteDeDiploma AVariante(PremioDelCatalogo premio, VarianteDelCatalogo variante)
    {
        var reglas = ReglasDeVariante.Construir(premio, variante);
        var banda = reglas.BandasEfectivas.Count == 1
            ? Banda.Parse(reglas.BandasEfectivas[0])
            : Banda.Vacia;

        // El modo ADIF solo se rellena cuando la variante nombra uno concreto; las que hablan
        // de familias —fonia, telegrafia, digitales— viajan en Clase, que es lo que dice el
        // reglamento. Poner «SSB» en una variante de fonia dejaria fuera AM y FM.
        var modo = variante.Modos.Count == 1 ? Modo.Crudo(variante.Modos[0]) : Modo.Vacio;

        return new VarianteDeDiploma(
            premio.Codigo,
            variante.Variante,
            banda,
            modo,
            premio.Exigencia,
            premio.MediosValidos,
            variante.Objetivo)
        {
            Clase = variante.Clase,
        };
    }

    private static string Clave(string codigo, string variante) => $"{codigo}/{variante}";

    private static (string Codigo, string Variante) Partir(string clave)
    {
        var corte = clave.IndexOf('/', StringComparison.Ordinal);
        return corte < 0 ? (clave, string.Empty) : (clave[..corte], clave[(corte + 1)..]);
    }

    /// <summary>Cierra la conexion con el cuaderno.</summary>
    /// <returns>La tarea del cierre.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_conexion is not null)
        {
            await _conexion.DisposeAsync().ConfigureAwait(false);
            _conexion = null;
        }
        _puerta.Dispose();
    }

    private sealed record ConjuntoDeVariante(
        ReglasDeVariante Reglas,
        HashSet<string> Trabajadas,
        HashSet<string> Confirmadas,
        IReadOnlyList<PatronDeIndicativo> Patrones);

    private sealed class FilaDeDetalle
    {
        public string? Referencia { get; init; }
        public string? Nombre { get; init; }
        public long Trabajada { get; init; }
        public long Confirmada { get; init; }
        public long? PrimerQsoId { get; init; }
    }

    private sealed class FilaAlcanzada
    {
        public string? Valor { get; init; }
        public long Confirmada { get; init; }
        public long? PrimerQsoId { get; init; }
    }
}
