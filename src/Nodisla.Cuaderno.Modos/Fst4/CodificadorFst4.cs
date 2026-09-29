using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Fst4;

/// <summary>
/// Convierte un mensaje escrito en los 160 tonos de FST4 o FST4W.
/// </summary>
/// <remarks>
/// <para>
/// FST4: texto a 77 bits con el empaquetado de FT8, mezcla con la secuencia fija, CRC de 24,
/// codigo LDPC(240,101), y los 240 bits de dos en dos por el codigo de Gray a 120 tonos, con
/// los cinco grupos de sincronismo intercalados.
/// </para>
/// <para>
/// FST4W: texto a 50 bits con el empaquetado de WSPR, CRC de 24 sin mezcla, codigo LDPC(240,74)
/// y los mismos 160 tonos.
/// </para>
/// </remarks>
public sealed class CodificadorFst4
{
    /// <summary>Crea el codificador con la tabla del codigo que toque.</summary>
    /// <param name="tabla">La (240,101) para FST4 o la (240,74) para FST4W.</param>
    /// <param name="esFst4w">Cierto si es la variante baliza.</param>
    public CodificadorFst4(TablaLdpc tabla, bool esFst4w)
    {
        ArgumentNullException.ThrowIfNull(tabla);
        var bits = esFst4w ? ParametrosFst4.BitsConCrcFst4w : ParametrosFst4.BitsConCrcFst4;
        if (tabla.Codigo.Longitud != ParametrosFst4.BitsDePalabra || tabla.Codigo.BitsDeMensaje != bits)
            throw new ArgumentException($"La tabla no es un código (240,{bits}).", nameof(tabla));
        Tabla = tabla;
        EsFst4w = esFst4w;
    }

    /// <summary>Tabla del codigo.</summary>
    public TablaLdpc Tabla { get; }

    /// <summary>Es la variante baliza.</summary>
    public bool EsFst4w { get; }

    /// <summary>Convierte un mensaje en sus 160 tonos.</summary>
    /// <param name="texto">Mensaje: uno de FT8 en FST4, uno de WSPR («EA8DLF IL18 37») en FST4W.</param>
    /// <param name="tonos">Un tono por simbolo, con el sincronismo intercalado.</param>
    /// <param name="motivo">Por que no se pudo.</param>
    public bool TryCodificar(string? texto, out byte[] tonos, out string motivo)
    {
        tonos = [];
        if (EsFst4w)
        {
            if (!MensajeWspr.TryAnalizar(texto, out var mensaje, out motivo)) return false;
            tonos = TonosDeLaPalabra(PalabraDeCodigo(mensaje.Empaquetar()));
            return true;
        }

        if (!MensajeDe77Bits.TryEmpaquetar(texto, out var bits77, out motivo)) return false;
        tonos = TonosDeLaPalabra(PalabraDeCodigo(bits77));
        return true;
    }

    /// <summary>Mezcla (solo FST4), CRC y paridad: entran 77 o 50 bits y salen 240.</summary>
    public byte[] PalabraDeCodigo(ReadOnlySpan<byte> bitsDelMensaje)
    {
        var esperados = EsFst4w ? MensajeWspr.Bits : MensajeDe77Bits.Bits;
        if (bitsDelMensaje.Length != esperados)
            throw new ArgumentException($"Hacen falta {esperados} bits y llegan {bitsDelMensaje.Length}.", nameof(bitsDelMensaje));
        Span<byte> mensaje = stackalloc byte[esperados];
        bitsDelMensaje.CopyTo(mensaje);
        if (!EsFst4w) AplicarMezcla(mensaje);
        return Tabla.Codigo.Codificar(Crc24.AnadirA(mensaje));
    }

    /// <summary>Revuelve o desrevuelve los 77 bits con la secuencia de FST4. Es su propia inversa.</summary>
    public static void AplicarMezcla(Span<byte> bits77)
    {
        var mezcla = ParametrosFst4.Mezcla;
        for (var i = 0; i < bits77.Length && i < mezcla.Length; i++) bits77[i] ^= mezcla[i];
    }

    /// <summary>Reparte los 240 bits en 120 tonos e intercala los cinco grupos de sincronismo.</summary>
    public static byte[] TonosDeLaPalabra(ReadOnlySpan<byte> palabra)
    {
        if (palabra.Length != ParametrosFst4.BitsDePalabra)
            throw new ArgumentException($"La palabra debe tener {ParametrosFst4.BitsDePalabra} bits.", nameof(palabra));

        var tonos = new byte[ParametrosFst4.SimbolosTotales];
        for (var i = 0; i < tonos.Length; i++)
            if (ParametrosFst4.EsSimboloDeSincronismo(i, out var tono)) tonos[i] = (byte)tono;

        var posiciones = ParametrosFst4.DelPeriodo(60).PosicionesDeDatos;
        var gray = ParametrosFst4.MapaDeGray;
        for (var s = 0; s < posiciones.Length; s++)
        {
            var valor = (palabra[2 * s] << 1) | palabra[(2 * s) + 1];
            tonos[posiciones[s]] = gray[valor];
        }
        return tonos;
    }
}
