using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Modos.Ft8;
using Nodisla.Cuaderno.Modos.Ldpc;
using Nodisla.Cuaderno.Modos.Senal;
using Nodisla.Cuaderno.Modos.Wspr;

namespace Nodisla.Cuaderno.Modos.Fst4;

/// <summary>Como fue la decodificacion de una ventana de FST4, con el detalle para medir.</summary>
/// <param name="Decodificaciones">Lo que se saco.</param>
/// <param name="Candidatas">Cuantos sitios se miraron de cerca.</param>
/// <param name="PalabrasValidas">Veces que el corrector devolvio una palabra que cuadraba.</param>
/// <param name="RechazadasPorElCrc">Palabras validas que el CRC de 24 bits tiro.</param>
/// <param name="Duracion">Lo que costo.</param>
public sealed record ResultadoFst4(
    IReadOnlyList<DecodificacionPropia> Decodificaciones,
    int Candidatas,
    int PalabrasValidas,
    int RechazadasPorElCrc,
    TimeSpan Duracion)
{
    /// <summary>Decodificaciones que salieron por la recuperacion profunda.</summary>
    public int PorRecuperacionProfunda { get; init; }
}

/// <summary>
/// Decodifica una ventana de FST4 o FST4W de cualquier periodo.
/// </summary>
/// <remarks>
/// <para>
/// El camino es el de FT8 con simbolos mucho mas largos: se remuestrea el audio a una
/// frecuencia en la que el simbolo cae en una potencia de dos, se calcula un espectrograma con
/// dos bloques por simbolo y casillas de medio tono, y se buscan en el los cinco grupos de
/// sincronismo.
/// </para>
/// <para>
/// <b>Cada candidata se mira en banda base.</b> Se hace una sola transformada de la ventana
/// entera y, para cada candidata, se recortan las casillas de alrededor y se vuelve al tiempo
/// con 32 muestras por simbolo. Ahi se afina la frecuencia hasta 1/256 del espaciado y el
/// comienzo hasta 1/32 de simbolo con el sincronismo sumado de forma coherente, y se correlan
/// los 160 simbolos con los cuatro tonos con una sola referencia de fase para toda la trama.
/// </para>
/// <para>
/// <b>Por que coherente (30-09-2026).</b> Hasta entonces cada simbolo se miraba suelto, sin
/// fase (4-FSK no coherente), y el banco quedaba cuatro o cinco decibelios por debajo de los
/// umbrales publicados sin ni un rechazo del CRC: el sincronismo encontraba la senal, pero a
/// −19 dB en FST4-15 llegaban al corrector 58 de 240 bits mal <i>incluso con la alineacion
/// exacta</i>, que es lo que da la teoria para ese detector. FST4 lleva la fase continua y los
/// tonos separados un baudio, asi que se pueden sumar bloques de 2, 4 y 8 simbolos como
/// complejos, igual que hace WSJT-X; con ocho bajan a unos 28 y el corrector cuaja. Eso
/// obliga a conocer la frecuencia con un error de un uno por ciento del espaciado, que es lo
/// que pide el afinado fino de arriba.
/// </para>
/// <para>
/// <b>Ancho de busqueda.</b> En los periodos de 15 a 120 segundos se busca en toda la banda de
/// audio (100 a 3000 Hz), como en FT8. En los de 300 en adelante, y en FST4W, solo en 1500 ±
/// 100 Hz: son modos de baliza y de banda baja en los que todo el mundo se pone en el mismo
/// sitio, y buscar 3000 Hz con tonos de una decima de hercio seria millones de candidatas para
/// nada. Se puede cambiar con <see cref="FrecuenciaMinima"/> y <see cref="FrecuenciaMaxima"/>.
/// </para>
/// <para>
/// <b>No inventar.</b> El CRC de 24 bits deja una entre diecisiete millones a cada palabra de
/// ruido, asi que aqui la recuperacion profunda puede trabajar con menos frenos que en FT8;
/// aun asi se le exige sincronismo de verdad, y la palabra reconstruida tiene que parecerse a
/// lo que oyo el demodulador.
/// </para>
/// </remarks>
public sealed class DecodificadorFst4
{
    private readonly ParametrosFst4 _p;
    private readonly TablaLdpc _tabla;
    private readonly bool _esFst4w;
    private readonly DecodificadorDeCreencia _corrector;
    private readonly RecuperacionProfunda _profunda;
    private readonly ILogger _registro;

