using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Servicios.Consulta;

/// <summary>
/// Consulta de indicativos en cadena (QRZ.com primero y HamQTH de reserva) con cache en
/// memoria y en disco.
/// </summary>
/// <remarks>
/// <para>
/// <b>La cache dura 24 horas.</b> En un pileup se consulta el mismo indicativo al teclearlo,
/// al registrarlo y otra vez al completarlo; sin cache serian tres peticiones a QRZ.com por
/// contacto, y QRZ limita a los programas que preguntan de mas. En disco va un fichero JSON
/// por indicativo, de modo que un reinicio del programa no vuelve a preguntar lo de hoy.
/// </para>
/// <para>
/// Los «no encontrado» se recuerdan solo en memoria: un indicativo recien expedido puede
/// aparecer en QRZ manana.
/// </para>
/// <para>
/// Las fuentes se piden cada vez a <c>fuentes</c> y no se guardan: si el operador escribe su
/// usuario de QRZ en Ajustes con el programa abierto, la siguiente consulta ya lo usa.
/// </para>
/// </remarks>
public sealed class ConsultaConCache : IConsultaIndicativo
{
    /// <summary>Lo que vale una ficha guardada.</summary>
    public static readonly TimeSpan Caducidad = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = false };

    private readonly Func<IReadOnlyList<IConsultaIndicativo>> _fuentes;
    private readonly string? _carpeta;
    private readonly Func<DateTimeOffset> _ahora;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, (FichaIndicativo? Ficha, DateTimeOffset Cuando)> _memoria =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Crea la consulta.</summary>
    /// <param name="fuentes">Servicios a los que preguntar, por orden de preferencia.</param>
    /// <param name="carpeta">Carpeta de la cache en disco; nula para no guardar en disco.</param>
    /// <param name="ahora">Reloj, sustituible en las pruebas.</param>
    /// <param name="log">Registro de trazas.</param>
    public ConsultaConCache(
        Func<IReadOnlyList<IConsultaIndicativo>> fuentes,
        string? carpeta,
        Func<DateTimeOffset>? ahora = null,
        ILogger<ConsultaConCache>? log = null)
    {
        _fuentes = fuentes ?? throw new ArgumentNullException(nameof(fuentes));
        _carpeta = carpeta;
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? NullLogger<ConsultaConCache>.Instance;
    }

    /// <inheritdoc />
    public string Nombre
    {
        get
        {
            var disponibles = Disponibles();
            return disponibles.Count == 0 ? "QRZ.com" : string.Join(" / ", disponibles.Select(f => f.Nombre));
        }
    }

    /// <inheritdoc />
    public bool EstaDisponible => Disponibles().Count > 0;

    /// <inheritdoc />
    public async Task<FichaIndicativo?> ConsultarAsync(Indicativo indicativo, CancellationToken ct = default)
    {
        if (indicativo.EsVacio) return null;
        var clave = indicativo.Valor.ToUpperInvariant();
        var ahora = _ahora();

        if (_memoria.TryGetValue(clave, out var enMemoria) && ahora - enMemoria.Cuando < Caducidad)
        {
            return enMemoria.Ficha;
        }

        if (LeerDeDisco(clave, ahora) is { } deDisco)
        {
            _memoria[clave] = (deDisco, deDisco.ConsultadoUtc);
            return deDisco;
        }

        Exception? ultimoFallo = null;
        var alguienContesto = false;
        foreach (var fuente in Disponibles())
        {
            try
            {
                var ficha = await fuente.ConsultarAsync(indicativo, ct).ConfigureAwait(false);
                alguienContesto = true;
                if (ficha is null) continue;

                ficha = ficha with { ConsultadoUtc = ahora };
                _memoria[clave] = (ficha, ahora);
                GuardarEnDisco(clave, ficha);
                return ficha;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Se prueba con la siguiente: para eso esta HamQTH.
                ultimoFallo = ex;
                _log.LogInformation("{Servicio} no ha contestado por {Indicativo}: {Motivo}",
                    fuente.Nombre, indicativo.Valor, ex.Message);
            }
        }

        if (alguienContesto)
        {
            _memoria[clave] = (null, ahora);
            return null;
        }

        if (ultimoFallo is not null) throw ultimoFallo;
        return null;
    }

    private List<IConsultaIndicativo> Disponibles()
    {
        try
        {
            return _fuentes().Where(f => f.EstaDisponible).ToList();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se han podido montar los servicios de consulta.");
            return [];
        }
    }

    private string? Ruta(string clave)
    {
        if (_carpeta is null) return null;
        var limpio = string.Concat(clave.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        return Path.Combine(_carpeta, limpio + ".json");
    }

    private FichaIndicativo? LeerDeDisco(string clave, DateTimeOffset ahora)
    {
        var ruta = Ruta(clave);
        if (ruta is null || !File.Exists(ruta)) return null;
        try
        {
            var guardada = JsonSerializer.Deserialize<FichaGuardada>(File.ReadAllText(ruta), Formato);
            if (guardada is null || ahora - guardada.ConsultadoUtc >= Caducidad) return null;
            return guardada.AFicha();
        }
        catch (Exception ex)
        {
            _log.LogInformation("Ficha en cache ilegible, se vuelve a pedir: {Motivo}", ex.Message);
            return null;
        }
    }

    private void GuardarEnDisco(string clave, FichaIndicativo ficha)
    {
        var ruta = Ruta(clave);
        if (ruta is null) return;
        try
        {
            Directory.CreateDirectory(_carpeta!);
            var temporal = ruta + ".nuevo";
            File.WriteAllText(temporal, JsonSerializer.Serialize(FichaGuardada.De(ficha), Formato));
            File.Move(temporal, ruta, overwrite: true);
        }
        catch (Exception ex)
        {
            _log.LogInformation("No se ha podido guardar la ficha en la cache: {Motivo}", ex.Message);
        }
    }

    /// <summary>La ficha tal y como se escribe en disco, con tipos que JSON entiende.</summary>
    private sealed record FichaGuardada
    {
        public string Indicativo { get; init; } = string.Empty;
        public string? Nombre { get; init; }
        public string? Direccion { get; init; }
        public string? Localidad { get; init; }
        public string? Pais { get; init; }
        public string? DivisionPrimaria { get; init; }
        public string? DivisionSecundaria { get; init; }
        public string? Localizador { get; init; }
        public double? Latitud { get; init; }
        public double? Longitud { get; init; }
        public int? ZonaCq { get; init; }
        public int? ZonaItu { get; init; }
        public int? Dxcc { get; init; }
        public string? CorreoElectronico { get; init; }
        public string? Web { get; init; }
        public string? GestorQsl { get; init; }
        public string? Iota { get; init; }
        public string? Imagen { get; init; }
        public bool? UsaLotw { get; init; }
        public bool? UsaEqsl { get; init; }
        public string? Aviso { get; init; }
        public string Fuente { get; init; } = string.Empty;
        public DateTimeOffset ConsultadoUtc { get; init; }

        public static FichaGuardada De(FichaIndicativo f) => new()
        {
            Indicativo = f.Indicativo.Valor,
            Nombre = f.Nombre,
            Direccion = f.Direccion,
            Localidad = f.Localidad,
            Pais = f.Pais,
            DivisionPrimaria = f.DivisionPrimaria,
            DivisionSecundaria = f.DivisionSecundaria,
            Localizador = f.Localizador.EsVacio ? null : f.Localizador.Valor,
            Latitud = f.Latitud,
            Longitud = f.Longitud,
            ZonaCq = f.ZonaCq,
            ZonaItu = f.ZonaItu,
            Dxcc = f.Dxcc,
            CorreoElectronico = f.CorreoElectronico,
            Web = f.Web,
            GestorQsl = f.GestorQsl,
            Iota = f.Iota,
            Imagen = f.Imagen?.ToString(),
            UsaLotw = f.UsaLotw,
            UsaEqsl = f.UsaEqsl,
            Aviso = f.Aviso,
            Fuente = f.Fuente,
            ConsultadoUtc = f.ConsultadoUtc,
        };

        public FichaIndicativo AFicha() => new()
        {
            Indicativo = Nodisla.Cuaderno.Dominio.Valores.Indicativo.Crudo(Indicativo),
            Nombre = Nombre,
            Direccion = Direccion,
            Localidad = Localidad,
            Pais = Pais,
            DivisionPrimaria = DivisionPrimaria,
            DivisionSecundaria = DivisionSecundaria,
            Localizador = Locator.TryParse(Localizador, out var l) ? l : Locator.Vacio,
            Latitud = Latitud,
            Longitud = Longitud,
            ZonaCq = ZonaCq,
            ZonaItu = ZonaItu,
            Dxcc = Dxcc,
            CorreoElectronico = CorreoElectronico,
            Web = Web,
            GestorQsl = GestorQsl,
            Iota = Iota,
            Imagen = Uri.TryCreate(Imagen, UriKind.Absolute, out var u) ? u : null,
            UsaLotw = UsaLotw,
            UsaEqsl = UsaEqsl,
            Aviso = Aviso,
            Fuente = Fuente,
            ConsultadoUtc = ConsultadoUtc,
        };
    }
}
