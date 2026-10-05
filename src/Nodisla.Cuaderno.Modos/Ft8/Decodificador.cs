using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Ft8;

/// <summary>Como fue la decodificacion de una ventana, con el detalle para poder medir.</summary>
/// <param name="Decodificaciones">Lo que se saco.</param>
/// <param name="Candidatas">Cuantos sitios se miraron.</param>
/// <param name="PalabrasValidas">Cuantas veces el corrector devolvio una palabra que cuadraba.</param>
/// <param name="RechazadasPorElCrc">
/// Cuantas de esas palabras se tiraron porque el CRC no cuadraba. <b>Es la cifra que importa</b>:
/// son los contactos falsos que no llegaron al cuaderno.
/// </param>
/// <param name="Duracion">Lo que costo la ventana entera.</param>
public sealed record ResultadoDeVentana(
    IReadOnlyList<DecodificacionPropia> Decodificaciones,
    int Candidatas,
    int PalabrasValidas,
    int RechazadasPorElCrc,
    TimeSpan Duracion)
{
    /// <summary>Decodificaciones que salieron por la recuperacion profunda y no por la pasada normal.</summary>
    public int PorRecuperacionProfunda { get; init; }

    /// <summary>Decodificaciones que salieron gracias a la pista del QSO en curso (AP).</summary>
    public int PorPista { get; init; }
}

/// <summary>
/// Decodifica una ventana entera de audio.
/// </summary>
/// <remarks>
/// <para>
/// Junta todas las piezas y las pone en orden: remuestrear, analizar, buscar candidatas, mirar
/// cada una de cerca, corregir errores, comprobar el sello y entender el mensaje. De cada cien
/// candidatas que se miran, en una ventana normal salen unas pocas decodificaciones; las demas
/// eran ruido que se parecia al patron, y es normal y sano que lo sean.
/// </para>
/// <para>
/// <b>Los tres filtros que impiden inventar un contacto</b>, por orden:
/// </para>
/// <list type="number">
/// <item>El corrector tiene que converger a una palabra que cumpla sus 83 ecuaciones.</item>
/// <item>El CRC de 14 bits tiene que cuadrar.</item>
/// <item>Los 77 bits tienen que desempaquetarse a un mensaje de un formato reconocido.</item>
/// </list>
/// <para>
/// El segundo es el que hace casi todo el trabajo, y por eso se cuenta aparte cuantas veces
/// salta: si esa cifra se desmadrara, seria la senal de que algo va mal mucho antes de que
/// apareciera un indicativo falso en la pantalla.
/// </para>
/// </remarks>
public sealed class Decodificador
{
    private readonly TablasDelProtocolo _tablas;
    private readonly Codificador _codificador;
    private readonly DecodificadorDeCreencia _corrector;
    private readonly RecuperacionProfunda _recuperacionProfunda;
    private readonly ILogger _registro;

    /// <summary>
    /// Ajuste del informe de senal, en decibelios.
    /// </summary>
    /// <remarks>
    /// La cuenta del informe sale de la teoria, pero la modulacion suavizada reparte parte de la
    /// energia fuera del tono que toca, asi que el valor medido queda algo por debajo del real.
    /// Esta correccion se saca <b>midiendo</b> en el banco de pruebas con senales de relacion
    /// conocida, no ajustando a ojo hasta que los numeros parezcan bonitos.
    /// </remarks>
    public const double CorreccionDelInforme = 0.0;

    /// <summary>Vueltas que se le dan al corrector antes de darlo por imposible.</summary>
    public int VueltasDelCorrector { get; set; } = 60;

    /// <summary>Candidatas que se miran como mucho en cada ventana.</summary>
    public int CandidatasPorVentana { get; set; } = 200;

    /// <summary>
    /// Se intenta la recuperacion profunda con las candidatas que la propagacion de creencias
    /// no consiga sacar.
    /// </summary>
    /// <remarks>
    /// Es lo que permite bajar unos decibelios mas, y es tambien lo unico de este decodificador
    /// que reconstruye un mensaje en lugar de corregirlo. Se puede apagar: cada decodificacion
    /// que sale por aqui viene marcada como tal, precisamente para poder decidir si compensa.
    /// </remarks>
    public bool UsarRecuperacionProfunda { get; set; } = true;

