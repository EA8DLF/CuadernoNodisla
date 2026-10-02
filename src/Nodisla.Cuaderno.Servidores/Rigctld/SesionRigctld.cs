using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Radio.Control;

namespace Nodisla.Cuaderno.Servidores.Rigctld;

/// <summary>
/// Una conexion al servidor compatible con rigctld: lee lineas y contesta como el rigctld de
/// Hamlib (protocolo de red, version 1 de <c>\dump_state</c>).
/// </summary>
/// <remarks>
/// <para>
/// Solo se atienden las ordenes de la lista de abajo. Nada de CAT en crudo
/// (<c>w</c>, <c>W</c>, <c>\send_cmd</c>), ni manipular CW (<c>b</c>, <c>\send_morse</c>), ni
/// memorias, antenas o encendido: esas se rechazan con <c>RPRT -19</c> y quedan en el registro.
/// Lo que no se conoce se contesta con <c>RPRT -4</c> (no implementado), como Hamlib.
/// </para>
/// <para>
/// Un navegador puede mandar una peticion HTTP a 127.0.0.1:4532 con ordenes en el cuerpo. En
/// cuanto llega algo con pinta de HTTP, se corta la conexion sin hacer nada.
/// </para>
/// </remarks>
internal sealed class SesionRigctld
{
    // Codigos de error de Hamlib.
    internal const int Ok = 0;
    internal const int Einval = -1;
    internal const int Enimpl = -4;
    internal const int Eio = -6;
    internal const int Erjcted = -9;
    internal const int Enavail = -11;
    internal const int Esecurity = -19;

    /// <summary>Modelo que se anuncia: el 2 de Hamlib es «NET rigctl».</summary>
    private const int ModeloAnunciado = 2;

    // Bits de modos de Hamlib que se anuncian: AM CW USB LSB RTTY FM CWR RTTYR PKTLSB PKTUSB PKTFM.
    private const string Modos = "0x1dbf";

    private static readonly TraductorDeModos Traductor = TraductorDeModos.PorOmision;

    private static readonly HashSet<string> Prohibidas = new(StringComparer.Ordinal)
    {
        "w", "W", "send_cmd", "b", "send_morse", "stop_morse", "wait_morse", "send_voice_mem",
        "set_powerstat", "set_ant", "set_mem", "set_bank", "vfo_op", "scan", "set_channel",
        "set_trn", "set_parm", "set_conf", "send_dtmf", "set_clock", "reset", "set_func",
        "E", "H", "Y", "C", "R", "N", "g", "G", "D", "0", "P", "U", "u", "J", "Z", "set_lock_mode",
    };

    private readonly RadioCompartida _radio;
    private readonly ClienteExterno _cliente;
    private readonly ILogger _registro;

    public SesionRigctld(RadioCompartida radio, ClienteExterno cliente, ILogger? registro = null)
    {
        _radio = radio;
        _cliente = cliente;
        _registro = registro ?? NullLogger.Instance;
    }

