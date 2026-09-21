using System.Buffers.Binary;
using System.Text;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Escribe el cuerpo de un datagrama con el mismo formato que usa Qt al serializar.
/// </summary>
/// <remarks>
/// Es la cara contraria de <see cref="LectorQDataStream"/>: mismo orden de bytes y mismas
/// convenciones. Aqui si se puede ser estricto, porque lo que se escribe lo decidimos nosotros.
/// </remarks>
internal sealed class EscritorQDataStream
{
    private byte[] _datos = new byte[256];
    private int _n;

    /// <summary>Dia juliano del 1 de enero de 1970, origen de las fechas de Qt.</summary>
    private const long DiaJulianoDeLaEpoca = 2_440_588L;

    private Span<byte> Reservar(int cuantos)
    {
        if (_n + cuantos > _datos.Length)
        {
            var nuevo = new byte[Math.Max(_datos.Length * 2, _n + cuantos)];
            Array.Copy(_datos, nuevo, _n);
            _datos = nuevo;
        }
        var destino = _datos.AsSpan(_n, cuantos);
        _n += cuantos;
        return destino;
    }

    /// <summary>Escribe un byte suelto.</summary>
    public void Byte(byte valor) => Reservar(1)[0] = valor;

    /// <summary>Escribe un logico, que ocupa un byte.</summary>
    public void Logico(bool valor) => Byte(valor ? (byte)1 : (byte)0);

    /// <summary>Escribe un entero de 32 bits sin signo.</summary>
    public void UInt32(uint valor) => BinaryPrimitives.WriteUInt32BigEndian(Reservar(4), valor);

    /// <summary>Escribe un entero de 32 bits con signo.</summary>
    public void Int32(int valor) => BinaryPrimitives.WriteInt32BigEndian(Reservar(4), valor);

    /// <summary>Escribe un entero de 64 bits sin signo.</summary>
    public void UInt64(ulong valor) => BinaryPrimitives.WriteUInt64BigEndian(Reservar(8), valor);

    /// <summary>Escribe un entero de 64 bits con signo.</summary>
    public void Int64(long valor) => BinaryPrimitives.WriteInt64BigEndian(Reservar(8), valor);

    /// <summary>Escribe un real de doble precision.</summary>
    public void Doble(double valor) =>
        BinaryPrimitives.WriteInt64BigEndian(Reservar(8), BitConverter.DoubleToInt64Bits(valor));

    /// <summary>Escribe un texto en UTF-8 con su longitud delante. Un nulo va como tal.</summary>
    public void Texto(string? valor)
    {
        if (valor is null)
        {
            UInt32(0xFFFF_FFFFu);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(valor);
        UInt32((uint)bytes.Length);
        bytes.CopyTo(Reservar(bytes.Length));
    }

    /// <summary>Escribe una hora del dia en milisegundos, como hace <c>QTime</c>.</summary>
    public void Hora(TimeSpan valor) => UInt32((uint)Math.Max(0, (long)valor.TotalMilliseconds));

    /// <summary>Escribe una fecha y hora en UTC, como hace <c>QDateTime</c>.</summary>
    public void FechaHoraUtc(DateTimeOffset valor)
    {
        var utc = valor.ToUniversalTime();
        var dias = (long)(utc.UtcDateTime.Date - DateTime.UnixEpoch).TotalDays;
        Int64(DiaJulianoDeLaEpoca + dias);
        UInt32((uint)utc.UtcDateTime.TimeOfDay.TotalMilliseconds);
        Byte(1); // UTC
    }

    /// <summary>
    /// Escribe un color como lo hace <c>QColor</c>: la clase de color y cinco enteros de 16
    /// bits con alfa, rojo, verde, azul y un relleno que Qt no usa pero si escribe.
    /// </summary>
    public void Color(byte rojo, byte verde, byte azul, byte alfa = 255)
    {
        static ushort Escalar(byte v) => (ushort)(v * 257);
        Byte(1); // Rgb
        UInt16(Escalar(alfa));
        UInt16(Escalar(rojo));
        UInt16(Escalar(verde));
        UInt16(Escalar(azul));
        UInt16(0);
    }

    /// <summary>Escribe un color invalido, que es como se pide «deja de resaltar».</summary>
    public void ColorInvalido()
    {
        Byte(0); // Invalid
        for (var i = 0; i < 5; i++) UInt16(0);
    }

    private void UInt16(ushort valor) => BinaryPrimitives.WriteUInt16BigEndian(Reservar(2), valor);

    /// <summary>Devuelve una copia de lo escrito hasta ahora.</summary>
    public byte[] ABytes() => _datos.AsSpan(0, _n).ToArray();
}
