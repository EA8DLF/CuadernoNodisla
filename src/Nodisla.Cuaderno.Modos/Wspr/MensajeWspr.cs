using System.Globalization;
using System.Text;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Modos.Tablas;

namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// Un mensaje de WSPR: lo que cabe en 50 bits.
/// </summary>
/// <remarks>
/// <para>
/// Hay tres tipos, y los tres reparten los 50 bits en un campo de 28 y otro de 22:
/// </para>
/// <list type="number">
/// <item><b>Tipo 1</b>, el normal: <c>EA8DLF IL18 37</c>. Indicativo (28 bits), localizador de
/// cuatro (15) y potencia en dBm (7).</item>
/// <item><b>Tipo 2</b>, indicativo con prefijo o sufijo y sin localizador: <c>PJ4/K1ABC 37</c>,
/// <c>EA8DLF/P 37</c>. El indicativo base va en los 28 bits, y el prefijo o sufijo en los 15
/// que dejaba libres el localizador, mas un bit escondido en la potencia.</item>
/// <item><b>Tipo 3</b>, localizador de seis y resumen del indicativo: <c>&lt;PJ4/K1ABC&gt; FK52UD 37</c>.
/// El localizador de seis va disfrazado de indicativo en los 28 bits, y el resumen de 15 bits
/// del indicativo en el hueco del localizador.</item>
/// </list>
/// <para>
/// <b>Como se distinguen.</b> El truco esta en la potencia. Solo valen potencias que acaban en
/// 0, 3 o 7, y el campo de 7 bits lleva la potencia mas 64 en el tipo 1. En el tipo 2 se le suma
/// 1 o 2 (con lo que ya no acaba en 0, 3 ni 7), y ese sumando es ademas el bit 16 del prefijo o
/// sufijo. En el tipo 3 la potencia va en negativo: <c>-(potencia + 1)</c>.
/// </para>
/// <para>
/// <b>Esta clase es tambien la gramatica.</b> Desempaquetar no devuelve nada a menos que el
/// indicativo tenga forma de indicativo, el localizador este dentro del mapa y la potencia sea
/// de las permitidas. Un decodificador secuencial puede terminar en un camino cualquiera cuando
/// hay ruido, y estas comprobaciones son la ultima puerta antes de que un mensaje inventado
/// se convierta en un spot de propagacion que nunca existio.
/// </para>
/// <para>
/// Escrito desde la especificacion del protocolo (WSPR 2.0 User's Guide) y la descripcion del
/// proceso de codificacion de G4JNT. Ver <see cref="TablasWspr"/>.
/// </para>
/// </remarks>
public sealed record MensajeWspr
{
    /// <summary>Bits de informacion de un mensaje.</summary>
    public const int Bits = 50;

    /// <summary>Texto con el que se muestra un resumen de tipo 3 que no se ha podido resolver.</summary>
    public const string ResumenDesconocido = "<...>";

    private const string Alfabeto37 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ ";
    private const int Espacio = 36;
    private const int LimiteDelPrefijo = 60000;

    private MensajeWspr(int tipo, string indicativo, string localizador, int potenciaDbm, int resumen)
    {
        Tipo = tipo;
        Indicativo = indicativo;
        Localizador = localizador;
        PotenciaDbm = potenciaDbm;
        Resumen = resumen;
    }

    /// <summary>1, 2 o 3. Ver la nota de la clase.</summary>
    public int Tipo { get; }

    /// <summary>
    /// Indicativo completo, con prefijo o sufijo si lo lleva. En un tipo 3 recibido cuyo resumen
    /// no se conoce, vale <see cref="ResumenDesconocido"/>.
    /// </summary>
    public string Indicativo { get; }

    /// <summary>Localizador de cuatro (tipo 1), de seis (tipo 3) o vacio (tipo 2).</summary>
    public string Localizador { get; }

    /// <summary>Potencia declarada, en dBm.</summary>
    public int PotenciaDbm { get; }

    /// <summary>Resumen de 15 bits del indicativo, solo en el tipo 3.</summary>
    public int Resumen { get; }

    /// <summary>El indicativo se conoce de verdad y no es un resumen sin resolver.</summary>
    public bool IndicativoConocido => Indicativo != ResumenDesconocido;

    /// <summary>Texto tal y como se muestra, con la forma de siempre.</summary>
    public string Texto => Tipo switch
    {
        1 => $"{Indicativo} {Localizador} {PotenciaDbm}",
        2 => $"{Indicativo} {PotenciaDbm}",
        _ => $"<{(IndicativoConocido ? Indicativo : "...")}> {Localizador} {PotenciaDbm}",
    };

