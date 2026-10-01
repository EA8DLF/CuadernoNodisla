using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Modos.Convolucional;
using Nodisla.Cuaderno.Modos.Tablas;

namespace Nodisla.Cuaderno.Modos.Wspr;

/// <summary>
/// Como fue una ventana de WSPR, con las cuentas que permiten vigilar al decodificador.
/// </summary>
/// <param name="Decodificaciones">Lo que se saco.</param>
/// <param name="Candidatas">Candidatas que dejo la busqueda gruesa.</param>
/// <param name="CaminosDeFano">Veces que el decodificador secuencial llego al final del arbol.</param>
/// <param name="RechazadasPorLaGramatica">
/// Caminos completos cuyos 50 bits no formaban un mensaje valido. Cada una es un intento del
/// ruido de colarse; si esta columna sube, el decodificador esta tentando a la suerte.
/// </param>
/// <param name="RechazadasPorLaCoincidencia">
/// Mensajes con gramatica valida que no se parecian bastante a lo recibido al volver a codificarlos.
/// </param>
/// <param name="Duracion">Lo que costo, en tiempo de reloj.</param>
public sealed record ResultadoWspr(
    IReadOnlyList<DecodificacionPropia> Decodificaciones,
    int Candidatas,
    int CaminosDeFano,
    int RechazadasPorLaGramatica,
    int RechazadasPorLaCoincidencia,
    TimeSpan Duracion)
{
    /// <summary>Un apunte por cada camino completo de Fano, aceptado o no, para el banco.</summary>
    public IReadOnlyList<DetalleWspr> Detalles { get; init; } = [];
}

/// <summary>Como fue un camino completo de Fano, para medir los umbrales en vez de opinarlos.</summary>
/// <param name="Texto">Mensaje, o vacio si no paso la gramatica.</param>
/// <param name="Sincronismo">Sincronismo fino de la candidata.</param>
/// <param name="Coincidencia">Parecido del mensaje recodificado con lo recibido.</param>
/// <param name="Metrica">Metrica final del camino.</param>
/// <param name="Esfuerzo">Movimientos que costo.</param>
/// <param name="Aceptado">Si salio como decodificacion.</param>
public readonly record struct DetalleWspr(string Texto, double Sincronismo, double Coincidencia, int Metrica, long Esfuerzo, bool Aceptado);

/// <summary>
/// El decodificador de WSPR: de una ventana de dos minutos de audio a los mensajes que hay en ella.
/// </summary>
/// <remarks>
/// <para>
/// El orden es el de siempre: analizar la ventana, buscar candidatas en grueso, afinar cada
/// una, sacar los valores blandos, deshacer el entrelazado, decodificar con Fano y comprobar.
/// </para>
/// <para>
/// <b>Las cinco puertas contra los mensajes falsos.</b> Un decodificador secuencial acaba
/// devolviendo <i>algun</i> camino si se le deja el tiempo suficiente, tenga sentido o no. Por
/// eso un mensaje solo sale si pasa las cinco:
/// </para>
/// <list type="number">
/// <item>El <b>sincronismo fino</b>: sin el, a Fano no se le da nada (ver <see cref="SincronismoFinoMinimo"/>).</item>
/// <item>La <b>cola</b>: el camino tiene que atravesar 31 bits en los que solo existe la rama
/// del cero, que son 62 bits recibidos sin alternativa.</item>
/// <item>El <b>esfuerzo</b>: si Fano se pasa del limite, no hay mensaje.</item>
/// <item>La <b>gramatica</b>: indicativo con forma de indicativo, localizador dentro del mapa,
/// potencia de las permitidas.</item>
/// <item>La <b>coincidencia</b>: el mensaje se vuelve a codificar y se compara con lo que se
/// oyo; si no se parece lo bastante, se tira. El umbral esta medido en el banco.</item>
/// </list>
/// <para>
/// Un WSPR falso no es un contacto falso, pero es un spot de propagacion mentira que otros
/// usaran para decidir si una banda esta abierta. Ante la duda, nada.
/// </para>
/// </remarks>
public sealed class DecodificadorWspr
{
    private readonly ILogger? _registro;
    private readonly Dictionary<int, string> _resumenes = [];

