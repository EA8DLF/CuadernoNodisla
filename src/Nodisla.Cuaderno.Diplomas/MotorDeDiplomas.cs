using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Diplomas.Cache;
using Nodisla.Cuaderno.Diplomas.Calculo;
using Nodisla.Cuaderno.Diplomas.Catalogo;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Diplomas;

/// <summary>Avisos que mantienen al dia la cache de progreso.</summary>
/// <remarks>
/// Lo implementa el motor de diplomas. Quien anade contactos o baja confirmaciones tiene que
/// avisar: la marca de agua del cuaderno es solo la red de seguridad para lo que venga de otro
/// proceso, y comprobarla tiene un coste que no se quiere pagar en cada tecla.
/// </remarks>
public interface INotificadorDeDiplomas
{
    /// <summary>Ha entrado, cambiado o desaparecido algun contacto.</summary>
    void AvisarDeCambioEnContactos();

    /// <summary>Han llegado o cambiado confirmaciones.</summary>
    void AvisarDeCambioEnConfirmaciones();

    /// <summary>Ha cambiado algo que no se sabe encuadrar; se tira toda la cache.</summary>
    void AvisarDeCambioGeneral();
}

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
/// <see cref="PorQueNoSeCalculaAsync(string, string, CancellationToken)"/> y en el diario.
/// </para>
/// </remarks>
/// <param name="opciones">Configuracion del motor.</param>
/// <param name="resolutor">Resolutor de entidades DXCC del dominio.</param>
/// <param name="registro">Diario de la aplicacion.</param>
public sealed class MotorDeDiplomas(
    OpcionesDeDiplomas opciones,
    IResolutorDxcc resolutor,
    ILogger<MotorDeDiplomas>? registro = null) : IDiplomas, INotificadorDeDiplomas, IAsyncDisposable
{
    private readonly ILogger _registro = registro ?? NullLogger<MotorDeDiplomas>.Instance;
    private readonly SemaphoreSlim _puerta = new(1, 1);

    private readonly Dictionary<string, ProgresoDeDiploma> _progresos =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ConjuntoDeVariante> _conjuntos =
        new(StringComparer.OrdinalIgnoreCase);

    private SqliteConnection? _conexion;
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
    /// Dice por que una variante no se puede calcular, o nulo si se calcula sin reservas.
    /// </summary>
    /// <param name="codigo">Diploma.</param>
    /// <param name="variante">Variante.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El motivo, en castellano, o nulo.</returns>
    public async Task<string?> PorQueNoSeCalculaAsync(
        string codigo, string variante, CancellationToken ct = default)
    {
        var reglas = await BuscarReglasAsync(codigo, variante, ct).ConfigureAwait(false);
        if (reglas is null) return "El diploma o la variante no están en el catálogo.";

        if (!ConsultasDeProgreso.SePuedeCalcular(reglas))
        {
            return reglas.Premio.MotivoNoCalculable
                ?? "El diploma cuenta por un campo del contacto que el cuaderno no guarda.";
        }

        var avisos = new List<string>(reglas.Avisos);
        if (reglas.Premio.ValidaElGestor && reglas.Premio.MediosValidos.Count == 0)
        {
            avisos.Add(
                "Este diploma lo valida su gestor con su propia base de datos, que el cuaderno " +
                "no puede consultar: se cuenta lo confirmado por cualquier vía, que es una cota " +
                "inferior de lo que en realidad tienes.");
        }

        return avisos.Count == 0 ? null : string.Join(" ", avisos);
    }

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
            foreach (var clave in await MisDiplomasAsync(ct).ConfigureAwait(false))
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
    public async Task<IReadOnlyList<EstadoDeReferencia>> DetalleAsync(
        string codigo, string variante, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(codigo);
        ArgumentException.ThrowIfNullOrWhiteSpace(variante);

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await AsegurarVigenciaAsync(ct).ConfigureAwait(false);

            var reglas = await BuscarReglasSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false);
            if (reglas is null || !ConsultasDeProgreso.SePuedeCalcular(reglas)) return [];

            var conexion = await AsegurarConexionAsync(ct).ConfigureAwait(false);
            var sql = ConsultasDeProgreso.Detalle(reglas, opciones.MaximoDeDetalle);
            var filas = await conexion.QueryAsync<FilaDeDetalle>(
                new CommandDefinition(sql, cancellationToken: ct)).ConfigureAwait(false);

            return filas
                .Select(f => new EstadoDeReferencia(
                    f.Referencia ?? string.Empty,
                    f.Nombre,
                    f.Trabajada != 0,
                    f.Confirmada != 0,
                    f.PrimerQsoId))
                .ToList();
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

            var claves = await MisDiplomasAsync(ct).ConfigureAwait(false);
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
    public void AvisarDeCambioEnContactos() => _invalidado = true;

    /// <inheritdoc/>
    public void AvisarDeCambioEnConfirmaciones() => _invalidado = true;

    /// <inheritdoc/>
    public void AvisarDeCambioGeneral() => _invalidado = true;

    // ── piezas internas ──────────────────────────────────────────────────────

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

        if (!ConsultasDeProgreso.SePuedeCalcular(reglas))
        {
            _registro.LogWarning(
                "El diploma {Codigo}/{Variante} no se calcula: {Motivo}",
                codigo, variante, reglas.Premio.MotivoNoCalculable ?? "campo no soportado");
            var vacio = new ProgresoDeDiploma(
                reglas.Premio.Codigo, reglas.Variante.Variante, 0, 0,
                reglas.Variante.Objetivo, DateTimeOffset.UtcNow);
            _progresos[clave] = vacio;
            return vacio;
        }

        var conexion = await AsegurarConexionAsync(ct).ConfigureAwait(false);
        var recuento = await conexion.QuerySingleAsync<RecuentoDeVariante>(
            new CommandDefinition(ConsultasDeProgreso.Recuento(reglas), cancellationToken: ct))
            .ConfigureAwait(false);

        var progreso = new ProgresoDeDiploma(
            reglas.Premio.Codigo,
            reglas.Variante.Variante,
            recuento.Trabajadas,
            recuento.Confirmadas,
            reglas.Variante.Objetivo,
            DateTimeOffset.UtcNow);

        _progresos[clave] = progreso;
        return progreso;
    }

    private async Task AsegurarConjuntosAsync(CancellationToken ct)
    {
        if (_conjuntos.Count > 0) return;

        var catalogo = await AsegurarCatalogoSinPuertaAsync(ct).ConfigureAwait(false);
        var conexion = await AsegurarConexionAsync(ct).ConfigureAwait(false);

        foreach (var clave in await MisDiplomasAsync(ct).ConfigureAwait(false))
        {
            var (codigo, variante) = Partir(clave);
            if (!catalogo.Diplomas.TryGetValue(codigo, out var premio)) continue;

            // Solo entran los diplomas que se pueden resolver sabiendo unicamente el
            // indicativo. Un parque POTA o una cima SOTA no se deducen de un indicativo,
            // asi que no tienen nada que decir al teclear.
            if (premio.Clase == ClaseDeDiploma.PorReferencia) continue;
            if (premio.Clase == ClaseDeDiploma.PorCampo && !EsDeducibleDelIndicativo(premio.Campo)) continue;

            var reglas = await BuscarReglasSinPuertaAsync(codigo, variante, ct).ConfigureAwait(false);
            if (reglas is null || !ConsultasDeProgreso.SePuedeCalcular(reglas)) continue;

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

    private async Task<IReadOnlyList<string>> MisDiplomasAsync(CancellationToken ct)
    {
        if (_misDiplomas is not null) return _misDiplomas;

        if (opciones.MisDiplomas.Count > 0)
        {
            _misDiplomas = opciones.MisDiplomas.ToList();
            return _misDiplomas;
        }

        // Sin seleccion del operador se toma, de cada diploma, la variante menos restrictiva:
        // es la unica eleccion que no inventa nada y sirve para que el programa diga algo util
        // desde el primer arranque.
        var catalogo = await AsegurarCatalogoSinPuertaAsync(ct).ConfigureAwait(false);
        var claves = new List<string>();

        foreach (var premio in catalogo.Diplomas.Values.OrderBy(p => p.Codigo, StringComparer.OrdinalIgnoreCase))
        {
            if (!premio.Calculable) continue;
            if (!catalogo.Variantes.TryGetValue(premio.Codigo, out var variantes)) continue;

            var elegida = variantes
                .OrderBy(Restricciones)
                .ThenBy(v => v.Variante, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (elegida is not null) claves.Add(Clave(premio.Codigo, elegida.Variante));
        }

        _misDiplomas = claves;
        return _misDiplomas;

        static int Restricciones(VarianteDelCatalogo v) =>
            v.Bandas.Count + v.Modos.Count + v.Continentes.Count +
            (string.IsNullOrEmpty(v.TipoDeEmision) ? 0 : 1) +
            (v.Anual ? 1 : 0) + (v.ExigeSatelite ? 1 : 0) + (v.ExcluyeSatelite ? 1 : 0);
    }

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

    private async Task<SqliteConnection> AsegurarConexionAsync(CancellationToken ct)
    {
        if (_conexion is not null) return _conexion;

        if (string.IsNullOrWhiteSpace(opciones.CadenaDeConexionDelCuaderno))
        {
            throw new InvalidOperationException(
                "El motor de diplomas necesita la cadena de conexión del cuaderno en " +
                $"{nameof(OpcionesDeDiplomas)}.{nameof(OpcionesDeDiplomas.CadenaDeConexionDelCuaderno)}.");
        }

        var catalogo = await AsegurarCatalogoSinPuertaAsync(ct).ConfigureAwait(false);
        var ruta = opciones.RutaDelCatalogoEfectiva();

        var constructor = new ConstructorDelCatalogo(resolutor);
        UltimaCompilacion = constructor.AsegurarCatalogo(
            ruta, catalogo, opciones.DiplomasIncluidos, ct) ?? UltimaCompilacion;

        var conexion = new SqliteConnection(opciones.CadenaDeConexionDelCuaderno);
        await conexion.OpenAsync(ct).ConfigureAwait(false);

        using (var orden = conexion.CreateCommand())
        {
            orden.CommandText = "PRAGMA busy_timeout=5000; PRAGMA query_only=ON;";
            await orden.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        using (var orden = conexion.CreateCommand())
        {
            orden.CommandText = $"ATTACH DATABASE @ruta AS {ConsultasDeProgreso.AliasDelCatalogo}";
            orden.Parameters.AddWithValue("@ruta", ruta);
            await orden.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

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

        var modo = variante.Modos.Count == 1
            ? Modo.Crudo(variante.Modos[0])
            : TipoDeEmision.Cw.Equals(variante.TipoDeEmision, StringComparison.OrdinalIgnoreCase)
                ? Modo.Crudo(TipoDeEmision.Cw)
                : Modo.Vacio;

        return new VarianteDeDiploma(
            premio.Codigo,
            variante.Variante,
            banda,
            modo,
            premio.Exigencia,
            premio.MediosValidos,
            variante.Objetivo);
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
