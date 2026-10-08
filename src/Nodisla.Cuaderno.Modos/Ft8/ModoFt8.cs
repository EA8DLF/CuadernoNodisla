using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Marco;

namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>
/// FT8 o FT4 vistos por el marco de modos: un envoltorio sobre el decodificador y el codificador
/// de esta carpeta, sin tocarlos.
/// </summary>
/// <remarks>
/// <para>
/// FT8 y FT4 llegaron antes que <see cref="IModoDigital"/>. Este envoltorio no reescribe nada:
/// le pasa al <see cref="Decodificador"/> de siempre el audio tal y como lo junta el modem (a la
/// frecuencia de captura y con su preludio), que es exactamente lo que hacia el modem antes de
/// despachar por el registro. Por eso las cifras del banco de FT8 no cambian.
/// </para>
/// <para>
/// FT8 y FT4 comparten el catalogo de indicativos: un indicativo largo oido en uno sirve para
/// resolver su resumen en el otro, igual que antes.
/// </para>
/// </remarks>
public sealed class ModoFt8 : IModoDigital
{
    private readonly ParametrosDelModo _parametros;
    private readonly Decodificador _decodificador;
    private readonly Codificador _codificador;

    /// <summary>Crea el modo.</summary>
    /// <param name="modo">FT8 o FT4.</param>
    /// <param name="tablas">Tablas del protocolo.</param>
    /// <param name="catalogo">Catalogo de indicativos, compartido entre FT8 y FT4.</param>
    /// <param name="registro">Registro.</param>
    public ModoFt8(ModoDelModem modo, TablasDelProtocolo tablas, CatalogoDeIndicativos catalogo, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(tablas);
        ArgumentNullException.ThrowIfNull(catalogo);
        _parametros = ParametrosDelModo.De(modo);
        _decodificador = new Decodificador(tablas, registro);
        _codificador = new Codificador(tablas);
        Catalogo = catalogo;
    }

    /// <summary>Catalogo de indicativos que va aprendiendo mientras escucha.</summary>
    public CatalogoDeIndicativos Catalogo { get; }

    /// <summary>El decodificador de siempre, para quien quiera ajustarlo o consultarlo.</summary>
    public Decodificador Decodificador => _decodificador;

    /// <inheritdoc/>
    public ModoDelModem Modo => _parametros.Modo;

    /// <inheritdoc/>
    public TimeSpan Periodo => TimeSpan.FromSeconds(_parametros.PeriodoSegundos);

    /// <inheritdoc/>
    public TimeSpan ArranqueDentroDelPeriodo => TimeSpan.Zero;

    /// <inheritdoc/>
    public TimeSpan ComienzoDeLaSenal => TimeSpan.FromSeconds(_parametros.ComienzoNominalSegundos);

    /// <inheritdoc/>
    /// <remarks>
    /// Redondeada al entero: 12800 en FT8 y 21333 en FT4. Da igual que no sea exacta, porque el
    /// decodificador remuestrea por su cuenta a la suya y, por el camino del modem, recibe el
    /// audio a la frecuencia de captura sin pasar por aqui.
    /// </remarks>
    public int FrecuenciaDeAnalisis => (int)Math.Round(_parametros.FrecuenciaDeAnalisis);

    /// <inheritdoc/>
    public PistaDeQso PistaDeQso { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    /// FT8 y FT4 son, de los modos de serie, los de periodo mas corto (15 y 7,5 s): son los que
    /// mas se benefician de adelantar el trabajo caro —demodular y corregir con el LDPC— al rato
    /// en que todavia esta llegando el resto de la ventana, en vez de esperar a que cierre.
    /// </remarks>
    public bool SoportaDecodificacionProgresiva => true;

    /// <inheritdoc/>
    public double AmplitudDeSalida { get; set; } = IModoDigital.AmplitudDeSalidaPorDefecto;

    /// <inheritdoc/>
    public void Reiniciar() => Catalogo.Olvidar();

    /// <inheritdoc/>
    public IReadOnlyList<DecodificacionPropia> Decodificar(ReadOnlySpan<float> audio, DateTimeOffset ventanaUtc, CancellationToken ct) =>
        _decodificador.Decodificar(audio, FrecuenciaDeAnalisis, Modo, ventanaUtc, Catalogo).Decodificaciones;

    /// <inheritdoc/>
    public IReadOnlyList<DecodificacionPropia> DecodificarVentana(
        ReadOnlySpan<float> audio, int frecuenciaDeMuestreo, DateTimeOffset ventanaUtc, double segundosDelPrimerMuestreo, CancellationToken ct) =>
        DecodificarConCuentas(audio, frecuenciaDeMuestreo, ventanaUtc, segundosDelPrimerMuestreo).Decodificaciones;

    /// <summary>Lo mismo que <see cref="DecodificarVentana"/>, con las cuentas del decodificador.</summary>
    public ResultadoDeVentana DecodificarConCuentas(
        ReadOnlySpan<float> audio, int frecuenciaDeMuestreo, DateTimeOffset ventanaUtc, double segundosDelPrimerMuestreo)
    {
        var pista = PistaAp.TryDesde(PistaDeQso.MiIndicativo, PistaDeQso.DxCall, out var p) ? p : (PistaAp?)null;
        return _decodificador.Decodificar(audio, frecuenciaDeMuestreo, Modo, ventanaUtc, Catalogo, segundosDelPrimerMuestreo, pista);
    }

    /// <inheritdoc/>
    public float[] Generar(string mensaje, int tonoHz, int frecuenciaDeMuestreo)
    {
        if (!_codificador.TryCodificar(mensaje, Modo, out var tonos, out var motivo)) throw new FormatException(motivo);
        // Lo que uno emite, lo sabe: se apunta para resolver los resumenes con que le contesten
        // (el propio indicativo en «<EA8DLF> HB10GBT RRR», el del DX en «EA8DLF <HB10GBT> -14»).
        foreach (var indicativo in MensajeDe77Bits.IndicativosQueViajan(mensaje)) Catalogo.Fijar(indicativo);
        return Modulador.Sintetizar(_parametros, tonos, tonoHz, frecuenciaDeMuestreo, amplitud: AmplitudDeSalida);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Codifica cada mensaje por separado con el mismo codificador de siempre —ningun mensaje
    /// sabe que va a sonar junto a otros— y le pasa a <see cref="Modulador.SintetizarMezcla"/> los
    /// tonos ya listos de todos. Igual que <see cref="Generar"/>, apunta en el catalogo los
    /// indicativos de cada mensaje que se emite.
    /// </remarks>
    public float[] GenerarMezcla(IReadOnlyList<(string Mensaje, int TonoHz)> mensajes, int frecuenciaDeMuestreo)
    {
        ArgumentNullException.ThrowIfNull(mensajes);
        if (mensajes.Count == 0) throw new ArgumentException("Hace falta al menos un mensaje para mezclar.", nameof(mensajes));

        var senales = new (byte[] Tonos, double TonoBaseHz)[mensajes.Count];
        for (var i = 0; i < mensajes.Count; i++)
        {
            var (texto, tonoHz) = mensajes[i];
            if (!_codificador.TryCodificar(texto, Modo, out var tonos, out var motivo)) throw new FormatException(motivo);
            foreach (var indicativo in MensajeDe77Bits.IndicativosQueViajan(texto)) Catalogo.Fijar(indicativo);
            senales[i] = (tonos, tonoHz);
        }

        return Modulador.SintetizarMezcla(_parametros, senales, frecuenciaDeMuestreo, amplitudDePico: AmplitudDeSalida);
    }
}