    /// <summary>Crea el decodificador con sus valores medidos en el banco.</summary>
    /// <param name="registro">Registro, si se quiere ver que pasa por dentro.</param>
    public DecodificadorWspr(ILogger? registro = null)
    {
        _registro = registro;
        Fano = new DecodificadorDeFano(CodigoConvolucional.DeWsprYJt9);
    }

    /// <summary>El decodificador secuencial, con su tabla de metricas y su limite de esfuerzo.</summary>
    public DecodificadorDeFano Fano { get; init; }

    /// <summary>Candidatas que se afinan por ventana, de mejor a peor sincronismo grueso.</summary>
    public int CandidatasPorVentana { get; init; } = 40;

    /// <summary>Sincronismo grueso por debajo del cual una candidata ni se mira.</summary>
    public double SincronismoMinimo { get; init; } = 0.08;

    /// <summary>
    /// Sincronismo fino, ya afinada la candidata, por debajo del cual no se intenta decodificar.
    /// </summary>
    /// <remarks>
    /// Medido el 27-09-2026: el acierto mas debil de 120 ventanas entre −29 y −31 dB tenia 0,281;
    /// el unico falso visto en el banco (un camino de Fano sacado del ruido a −26 dB, en otra
    /// frecuencia) tenia 0,113 y coincidencia 0,81, que la puerta de coincidencia dejo pasar.
    /// La metrica final de Fano no separa (−332 un acierto, −341 el falso). 0,20 queda en medio.
    /// </remarks>
    public double SincronismoFinoMinimo { get; init; } = 0.20;

    /// <summary>Cuanto vale una desviacion tipica del valor blando en la escala de 0 a 255.</summary>
    public double GananciaDeLosBlandos { get; init; } = 40;

    /// <summary>
    /// Parecido minimo entre el mensaje recodificado y lo recibido, de -1 a 1, para darlo por bueno.
    /// </summary>
    public double CoincidenciaMinima { get; init; } = 0.30;

    /// <summary>
    /// A menos de esto de una senal ya decodificada no se mira ninguna otra candidata: es la
    /// misma senal vista desde un tono al lado. Una senal ocupa seis hercios.
    /// </summary>
    public double SeparacionMinimaHz { get; init; } = 4.0;

    /// <summary>Correccion que se suma a la relacion senal-ruido medida para que cuadre con la de verdad.</summary>
    public double CorreccionDelInforme { get; init; } = 0.0;

    /// <summary>Indicativos compuestos que se conocen, por su resumen, para resolver los mensajes de tipo 3.</summary>
    public IReadOnlyDictionary<int, string> Resumenes => _resumenes;

    /// <summary>Apunta un indicativo compuesto para poder resolver su resumen cuando llegue en un tipo 3.</summary>
    public void Recordar(string indicativoCompuesto)
    {
        ArgumentNullException.ThrowIfNull(indicativoCompuesto);
        var ind = indicativoCompuesto.Trim().ToUpperInvariant();
        if (ind.Length == 0) return;
        _resumenes[HashDeIndicativo.Calcular(ind)] = ind;
    }

