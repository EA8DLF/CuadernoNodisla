using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Senal;

namespace Nodisla.Cuaderno.Modos.Msk144;

/// <summary>Como fue la decodificacion de un trozo de audio MSK144, con el detalle para medir.</summary>
/// <param name="Decodificaciones">Lo que se saco, ya sin repeticiones.</param>
/// <param name="Candidatas">Posiciones de trama que se miraron de cerca.</param>
/// <param name="PalabrasValidas">Veces que el corrector devolvio una palabra que cuadraba.</param>
/// <param name="RechazadasPorElCrc">Palabras validas que el CRC (o el resumen, en los cortos) tiro.</param>
/// <param name="Duracion">Lo que costo.</param>
public sealed record ResultadoMsk144(
    IReadOnlyList<DecodificacionPropia> Decodificaciones,
    int Candidatas,
    int PalabrasValidas,
    int RechazadasPorElCrc,
    TimeSpan Duracion)
{
    /// <summary>Decodificaciones que solo salieron promediando varias tramas.</summary>
    public int PorPromediado { get; init; }

    /// <summary>Decodificaciones de mensajes cortos.</summary>
    public int Cortos { get; init; }

    /// <summary>Una linea por palabra que el corrector dio por valida, pasara o no los sellos. Para medir.</summary>
    public IReadOnlyList<DetalleMsk144> Detalles { get; init; } = [];
}

/// <summary>Una palabra valida del corrector, con lo que hace falta para afinar los frenos.</summary>
/// <param name="EsCorto">Era una trama corta.</param>
/// <param name="Tramas">Cuantas tramas se sumaron.</param>
/// <param name="Sincronismo">Puntuacion de sincronismo de la candidata, con una trama.</param>
/// <param name="SincronismoCoherente">Sincronismo coherente de las tramas sumadas, en veces el ruido.</param>
/// <param name="ErroresDuros">Bits en que la palabra contradice la decision dura del demodulador.</param>
/// <param name="Aceptada">Paso el CRC (o el resumen) y se desempaqueto.</param>
/// <param name="Texto">El mensaje, si se acepto.</param>
public sealed record DetalleMsk144(bool EsCorto, int Tramas, double Sincronismo, double SincronismoCoherente, int ErroresDuros, bool Aceptada, string? Texto);

/// <summary>
/// Decodifica MSK144: encuentra los pings, los demodula coherentemente y corrige los errores.
/// </summary>
/// <remarks>
/// <para>
/// <b>Como se hace, en cristiano.</b> Primero se baja todo el audio a banda base —se le quita
/// la portadora de 1500 Hz— y se pasa por el filtro adaptado al pulso de medio seno, con lo que
/// cada bit queda reducido a un numero complejo en su instante. Despues se recorre ese vector
/// buscando las dos palabras de sincronismo de la trama: donde las dos aparecen a 28 ms una de
/// otra, hay una trama. Con las dos se saca ademas la frecuencia exacta y la fase de la
/// portadora, que es lo que permite demodular <b>coherentemente</b>: cada bit se lee como el
/// signo de la parte real (canal en fase) o imaginaria (canal en cuadratura) de su numero,
/// girado para deshacer la fase de la portadora. De ahi salen 128 confianzas que van al
/// corrector de errores, y si la palabra cuadra y el CRC tambien, hay mensaje.
/// </para>
/// <para>
/// <b>Promediado de tramas.</b> Como la misma trama se repite sin parar, si el ping dura mas de
/// una trama se pueden sumar en fase varias consecutivas antes de demodular: dos tramas dan 3
/// dB mas, siete dan 8,5. Para eso hace falta la frecuencia con precision de decimas de hercio,
/// que se saca de como gira la fase del sincronismo de una trama a la siguiente.
/// </para>
/// <para>
/// <b>No inventar.</b> El CRC de trece bits deja pasar una palabra de ruido de cada ocho mil, y
/// aqui se hacen muchos mas intentos por periodo que en FT8: por eso solo se intenta el
/// corrector donde el sincronismo de verdad destaca, y el mensaje corto —que no lleva CRC— solo
/// se acepta si su resumen coincide con un par de indicativos que el operador haya dado a
/// conocer.
/// </para>
/// <para>
/// El decodificador trabaja sobre cualquier trozo de audio, no solo sobre la ventana entera:
/// <see cref="Decodificar"/> devuelve cada ping con su instante, asi que el modem puede
/// llamarlo cada medio segundo y ensenar los pings segun caen, que es como se opera en
/// dispersion meteorica.
/// </para>
/// </remarks>
public sealed class DecodificadorMsk144
{
    private const int Fs = ParametrosMsk144.FrecuenciaDeAnalisis;
    private const int Mpb = ParametrosMsk144.MuestrasPorBit;
    private const int TapsDelPulso = 11;
    private const int PasoDeLaRejillaHz = 30;

    private static readonly float[] Pulso = CalcularPulso();
    private static readonly int[] EscalaDePromediado = [1, 2, 3, 4, 5, 7];

    private readonly TablaLdpc _larga;
    private readonly TablaLdpc _corta;
    private readonly DecodificadorDeCreencia _correctorLargo;
    private readonly DecodificadorDeCreencia _correctorCorto;
    private readonly ILogger _registro;
    private readonly Dictionary<int, (string Llamado, string Propio)> _paresConocidos = [];

    private readonly Formato _formatoLargo = Formato.Largo();
    private readonly Formato _formatoCorto = Formato.Corto();

