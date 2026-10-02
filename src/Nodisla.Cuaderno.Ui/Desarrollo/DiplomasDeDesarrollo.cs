using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Dxcc;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Desarrollo;

/// <summary>
/// Diplomas calculados sobre el cuaderno en memoria, mientras no hay base de datos.
/// </summary>
/// <remarks>
/// <para>
/// El motor de verdad —<c>MotorDeDiplomas</c>, con 87 diplomas y 553.064 referencias— necesita
/// una conexion a la base del cuaderno, y la interfaz todavia trabaja contra el repositorio en
/// memoria. Esto cubre el puerto con lo que si se puede calcular de verdad desde los contactos
/// que hay: las entidades DXCC, las zonas y los prefijos. Cuando llegue la base, se cambia la
/// linea del registro de servicios y ni la pantalla ni el modelo de vista se enteran.
/// </para>
/// <para>
/// <b>Lo que no se puede calcular se dice, no se inventa.</b> IOTA, SOTA y POTA cuentan
/// referencias que el cuaderno de demostracion no guarda: su progreso sale con
/// <see cref="ProgresoDeDiploma.PorQueNoEsFirme"/> puesto, y la pantalla lo ensena junto a la
/// cifra. Ensenar un cero sin explicacion haria creer que no se ha trabajado ninguna isla.
/// </para>
/// </remarks>
public sealed class DiplomasDeDesarrollo : IDiplomas
{
    private readonly IReadOnlyList<Qso> _cuaderno;
    private readonly IResolutorDxcc _dxcc;

    /// <summary>Entidad de cada indicativo, resuelta una sola vez.</summary>
    /// <remarks>
    /// Veinte mil contactos son unos pocos miles de indicativos distintos. Resolver cada uno
    /// una vez y recordarlo convierte cinco recuentos de diploma en un trabajo despreciable.
    /// </remarks>
    private readonly Dictionary<string, EntidadDxcc?> _entidades = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Fichero donde se guardan los diplomas elegidos.</summary>
    public const string FicheroDeLaEleccion = "diplomas-elegidos.json";

    private readonly string _carpeta;
    private List<string> _mios = [];

    /// <summary>Monta el motor de desarrollo.</summary>
    /// <param name="cuaderno">Contactos sobre los que se cuenta.</param>
    /// <param name="dxcc">Resolutor de entidades, para el universo de referencias del DXCC.</param>
    /// <param name="carpeta">Carpeta de datos donde se recuerda la eleccion.</param>
    public DiplomasDeDesarrollo(IReadOnlyList<Qso> cuaderno, IResolutorDxcc dxcc, string carpeta)
    {
        _cuaderno = cuaderno ?? throw new ArgumentNullException(nameof(cuaderno));
        _dxcc = dxcc ?? throw new ArgumentNullException(nameof(dxcc));
        _carpeta = carpeta ?? throw new ArgumentNullException(nameof(carpeta));

        _mios = LeerLaEleccion();
    }

    private static readonly IReadOnlyList<Diploma> Catalogo =
    [
        new("DXCC", "DXCC — Entidades del mundo", ClaseDeDiploma.PorCampo, "ARRL", new Uri("https://www.arrl.org/dxcc")),
        new("WAC", "WAC — Todos los continentes", ClaseDeDiploma.PorCampo, "IARU", null),
        new("WAZ", "WAZ — Zonas CQ", ClaseDeDiploma.PorCampo, "CQ Magazine", null),
        new("WPX", "WPX — Prefijos", ClaseDeDiploma.PorIndicativo, "CQ Magazine", null),
        new("IOTA", "IOTA — Islas del mundo", ClaseDeDiploma.PorReferencia, "RSGB", new Uri("https://www.iota-world.org/")),
        new("SOTA", "SOTA — Cumbres", ClaseDeDiploma.PorReferencia, "SOTA MT", new Uri("https://www.sota.org.uk/")),
        new("POTA", "POTA — Parques", ClaseDeDiploma.PorReferencia, "POTA", new Uri("https://parksontheair.com/")),
    ];