    /// <summary>
    /// Atiende una linea. Devuelve lo que hay que contestar (puede ser vacio) o nulo si hay que
    /// cerrar la conexion.
    /// </summary>
    public async Task<string?> AtenderAsync(string linea, CancellationToken ct = default)
    {
        if (PareceHttp(linea))
        {
            _registro.LogWarning("Peticion con pinta de HTTP en el puerto rigctld desde {Cliente}: se corta.", _cliente.Nombre);
            return null;
        }

        var fichas = new Queue<string>(linea.Split((char[])[' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries));
        var salida = new StringBuilder();
        while (fichas.Count > 0)
        {
            var ficha = fichas.Dequeue();

            // Respuesta extendida: «+f», «+\get_freq», o con separador «;f».
            var extendida = false;
            var separador = '\n';
            if (ficha.Length > 1 && ficha[0] is '+' or ';' or '|' or ',')
            {
                extendida = true;
                separador = ficha[0] == '+' ? '\n' : ficha[0];
                ficha = ficha[1..];
            }
            else if (ficha == "+" && fichas.Count > 0)
            {
                extendida = true;
                ficha = fichas.Dequeue();
            }

            string orden;
            if (ficha.StartsWith('\\'))
            {
                orden = ficha[1..];
            }
            else
            {
                orden = ficha[..1];

                // «F14074000» o «fm»: lo que sigue a la letra es otra ficha.
                if (ficha.Length > 1)
                {
                    var resto = new Queue<string>();
                    resto.Enqueue(ficha[1..]);
                    while (fichas.Count > 0) resto.Enqueue(fichas.Dequeue());
                    fichas = resto;
                }
            }

            if (orden is "q" or "Q" or "quit" or "exit") return salida.Length > 0 ? salida.ToString() : null;

            var def = Buscar(orden);
            if (def is null)
            {
                var cerrada = Prohibidas.Contains(orden);
                if (cerrada) _registro.LogWarning("{Cliente} pide la orden prohibida «{Orden}»: rechazada.", _cliente.Nombre, orden);
                else _registro.LogInformation("{Cliente} pide la orden desconocida «{Orden}».", _cliente.Nombre, orden);
                _radio.Orden(_cliente, new Respuesta(ResultadoDeOrden.Rechazado));

                // Las prohibidas se tragan sus argumentos para no tomarlos por ordenes.
                fichas.Clear();
                Rprt(salida, extendida, orden, [], cerrada ? Esecurity : Enimpl, separador);
                continue;
            }

            var argumentos = new List<string>();
            for (var i = 0; i < def.Argumentos && fichas.Count > 0; i++) argumentos.Add(fichas.Dequeue());

            // Con «--vfo» delante va el VFO: se acepta y se usa si es A o B.
            NombreDeVfo? vfo = null;
            if (argumentos.Count > 0 && def.Argumentos > 0 && EsNombreDeVfo(argumentos[0]) && def.AdmiteVfo)
            {
                vfo = VfoDe(argumentos[0]);
                argumentos.RemoveAt(0);
                if (fichas.Count > 0) argumentos.Add(fichas.Dequeue());
            }
            else if (def.Argumentos == 0 && def.AdmiteVfo && fichas.Count > 0 && EsNombreDeVfo(fichas.Peek()))
            {
                vfo = VfoDe(fichas.Dequeue());
            }

            if (argumentos.Count < def.Argumentos)
            {
                _radio.Orden(_cliente, new Respuesta(ResultadoDeOrden.Invalido));
                Rprt(salida, extendida, def.Largo, argumentos, Einval, separador);
                continue;
            }

            int codigo;
            IReadOnlyList<(string Etiqueta, string Valor)> valores = [];
            try
            {
                (codigo, valores) = await def.Atender(this, argumentos, vfo, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                _registro.LogWarning(ex, "Orden {Orden} de {Cliente} fallida.", def.Largo, _cliente.Nombre);
                codigo = Eio;
            }

            _radio.Orden(_cliente, new Respuesta(codigo == Ok ? ResultadoDeOrden.Hecho : codigo == Enavail ? ResultadoDeOrden.NoDisponible : ResultadoDeOrden.Rechazado));
            Escribir(salida, extendida, separador, def, argumentos, codigo, valores);
        }

        return salida.ToString();
    }

    private static void Escribir(StringBuilder salida, bool extendida, char separador, Orden def, List<string> argumentos, int codigo, IReadOnlyList<(string Etiqueta, string Valor)> valores)
    {
        if (!extendida)
        {
            if (codigo != Ok || def.EsDeCambio)
            {
                salida.Append("RPRT ").Append(codigo.ToString(CultureInfo.InvariantCulture)).Append('\n');
                return;
            }

            foreach (var (_, valor) in valores) salida.Append(valor).Append('\n');
            return;
        }

        salida.Append(def.Largo).Append(':');
        foreach (var a in argumentos) salida.Append(' ').Append(a);
        salida.Append(separador);
        if (codigo == Ok)
        {
            foreach (var (etiqueta, valor) in valores)
            {
                if (etiqueta.Length > 0) salida.Append(etiqueta).Append(": ");
                salida.Append(valor).Append(separador);
            }
        }

        salida.Append("RPRT ").Append(codigo.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    private static void Rprt(StringBuilder salida, bool extendida, string orden, List<string> argumentos, int codigo, char separador)
    {
        if (extendida)
        {
            salida.Append(orden).Append(':');
            foreach (var a in argumentos) salida.Append(' ').Append(a);
            salida.Append(separador);
        }

        salida.Append("RPRT ").Append(codigo.ToString(CultureInfo.InvariantCulture)).Append('\n');
    }

    internal static bool PareceHttp(string linea)
    {
        var t = linea.TrimStart();
        if (t.Contains("HTTP/", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var verbo in (string[])["GET ", "POST ", "PUT ", "HEAD ", "OPTIONS ", "DELETE ", "PATCH ", "CONNECT ", "TRACE "])
        {
            if (t.StartsWith(verbo, StringComparison.OrdinalIgnoreCase)) return true;
        }

        // Cabeceras («Host: ...», «Origin: ...»): ninguna orden de rigctld empieza asi.
        var dosPuntos = t.IndexOf(':', StringComparison.Ordinal);
        return dosPuntos > 1 && t.IndexOf(' ', StringComparison.Ordinal) == dosPuntos + 1 && !t.StartsWith('\\') && char.IsLetter(t[0]) && t[..dosPuntos].All(c => char.IsLetter(c) || c == '-');
    }

    private static bool EsNombreDeVfo(string texto) =>
        texto is "VFOA" or "VFOB" or "currVFO" or "Main" or "Sub" or "TX" or "RX" or "MainA" or "MainB" or "VFOC" or "None";

    private NombreDeVfo? VfoDe(string texto) => texto switch
    {
        "VFOA" or "Main" or "MainA" => NombreDeVfo.A,
        "VFOB" or "Sub" or "MainB" => NombreDeVfo.B,
        "TX" => _radio.VfoDeTransmision,
        _ => null,
    };

    private static string NombreHamlib(NombreDeVfo vfo) => vfo == NombreDeVfo.B ? "VFOB" : "VFOA";

    // ── Tabla de ordenes ──────────────────────────────────────────────────────────────────

    private sealed record Orden(
        string Corto,
        string Largo,
        int Argumentos,
        bool EsDeCambio,
        bool AdmiteVfo,
        Func<SesionRigctld, List<string>, NombreDeVfo?, CancellationToken, Task<(int, IReadOnlyList<(string, string)>)>> Atender);

    private static readonly Orden[] Tabla =
    [
        new("F", "set_freq", 1, true, true, (s, a, v, ct) => s.PonerFrecuenciaAsync(a[0], v, ct)),
        new("f", "get_freq", 0, false, true, (s, _, v, _) => s.Valores(("Frequency", Hz(s._radio.DelVfo(v ?? s._radio.VfoActivo).Frecuencia)))),
        new("M", "set_mode", 2, true, true, (s, a, v, ct) => s.PonerModoAsync(a[0], v, ct)),
        new("m", "get_mode", 0, false, true, (s, _, v, _) => s.LeerModo(v ?? s._radio.VfoActivo, "Mode", "Passband")),
        new("V", "set_vfo", 1, true, false, (s, a, _, ct) => s.PonerVfoAsync(a[0], ct)),
        new("v", "get_vfo", 0, false, false, (s, _, _, _) => s.Valores(("VFO", NombreHamlib(s._radio.VfoActivo)))),
        new("T", "set_ptt", 1, true, true, (s, a, _, ct) => s.PttAsync(a[0], ct)),
        new("t", "get_ptt", 0, false, true, (s, _, _, _) => s.Valores(("PTT", s._cliente.Transmitiendo ? "1" : "0"))),
        new("S", "set_split_vfo", 2, true, true, (s, a, _, ct) => s.PonerSplitAsync(a[0], a[1], ct)),
        new("s", "get_split_vfo", 0, false, true, (s, _, _, _) => s.Valores(("Split", s._radio.Split ? "1" : "0"), ("TX VFO", NombreHamlib(s._radio.Split ? s._radio.VfoDeTransmision : s._radio.VfoActivo)))),
        new("I", "set_split_freq", 1, true, true, (s, a, _, ct) => s.PonerFrecuenciaTxAsync(a[0], ct)),
        new("i", "get_split_freq", 0, false, true, (s, _, _, _) => s.Valores(("TX Frequency", Hz(s._radio.DelVfo(s.VfoTxSplit).Frecuencia)))),
        new("X", "set_split_mode", 2, true, true, (s, a, _, ct) => s.PonerModoAsync(a[0], s.VfoTxSplit, ct)),
        new("x", "get_split_mode", 0, false, true, (s, _, _, _) => s.LeerModo(s.VfoTxSplit, "TX Mode", "TX Passband")),
        new("L", "set_level", 2, true, true, (s, a, _, ct) => s.PonerNivelAsync(a[0], a[1], ct)),
        new("l", "get_level", 1, false, true, (s, a, _, ct) => s.LeerNivelAsync(a[0], ct)),
        new("j", "get_rit", 0, false, true, (s, _, _, _) => s.Valores(("RIT", "0"))),
        new("z", "get_xit", 0, false, true, (s, _, _, _) => s.Valores(("XIT", "0"))),
        new("_", "get_info", 0, false, false, (s, _, _, _) => s.Valores(("Info", "Cuaderno NODISLA"))),
        new(string.Empty, "chk_vfo", 0, false, false, (s, _, _, _) => s.Valores(("", "0"))),
        new(string.Empty, "dump_state", 0, false, false, (s, _, _, _) => s.Valores(("", s.Estado()))),
        new(string.Empty, "get_powerstat", 0, false, false, (s, _, _, _) => s.Valores(("Power Status", s._radio.Estado.Conectado ? "1" : "0"))),
        new(string.Empty, "get_lock_mode", 0, false, false, (s, _, _, _) => s.Valores(("Locked", "0"))),
        new(string.Empty, "get_vfo_info", 1, false, false, (s, a, _, _) => s.InfoDelVfo(a[0])),
    ];

    private static Orden? Buscar(string orden)
    {
        foreach (var o in Tabla)
        {
            if ((o.Corto.Length > 0 && string.Equals(o.Corto, orden, StringComparison.Ordinal)) || string.Equals(o.Largo, orden, StringComparison.Ordinal)) return o;
        }

        return null;
    }

    private Task<(int, IReadOnlyList<(string, string)>)> Valores(params (string, string)[] valores) =>
        Task.FromResult<(int, IReadOnlyList<(string, string)>)>((Ok, valores));

    private static Task<(int, IReadOnlyList<(string, string)>)> Codigo(int codigo) =>
        Task.FromResult<(int, IReadOnlyList<(string, string)>)>((codigo, []));

    private static async Task<(int, IReadOnlyList<(string, string)>)> Codigo(Task<Respuesta> respuesta)
    {
        var r = await respuesta.ConfigureAwait(false);
        return (r.Resultado switch
        {
            ResultadoDeOrden.Hecho => Ok,
            ResultadoDeOrden.NoDisponible => Enavail,
            ResultadoDeOrden.Invalido => Einval,
            ResultadoDeOrden.Fallo => Eio,
            _ => Erjcted,
        }, []);
    }

    private static string Hz(Frecuencia f) => f.Hercios.ToString(CultureInfo.InvariantCulture);

    private NombreDeVfo VfoTxSplit => _radio.Split
        ? _radio.VfoDeTransmision
        : (_radio.Vfos is not null ? (_radio.VfoActivo == NombreDeVfo.A ? NombreDeVfo.B : NombreDeVfo.A) : _radio.VfoActivo);

    private static bool LeerHz(string texto, out Frecuencia frecuencia)
    {
        frecuencia = Frecuencia.Cero;
        if (!double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var hz) || !double.IsFinite(hz) || hz <= 0 || hz > 2e9) return false;
        frecuencia = Frecuencia.DesdeHercios((long)Math.Round(hz));
        return true;
    }

    private Task<(int, IReadOnlyList<(string, string)>)> PonerFrecuenciaAsync(string texto, NombreDeVfo? vfo, CancellationToken ct) =>
        LeerHz(texto, out var f) ? Codigo(_radio.PonerFrecuenciaAsync(_cliente, f, vfo, ct)) : Codigo(Einval);

    private Task<(int, IReadOnlyList<(string, string)>)> PonerFrecuenciaTxAsync(string texto, CancellationToken ct)
    {
        if (!LeerHz(texto, out var f)) return Codigo(Einval);

        // Sin dos VFO no hay frecuencia de TX aparte: si es la misma, nada que hacer.
        if (_radio.Vfos is null) return Codigo(f == _radio.Estado.Frecuencia ? Ok : Enavail);
        return Codigo(_radio.PonerFrecuenciaAsync(_cliente, f, VfoTxSplit, ct));
    }

    /// <summary>Del nombre de Hamlib al modo del Cuaderno («PKTUSB» es el modo de datos, FT8).</summary>
    internal static Modo ModoDesdeHamlib(string nombre)
    {
        var n = nombre.Trim().ToUpperInvariant();
        if (n is "?" or "") return Modo.Vacio;
        var modo = Traductor.DesdeElEquipo(n);
        return modo;
    }

    /// <summary>Del modo del Cuaderno al nombre de Hamlib.</summary>
    internal static string ModoAHamlib(Modo modo, Frecuencia frecuencia)
    {
        var nombre = Traductor.AlEquipo(modo, frecuencia) ?? "USB";
        return nombre switch
        {
            "USB" or "LSB" or "CW" or "CWR" or "RTTY" or "RTTYR" or "AM" or "FM" or "WFM" or "PKTUSB" or "PKTLSB" or "PKTFM" => nombre,
            "DSTAR" or "C4FM" or "DMR" => "FM",
            _ => "PKTUSB",
        };
    }

    private static int AnchoPorOmision(string modo) => modo switch
    {
        "CW" or "CWR" => 500,
        "AM" => 6000,
        "FM" or "PKTFM" => 12000,
        "WFM" => 230000,
        "RTTY" or "RTTYR" => 500,
        "PKTUSB" or "PKTLSB" => 3000,
        _ => 2400,
    };

    private Task<(int, IReadOnlyList<(string, string)>)> LeerModo(NombreDeVfo vfo, string etiquetaModo, string etiquetaAncho)
    {
        var (f, m, ancho) = _radio.DelVfo(vfo);
        var nombre = ModoAHamlib(m, f);
        return Valores((etiquetaModo, nombre), (etiquetaAncho, (ancho ?? AnchoPorOmision(nombre)).ToString(CultureInfo.InvariantCulture)));
    }

    private Task<(int, IReadOnlyList<(string, string)>)> PonerModoAsync(string nombre, NombreDeVfo? vfo, CancellationToken ct)
    {
        var modo = ModoDesdeHamlib(nombre);
        if (modo.EsVacio) return Codigo(Ok);
        if (!EsModoHamlib(nombre)) return Codigo(Einval);

        // Si ya esta en ese modo no se manda nada: WSJT-X lo repite a menudo y, en el aire, un
        // cambio de modo se rechazaria y le saltaria un error sin motivo.
        var (f, actual, _) = _radio.DelVfo(vfo ?? _radio.VfoActivo);
        if (string.Equals(ModoAHamlib(actual, f), ModoAHamlib(modo, f), StringComparison.Ordinal)) return Codigo(Ok);
        return Codigo(_radio.PonerModoAsync(_cliente, modo, vfo, ct));
    }

    private static bool EsModoHamlib(string nombre) => nombre.Trim().ToUpperInvariant() is
        "USB" or "LSB" or "CW" or "CWR" or "RTTY" or "RTTYR" or "AM" or "FM" or "WFM" or "PKTUSB" or "PKTLSB" or "PKTFM"
        or "AMS" or "DSB" or "ECSSUSB" or "ECSSLSB" or "FMN" or "SAM" or "SAL" or "SAH" or "PKTAM";

    private Task<(int, IReadOnlyList<(string, string)>)> PonerVfoAsync(string texto, CancellationToken ct)
    {
        if (texto == "currVFO") return Codigo(Ok);
        return VfoDe(texto) is { } vfo ? Codigo(_radio.PonerVfoAsync(_cliente, vfo, ct)) : Codigo(Einval);
    }

    private Task<(int, IReadOnlyList<(string, string)>)> PttAsync(string texto, CancellationToken ct)
    {
        if (!int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ptt) || ptt is < 0 or > 3) return Codigo(Einval);
        return Codigo(_radio.PttAsync(_cliente, ptt != 0, ct));
    }

    private Task<(int, IReadOnlyList<(string, string)>)> PonerSplitAsync(string texto, string vfoTx, CancellationToken ct)
    {
        if (texto is not ("0" or "1")) return Codigo(Einval);
        var split = texto == "1";

        // Si se pide transmitir por el mismo VFO activo, no es split de verdad.
        if (split && VfoDe(vfoTx) is { } tx && tx == _radio.VfoActivo && _radio.Vfos is not null) split = false;
        return Codigo(_radio.PonerSplitAsync(_cliente, split, ct));
    }

    private async Task<(int, IReadOnlyList<(string, string)>)> PonerNivelAsync(string nivel, string valor, CancellationToken ct)
    {
        if (!string.Equals(nivel, "RFPOWER", StringComparison.OrdinalIgnoreCase)) return (Enavail, []);
        if (!double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraccion) || !double.IsFinite(fraccion) || fraccion is < 0 or > 1) return (Einval, []);
        if (_radio.PotenciaMaximaDelEquipo is not { } maximo || maximo <= 0) return (Enavail, []);
        return await Codigo(_radio.PonerPotenciaAsync(_cliente, fraccion * maximo, ct)).ConfigureAwait(false);
    }

    private async Task<(int, IReadOnlyList<(string, string)>)> LeerNivelAsync(string nivel, CancellationToken ct)
    {
        switch (nivel.ToUpperInvariant())
        {
            case "RFPOWER":
                if (_radio.PotenciaMaximaDelEquipo is not { } maximo || maximo <= 0) return (Enavail, []);
                if (await _radio.LeerPotenciaAsync(ct).ConfigureAwait(false) is not { } w) return (Enavail, []);
                return (Ok, [(nivel, Math.Clamp(w / maximo, 0, 1).ToString("0.000000", CultureInfo.InvariantCulture))]);
            case "STRENGTH":
                // Hamlib: dB sobre S9 (S9 = 0, S0 = -54).
                var s = _radio.Estado.SenalRecibida ?? 0;
                var db = s <= 9 ? (int)Math.Round((s - 9) * 6) : (int)Math.Round((s - 9) * 10);
                return (Ok, [(nivel, Math.Clamp(db, -54, 60).ToString(CultureInfo.InvariantCulture))]);
            default:
                return (Enavail, []);
        }
    }

    private Task<(int, IReadOnlyList<(string, string)>)> InfoDelVfo(string texto)
    {
        var vfo = VfoDe(texto) ?? _radio.VfoActivo;
        var (f, m, ancho) = _radio.DelVfo(vfo);
        var nombre = ModoAHamlib(m, f);
        return Valores(
            ("Freq", Hz(f)),
            ("Mode", nombre),
            ("Width", (ancho ?? AnchoPorOmision(nombre)).ToString(CultureInfo.InvariantCulture)),
            ("Split", _radio.Split ? "1" : "0"),
            ("SatMode", "0"));
    }

    /// <summary>
    /// La respuesta a <c>\dump_state</c>, version 1 del protocolo: rangos, pasos, filtros, niveles
    /// y las claves que lee el cliente «NET rigctl» de Hamlib (el de WSJT-X, JTDX, GridTracker...).
    /// </summary>
    private string Estado()
    {
        var potencia = _radio.PotenciaMaximaDelEquipo ?? 100;
        var mw = ((long)Math.Round(potencia * 1000)).ToString(CultureInfo.InvariantCulture);
        var doble = _radio.Vfos is not null;
        var vfos = doble ? "0x3" : "0x1";
        var b = new StringBuilder();
        b.Append("1\n");                                   // version del protocolo
        b.Append(ModeloAnunciado).Append('\n');            // modelo
        b.Append("0\n");                                   // region ITU (0: sin dato)
        b.Append("30000 75000000 ").Append(Modos).Append(" -1 -1 ").Append(vfos).Append(" 0x1\n"); // RX
        b.Append("0 0 0 0 0 0 0\n");
        b.Append("1800000 54000000 ").Append(Modos).Append(" 5000 ").Append(mw).Append(' ').Append(vfos).Append(" 0x1\n"); // TX
        b.Append("0 0 0 0 0 0 0\n");
        b.Append(Modos).Append(" 1\n");                    // pasos de sintonia
        b.Append("0 0\n");
        b.Append("0xc 2400\n0xc 1800\n0x82 500\n0x82 200\n0x1 6000\n0x20 12000\n0xc00 3000\n0x110 500\n"); // filtros
        b.Append("0 0\n");
        b.Append("9990\n9990\n0\n0\n");                   // max_rit, max_xit, max_ifshift, announces
        b.Append('\n');                                    // preamplificadores
        b.Append('\n');                                    // atenuadores
        b.Append("0x0\n0x0\n");                            // has_get_func, has_set_func
        b.Append("0x40001000\n0x1000\n");                  // get_level: RFPOWER|STRENGTH; set_level: RFPOWER
        b.Append("0x0\n0x0\n");                            // parm
        b.Append("vfo_ops=0x0\n");
        b.Append("ptt_type=0x1\n");
        b.Append("targetable_vfo=0x0\n");
        b.Append("has_set_vfo=").Append(doble ? '1' : '0').Append('\n');
        b.Append("has_get_vfo=1\n");
        b.Append("has_set_freq=1\nhas_get_freq=1\n");
        b.Append("has_set_conf=0\nhas_get_conf=0\n");
        b.Append("has_power2mW=0\nhas_mW2power=0\n");
        b.Append("timeout=1000\n");
        b.Append("rig_model=").Append(ModeloAnunciado).Append('\n');
        b.Append("rigctld_version=Cuaderno NODISLA\n");
        b.Append("done");
        return b.ToString();
    }
}
