using System.Globalization;
using System.Text;

namespace Nodisla.Cuaderno.Radio.Control.Icom;

/// <summary>
/// Una trama CI-V: <c>FE FE destino origen orden [suborden] [datos] FD</c>.
/// </summary>
/// <remarks>
/// Formato de los manuales CI-V de ICOM (IC-7300 «Full manual» cap. 19, IC-705/IC-7610/IC-9700
/// «CI-V Reference Guide»): dos bytes de preambulo <c>FE</c>, la direccion de quien recibe, la
/// de quien manda, la orden y lo que siga, y <c>FD</c> al final. La radio contesta
/// <c>FB</c> (bien) o <c>FA</c> (no lo admite) a las ordenes que no devuelven datos.
/// </remarks>
/// <param name="Destino">Direccion de quien recibe (<c>0x94</c> un IC-7300, <c>0xE0</c> el ordenador, <c>0x00</c> todos).</param>
/// <param name="Origen">Direccion de quien manda.</param>
/// <param name="Cuerpo">Orden, suborden y datos, sin preambulo ni <c>FD</c>.</param>
public sealed record TramaCiv(byte Destino, byte Origen, byte[] Cuerpo)
{
    /// <summary>Byte del preambulo.</summary>
    public const byte Preambulo = 0xFE;

    /// <summary>Byte de fin de trama.</summary>
    public const byte Fin = 0xFD;

    /// <summary>Respuesta «bien».</summary>
    public const byte Bien = 0xFB;

    /// <summary>Respuesta «no lo admito».</summary>
    public const byte NoAdmitido = 0xFA;

    /// <summary>Codigo de colision del bus (<c>FC</c>): la trama se ha perdido.</summary>
    public const byte Colision = 0xFC;

    /// <summary>Direccion de difusion: la usa la radio en el modo transceive.</summary>
    public const byte Difusion = 0x00;

    /// <summary>Direccion del ordenador por omision en todos los manuales.</summary>
    public const byte DireccionDelOrdenador = 0xE0;

    /// <summary>La orden (primer byte del cuerpo), o -1 si no hay cuerpo.</summary>
    public int Orden => Cuerpo.Length > 0 ? Cuerpo[0] : -1;

    /// <summary>Es la respuesta «bien» (<c>FB</c>).</summary>
    public bool EsBien => Cuerpo is [Bien];

    /// <summary>Es la respuesta «no lo admito» (<c>FA</c>).</summary>
    public bool EsNoAdmitido => Cuerpo is [NoAdmitido];

    /// <summary>Los bytes de la trama completa, listos para mandar.</summary>
    public byte[] ABytes()
    {
        var bytes = new byte[Cuerpo.Length + 5];
        bytes[0] = Preambulo;
        bytes[1] = Preambulo;
        bytes[2] = Destino;
        bytes[3] = Origen;
        Cuerpo.CopyTo(bytes, 4);
        bytes[^1] = Fin;
        return bytes;
    }

    /// <summary>El cuerpo empieza por estos bytes (la orden y la suborden que se preguntaron).</summary>
    /// <param name="prefijo">Orden y suborden.</param>
    /// <returns>Verdadero si coincide.</returns>
    public bool EmpiezaPor(ReadOnlySpan<byte> prefijo) => Cuerpo.AsSpan().StartsWith(prefijo);

    /// <summary>Los datos que siguen a un prefijo de orden.</summary>
    /// <param name="longitudDelPrefijo">Bytes de orden y suborden.</param>
    /// <returns>Los datos.</returns>
    public byte[] DatosTras(int longitudDelPrefijo) =>
        Cuerpo.Length > longitudDelPrefijo ? Cuerpo[longitudDelPrefijo..] : [];

    /// <inheritdoc />
    public override string ToString() => Hex.De(ABytes());
}

/// <summary>Paso de bytes a texto hexadecimal y al reves, para el registro y la orden en crudo.</summary>
public static class Hex
{
    /// <summary>Bytes a <c>FE FE 94 E0 03 FD</c>.</summary>
    /// <param name="bytes">Bytes.</param>
    /// <returns>El texto.</returns>
    public static string De(ReadOnlySpan<byte> bytes)
    {
        var texto = new StringBuilder(bytes.Length * 3);
        foreach (var b in bytes)
        {
            if (texto.Length > 0) texto.Append(' ');
            texto.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return texto.ToString();
    }

    /// <summary>Texto hexadecimal (con o sin espacios) a bytes.</summary>
    /// <param name="texto">Por ejemplo <c>14 0A</c> o <c>140A</c>.</param>
    /// <returns>Los bytes.</returns>
    /// <exception cref="FormatException">Si no es hexadecimal par.</exception>
    public static byte[] ABytes(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var limpio = new string(texto.Where(c => !char.IsWhiteSpace(c) && c != ',' && c != '-').ToArray());
        if (limpio.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) limpio = limpio[2..];
        if (limpio.Length == 0 || limpio.Length % 2 != 0)
        {
            throw new FormatException($"«{texto}» no son bytes en hexadecimal.");
        }

        return Convert.FromHexString(limpio);
    }
}

/// <summary>
/// Numeros en BCD como los usa el CI-V.
/// </summary>
/// <remarks>
/// Las frecuencias van en BCD <b>al reves</b>: el primer byte lleva las decenas y unidades de
/// hercio y el ultimo los gigahercios (manual IC-7300, «Operating frequency»). Los niveles
/// (<c>14 xx</c>, <c>15 xx</c>) van en BCD <b>al derecho</b> en dos bytes: <c>02 55</c> es 255.
/// </remarks>
public static class BcdCiv
{
    /// <summary>Frecuencia en hercios a BCD invertido.</summary>
    /// <param name="hercios">Frecuencia.</param>
    /// <param name="bytes">5 en todos los modelos de este cuaderno (hasta 9,99 GHz); 6 en el IC-905.</param>
    /// <returns>Los bytes.</returns>
    public static byte[] Frecuencia(long hercios, int bytes = 5)
    {
        var tope = (long)Math.Pow(10, bytes * 2);
        if (hercios < 0 || hercios >= tope)
        {
            throw new ArgumentOutOfRangeException(nameof(hercios), hercios, $"No cabe en {bytes} bytes BCD.");
        }

        var resultado = new byte[bytes];
        for (var i = 0; i < bytes; i++)
        {
            var dos = (int)(hercios % 100);
            resultado[i] = (byte)(((dos / 10) << 4) | (dos % 10));
            hercios /= 100;
        }

        return resultado;
    }

