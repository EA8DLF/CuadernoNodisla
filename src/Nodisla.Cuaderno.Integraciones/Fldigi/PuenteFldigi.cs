using System.Text;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Integraciones.Fldigi;

/// <summary>Como esta el transceptor segun FLDigi.</summary>
public enum EstadoDeTransmision
{
    /// <summary>FLDigi no ha dicho nada que se entienda.</summary>
    Desconocido,

    /// <summary>Recibiendo.</summary>
    Recibiendo,

    /// <summary>Transmitiendo.</summary>
    Transmitiendo,

    /// <summary>Sintonizando la antena con portadora.</summary>
    Sintonizando,
}

/// <summary>Donde escucha el servidor XML-RPC de FLDigi.</summary>
public sealed record OpcionesFldigi
{
    /// <summary>Maquina donde corre FLDigi.</summary>
    public string Servidor { get; init; } = "127.0.0.1";

    /// <summary>
    /// Puerto del servidor XML-RPC. El 7362 es el que trae FLDigi de fabrica.
    /// </summary>
    public int Puerto { get; init; } = 7362;

    /// <summary>Ruta del servidor. FLDigi atiende en <c>/RPC2</c>.</summary>
    public string Ruta { get; init; } = "/RPC2";

    /// <summary>Tiempo maximo de cada llamada.</summary>
    public TimeSpan Espera { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Cada cuanto se le pregunta a FLDigi por el texto recibido y por su estado.
    /// </summary>
    /// <remarks>
    /// FLDigi no avisa por su cuenta. Un cuarto de segundo va sobrado para texto tecleado y
    /// no carga nada: son dos llamadas locales.
    /// </remarks>
    public TimeSpan Sondeo { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Direccion completa del servidor.</summary>
    public Uri Direccion => new($"http://{Servidor}:{Puerto}{Ruta}");
}

/// <summary>
/// Puente con FLDigi por XML-RPC: frecuencia, modo, texto recibido, texto a transmitir y
/// estado del transmisor.
/// </summary>
/// <remarks>
/// FLDigi no avisa de nada por su cuenta: hay que preguntarle. El texto recibido se lee con
/// <c>rx.get_data</c>, que devuelve solo lo nuevo desde la ultima vez, asi que quien lo use
/// tiene que llamar a intervalos regulares y guardar lo que le den.
/// </remarks>
public sealed class PuenteFldigi : IAsyncDisposable
{
    private readonly ClienteXmlRpc _rpc;

    /// <summary>Crea el puente.</summary>
    /// <param name="opciones">Donde escucha FLDigi.</param>
    /// <param name="http">Cliente HTTP a reutilizar. Si no se da, se crea uno propio.</param>
    public PuenteFldigi(OpcionesFldigi? opciones = null, System.Net.Http.HttpClient? http = null)
    {
        Opciones = opciones ?? new OpcionesFldigi();
        _rpc = new ClienteXmlRpc(Opciones.Direccion, Opciones.Espera, http);
    }

    /// <summary>Donde escucha FLDigi.</summary>
    public OpcionesFldigi Opciones { get; }

    /// <summary>Version de FLDigi, que sirve para saber si esta ahi.</summary>
    public async Task<string> VersionAsync(CancellationToken ct = default) =>
        Texto(await _rpc.LlamarAsync("fldigi.version", null, ct).ConfigureAwait(false));

    /// <summary>Comprueba si FLDigi responde.</summary>
    public async Task<bool> RespondeAsync(CancellationToken ct = default)
    {
        try
        {
            await VersionAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (ErrorXmlRpc)
        {
            return false;
        }
    }

    /// <summary>Lee la frecuencia del dial.</summary>
    /// <remarks>FLDigi la da en hercios, no en kilohercios como los clusters.</remarks>
    public async Task<Frecuencia> LeerFrecuenciaAsync(CancellationToken ct = default)
    {
        var valor = await _rpc.LlamarAsync("main.get_frequency", null, ct).ConfigureAwait(false);
        return Frecuencia.DesdeHercios((long)Math.Round(Doble(valor)));
    }

    /// <summary>Pone la frecuencia del dial y devuelve la que habia antes.</summary>
    public async Task<Frecuencia> PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default)
    {
        var anterior = await _rpc
            .LlamarAsync("main.set_frequency", [(double)frecuencia.Hercios], ct)
            .ConfigureAwait(false);
        return Frecuencia.DesdeHercios((long)Math.Round(Doble(anterior)));
    }

    /// <summary>Nombre del modem que tiene FLDigi puesto, por ejemplo <c>BPSK31</c>.</summary>
    public async Task<string> LeerModemAsync(CancellationToken ct = default) =>
        Texto(await _rpc.LlamarAsync("modem.get_name", null, ct).ConfigureAwait(false));

    /// <summary>Modo ADIF que corresponde al modem que tiene FLDigi puesto.</summary>
    public async Task<Modo> LeerModoAsync(CancellationToken ct = default) =>
        ModoDeModem(await LeerModemAsync(ct).ConfigureAwait(false));

    /// <summary>Todos los modems que conoce esta instalacion de FLDigi.</summary>
    public async Task<IReadOnlyList<string>> ListarModemsAsync(CancellationToken ct = default)
    {
        var valor = await _rpc.LlamarAsync("modem.get_names", null, ct).ConfigureAwait(false);
        if (valor is not object?[] lista) return [];
        return lista.Select(v => v?.ToString() ?? string.Empty).Where(v => v.Length > 0).ToArray();
    }

    /// <summary>Pone un modem por su nombre de FLDigi.</summary>
    public async Task PonerModemAsync(string modem, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modem);
        await _rpc.LlamarAsync("modem.set_by_name", [modem.Trim()], ct).ConfigureAwait(false);
    }

    /// <summary>Pone el modem que corresponde a un modo ADIF.</summary>
    public Task PonerModoAsync(Modo modo, CancellationToken ct = default) =>
        PonerModemAsync(ModemDeModo(modo), ct);

    /// <summary>
    /// Lee el texto recibido desde la ultima llamada.
    /// </summary>
    /// <remarks>
    /// <c>rx.get_data</c> devuelve solo lo nuevo y vacia el buffer, asi que lo que no se
    /// recoja se pierde: hay que llamar a intervalos regulares.
    /// </remarks>
    public async Task<string> LeerRecibidoAsync(CancellationToken ct = default) =>
        Texto(await _rpc.LlamarAsync("rx.get_data", null, ct).ConfigureAwait(false));

    /// <summary>Lee todo el texto que hay en la ventana de recepcion.</summary>
    public async Task<string> LeerTodoLoRecibidoAsync(CancellationToken ct = default)
    {
        var largo = Entero(await _rpc.LlamarAsync("text.get_rx_length", null, ct).ConfigureAwait(false));
        if (largo <= 0) return string.Empty;
        var valor = await _rpc.LlamarAsync("text.get_rx", [0, largo], ct).ConfigureAwait(false);
        return Texto(valor);
    }

    /// <summary>Anade texto a la cola de transmision.</summary>
    public async Task EnviarTextoAsync(string texto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texto);
        if (texto.Length == 0) return;
        await _rpc.LlamarAsync("text.add_tx", [texto], ct).ConfigureAwait(false);
    }

