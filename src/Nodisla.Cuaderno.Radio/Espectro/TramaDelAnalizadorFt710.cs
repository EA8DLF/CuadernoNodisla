using System.Buffers.Binary;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Radio.Control.Ft710;

namespace Nodisla.Cuaderno.Radio.Espectro;

/// <summary>
/// Descifra la trama de 4096 bytes que el FT-710 manda por su puente FT4222.
/// </summary>
/// <remarks>
/// <para>
/// Disposicion segun wfview (<c>include/packettypes.h</c>, <c>src/radio/yaesucommander.cpp</c>),
/// documentada en <c>docs/14-espectro-ft710.md</c>: 850 puntos de la traza principal al
/// principio, invertidos; un bloque de estado de 150 bytes en 2900; y la cola
/// <c>FF 01 EE 01</c> al final.
/// </para>
/// <para>Es codigo puro, sin dispositivo: las pruebas lo pasan por tramas guardadas.</para>
/// </remarks>
public static class TramaDelAnalizadorFt710
{
    /// <summary>Largo de una trama.</summary>
    public const int Largo = 4096;

    /// <summary>Puntos de la traza principal.</summary>
    public const int Puntos = 850;

    /// <summary>Donde empieza el bloque de estado.</summary>
    public const int InicioDelEstado = 2900;

    /// <summary>Largo del bloque de estado.</summary>
    public const int LargoDelEstado = 150;

    /// <summary>La marca con que acaba cada trama.</summary>
    public static ReadOnlySpan<byte> Cola => [0xFF, 0x01, 0xEE, 0x01];

    private static readonly byte[] ColaRepetida = [0xFF, 0x01, 0xEE, 0x01, 0xFF, 0x01, 0xEE, 0x01, 0xFF, 0x01, 0xEE, 0x01, 0xFF, 0x01, 0xEE, 0x01];

    /// <summary>Anchos del SPAN del equipo, por su indice (nibble bajo del byte 32 del estado).</summary>
    public static IReadOnlyList<int> Spans { get; } =
        [1_000, 2_000, 5_000, 10_000, 20_000, 50_000, 100_000, 200_000, 500_000, 1_000_000];

    /// <summary>La trama tiene el largo y la cola buenos.</summary>
    /// <param name="trama">Bytes leidos.</param>
    /// <returns>Si se puede descifrar.</returns>
    public static bool EsValida(ReadOnlySpan<byte> trama) =>
        trama.Length == Largo && trama[^4..].SequenceEqual(Cola);

    /// <summary>
    /// Endereza una trama que llega corrida unos bits.
    /// </summary>
    /// <remarks>
    /// Comprobado en la radio de Jose el 29-09-2026: al cambiar el SPAN (<c>SS05</c>) el FT-710
    /// sigue mandando tramas de 4096 bytes alineadas con cada lectura, pero <b>retrasadas un
    /// bit</b> (cada byte es el de antes desplazado a la derecha, con el ultimo bit del anterior
    /// delante), y asi se quedan aunque se cierre y se vuelva a abrir el puente. Sin enderezarlas,
    /// ninguna trama vale y el analizador se queda congelado justo al tocar SPAN. La cola se
    /// repite cuatro veces al final (<c>FF 01 EE 01</c> × 4), asi que se busca el retraso que la
    /// deja en su sitio; el ultimo bit, que ya viaja en la lectura siguiente, es el <c>01</c> de la
    /// cola y se pone a mano.
    /// </remarks>
    /// <param name="leida">Los 4096 bytes tal como se han leido.</param>
    /// <param name="destino">Donde se deja la trama enderezada (4096 bytes).</param>
    /// <param name="bits">Bits de retraso que tenia: 0 si ya venia bien.</param>
    /// <returns>Falso si no se reconoce la trama con ningun retraso.</returns>
    public static bool IntentarEnderezar(ReadOnlySpan<byte> leida, Span<byte> destino, out int bits)
    {
        bits = 0;
        if (leida.Length != Largo || destino.Length < Largo) return false;
        if (EsValida(leida))
        {
            leida.CopyTo(destino);
            return true;
        }

        for (var k = 1; k < 8; k++)
        {
            for (var i = 0; i < Largo - 1; i++)
            {
                destino[i] = (byte)((leida[i] << k) | (leida[i + 1] >> (8 - k)));
            }

            destino[Largo - 1] = Cola[^1];
            if (destino.Slice(Largo - 16, 15).SequenceEqual(ColaRepetida.AsSpan(0, 15)))
            {
                bits = k;
                return true;
            }
        }

        return false;
    }