    /// <summary>
    /// Sincronismo minimo que tiene que tener una candidata para intentar la recuperacion profunda.
    /// </summary>
    /// <remarks>
    /// <para>
    /// La puntuacion del sincronizador vale uno cuando en ese sitio solo hay ruido y sube cuanto
    /// mas se parece lo que suena al patron de Costas. Las doscientas candidatas de una ventana
    /// son casi todas ruido que pasa de uno por casualidad; las que llevan senal de verdad pasan
    /// holgadamente de dos.
    /// </para>
    /// <para>
    /// <b>Por que hace falta esta puerta.</b> La propagacion de creencias se rinde sola con
    /// ruido y por eso se le pueden dar las doscientas candidatas sin peligro. La recuperacion
    /// profunda no se rinde nunca: siempre reconstruye <i>algo</i>. Dandole las doscientas, el
    /// CRC se tira doscientas veces por ventana en lugar de una cada veinte ventanas, y por
    /// mucho que cada tirada sea de uno entre dieciseis mil, doscientas por ventana acaban
    /// sacando un indicativo inventado cada pocos minutos. Medido: 12 por cada mil ventanas.
    /// </para>
    /// <para>
    /// Con el corte en dos, las candidatas que llegan a la recuperacion profunda pasan de 200 a
    /// menos de dos por ventana, y no se pierde ninguna decodificacion buena de las medidas en
    /// el banco: las senales que la recuperacion profunda rescata puntuan todas muy por encima.
    /// </para>
    /// </remarks>
    public double SincronismoMinimoParaLaProfunda { get; set; } = 2.0;

    /// <summary>Crea el decodificador.</summary>
    /// <param name="tablas">Tablas del protocolo.</param>
    /// <param name="registro">Para dejar constancia; por omision no se traza nada.</param>
    public Decodificador(TablasDelProtocolo tablas, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(tablas);
        _tablas = tablas;
        _codificador = new Codificador(tablas);
        _corrector = new DecodificadorDeCreencia(tablas.Ldpc);
        _recuperacionProfunda = new RecuperacionProfunda(tablas.Ldpc);
        _registro = registro ?? NullLogger.Instance;
    }

    /// <summary>Tablas con las que trabaja.</summary>
    public TablasDelProtocolo Tablas => _tablas;

    /// <summary>
    /// El corrector de errores, para poder ajustarlo desde el banco de medida.
    /// </summary>
    /// <remarks>
    /// Sus dos mandos —la atenuacion y el tope de confianza— no se tocan a ojo: se barren en el
    /// banco y se deja lo que salga medido. Esta expuesto para poder hacer ese barrido sin
    /// recompilar el modem entero con cada combinacion.
    /// </remarks>
    public DecodificadorDeCreencia Corrector => _corrector;

    /// <summary>La recuperacion profunda, con sus frenos, para poder medirla aparte.</summary>
    public RecuperacionProfunda Profunda => _recuperacionProfunda;

    /// <summary>
    /// Decodifica una ventana de audio.
    /// </summary>
    /// <param name="audio">Muestras de la ventana, de -1 a 1, un solo canal.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del audio.</param>
    /// <param name="modo">FT8 o FT4.</param>
    /// <param name="ventanaUtc">Momento en que empieza la ventana.</param>
    /// <param name="catalogo">Catalogo de indicativos para resolver los resumidos.</param>
    /// <param name="segundosDelPrimerMuestreo">
    /// Instante, respecto al comienzo de la ventana, de la primera muestra. Negativo si el audio
    /// empieza antes de la ventana, que es lo que permite pillar a quien transmite adelantado.
    /// </param>
    /// <param name="pista">
    /// Lo que ya se sabe del QSO en curso, para la decodificacion AP. Nula si no hay QSO en
    /// marcha o el corresponsal no se conoce todavia: la decodificacion sigue siendo a ciegas.
    /// </param>
    public ResultadoDeVentana Decodificar(
        ReadOnlySpan<float> audio,
        int frecuenciaDeMuestreo,
        ModoDelModem modo,
        DateTimeOffset ventanaUtc,
        CatalogoDeIndicativos catalogo,
        double segundosDelPrimerMuestreo = 0,
        PistaAp? pista = null)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        var reloj = Stopwatch.StartNew();
        var p = ParametrosDelModo.De(modo);

