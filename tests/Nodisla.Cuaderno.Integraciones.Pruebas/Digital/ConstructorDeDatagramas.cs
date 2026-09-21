using System.Text;

namespace Nodisla.Cuaderno.Integraciones.Pruebas.Digital;

/// <summary>
/// Arma datagramas del protocolo de WSJT-X byte a byte.
/// </summary>
/// <remarks>
/// Escribe los bytes a mano y no con el escritor del proyecto a proposito: si las pruebas
/// usaran el mismo codigo que se quiere comprobar, un error de orden de bytes pasaria
/// desapercibido porque estaria en los dos lados.
/// </remarks>
internal sealed class ConstructorDeDatagramas
{
    private readonly List<byte> _bytes = [];

    /// <summary>Magia del protocolo.</summary>
    public const uint Magia = 0xADBC_CBDAu;

    /// <summary>Dia juliano del 1 de enero de 1970.</summary>
    private const long DiaJulianoDeLaEpoca = 2_440_588L;

    public static ConstructorDeDatagramas Nuevo(uint tipo, string id, uint esquema = 3, uint magia = Magia)
    {
        var c = new ConstructorDeDatagramas();
        c.U32(magia);
        c.U32(esquema);
        c.U32(tipo);
        c.Txt(id);
        return c;
    }

    public ConstructorDeDatagramas U8(byte v)
    {
        _bytes.Add(v);
        return this;
    }

    public ConstructorDeDatagramas Log(bool v) => U8(v ? (byte)1 : (byte)0);

    public ConstructorDeDatagramas U32(uint v)
    {
        _bytes.Add((byte)(v >> 24));
        _bytes.Add((byte)(v >> 16));
        _bytes.Add((byte)(v >> 8));
        _bytes.Add((byte)v);
        return this;
    }

    public ConstructorDeDatagramas I32(int v) => U32(unchecked((uint)v));

    public ConstructorDeDatagramas U64(ulong v)
    {
        for (var i = 7; i >= 0; i--) _bytes.Add((byte)(v >> (i * 8)));
        return this;
    }

    public ConstructorDeDatagramas I64(long v) => U64(unchecked((ulong)v));

    public ConstructorDeDatagramas Doble(double v) => I64(BitConverter.DoubleToInt64Bits(v));

    public ConstructorDeDatagramas Txt(string? v)
    {
        if (v is null) return U32(0xFFFF_FFFFu);
        var datos = Encoding.UTF8.GetBytes(v);
        U32((uint)datos.Length);
        _bytes.AddRange(datos);
        return this;
    }

    public ConstructorDeDatagramas Hora(TimeSpan v) => U32((uint)v.TotalMilliseconds);

    public ConstructorDeDatagramas FechaUtc(DateTimeOffset v)
    {
        var utc = v.ToUniversalTime().UtcDateTime;
        var dias = (long)(utc.Date - DateTime.UnixEpoch).TotalDays;
        I64(DiaJulianoDeLaEpoca + dias);
        U32((uint)utc.TimeOfDay.TotalMilliseconds);
        return U8(1); // UTC
    }

    /// <summary>Bytes crudos, sin interpretar. Para meter basura a proposito.</summary>
    public ConstructorDeDatagramas Crudo(params byte[] datos)
    {
        _bytes.AddRange(datos);
        return this;
    }

    public byte[] ABytes() => [.. _bytes];
}

/// <summary>Datagramas de ejemplo tomados de lo que emiten los programas de verdad.</summary>
internal static class Datagramas
{
    /// <summary>Latido, igual en todos los dialectos.</summary>
    public static byte[] Latido(string id, string version = "2.7.0", string revision = "abc123") =>
        ConstructorDeDatagramas.Nuevo(0, id)
            .U32(3)
            .Txt(version)
            .Txt(revision)
            .ABytes();

    /// <summary>Estado tal y como lo envia WSJT-X, con los cinco campos del final.</summary>
    public static byte[] EstadoWsjtX(
        string id = "WSJT-X",
        ulong dialHz = 14_074_000,
        string modo = "FT8",
        string dxCall = "K1ABC",
        bool transmitiendo = false,
        bool decodificando = true) =>
        EstadoComun(id, dialHz, modo, dxCall, transmitiendo, decodificando)
            .U8(2)              // modo de operacion especial: EU VHF
            .U32(50)            // tolerancia de frecuencia
            .U32(15)            // periodo T/R
            .Txt("Default")     // nombre de configuracion
            .Txt("CQ EA8DLF IL18")
            .ABytes();

