using System.Text;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Modos.Jt65;

/// <summary>Un mensaje de 72 bits ya entendido.</summary>
/// <param name="Texto">El mensaje tal como se muestra.</param>
/// <param name="EsTextoLibre">Era texto libre de 13 caracteres, no un mensaje estructurado.</param>
public sealed record MensajeJt(string Texto, bool EsTextoLibre)
{
    /// <summary>Indicativo que llama (el segundo campo), si lo hay.</summary>
    public Indicativo Llamante { get; init; }

    /// <summary>Indicativo llamado (el primer campo). <c>CQ</c> no es un indicativo.</summary>
    public Indicativo Llamado { get; init; }

    /// <summary>Localizador del tercer campo, si lo hay.</summary>
    public Locator Locator { get; init; }

    /// <summary>Es una llamada general (CQ, CQ DX, CQ nnn o QRZ).</summary>
    public bool EsCq { get; init; }

    /// <summary>Informe de senal del tercer campo, si lo hay (−01 a −30).</summary>
    public int? Informe { get; init; }
}

/// <summary>
/// Empaqueta y desempaqueta los mensajes de 72 bits que comparten JT65 y JT9.
/// </summary>
/// <remarks>
/// <para>
/// <b>El formato.</b> Tres campos: 28 bits para el primer indicativo, 28 para el segundo y 16
/// para el localizador o el informe. Con 28 bits caben todos los indicativos de la forma
/// corriente (prefijo de una o dos letras o letra y numero, un digito, sufijo de hasta tres
/// letras) mas unos pocos valores especiales (CQ, QRZ, DE, CQ con frecuencia). En el tercer
/// campo, los 15 bits bajos llevan el localizador de cuatro caracteres o un informe, y el bit
/// alto marca que el mensaje no es estructurado sino <b>texto libre</b>: trece caracteres de un
/// alfabeto de 42 signos, que caben en 71 bits (42^13 &lt; 2^71).
/// </para>
/// <para>
/// <b>Procedencia.</b> Es el formato descrito por J. Taylor en «The JT65 Communications
/// Protocol» (QEX, 2005) y en la guia de WSJT-X; se ha implementado desde esa descripcion.
/// Las constantes (bases de los indicativos especiales, base de los informes, alfabeto del
/// texto libre) son del protocolo.
/// </para>
/// <para>
/// <b>Lo que no se hace.</b> Los indicativos compuestos (<c>EA8/DL1ABC</c>, <c>EA8DLF/P</c>)
/// tienen en JT65 un mecanismo de prefijos y sufijos que roba el segundo campo y que casi no
/// se usa; aqui no se generan y, si llegan, se rechazan. Ante la duda, nada.
/// </para>
/// </remarks>
public static class MensajeDe72Bits
{
    /// <summary>Bits del mensaje.</summary>
    public const int Bits = 72;

    /// <summary>Caracteres de un mensaje de texto libre.</summary>
    public const int CaracteresDeTextoLibre = 13;

    /// <summary>Alfabeto del texto libre; el indice de cada caracter es su valor.</summary>
    public const string AlfabetoDeTextoLibre = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ +-./?";

    /// <summary>Base de los indicativos corrientes: 37 · 36 · 10 · 27 · 27 · 27.</summary>
    private const long BaseDeIndicativos = 37L * 36 * 10 * 27 * 27 * 27; // 262 177 560

    private const long ValorCq = BaseDeIndicativos + 1;
    private const long ValorQrz = BaseDeIndicativos + 2;
    private const long PrimerCqConFrecuencia = BaseDeIndicativos + 3;   // CQ 000 .. CQ 999
    private const long UltimoCqConFrecuencia = BaseDeIndicativos + 3 + 999;
    private const long ValorDe = 267_796_945;
    private const long TopeDe28Bits = 1L << 28;