        var muestras = Math.Abs(frecuenciaDeMuestreo - p.FrecuenciaDeAnalisis) < 1e-6
            ? audio.ToArray()
            : Remuestreador.Remuestrear(audio, frecuenciaDeMuestreo, p.FrecuenciaDeAnalisis);

        if (muestras.Length < p.MuestrasDeLaSenal)
            return new ResultadoDeVentana([], 0, 0, 0, reloj.Elapsed);

        var analisis = AnalisisDeVentana.Calcular(muestras, p);
        var candidatas = Sincronizador.Buscar(analisis, CandidatasPorVentana);
        var demodulador = new Demodulador(analisis);

        var salida = new List<DecodificacionPropia>();
        var vistas = new List<(string Texto, double Tono)>();
        var palabra = new byte[_tablas.Ldpc.Longitud];
        var bits77 = new byte[MensajeDe77Bits.Bits];
        var confianzasConPista = pista is null ? null : new float[_tablas.Ldpc.Longitud];
        var palabrasValidas = 0;
        var rechazadasPorElCrc = 0;

        var porRecuperacionProfunda = 0;
        var porPista = 0;

        foreach (var candidata in candidatas)
        {
            var medida = demodulador.Medir(candidata, segundosDelPrimerMuestreo);

            // Primero la pasada normal. Si no cuaja y hay una pista del QSO en curso, se repite
            // la misma propagacion de creencias pero dando por ciertos los bits de los dos
            // indicativos: lo unico que le falta de verdad a la señal es el campo del informe. Si
            // tampoco asi cuaja, y solo si en ese sitio hay sincronismo de verdad, se reconstruye
            // a partir de los bits mas fiables. Las dos condiciones de mas abajo —el corte de
            // sincronismo y el de errores duros dentro de la recuperacion— son lo que impide que
            // esta vuelta de tuerca llene el cuaderno de contactos inventados; estan explicadas y
            // medidas en sus propias paginas.
            var profunda = false;
            var porEstaPista = false;
            var cuantosMirar = 1;

            if (!_corrector.TryDecodificar(demodulador.Confianzas, palabra, VueltasDelCorrector))
            {
                if (pista is { } pistaDelQso && confianzasConPista is not null)
                {
                    AplicarPista(demodulador.Confianzas, pistaDelQso, _corrector.TopeDeConfianza, confianzasConPista);
                    porEstaPista = _corrector.TryDecodificar(confianzasConPista, palabra, VueltasDelCorrector);
                }

                if (!porEstaPista)
                {
                    if (!UsarRecuperacionProfunda) continue;
                    if (candidata.Puntuacion < SincronismoMinimoParaLaProfunda) continue;
                    if (!_recuperacionProfunda.TryRecuperar(demodulador.Confianzas, palabra)) continue;
                    profunda = true;
                    cuantosMirar = _recuperacionProfunda.CandidatosEncontrados;
                }
            }

            // De la recuperacion profunda pueden salir varias reconstrucciones, ordenadas de la
            // mas creible a la menos. Se le da a cada una su oportunidad ante el CRC y se para en
            // cuanto una lo pasa: la buena suele ser la primera, pero no siempre.
            for (var c = 0; c < cuantosMirar; c++)
            {
                if (profunda && c > 0) _recuperacionProfunda.CopiarCandidato(c, palabra);
                palabrasValidas++;

                var conCrc = palabra.AsSpan(0, Crc14.BitsConCrc);
                if (!Crc14.EsValido(conCrc)) { rechazadasPorElCrc++; continue; }

                conCrc[..MensajeDe77Bits.Bits].CopyTo(bits77);
                if (modo == ModoDelModem.Ft4) _codificador.AplicarMezclaDeFt4(bits77);

                if (!MensajeDe77Bits.TryDesempaquetar(bits77, catalogo, out var mensaje)) continue;
                if (mensaje.Texto.Length == 0) continue;

                var tonoHz = medida.TonoBaseHz;
                if (YaEstaba(vistas, mensaje.Texto, tonoHz)) break;
                vistas.Add((mensaje.Texto, tonoHz));

                if (profunda) porRecuperacionProfunda++;
                if (porEstaPista) porPista++;
                salida.Add(new DecodificacionPropia(
                    mensaje.Texto,
                    Informe(medida, p),
                    Math.Round(medida.ComienzoEnSegundos - p.ComienzoNominalSegundos, 2),
                    (int)Math.Round(tonoHz),
                    modo,
                    ventanaUtc)
                {
                    Llamante = mensaje.Llamante,
                    Llamado = mensaje.Llamado,
                    Locator = mensaje.Locator,
                    EsCq = mensaje.EsCq,
                    EsRecuperacionProfunda = profunda,
                    EsPorPista = porEstaPista,
                });
                break;
            }
        }