    /// <summary>Vacia la cola de transmision.</summary>
    public async Task VaciarTextoAsync(CancellationToken ct = default) =>
        await _rpc.LlamarAsync("text.clear_tx", null, ct).ConfigureAwait(false);

    /// <summary>Pasa a transmision.</summary>
    public async Task TransmitirAsync(CancellationToken ct = default) =>
        await _rpc.LlamarAsync("main.tx", null, ct).ConfigureAwait(false);

    /// <summary>Vuelve a recepcion cuando termine de mandar lo que tiene en cola.</summary>
    public async Task RecibirAsync(CancellationToken ct = default) =>
        await _rpc.LlamarAsync("main.rx", null, ct).ConfigureAwait(false);

    /// <summary>Corta la transmision en seco, sin mandar lo que queda.</summary>
    public async Task AbortarAsync(CancellationToken ct = default) =>
        await _rpc.LlamarAsync("main.abort", null, ct).ConfigureAwait(false);

    /// <summary>Estado del transceptor.</summary>
    public async Task<EstadoDeTransmision> LeerEstadoAsync(CancellationToken ct = default)
    {
        var estado = Texto(await _rpc.LlamarAsync("main.get_trx_state", null, ct).ConfigureAwait(false));
        return estado.Trim().ToUpperInvariant() switch
        {
            "RX" => EstadoDeTransmision.Recibiendo,
            "TX" => EstadoDeTransmision.Transmitiendo,
            "TUNE" => EstadoDeTransmision.Sintonizando,
            _ => EstadoDeTransmision.Desconocido,
        };
    }

    /// <summary>Cierra el puente.</summary>
    public ValueTask DisposeAsync()
    {
        _rpc.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Nombre del modem de FLDigi para un modo ADIF.
    /// </summary>
    /// <remarks>
    /// FLDigi no habla ADIF: llama <c>BPSK31</c> a lo que ADIF llama <c>PSK31</c> y no
    /// distingue SSB de banda lateral, que para el son dos modems distintos. Lo que no esta
    /// en la tabla se manda tal cual, porque el nombre suele coincidir.
    /// </remarks>
    public static string ModemDeModo(Modo modo)
    {
        if (modo.EsVacio) return "NULL";
        var nombre = modo.NombreUsual.ToUpperInvariant();
        return nombre switch
        {
            "PSK31" => "BPSK31",
            "PSK63" => "BPSK63",
            "PSK125" => "BPSK125",
            "PSK250" => "BPSK250",
            "PSK500" => "BPSK500",
            "SSB" => "USB",
            "LSB" => "LSB",
            "USB" => "USB",
            _ => nombre,
        };
    }

    /// <summary>Modo ADIF que corresponde a un modem de FLDigi.</summary>
    public static Modo ModoDeModem(string? modem)
    {
        if (string.IsNullOrWhiteSpace(modem)) return Modo.Vacio;
        var nombre = modem.Trim().ToUpperInvariant();

        var adif = nombre switch
        {
            "BPSK31" => "PSK31",
            "BPSK63" => "PSK63",
            "BPSK125" => "PSK125",
            "BPSK250" => "PSK250",
            "BPSK500" => "PSK500",
            "USB" or "LSB" => "SSB",
            "NULL" => string.Empty,
            _ => nombre,
        };

        return Modo.TryParse(adif, null, out var modo) ? modo : Modo.Vacio;
    }

    private static string Texto(object? valor) => valor switch
    {
        null => string.Empty,
        string s => s,
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        _ => valor.ToString() ?? string.Empty,
    };

    private static double Doble(object? valor) => valor switch
    {
        double d => d,
        int i => i,
        string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        _ => 0d,
    };

    private static int Entero(object? valor) => valor switch
    {
        int i => i,
        double d => (int)Math.Round(d),
        string s when int.TryParse(s, out var i) => i,
        _ => 0,
    };
}
