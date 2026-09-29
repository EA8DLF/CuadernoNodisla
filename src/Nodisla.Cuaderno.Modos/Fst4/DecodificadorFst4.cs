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
/// dos bloques por simbolo y casillas de medio tono, se buscan en el los cinco grupos de
/// sincronismo, y cada candidata se mira de cerca correlando directamente los 160 simbolos con
/// los cuatro tonos —afinando antes un cuarto de simbolo y medio tono a cada lado— para sacar
/// las 240 confianzas que van al corrector.
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

        var salida = new List<DecodificacionPropia>();
        var vistas = new List<(string Texto, double Tono)>();
        var confianzas = new float[ParametrosFst4.BitsDePalabra];
        var palabra = new byte[ParametrosFst4.BitsDePalabra];
        var energias = new double[ParametrosFst4.SimbolosTotales * ParametrosFst4.Tonos];
        int palabrasValidas = 0, rechazadas = 0, profundas = 0;

        foreach (var candidata in candidatas)
        {
            ct.ThrowIfCancellationRequested();
            var medida = Afinar(muestras, espectro, candidata, energias);
            var (senal, ruido) = MedirNiveles(energias);
            CalcularConfianzas(energias, ruido, confianzas);

            var profunda = false;
            var cuantos = 1;
            if (!_corrector.TryDecodificar(confianzas, palabra, VueltasDelCorrector))
            {
                if (!UsarRecuperacionProfunda) continue;
                if (medida.Sincronismo < SincronismoMinimoParaLaProfunda) continue;
                if (!_profunda.TryRecuperar(confianzas, palabra)) continue;
                profunda = true;
                cuantos = _profunda.CandidatosEncontrados;
            }

            for (var c = 0; c < cuantos; c++)
            {
                if (profunda && c > 0) _profunda.CopiarCandidato(c, palabra);
                palabrasValidas++;
                if (!TryInterpretar(palabra, catalogo, out var texto, out var mensaje, out var baliza)) { rechazadas++; continue; }

                var tonoHz = medida.TonoBaseHz;
                if (YaEstaba(vistas, texto, tonoHz)) break;
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
                break;
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
    // Medida fina de una candidata
    // ----------------------------------------------------------------------------------------

    private Medida Afinar(float[] muestras, Espectrograma espectro, Candidata candidata, double[] energias)
    {
        var fs = _p.FrecuenciaDeAnalisis;
        var nsps = _p.MuestrasPorSimboloDeAnalisis;
        var hzPorCasilla = espectro.HzPorCasilla;
        var f0 = (candidata.Casilla + espectro.PrimeraCasilla) * hzPorCasilla;
        var n0 = candidata.Bloque * espectro.Paso;

        var mejor = double.NegativeInfinity;
        var mejorF = f0;
        var mejorN = n0;
        var pasoT = Math.Max(1, nsps / 4);
        var pasoF = hzPorCasilla / 2;
        Span<double> sync = stackalloc double[ParametrosFst4.Tonos];

        for (var df = -1; df <= 1; df++)
            for (var dt = -1; dt <= 1; dt++)
            {
                var f = f0 + (df * pasoF);
                var n = n0 + (dt * pasoT);
                if (f <= 0 || n < 0 || n + _p.MuestrasDeLaSenal > muestras.Length) continue;
                var puntos = PuntuarSincronismo(muestras, n, f, sync);
                if (puntos <= mejor) continue;
                mejor = puntos;
                mejorF = f;
                mejorN = n;
            }

        // Segunda vuelta mas fina alrededor de lo mejor.
        var baseF = mejorF;
        var baseN = mejorN;
        for (var df = -1; df <= 1; df++)
            for (var dt = -1; dt <= 1; dt++)
            {
                if (df == 0 && dt == 0) continue;
                var f = baseF + (df * pasoF / 2);
                var n = baseN + (dt * pasoT / 2);
                if (f <= 0 || n < 0 || n + _p.MuestrasDeLaSenal > muestras.Length) continue;
                var puntos = PuntuarSincronismo(muestras, n, f, sync);
                if (puntos <= mejor) continue;
                mejor = puntos;
                mejorF = f;
                mejorN = n;
            }

        MedirTodosLosSimbolos(muestras, mejorN, mejorF, energias);
        return new Medida(mejor, mejorF, mejorN / fs);
    }

    /// <summary>Energia de cada tono en un simbolo, por correlacion directa con las cuatro frecuencias.</summary>
    private void MedirUnSimbolo(float[] muestras, int desde, double tonoBaseHz, Span<double> energias)
    {
        var fs = _p.FrecuenciaDeAnalisis;
        var nsps = _p.MuestrasPorSimboloDeAnalisis;
        Span<double> re = stackalloc double[ParametrosFst4.Tonos];
        Span<double> im = stackalloc double[ParametrosFst4.Tonos];
        Span<double> cosPaso = stackalloc double[ParametrosFst4.Tonos];
        Span<double> senPaso = stackalloc double[ParametrosFst4.Tonos];
        Span<double> cr = stackalloc double[ParametrosFst4.Tonos];
        Span<double> ci = stackalloc double[ParametrosFst4.Tonos];
        for (var t = 0; t < ParametrosFst4.Tonos; t++)
        {
            var w = -2 * Math.PI * (tonoBaseHz + (t * _p.EspaciadoDeTonosHz)) / fs;
            cosPaso[t] = Math.Cos(w);
            senPaso[t] = Math.Sin(w);
            cr[t] = 1;
            ci[t] = 0;
            re[t] = 0;
            im[t] = 0;
        }

        var fin = Math.Min(muestras.Length, desde + nsps);
        for (var n = Math.Max(0, desde); n < fin; n++)
        {
            var x = muestras[n];
            for (var t = 0; t < ParametrosFst4.Tonos; t++)
            {
                re[t] += x * cr[t];
                im[t] += x * ci[t];
                var nr = (cr[t] * cosPaso[t]) - (ci[t] * senPaso[t]);
                ci[t] = (cr[t] * senPaso[t]) + (ci[t] * cosPaso[t]);
                cr[t] = nr;
            }
        }
        for (var t = 0; t < ParametrosFst4.Tonos; t++) energias[t] = (re[t] * re[t]) + (im[t] * im[t]);
    }

    private double PuntuarSincronismo(float[] muestras, int comienzo, double tonoBaseHz, Span<double> energias)
    {
        var nsps = _p.MuestrasPorSimboloDeAnalisis;
        var posiciones = ParametrosFst4.PosicionesDeSincronismo;
        double enElPatron = 0, enTodos = 0;
        for (var g = 0; g < posiciones.Length; g++)
        {
            var patron = (g & 1) == 0 ? ParametrosFst4.SincronismoUno : ParametrosFst4.SincronismoDos;
            for (var k = 0; k < patron.Length; k++)
            {
                MedirUnSimbolo(muestras, comienzo + ((posiciones[g] + k) * nsps), tonoBaseHz, energias);
                enElPatron += energias[patron[k]];
                for (var t = 0; t < ParametrosFst4.Tonos; t++) enTodos += energias[t];
            }
        }
        return enTodos <= 0 ? 0 : enElPatron * ParametrosFst4.Tonos / enTodos;
    }

    private void MedirTodosLosSimbolos(float[] muestras, int comienzo, double tonoBaseHz, double[] energias)
    {
        var nsps = _p.MuestrasPorSimboloDeAnalisis;
        for (var s = 0; s < ParametrosFst4.SimbolosTotales; s++)
            MedirUnSimbolo(muestras, comienzo + (s * nsps), tonoBaseHz, energias.AsSpan(s * ParametrosFst4.Tonos, ParametrosFst4.Tonos));
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

    /// <summary>Confianza por bit: diferencia de energias del mejor tono a cero y a uno, en veces el ruido, como en FT8.</summary>
    private void CalcularConfianzas(double[] energias, double ruido, float[] confianzas)
    {
        var posiciones = _p.PosicionesDeDatos;
        var escala = ruido > 0 ? 1.0 / ruido : 0;
        var inverso = ParametrosFst4.MapaDeGrayInverso;
        for (var s = 0; s < posiciones.Length; s++)
        {
            var baseTonos = posiciones[s] * ParametrosFst4.Tonos;
            for (var b = 0; b < ParametrosFst4.BitsPorSimbolo; b++)
            {
                var peso = 1 << (ParametrosFst4.BitsPorSimbolo - 1 - b);
                double mejorCero = 0, mejorUno = 0;
                for (var t = 0; t < ParametrosFst4.Tonos; t++)
                {
                    var e = energias[baseTonos + t];
                    if ((inverso[t] & peso) == 0) { if (e > mejorCero) mejorCero = e; }
                    else if (e > mejorUno) mejorUno = e;
                }
                confianzas[(s * ParametrosFst4.BitsPorSimbolo) + b] = (float)((mejorCero - mejorUno) * escala);
            }
        }
        if (escala == 0) Array.Clear(confianzas);
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