    /// <summary>Base de los informes en el tercer campo: 180 · 180 localizadores por debajo.</summary>
    private const int BaseDeInformes = 180 * 180; // 32 400
    private const int MarcaDeTextoLibre = 1 << 15;

    /// <summary>
    /// Empaqueta un mensaje.
    /// </summary>
    /// <param name="texto">Mensaje: «CQ EA8DLF IL18», «EA1ABC EA8DLF -15», «EA1ABC EA8DLF RRR» o texto libre.</param>
    /// <param name="bits">72 bits, un byte por bit, del mas significativo al menos.</param>
    /// <param name="motivo">Por que no cupo, si no cupo.</param>
    public static bool TryEmpaquetar(string? texto, out byte[] bits, out string motivo)
    {
        bits = [];
        motivo = string.Empty;
        var limpio = (texto ?? string.Empty).Trim().ToUpperInvariant();
        while (limpio.Contains("  ", StringComparison.Ordinal)) limpio = limpio.Replace("  ", " ", StringComparison.Ordinal);
        if (limpio.Length == 0) { motivo = "El mensaje esta vacio."; return false; }

        if (TryEmpaquetarEstructurado(limpio, out var nc1, out var nc2, out var ng))
        {
            bits = Componer(nc1, nc2, ng);
            return true;
        }

        if (limpio.Length > CaracteresDeTextoLibre)
        {
            motivo = $"No es un mensaje estructurado y como texto libre pasa de {CaracteresDeTextoLibre} caracteres.";
            return false;
        }
        foreach (var c in limpio)
            if (!AlfabetoDeTextoLibre.Contains(c, StringComparison.Ordinal))
            {
                motivo = $"El caracter '{c}' no existe en el alfabeto del texto libre.";
                return false;
            }

        EmpaquetarTextoLibre(limpio.PadRight(CaracteresDeTextoLibre), out nc1, out nc2, out ng);
        bits = Componer(nc1, nc2, ng);
        return true;
    }

    /// <summary>
    /// Desempaqueta 72 bits.
    /// </summary>
    /// <param name="bits">72 bits, un byte por bit.</param>
    /// <param name="mensaje">El mensaje, si los bits tienen sentido.</param>
    /// <returns>Falso si los bits no forman un mensaje valido: un indicativo imposible, un localizador fuera de rango...</returns>
    public static bool TryDesempaquetar(ReadOnlySpan<byte> bits, out MensajeJt mensaje)
    {
        mensaje = null!;
        if (bits.Length != Bits) return false;
        var nc1 = Leer(bits, 0, 28);
        var nc2 = Leer(bits, 28, 28);
        var ng = (int)Leer(bits, 56, 16);

        if ((ng & MarcaDeTextoLibre) != 0)
        {
            var textoLibre = DesempaquetarTextoLibre(nc1, nc2, ng & (MarcaDeTextoLibre - 1));
            if (textoLibre is null) return false;
            mensaje = new MensajeJt(textoLibre, EsTextoLibre: true);
            return true;
        }

        if (!TryDesempaquetarIndicativo(nc1, out var primero) || !TryDesempaquetarIndicativo(nc2, out var segundo)) return false;
        if (!TryDesempaquetarTercerCampo(ng, out var tercero, out var locator, out var informe)) return false;

        // El segundo campo tiene que ser un indicativo de verdad: es el que llama. El primero
        // puede ser CQ, QRZ o DE. Un mensaje sin un llamante util no sirve al cuaderno.
        var esCq = primero is "CQ" or "QRZ" or "DE" || primero.StartsWith("CQ ", StringComparison.Ordinal);
        if (!Indicativo.TryParse(segundo, out var llamante) || EsEspecial(segundo)) return false;
        var llamado = Indicativo.Vacio;
        if (!esCq && !Indicativo.TryParse(primero, out llamado)) return false;

        var sb = new StringBuilder(primero).Append(' ').Append(segundo);
        if (tercero.Length > 0) sb.Append(' ').Append(tercero);
        mensaje = new MensajeJt(sb.ToString(), EsTextoLibre: false)
        {
            Llamante = llamante,
            Llamado = llamado,
            Locator = locator,
            EsCq = esCq,
            Informe = informe,
        };
        return true;
    }