    /// <summary>BCD invertido a hercios.</summary>
    /// <param name="datos">Bytes de la frecuencia.</param>
    /// <returns>Hercios, o nulo si algun digito no es BCD.</returns>
    public static long? Hercios(ReadOnlySpan<byte> datos)
    {
        if (datos.Length == 0) return null;
        long valor = 0;
        for (var i = datos.Length - 1; i >= 0; i--)
        {
            var alto = datos[i] >> 4;
            var bajo = datos[i] & 0x0F;
            if (alto > 9 || bajo > 9) return null;
            valor = (valor * 100) + (alto * 10) + bajo;
        }

        return valor;
    }

    /// <summary>Numero a BCD al derecho en tantos bytes como se pidan (<c>255</c> → <c>02 55</c>).</summary>
    /// <param name="valor">Numero.</param>
    /// <param name="bytes">Bytes.</param>
    /// <returns>Los bytes.</returns>
    public static byte[] Numero(int valor, int bytes)
    {
        var tope = (int)Math.Pow(10, bytes * 2);
        if (valor < 0 || valor >= tope)
        {
            throw new ArgumentOutOfRangeException(nameof(valor), valor, $"No cabe en {bytes} bytes BCD.");
        }

        var resultado = new byte[bytes];
        for (var i = bytes - 1; i >= 0; i--)
        {
            var dos = valor % 100;
            resultado[i] = (byte)(((dos / 10) << 4) | (dos % 10));
            valor /= 100;
        }

        return resultado;
    }

    /// <summary>BCD al derecho a numero.</summary>
    /// <param name="datos">Bytes.</param>
    /// <returns>El numero, o nulo si no es BCD.</returns>
    public static int? Numero(ReadOnlySpan<byte> datos)
    {
        if (datos.Length == 0) return null;
        var valor = 0;
        foreach (var b in datos)
        {
            var alto = b >> 4;
            var bajo = b & 0x0F;
            if (alto > 9 || bajo > 9) return null;
            valor = (valor * 100) + (alto * 10) + bajo;
        }

        return valor;
    }
}

/// <summary>
/// Corta en tramas lo que llega por el bus CI-V.
/// </summary>
/// <remarks>
/// Por el bus llega de todo: el <b>eco</b> de lo que mandamos (el CI-V es un bus de un solo hilo
/// y por el conector REMOTE, o por USB con «CI-V USB Echo Back», se oye a si mismo), las
/// respuestas, los avisos del transceive y a veces restos de una colision. Esto solo junta
/// bytes hasta cada <c>FD</c>; quien lo usa decide que es cada trama por sus direcciones.
/// </remarks>
public sealed class AnalizadorDeTramasCiv
{
    private readonly List<byte> _pendiente = [];

    /// <summary>Anade bytes recibidos y devuelve las tramas completas que haya.</summary>
    /// <param name="bytes">Bytes recibidos.</param>
    /// <returns>Tramas completas, en orden.</returns>
    public IReadOnlyList<TramaCiv> Anadir(ReadOnlySpan<byte> bytes)
    {
        List<TramaCiv> tramas = [];
        foreach (var b in bytes)
        {
            _pendiente.Add(b);
            if (b != TramaCiv.Fin) continue;

            var trama = Cortar();
            if (trama is not null) tramas.Add(trama);
        }

        // Basura sin FD que crece sin fin: un puerto que no es una radio ICOM.
        if (_pendiente.Count > 4096) _pendiente.Clear();
        return tramas;
    }

    /// <summary>Olvida lo que hubiera a medias.</summary>
    public void Vaciar() => _pendiente.Clear();

    private TramaCiv? Cortar()
    {
        var bytes = _pendiente.ToArray();
        _pendiente.Clear();

        // El inicio es el ultimo par FE FE seguido de algo que no sea FE (la radio puede mandar
        // mas preambulos, por ejemplo al despertarla).
        var inicio = -1;
        for (var i = 0; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == TramaCiv.Preambulo && bytes[i + 1] == TramaCiv.Preambulo)
            {
                var j = i + 2;
                while (j < bytes.Length && bytes[j] == TramaCiv.Preambulo) j++;
                inicio = j;
                i = j - 1;
            }
        }

        // Hace falta destino y origen; una trama de colision (FC) se descarta.
        if (inicio < 0 || bytes.Length - 1 - inicio < 2) return null;
        var destino = bytes[inicio];
        var origen = bytes[inicio + 1];
        if (destino == TramaCiv.Colision || origen == TramaCiv.Colision) return null;
        var cuerpo = bytes[(inicio + 2)..^1];
        return new TramaCiv(destino, origen, cuerpo);
    }
}
