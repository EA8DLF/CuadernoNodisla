using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace Nodisla.Cuaderno.Ui.Digital;

/// <summary>Una recepcion que se manda a PSK Reporter.</summary>
/// <param name="Indicativo">Quien se oyo.</param>
/// <param name="Locator">Su localizador, si lo dijo.</param>
/// <param name="FrecuenciaHz">Frecuencia real de la señal: dial mas tono.</param>
/// <param name="Decibelios">Relacion señal-ruido.</param>
/// <param name="Modo">Modo en letras: FT8, FT4…</param>
/// <param name="InstanteUtc">Cuando se oyo.</param>
public readonly record struct RecepcionParaInformar(
    string Indicativo,
    string Locator,
    long FrecuenciaHz,
    int Decibelios,
    string Modo,
    DateTimeOffset InstanteUtc);

/// <summary>
/// Manda a PSK Reporter lo que se ha oido, por UDP, con el protocolo publico (IPFIX sobre el
/// puerto 4739).
/// </summary>
/// <remarks>
/// <para>
/// El formato es el que documenta pskreporter.info: una cabecera IPFIX, una plantilla con los
/// datos del receptor (indicativo, localizador, programa), otra con los del emisor oido
/// (indicativo, frecuencia, señal, modo, localizador, origen y hora), y los registros. Las
/// plantillas van en cada paquete: cuesta unos bytes y evita que el servidor reciba datos de
/// una plantilla que aun no conoce.
/// </para>
/// <para>
/// <b>Es red saliente</b> y por eso viene desactivado. Quien lo activa manda al mundo su
/// indicativo y su localizador con cada decodificacion. El empaquetado esta separado del envio
/// para poder probarlo sin abrir ningun socket.
/// </para>
/// </remarks>
public sealed class ReportadorPskReporter
{
    /// <summary>Servidor publico.</summary>
    public const string Servidor = "report.pskreporter.info";

    /// <summary>Puerto UDP del servidor.</summary>
    public const int Puerto = 4739;

    /// <summary>Numero de empresa de PSK Reporter en el registro IANA.</summary>
    private const int Empresa = 30351;

    /// <summary>Cuanto se espera antes de repetir un mismo indicativo.</summary>
    private static readonly TimeSpan NoRepetirAntesDe = TimeSpan.FromMinutes(5);

    private readonly Queue<RecepcionParaInformar> _pendientes = new();
    private readonly Dictionary<string, DateTimeOffset> _ultimaVez = new(StringComparer.Ordinal);
    private readonly uint _origen;
    private readonly string _programa;
    private uint _secuencia;

    /// <summary>Monta el reportador.</summary>
    /// <param name="programa">Nombre y version del programa que decodifica.</param>
    /// <param name="origen">Identificador de sesion; si no se da, uno al azar.</param>
    public ReportadorPskReporter(string programa, uint? origen = null)
    {
        _programa = programa;
        _origen = origen ?? (uint)Random.Shared.Next(1, int.MaxValue);
    }

    /// <summary>Indicativo del receptor: el propio.</summary>
    public string MiIndicativo { get; set; } = string.Empty;

    /// <summary>Localizador del receptor: el propio.</summary>
    public string MiLocalizador { get; set; } = string.Empty;

    /// <summary>Cuantas recepciones esperan a salir.</summary>
    public int Pendientes => _pendientes.Count;

    /// <summary>Paquetes que se han empaquetado desde el arranque.</summary>
    public uint Secuencia => _secuencia;

    /// <summary>
    /// Apunta una recepcion. Sin localizador no se manda: PSK Reporter lo necesita para pintar.
    /// </summary>
    /// <returns>Si se ha apuntado o se ha descartado (repetida o sin localizador).</returns>
    public bool Anotar(RecepcionParaInformar recepcion)
    {
        if (recepcion.Indicativo.Length == 0 || recepcion.Locator.Length < 4) return false;

        if (_ultimaVez.TryGetValue(recepcion.Indicativo, out var antes)
            && recepcion.InstanteUtc - antes < NoRepetirAntesDe)
        {
            return false;
        }

        _ultimaVez[recepcion.Indicativo] = recepcion.InstanteUtc;
        _pendientes.Enqueue(recepcion);
        return true;
    }

