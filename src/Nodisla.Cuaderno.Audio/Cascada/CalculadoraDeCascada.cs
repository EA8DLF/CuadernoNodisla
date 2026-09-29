using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Audio.Cascada;

/// <summary>
/// Convierte el audio que entra en columnas de cascada.
/// </summary>
/// <remarks>
/// <para>
/// Aqui no hay ni un pixel: se entregan magnitudes en decibelios y la anchura de cada casilla,
/// y quien pinte que decida colores y escala. Asi la cascada se puede probar sin pantalla y
/// sin tarjeta de sonido, con muestras inventadas.
/// </para>
/// <para>
/// La cascada se calcula <b>del audio</b> porque no hay otra via: esta instalacion no tiene
/// ningun puente FTDI por el que pedirle al FT-710 su propio analizador de espectro.
/// </para>
/// </remarks>
public sealed class CalculadoraDeCascada
{
    private readonly OpcionesDeCascada _opciones;
    private readonly float[] _ventana;
    private readonly double _normalizador;
    private readonly float[] _real;
    private readonly float[] _imaginaria;
    private readonly TroceadorConSolape _troceador;

    private int _frecuenciaDeMuestreo;
    private int _casillas;

    /// <summary>Crea la calculadora.</summary>
    /// <param name="opciones">Ajustes de la cascada; si es nulo, los de partida.</param>
    public CalculadoraDeCascada(OpcionesDeCascada? opciones = null)
    {
        _opciones = (opciones ?? new OpcionesDeCascada()).Copiar();

        var tamano = _opciones.TamanoDeTransformada;
        _ventana = VentanaDeHann.Crear(tamano);

        // Con esto una senoide de amplitud uno sale a cero decibelios: la mitad de su energia
        // se va a la casilla espejo, de ahi el factor dos.
        _normalizador = 2.0 / VentanaDeHann.Suma(_ventana);

        _real = new float[tamano];
        _imaginaria = new float[tamano];
        _troceador = new TroceadorConSolape(tamano, _opciones.Salto);
    }

    /// <summary>Muestras de cada tramo analizado.</summary>
    public int TamanoDeTransformada => _opciones.TamanoDeTransformada;

    /// <summary>Muestras que se avanza de una columna a la siguiente.</summary>
    public int Salto => _troceador.Salto;

    /// <summary>Anchura en hercios de cada casilla, o cero si todavia no ha entrado audio.</summary>
    public double HzPorCasilla => _frecuenciaDeMuestreo <= 0
        ? 0.0
        : (double)_frecuenciaDeMuestreo / _opciones.TamanoDeTransformada;

    /// <summary>Casillas que se entregan en cada columna, o cero si todavia no ha entrado audio.</summary>
    public int Casillas => _casillas;

    /// <summary>Olvida lo acumulado y vuelve a empezar.</summary>
    public void Reiniciar()
    {
        _troceador.Reiniciar();
        _frecuenciaDeMuestreo = 0;
        _casillas = 0;
    }

    /// <summary>
    /// Mete un bloque de audio y devuelve las columnas que hayan salido.
    /// </summary>
    /// <param name="bloque">Bloque recien capturado.</param>
    /// <returns>Las columnas completadas con este bloque; puede venir vacia.</returns>
    /// <exception cref="ArgumentNullException">Si el bloque es nulo.</exception>
    public IReadOnlyList<ColumnaDeCascada> Procesar(BloqueDeAudio bloque)
    {
        ArgumentNullException.ThrowIfNull(bloque);

        PrepararPara(bloque.FrecuenciaDeMuestreo);

        // Numero de la primera muestra de este bloque dentro del flujo: sirve para fechar cada
        // columna, que casi siempre empieza antes que el bloque que la termina.
        var indiceDelBloque = _troceador.MuestrasRecibidas;
        var columnas = new List<ColumnaDeCascada>();
        var hzPorCasilla = HzPorCasilla;
        var frecuencia = bloque.FrecuenciaDeMuestreo;

        _troceador.Anadir(bloque.Muestras.Span, (indiceDeInicio, tramo) =>
        {
            var desfase = (double)(indiceDeInicio - indiceDelBloque) / frecuencia;
            var instante = bloque.InstanteUtc + TimeSpan.FromSeconds(desfase);
            columnas.Add(new ColumnaDeCascada(Analizar(tramo), hzPorCasilla, instante));
        });

        return columnas;
    }

    /// <summary>
    /// Analiza un tramo suelto y devuelve sus magnitudes en decibelios.
    /// </summary>
    /// <param name="tramo">Tramo del tamano de la transformada.</param>
    /// <returns>Magnitudes en decibelios, desde continua hasta la frecuencia maxima.</returns>
    /// <exception cref="ArgumentException">Si el tramo no mide lo que la transformada.</exception>
    public float[] Analizar(ReadOnlySpan<float> tramo)
    {
        if (tramo.Length != _opciones.TamanoDeTransformada)
        {
            throw new ArgumentException(
                "El tramo tiene que medir exactamente lo que la transformada.",
                nameof(tramo));
        }

        for (var i = 0; i < tramo.Length; i++)
        {
            _real[i] = tramo[i] * _ventana[i];
            _imaginaria[i] = 0f;
        }

        TransformadaRapida.Transformar(_real, _imaginaria);

        var casillas = _casillas > 0 ? _casillas : (_opciones.TamanoDeTransformada / 2) + 1;
        var magnitudes = new float[casillas];
        var suelo = _opciones.SueloDb;

        for (var i = 0; i < casillas; i++)
        {
            var modulo = Math.Sqrt((_real[i] * (double)_real[i]) + (_imaginaria[i] * (double)_imaginaria[i]))
                * _normalizador;

            var decibelios = modulo <= 1e-12 ? suelo : 20.0 * Math.Log10(modulo);
            magnitudes[i] = (float)Math.Max(decibelios, suelo);
        }

        return magnitudes;
    }

    /// <summary>
    /// Ajusta lo que depende de la frecuencia de muestreo, y reinicia si esta ha cambiado.
    /// </summary>
    private void PrepararPara(int frecuenciaDeMuestreo)
    {
        if (frecuenciaDeMuestreo <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frecuenciaDeMuestreo),
                frecuenciaDeMuestreo,
                "La frecuencia de muestreo tiene que ser positiva.");
        }

        if (frecuenciaDeMuestreo == _frecuenciaDeMuestreo)
        {
            return;
        }

        // Cambiar de dispositivo a media cascada deja lo acumulado sin sentido: mejor tirarlo
        // que mezclar audio de dos sitios en un mismo tramo.
        if (_frecuenciaDeMuestreo != 0)
        {
            _troceador.Reiniciar();
        }

        _frecuenciaDeMuestreo = frecuenciaDeMuestreo;

        var tope = (_opciones.TamanoDeTransformada / 2) + 1;
        var porFrecuencia = (int)Math.Floor(_opciones.FrecuenciaMaximaHz / HzPorCasilla) + 1;
        _casillas = Math.Clamp(porFrecuencia, 1, tope);
    }
}