    /// <inheritdoc/>
    public override string ToString() => Texto;

    /// <summary>Mensaje de tipo 1.</summary>
    /// <exception cref="FormatException">Si algo no cabe en la gramatica.</exception>
    public static MensajeWspr Tipo1(string indicativo, string localizador, int potenciaDbm)
    {
        var ind = NormalizarIndicativo(indicativo);
        if (!TryComprimirIndicativo(ind, out _)) throw new FormatException($"El indicativo «{indicativo}» no cabe en un mensaje de tipo 1.");
        var loc = localizador.Trim().ToUpperInvariant();
        if (!TryComprimirLocalizador(loc, out _)) throw new FormatException($"El localizador «{localizador}» no vale: hacen falta cuatro caracteres, de AA00 a RR99.");
        if (!TablasWspr.EsPotenciaValida(potenciaDbm)) throw new FormatException($"La potencia {potenciaDbm} dBm no es de las que admite WSPR (0, 3, 7, 10, 13... 60).");
        return new MensajeWspr(1, ind, loc, potenciaDbm, 0);
    }

    /// <summary>Mensaje de tipo 2: indicativo con prefijo o sufijo, sin localizador.</summary>
    /// <exception cref="FormatException">Si algo no cabe en la gramatica.</exception>
    public static MensajeWspr Tipo2(string indicativoCompuesto, int potenciaDbm)
    {
        var ind = NormalizarIndicativo(indicativoCompuesto);
        if (!TryComprimirCompuesto(ind, out _, out _, out _)) throw new FormatException($"El indicativo «{indicativoCompuesto}» no cabe en un mensaje de tipo 2: hace falta un prefijo de hasta tres caracteres o un sufijo de un caracter o de dos cifras.");
        if (!TablasWspr.EsPotenciaValida(potenciaDbm)) throw new FormatException($"La potencia {potenciaDbm} dBm no es de las que admite WSPR.");
        return new MensajeWspr(2, ind, string.Empty, potenciaDbm, 0);
    }

    /// <summary>Mensaje de tipo 3: resumen del indicativo compuesto y localizador de seis.</summary>
    /// <exception cref="FormatException">Si algo no cabe en la gramatica.</exception>
    public static MensajeWspr Tipo3(string indicativoCompuesto, string localizador6, int potenciaDbm)
    {
        var ind = NormalizarIndicativo(indicativoCompuesto);
        if (!Dominio.Valores.Indicativo.EsFormaValida(ind)) throw new FormatException($"El indicativo «{indicativoCompuesto}» no tiene forma de indicativo.");
        var loc = localizador6.Trim().ToUpperInvariant();
        if (loc.Length != 6 || !Locator.TryParse(loc, out _)) throw new FormatException($"El localizador «{localizador6}» no vale: hacen falta seis caracteres.");
        if (!TablasWspr.EsPotenciaValida(potenciaDbm)) throw new FormatException($"La potencia {potenciaDbm} dBm no es de las que admite WSPR.");
        return new MensajeWspr(3, ind, loc, potenciaDbm, HashDeIndicativo.Calcular(ind));
    }

    /// <summary>
    /// Interpreta el texto de un mensaje con la forma en que se muestra.
    /// </summary>
    /// <param name="texto">
    /// <c>EA8DLF IL18 37</c>, <c>PJ4/K1ABC 37</c>, <c>&lt;PJ4/K1ABC&gt; FK52UD 37</c>. Un indicativo
    /// compuesto con localizador de cuatro se manda como tipo 2, sin el localizador, que es lo
    /// que hace el protocolo.
    /// </param>
    /// <param name="mensaje">El mensaje, si el texto tiene sentido.</param>
    /// <param name="motivo">Por que no lo tiene.</param>
    public static bool TryAnalizar(string? texto, out MensajeWspr mensaje, out string motivo)
    {
        mensaje = null!;
        motivo = string.Empty;
        var partes = (texto ?? string.Empty).Trim().ToUpperInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        try
        {
            switch (partes.Length)
            {
                case 2 when partes[0].Contains('/'):
                    mensaje = Tipo2(partes[0], LeerPotencia(partes[1]));
                    return true;
                case 3 when partes[0].StartsWith('<') && partes[0].EndsWith('>'):
                    mensaje = Tipo3(partes[0][1..^1], partes[1], LeerPotencia(partes[2]));
                    return true;
                case 3 when partes[0].Contains('/'):
                    if (partes[1].Length == 6)
                    {
                        mensaje = Tipo3(partes[0], partes[1], LeerPotencia(partes[2]));
                        return true;
                    }
                    mensaje = Tipo2(partes[0], LeerPotencia(partes[2]));
                    return true;
                case 3:
                    mensaje = Tipo1(partes[0], partes[1], LeerPotencia(partes[2]));
                    return true;
                default:
                    motivo = "Un mensaje de WSPR es «indicativo localizador potencia», «indicativo/sufijo potencia» o «<indicativo> localizador6 potencia».";
                    return false;
            }
        }
        catch (FormatException e)
        {
            motivo = e.Message;
            return false;
        }
    }