    /// <summary>
    /// Vacia la cola en un paquete listo para mandar. Devuelve nulo si no hay nada o faltan
    /// el indicativo o el localizador propios.
    /// </summary>
    public byte[]? Empaquetar(DateTimeOffset ahora)
    {
        if (_pendientes.Count == 0 || MiIndicativo.Length == 0 || MiLocalizador.Length < 4) return null;

        var cuerpo = new List<byte>(512);

        // Cabecera IPFIX: version, longitud (se rellena al final), hora, secuencia, origen.
        Uint16(cuerpo, 0x000A);
        Uint16(cuerpo, 0);
        Uint32(cuerpo, (uint)ahora.ToUnixTimeSeconds());
        Uint32(cuerpo, ++_secuencia);
        Uint32(cuerpo, _origen);

        cuerpo.AddRange(PlantillaDelReceptor());
        cuerpo.AddRange(PlantillaDelEmisor());
        cuerpo.AddRange(DatosDelReceptor());
        cuerpo.AddRange(DatosDeLosEmisores());

        var paquete = cuerpo.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(paquete.AsSpan(2, 2), (ushort)paquete.Length);
        return paquete;
    }

    /// <summary>Manda un paquete al servidor. Abre y cierra el socket cada vez.</summary>
    public static async Task EnviarAsync(byte[] paquete, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paquete);
        using var udp = new UdpClient();
        await udp.SendAsync(paquete, Servidor, Puerto, ct).ConfigureAwait(false);
    }

    /// <summary>Plantilla de opciones 0x9992: indicativo, localizador y programa del receptor.</summary>
    private static byte[] PlantillaDelReceptor()
    {
        var b = new List<byte>(40);
        Uint16(b, 3);            // Options Template Set
        Uint16(b, 0);            // longitud, luego
        Uint16(b, 0x9992);
        Uint16(b, 3);            // campos
        Uint16(b, 0);            // campos de ambito
        Campo(b, 2, 0xFFFF);     // receiverCallsign
        Campo(b, 4, 0xFFFF);     // receiverLocator
        Campo(b, 8, 0xFFFF);     // decodingSoftware
        Rellenar(b);
        return ConLongitud(b);
    }

    /// <summary>Plantilla 0x9993: lo que se oyo de cada emisor.</summary>
    private static byte[] PlantillaDelEmisor()
    {
        var b = new List<byte>(64);
        Uint16(b, 2);            // Template Set
        Uint16(b, 0);
        Uint16(b, 0x9993);
        Uint16(b, 7);
        Campo(b, 1, 0xFFFF);     // senderCallsign
        Campo(b, 5, 4);          // frequency
        Campo(b, 6, 1);          // sNR
        Campo(b, 10, 0xFFFF);    // mode
        Campo(b, 3, 0xFFFF);     // senderLocator
        Campo(b, 11, 1);         // informationSource
        Uint16(b, 150);          // flowStartSeconds (IANA, sin empresa)
        Uint16(b, 4);
        Rellenar(b);
        return ConLongitud(b);
    }

    private byte[] DatosDelReceptor()
    {
        var b = new List<byte>(64);
        Uint16(b, 0x9992);
        Uint16(b, 0);
        Cadena(b, MiIndicativo);
        Cadena(b, MiLocalizador);
        Cadena(b, _programa);
        Rellenar(b);
        return ConLongitud(b);
    }

    private byte[] DatosDeLosEmisores()
    {
        var b = new List<byte>(32 * _pendientes.Count);
        Uint16(b, 0x9993);
        Uint16(b, 0);

        while (_pendientes.TryDequeue(out var r))
        {
            Cadena(b, r.Indicativo);
            Uint32(b, (uint)Math.Clamp(r.FrecuenciaHz, 0, uint.MaxValue));
            b.Add(unchecked((byte)(sbyte)Math.Clamp(r.Decibelios, -128, 127)));
            Cadena(b, r.Modo);
            Cadena(b, r.Locator);
            b.Add(1); // informationSource: sacado automaticamente del mensaje
            Uint32(b, (uint)r.InstanteUtc.ToUnixTimeSeconds());
        }

        Rellenar(b);
        return ConLongitud(b);
    }

    private static void Campo(List<byte> b, int id, int longitud)
    {
        Uint16(b, (ushort)(0x8000 | id));
        Uint16(b, (ushort)longitud);
        Uint32(b, Empresa);
    }

    private static void Cadena(List<byte> b, string texto)
    {
        var bytes = Encoding.ASCII.GetBytes(texto.Length > 254 ? texto[..254] : texto);
        b.Add((byte)bytes.Length);
        b.AddRange(bytes);
    }

    private static void Uint16(List<byte> b, ushort v)
    {
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    private static void Uint32(List<byte> b, uint v)
    {
        b.Add((byte)(v >> 24));
        b.Add((byte)(v >> 16));
        b.Add((byte)(v >> 8));
        b.Add((byte)v);
    }

    private static void Rellenar(List<byte> b)
    {
        while (b.Count % 4 != 0) b.Add(0);
    }

    private static byte[] ConLongitud(List<byte> b)
    {
        var bytes = b.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(2, 2), (ushort)bytes.Length);
        return bytes;
    }
}