    /// <summary>Crea el decodificador con las tablas de los dos codigos.</summary>
    public DecodificadorMsk144(TablaLdpc tablaLarga, TablaLdpc tablaCorta, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(tablaLarga);
        ArgumentNullException.ThrowIfNull(tablaCorta);
        _larga = tablaLarga;
        _corta = tablaCorta;
        _correctorLargo = new DecodificadorDeCreencia(tablaLarga.Codigo);
        _correctorCorto = new DecodificadorDeCreencia(tablaCorta.Codigo);
        _registro = registro ?? NullLogger.Instance;
    }

    /// <summary>Tabla del codigo largo.</summary>
    public TablaLdpc TablaLarga => _larga;

    /// <summary>Tabla del codigo corto.</summary>
    public TablaLdpc TablaCorta => _corta;

    /// <summary>Hasta cuantos hercios a cada lado de la portadora se busca.</summary>
    public double ToleranciaHz { get; set; } = 100;

    /// <summary>Posiciones de trama que se miran de cerca, como mucho, por cada segundo de audio.</summary>
    public int CandidatasPorSegundo { get; set; } = 12;

    /// <summary>
    /// Sincronismo minimo para mirar una posicion de cerca.
    /// </summary>
    /// <remarks>
    /// La puntuacion vale alrededor de 0,9 donde solo hay ruido: es la suma de las dos
    /// correlaciones de sincronismo medida en veces lo que daria el ruido solo. Una trama que se
    /// va a poder decodificar puntua bastante mas; el corte es lo que impide tirar el CRC miles
    /// de veces por periodo sobre ruido puro.
    /// </remarks>
    public double SincronismoMinimo { get; set; } = 1.7;

    /// <summary>
    /// Bits en que la palabra reconstruida puede contradecir al demodulador, como mucho.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Este es el freno que hace falta en MSK144 y no en FT8.</b> El codigo (128,90) es de
    /// tasa alta y tiene solo 38 ecuaciones: con ruido puro, la propagacion de creencias
    /// converge a <i>alguna</i> palabra valida en un tercio de los intentos. A doscientos
    /// intentos por periodo, eso son sesenta tiradas del CRC de trece bits por periodo, y el
    /// CRC dejaria pasar un mensaje inventado cada dos horas. El freno es que la palabra tiene
    /// que parecerse a lo que oyo el demodulador: las de verdad, a la relacion en que se
    /// decodifican, contradicen a la decision dura en pocos bits; las de ruido, en muchos.
    /// </para>
    /// <para>
    /// El numero sale de medir, ver el banco de MSK144: es el que no pierde ninguna
    /// decodificacion buena y corta todas las de ruido.
    /// </para>
    /// </remarks>
    public int ErroresDurosMaximos { get; set; } = 22;

    /// <summary>
    /// Sincronismo coherente minimo para intentar el corrector con una suma de tramas.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Es <b>el freno que de verdad separa</b> el ruido de la senal en MSK144. La puntuacion de
    /// candidata se mide con una trama; al sumar N tramas en fase, la correlacion del
    /// sincronismo de una senal de verdad crece con N y la del ruido no. Se mide en veces lo
    /// que daria el ruido solo: alrededor de 0,9 sin senal.
    /// </para>
    /// <para>
    /// Sin este freno, la propagacion de creencias con el codigo (128,90) daba por validas
    /// treinta y siete palabras de ruido por periodo, y a una entre ocho mil que deja pasar el
    /// CRC de trece bits eso era un mensaje inventado cada hora. El valor sale del banco.
    /// </para>
    /// </remarks>
    public double SincronismoCoherenteMinimo { get; set; } = 4.2;

    /// <summary>Sincronismo coherente minimo para intentar el corrector con una trama corta.</summary>
    public double SincronismoCoherenteMinimoCorto { get; set; } = 3.0;

    /// <summary>
    /// Sincronismo coherente a partir del cual un mensaje corto se acepta con una sola
    /// decodificacion. Por debajo, hace falta que el mismo mensaje salga dos veces —en dos
    /// tramas o con dos sumas distintas— dentro del mismo ping.
    /// </summary>
    /// <remarks>
    /// La trama corta no lleva CRC y su codigo (32,16) es de tasa un medio: el ruido produce
    /// palabras validas a cientos por periodo y el resumen de 12 bits solo tira una de cada
    /// cuatro mil. Medido en el banco, el ruido llega a 4,5 de sincronismo coherente; los
    /// mensajes cortos de verdad quedan entre 3 y 5,5. La confirmacion por duplicado es lo que
    /// permite bajar el corte sin inventar informes.
    /// </remarks>
    public double SincronismoCoherenteSeguroCorto { get; set; } = 4.75;

    /// <summary>
    /// Sincronismo minimo para mirar de cerca una trama corta.
    /// </summary>
    /// <remarks>
    /// La trama corta solo tiene una palabra de sincronismo de 8 bits y ningun CRC: su unico
    /// sello es el resumen de 12 bits. Por eso se le exige mas antes de intentar nada; el
    /// numero sale de medir cuantas tramas cortas inventa el ruido puro, ver el banco.
    /// </remarks>
    public double SincronismoMinimoCorto { get; set; } = 2.6;

    /// <summary>
    /// Bits en que la palabra corta reconstruida puede contradecir al demodulador, como mucho.
    /// </summary>
    /// <remarks>
    /// Es el segundo freno de los mensajes cortos, el mismo que usa la recuperacion profunda de
    /// FT8: una trama corta de verdad, a la relacion en que se puede decodificar, llega con
    /// pocos bits mal de 32; una palabra a la que el corrector ha llegado desde ruido contradice
    /// a la mitad.
    /// </remarks>
    public int ErroresDurosMaximosCortos { get; set; } = 5;

