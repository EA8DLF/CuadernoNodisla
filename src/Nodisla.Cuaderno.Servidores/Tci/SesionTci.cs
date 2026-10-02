using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;

namespace Nodisla.Cuaderno.Servidores.Tci;

/// <summary>
/// Una conexion TCI: entiende las ordenes de la lista blanca y prepara lo que hay que mandar.
/// </summary>
/// <remarks>
/// <para>
/// TCI (Expert Electronics) son ordenes de texto <c>nombre:arg1,arg2;</c>. Aqui solo se atiende
/// una lista blanca corta: <c>vfo</c>, <c>dds</c>, <c>modulation</c>, <c>trx</c> (PTT, por el
/// vigilante), <c>split_enable</c>, <c>drive</c> (recortado a la potencia maxima de la banda),
/// las lecturas de estado y los spots. Todo lo demas se rechaza y queda anotado; en especial no
/// hay nada parecido a <c>run_cat_ex</c> ni CAT en bruto, y tampoco <c>tune</c>,
/// <c>keyer</c>/<c>cw_msg</c> ni audio.
/// </para>
/// <para>
/// El audio por TCI (<c>audio_start</c>, <c>trx:0,true,tci</c>) queda para una fase posterior:
/// meter audio de transmision que llega por la red exige pasar por el mismo orden «camino en
/// silencio → PTT → audio» que la fonia y el modem, con su vigilancia, y eso no se improvisa.
/// </para>
/// </remarks>
internal sealed class SesionTci
{
    /// <summary>Spots por segundo que se aceptan de un cliente.</summary>
    internal const int SpotsPorSegundo = 10;

    private static readonly TraductorDeModos Traductor = TraductorDeModos.PorOmision;

    private readonly RadioCompartida _radio;
    private readonly ClienteExterno _cliente;
    private readonly ILogger _registro;
    private readonly Dictionary<string, string> _ultimoEnviado = new(StringComparer.Ordinal);
    private readonly object _candado = new();
    private long _ventanaDeSpots;
    private int _spotsEnLaVentana;

    public SesionTci(RadioCompartida radio, ClienteExterno cliente, ILogger? registro = null)
    {
        _radio = radio;
        _cliente = cliente;
        _registro = registro ?? NullLogger.Instance;
    }

    /// <summary>Salta con un spot que manda el cliente, para pintarlo en el bandmap.</summary>
    public event EventHandler<Spot>? SpotRecibido;

    /// <summary>Salta cuando el cliente borra sus spots (nulo: todos).</summary>
    public event EventHandler<string?>? SpotsBorrados;

    /// <summary>Lo que se manda nada mas conectar: quien somos, limites y estado, y «ready».</summary>
    public IReadOnlyList<string> Saludo()
    {
        var mensajes = new List<string>
        {
            "protocol:ExpertSDR3,2.0;",
            "device:CuadernoNODISLA;",
            "receive_only:" + B(!_radio.Opciones.PermitirTx) + ";",
            "trx_count:1;",
            "channels_count:2;",
            "vfo_limits:30000,75000000;",
            "if_limits:-48000,48000;",
            "modulations_list:AM,LSB,USB,CW,NFM,DIGL,DIGU;",
        };
        mensajes.AddRange(Cambios(forzar: true));
        mensajes.Add("ready;");
        return mensajes;
    }

    /// <summary>
    /// Lo que ha cambiado en la radio desde lo ultimo que se le mando a este cliente.
    /// </summary>
    /// <param name="forzar">Mandarlo todo, haya cambiado o no.</param>
    public IReadOnlyList<string> Cambios(bool forzar = false)
    {
        var estado = new List<(string Clave, string Mensaje)>
        {
            ("dds", $"dds:0,{Hz(_radio.DelVfo(_radio.VfoActivo).Frecuencia)};"),
            ("vfo0", $"vfo:0,0,{Hz(_radio.DelVfo(NombreDeVfo.A).Frecuencia)};"),
            ("vfo1", $"vfo:0,1,{Hz(_radio.DelVfo(NombreDeVfo.B).Frecuencia)};"),
            ("modulation", $"modulation:0,{ModoATci(_radio.DelVfo(_radio.VfoActivo))};"),
            ("trx", $"trx:0,{B(_cliente.Transmitiendo)};"),
            ("split", $"split_enable:0,{B(_radio.Split)};"),
            ("tx_enable", $"tx_enable:0,{B(_radio.Opciones.PermitirTx)};"),
        };

        var salida = new List<string>();
        lock (_candado)
        {
            foreach (var (clave, mensaje) in estado)
            {
                if (!forzar && _ultimoEnviado.TryGetValue(clave, out var antes) && antes == mensaje) continue;
                _ultimoEnviado[clave] = mensaje;
                salida.Add(mensaje);
            }
        }

        return salida;
    }