    /// <summary>Crea el decodificador para un periodo.</summary>
    /// <param name="periodoSegundos">Uno de los siete periodos del protocolo.</param>
    /// <param name="tabla">La tabla (240,101) de FST4 o la (240,74) de FST4W.</param>
    /// <param name="esFst4w">Cierto para la variante baliza.</param>
    /// <param name="registro">Para dejar constancia.</param>
    public DecodificadorFst4(int periodoSegundos, TablaLdpc tabla, bool esFst4w, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(tabla);
        _p = ParametrosFst4.DelPeriodo(periodoSegundos);
        if (esFst4w && periodoSegundos < 120)
            throw new ArgumentOutOfRangeException(nameof(periodoSegundos), periodoSegundos, "FST4W solo existe en los periodos de 120 segundos en adelante.");
        var bits = esFst4w ? ParametrosFst4.BitsConCrcFst4w : ParametrosFst4.BitsConCrcFst4;
        if (tabla.Codigo.Longitud != ParametrosFst4.BitsDePalabra || tabla.Codigo.BitsDeMensaje != bits)
            throw new ArgumentException($"La tabla no es un código (240,{bits}).", nameof(tabla));
        _tabla = tabla;
        _esFst4w = esFst4w;
        _corrector = new DecodificadorDeCreencia(tabla.Codigo);
        _profunda = new RecuperacionProfunda(tabla.Codigo)
        {
            // El freno de FT8 es 26 de 174 bits: la misma fraccion sobre 240.
            ErroresDurosMaximos = 36,
        };
        _registro = registro ?? NullLogger.Instance;
        FrecuenciaMinima = _p.FrecuenciaMinimaPorOmision;
        FrecuenciaMaxima = _p.FrecuenciaMaximaPorOmision;
    }

    /// <summary>Parametros del periodo.</summary>
    public ParametrosFst4 Parametros => _p;

    /// <summary>Tabla del codigo.</summary>
    public TablaLdpc Tabla => _tabla;

    /// <summary>Es la variante baliza.</summary>
    public bool EsFst4w => _esFst4w;

    /// <summary>Frecuencia mas baja del tono cero que se busca.</summary>
    public double FrecuenciaMinima { get; set; }

    /// <summary>Frecuencia mas alta del tono cero que se busca.</summary>
    public double FrecuenciaMaxima { get; set; }

    /// <summary>Candidatas que se miran de cerca como mucho.</summary>
    public int CandidatasPorVentana { get; set; } = 60;

    /// <summary>Vueltas del corrector.</summary>
    public int VueltasDelCorrector { get; set; } = 80;

    /// <summary>Se intenta la recuperacion profunda cuando la propagacion de creencias no cuaja.</summary>
    public bool UsarRecuperacionProfunda { get; set; } = true;

    /// <summary>Sincronismo minimo para intentar la recuperacion profunda.</summary>
    public double SincronismoMinimoParaLaProfunda { get; set; } = 1.6;

    /// <summary>Ajuste del informe de senal, en decibelios, medido en el banco.</summary>
    public double CorreccionDelInforme { get; set; } = 0;

    /// <summary>La recuperacion profunda, para medirla aparte.</summary>
    public RecuperacionProfunda Profunda => _profunda;

    /// <summary>El corrector, para ajustarlo desde el banco.</summary>
    public DecodificadorDeCreencia Corrector => _corrector;

    /// <summary>
    /// Decodifica una ventana de audio.
    /// </summary>
    /// <param name="audio">Muestras de la ventana, de -1 a 1, un canal.</param>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del audio.</param>
    /// <param name="ventanaUtc">Momento en que empieza la ventana.</param>
    /// <param name="catalogo">Catalogo de indicativos para resolver los resumidos.</param>
    /// <param name="segundosDelPrimerMuestreo">Instante de la primera muestra respecto al comienzo de la ventana.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public ResultadoFst4 Decodificar(
        ReadOnlySpan<float> audio,
        int frecuenciaDeMuestreo,
        DateTimeOffset ventanaUtc,
        CatalogoDeIndicativos catalogo,
        double segundosDelPrimerMuestreo = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(catalogo);
        var reloj = Stopwatch.StartNew();
        var fs = _p.FrecuenciaDeAnalisis;
        var muestras = Math.Abs(frecuenciaDeMuestreo - fs) < 1e-6
            ? audio.ToArray()
            : Remuestreador.Remuestrear(audio, frecuenciaDeMuestreo, fs);

        var nsps = _p.MuestrasPorSimboloDeAnalisis;
        if (muestras.Length < _p.MuestrasDeLaSenal)
            return new ResultadoFst4([], 0, 0, 0, reloj.Elapsed);

        var espectro = Espectrograma.Calcular(muestras, nsps, fs, FrecuenciaMinima, FrecuenciaMaxima + (2 * ParametrosFst4.Tonos * _p.EspaciadoDeTonosHz), ct);
        var candidatas = BuscarCandidatas(espectro);

        var todo = EspectroCompleto.Calcular(muestras, fs, ct);
        var banda = new BandaBase(MuestrasPorSimboloEnBase * (todo.Longitud / nsps));

