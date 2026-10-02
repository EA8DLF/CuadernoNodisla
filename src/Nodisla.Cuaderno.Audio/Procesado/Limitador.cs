namespace Nodisla.Cuaderno.Audio.Procesado;

/// <summary>
/// Limitador de picos sin retraso: ninguna muestra sale por encima del techo.
/// </summary>
/// <remarks>
/// <para>
/// La ganancia baja de golpe en cuanto una muestra pasaria del techo y vuelve despacio (unos
/// 80 ms) a la unidad. Como cada muestra se multiplica por una ganancia que nunca es mayor que
/// <c>techo / |muestra|</c>, el techo se cumple <b>siempre</b>, no en promedio. Al final, por si
/// el redondeo, se recorta al techo.
/// </para>
/// <para>
/// Sin mirar adelante, el ataque instantaneo deforma un poco el primer ciclo de un golpe fuerte:
/// para los altavoces es mejor eso que un chasquido saturado.
/// </para>
/// </remarks>
public sealed class Limitador
{
    private int _frecuencia;
    private double _vuelta;
    private double _ganancia = 1.0;

    /// <summary>Techo en decibelios bajo el fondo de escala (de -20 a 0).</summary>
    public double TechoDb { get; set; } = -3.0;

    /// <summary>Ganancia en curso, de 0 a 1 (para el medidor de reduccion).</summary>
    public double GananciaActual => _ganancia;

    /// <summary>Vuelve a la unidad.</summary>
    public void Reiniciar() => _ganancia = 1.0;

    /// <summary>Techo lineal para unos decibelios.</summary>
    public static float TechoLineal(double techoDb) =>
        (float)Math.Pow(10.0, Math.Clamp(double.IsFinite(techoDb) ? techoDb : -3.0, -40.0, 0.0) / 20.0);

    /// <summary>Limita el bloque en el sitio.</summary>
    public void Procesar(Span<float> muestras, int frecuencia)
    {
        if (frecuencia != _frecuencia && frecuencia > 0)
        {
            _frecuencia = frecuencia;
            _vuelta = 1.0 - Math.Exp(-1.0 / (0.080 * frecuencia));
        }

        var techo = TechoLineal(TechoDb);
        var g = _ganancia;
        for (var i = 0; i < muestras.Length; i++)
        {
            var x = float.IsFinite(muestras[i]) ? muestras[i] : 0f;
            var absoluto = Math.Abs(x);
            var tope = absoluto > techo ? techo / absoluto : 1.0;
            g += (1.0 - g) * _vuelta;
            if (g > tope) g = tope;
            var y = (float)(x * g);
            muestras[i] = Math.Clamp(y, -techo, techo);
        }

        _ganancia = g;
    }
}
