namespace Nodisla.Cuaderno.Modos.Cw;

/// <summary>
/// La envolvente de un tono: cuánta potencia hay, a cada instante, en un filtro estrecho
/// centrado en él.
/// </summary>
/// <remarks>
/// <para>
/// Se baja el tono a cero con un oscilador complejo (un fasor que gira, sin senos por muestra)
/// y se filtra con dos medias móviles en cascada: respuesta triangular en el tiempo y de seno
/// cardinal al cuadrado en frecuencia, con lóbulos a −26 dB y ceros cada <c>fs/L</c> hercios.
/// Con <c>L = 0,64·fs/B</c> el ancho a −3 dB es <c>B</c>. Es el filtro estrecho ajustable.
/// </para>
/// <para>
/// La potencia se entrega en <b>cuadros de 2 ms</b>, que es la resolución con que se miden los
/// tiempos (a 60 WPM un punto son 20 ms: diez cuadros).
/// </para>
/// </remarks>
public sealed class EnvolventeCw
{
    /// <summary>Duración de un cuadro de envolvente.</summary>
    public const double MilisegundosPorCuadro = 2.0;

    private readonly double _fs;
    private readonly int _muestrasPorCuadro;
    private double _cosPaso;
    private double _senPaso;
    private double _re = 1;
    private double _im;
    private long _cuenta;

    private double[] _anillo1Re = [];
    private double[] _anillo1Im = [];
    private double[] _anillo2Re = [];
    private double[] _anillo2Im = [];
    private int _largo;
    private int _i1;
    private int _i2;
    private double _s1Re;
    private double _s1Im;
    private double _s2Re;
    private double _s2Im;

    private int _vueltas1;
    private int _vueltas2;
    private int _enCuadro;
    private double _acumulado;

    /// <summary>Monta el filtro.</summary>
    /// <param name="frecuenciaDeMuestreo">Muestras por segundo del audio que llega.</param>
    /// <param name="tonoHz">Tono a seguir.</param>
    /// <param name="anchoHz">Ancho del filtro a −3 dB.</param>
    public EnvolventeCw(double frecuenciaDeMuestreo, double tonoHz, double anchoHz)
    {
        _fs = frecuenciaDeMuestreo;
        _muestrasPorCuadro = Math.Max(1, (int)Math.Round(_fs * MilisegundosPorCuadro / 1000));
        Afinar(tonoHz);
        CambiarAncho(anchoHz);
    }

    /// <summary>Tono que se sigue.</summary>
    public double TonoHz { get; private set; }

    /// <summary>Ancho a −3 dB.</summary>
    public double AnchoHz { get; private set; }

    /// <summary>Retraso del filtro, en ms (lo que tarda en subir a media altura).</summary>
    public double RetrasoMs => _largo / _fs * 1000;

    /// <summary>Mueve el oscilador a otro tono sin vaciar el filtro.</summary>
    public void Afinar(double tonoHz)
    {
        TonoHz = tonoHz;
        var paso = 2 * Math.PI * tonoHz / _fs;
        _cosPaso = Math.Cos(paso);
        _senPaso = Math.Sin(paso);
    }

    /// <summary>Cambia el ancho del filtro (vacía el filtro).</summary>
    public void CambiarAncho(double anchoHz)
    {
        AnchoHz = anchoHz;
        _largo = Math.Max(2, (int)Math.Round(0.64 * _fs / anchoHz));
        _anillo1Re = new double[_largo];
        _anillo1Im = new double[_largo];
        _anillo2Re = new double[_largo];
        _anillo2Im = new double[_largo];
        _i1 = _i2 = 0;
        _s1Re = _s1Im = _s2Re = _s2Im = 0;
    }

    /// <summary>Mete una muestra.</summary>
    /// <param name="x">La muestra.</param>
    /// <param name="potencia">La potencia media del cuadro, si se ha completado uno.</param>
    /// <returns>Si se ha completado un cuadro.</returns>
    public bool Anadir(float x, out double potencia)
    {
        // Bajada a cero: x · conj(fasor).
        var mr = x * _re;
        var mi = -x * _im;
        var nr = (_re * _cosPaso) - (_im * _senPaso);
        var ni = (_re * _senPaso) + (_im * _cosPaso);
        _re = nr;
        _im = ni;

        if ((++_cuenta & 0x3FF) == 0)
        {
            var modulo = Math.Sqrt((_re * _re) + (_im * _im));
            _re /= modulo;
            _im /= modulo;
        }

        // Primera media móvil.
        _s1Re += mr - _anillo1Re[_i1];
        _s1Im += mi - _anillo1Im[_i1];
        _anillo1Re[_i1] = mr;
        _anillo1Im[_i1] = mi;
        if (++_i1 == _largo)
        {
            _i1 = 0;
            if ((++_vueltas1 & 63) == 0) Recalcular(_anillo1Re, _anillo1Im, ref _s1Re, ref _s1Im);
        }

        var y1r = _s1Re / _largo;
        var y1i = _s1Im / _largo;

        // Segunda.
        _s2Re += y1r - _anillo2Re[_i2];
        _s2Im += y1i - _anillo2Im[_i2];
        _anillo2Re[_i2] = y1r;
        _anillo2Im[_i2] = y1i;
        if (++_i2 == _largo)
        {
            _i2 = 0;
            if ((++_vueltas2 & 63) == 0) Recalcular(_anillo2Re, _anillo2Im, ref _s2Re, ref _s2Im);
        }

        var yr = _s2Re / _largo;
        var yi = _s2Im / _largo;

        // Un tono de amplitud A justo en el centro da A²/4.
        _acumulado += (yr * yr) + (yi * yi);
        if (++_enCuadro < _muestrasPorCuadro)
        {
            potencia = 0;
            return false;
        }

        potencia = _acumulado / _enCuadro;
        _acumulado = 0;
        _enCuadro = 0;
        return true;
    }

    /// <summary>Las sumas corridas acumulan error de redondeo: de vez en cuando se rehacen.</summary>
    private static void Recalcular(double[] re, double[] im, ref double sRe, ref double sIm)
    {
        double a = 0, b = 0;
        for (var k = 0; k < re.Length; k++)
        {
            a += re[k];
            b += im[k];
        }

        sRe = a;
        sIm = b;
    }
}