        var salida = new List<DecodificacionPropia>();
        var vistas = new List<(string Texto, double Tono)>();
        var confianzas = new float[ParametrosFst4.BitsDePalabra];
        var palabra = new byte[ParametrosFst4.BitsDePalabra];
        var energias = new double[ParametrosFst4.SimbolosTotales * ParametrosFst4.Tonos];
        var simbolos = new Complejos(ParametrosFst4.SimbolosTotales * ParametrosFst4.Tonos);
        var trabajo = new TrabajoDeMetricas();
        int palabrasValidas = 0, rechazadas = 0, profundas = 0;

        foreach (var candidata in candidatas)
        {
            ct.ThrowIfCancellationRequested();
            var medida = Afinar(todo, espectro, candidata, banda, simbolos, energias);
            var (senal, ruido) = MedirNiveles(energias);

            // Una pasada del corrector por cada longitud de bloque coherente.
            var salio = false;
            foreach (var nsym in BloquesCoherentes)
            {
                CalcularConfianzas(simbolos, nsym, confianzas, trabajo);
                var okBp = _corrector.TryDecodificar(confianzas, palabra, VueltasDelCorrector);
                if (!okBp) continue;
                palabrasValidas++;
                if (Anotar(palabra, profunda: false)) { salio = true; break; }
                rechazadas++;
            }
            if (salio || !UsarRecuperacionProfunda || medida.Sincronismo < SincronismoMinimoParaLaProfunda) continue;

            foreach (var nsym in BloquesCoherentes)
            {
                CalcularConfianzas(simbolos, nsym, confianzas, trabajo);
                if (!_profunda.TryRecuperar(confianzas, palabra)) continue;
                var cuantos = _profunda.CandidatosEncontrados;
                for (var c = 0; c < cuantos && !salio; c++)
                {
                    if (c > 0) _profunda.CopiarCandidato(c, palabra);
                    palabrasValidas++;
                    if (Anotar(palabra, profunda: true)) salio = true;
                    else rechazadas++;
                }
                if (salio) break;
            }

            // Cierto si la palabra paso el CRC y se interpreto, aunque ya estuviera apuntada.
            bool Anotar(byte[] bits, bool profunda)
            {
                if (!TryInterpretar(bits, catalogo, out var texto, out var mensaje, out var baliza)) return false;
                var tonoHz = medida.TonoBaseHz;
                if (YaEstaba(vistas, texto, tonoHz)) return true;
                vistas.Add((texto, tonoHz));
                if (profunda) profundas++;
                salida.Add(new DecodificacionPropia(
                    texto,
                    Informe(senal, Math.Min(ruido, espectro.Ruido / Math.Log(2))),
                    Math.Round(medida.ComienzoEnSegundos + segundosDelPrimerMuestreo - _p.ComienzoNominalSegundos, 2),
                    (int)Math.Round(tonoHz),
                    _esFst4w ? ModoDelModem.Fst4w : ModoDelModem.Fst4,
                    ventanaUtc)
                {
                    Llamante = mensaje?.Llamante ?? (baliza is not null && baliza.IndicativoConocido ? Indicativo(baliza.Indicativo) : default),
                    Llamado = mensaje?.Llamado ?? default,
                    Locator = mensaje?.Locator ?? (baliza is not null && baliza.Localizador.Length > 0 ? Localizador(baliza.Localizador) : default),
                    EsCq = mensaje?.EsCq ?? false,
                    EsRecuperacionProfunda = profunda,
                });
                return true;
            }
        }

        salida.Sort(static (x, y) => x.TonoHz.CompareTo(y.TonoHz));
        _registro.LogDebug(
            "FST4{W}-{Periodo} {Ventana}: {Candidatas} candidatas, {Validas} válidas, {Rechazadas} rechazadas, {Salieron} decodificaciones en {Ms} ms.",
            _esFst4w ? "W" : string.Empty, _p.PeriodoSegundos, ventanaUtc, candidatas.Count, palabrasValidas, rechazadas, salida.Count, reloj.ElapsedMilliseconds);

