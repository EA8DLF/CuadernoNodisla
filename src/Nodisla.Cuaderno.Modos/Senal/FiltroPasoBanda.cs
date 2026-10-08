namespace Nodisla.Cuaderno.Modos.Senal;

/// <summary>
/// Filtro de paso de banda de segundo orden (biquad, formulas RBJ del «Audio EQ Cookbook»),
/// pensado para limpiar el audio <b>de verdad</b> antes de decodificar.
/// </summary>
/// <remarks>
/// <para>
/// No es el ancho visible de la cascada (<c>AnchoVisibleHz</c>): aquello solo decide hasta donde
/// se pinta, nunca toca una muestra. Esto se aplica al audio que luego ve el decodificador, asi
/// que una senal fuerte fuera de banda (ruido de red, un canal vecino) deja de competir con la
/// que interesa.
/// </para>
/// </remarks>
public sealed class FiltroPasoBanda
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _x1, _x2, _y1, _y2;
    private bool _hayCoeficientes;

    /// <summary>Desde donde se calcularon los coeficientes vigentes, para no recalcular sin necesidad.</summary>
    private int _desdeHzDeLosCoeficientes;
    private int _hastaHzDeLosCoeficientes;
    private int _frecuenciaDeLosCoeficientes;

    /// <summary>
    /// Dice si hace falta recalcular los coeficientes para estos limites y esta frecuencia de
    /// muestreo, y si asi es, los recalcula.
    /// </summary>
    public void AsegurarAjuste(int desdeHz, int hastaHz, int frecuenciaDeMuestreo)
    {
        if (_hayCoeficientes
            && desdeHz == _desdeHzDeLosCoeficientes
            && hastaHz == _hastaHzDeLosCoeficientes
            && frecuenciaDeMuestreo == _frecuenciaDeLosCoeficientes)
            return;

        Ajustar(desdeHz, hastaHz, frecuenciaDeMuestreo);
    }

    /// <summary>Recalcula los coeficientes para dejar pasar, aproximadamente, entre <paramref name="desdeHz"/> y <paramref name="hastaHz"/>.</summary>
    public void Ajustar(int desdeHz, int hastaHz, int frecuenciaDeMuestreo)
    {
        var desde = Math.Max(1, desdeHz);
        var hasta = Math.Max(desde + 1, hastaHz);
        var nyquist = frecuenciaDeMuestreo / 2;
        hasta = Math.Min(hasta, nyquist - 1);
        if (hasta <= desde) hasta = desde + 1;

        var centro = Math.Sqrt(desde * hasta);
        var ancho = Math.Max(1, hasta - desde);
        var q = Math.Clamp(centro / ancho, 0.2, 20.0);

        var w0 = 2 * Math.PI * centro / frecuenciaDeMuestreo;
        var alpha = Math.Sin(w0) / (2 * q);
        var cosw0 = Math.Cos(w0);

        // Paso de banda, ganancia constante en el pico (constant skirt gain, BW en Q).
        var a0 = 1 + alpha;
        _b0 = alpha / a0;
        _b1 = 0;
        _b2 = -alpha / a0;
        _a1 = -2 * cosw0 / a0;
        _a2 = (1 - alpha) / a0;

        _desdeHzDeLosCoeficientes = desdeHz;
        _hastaHzDeLosCoeficientes = hastaHz;
        _frecuenciaDeLosCoeficientes = frecuenciaDeMuestreo;
        _hayCoeficientes = true;
    }

    /// <summary>Olvida el estado interno (no los coeficientes): para un cambio de sesion limpio.</summary>
    public void Reiniciar()
    {
        _x1 = _x2 = _y1 = _y2 = 0;
    }

    /// <summary>Filtra las muestras en el sitio.</summary>
    public void Procesar(Span<float> muestras)
    {
        for (var i = 0; i < muestras.Length; i++)
        {
            var x0 = muestras[i];
            var y0 = (_b0 * x0) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);
            _x2 = _x1;
            _x1 = x0;
            _y2 = _y1;
            _y1 = y0;
            muestras[i] = (float)y0;
        }
    }
}