    /// <summary>Atiende un trozo de texto del cliente (una o varias ordenes) y devuelve las respuestas.</summary>
    public async Task<IReadOnlyList<string>> AtenderAsync(string texto, CancellationToken ct = default)
    {
        var respuestas = new List<string>();
        foreach (var crudo in texto.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var orden = crudo.Trim();
            if (orden.Length == 0) continue;
            var dosPuntos = orden.IndexOf(':', StringComparison.Ordinal);
            var nombre = (dosPuntos < 0 ? orden : orden[..dosPuntos]).Trim().ToLowerInvariant();
            var argumentos = dosPuntos < 0
                ? []
                : orden[(dosPuntos + 1)..].Split(',').Select(a => a.Trim()).ToArray();

            Respuesta resultado;
            try
            {
                resultado = await AtenderUnaAsync(nombre, argumentos, respuestas, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                _registro.LogWarning(ex, "Orden TCI «{Orden}» de {Cliente} fallida.", nombre, _cliente.Nombre);
                resultado = new Respuesta(ResultadoDeOrden.Fallo, ex.Message);
            }

            _radio.Orden(_cliente, resultado);
        }

        return respuestas;
    }

    private async Task<Respuesta> AtenderUnaAsync(string nombre, string[] a, List<string> respuestas, CancellationToken ct)
    {
        switch (nombre)
        {
            case "vfo":
                {
                    if (a.Length < 2 || !Trx(a[0]) || !Canal(a[1], out var vfo)) return Invalida(nombre);
                    Respuesta r = Respuesta.Hecha;
                    if (a.Length >= 3)
                    {
                        if (!LeerHz(a[2], out var f)) return Invalida(nombre);
                        r = await _radio.PonerFrecuenciaAsync(_cliente, f, vfo, ct).ConfigureAwait(false);
                    }

                    respuestas.Add($"vfo:0,{a[1]},{Hz(_radio.DelVfo(vfo).Frecuencia)};");
                    return r;
                }

            case "dds":
                {
                    if (a.Length < 1 || !Trx(a[0])) return Invalida(nombre);
                    Respuesta r = Respuesta.Hecha;
                    if (a.Length >= 2)
                    {
                        if (!LeerHz(a[1], out var f)) return Invalida(nombre);
                        r = await _radio.PonerFrecuenciaAsync(_cliente, f, null, ct).ConfigureAwait(false);
                    }

                    respuestas.Add($"dds:0,{Hz(_radio.DelVfo(_radio.VfoActivo).Frecuencia)};");
                    return r;
                }

            case "modulation":
                {
                    if (a.Length < 1 || !Trx(a[0])) return Invalida(nombre);
                    Respuesta r = Respuesta.Hecha;
                    if (a.Length >= 2)
                    {
                        if (ModoDesdeTci(a[1]) is not { } modo) return Invalida(nombre);
                        var actual = _radio.DelVfo(_radio.VfoActivo);
                        if (!string.Equals(ModoATci(actual), ModoATci((actual.Frecuencia, modo, null)), StringComparison.Ordinal))
                        {
                            r = await _radio.PonerModoAsync(_cliente, modo, null, ct).ConfigureAwait(false);
                        }
                    }

                    respuestas.Add($"modulation:0,{ModoATci(_radio.DelVfo(_radio.VfoActivo))};");
                    return r;
                }

            case "trx":
                {
                    if (a.Length < 1 || !Trx(a[0])) return Invalida(nombre);
                    Respuesta r = Respuesta.Hecha;
                    if (a.Length >= 2)
                    {
                        if (!Booleano(a[1], out var tx)) return Invalida(nombre);
                        if (tx && a.Length >= 3 && a[2].Length > 0 && a[2].ToLowerInvariant() is not ("mic" or "mic1" or "mic2"))
                        {
                            // «trx:0,true,tci»: transmitir con audio por TCI, que no hay (fase
                            // posterior); «micpc» o «ecoder2» tampoco son caminos del Cuaderno.
                            // Sin tercer argumento, o con el micro del equipo, si.
                            r = new Respuesta(ResultadoDeOrden.NoDisponible, "audio TCI");
                            _registro.LogWarning("{Cliente} pide PTT con audio por TCI ({Fuente}): no disponible.", _cliente.Nombre, a[2]);
                        }
                        else
                        {
                            r = await _radio.PttAsync(_cliente, tx, ct).ConfigureAwait(false);
                        }
                    }

                    respuestas.Add($"trx:0,{B(_cliente.Transmitiendo)};");
                    return r;
                }

            case "split_enable":
                {
                    if (a.Length < 1 || !Trx(a[0])) return Invalida(nombre);
                    Respuesta r = Respuesta.Hecha;
                    if (a.Length >= 2)
                    {
                        if (!Booleano(a[1], out var split)) return Invalida(nombre);
                        r = await _radio.PonerSplitAsync(_cliente, split, ct).ConfigureAwait(false);
                    }

                    respuestas.Add($"split_enable:0,{B(_radio.Split)};");
                    return r;
                }

            case "drive":
                {
                    // TCI 2.0: «drive:0,50;» pone, «drive:0;» o «drive;» pregunta.
                    if (a.Length >= 1 && !Trx(a[0])) return Invalida(nombre);
                    if (_radio.PotenciaMaximaDelEquipo is not { } maximo || maximo <= 0) return new Respuesta(ResultadoDeOrden.NoDisponible);
                    Respuesta r = Respuesta.Hecha;
                    if (a.Length >= 2)
                    {
                        if (!int.TryParse(a[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var porcentaje) || porcentaje is < 0 or > 100) return Invalida(nombre);
                        r = await _radio.PonerPotenciaAsync(_cliente, porcentaje / 100.0 * maximo, ct).ConfigureAwait(false);
                    }

                    var w = await _radio.LeerPotenciaAsync(ct).ConfigureAwait(false) ?? 0;
                    respuestas.Add($"drive:0,{(int)Math.Round(Math.Clamp(w / maximo, 0, 1) * 100)};");
                    return r;
                }

            case "tx_enable":
                respuestas.Add($"tx_enable:0,{B(_radio.Opciones.PermitirTx)};");
                return Respuesta.Hecha;

            case "rx_smeter":
                {
                    // dBm: S9 = -73 dBm, 6 dB por unidad S.
                    var s = _radio.Estado.SenalRecibida ?? 0;
                    var dbm = s <= 9 ? -73 + ((s - 9) * 6) : -73 + ((s - 9) * 10);
                    var canal = a.Length >= 2 ? a[1] : "0";
                    respuestas.Add($"rx_smeter:0,{canal},{((int)Math.Round(dbm)).ToString(CultureInfo.InvariantCulture)};");
                    return Respuesta.Hecha;
                }

            case "trx_count":
                respuestas.Add("trx_count:1;");
                return Respuesta.Hecha;

            case "vfo_limits":
                respuestas.Add("vfo_limits:30000,75000000;");
                return Respuesta.Hecha;

            case "modulations_list":
                respuestas.Add("modulations_list:AM,LSB,USB,CW,NFM,DIGL,DIGU;");
                return Respuesta.Hecha;

            case "protocol":
            case "device":
                respuestas.AddRange(Saludo().Where(m => m.StartsWith(nombre + ":", StringComparison.Ordinal)));
                return Respuesta.Hecha;

            case "spot":
                return RecibirSpot(a);

            case "spot_delete":
                if (a.Length < 1 || a[0].Length is 0 or > 20) return Invalida(nombre);
                SpotsBorrados?.Invoke(this, Indicativo.Normalizar(a[0]));
                return Respuesta.Hecha;

            case "spot_clear":
                SpotsBorrados?.Invoke(this, null);
                return Respuesta.Hecha;

            default:
                _registro.LogWarning("{Cliente} manda la orden TCI «{Orden}», que no esta en la lista blanca: rechazada.", _cliente.Nombre, nombre);
                return new Respuesta(ResultadoDeOrden.Rechazado, nombre);
        }
    }

    private Respuesta Invalida(string nombre)
    {
        _registro.LogInformation("Orden TCI «{Orden}» de {Cliente} con argumentos que no se entienden.", nombre, _cliente.Nombre);
        return new Respuesta(ResultadoDeOrden.Invalido);
    }

    /// <summary>«spot:EA8DLF,cw,7012000,4294967295,texto;» → un spot para el bandmap.</summary>
    private Respuesta RecibirSpot(string[] a)
    {
        if (a.Length < 3) return Invalida("spot");
        var indicativo = Indicativo.Normalizar(a[0]);
        if (indicativo.Length is < 3 or > 20 || !indicativo.All(c => char.IsAsciiLetterOrDigit(c) || c == '/')) return Invalida("spot");
        if (!LeerHz(a[2], out var frecuencia)) return Invalida("spot");

        // Como mucho unos pocos por segundo y cliente: un bucle descontrolado no llena el bandmap.
        var ahora = Environment.TickCount64 / 1000;
        lock (_candado)
        {
            if (ahora != _ventanaDeSpots)
            {
                _ventanaDeSpots = ahora;
                _spotsEnLaVentana = 0;
            }

            if (++_spotsEnLaVentana > SpotsPorSegundo) return new Respuesta(ResultadoDeOrden.Rechazado, "spot");
        }

        if (!_radio.Opciones.RecibirSpots) return new Respuesta(ResultadoDeOrden.Rechazado, "spot");

        var texto = a.Length >= 5 ? string.Join(",", a[4..]) : null;
        if (texto is not null)
        {
            texto = new string(texto.Where(c => !char.IsControl(c)).Take(80).ToArray()).Trim();
            if (texto.Length == 0) texto = null;
        }

        var modo = ModoDesdeTci(a[1]) ?? Modo.Vacio;
        var spot = new Spot(
            Indicativo.Crudo(indicativo),
            frecuencia,
            Indicativo.Vacio,
            texto,
            DateTimeOffset.UtcNow,
            "TCI " + _cliente.Remoto.Address)
        {
            ModoAnunciado = modo,
        };
        SpotRecibido?.Invoke(this, spot);
        return Respuesta.Hecha;
    }

    /// <summary>Un spot del Cuaderno como mensaje TCI.</summary>
    internal static string MensajeDeSpot(Spot spot)
    {
        var modo = spot.ModoAnunciado.EsVacio ? "usb" : ModoATci((spot.Frecuencia, spot.ModoAnunciado, null));
        var texto = new string((spot.Comentario ?? string.Empty).Where(c => !char.IsControl(c) && c is not ';' and not ',' and not ':').Take(60).ToArray());
        return $"spot:{spot.Indicativo.Valor},{modo},{Hz(spot.Frecuencia)},4294967295,{texto};";
    }

    // ── Utilidades ──────────────────────────────────────────────────────────────────────

    private static string B(bool valor) => valor ? "true" : "false";

    private static string Hz(Frecuencia f) => f.Hercios.ToString(CultureInfo.InvariantCulture);

    private static bool Trx(string texto) => texto == "0";

    private static bool Canal(string texto, out NombreDeVfo vfo)
    {
        vfo = texto == "1" ? NombreDeVfo.B : NombreDeVfo.A;
        return texto is "0" or "1";
    }

    private static bool Booleano(string texto, out bool valor)
    {
        valor = texto.Equals("true", StringComparison.OrdinalIgnoreCase) || texto == "1";
        return valor || texto.Equals("false", StringComparison.OrdinalIgnoreCase) || texto == "0";
    }

    private static bool LeerHz(string texto, out Frecuencia frecuencia)
    {
        frecuencia = Frecuencia.Cero;
        if (!double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var hz) || !double.IsFinite(hz) || hz < 30_000 || hz > 2e9) return false;
        frecuencia = Frecuencia.DesdeHercios((long)Math.Round(hz));
        return true;
    }

    /// <summary>Del modo del Cuaderno al de TCI.</summary>
    internal static string ModoATci((Frecuencia Frecuencia, Modo Modo, int? Ancho) vfo)
    {
        var nombre = Traductor.AlEquipo(vfo.Modo, vfo.Frecuencia);
        return nombre switch
        {
            null => "usb",
            "USB" => "usb",
            "LSB" => "lsb",
            "CW" or "CWR" => "cw",
            "AM" => "am",
            "FM" or "DSTAR" or "C4FM" or "DMR" => "nfm",
            "PKTLSB" => "digl",
            _ => "digu",
        };
    }

    /// <summary>Del modo de TCI al del Cuaderno; nulo si no se entiende.</summary>
    internal static Modo? ModoDesdeTci(string texto) => texto.Trim().ToLowerInvariant() switch
    {
        "usb" => Traductor.DesdeElEquipo("USB"),
        "lsb" => Traductor.DesdeElEquipo("LSB"),
        "cw" => Traductor.DesdeElEquipo("CW"),
        "am" or "sam" or "dsb" => Traductor.DesdeElEquipo("AM"),
        "nfm" or "wfm" or "fm" => Traductor.DesdeElEquipo("FM"),
        "digu" => Traductor.DesdeElEquipo("PKTUSB"),
        "digl" => Traductor.DesdeElEquipo("PKTLSB"),
        _ => null,
    };
}