    /// <summary>Los 50 bits del mensaje, uno por byte y el mas significativo primero.</summary>
    public byte[] Empaquetar()
    {
        uint n;
        uint m;
        switch (Tipo)
        {
            case 1:
                TryComprimirIndicativo(Indicativo, out n);
                TryComprimirLocalizador(Localizador, out var ng);
                m = (ng << 7) | (uint)(PotenciaDbm + 64);
                break;
            case 2:
                TryComprimirCompuesto(Indicativo, out var baseDelIndicativo, out var n2, out _);
                TryComprimirIndicativo(baseDelIndicativo, out n);
                var nadd = 1 + (n2 >> 15);
                m = ((n2 & 0x7FFF) << 7) | (uint)(PotenciaDbm + nadd + 64);
                break;
            default:
                // El localizador de seis se gira para que tenga forma de indicativo: las cifras
                // caen en la tercera posicion, que es donde el campo exige un digito.
                TryComprimirIndicativo(Localizador[1..] + Localizador[0], out n);
                m = ((uint)Resumen << 7) | (uint)(-(PotenciaDbm + 1) + 64);
                break;
        }

        var bits = new byte[Bits];
        for (var i = 0; i < 28; i++) bits[i] = (byte)((n >> (27 - i)) & 1);
        for (var i = 0; i < 22; i++) bits[28 + i] = (byte)((m >> (21 - i)) & 1);
        return bits;
    }

    /// <summary>
    /// Reconstruye el mensaje a partir de sus 50 bits, o dice que no tienen sentido.
    /// </summary>
    /// <param name="bits">Los 50 bits, uno por byte y el mas significativo primero.</param>
    /// <param name="resolverResumen">
    /// Quien sepa a que indicativo corresponde un resumen de tipo 3; puede devolver nulo.
    /// </param>
    /// <param name="mensaje">El mensaje, si pasa la gramatica.</param>
    /// <returns>Falso si los bits no forman un mensaje valido. Entonces no hay nada que mostrar.</returns>
    public static bool TryDesempaquetar(ReadOnlySpan<byte> bits, Func<int, string?>? resolverResumen, out MensajeWspr mensaje)
    {
        mensaje = null!;
        if (bits.Length < Bits) return false;

        uint n = 0;
        for (var i = 0; i < 28; i++) n = (n << 1) | (uint)(bits[i] & 1);
        uint m = 0;
        for (var i = 0; i < 22; i++) m = (m << 1) | (uint)(bits[28 + i] & 1);

        var ng = (int)(m >> 7);
        var ntype = (int)(m & 127) - 64;

        if (ntype < 0)
        {
            // Tipo 3.
            var potencia = -(ntype + 1);
            if (!TablasWspr.EsPotenciaValida(potencia)) return false;
            if (!TryDescomprimirIndicativo(n, out var girado)) return false;
            if (girado.Length != 6) return false;
            var localizador = girado[5] + girado[..5];
            if (!Locator.TryParse(localizador, out var loc) || loc.Valor.Length != 6) return false;
            var indicativo = resolverResumen?.Invoke(ng);
            if (indicativo is not null && HashDeIndicativo.Calcular(indicativo) != ng) indicativo = null;
            mensaje = new MensajeWspr(3, indicativo ?? ResumenDesconocido, loc.Valor, potencia, ng);
            return true;
        }

        var nu = ntype % 10;
        if (nu is 0 or 3 or 7)
        {
            // Tipo 1.
            if (!TablasWspr.EsPotenciaValida(ntype)) return false;
            if (!TryDescomprimirIndicativo(n, out var indicativo)) return false;
            if (!TryDescomprimirLocalizador(ng, out var localizador)) return false;
            mensaje = new MensajeWspr(1, indicativo, localizador, ntype, 0);
            return true;
        }

        // Tipo 2.
        var nadd = nu is 1 or 4 or 8 ? 1 : 2;
        var potencia2 = ntype - nadd;
        if (!TablasWspr.EsPotenciaValida(potencia2)) return false;
        if (!TryDescomprimirIndicativo(n, out var baseDelIndicativo)) return false;
        var n2 = ng + (32768 * (nadd - 1));
        string compuesto;
        if (n2 < LimiteDelPrefijo)
        {
            var c3 = n2 % 37;
            var c2 = (n2 / 37) % 37;
            var c1 = n2 / (37 * 37);
            if (c1 > 36) return false;
            var prefijo = new string([Alfabeto37[c1], Alfabeto37[c2], Alfabeto37[c3]]).Trim();
            if (prefijo.Length == 0 || prefijo.Contains(' ')) return false;
            compuesto = prefijo + "/" + baseDelIndicativo;
        }
        else
        {
            var n3 = n2 - LimiteDelPrefijo;
            string sufijo;
            if (n3 < 36) sufijo = Alfabeto37[n3].ToString();
            else if (n3 < 126) sufijo = (n3 - 36 + 10).ToString(CultureInfo.InvariantCulture);
            else return false;
            compuesto = baseDelIndicativo + "/" + sufijo;
        }
        mensaje = new MensajeWspr(2, compuesto, string.Empty, potencia2, 0);
        return true;
    }