    /// <summary>Cuantas tramas consecutivas se suman en fase como mucho.</summary>
    public int TramasAPromediar { get; set; } = 7;

    /// <summary>Vueltas del corrector.</summary>
    public int VueltasDelCorrector { get; set; } = 60;

    /// <summary>Ajuste del informe de senal, en decibelios, medido en el banco.</summary>
    public double CorreccionDelInforme { get; set; } = 0;

    /// <summary>Pares de indicativos (llamado, propio) cuyos mensajes cortos se aceptan.</summary>
    public IReadOnlyCollection<(string Llamado, string Propio)> ParesConocidos => _paresConocidos.Values;

    /// <summary>
    /// Da a conocer un par de indicativos para poder recibir sus mensajes cortos.
    /// </summary>
    /// <remarks>
    /// Lo normal es dar el par «mi indicativo, el del corresponsal» al empezar un contacto: los
    /// mensajes cortos que mande el corresponsal llevan el resumen de ese par, en ese orden.
    /// </remarks>
    public void RecordarPar(string llamado, string propio)
    {
        var resumen = MensajeCortoMsk144.Resumen(llamado, propio);
        _paresConocidos[resumen] = (llamado.Trim().ToUpperInvariant(), propio.Trim().ToUpperInvariant());
    }

    /// <summary>Olvida todos los pares de mensajes cortos.</summary>
    public void OlvidarPares() => _paresConocidos.Clear();

    /// <summary>
    /// Decodifica un trozo de audio, que puede ser la ventana entera o un bloque de ella.
    /// </summary>
    /// <param name="audio">Muestras, mono, de -1 a 1.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del audio.</param>
    /// <param name="ventanaUtc">Ventana a la que pertenece.</param>
    /// <param name="catalogo">Catalogo de indicativos para resolver los resumidos.</param>
    /// <param name="segundosDelPrimerMuestreo">Instante de la primera muestra respecto al comienzo de la ventana.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public ResultadoMsk144 Decodificar(
        ReadOnlySpan<float> audio,
        int frecuenciaDeMuestreo,
        DateTimeOffset ventanaUtc,
        CatalogoDeIndicativos catalogo,
        double segundosDelPrimerMuestreo = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        var reloj = Stopwatch.StartNew();

        var muestras = frecuenciaDeMuestreo == Fs
            ? audio.ToArray()
            : Remuestreador.Remuestrear(audio, frecuenciaDeMuestreo, Fs);

        if (muestras.Length < ParametrosMsk144.MuestrasPorTrama + (2 * Mpb))
            return new ResultadoMsk144([], 0, 0, 0, reloj.Elapsed);

        var (qr, qi) = BandaBaseFiltrada(muestras);
        var ruido = PotenciaDelRuido(qr, qi);
        if (ruido <= 0) return new ResultadoMsk144([], 0, 0, 0, reloj.Elapsed);

        var brutas = new List<Decodificacion>();
        var cuentas = new Cuentas();
        var maximo = Math.Max(4, (int)Math.Ceiling(CandidatasPorSegundo * (double)muestras.Length / Fs));

        DecodificarConFormato(_formatoLargo, _correctorLargo, qr, qi, ruido, maximo, catalogo, brutas, cuentas, ct);
        if (_paresConocidos.Count > 0)
            DecodificarConFormato(_formatoCorto, _correctorCorto, qr, qi, ruido, maximo, catalogo, brutas, cuentas, ct);

        var salida = Depurar(brutas, ventanaUtc, segundosDelPrimerMuestreo);
        _registro.LogDebug(
            "MSK144 {Ventana}: {Candidatas} candidatas, {Validas} palabras válidas, {Rechazadas} rechazadas, {Salieron} decodificaciones en {Ms} ms.",
            ventanaUtc, cuentas.Candidatas, cuentas.PalabrasValidas, cuentas.Rechazadas, salida.Count, reloj.ElapsedMilliseconds);