    private static bool EsEspecial(string campo) =>
        campo is "CQ" or "QRZ" or "DE" || campo.StartsWith("CQ ", StringComparison.Ordinal);

    private static bool TryEmpaquetarEstructurado(string texto, out long nc1, out long nc2, out int ng)
    {
        nc1 = nc2 = ng = 0;
        var campos = texto.Split(' ');

        // «CQ DX» y «CQ nnn» ocupan dos palabras; se juntan antes de repartir.
        if (campos.Length >= 3 && campos[0] == "CQ" && (campos[1] == "DX" || EsFrecuencia(campos[1])))
            campos = [campos[0] + " " + campos[1], .. campos[2..]];

        if (campos.Length is < 2 or > 3) return false;
        if (!TryEmpaquetarIndicativo(campos[0], out nc1)) return false;
        if (!TryEmpaquetarIndicativo(campos[1], out nc2) || EsEspecial(campos[1])) return false;
        var tercero = campos.Length == 3 ? campos[2] : string.Empty;
        return TryEmpaquetarTercerCampo(tercero, out ng);
    }

    private static bool EsFrecuencia(string campo) => campo.Length == 3 && campo.All(char.IsAsciiDigit);

    /// <summary>
    /// Un indicativo corriente a 28 bits: de la forma <c>[letra o digito][letra o digito]digito[letra][letra][letra]</c>,
    /// con espacios donde falte algo. Si el tercer caracter no es el digito, se corre uno a la derecha.
    /// </summary>
    private static bool TryEmpaquetarIndicativo(string campo, out long valor)
    {
        valor = 0;
        switch (campo)
        {
            case "CQ": valor = ValorCq; return true;
            case "QRZ": valor = ValorQrz; return true;
            case "DE": valor = ValorDe; return true;
            case "CQ DX": campo = "CQ9DX"; break;
        }
        if (campo.StartsWith("CQ ", StringComparison.Ordinal) && campo.Length == 6 && EsFrecuencia(campo[3..]))
        {
            valor = PrimerCqConFrecuencia + int.Parse(campo[3..], System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        if (campo.Length is < 3 or > 6) return false;
        var seis = campo.Length >= 3 && char.IsAsciiDigit(campo[2]) ? campo.PadRight(6)
            : campo.Length >= 2 && char.IsAsciiDigit(campo[1]) && campo.Length <= 5 ? (" " + campo).PadRight(6)
            : null;
        if (seis is null) return false;

        var c1 = IndiceDe(seis[0], " 0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ", esPrimero: true);
        var c2 = IndiceDe(seis[1], "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ", esPrimero: false);
        var c3 = char.IsAsciiDigit(seis[2]) ? seis[2] - '0' : -1;
        var c4 = IndiceDeSufijo(seis[3]);
        var c5 = IndiceDeSufijo(seis[4]);
        var c6 = IndiceDeSufijo(seis[5]);
        if (c1 < 0 || c2 < 0 || c3 < 0 || c4 < 0 || c5 < 0 || c6 < 0) return false;
        // Un sufijo con hueco en medio («A1B C») no es un indicativo.
        if ((c4 == 0 && (c5 != 0 || c6 != 0)) || (c5 == 0 && c6 != 0)) return false;

        valor = (((((c1 * 36L) + c2) * 10 + c3) * 27 + c4) * 27 + c5) * 27 + c6;
        return true;

        static int IndiceDe(char c, string alfabeto, bool esPrimero)
        {
            var i = alfabeto.IndexOf(c, StringComparison.Ordinal);
            // El primer caracter admite espacio, pero se codifica como 36 (va el ultimo).
            return esPrimero ? (c == ' ' ? 36 : (i < 0 ? -1 : i - 1)) : i;
        }

        static int IndiceDeSufijo(char c) => c == ' ' ? 0 : char.IsAsciiLetterUpper(c) ? c - 'A' + 1 : -1;
    }

    private static bool TryDesempaquetarIndicativo(long valor, out string campo)
    {
        campo = string.Empty;
        if (valor < 0 || valor >= TopeDe28Bits) return false;
        if (valor == ValorCq) { campo = "CQ"; return true; }
        if (valor == ValorQrz) { campo = "QRZ"; return true; }
        if (valor == ValorDe) { campo = "DE"; return true; }
        if (valor is >= PrimerCqConFrecuencia and <= UltimoCqConFrecuencia)
        {
            campo = "CQ " + (valor - PrimerCqConFrecuencia).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        if (valor >= BaseDeIndicativos) return false;

        Span<char> seis = stackalloc char[6];
        var c6 = (int)(valor % 27); valor /= 27;
        var c5 = (int)(valor % 27); valor /= 27;
        var c4 = (int)(valor % 27); valor /= 27;
        var c3 = (int)(valor % 10); valor /= 10;
        var c2 = (int)(valor % 36); valor /= 36;
        var c1 = (int)valor;
        seis[0] = c1 == 36 ? ' ' : "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[c1];
        seis[1] = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"[c2];
        seis[2] = (char)('0' + c3);
        seis[3] = c4 == 0 ? ' ' : (char)('A' + c4 - 1);
        seis[4] = c5 == 0 ? ' ' : (char)('A' + c5 - 1);
        seis[5] = c6 == 0 ? ' ' : (char)('A' + c6 - 1);
        // Un sufijo con huecos no es un indicativo; tampoco uno sin sufijo.
        if (c4 == 0 || (c5 == 0 && c6 != 0)) return false;

        var texto = new string(seis).Trim();
        if (texto == "CQ9DX") { campo = "CQ DX"; return true; }
        if (!Indicativo.EsFormaValida(texto)) return false;
        campo = texto;
        return true;
    }

    private static bool TryEmpaquetarTercerCampo(string campo, out int ng)
    {
        ng = 0;
        switch (campo)
        {
            case "": ng = BaseDeInformes + 1; return true;
            case "RO": ng = BaseDeInformes + 62; return true;
            case "RRR": ng = BaseDeInformes + 63; return true;
            case "73": ng = BaseDeInformes + 64; return true;
        }
        if (campo.Length == 3 && campo[0] == '-' && char.IsAsciiDigit(campo[1]) && char.IsAsciiDigit(campo[2]))
        {
            var n = ((campo[1] - '0') * 10) + (campo[2] - '0');
            if (n is < 1 or > 30) return false;
            ng = BaseDeInformes + 1 + n;
            return true;
        }
        if (campo.Length == 4 && campo[0] == 'R' && campo[1] == '-' && char.IsAsciiDigit(campo[2]) && char.IsAsciiDigit(campo[3]))
        {
            var n = ((campo[2] - '0') * 10) + (campo[3] - '0');
            if (n is < 1 or > 30) return false;
            ng = BaseDeInformes + 31 + n;
            return true;
        }
        if (!Locator.TryParse(campo, out var locator) || locator.Precision < 2) return false;
        var v = locator.Valor;
        var longitudOeste = 180 - (20 * (v[0] - 'A')) - (2 * (v[2] - '0')) - 1;
        var latitud = -90 + (10 * (v[1] - 'A')) + (v[3] - '0');
        ng = ((longitudOeste + 180) / 2 * 180) + latitud + 90;
        return true;
    }

    private static bool TryDesempaquetarTercerCampo(int ng, out string campo, out Locator locator, out int? informe)
    {
        campo = string.Empty;
        locator = Locator.Vacio;
        informe = null;
        if (ng < 0) return false;
        if (ng < BaseDeInformes)
        {
            var latitud = (ng % 180) - 90;
            var longitudOeste = (ng / 180 * 2) - 180 + 2;
            var nlong = 180 - longitudOeste;
            var nlat = latitud + 90;
            if (nlong is < 0 or >= 360) return false;
            Span<char> cuatro =
            [
                (char)('A' + (nlong / 20)),
                (char)('A' + (nlat / 10)),
                (char)('0' + (nlong % 20 / 2)),
                (char)('0' + (nlat % 10)),
            ];
            if (!Locator.TryParse(new string(cuatro), out locator)) return false;
            campo = locator.Valor;
            return true;
        }

        var resto = ng - BaseDeInformes;
        switch (resto)
        {
            case 1: return true;
            case >= 2 and <= 31: informe = -(resto - 1); campo = $"-{resto - 1:00}"; return true;
            case >= 32 and <= 61: informe = -(resto - 31); campo = $"R-{resto - 31:00}"; return true;
            case 62: campo = "RO"; return true;
            case 63: campo = "RRR"; return true;
            case 64: campo = "73"; return true;
            default: return false;
        }
    }

    private static void EmpaquetarTextoLibre(string trece, out long nc1, out long nc2, out int ng)
    {
        long a = 0, b = 0, c = 0;
        for (var i = 0; i < 5; i++) a = (a * 42) + AlfabetoDeTextoLibre.IndexOf(trece[i], StringComparison.Ordinal);
        for (var i = 5; i < 10; i++) b = (b * 42) + AlfabetoDeTextoLibre.IndexOf(trece[i], StringComparison.Ordinal);
        for (var i = 10; i < 13; i++) c = (c * 42) + AlfabetoDeTextoLibre.IndexOf(trece[i], StringComparison.Ordinal);
        // a y b ocupan 27 bits; c ocupa 17. Los dos bits altos de c se reparten como bit bajo
        // de los dos primeros campos y los 15 restantes van al tercero con la marca de texto.
        nc1 = (a << 1) | ((c >> 16) & 1);
        nc2 = (b << 1) | ((c >> 15) & 1);
        ng = (int)(c & 0x7FFF) | MarcaDeTextoLibre;
    }

    private static string? DesempaquetarTextoLibre(long nc1, long nc2, int quince)
    {
        var c = ((nc1 & 1) << 16) | ((nc2 & 1) << 15) | (long)quince;
        var a = nc1 >> 1;
        var b = nc2 >> 1;
        const long Tope5 = 42L * 42 * 42 * 42 * 42;
        const long Tope3 = 42L * 42 * 42;
        if (a >= Tope5 || b >= Tope5 || c >= Tope3) return null;

        Span<char> trece = stackalloc char[13];
        for (var i = 4; i >= 0; i--) { trece[i] = AlfabetoDeTextoLibre[(int)(a % 42)]; a /= 42; }
        for (var i = 9; i >= 5; i--) { trece[i] = AlfabetoDeTextoLibre[(int)(b % 42)]; b /= 42; }
        for (var i = 12; i >= 10; i--) { trece[i] = AlfabetoDeTextoLibre[(int)(c % 42)]; c /= 42; }
        var texto = new string(trece).TrimEnd();
        return texto.Length == 0 ? null : texto;
    }

    private static byte[] Componer(long nc1, long nc2, int ng)
    {
        var bits = new byte[Bits];
        Escribir(bits, 0, 28, nc1);
        Escribir(bits, 28, 28, nc2);
        Escribir(bits, 56, 16, ng);
        return bits;
    }

    private static void Escribir(Span<byte> destino, int posicion, int anchura, long valor)
    {
        for (var i = 0; i < anchura; i++)
            destino[posicion + i] = (byte)((valor >> (anchura - 1 - i)) & 1);
    }

    private static long Leer(ReadOnlySpan<byte> origen, int posicion, int anchura)
    {
        long v = 0;
        for (var i = 0; i < anchura; i++) v = (v << 1) | (origen[posicion + i] & 1L);
        return v;
    }
}