    /// <summary>Descifra una trama.</summary>
    /// <param name="trama">Los 4096 bytes.</param>
    /// <param name="traza">La traza, si la trama vale.</param>
    /// <returns>Falso si la trama no es valida o no trae frecuencia.</returns>
    public static bool IntentarDescifrar(ReadOnlySpan<byte> trama, out TrazaDeEspectro? traza)
    {
        traza = null;
        if (trama.Length != Largo) return false;
        if (!EsValida(trama))
        {
            var enderezada = new byte[Largo];
            if (!IntentarEnderezar(trama, enderezada, out _)) return false;
            trama = enderezada;
        }

        var estado = trama.Slice(InicioDelEstado, LargoDelEstado);

        var vfo = LeerBcd(estado.Slice(64, 5));
        if (vfo <= 0) vfo = BinaryPrimitives.ReadUInt32BigEndian(estado.Slice(132, 4));
        if (vfo <= 0) return false;

        // Nibble bajo: el SPAN; el alto va a 4 en CURSOR y a 8 en FIX (radio real, 29-09-2026).
        var indiceDeSpan = estado[32] & 0x0F;
        var span = indiceDeSpan < Spans.Count ? Spans[indiceDeSpan] : 0;

        var modo = (estado[52] & 0x03) switch
        {
            0 => ModoDelAnalizador.Centro,
            1 => ModoDelAnalizador.Cursor,
            2 => ModoDelAnalizador.Fijo,
            _ => ModoDelAnalizador.Desconocido,
        };

        long inicio;
        long fin;
        var inicioFijo = BinaryPrimitives.ReadUInt32BigEndian(estado.Slice(144, 4));
        if (modo == ModoDelAnalizador.Fijo && inicioFijo > 0 && span > 0)
        {
            inicio = inicioFijo;
            fin = inicioFijo + span;
        }
        else
        {
            inicio = vfo - (span / 2);
            fin = vfo + (span / 2);
        }

        var niveles = new byte[Puntos];
        for (var i = 0; i < Puntos; i++) niveles[i] = (byte)~trama[i];

        // Byte 17: el modo del analizador con el mismo numero que contesta SS06 (0-2 3DSS,
        // 4/7/A cascada, 5/8/B cascada ampliada). Comprobado en la radio real el 29-09-2026.
        var vista = VistaDe(estado[17]);

        traza = new TrazaDeEspectro(
            niveles, inicio, fin, vfo, span, modo, estado[33] >> 4, vista?.EsTresD ?? false, vista?.Ampliado ?? false);
        return true;
    }

    private static ModoDelAnalizadorFt710? VistaDe(byte valor) =>
        valor < 16 && ModosDelAnalizadorFt710.DesdeLoLeido("0123456789ABCDEF"[valor]) is { } i
            ? ModosDelAnalizadorFt710.Todos[(int)i]
            : null;

    /// <summary>Frecuencia en BCD empaquetado: dos cifras por byte, en hercios.</summary>
    internal static long LeerBcd(ReadOnlySpan<byte> bcd)
    {
        long valor = 0;
        foreach (var b in bcd)
        {
            var alta = b >> 4;
            var baja = b & 0x0F;
            if (alta > 9 || baja > 9) return -1;
            valor = (valor * 100) + (alta * 10) + baja;
        }

        return valor;
    }
}