    /// <summary>
    /// Decodifica una ventana.
    /// </summary>
    /// <param name="audio">Audio a 12000 muestras por segundo, hasta 174 s.</param>
    /// <param name="ventanaUtc">Instante de arranque de la ventana.</param>
    /// <param name="segundosDelPrimerMuestreo">
    /// Instante, respecto al minuto par, de la primera muestra. Cero si el audio empieza en el
    /// minuto par; uno si empieza en el segundo en que arranca la senal por convenio.
    /// </param>
    /// <param name="ct">Testigo de cancelacion; se comprueba entre candidatas.</param>
    public ResultadoWspr Decodificar(
        ReadOnlySpan<float> audio,
        DateTimeOffset ventanaUtc,
        double segundosDelPrimerMuestreo = 0,
        CancellationToken ct = default)
    {
        var reloj = Stopwatch.StartNew();
        var salida = new List<DecodificacionPropia>();
        var minimoDeMuestras = (int)(ParametrosWspr.Simbolos / 2 * ParametrosWspr.DuracionDeSimboloSegundos * ParametrosWspr.FrecuenciaDeAnalisis);
        if (audio.Length < minimoDeMuestras) return new ResultadoWspr(salida, 0, 0, 0, 0, reloj.Elapsed);

        var analisis = new AnalisisWspr(audio);
        var segundosUtiles = (double)analisis.MuestrasDeAudio / ParametrosWspr.FrecuenciaDeAnalisis;
        var bandaBase = analisis.BandaBase();
        var candidatas = SincronizadorWspr.Buscar(
            bandaBase,
            (int)(segundosUtiles * ParametrosWspr.FrecuenciaDeBandaBase),
            CandidatasPorVentana,
            SincronismoMinimo);

        int caminos = 0, sinGramatica = 0, sinCoincidencia = 0;
        var detalles = new List<DetalleWspr>();
        var blandosPorSimbolo = new byte[ParametrosWspr.Simbolos];
        var blandosPorBit = new byte[ParametrosWspr.Simbolos];
        var muestrasEstrechasUtiles = (int)(segundosUtiles * ParametrosWspr.FrecuenciaEstrecha);

        var frecuenciasDecodificadas = new List<double>();
        foreach (var candidata in candidatas)
        {
            ct.ThrowIfCancellationRequested();

            var centro = ParametrosWspr.CentroDeLaBandaHz + candidata.DesplazamientoHz;

            // Una senal fuerte deja candidatas a un tono de distancia de si misma. Volver a
            // decodificarlas es tiempo perdido y, peor, es darle a Fano valores blandos de una
            // senal desalineada, que es justo la materia prima de un camino inventado.
            if (frecuenciasDecodificadas.Any(f => Math.Abs(f - centro) < SeparacionMinimaHz)) continue;
            var estrecha = analisis.Estrecha(centro);
            var demodulador = new DemoduladorWspr(estrecha, muestrasEstrechasUtiles);
            var comienzo = (int)Math.Round(candidata.ComienzoSegundos * ParametrosWspr.FrecuenciaEstrecha);
            var medida = demodulador.Afinar(estrecha.ResiduoHz, comienzo, candidata.DerivaHz);

            // Sin sincronismo de verdad no se le da nada a Fano: con tiempo suficiente acaba
            // atravesando el arbol por ruido puro y la coincidencia no lo delata, porque el
            // camino que elige es justo el que mas se parece a lo recibido.
            if (medida.Sincronismo < SincronismoFinoMinimo) continue;

            var blandos = medida.ValoresBlandos(GananciaDeLosBlandos);
            blandos.CopyTo(blandosPorSimbolo, 0);
            CodificadorWspr.Desentrelazar(blandosPorSimbolo, blandosPorBit);

            var resultado = Fano.Decodificar(blandosPorBit, MensajeWspr.Bits, CodificadorWspr.BitsCodificados);
            if (!resultado.Decodificado) continue;
            caminos++;

            if (!MensajeWspr.TryDesempaquetar(resultado.Bits.AsSpan(0, MensajeWspr.Bits), Resolver, out var mensaje))
            {
                sinGramatica++;
                detalles.Add(new DetalleWspr(string.Empty, medida.Sincronismo, 0, resultado.Metrica, resultado.Esfuerzo, false));
                continue;
            }

            var coincidencia = Coincidencia(mensaje, medida);
            detalles.Add(new DetalleWspr(mensaje.Texto, medida.Sincronismo, coincidencia, resultado.Metrica, resultado.Esfuerzo, coincidencia >= CoincidenciaMinima));
            if (coincidencia < CoincidenciaMinima)
            {
                sinCoincidencia++;
                _registro?.LogDebug("WSPR: «{Mensaje}» descartado por coincidencia {Coincidencia:0.00}.", mensaje.Texto, coincidencia);
                continue;
            }

            if (mensaje.Tipo == 2) Recordar(mensaje.Indicativo);

            var frecuencia = centro + medida.DesplazamientoHz;
            var comienzoSegundos = medida.ComienzoMuestras / ParametrosWspr.FrecuenciaEstrecha;
            var desfase = comienzoSegundos + segundosDelPrimerMuestreo - ParametrosWspr.ComienzoNominalSegundos;
            var decibelios = RelacionSenalRuido(mensaje, medida);

            var texto = mensaje.Texto;
            frecuenciasDecodificadas.Add(frecuencia);
            if (salida.Any(d => d.Texto == texto)) continue;

            salida.Add(new DecodificacionPropia(
                texto,
                (int)Math.Round(decibelios),
                Math.Round(desfase, 2),
                (int)Math.Round(frecuencia),
                ModoDelModem.Wspr,
                ventanaUtc)
            {
                Llamante = mensaje.IndicativoConocido ? Indicativo.Crudo(mensaje.Indicativo) : Indicativo.Vacio,
                Llamado = Indicativo.Vacio,
                Locator = mensaje.Localizador.Length > 0 && Locator.TryParse(mensaje.Localizador, out var loc) ? loc : Locator.Vacio,
                EsCq = false,
            });

            _registro?.LogDebug(
                "WSPR: «{Mensaje}» a {Frecuencia:0.0} Hz, {Decibelios:0} dB, desfase {Desfase:0.00} s, deriva {Deriva:0.0} Hz, sincronismo {Sincronismo:0.00}, coincidencia {Coincidencia:0.00}, esfuerzo {Esfuerzo}.",
                texto, frecuencia, decibelios, desfase, medida.DerivaHz, medida.Sincronismo, coincidencia, resultado.Esfuerzo);
        }

        return new ResultadoWspr(salida, candidatas.Count, caminos, sinGramatica, sinCoincidencia, reloj.Elapsed) { Detalles = detalles };
    }

