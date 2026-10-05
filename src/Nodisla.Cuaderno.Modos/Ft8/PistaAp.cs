namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>
/// La pista de un QSO en curso, traducida a los bits del mensaje de 77 que ya se conocen.
/// </summary>
/// <remarks>
/// <para>
/// De los 77 bits del mensaje normal, 58 son los dos indicativos (28 bits cada uno mas el de
/// <c>/R</c>) y 3 son el tipo. Esos 61 no dependen de qué informe se mande, así que si ya se sabe
/// quién llama a quién, ya se conocen sin necesidad de leer la señal. Lo que queda —el bit de
/// acuse y los 15 del campo de informe/localizador/RR73/73— sigue saliendo de la señal real: es
/// lo único que de verdad hace falta decodificar.
/// </para>
/// </remarks>
/// <param name="Bits77">
/// Los 77 bits de un mensaje de muestra con esos dos indicativos (el informe que lleve no importa:
/// no se usa ninguno de sus bits).
/// </param>
/// <param name="Conocido77">Qué posiciones de <paramref name="Bits77"/> se dan por seguras.</param>
public readonly record struct PistaAp(byte[] Bits77, bool[] Conocido77)
{
    /// <summary>Hasta aquí van los dos indicativos y sus «/R»: destino (0-28), origen (29-57).</summary>
    private const int FinDeLosIndicativos = 58;

    /// <summary>El campo de tipo, que en un mensaje normal vale siempre uno.</summary>
    private const int InicioDelTipo = 74;

    /// <summary>
    /// Construye la pista para un QSO entre dos indicativos de forma corriente.
    /// </summary>
    /// <remarks>
    /// Solo vale para los dos indicativos que caben en el formato corriente de seis huecos
    /// (<see cref="MensajeDe77Bits.EsIndicativoEstandar"/>). Un indicativo raro viaja resumido o
    /// en claro según el resto del contexto del mensaje, que aquí no se conoce, así que para esos
    /// casos no hay pista: la decodificación sigue siendo a ciegas, como hasta ahora.
    /// </remarks>
    /// <param name="miIndicativo">El propio.</param>
    /// <param name="dxCall">El corresponsal.</param>
    /// <param name="pista">La pista, si se pudo construir.</param>
    public static bool TryDesde(string? miIndicativo, string? dxCall, out PistaAp pista)
    {
        pista = default;
        if (!MensajeDe77Bits.EsIndicativoEstandar(miIndicativo) || !MensajeDe77Bits.EsIndicativoEstandar(dxCall))
            return false;

        // El informe es una muestra cualquiera: ninguno de sus bits entra en la parte conocida.
        var texto = $"{dxCall} {miIndicativo} +01";
        if (!MensajeDe77Bits.TryEmpaquetar(texto, out var bits, out _)) return false;

        var conocido = new bool[MensajeDe77Bits.Bits];
        for (var i = 0; i < FinDeLosIndicativos; i++) conocido[i] = true;
        for (var i = InicioDelTipo; i < MensajeDe77Bits.Bits; i++) conocido[i] = true;

        pista = new PistaAp(bits, conocido);
        return true;
    }
}