        return new ResultadoMsk144(salida, cuentas.Candidatas, cuentas.PalabrasValidas, cuentas.Rechazadas, reloj.Elapsed)
        {
            PorPromediado = cuentas.PorPromediado,
            Cortos = cuentas.Cortos,
            Detalles = cuentas.Detalles,
        };
    }

    // ----------------------------------------------------------------------------------------
    // Banda base y filtro adaptado
    // ----------------------------------------------------------------------------------------

    private static float[] CalcularPulso()
    {
        var p = new float[TapsDelPulso];
        var centro = TapsDelPulso / 2;
        for (var m = 0; m < TapsDelPulso; m++)
            p[m] = (float)Math.Cos(Math.PI * (m - centro) / (2.0 * Mpb));
        return p;
    }

    /// <summary>
    /// Baja el audio a banda base y lo pasa por el filtro adaptado al medio seno.
    /// </summary>
    /// <remarks>
    /// Con la portadora en un octavo de la frecuencia de muestreo, quitarla es multiplicar por
    /// una tabla de ocho valores. El filtro adaptado es el propio pulso: once muestras de coseno.
    /// A la salida, el valor en el instante de cada bit es su «numero»: la energia del bit
    /// concentrada en un complejo cuya fase lleva la de la portadora.
    /// </remarks>
    private static (float[] Real, float[] Imaginaria) BandaBaseFiltrada(float[] muestras)
    {
        var n = muestras.Length;
        var zr = new float[n];
        var zi = new float[n];
        Span<float> cos = stackalloc float[8];
        Span<float> sen = stackalloc float[8];
        for (var k = 0; k < 8; k++)
        {
            cos[k] = (float)Math.Cos(Math.PI * k / 4);
            sen[k] = (float)Math.Sin(Math.PI * k / 4);
        }
        for (var k = 0; k < n; k++)
        {
            var x = muestras[k];
            zr[k] = x * cos[k & 7];
            zi[k] = -x * sen[k & 7];
        }

        var qr = new float[n];
        var qi = new float[n];
        var mitad = TapsDelPulso / 2;
        for (var k = mitad; k < n - mitad; k++)
        {
            float sr = 0, si = 0;
            for (var m = 0; m < TapsDelPulso; m++)
            {
                var j = k + m - mitad;
                sr += zr[j] * Pulso[m];
                si += zi[j] * Pulso[m];
            }
            qr[k] = sr;
            qi[k] = si;
        }
        return (qr, qi);
    }

    /// <summary>
    /// Potencia media del ruido a la salida del filtro adaptado.
    /// </summary>
    /// <remarks>
    /// Se toma el cuartil bajo de la potencia instantanea y se corrige con la forma que tiene el
    /// ruido gaussiano complejo —exponencial—, que da su media a partir de cualquier cuantil. El
    /// cuartil bajo y no la media para que un ping fuerte, o una senal continua, no engorde la
    /// medida del ruido.
    /// </remarks>
    private static double PotenciaDelRuido(float[] qr, float[] qi)
    {
        var paso = Math.Max(1, qr.Length / 20000);
        var lista = new List<float>(qr.Length / paso + 1);
        for (var i = 0; i < qr.Length; i += paso) lista.Add((qr[i] * qr[i]) + (qi[i] * qi[i]));
        lista.Sort();
        var cuartil = lista[lista.Count / 4];
        return cuartil / 0.28768207;
    }

    // ----------------------------------------------------------------------------------------
    // Busqueda de tramas
    // ----------------------------------------------------------------------------------------

    /// <summary>Lo que distingue la trama larga de la corta: donde va el sincronismo y que codigo lleva.</summary>
    private sealed class Formato
    {
        public int BitsPorTrama;
        public int MuestrasPorTrama;
        public int[] BitsDeSincronismo = [];   // posiciones dentro de la trama
        public float[] SignoDeSincronismo = []; // +1 para bit 1, -1 para bit 0
        public int[] BitsDeDatos = [];          // posicion en la trama de cada bit de la palabra
        public bool EsCorto;

        public static Formato Largo()
        {
            var f = new Formato
            {
                BitsPorTrama = ParametrosMsk144.BitsPorTrama,
                MuestrasPorTrama = ParametrosMsk144.MuestrasPorTrama,
                EsCorto = false,
            };
            var sync = ParametrosMsk144.Sincronismo;
            var posiciones = new List<int>();
            var signos = new List<float>();
            foreach (var inicio in new[] { ParametrosMsk144.PrimerSincronismo, ParametrosMsk144.SegundoSincronismo })
                for (var k = 0; k < sync.Length; k++)
                {
                    posiciones.Add(inicio + k);
                    signos.Add(sync[k] != 0 ? 1f : -1f);
                }
            f.BitsDeSincronismo = [.. posiciones];
            f.SignoDeSincronismo = [.. signos];
            f.BitsDeDatos = new int[ParametrosMsk144.BitsDePalabra];
            for (var i = 0; i < f.BitsDeDatos.Length; i++) f.BitsDeDatos[i] = ParametrosMsk144.PosicionEnLaTrama(i);
            return f;
        }

        public static Formato Corto()
        {
            var f = new Formato
            {
                BitsPorTrama = ParametrosMsk144.BitsPorTramaCorta,
                MuestrasPorTrama = ParametrosMsk144.MuestrasPorTramaCorta,
                EsCorto = true,
            };
            var sync = ParametrosMsk144.SincronismoCorto;
            f.BitsDeSincronismo = new int[sync.Length];
            f.SignoDeSincronismo = new float[sync.Length];
            for (var k = 0; k < sync.Length; k++)
            {
                f.BitsDeSincronismo[k] = k;
                f.SignoDeSincronismo[k] = sync[k] != 0 ? 1f : -1f;
            }
            f.BitsDeDatos = new int[ParametrosMsk144.BitsDePalabraCorta];
            for (var i = 0; i < f.BitsDeDatos.Length; i++) f.BitsDeDatos[i] = ParametrosMsk144.BitsDeSincronismo + i;
            return f;
        }
    }

    private sealed class Cuentas
    {
        public int Candidatas;
        public int PalabrasValidas;
        public int Rechazadas;
        public int PorPromediado;
        public int Cortos;
        public List<DetalleMsk144> Detalles = [];
    }

    private readonly record struct Decodificacion(string Texto, double Segundo, double Hz, double Decibelios, int Tramas, bool EsCorto, double Coherente, MensajeDescifrado? Mensaje);

    /// <summary>
    /// Correlacion coherente del sincronismo en una posicion, a una frecuencia dada.
    /// </summary>
    /// <remarks>
    /// Cada bit de sincronismo aporta su numero girado para deshacer la desviacion de
    /// frecuencia, con el signo del bit, y tomado del canal que le toca: los impares tal cual
    /// (en fase) y los pares multiplicados por −j (cuadratura). Con la senal exacta, la suma cae
    /// en el eje real positivo; la fase de la suma es la fase de la portadora.
    /// </remarks>
    private static (double Re, double Im) CorrelarSincronismo(Formato f, float[] qr, float[] qi, int n0, double dfHz, int desde, int hasta)
    {
        double re = 0, im = 0;
        var giroPorMuestra = -2 * Math.PI * dfHz / Fs;
        for (var k = desde; k < hasta; k++)
        {
            var bit = f.BitsDeSincronismo[k];
            var j = n0 + (bit * Mpb);
            var x = qr[j];
            var y = qi[j];
            // Canal: impar -> en fase (tal cual); par -> cuadratura (multiplicar por -j).
            if ((bit & 1) == 0) (x, y) = (y, -x);
            var angulo = giroPorMuestra * bit * Mpb;
            var c = Math.Cos(angulo);
            var s = Math.Sin(angulo);
            var signo = f.SignoDeSincronismo[k];
            re += signo * ((x * c) - (y * s));
            im += signo * ((x * s) + (y * c));
        }
        return (re, im);
    }

    private void DecodificarConFormato(
        Formato f,
        DecodificadorDeCreencia corrector,
        float[] qr,
        float[] qi,
        double ruido,
        int maximo,
        CatalogoDeIndicativos catalogo,
        List<Decodificacion> salida,
        Cuentas cuentas,
        CancellationToken ct)
    {
        var n = qr.Length;
        var ultimo = n - f.MuestrasPorTrama - Mpb;
        if (ultimo <= 0) return;

        // Rejilla gruesa de frecuencia: con pasos de 30 Hz el resto queda por debajo de 15, que
        // es lo que la fase entre las dos palabras de sincronismo puede resolver sin ambiguedad.
        var pasos = (int)Math.Ceiling(ToleranciaHz / PasoDeLaRejillaHz);
        var rejilla = new double[(2 * pasos) + 1];
        for (var i = 0; i < rejilla.Length; i++) rejilla[i] = (i - pasos) * PasoDeLaRejillaHz;

        var sincronismos = f.BitsDeSincronismo.Length;
        var mitad = f.EsCorto ? sincronismos : sincronismos / 2;
        var escala = 1.0 / (Math.Sqrt(mitad * ruido) * (f.EsCorto ? 1 : 2));

        var puntuacion = new float[ultimo];
        var mejorRejilla = new byte[ultimo];
        for (var n0 = 0; n0 < ultimo; n0++)
        {
            var mejor = 0.0;
            byte indice = 0;
            for (var r = 0; r < rejilla.Length; r++)
            {
                var (re1, im1) = CorrelarSincronismo(f, qr, qi, n0, rejilla[r], 0, mitad);
                var valor = Math.Sqrt((re1 * re1) + (im1 * im1));
                if (!f.EsCorto)
                {
                    var (re2, im2) = CorrelarSincronismo(f, qr, qi, n0, rejilla[r], mitad, sincronismos);
                    valor += Math.Sqrt((re2 * re2) + (im2 * im2));
                }
                if (valor > mejor) { mejor = valor; indice = (byte)r; }
            }
            puntuacion[n0] = (float)(mejor * escala);
            mejorRejilla[n0] = indice;
        }

        // Candidatas: maximos locales por encima del corte, las mejores primero.
        var candidatas = new List<(int N0, float Puntos)>();
        var radio = 2 * Mpb;
        for (var n0 = 0; n0 < ultimo; n0++)
        {
            var v = puntuacion[n0];
            if (v < (f.EsCorto ? SincronismoMinimoCorto : SincronismoMinimo)) continue;
            var esMaximo = true;
            for (var d = -radio; d <= radio && esMaximo; d++)
            {
                if (d == 0) continue;
                var j = n0 + d;
                if (j < 0 || j >= ultimo) continue;
                if (puntuacion[j] > v) esMaximo = false;
            }
            if (esMaximo) candidatas.Add((n0, v));
        }
        candidatas.Sort(static (a, b) => b.Puntos.CompareTo(a.Puntos));
        if (candidatas.Count > maximo) candidatas.RemoveRange(maximo, candidatas.Count - maximo);

        var confianzas = new float[f.BitsDeDatos.Length];
        var palabra = new byte[f.BitsDeDatos.Length];
        var sumaR = new double[f.BitsPorTrama];
        var sumaI = new double[f.BitsPorTrama];

        foreach (var (n0, puntos) in candidatas)
        {
            ct.ThrowIfCancellationRequested();
            cuentas.Candidatas++;

            var dfGrueso = rejilla[mejorRejilla[n0]];
            var (df, fase, magnitud) = AfinarFrecuencia(f, qr, qi, n0, dfGrueso);

            // Primero una sola trama; si no cuaja y hay mas audio, se suman tramas en fase.
            // Como en el articulo: una, dos, tres, cuatro, cinco y siete tramas.
            var tramasMaximas = Math.Max(1, TramasAPromediar);
            foreach (var tramas in EscalaDePromediado)
            {
                if (tramas > tramasMaximas) break;
                if (tramas > 1 && n0 + (tramas * f.MuestrasPorTrama) + Mpb > n) break;

                double dfUsada = df, faseUsada = fase, magnitudUsada = magnitud;
                if (tramas > 1)
                {
                    if (!AfinarConVariasTramas(f, qr, qi, n0, tramas, df, out dfUsada, out faseUsada, out magnitudUsada)) break;
                }
                AfinarConLosDatos(f, qr, qi, n0, tramas, ref dfUsada, ref faseUsada, ref magnitudUsada, sumaR, sumaI);

                // El sincronismo coherente de las tramas sumadas: lo que de verdad dice si
                // ahi hay senal. Si no llega, no merece la pena ni intentar el corrector.
                var coherente = magnitudUsada / Math.Sqrt(sincronismos * ruido / tramas);
                if (coherente < (f.EsCorto ? SincronismoCoherenteMinimoCorto : SincronismoCoherenteMinimo)) continue;

                PromediarTramas(f, qr, qi, n0, tramas, dfUsada, sumaR, sumaI);
                var amplitud = Math.Sqrt(Math.Max(0, (magnitudUsada * magnitudUsada) - (sincronismos * ruido / tramas))) / sincronismos;
                var varianza = ruido / (2.0 * tramas);
                Demodular(f, sumaR, sumaI, faseUsada, amplitud, varianza, confianzas);

                if (!corrector.TryDecodificar(confianzas, palabra, VueltasDelCorrector)) continue;
                cuentas.PalabrasValidas++;

                var errores = 0;
                for (var i = 0; i < palabra.Length; i++)
                    if ((confianzas[i] < 0 ? 1 : 0) != palabra[i]) errores++;
                if (errores > (f.EsCorto ? ErroresDurosMaximosCortos : ErroresDurosMaximos))
                {
                    cuentas.Rechazadas++;
                    cuentas.Detalles.Add(new DetalleMsk144(f.EsCorto, tramas, puntos, coherente, errores, false, null));
                    continue;
                }

                var db = Informe(amplitud, ruido);
                var hz = ParametrosMsk144.PortadoraHz + dfUsada;
                var segundo = (double)n0 / Fs;

                if (f.EsCorto)
                {
                    Span<byte> bits16 = stackalloc byte[ParametrosMsk144.BitsDeMensajeCorto];
                    _corta.Codigo.ExtraerMensaje(palabra, bits16);
                    var (resumen, informe) = MensajeCortoMsk144.Desempaquetar(bits16);
                    if (!_paresConocidos.TryGetValue(resumen, out var par))
                    {
                        cuentas.Rechazadas++;
                        cuentas.Detalles.Add(new DetalleMsk144(true, tramas, puntos, coherente, errores, false, null));
                        continue;
                    }
                    var texto = MensajeCortoMsk144.Texto(par.Llamado, par.Propio, informe);
                    salida.Add(new Decodificacion(texto, segundo, hz, db, tramas, EsCorto: true, coherente, null));
                    cuentas.Detalles.Add(new DetalleMsk144(true, tramas, puntos, coherente, errores, true, texto));
                    cuentas.Cortos++;
                }
                else
                {
                    var conCrc = palabra.AsSpan(0, Crc13.BitsConCrc);
                    if (!Crc13.EsValido(conCrc)
                        || !MensajeDe77Bits.TryDesempaquetar(conCrc[..ParametrosMsk144.BitsDeMensaje], catalogo, out var mensaje)
                        || mensaje.Texto.Length == 0)
                    {
                        cuentas.Rechazadas++;
                        cuentas.Detalles.Add(new DetalleMsk144(false, tramas, puntos, coherente, errores, false, null));
                        continue;
                    }
                    salida.Add(new Decodificacion(mensaje.Texto, segundo, hz, db, tramas, EsCorto: false, coherente, mensaje));
                    cuentas.Detalles.Add(new DetalleMsk144(false, tramas, puntos, coherente, errores, true, mensaje.Texto));
                }
                if (tramas > 1) cuentas.PorPromediado++;
                break;
            }
        }
    }

    /// <summary>
    /// Afina la frecuencia alrededor del valor grueso y saca la fase de la portadora.
    /// </summary>
    /// <remarks>
    /// Se recorre en pasos de un hercio y luego de un cuarto, quedandose con la frecuencia en
    /// que la correlacion coherente de todo el sincronismo es mayor. La fase de esa correlacion
    /// es la de la portadora, que es lo que hace falta para demodular.
    /// </remarks>
    private static (double Df, double Fase, double Magnitud) AfinarFrecuencia(Formato f, float[] qr, float[] qi, int n0, double dfGrueso)
    {
        var total = f.BitsDeSincronismo.Length;
        var mejorDf = dfGrueso;
        var mejor = -1.0;
        double mejorRe = 0, mejorIm = 0;

        void Probar(double df)
        {
            var (re, im) = CorrelarSincronismo(f, qr, qi, n0, df, 0, total);
            var m = (re * re) + (im * im);
            if (m > mejor) { mejor = m; mejorDf = df; mejorRe = re; mejorIm = im; }
        }

        // La trama corta solo tiene una palabra de 4 ms: no resuelve mas fino que la rejilla.
        var alcance = f.EsCorto ? 15.0 : PasoDeLaRejillaHz / 2.0 + 1;
        var paso = f.EsCorto ? 5.0 : 1.0;
        for (var df = dfGrueso - alcance; df <= dfGrueso + alcance + 1e-9; df += paso) Probar(df);
        if (!f.EsCorto)
        {
            var centro = mejorDf;
            for (var df = centro - 0.75; df <= centro + 0.75 + 1e-9; df += 0.25) Probar(df);
        }
        return (mejorDf, Math.Atan2(mejorIm, mejorRe), Math.Sqrt(mejor));
    }

    /// <summary>
    /// Con varias tramas, la frecuencia se afina mirando como gira la fase del sincronismo de
    /// una trama a la siguiente, y se comprueba que las tramas siguen ahi.
    /// </summary>
    private static bool AfinarConVariasTramas(Formato f, float[] qr, float[] qi, int n0, int tramas, double df, out double dfFina, out double fase, out double magnitud)
    {
        var total = f.BitsDeSincronismo.Length;
        var fases = new double[tramas];
        var giroPorTrama = 2 * Math.PI * df * f.MuestrasPorTrama / Fs;
        for (var k = 0; k < tramas; k++)
        {
            var (re, im) = CorrelarSincronismo(f, qr, qi, n0 + (k * f.MuestrasPorTrama), df, 0, total);
            // Se deshace el giro que la desviacion ya conocida impone entre tramas.
            var angulo = Math.Atan2(im, re) - (k * giroPorTrama);
            fases[k] = angulo;
        }

        // Ajuste lineal de la fase desenrollada frente al numero de trama.
        var desenrollada = new double[tramas];
        desenrollada[0] = fases[0];
        for (var k = 1; k < tramas; k++)
        {
            var d = fases[k] - fases[k - 1];
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            desenrollada[k] = desenrollada[k - 1] + d;
        }
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (var k = 0; k < tramas; k++)
        {
            sx += k; sy += desenrollada[k]; sxx += k * k; sxy += k * desenrollada[k];
        }
        var pendiente = ((tramas * sxy) - (sx * sy)) / ((tramas * sxx) - (sx * sx));
        var correccion = pendiente / (2 * Math.PI * f.MuestrasPorTrama / Fs);
        // Una correccion mayor que la ambiguedad de la fase no es de fiar.
        if (Math.Abs(correccion) > Fs / (2.0 * f.MuestrasPorTrama)) { dfFina = df; fase = 0; magnitud = 0; return false; }
        dfFina = df + correccion;

        // Correlacion coherente de todas las tramas con la frecuencia afinada.
        double sre = 0, sim = 0;
        var giroFino = -2 * Math.PI * dfFina * f.MuestrasPorTrama / Fs;
        for (var k = 0; k < tramas; k++)
        {
            var (re, im) = CorrelarSincronismo(f, qr, qi, n0 + (k * f.MuestrasPorTrama), dfFina, 0, total);
            var c = Math.Cos(k * giroFino);
            var s = Math.Sin(k * giroFino);
            sre += (re * c) - (im * s);
            sim += (re * s) + (im * c);
        }
        fase = Math.Atan2(sim, sre);
        magnitud = Math.Sqrt((sre * sre) + (sim * sim)) / tramas;
        return true;
    }

    /// <summary>Correlacion coherente del sincronismo de varias tramas seguidas, a una frecuencia.</summary>
    private static (double Re, double Im) SincronismoCoherente(Formato f, float[] qr, float[] qi, int n0, int tramas, double df)
    {
        var total = f.BitsDeSincronismo.Length;
        double sre = 0, sim = 0;
        var giro = -2 * Math.PI * df * f.MuestrasPorTrama / Fs;
        for (var k = 0; k < tramas; k++)
        {
            var (re, im) = CorrelarSincronismo(f, qr, qi, n0 + (k * f.MuestrasPorTrama), df, 0, total);
            var c = Math.Cos(k * giro);
            var s = Math.Sin(k * giro);
            sre += (re * c) - (im * s);
            sim += (re * s) + (im * c);
        }
        return (sre, sim);
    }

    /// <summary>
    /// Afina la frecuencia con los propios datos, no solo con el sincronismo.
    /// </summary>
    /// <remarks>
    /// Dieciseis bits de sincronismo dan la frecuencia con un error de unos hercios cuando la
    /// senal es floja, y tres hercios en 72 ms son ochenta grados de fase al final de la trama:
    /// bastante para perder el mensaje. Los otros 128 bits tambien saben a que frecuencia
    /// estan: a la buena, cada uno cae entero en su canal y la suma de sus valores absolutos es
    /// maxima; a una frecuencia equivocada se reparten entre los dos canales y la suma baja. Se
    /// prueba una rejilla fina alrededor de lo que dio el sincronismo y se toma la que mas
    /// «abre el ojo». Con varias tramas sumadas el paso se hace mas fino, porque la coherencia
    /// tiene que aguantar mas tiempo.
    /// </remarks>
    private static void AfinarConLosDatos(Formato f, float[] qr, float[] qi, int n0, int tramas, ref double df, ref double fase, ref double magnitud, double[] sumaR, double[] sumaI)
    {
        var alcance = 4.0;
        var paso = Math.Max(0.15, 0.5 / tramas);
        var mejor = double.NegativeInfinity;
        double mejorDf = df, mejorFase = fase, mejorMagnitud = magnitud;
        var centro = df;

        for (var d = -alcance; d <= alcance + 1e-9; d += paso)
        {
            var hipotesis = centro + d;
            var (re, im) = SincronismoCoherente(f, qr, qi, n0, tramas, hipotesis);
            var faseH = Math.Atan2(im, re);
            PromediarTramas(f, qr, qi, n0, tramas, hipotesis, sumaR, sumaI);
            var c = Math.Cos(-faseH);
            var s = Math.Sin(-faseH);
            var metrica = 0.0;
            for (var bit = 0; bit < f.BitsPorTrama; bit++)
            {
                var x = (sumaR[bit] * c) - (sumaI[bit] * s);
                var y = (sumaR[bit] * s) + (sumaI[bit] * c);
                metrica += Math.Abs((bit & 1) != 0 ? x : y);
            }
            if (metrica <= mejor) continue;
            mejor = metrica;
            mejorDf = hipotesis;
            mejorFase = faseH;
            mejorMagnitud = Math.Sqrt((re * re) + (im * im)) / tramas;
        }

        df = mejorDf;
        fase = mejorFase;
        magnitud = mejorMagnitud;
    }

    /// <summary>Suma en fase los numeros de cada bit a lo largo de varias tramas.</summary>
    private static void PromediarTramas(Formato f, float[] qr, float[] qi, int n0, int tramas, double df, double[] sumaR, double[] sumaI)
    {
        Array.Clear(sumaR);
        Array.Clear(sumaI);
        var giroPorMuestra = -2 * Math.PI * df / Fs;
        for (var k = 0; k < tramas; k++)
        {
            var base_ = n0 + (k * f.MuestrasPorTrama);
            for (var bit = 0; bit < f.BitsPorTrama; bit++)
            {
                var j = base_ + (bit * Mpb);
                var angulo = giroPorMuestra * (j - n0);
                var c = Math.Cos(angulo);
                var s = Math.Sin(angulo);
                sumaR[bit] += (qr[j] * c) - (qi[j] * s);
                sumaI[bit] += (qr[j] * s) + (qi[j] * c);
            }
        }
        if (tramas > 1)
            for (var bit = 0; bit < f.BitsPorTrama; bit++)
            {
                sumaR[bit] /= tramas;
                sumaI[bit] /= tramas;
            }
    }

    /// <summary>
    /// Pasa de los numeros de cada bit a una confianza por bit de la palabra de codigo.
    /// </summary>
    /// <remarks>
    /// Se gira cada numero para quitarle la fase de la portadora y se lee el canal que le toca:
    /// parte real en los bits impares, imaginaria en los pares. Positivo es un uno. La confianza
    /// es la de una senal binaria en ruido gaussiano —el doble de la amplitud por el valor leido
    /// partido por la varianza— con el signo cambiado, porque en este modem positivo quiere
    /// decir cero.
    /// </remarks>
    private static void Demodular(Formato f, double[] sumaR, double[] sumaI, double fase, double amplitud, double varianza, float[] confianzas)
    {
        var c = Math.Cos(-fase);
        var s = Math.Sin(-fase);
        var escala = varianza > 0 ? 2 * amplitud / varianza : 0;
        for (var i = 0; i < f.BitsDeDatos.Length; i++)
        {
            var bit = f.BitsDeDatos[i];
            var x = (sumaR[bit] * c) - (sumaI[bit] * s);
            var y = (sumaR[bit] * s) + (sumaI[bit] * c);
            var valor = (bit & 1) != 0 ? x : y;
            confianzas[i] = (float)(-valor * escala);
        }
    }

    /// <summary>
    /// Relacion senal-ruido en 2500 Hz a partir de la amplitud a la salida del filtro adaptado.
    /// </summary>
    /// <remarks>
    /// La amplitud del pulso en banda base es la mitad de la de audio, y el filtro adaptado la
    /// multiplica por la energia del pulso (seis, con once muestras de coseno); el ruido blanco
    /// del audio sale del filtro multiplicado por esa misma energia. Despejando, la relacion en
    /// los 2500 Hz de referencia es 0,8 veces la amplitud al cuadrado partido por la potencia
    /// del ruido filtrado.
    /// </remarks>
    private int Informe(double amplitud, double ruido)
    {
        if (amplitud <= 0 || ruido <= 0) return -30;
        var db = (10 * Math.Log10(0.8 * amplitud * amplitud / ruido)) + CorreccionDelInforme;
        return (int)Math.Round(Math.Clamp(db, -30, 50));
    }

    /// <summary>
    /// Quita las repeticiones: el mismo mensaje sacado de tramas consecutivas de un mismo ping
    /// es una sola decodificacion, con el instante del primer acierto y el mejor informe.
    /// </summary>
    private List<DecodificacionPropia> Depurar(List<Decodificacion> brutas, DateTimeOffset ventanaUtc, double segundosDelPrimerMuestreo)
    {
        brutas.Sort(static (a, b) => a.Segundo.CompareTo(b.Segundo));
        var grupos = new List<(Decodificacion Primera, Decodificacion Mejor, double Ultimo, int Cuantas, double Coherente)>();
        foreach (var d in brutas)
        {
            var encontrado = -1;
            for (var g = 0; g < grupos.Count; g++)
            {
                var (primera, _, ultimo, _, _) = grupos[g];
                if (primera.Texto == d.Texto && d.Segundo - ultimo < 1.0) { encontrado = g; break; }
            }
            if (encontrado < 0) { grupos.Add((d, d, d.Segundo, 1, d.Coherente)); continue; }
            var grupo = grupos[encontrado];
            grupos[encontrado] = (grupo.Primera, d.Decibelios > grupo.Mejor.Decibelios ? d : grupo.Mejor, d.Segundo, grupo.Cuantas + 1, Math.Max(grupo.Coherente, d.Coherente));
        }

        var salida = new List<DecodificacionPropia>(grupos.Count);
        foreach (var (primera, mejor, _, cuantas, coherente) in grupos)
        {
            // Un mensaje corto solo vale con sincronismo sobrado o confirmado por duplicado.
            if (primera.EsCorto && cuantas < 2 && coherente < SincronismoCoherenteSeguroCorto) continue;
            var m = primera.Mensaje;
            salida.Add(new DecodificacionPropia(
                primera.Texto,
                (int)Math.Round(mejor.Decibelios),
                Math.Round(primera.Segundo + segundosDelPrimerMuestreo, 2),
                (int)Math.Round(mejor.Hz),
                ModoDelModem.Msk144,
                ventanaUtc)
            {
                Llamante = m?.Llamante ?? default,
                Llamado = m?.Llamado ?? default,
                Locator = m?.Locator ?? default,
                EsCq = m?.EsCq ?? false,
            });
        }
        return salida;
    }
}