    /// <summary>
    /// Estado tal y como lo envia JTDX: donde WSJT-X pone el modo de operacion especial, JTDX
    /// pone quien transmite primero, y ahi se acaba el mensaje.
    /// </summary>
    public static byte[] EstadoJtdx(
        string id = "JTDX",
        ulong dialHz = 7_074_000,
        string modo = "FT8",
        string dxCall = "W9XYZ",
        bool txPrimero = true) =>
        EstadoComun(id, dialHz, modo, dxCall, false, false)
            .Log(txPrimero)
            .ABytes();

    private static ConstructorDeDatagramas EstadoComun(
        string id, ulong dialHz, string modo, string dxCall, bool transmitiendo, bool decodificando) =>
        ConstructorDeDatagramas.Nuevo(1, id)
            .U64(dialHz)
            .Txt(modo)
            .Txt(dxCall)
            .Txt("-15")
            .Txt(modo)
            .Log(true)              // tx habilitado
            .Log(transmitiendo)
            .Log(decodificando)
            .U32(1500)              // rx DF
            .U32(1200)              // tx DF
            .Txt("EA8DLF")
            .Txt("IL18")
            .Txt("FN42")
            .Log(false)             // perro guardian
            .Txt(string.Empty)      // submodo
            .Log(false);            // modo rapido: ultimo campo comun a los dos dialectos

    /// <summary>Decodificacion, identica en los dos dialectos.</summary>
    public static byte[] Decodificacion(
        string id,
        string mensaje,
        TimeSpan hora,
        int snr = -12,
        double desfase = 0.2,
        uint delta = 1234,
        string modo = "~") =>
        ConstructorDeDatagramas.Nuevo(2, id)
            .Log(true)
            .Hora(hora)
            .I32(snr)
            .Doble(desfase)
            .U32(delta)
            .Txt(modo)
            .Txt(mensaje)
            .Log(false)
            .Log(false)
            .ABytes();

    /// <summary>Contacto cerrado de WSJT-X, con los campos de concurso y el modo de propagacion.</summary>
    public static byte[] QsoWsjtX(
        string id = "WSJT-X",
        string dxCall = "K1ABC",
        string modo = "FT8",
        ulong frecuencia = 14_075_000) =>
        QsoComun(id, dxCall, modo, frecuencia)
            .Txt("001")
            .Txt("002")
            .Txt("SAT")
            .ABytes();

    /// <summary>Contacto cerrado de JTDX: el mensaje se acaba sin los campos de concurso.</summary>
    public static byte[] QsoJtdx(
        string id = "JTDX",
        string dxCall = "W9XYZ",
        string modo = "FT4",
        ulong frecuencia = 7_047_500) =>
        QsoComun(id, dxCall, modo, frecuencia).ABytes();

    private static ConstructorDeDatagramas QsoComun(
        string id, string dxCall, string modo, ulong frecuencia) =>
        ConstructorDeDatagramas.Nuevo(5, id)
            .FechaUtc(new DateTimeOffset(2026, 9, 21, 10, 15, 30, TimeSpan.Zero))
            .Txt(dxCall)
            .Txt("FN42")
            .U64(frecuencia)
            .Txt(modo)
            .Txt("-15")
            .Txt("+03")
            .Txt("25 W")
            .Txt("Contacto de prueba")
            .Txt("Bob")
            .FechaUtc(new DateTimeOffset(2026, 9, 21, 10, 14, 15, TimeSpan.Zero))
            .Txt("EA8DLF")
            .Txt("EA8DLF")
            .Txt("IL18");

    /// <summary>Contacto cerrado en texto ADIF, que el programa manda ademas del binario.</summary>
    public static byte[] Adif(string id, string texto) =>
        ConstructorDeDatagramas.Nuevo(12, id).Txt(texto).ABytes();

    /// <summary>Cierre de la instancia.</summary>
    public static byte[] Cierre(string id) => ConstructorDeDatagramas.Nuevo(6, id).ABytes();

    /// <summary>Mensaje 50 de JTDX: fija el desplazamiento de transmision.</summary>
    public static byte[] FijarTxDeltaFreq(string id, uint hercios) =>
        ConstructorDeDatagramas.Nuevo(50, id).U32(hercios).ABytes();

    /// <summary>Mensaje 51 de JTDX: fija y dispara la llamada general.</summary>
    public static byte[] DispararCq(string id, string direccion) =>
        ConstructorDeDatagramas.Nuevo(51, id).Txt(direccion).Log(true).Log(true).ABytes();
}
