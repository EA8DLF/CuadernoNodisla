using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Msk144;

/// <summary>
/// Convierte un mensaje escrito en la trama de bits de MSK144.
/// </summary>
/// <remarks>
/// <para>
/// El camino es el de FT8 con otro traje: texto a 77 bits con el mismo empaquetado, CRC de 13
/// en vez de 14, codigo LDPC(128,90) en vez de (174,91), y en lugar de tonos con grupos de
/// Costas, una trama de 144 bits con dos palabras de sincronismo de 8 bits: sincronismo, 48
/// bits de datos, sincronismo, 80 bits de datos.
/// </para>
/// <para>
/// El mensaje corto va por otro camino mas simple: 16 bits (resumen de los indicativos e
/// informe), codigo LDPC(32,16), y una trama de 40 bits con su propia palabra de sincronismo.
/// </para>
/// </remarks>
public sealed class CodificadorMsk144
{
    /// <summary>Crea el codificador con las tablas de los dos codigos.</summary>
    /// <param name="tablaLarga">Tabla del LDPC(128,90).</param>
    /// <param name="tablaCorta">Tabla del LDPC(32,16) de los mensajes cortos.</param>
    public CodificadorMsk144(TablaLdpc tablaLarga, TablaLdpc tablaCorta)
    {
        ArgumentNullException.ThrowIfNull(tablaLarga);
        ArgumentNullException.ThrowIfNull(tablaCorta);
        if (tablaLarga.Codigo.Longitud != ParametrosMsk144.BitsDePalabra || tablaLarga.Codigo.BitsDeMensaje != ParametrosMsk144.BitsConCrc)
            throw new ArgumentException("La tabla larga no es un código (128,90).", nameof(tablaLarga));
        if (tablaCorta.Codigo.Longitud != ParametrosMsk144.BitsDePalabraCorta || tablaCorta.Codigo.BitsDeMensaje != ParametrosMsk144.BitsDeMensajeCorto)
            throw new ArgumentException("La tabla corta no es un código (32,16).", nameof(tablaCorta));
        TablaLarga = tablaLarga;
        TablaCorta = tablaCorta;
    }

    /// <summary>Tabla del codigo largo.</summary>
    public TablaLdpc TablaLarga { get; }

    /// <summary>Tabla del codigo corto.</summary>
    public TablaLdpc TablaCorta { get; }

    /// <summary>Se trabaja con los dos codigos de verdad.</summary>
    public bool EsElCodigoReal => TablaLarga.EsElCodigoReal && TablaCorta.EsElCodigoReal;

    /// <summary>
    /// Convierte un mensaje en su trama de bits: 144 para un mensaje normal, 40 para uno corto.
    /// </summary>
    /// <param name="texto">Mensaje, por ejemplo <c>CQ EA8DLF IL18</c> o <c>&lt;EA1ABC EA8DLF&gt; R+06</c>.</param>
    /// <param name="trama">Los bits de la trama, listos para modular.</param>
    /// <param name="motivo">Por que no se pudo.</param>
    public bool TryCodificar(string? texto, out byte[] trama, out string motivo)
    {
        trama = [];
        if (MensajeCortoMsk144.TryAnalizar(texto, out var llamado, out var propio, out var informe))
        {
            if (!MensajeCortoMsk144.TryEmpaquetar(llamado, propio, informe, out var bits16, out motivo)) return false;
            trama = TramaCorta(bits16);
            return true;
        }

        if (!MensajeDe77Bits.TryEmpaquetar(texto, out var bits77, out motivo)) return false;
        trama = TramaDeLosBits(bits77);
        return true;
    }

    /// <summary>Trama de 144 bits de unos 77 bits ya empaquetados.</summary>
    public byte[] TramaDeLosBits(ReadOnlySpan<byte> bits77) => TramaDeLaPalabra(PalabraDeCodigo(bits77));

    /// <summary>Anade el CRC y la paridad: 77 bits entran y salen 128.</summary>
    public byte[] PalabraDeCodigo(ReadOnlySpan<byte> bits77)
    {
        if (bits77.Length != ParametrosMsk144.BitsDeMensaje)
            throw new ArgumentException($"Hacen falta {ParametrosMsk144.BitsDeMensaje} bits.", nameof(bits77));
        return TablaLarga.Codigo.Codificar(Crc13.AnadirA(bits77));
    }

    /// <summary>Intercala las dos palabras de sincronismo con los 128 bits de la palabra.</summary>
    public static byte[] TramaDeLaPalabra(ReadOnlySpan<byte> palabra)
    {
        if (palabra.Length != ParametrosMsk144.BitsDePalabra)
            throw new ArgumentException($"La palabra debe tener {ParametrosMsk144.BitsDePalabra} bits.", nameof(palabra));
        var trama = new byte[ParametrosMsk144.BitsPorTrama];
        var sincronismo = ParametrosMsk144.Sincronismo;
        sincronismo.CopyTo(trama.AsSpan(ParametrosMsk144.PrimerSincronismo));
        sincronismo.CopyTo(trama.AsSpan(ParametrosMsk144.SegundoSincronismo));
        for (var i = 0; i < ParametrosMsk144.BitsDePalabra; i++)
            trama[ParametrosMsk144.PosicionEnLaTrama(i)] = palabra[i];
        return trama;
    }

    /// <summary>Saca los 128 bits de la palabra de una trama de 144.</summary>
    public static void PalabraDeLaTrama(ReadOnlySpan<byte> trama, Span<byte> palabra)
    {
        for (var i = 0; i < ParametrosMsk144.BitsDePalabra; i++)
            palabra[i] = trama[ParametrosMsk144.PosicionEnLaTrama(i)];
    }

    /// <summary>Trama corta de 40 bits de un mensaje corto de 16.</summary>
    public byte[] TramaCorta(ReadOnlySpan<byte> bits16)
    {
        if (bits16.Length != ParametrosMsk144.BitsDeMensajeCorto)
            throw new ArgumentException($"Hacen falta {ParametrosMsk144.BitsDeMensajeCorto} bits.", nameof(bits16));
        var palabra = TablaCorta.Codigo.Codificar(bits16);
        var trama = new byte[ParametrosMsk144.BitsPorTramaCorta];
        ParametrosMsk144.SincronismoCorto.CopyTo(trama);
        palabra.CopyTo(trama.AsSpan(ParametrosMsk144.BitsDeSincronismo));
        return trama;
    }
}