    private string? Resolver(int resumen) => _resumenes.TryGetValue(resumen, out var ind) ? ind : null;

    /// <summary>
    /// Cuanto se parece lo recibido al mensaje que se acaba de decodificar, de -1 a 1.
    /// </summary>
    /// <remarks>
    /// Se vuelven a codificar los 50 bits y, simbolo a simbolo, se mira si la diferencia de
    /// potencia entre los dos tonos posibles apunta hacia el bit que dice el mensaje. Ojo: no es
    /// independiente de Fano. Un camino sacado del ruido tambien da 0,8, porque Fano elige
    /// justo el que mas se parece a lo recibido; la puerta que separa es el sincronismo fino.
    /// </remarks>
    public static double Coincidencia(MensajeWspr mensaje, MedidaWspr medida)
    {
        ArgumentNullException.ThrowIfNull(mensaje);
        ArgumentNullException.ThrowIfNull(medida);
        var tonos = CodificadorWspr.Tonos(mensaje);
        var crudos = medida.ValoresCrudos();
        double acuerdo = 0, magnitud = 0;
        for (var k = 0; k < crudos.Length; k++)
        {
            var dato = tonos[k] >> 1;
            acuerdo += (dato == 1 ? 1 : -1) * crudos[k];
            magnitud += Math.Abs(crudos[k]);
        }
        return magnitud > 0 ? acuerdo / magnitud : 0;
    }

    /// <summary>
    /// Relacion senal-ruido referida a 2500 Hz, como la apuntan todos los programas de WSPR.
    /// </summary>
    /// <remarks>
    /// En cada simbolo, el tono que de verdad se emitio lleva senal mas ruido y los otros tres
    /// solo ruido. La casilla de un tono tiene el ancho de la separacion entre tonos, asi que el
    /// ruido por hercio sale de dividir por 1,4648, y la relacion se refiere a 2500 Hz
    /// multiplicando por esa anchura.
    /// </remarks>
    private double RelacionSenalRuido(MensajeWspr mensaje, MedidaWspr medida)
    {
        var tonos = CodificadorWspr.Tonos(mensaje);
        double senal = 0, ruido = 0;
        var contados = 0;
        for (var k = 0; k < tonos.Length; k++)
        {
            double total = 0;
            for (var t = 0; t < 4; t++) total += medida.PotenciaDeCadaTono[k, t];
            if (total <= 0) continue;
            var propia = medida.PotenciaDeCadaTono[k, tonos[k]];
            senal += propia;
            ruido += (total - propia) / 3;
            contados++;
        }
        if (contados == 0 || ruido <= 0) return -99;
        var potenciaDeSenal = Math.Max((senal - ruido) / contados, 1e-12 * ruido / contados);
        var ruidoPorHercio = ruido / contados / ParametrosWspr.EspaciadoDeTonosHz;
        return (10 * Math.Log10(potenciaDeSenal / (ruidoPorHercio * ParametrosWspr.AnchoDeBandaDeReferencia))) + CorreccionDelInforme;
    }
}
