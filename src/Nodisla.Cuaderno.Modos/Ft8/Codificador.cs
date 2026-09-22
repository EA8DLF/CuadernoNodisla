using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ldpc;

namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>
/// Convierte un mensaje escrito en la secuencia de tonos que hay que emitir.
/// </summary>
/// <remarks>
/// <para>
/// El camino completo, de arriba abajo, es este:
/// </para>
/// <list type="number">
/// <item><b>Texto a 77 bits.</b> «CQ EA8DLF IL18» se convierte en numeros de catalogo.</item>
/// <item><b>Mezcla, solo en FT4.</b> Se revuelve el mensaje con una secuencia fija para que una
/// senal de FT4 no pueda decodificarse como si fuera de FT8.</item>
/// <item><b>CRC de 14 bits.</b> Quedan 91 bits. Es el sello que permitira al receptor saber si
/// lo que recupere es de verdad.</item>
/// <item><b>Codigo corrector.</b> 91 bits entran y salen 174: los mismos 91 mas 83 de paridad.
/// Esos 83 son lo que permite recuperar el mensaje aunque el ruido se cargue una parte.</item>
/// <item><b>Tonos.</b> Los 174 bits se parten en grupos de tres (FT8) o dos (FT4), se pasan por
/// el codigo de Gray y se intercalan con los grupos de sincronismo.</item>
/// </list>
/// <para>
/// El receptor hace exactamente lo mismo en sentido contrario, y por eso empezar por el
/// transmisor es la unica manera sensata de construir esto: codificar un mensaje, decodificarlo
/// con el propio decodificador y comprobar que vuelve igual es una prueba que se cierra sola,
/// sin radio, sin grabaciones y sin depender de que otro programa este de acuerdo.
/// </para>
/// </remarks>
public sealed class Codificador
{
    private readonly TablasDelProtocolo _tablas;

    /// <summary>Crea el codificador con las tablas del protocolo.</summary>
    /// <param name="tablas">Tablas; si son las de pruebas, lo emitido solo lo entiende este modem.</param>
    public Codificador(TablasDelProtocolo tablas)
    {
        ArgumentNullException.ThrowIfNull(tablas);
        _tablas = tablas;
    }

    /// <summary>Las tablas con las que trabaja.</summary>
    public TablasDelProtocolo Tablas => _tablas;

    /// <summary>
    /// Convierte un mensaje escrito en la secuencia de tonos del modo.
    /// </summary>
    /// <param name="texto">Mensaje, por ejemplo <c>EA1ABC EA8DLF -12</c>.</param>
    /// <param name="modo">FT8 o FT4.</param>
    /// <param name="tonos">Un tono por simbolo emitido, incluidos sincronismo y rampas.</param>
    /// <param name="motivo">Por que no se pudo, para decirselo al operador.</param>
    public bool TryCodificar(string? texto, ModoDelModem modo, out byte[] tonos, out string motivo)
    {
        tonos = [];
        if (!MensajeDe77Bits.TryEmpaquetar(texto, out var bits77, out motivo)) return false;
        tonos = TonosDeLosBits(bits77, modo);
        return true;
    }

    /// <summary>Convierte unos 77 bits ya empaquetados en la secuencia de tonos.</summary>
    /// <param name="bits77">Los 77 bits del mensaje.</param>
    /// <param name="modo">FT8 o FT4.</param>
    public byte[] TonosDeLosBits(ReadOnlySpan<byte> bits77, ModoDelModem modo)
    {
        var parametros = ParametrosDelModo.De(modo);
        var palabra = PalabraDeCodigo(bits77, modo);
        return TonosDeLaPalabra(palabra, parametros);
    }

    /// <summary>
    /// Aplica la mezcla, el CRC y el codigo corrector, y devuelve los 174 bits que se emiten.
    /// </summary>
    public byte[] PalabraDeCodigo(ReadOnlySpan<byte> bits77, ModoDelModem modo)
    {
        Span<byte> mensaje = stackalloc byte[MensajeDe77Bits.Bits];
        bits77.CopyTo(mensaje);
        if (modo == ModoDelModem.Ft4) AplicarMezclaDeFt4(mensaje);

        var conCrc = Crc14.AnadirA(mensaje);
        return _tablas.Ldpc.Codificar(conCrc);
    }

    /// <summary>
    /// Revuelve o desrevuelve los 77 bits con la secuencia de FT4.
    /// </summary>
    /// <remarks>
    /// Es su propia inversa, porque un o-exclusivo aplicado dos veces deja las cosas como
    /// estaban. Mientras la secuencia no conste vale todo ceros, que no revuelve nada: FT4
    /// seguira hablando consigo mismo, pero no con los demas.
    /// </remarks>
    public void AplicarMezclaDeFt4(Span<byte> bits77)
    {
        var mezcla = _tablas.MezclaDeFt4;
        for (var i = 0; i < MensajeDe77Bits.Bits; i++)
            bits77[i] ^= (byte)((mezcla[i / 8] >> (7 - (i % 8))) & 1);
    }

    /// <summary>
    /// Reparte los 174 bits entre los simbolos de datos e intercala los grupos de sincronismo.
    /// </summary>
    /// <param name="palabra">Los 174 bits de la palabra de codigo.</param>
    /// <param name="parametros">Parametros del modo.</param>
    public static byte[] TonosDeLaPalabra(ReadOnlySpan<byte> palabra, ParametrosDelModo parametros)
    {
        ArgumentNullException.ThrowIfNull(parametros);
        var bitsPorSimbolo = parametros.BitsPorSimbolo;
        var esperados = parametros.SimbolosDeDatos * bitsPorSimbolo;
        if (palabra.Length != esperados)
            throw new ArgumentException($"La palabra debe tener {esperados} bits para {parametros.Modo} y tiene {palabra.Length}.", nameof(palabra));

        var tonos = new byte[parametros.SimbolosTotales];

        // Primero el sincronismo, que ocupa posiciones fijas.
        for (var i = 0; i < parametros.SimbolosTotales; i++)
            if (parametros.EsSimboloDeSincronismo(i, out var tonoDeCostas))
                tonos[i] = (byte)tonoDeCostas;

        // Luego los datos, en las posiciones que quedan, de tres en tres bits o de dos en dos.
        var posiciones = parametros.PosicionesDeDatos;
        for (var s = 0; s < posiciones.Length; s++)
        {
            var valor = 0;
            for (var b = 0; b < bitsPorSimbolo; b++)
                valor = (valor << 1) | palabra[(s * bitsPorSimbolo) + b];
            tonos[posiciones[s]] = parametros.MapaDeGray[valor];
        }

        return tonos;
    }
}
