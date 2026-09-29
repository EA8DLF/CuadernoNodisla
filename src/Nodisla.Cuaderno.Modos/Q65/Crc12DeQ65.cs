namespace Nodisla.Cuaderno.Modos.Q65;

/// <summary>
/// El sello de 12 bits que Q65 pone a los 13 simbolos del mensaje.
/// </summary>
/// <remarks>
/// <para>
/// Es un CRC corriente calculado bit a bit, en forma reflejada: se recorren los seis bits de
/// cada simbolo empezando por el menos significativo y el registro se desplaza hacia la
/// derecha. El polinomio esta en las tablas del protocolo. El resultado son dos simbolos: los
/// seis bits bajos del registro y los seis altos.
/// </para>
/// <para>
/// Esos dos simbolos <b>no se transmiten</b>: entran en el codigo corrector pero se recortan de
/// la palabra emitida. El decodificador los reconstruye por las ecuaciones de paridad y el
/// sello sirve de comprobacion final, una entre 4096 de que una palabra mal reconstruida pase.
/// </para>
/// </remarks>
public static class Crc12DeQ65
{
    /// <summary>Simbolos del mensaje que protege.</summary>
    public const int SimbolosDelMensaje = 13;

    /// <summary>Simbolos que ocupa el sello.</summary>
    public const int SimbolosDelSello = 2;

    /// <summary>Calcula el sello de los 13 simbolos del mensaje.</summary>
    /// <returns>Los dos simbolos del sello: bajo y alto.</returns>
    public static (int Bajo, int Alto) Calcular(ReadOnlySpan<int> simbolos, int polinomio)
    {
        if (simbolos.Length != SimbolosDelMensaje)
            throw new ArgumentException($"Hacen falta {SimbolosDelMensaje} simbolos.", nameof(simbolos));
        var registro = 0;
        foreach (var simbolo in simbolos)
        {
            var bits = simbolo;
            for (var b = 0; b < CampoDeGalois64.BitsPorElemento; b++)
            {
                var entra = ((bits ^ registro) & 1) != 0;
                registro >>= 1;
                if (entra) registro ^= polinomio;
                bits >>= 1;
            }
        }
        return (registro & 0x3F, registro >> 6);
    }

    /// <summary>Dice si los 15 simbolos de informacion llevan el sello que les toca.</summary>
    public static bool EsValido(ReadOnlySpan<int> informacion, int polinomio)
    {
        if (informacion.Length != SimbolosDelMensaje + SimbolosDelSello) return false;
        var (bajo, alto) = Calcular(informacion[..SimbolosDelMensaje], polinomio);
        return informacion[SimbolosDelMensaje] == bajo && informacion[SimbolosDelMensaje + 1] == alto;
    }
}