        salida.Sort(static (x, y) => x.TonoHz.CompareTo(y.TonoHz));
        _registro.LogDebug(
            "Ventana {Ventana}: {Candidatas} candidatas, {Validas} palabras válidas, {Rechazadas} rechazadas por el CRC, {Salieron} decodificaciones ({Profundas} por recuperación profunda, {PorPista} por pista) en {Milisegundos} ms.",
            ventanaUtc, candidatas.Count, palabrasValidas, rechazadasPorElCrc, salida.Count, porRecuperacionProfunda, porPista, reloj.ElapsedMilliseconds);

        return new ResultadoDeVentana(salida, candidatas.Count, palabrasValidas, rechazadasPorElCrc, reloj.Elapsed)
        {
            PorRecuperacionProfunda = porRecuperacionProfunda,
            PorPista = porPista,
        };
    }

    /// <summary>
    /// Combina las confianzas medidas con la pista del QSO: en los bits de los indicativos se
    /// impone la maxima confianza en el valor que diga la pista; el resto —acuse, informe y los
    /// bits de paridad— se deja exactamente como lo midio el demodulador.
    /// </summary>
    private static void AplicarPista(ReadOnlySpan<float> confianzas, PistaAp pista, float topeDeConfianza, Span<float> destino)
    {
        confianzas.CopyTo(destino);
        for (var i = 0; i < MensajeDe77Bits.Bits; i++)
        {
            if (!pista.Conocido77[i]) continue;
            // Mismo convenio que el corrector: positivo es que el bit parece un cero.
            destino[i] = pista.Bits77[i] == 0 ? topeDeConfianza : -topeDeConfianza;
        }
    }

    /// <summary>
    /// Pasa las potencias medidas a un informe de senal en decibelios.
    /// </summary>
    /// <remarks>
    /// El informe de FT8 se da siempre referido a un ancho de banda de 2500 hercios, que es lo
    /// que ocuparia una emision de voz. Como aqui se mide en casillas de un tono de ancho, hay
    /// que extender el ruido medido a esos 2500 hercios antes de comparar; de ahi el factor.
    /// </remarks>
    private static int Informe(MedidaDeCandidata medida, ParametrosDelModo p)
    {
        if (medida.PotenciaDelRuido <= 0 || medida.PotenciaDeLaSenal <= 0) return -30;
        var ruidoEnLaBandaDeReferencia = medida.PotenciaDelRuido * (2500.0 / p.EspaciadoDeTonosHz);
        var db = (10 * Math.Log10(medida.PotenciaDeLaSenal / ruidoEnLaBandaDeReferencia)) + CorreccionDelInforme;
        return (int)Math.Round(Math.Clamp(db, -30, 50));
    }

    /// <summary>
    /// Dice si ese mismo mensaje ya salio en una frecuencia parecida.
    /// </summary>
    /// <remarks>
    /// Una senal fuerte deja rastro en varias candidatas vecinas y se decodifica varias veces.
    /// Se descartan las repeticiones por texto y frecuencia, no solo por texto: dos estaciones
    /// distintas pueden mandar el mismo mensaje —«CQ» a secas, o un 73— en la misma ventana.
    /// </remarks>
    private static bool YaEstaba(List<(string Texto, double Tono)> vistas, string texto, double tono)
    {
        foreach (var (t, f) in vistas)
            if (Math.Abs(f - tono) < 10 && string.Equals(t, texto, StringComparison.Ordinal)) return true;
        return false;
    }
}