        return new ResultadoFst4(salida, candidatas.Count, palabrasValidas, rechazadas, reloj.Elapsed) { PorRecuperacionProfunda = profundas };
    }

    private static Dominio.Valores.Indicativo Indicativo(string texto) => Dominio.Valores.Indicativo.Crudo(texto);

    private static Dominio.Valores.Locator Localizador(string texto) =>
        Dominio.Valores.Locator.TryParse(texto, out var l) ? l : Dominio.Valores.Locator.Vacio;

    /// <summary>Desmezcla, comprueba el CRC y desempaqueta el mensaje que toque.</summary>
    private bool TryInterpretar(byte[] palabra, CatalogoDeIndicativos catalogo, out string texto, out MensajeDescifrado? mensaje, out MensajeWspr? baliza)
    {
        texto = string.Empty;
        mensaje = null;
        baliza = null;
        var bits = _tabla.Codigo.BitsDeMensaje;
        var conCrc = palabra.AsSpan(0, bits);
        if (!Crc24.EsValido(conCrc)) return false;

        if (_esFst4w)
        {
            if (!MensajeWspr.TryDesempaquetar(conCrc[..MensajeWspr.Bits], null, out var m)) return false;
            if (m.Texto.Length == 0) return false;
            baliza = m;
            texto = m.Texto;
            return true;
        }

        Span<byte> bits77 = stackalloc byte[MensajeDe77Bits.Bits];
        conCrc[..MensajeDe77Bits.Bits].CopyTo(bits77);
        CodificadorFst4.AplicarMezcla(bits77);
        if (!MensajeDe77Bits.TryDesempaquetar(bits77, catalogo, out var descifrado)) return false;
        if (descifrado.Texto.Length == 0) return false;
        mensaje = descifrado;
        texto = descifrado.Texto;
        return true;
    }

    // ----------------------------------------------------------------------------------------
    // Espectrograma y busqueda gruesa
    // ----------------------------------------------------------------------------------------

    /// <summary>Potencia por bloque de medio simbolo y casilla de medio tono, en la banda que se busca.</summary>
    private sealed class Espectrograma
    {
        public float[] Potencia = [];
        public int Bloques;
        public int Casillas;
        public int PrimeraCasilla; // casilla absoluta (de la transformada de 2·nsps) de la columna 0
        public double HzPorCasilla;
        public int Paso;
        public double Ruido;

        public float De(int bloque, int casilla) =>
            bloque < 0 || bloque >= Bloques || casilla < 0 || casilla >= Casillas ? 0 : Potencia[(bloque * Casillas) + casilla];

        public static Espectrograma Calcular(float[] muestras, int nsps, double fs, double fMin, double fMax, CancellationToken ct)
        {
            var longitudFft = 2 * nsps;
            var hzPorCasilla = fs / longitudFft;
            var primera = Math.Max(0, (int)Math.Floor(fMin / hzPorCasilla));
            var ultima = Math.Min(nsps, (int)Math.Ceiling(fMax / hzPorCasilla));
            var casillas = Math.Max(1, ultima - primera + 1);
            var paso = nsps / 2;
            var bloques = ((muestras.Length - nsps) / paso) + 1;

            var potencia = new float[bloques * casillas];
            var real = new float[longitudFft];
            var imaginaria = new float[longitudFft];
            for (var b = 0; b < bloques; b++)
            {
                if ((b & 15) == 0) ct.ThrowIfCancellationRequested();
                Array.Clear(real);
                Array.Clear(imaginaria);
                muestras.AsSpan(b * paso, nsps).CopyTo(real);
                Fft.Transformar(real, imaginaria);
                for (var c = 0; c < casillas; c++)
                {
                    var k = primera + c;
                    potencia[(b * casillas) + c] = (real[k] * real[k]) + (imaginaria[k] * imaginaria[k]);
                }
            }

            return new Espectrograma
            {
                Potencia = potencia,
                Bloques = bloques,
                Casillas = casillas,
                PrimeraCasilla = primera,
                HzPorCasilla = hzPorCasilla,
                Paso = paso,
                Ruido = Mediana(potencia),
            };
        }

        private static double Mediana(float[] valores)
        {
            if (valores.Length == 0) return 0;
            var paso = Math.Max(1, valores.Length / 20000);
            var lista = new List<float>((valores.Length / paso) + 1);
            for (var i = 0; i < valores.Length; i += paso) lista.Add(valores[i]);
            lista.Sort();
            return lista[lista.Count / 2];
        }
    }

    private readonly record struct Candidata(int Bloque, int Casilla, double Puntuacion);

    private readonly record struct Medida(double Sincronismo, double TonoBaseHz, double ComienzoEnSegundos);

    private List<Candidata> BuscarCandidatas(Espectrograma e)
    {
        const int CasillasPorTono = 2;
        var ultimaCasilla = e.Casillas - (CasillasPorTono * (ParametrosFst4.Tonos - 1)) - 1;
        var ultimoBloque = e.Bloques - (2 * ParametrosFst4.SimbolosTotales);
        var fMaxCasilla = (int)Math.Round(FrecuenciaMaxima / e.HzPorCasilla) - e.PrimeraCasilla;
        ultimaCasilla = Math.Min(ultimaCasilla, fMaxCasilla);
        if (ultimoBloque < 0 || ultimaCasilla < 0) return [];

        var ancho = ultimaCasilla + 1;
        var alto = ultimoBloque + 1;
        var puntuaciones = new double[alto * ancho];
        var posiciones = ParametrosFst4.PosicionesDeSincronismo;

        for (var b = 0; b <= ultimoBloque; b++)
            for (var c = 0; c <= ultimaCasilla; c++)
            {
                double enElPatron = 0, enTodos = 0;
                for (var g = 0; g < posiciones.Length; g++)
                {
                    var patron = (g & 1) == 0 ? ParametrosFst4.SincronismoUno : ParametrosFst4.SincronismoDos;
                    for (var k = 0; k < patron.Length; k++)
                    {
                        var bloque = b + (2 * (posiciones[g] + k));
                        enElPatron += e.De(bloque, c + (CasillasPorTono * patron[k]));
                        for (var t = 0; t < ParametrosFst4.Tonos; t++)
                            enTodos += e.De(bloque, c + (CasillasPorTono * t));
                    }
                }
                puntuaciones[(b * ancho) + c] = enTodos <= 0 ? 0 : enElPatron * ParametrosFst4.Tonos / enTodos;
            }

        var candidatas = new List<Candidata>();
        for (var b = 0; b <= ultimoBloque; b++)
            for (var c = 0; c <= ultimaCasilla; c++)
            {
                var v = puntuaciones[(b * ancho) + c];
                if (v <= 1.0) continue;
                var esMaximo = true;
                for (var db = -1; db <= 1 && esMaximo; db++)
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        if (db == 0 && dc == 0) continue;
                        var bb = b + db;
                        var cc = c + dc;
                        if (bb < 0 || bb >= alto || cc < 0 || cc >= ancho) continue;
                        if (puntuaciones[(bb * ancho) + cc] > v) { esMaximo = false; break; }
                    }
                if (esMaximo) candidatas.Add(new Candidata(b, c, v));
            }
        candidatas.Sort(static (x, y) => y.Puntuacion.CompareTo(x.Puntuacion));
        if (candidatas.Count > CandidatasPorVentana) candidatas.RemoveRange(CandidatasPorVentana, candidatas.Count - CandidatasPorVentana);
        return candidatas;
    }

    // ----------------------------------------------------------------------------------------
    // Medida fina de una candidata, en banda base
    // ----------------------------------------------------------------------------------------

    /// <summary>Muestras por simbolo de la banda base en que se mira cada candidata.</summary>
    private const int MuestrasPorSimboloEnBase = 32;

    /// <summary>
    /// Longitudes, en simbolos, de los bloques que se demodulan de forma coherente, una pasada
    /// del corrector por cada una y en este orden.
    /// </summary>
    /// <remarks>
    /// <para>
    /// FST4 lleva la fase continua y los tonos separados justo un baudio, asi que la fase de la
    /// portadora no salta de un simbolo al siguiente: si se miden todos con la misma referencia
    /// de fase, varios simbolos seguidos se pueden sumar como numeros complejos antes de mirar
    /// cuanto se parecen a cada combinacion de tonos. Eso es lo que separa la deteccion de un
    /// simbolo suelto (4-FSK no coherente) de la que usa WSJT-X, y medido en el banco son unos
    /// tres decibelios.
    /// </para>
    /// <para>
    /// Con uno se aguanta cualquier canal; con ocho se saca todo lo que da el modo cuando la
    /// fase aguanta ocho simbolos. Se prueban todos porque en el aire no se sabe de antemano.
    /// </para>
    /// </remarks>
    public int[] BloquesCoherentes { get; set; } = [1, 2, 4, 8];

    /// <summary>Transformada de la ventana entera, de la que se recorta la banda base de cada candidata.</summary>
    private sealed class EspectroCompleto
    {
        public float[] Re = [];
        public float[] Im = [];
        public int Longitud;
        public double HzPorCasilla;

        public static EspectroCompleto Calcular(float[] muestras, double fs, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var n = Fft.PotenciaDeDosQueCubre(muestras.Length);
            var re = new float[n];
            var im = new float[n];
            muestras.AsSpan().CopyTo(re);
            Fft.Transformar(re, im);
            return new EspectroCompleto { Re = re, Im = im, Longitud = n, HzPorCasilla = fs / n };
        }
    }

    /// <summary>La senal compleja alrededor de una candidata, a pocas muestras por simbolo.</summary>
    private sealed class BandaBase(int muestras)
    {
        public readonly float[] Re = new float[muestras];
        public readonly float[] Im = new float[muestras];
        public int Muestras => Re.Length;

        /// <summary>Frecuencia real, en hercios, que ha quedado en el cero de la banda base.</summary>
        public double FrecuenciaCero;

        /// <summary>
        /// Recorta las casillas alrededor de <paramref name="centroHz"/> y vuelve al tiempo. La
        /// escala queda como la de correlar el audio original con un tono: un simbolo aqui mide
        /// lo mismo que un simbolo alli, en senal y en ruido.
        /// </summary>
        public void Extraer(EspectroCompleto e, double centroHz)
        {
            var m = Muestras;
            var kc = (int)Math.Round(centroHz / e.HzPorCasilla);
            Array.Clear(Re);
            Array.Clear(Im);
            for (var i = -m / 2; i < m / 2; i++)
            {
                var k = kc + i;
                if (k <= 0 || k >= e.Longitud / 2) continue;
                var d = i < 0 ? i + m : i;
                Re[d] = e.Re[k];
                Im[d] = e.Im[k];
            }
            Fft.TransformarInversa(Re, Im);
            FrecuenciaCero = kc * e.HzPorCasilla;
        }
    }

    /// <summary>Numeros complejos en dos vectores.</summary>
    private sealed class Complejos(int cuantos)
    {
        public readonly double[] Re = new double[cuantos];
        public readonly double[] Im = new double[cuantos];
    }

    /// <summary>Memoria de trabajo de las confianzas, para no recolectar basura por candidata.</summary>
    private sealed class TrabajoDeMetricas
    {
        public readonly double[] Metricas = new double[2 * ParametrosFst4.SimbolosTotales];
        public readonly double[] ARe = new double[256];
        public readonly double[] AIm = new double[256];
        public readonly bool[] AValida = new bool[256];
        public readonly double[] BRe = new double[256];
        public readonly double[] BIm = new double[256];
        public readonly bool[] BValida = new bool[256];
        public readonly double[] MaxA = new double[256];
        public readonly double[] MaxB = new double[256];
    }

    private Medida Afinar(EspectroCompleto todo, Espectrograma espectro, Candidata candidata, BandaBase banda, Complejos simbolos, double[] energias)
    {
        var fs = _p.FrecuenciaDeAnalisis;
        var nsps = _p.MuestrasPorSimboloDeAnalisis;
        const int D = MuestrasPorSimboloEnBase;
        var delta = _p.EspaciadoDeTonosHz;
        var tasa = D * delta;

        var f0 = (candidata.Casilla + espectro.PrimeraCasilla) * espectro.HzPorCasilla;
        banda.Extraer(todo, f0 + (1.5 * delta));
        var fb0 = f0 - banda.FrecuenciaCero;
        var m0 = (int)Math.Round((double)candidata.Bloque * espectro.Paso * D / nsps);

        // La rejilla gruesa tiene medio simbolo y medio tono: se barre algo mas de un cuarto a
        // cada lado, primero a trazo grueso y luego fino, con el sincronismo coherente.
        var mejor = double.NegativeInfinity;
        var mejorF = fb0;
        var mejorM = m0;
        for (var dm = -12; dm <= 12; dm += 2)
            for (var k = -10; k <= 10; k++)
            {
                var f = fb0 + (k * delta / 32);
                var puntos = SincronismoCoherente(banda, m0 + dm, f, tasa, delta);
                if (puntos <= mejor) continue;
                mejor = puntos;
                mejorF = f;
                mejorM = m0 + dm;
            }

        var baseF = mejorF;
        var baseM = mejorM;
        for (var dm = -1; dm <= 1; dm++)
            for (var k = -8; k <= 8; k++)
            {
                var f = baseF + (k * delta / 256);
                var puntos = SincronismoCoherente(banda, baseM + dm, f, tasa, delta);
                if (puntos <= mejor) continue;
                mejor = puntos;
                mejorF = f;
                mejorM = baseM + dm;
            }

        for (var s = 0; s < ParametrosFst4.SimbolosTotales; s++)
            for (var t = 0; t < ParametrosFst4.Tonos; t++)
            {
                var i = (s * ParametrosFst4.Tonos) + t;
                Correlar(banda, mejorM, s, mejorF + (t * delta), tasa, out simbolos.Re[i], out simbolos.Im[i]);
                energias[i] = (simbolos.Re[i] * simbolos.Re[i]) + (simbolos.Im[i] * simbolos.Im[i]);
            }

        return new Medida(SincronismoNoCoherente(energias), banda.FrecuenciaCero + mejorF, (double)mejorM * nsps / D / fs);
    }

    /// <summary>
    /// Correlacion de un simbolo con un tono, con la fase de referencia contada desde el
    /// comienzo de la trama: asi todos los simbolos quedan medidos con la misma referencia.
    /// </summary>
    private static void Correlar(BandaBase b, int comienzo, int simbolo, double tonoHz, double tasa, out double re, out double im)
    {
        const int D = MuestrasPorSimboloEnBase;
        var w = 2 * Math.PI * tonoHz / tasa;
        var desde = simbolo * D;
        var angulo = -w * desde;
        double cr = Math.Cos(angulo), ci = Math.Sin(angulo);
        double pr = Math.Cos(w), pi = -Math.Sin(w);
        double sr = 0, si = 0;
        var n = comienzo + desde;
        for (var j = 0; j < D; j++, n++)
        {
            if ((uint)n < (uint)b.Muestras)
            {
                double xr = b.Re[n], xi = b.Im[n];
                sr += (xr * cr) - (xi * ci);
                si += (xr * ci) + (xi * cr);
            }
            var nr = (cr * pr) - (ci * pi);
            ci = (cr * pi) + (ci * pr);
            cr = nr;
        }
        re = sr;
        im = si;
    }

    /// <summary>Suma de la potencia de cada grupo de sincronismo, sumado de forma coherente.</summary>
    private static double SincronismoCoherente(BandaBase b, int comienzo, double tonoBaseHz, double tasa, double delta)
    {
        var posiciones = ParametrosFst4.PosicionesDeSincronismo;
        double total = 0;
        for (var g = 0; g < posiciones.Length; g++)
        {
            var patron = (g & 1) == 0 ? ParametrosFst4.SincronismoUno : ParametrosFst4.SincronismoDos;
            double zr = 0, zi = 0;
            for (var k = 0; k < patron.Length; k++)
            {
                Correlar(b, comienzo, posiciones[g] + k, tonoBaseHz + (patron[k] * delta), tasa, out var r, out var i);
                zr += r;
                zi += i;
            }
            total += (zr * zr) + (zi * zi);
        }
        return total;
    }

    /// <summary>Energia en los tonos del sincronismo frente a la de todos, por cuatro: uno es ruido.</summary>
    private static double SincronismoNoCoherente(double[] energias)
    {
        double enElPatron = 0, enTodos = 0;
        for (var s = 0; s < ParametrosFst4.SimbolosTotales; s++)
        {
            if (!ParametrosFst4.EsSimboloDeSincronismo(s, out var bueno)) continue;
            for (var t = 0; t < ParametrosFst4.Tonos; t++)
            {
                var e = energias[(s * ParametrosFst4.Tonos) + t];
                enTodos += e;
                if (t == bueno) enElPatron += e;
            }
        }
        return enTodos <= 0 ? 0 : enElPatron * ParametrosFst4.Tonos / enTodos;
    }

    /// <summary>
    /// Confianza de cada bit mirando bloques de <paramref name="nsym"/> simbolos seguidos de
    /// forma coherente.
    /// </summary>
    /// <remarks>
    /// Para cada bloque se prueban todas las combinaciones de tonos (las que respetan el
    /// sincronismo, si el bloque pisa alguno) y se suma la correlacion de cada simbolo con su
    /// tono como numeros complejos. La confianza de un bit es la mejor combinacion con ese bit
    /// a cero menos la mejor con el a uno, y luego todo se lleva a desviacion uno y se escala
    /// como en WSJT-X (2,83). Los bloques de ocho se parten en dos mitades de cuatro para no
    /// tener que sumar ocho terminos en cada una de las 65 536 combinaciones.
    /// </remarks>
    private void CalcularConfianzas(Complejos simbolos, int nsym, float[] confianzas, TrabajoDeMetricas w)
    {
        var bm = w.Metricas;
        var mitad = Math.Min(nsym, 4);
        var dosMitades = nsym > 4;
        var combinaciones = 1 << (2 * mitad);

        for (var ks = 0; ks + nsym <= ParametrosFst4.SimbolosTotales; ks += nsym)
        {
            Enumerar(simbolos, ks, mitad, w.ARe, w.AIm, w.AValida);
            if (!dosMitades)
            {
                for (var i = 0; i < combinaciones; i++)
                    w.MaxA[i] = w.AValida[i] ? Math.Sqrt((w.ARe[i] * w.ARe[i]) + (w.AIm[i] * w.AIm[i])) : -1;
                MetricasDeMitad(w.MaxA, combinaciones, mitad, bm, 2 * ks);
                continue;
            }

            Enumerar(simbolos, ks + mitad, mitad, w.BRe, w.BIm, w.BValida);
            Array.Fill(w.MaxA, -1);
            Array.Fill(w.MaxB, -1);
            for (var a = 0; a < combinaciones; a++)
            {
                if (!w.AValida[a]) continue;
                double ar = w.ARe[a], ai = w.AIm[a];
                var maxA = w.MaxA[a];
                for (var b = 0; b < combinaciones; b++)
                {
                    if (!w.BValida[b]) continue;
                    var r = ar + w.BRe[b];
                    var i = ai + w.BIm[b];
                    var p = (r * r) + (i * i);
                    if (p > maxA) maxA = p;
                    if (p > w.MaxB[b]) w.MaxB[b] = p;
                }
                w.MaxA[a] = maxA;
            }
            for (var i = 0; i < combinaciones; i++)
            {
                if (w.MaxA[i] >= 0) w.MaxA[i] = Math.Sqrt(w.MaxA[i]);
                if (w.MaxB[i] >= 0) w.MaxB[i] = Math.Sqrt(w.MaxB[i]);
            }
            MetricasDeMitad(w.MaxA, combinaciones, mitad, bm, 2 * ks);
            MetricasDeMitad(w.MaxB, combinaciones, mitad, bm, 2 * (ks + mitad));
        }

        var posiciones = _p.PosicionesDeDatos;
        double suma = 0, suma2 = 0;
        for (var s = 0; s < posiciones.Length; s++)
            for (var b = 0; b < ParametrosFst4.BitsPorSimbolo; b++)
            {
                var x = bm[(ParametrosFst4.BitsPorSimbolo * posiciones[s]) + b];
                suma += x;
                suma2 += x * x;
            }
        var n = (double)ParametrosFst4.BitsDePalabra;
        var varianza = (suma2 / n) - ((suma / n) * (suma / n));
        var escala = varianza > 0 ? 2.83 / Math.Sqrt(varianza) : 0;
        for (var s = 0; s < posiciones.Length; s++)
            for (var b = 0; b < ParametrosFst4.BitsPorSimbolo; b++)
                confianzas[(s * ParametrosFst4.BitsPorSimbolo) + b] = (float)(bm[(ParametrosFst4.BitsPorSimbolo * posiciones[s]) + b] * escala);
    }

    /// <summary>Suma coherente de las 4^n combinaciones de tonos de n simbolos seguidos.</summary>
    private static void Enumerar(Complejos simbolos, int desde, int n, double[] re, double[] im, bool[] valida)
    {
        var gray = ParametrosFst4.MapaDeGray;
        var combinaciones = 1 << (2 * n);
        for (var i = 0; i < combinaciones; i++)
        {
            double r = 0, m = 0;
            var ok = true;
            for (var j = 0; j < n; j++)
            {
                var s = desde + j;
                var tono = gray[(i >> (2 * (n - 1 - j))) & 3];
                if (ParametrosFst4.EsSimboloDeSincronismo(s, out var debido) && debido != tono) { ok = false; break; }
                var k = (s * ParametrosFst4.Tonos) + tono;
                r += simbolos.Re[k];
                m += simbolos.Im[k];
            }
            re[i] = r;
            im[i] = m;
            valida[i] = ok;
        }
    }

    /// <summary>Metrica de los bits de n simbolos a partir del mejor valor de cada combinacion.</summary>
    private static void MetricasDeMitad(double[] mejores, int combinaciones, int n, double[] bm, int primerBit)
    {
        var bits = 2 * n;
        for (var ib = 0; ib < bits; ib++)
        {
            var bit = bits - 1 - ib;
            double conCero = -1, conUno = -1;
            for (var i = 0; i < combinaciones; i++)
            {
                var v = mejores[i];
                if (v < 0) continue;
                if (((i >> bit) & 1) == 0) { if (v > conCero) conCero = v; }
                else if (v > conUno) conUno = v;
            }
            bm[primerBit + ib] = conCero < 0 || conUno < 0 ? 0 : conCero - conUno;
        }
    }

    private static (double Senal, double Ruido) MedirNiveles(double[] energias)
    {
        double conSenal = 0, sinSenal = 0;
        int cuantosCon = 0, cuantosSin = 0;
        for (var s = 0; s < ParametrosFst4.SimbolosTotales; s++)
        {
            if (!ParametrosFst4.EsSimboloDeSincronismo(s, out var bueno)) continue;
            for (var t = 0; t < ParametrosFst4.Tonos; t++)
            {
                var e = energias[(s * ParametrosFst4.Tonos) + t];
                if (t == bueno) { conSenal += e; cuantosCon++; }
                else { sinSenal += e; cuantosSin++; }
            }
        }
        var ruido = cuantosSin > 0 ? sinSenal / cuantosSin : 0;
        var senal = cuantosCon > 0 ? Math.Max(0, (conSenal / cuantosCon) - ruido) : 0;
        return (senal, ruido);
    }

    /// <summary>
    /// Informe en 2500 Hz.
    /// </summary>
    /// <remarks>
    /// El ruido se toma de la mediana del espectrograma (que en ruido gaussiano es la media por
    /// el logaritmo de dos) y no solo de los tonos vecinos del sincronismo: con senal fuerte la
    /// falda de la modulacion suavizada cae en los tonos vecinos y se tomaria por ruido,
    /// hundiendo el informe una decena de decibelios. Se usa el menor de los dos.
    /// </remarks>
    private int Informe(double senal, double ruido)
    {
        if (ruido <= 0 || senal <= 0) return -40;
        var ruidoEnLaReferencia = ruido * (2500.0 / _p.EspaciadoDeTonosHz);
        var db = (10 * Math.Log10(senal / ruidoEnLaReferencia)) + CorreccionDelInforme;
        return (int)Math.Round(Math.Clamp(db, -50, 50));
    }

    private bool YaEstaba(List<(string Texto, double Tono)> vistas, string texto, double tono)
    {
        foreach (var (t, f) in vistas)
            if (Math.Abs(f - tono) < 2 * _p.EspaciadoDeTonosHz && string.Equals(t, texto, StringComparison.Ordinal)) return true;
        return false;
    }
}
