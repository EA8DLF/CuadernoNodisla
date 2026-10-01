using System.Text;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Msk144;

/// <summary>
/// El mensaje corto de MSK144: un informe de senal en una trama de 20 milisegundos.
/// </summary>
/// <remarks>
/// <para>
/// En 144 MHz los pings duran la octava parte que en 50 MHz, y muchos no llegan a los 72 ms de
/// una trama entera. Para aprovecharlos, una vez intercambiados los indicativos, el protocolo
/// permite mandar el resto del contacto en tramas de 40 bits: 8 de sincronismo, 4 que dicen
/// cual de los dieciseis informes posibles es, 12 que son un resumen del par «indicativo
/// llamado, indicativo propio», y 16 de paridad de un codigo LDPC(32,16).
/// </para>
/// <para>
/// El resumen de 12 bits cumple dos papeles: le dice al operador que el mensaje era para el, y
/// rechaza casi todas las decodificaciones falsas, porque una trama de ruido que cuadre la
/// paridad tiene que cuadrar ademas un resumen que el receptor calcula por su cuenta. Es la
/// unica «sello» de la trama corta —no lleva CRC— y por eso <b>solo se acepta un mensaje corto
/// cuyo resumen coincida con un par de indicativos que el receptor conozca</b>.
/// </para>
/// <para>
/// El resumen es el mismo hash de Bob Jenkins (dominio publico) que usa WSPR para los
/// indicativos compuestos, con semilla 146, sobre el texto «LLAMADO PROPIO» rellenado con
/// espacios hasta 37 caracteres. Fuente: QEX julio/agosto 2017, «Optional Short-Message Format».
/// </para>
/// </remarks>
public static class MensajeCortoMsk144
{
    /// <summary>Los dieciseis informes que caben en cuatro bits, por su indice.</summary>
    public static readonly IReadOnlyList<string> Informes =
    [
        "-03", "+00", "+03", "+06", "+10", "+13", "+16", "RRR",
        "R-03", "R+00", "R+03", "R+06", "R+10", "R+13", "R+16", "73",
    ];

    /// <summary>Semilla del hash, fijada por el protocolo.</summary>
    public const uint SemillaDelResumen = 146;

    /// <summary>Longitud a la que se rellena con espacios el texto que se resume.</summary>
    public const int LongitudDelTextoResumido = 37;

    /// <summary>Resumen de 12 bits del par de indicativos.</summary>
    /// <param name="llamado">Indicativo del corresponsal.</param>
    /// <param name="propio">Indicativo de quien emite.</param>
    public static int Resumen(string llamado, string propio)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(llamado);
        ArgumentException.ThrowIfNullOrWhiteSpace(propio);
        var texto = (llamado.Trim().ToUpperInvariant() + " " + propio.Trim().ToUpperInvariant())
            .PadRight(LongitudDelTextoResumido);
        if (texto.Length > LongitudDelTextoResumido) texto = texto[..LongitudDelTextoResumido];
        var bytes = Encoding.ASCII.GetBytes(texto);
        return (int)(HashDeIndicativo.Lookup3(bytes, SemillaDelResumen) & 0xFFF);
    }

    /// <summary>
    /// Empaqueta un mensaje corto en sus 16 bits: 12 de resumen y 4 de informe.
    /// </summary>
    /// <param name="llamado">Indicativo del corresponsal.</param>
    /// <param name="propio">Indicativo propio.</param>
    /// <param name="informe">Uno de <see cref="Informes"/>.</param>
    /// <param name="bits">Los 16 bits, el mas significativo primero.</param>
    /// <param name="motivo">Por que no se pudo.</param>
    public static bool TryEmpaquetar(string llamado, string propio, string informe, out byte[] bits, out string motivo)
    {
        bits = [];
        motivo = string.Empty;
        var indice = IndiceDelInforme(informe);
        if (indice < 0)
        {
            motivo = $"El informe «{informe}» no es uno de los dieciséis del mensaje corto.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(llamado) || string.IsNullOrWhiteSpace(propio))
        {
            motivo = "El mensaje corto necesita los dos indicativos.";
            return false;
        }
        var valor = (Resumen(llamado, propio) << 4) | indice;
        bits = new byte[ParametrosMsk144.BitsDeMensajeCorto];
        for (var i = 0; i < bits.Length; i++) bits[i] = (byte)((valor >> (15 - i)) & 1);
        return true;
    }

    /// <summary>Indice de un informe en la tabla, o -1.</summary>
    public static int IndiceDelInforme(string? informe)
    {
        if (informe is null) return -1;
        var limpio = informe.Trim().ToUpperInvariant();
        for (var i = 0; i < Informes.Count; i++)
            if (string.Equals(Informes[i], limpio, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>Saca el resumen y el informe de 16 bits recien corregidos.</summary>
    public static (int Resumen, string Informe) Desempaquetar(ReadOnlySpan<byte> bits)
    {
        if (bits.Length != ParametrosMsk144.BitsDeMensajeCorto)
            throw new ArgumentException($"Hacen falta {ParametrosMsk144.BitsDeMensajeCorto} bits.", nameof(bits));
        var valor = 0;
        for (var i = 0; i < bits.Length; i++) valor = (valor << 1) | (bits[i] & 1);
        return (valor >> 4, Informes[valor & 0xF]);
    }

    /// <summary>
    /// Reconoce el texto de un mensaje corto: «&lt;LLAMADO PROPIO&gt; INFORME».
    /// </summary>
    /// <remarks>
    /// Se escribe con los indicativos entre angulos, como hacen los demas programas, para que
    /// se vea que lo que viaja por el aire no son los indicativos sino su resumen.
    /// </remarks>
    public static bool TryAnalizar(string? texto, out string llamado, out string propio, out string informe)
    {
        llamado = propio = informe = string.Empty;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var limpio = texto.Trim().ToUpperInvariant();
        var abre = limpio.IndexOf('<', StringComparison.Ordinal);
        var cierra = limpio.IndexOf('>', StringComparison.Ordinal);
        if (abre != 0 || cierra < 0) return false;
        var dentro = limpio[1..cierra].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (dentro.Length != 2) return false;
        var resto = limpio[(cierra + 1)..].Trim();
        if (IndiceDelInforme(resto) < 0) return false;
        llamado = dentro[0];
        propio = dentro[1];
        informe = Informes[IndiceDelInforme(resto)];
        return true;
    }

    /// <summary>Texto con el que se muestra un mensaje corto.</summary>
    public static string Texto(string llamado, string propio, string informe) => $"<{llamado} {propio}> {informe}";
}
