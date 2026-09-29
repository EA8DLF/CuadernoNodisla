using System.Buffers.Binary;
using System.Text;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Lector posicional sobre el cuerpo de un datagrama de WSJT-X.
/// </summary>
/// <remarks>
/// Qt serializa con <c>QDataStream</c> en orden de red —primero el byte mas significativo— y
/// sin ninguna marca que separe un campo del siguiente: la unica forma de saber que hay es
/// contar. Por eso ninguna lectura lanza: devuelven un logico y, cuando el datagrama se acaba
/// antes de lo esperado porque el programa del otro lado es de otra version, los campos que
/// faltan se quedan sin valor y lo que ya se habia leido sigue siendo bueno.
/// </remarks>
public ref struct LectorQDataStream
{
    private readonly ReadOnlySpan<byte> _datos;
    private int _posicion;

    /// <summary>Crea el lector sobre los bytes dados.</summary>
    public LectorQDataStream(ReadOnlySpan<byte> datos)
    {
        _datos = datos;
        _posicion = 0;
    }

    /// <summary>Marca que usa Qt para un texto nulo, distinto de un texto vacio.</summary>
    private const uint LongitudNula = 0xFFFF_FFFFu;

    /// <summary>Dia juliano del 1 de enero de 1970, origen de las fechas de Qt.</summary>
    private const long DiaJulianoDeLaEpoca = 2_440_588L;

    /// <summary>Milisegundos que tiene un dia, tope de una hora valida.</summary>
    private const uint MilisegundosPorDia = 86_400_000u;

    /// <summary>Bytes ya consumidos.</summary>
    public readonly int Posicion => _posicion;

    /// <summary>Bytes que quedan por leer.</summary>
    public readonly int Restante => _datos.Length - _posicion;

    /// <summary>No queda nada por leer.</summary>
    public readonly bool Agotado => _posicion >= _datos.Length;

    private bool TryTomar(int cuantos, out ReadOnlySpan<byte> trozo)
    {
        if (cuantos < 0 || Restante < cuantos)
        {
            trozo = default;
            return false;
        }
        trozo = _datos.Slice(_posicion, cuantos);
        _posicion += cuantos;
        return true;
    }

    /// <summary>Lee un byte suelto (<c>quint8</c>).</summary>
    public bool TryByte(out byte valor)
    {
        if (!TryTomar(1, out var t)) { valor = 0; return false; }
        valor = t[0];
        return true;
    }

    /// <summary>Lee un logico, que Qt escribe como un byte.</summary>
    public bool TryLogico(out bool valor)
    {
        if (!TryByte(out var b)) { valor = false; return false; }
        valor = b != 0;
        return true;
    }

    /// <summary>Lee un entero de 32 bits sin signo (<c>quint32</c>).</summary>
    public bool TryUInt32(out uint valor)
    {
        if (!TryTomar(4, out var t)) { valor = 0; return false; }
        valor = BinaryPrimitives.ReadUInt32BigEndian(t);
        return true;
    }

    /// <summary>Lee un entero de 32 bits con signo (<c>qint32</c>).</summary>
    public bool TryInt32(out int valor)
    {
        if (!TryTomar(4, out var t)) { valor = 0; return false; }
        valor = BinaryPrimitives.ReadInt32BigEndian(t);
        return true;
    }

    /// <summary>Lee un entero de 64 bits sin signo (<c>quint64</c>).</summary>
    public bool TryUInt64(out ulong valor)
    {
        if (!TryTomar(8, out var t)) { valor = 0; return false; }
        valor = BinaryPrimitives.ReadUInt64BigEndian(t);
        return true;
    }

    /// <summary>Lee un entero de 64 bits con signo (<c>qint64</c>).</summary>
    public bool TryInt64(out long valor)
    {
        if (!TryTomar(8, out var t)) { valor = 0; return false; }
        valor = BinaryPrimitives.ReadInt64BigEndian(t);
        return true;
    }

    /// <summary>Lee un real de doble precision (<c>double</c>).</summary>
    public bool TryDoble(out double valor)
    {
        if (!TryTomar(8, out var t)) { valor = 0; return false; }
        valor = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(t));
        return true;
    }

    /// <summary>
    /// Lee un texto: Qt lo escribe como un <c>QByteArray</c> con la longitud delante y los
    /// bytes en UTF-8. La longitud <c>0xFFFFFFFF</c> significa nulo, que no es lo mismo que
    /// vacio; aqui las dos cosas se devuelven como nulo y cadena vacia respectivamente.
    /// </summary>
    public bool TryTexto(out string? valor)
    {
        valor = null;
        if (!TryUInt32(out var longitud)) return false;
        if (longitud == LongitudNula) return true;
        if (longitud > (uint)Restante)
        {
            // El datagrama promete mas texto del que trae: se da por truncado.
            _posicion = _datos.Length;
            return false;
        }
        if (!TryTomar((int)longitud, out var t)) return false;
        valor = Encoding.UTF8.GetString(t);
        return true;
    }

    /// <summary>Lee una hora del dia (<c>QTime</c>), que Qt escribe en milisegundos.</summary>
    public bool TryHora(out TimeSpan valor)
    {
        valor = default;
        if (!TryUInt32(out var ms)) return false;
        if (ms >= MilisegundosPorDia) return true; // hora invalida: se deja en cero
        valor = TimeSpan.FromMilliseconds(ms);
        return true;
    }

    /// <summary>
    /// Lee una fecha y hora (<c>QDateTime</c>): dia juliano, milisegundos del dia y una marca
    /// de huso. Si la marca dice que hay desplazamiento, detras viene en segundos.
    /// </summary>
    /// <remarks>
    /// WSJT-X y JTDX siempre envian UTC, pero la marca se lee de verdad en lugar de darla por
    /// supuesta: un dialecto que envie hora local dejaria el cuaderno con horas falsas, que es
    /// justo el error mas dificil de ver despues.
    /// </remarks>
    public bool TryFechaHoraUtc(out DateTimeOffset valor)
    {
        valor = default;
        if (!TryInt64(out var diaJuliano)) return false;
        if (!TryUInt32(out var milisegundos)) return false;
        if (!TryByte(out var marcaDeHuso)) return false;

        var desplazamiento = TimeSpan.Zero;
        if (marcaDeHuso == 2)
        {
            if (!TryInt32(out var segundos)) return false;
            desplazamiento = TimeSpan.FromSeconds(segundos);
        }

        if (diaJuliano == 0 || milisegundos >= MilisegundosPorDia) return true;

        var dias = diaJuliano - DiaJulianoDeLaEpoca;
        if (dias is < -1_000_000 or > 1_000_000) return true;

        var instante = DateTime.UnixEpoch.AddDays(dias).AddMilliseconds(milisegundos);
        valor = marcaDeHuso switch
        {
            // 0 es hora local del otro extremo: se interpreta como local de esta maquina.
            0 => new DateTimeOffset(DateTime.SpecifyKind(instante, DateTimeKind.Unspecified),
                    TimeZoneInfo.Local.GetUtcOffset(instante)).ToUniversalTime(),
            2 => new DateTimeOffset(DateTime.SpecifyKind(instante, DateTimeKind.Unspecified),
                    desplazamiento).ToUniversalTime(),
            _ => new DateTimeOffset(instante, TimeSpan.Zero),
        };
        return true;
    }
}