    private static int LeerPotencia(string texto) =>
        int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
            ? p
            : throw new FormatException($"La potencia «{texto}» no es un numero.");

    private static string NormalizarIndicativo(string? indicativo) =>
        (indicativo ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>
    /// Comprime un indicativo corriente en 28 bits.
    /// </summary>
    /// <remarks>
    /// El campo tiene seis casillas con alfabetos distintos: la primera admite letra, cifra o
    /// espacio; la segunda letra o cifra; la tercera <b>tiene que ser cifra</b>; y las tres
    /// ultimas admiten letra o espacio. Un indicativo cuya cifra esta en la segunda posicion
    /// (<c>K1ABC</c>) se corre un sitio metiendole un espacio delante. Uno que no encaje ni asi
    /// no cabe, y no se inventa nada.
    /// </remarks>
    public static bool TryComprimirIndicativo(string indicativo, out uint n)
    {
        n = 0;
        var s = indicativo;
        if (s.Length < 3 || s.Length > 6) return false;
        if (!char.IsAsciiDigit(s[2]))
        {
            if (!char.IsAsciiDigit(s[1]) || s.Length > 5) return false;
            s = " " + s;
        }
        s = s.PadRight(6);

        var c1 = Alfabeto37.IndexOf(s[0], StringComparison.Ordinal);
        var c2 = Alfabeto37.IndexOf(s[1], StringComparison.Ordinal);
        if (c1 < 0 || c2 < 0 || c2 == Espacio) return false;
        var c3 = s[2] - '0';
        var resto = new int[3];
        for (var i = 0; i < 3; i++)
        {
            var c = s[3 + i];
            if (c == ' ') resto[i] = 26;
            else if (char.IsAsciiLetterUpper(c)) resto[i] = c - 'A';
            else return false;
        }

        n = (uint)c1;
        n = (n * 36) + (uint)c2;
        n = (n * 10) + (uint)c3;
        n = (n * 27) + (uint)resto[0];
        n = (n * 27) + (uint)resto[1];
        n = (n * 27) + (uint)resto[2];
        return true;
    }

    /// <summary>Deshace <see cref="TryComprimirIndicativo"/> y comprueba que lo que sale tiene forma de indicativo.</summary>
    public static bool TryDescomprimirIndicativo(uint n, out string indicativo)
    {
        indicativo = string.Empty;
        var c = new char[6];
        c[5] = LetraOEspacio((int)(n % 27)); n /= 27;
        c[4] = LetraOEspacio((int)(n % 27)); n /= 27;
        c[3] = LetraOEspacio((int)(n % 27)); n /= 27;
        c[2] = (char)('0' + (int)(n % 10)); n /= 10;
        var c2 = (int)(n % 36); n /= 36;
        if (n > 36) return false;
        c[1] = Alfabeto37[c2];
        c[0] = Alfabeto37[(int)n];

        var texto = new string(c).Trim();
        // Solo se admiten espacios delante (uno) o detras; uno en medio no es un indicativo.
        if (texto.Contains(' ')) return false;
        if (!Dominio.Valores.Indicativo.EsFormaValida(texto)) return false;
        indicativo = texto;
        return true;
    }

    private static char LetraOEspacio(int v) => v == 26 ? ' ' : (char)('A' + v);

    /// <summary>Localizador de cuatro caracteres a 15 bits.</summary>
    public static bool TryComprimirLocalizador(string localizador, out uint ng)
    {
        ng = 0;
        if (localizador.Length != 4 || !Locator.TryParse(localizador, out _)) return false;
        var lon = localizador[0] - 'A';
        var lat = localizador[1] - 'A';
        var lonCifra = localizador[2] - '0';
        var latCifra = localizador[3] - '0';
        ng = (uint)(((179 - (10 * lon) - lonCifra) * 180) + (10 * lat) + latCifra);
        return true;
    }

    /// <summary>De 15 bits a localizador de cuatro, o falso si cae fuera del mapa.</summary>
    public static bool TryDescomprimirLocalizador(int ng, out string localizador)
    {
        localizador = string.Empty;
        if (ng < 0 || ng >= 32400) return false;
        var latParte = ng % 180;
        var lonParte = 179 - (ng / 180);
        var lon = lonParte / 10;
        var lat = latParte / 10;
        if (lon > 17 || lat > 17) return false;
        localizador = new string([(char)('A' + lon), (char)('A' + lat), (char)('0' + (lonParte % 10)), (char)('0' + (latParte % 10))]);
        return true;
    }

    /// <summary>
    /// Separa un indicativo compuesto en su base y su prefijo o sufijo codificado en 16 bits.
    /// </summary>
    /// <remarks>
    /// El prefijo son hasta tres caracteres del alfabeto de 37 (letras, cifras y espacio) en
    /// base 37; el sufijo, un solo caracter (0 a 35) o un numero de dos cifras (36 en adelante),
    /// sumado a 60000 para que no se confunda con un prefijo, que como mucho llega a 50652.
    /// </remarks>
    public static bool TryComprimirCompuesto(string compuesto, out string baseDelIndicativo, out uint n2, out bool esPrefijo)
    {
        baseDelIndicativo = string.Empty;
        n2 = 0;
        esPrefijo = false;
        var partes = compuesto.Split('/');
        if (partes.Length != 2 || partes[0].Length == 0 || partes[1].Length == 0) return false;

        // Primero se mira si la segunda parte es un sufijo (un caracter o dos cifras) y la
        // primera un indicativo; si no, si la primera es un prefijo de hasta tres caracteres
        // y la segunda el indicativo. Un prefijo como PJ4 tiene forma de indicativo, asi que
        // el orden de las dos comprobaciones importa.
        var esSufijo = partes[1].Length == 1
            || (partes[1].Length == 2 && char.IsAsciiDigit(partes[1][0]) && char.IsAsciiDigit(partes[1][1]));
        if (!(esSufijo && TryComprimirIndicativo(partes[0], out _))
            && partes[0].Length <= 3 && TryComprimirIndicativo(partes[1], out _))
        {
            // Prefijo delante de la base: PJ4/K1ABC.
            var prefijo = partes[0].PadLeft(3);
            uint valor = 0;
            foreach (var ch in prefijo)
            {
                var v = Alfabeto37.IndexOf(ch, StringComparison.Ordinal);
                if (v < 0) return false;
                valor = (valor * 37) + (uint)v;
            }
            baseDelIndicativo = partes[1];
            n2 = valor;
            esPrefijo = true;
            return true;
        }

        if (!TryComprimirIndicativo(partes[0], out _)) return false;
        var sufijo = partes[1];
        uint codigo;
        if (sufijo.Length == 1)
        {
            var v = Alfabeto37.IndexOf(sufijo[0], StringComparison.Ordinal);
            if (v < 0 || v == Espacio) return false;
            codigo = (uint)v;
        }
        else if (sufijo.Length == 2 && char.IsAsciiDigit(sufijo[0]) && char.IsAsciiDigit(sufijo[1]) && sufijo[0] != '0')
        {
            codigo = (uint)(36 + int.Parse(sufijo, CultureInfo.InvariantCulture) - 10);
        }
        else
        {
            return false;
        }
        baseDelIndicativo = partes[0];
        n2 = LimiteDelPrefijo + codigo;
        return true;
    }

    /// <summary>Texto para el registro: tipo y campos, para depurar sin ambiguedad.</summary>
    public string Describir()
    {
        var sb = new StringBuilder();
        sb.Append("tipo ").Append(Tipo).Append(", ").Append(Indicativo);
        if (Localizador.Length > 0) sb.Append(' ').Append(Localizador);
        sb.Append(", ").Append(PotenciaDbm).Append(" dBm");
        return sb.ToString();
    }
}