    /// <summary>Variantes que se ofrecen de cada diploma.</summary>
    private static readonly IReadOnlyList<(string Variante, ClaseDeModo Clase, Banda Banda)> Variantes =
    [
        ("Mixto", ClaseDeModo.Cualquiera, Banda.Vacia),
        ("Telegrafía", ClaseDeModo.Telegrafia, Banda.Vacia),
        ("Fonía", ClaseDeModo.Fonia, Banda.Vacia),
        ("Digitales", ClaseDeModo.Digital, Banda.Vacia),
    ];

    /// <summary>Cuantas referencias pide cada diploma para el nivel de partida.</summary>
    private static readonly Dictionary<string, int> Objetivos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DXCC"] = 100,
        ["WAC"] = 6,
        ["WAZ"] = 40,
        ["WPX"] = 300,
        ["IOTA"] = 100,
        ["SOTA"] = 100,
        ["POTA"] = 50,
    };

    /// <inheritdoc />
    public Task<IReadOnlyList<Diploma>> CatalogoAsync(CancellationToken ct = default) =>
        Task.FromResult(Catalogo);

    /// <inheritdoc />
    public Task<IReadOnlyList<VarianteDeDiploma>> VariantesAsync(string codigo, CancellationToken ct = default)
    {
        IReadOnlyList<VarianteDeDiploma> variantes =
        [
            .. Variantes.Select(v => new VarianteDeDiploma(
                codigo,
                v.Variante,
                v.Banda,
                Modo.Vacio,
                ExigenciaDeConfirmacion.Confirmado,
                [],
                Objetivos.GetValueOrDefault(codigo, 100))
            {
                Clase = v.Clase,
            }),
        ];

        return Task.FromResult(variantes);
    }

    /// <inheritdoc />
    public Task<ProgresoDeDiploma> ProgresoAsync(string codigo, string variante, CancellationToken ct = default)
    {
        var clase = Variantes.FirstOrDefault(v =>
            string.Equals(v.Variante, variante, StringComparison.OrdinalIgnoreCase)).Clase;

        var cuentan = _cuaderno.Where(q => EncajaEnLaClase(q, clase)).ToList();
        var objetivo = Objetivos.GetValueOrDefault(codigo, 100);

        var (trabajadas, confirmadas, porQue) = codigo.ToUpperInvariant() is "DXCC" or "WAC" or "WAZ" or "WPX"
            ? Contar(cuentan, q => ClaveDe(codigo, q))
            : (0, 0, Textos.T("Dialogos.Simulado.SinReferencias"));

        return Task.FromResult(new ProgresoDeDiploma(
            codigo,
            variante,
            trabajadas,
            confirmadas,
            objetivo,
            DateTimeOffset.UtcNow)
        {
            PorQueNoEsFirme = porQue,
        });
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> MisDiplomasAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([.. _mios]);

    /// <inheritdoc />
    /// <remarks>
    /// La eleccion la guarda el motor, no la pantalla: quien quiera saber que diplomas sigue
    /// el operador le pregunta al puerto y no a la ventana.
    /// </remarks>
    public Task FijarMisDiplomasAsync(IReadOnlyList<string> codigos, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(codigos);

        _mios = [.. codigos];
        EscribirLaEleccion();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Con la eleccion vacia devuelve lista vacia, igual que el motor de verdad: calcular los
    /// ochenta y siete diplomas «por si acaso» es trabajo tirado.
    /// </remarks>
    public async Task<IReadOnlyList<ProgresoDeDiploma>> ProgresoDeMisDiplomasAsync(CancellationToken ct = default)
    {
        var progresos = new List<ProgresoDeDiploma>(_mios.Count);

        foreach (var clave in _mios)
        {
            ct.ThrowIfCancellationRequested();

            var trozos = clave.Split('/', 2);
            if (trozos.Length != 2) continue;

            progresos.Add(await ProgresoAsync(trozos[0], trozos[1], ct).ConfigureAwait(false));
        }

        return progresos;
    }

    private List<string> LeerLaEleccion()
    {
        try
        {
            var ruta = System.IO.Path.Combine(_carpeta, FicheroDeLaEleccion);
            if (!System.IO.File.Exists(ruta)) return [];

            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                System.IO.File.ReadAllText(ruta)) ?? [];
        }
        catch (Exception ex)
        {
            // Un fichero de ajustes roto no puede impedir abrir el cuaderno: se arranca sin
            // ningun diploma elegido, que es el estado de partida.
            Serilog.Log.Warning(ex, "No se ha podido leer la selección de diplomas.");
            return [];
        }
    }

    private void EscribirLaEleccion()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_carpeta);
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(_carpeta, FicheroDeLaEleccion),
                System.Text.Json.JsonSerializer.Serialize(_mios, JsonFormato));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "No se ha podido guardar la selección de diplomas.");
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonFormato = new() { WriteIndented = true };

    /// <inheritdoc />
    public async Task<Pagina<EstadoDeReferencia>> DetalleAsync(
        string codigo,
        string variante,
        int desplazamiento,
        int limite,
        CancellationToken ct = default)
    {
        var clase = Variantes.FirstOrDefault(v =>
            string.Equals(v.Variante, variante, StringComparison.OrdinalIgnoreCase)).Clase;

        var cuentan = _cuaderno.Where(q => EncajaEnLaClase(q, clase)).ToList();

        var universo = UniversoDe(codigo);
        var trabajadas = new Dictionary<string, (bool Confirmada, long? Qso)>(StringComparer.OrdinalIgnoreCase);

        foreach (var qso in cuentan)
        {
            var clave = ClaveDe(codigo, qso);
            if (clave is null) continue;

            var confirmada = qso.Confirmaciones.Any(c => c.EstaConfirmada);
            if (trabajadas.TryGetValue(clave, out var hay))
            {
                trabajadas[clave] = (hay.Confirmada || confirmada, hay.Qso ?? qso.Id);
            }
            else
            {
                trabajadas[clave] = (confirmada, qso.Id);
            }
        }

        var filas = universo
            .Select(r => new EstadoDeReferencia(
                r.Referencia,
                r.Nombre,
                trabajadas.ContainsKey(r.Referencia),
                trabajadas.TryGetValue(r.Referencia, out var e) && e.Confirmada,
                trabajadas.TryGetValue(r.Referencia, out var q) ? q.Qso : null))
            .ToList();

        await Task.Yield();

        var pagina = filas.Skip(Math.Max(0, desplazamiento)).Take(Math.Max(1, limite)).ToList();
        return new Pagina<EstadoDeReferencia>(pagina, filas.Count, desplazamiento);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> QueAportaAsync(
        Indicativo indicativo,
        Banda banda,
        Modo modo,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    /// <inheritdoc />
    public async Task RecalcularAsync(
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default)
    {
        var total = Catalogo.Count;
        for (var i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            progreso?.Report(new ProgresoDeSincronizacion(
                "Diplomas",
                i + 1,
                total,
                $"Recontando {Catalogo[i].Nombre}"));

            await Task.Delay(120, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Cuenta referencias distintas trabajadas y confirmadas.</summary>
    private static (int Trabajadas, int Confirmadas, string? PorQue) Contar(
        IReadOnlyList<Qso> contactos,
        Func<Qso, string?> clave)
    {
        var trabajadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var confirmadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var qso in contactos)
        {
            if (clave(qso) is not { Length: > 0 } k) continue;

            trabajadas.Add(k);
            if (qso.Confirmaciones.Any(c => c.EstaConfirmada)) confirmadas.Add(k);
        }

        // El cuaderno de demostracion no trae confirmaciones: decir «cero confirmadas» sin
        // explicarlo haria pensar que no ha llegado ninguna tarjeta en veinte mil contactos.
        var porQue = confirmadas.Count == 0 && trabajadas.Count > 0
            ? Textos.T("Dialogos.Simulado.SinConfirmaciones")
            : null;

        return (trabajadas.Count, confirmadas.Count, porQue);
    }

    /// <summary>El contacto vale para esa familia de modos.</summary>
    private static bool EncajaEnLaClase(Qso qso, ClaseDeModo clase) => clase switch
    {
        ClaseDeModo.Telegrafia => qso.Mode.Principal == "CW",
        ClaseDeModo.Fonia => qso.Mode.Principal is "SSB" or "AM" or "FM",
        ClaseDeModo.Digital => qso.Mode.Principal is not ("CW" or "SSB" or "AM" or "FM"),
        _ => true,
    };

    /// <summary>
    /// Saca del contacto la referencia que cuenta para ese diploma.
    /// </summary>
    /// <remarks>
    /// El cuaderno de demostracion no trae rellenos los campos de entidad, continente ni zona
    /// —los trae un ADIF de verdad, pero estos contactos se generan—, asi que cuando faltan se
    /// <b>resuelven del indicativo</b>, que es de donde salen tambien al importar. Sin esto la
    /// pantalla ensenaria ceros en todo y pareceria rota.
    /// </remarks>
    private string? ClaveDe(string codigo, Qso qso)
    {
        var entidad = EntidadDe(qso);

        return codigo.ToUpperInvariant() switch
        {
            "DXCC" => (qso.Dxcc > 0 ? qso.Dxcc : entidad?.Numero) is { } n and > 0
                ? n.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null,
            "WAC" => qso.Cont ?? entidad?.Continente,
            "WAZ" => (qso.Cqz ?? entidad?.ZonaCq) is { } z and > 0
                ? z.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null,
            "WPX" => qso.Pfx ?? PrefijoDe(qso.Call.Valor),
            _ => null,
        };
    }

    /// <summary>Entidad del contacto, resuelta del indicativo y recordada.</summary>
    private EntidadDxcc? EntidadDe(Qso qso)
    {
        var indicativo = qso.Call.Valor;
        if (_entidades.TryGetValue(indicativo, out var guardada)) return guardada;

        var entidad = _dxcc.Resolver(qso.Call, DateOnly.FromDateTime(qso.InicioUtc.UtcDateTime)).Entidad;
        _entidades[indicativo] = entidad;
        return entidad;
    }

    /// <summary>
    /// Prefijo WPX de un indicativo: lo que hay hasta el ultimo digito, ambos incluidos.
    /// </summary>
    /// <remarks>
    /// Es la regla de CQ simplificada, suficiente para contar: <c>EA8DLF</c> da <c>EA8</c> y
    /// <c>K1ABC</c> da <c>K1</c>. Los casos raros del reglamento —portables, barras— los
    /// resolvera el motor de verdad.
    /// </remarks>
    private static string? PrefijoDe(string indicativo)
    {
        if (string.IsNullOrWhiteSpace(indicativo)) return null;

        var limpio = indicativo.Split('/')[0];
        var ultimo = -1;
        for (var i = 0; i < limpio.Length; i++)
        {
            if (char.IsDigit(limpio[i])) ultimo = i;
        }

        return ultimo < 0 ? null : limpio[..(ultimo + 1)];
    }

    /// <summary>Todas las referencias que existen de un diploma.</summary>
    private IReadOnlyList<(string Referencia, string? Nombre)> UniversoDe(string codigo) =>
        codigo.ToUpperInvariant() switch
        {
            "DXCC" =>
            [
                .. _dxcc.Todas
                    .OrderBy(e => e.NombreParaMostrar, StringComparer.CurrentCulture)
                    .Select(e => (
                        e.Numero.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        (string?)$"{e.PrefijoPrincipal} · {e.NombreParaMostrar}")),
            ],
            "WAC" =>
            [
                ("AF", "África"), ("AN", "Antártida"), ("AS", "Asia"),
                ("EU", "Europa"), ("NA", "América del Norte"), ("OC", "Oceanía"),
                ("SA", "América del Sur"),
            ],
            "WAZ" =>
            [
                .. Enumerable.Range(1, 40).Select(z => (
                    z.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    (string?)$"Zona CQ {z}")),
            ],
            _ => [],
        };
}
