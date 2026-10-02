// Portado a C# de WDSP: emnr.c (EMNR, el «NR2» de Thetis).
// Copyright (C) 2015, 2025 Warren Pratt, NR0V.
// Funciones de Bessel e integral exponencial: M. Abramowitz e I. Stegun (1964) y S. Zhang y
// J. Jin (1996), segun las cita emnr.c.
// Adaptacion a C# para el Cuaderno NODISLA (C) 2026 EA8DLF.
//
// This program is free software; you can redistribute it and/or modify it under the terms of
// the GNU General Public License as published by the Free Software Foundation; either version 2
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY;
// without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See
// the GNU General Public License for more details. Ver TERCEROS.md.

namespace Nodisla.Cuaderno.Audio.Procesado;

/// <summary>Como calcula el reductor espectral la ganancia de cada franja.</summary>
public enum GananciaEspectral
{
    /// <summary>
    /// Estimador MMSE de la amplitud con modelo gaussiano de la voz y probabilidad de presencia
    /// (gain_method 0 de WDSP). Mas suave: deja algo de ruido pero casi no deja «musiquilla».
    /// </summary>
    Gaussiana,

    /// <summary>
    /// Estimador MMSE del logaritmo de la amplitud (gain_method 1 de WDSP). Quita mas ruido.
    /// </summary>
    Logaritmica,
}

/// <summary>
/// Reductor de ruido espectral: el EMNR de WDSP («NR2» en Thetis), con tramas cortas para que
/// la escucha no se retrase.
/// </summary>
/// <remarks>
/// <para>
/// Por cada trama: ventana, transformada, estimacion del ruido de cada franja por probabilidad
/// de presencia de voz (npe_method 1 de WDSP, de Gerkmann y Hendriks), ganancia por franja con
/// relacion senal-ruido «a priori» dirigida por decision, y el filtro de artefactos de WDSP
/// (aepf), que alisa la ganancia cuando hay poca voz para que no quede la «musiquilla».
/// </para>
/// <para>
/// <b>Lo que cambia respecto a WDSP.</b> WDSP usa tramas de 4.096 puntos (85 ms de retraso).
/// Aqui son de unos 10 ms (512 a 48 kHz) con el mismo solape de cuatro; todas las constantes de
/// tiempo de WDSP estan escritas en segundos, asi que valen igual. El alisado de aepf se mide en
/// franjas y se escala con el tamano. Ademas hay un <see cref="Nivel"/>: mezcla la ganancia
/// calculada con la unidad, para elegir cuanto se quita. Se dejan fuera las tablas entrenadas
/// (gain_method 2 y 3), el estimador de minimos (npe 0) y el ruido de confort (post2).
/// </para>
/// </remarks>
public sealed class ReductorEspectral
{
    private const int Solape = 4;

    private int _frecuencia;
    private int _tamano;
    private int _paso;
    private int _franjas;
    private TransformadaCompleja? _fft;
    private double[] _ventana = [];
    private double _ganancia;

    private double[] _entrada = [];
    private int _escritura;
    private int _llevadas;
    private double[] _salidaAcumulada = [];
    private float[] _salidaLista = [];
    private int _salidaLeida;
    private int _salidaDisponible;

    private double[] _re = [];
    private double[] _im = [];
    private double[] _mascara = [];
    private double[] _mascaraAlisada = [];
    private double[] _lambdaY = [];
    private double[] _lambdaD = [];
    private double[] _gammaAnterior = [];
    private double[] _mascaraAnterior = [];

    // Estimador por probabilidad de presencia (nps de WDSP).
    private double[] _sigma2N = [];
    private double[] _pBarra = [];
    private double _alfaPotencia;
    private double _alfaPBarra;
    private double _epsH1;
    private double _epsH1r;

    // Ganancia (g de WDSP).
    private double _alfa;
    private const double PisoEps = 1.0e-300;
    private const double GammaMaxima = 40.0;
    private static readonly double XiMinima = Math.Pow(10.0, -40.0 / 10.0);
    private const double Q = 0.2;
    private const double GananciaMaxima = 10000.0;
    private static readonly double Gf1p5 = Math.Sqrt(Math.PI) / 2.0;

    // Eliminacion de artefactos (ae de WDSP).
    private const double UmbralZeta = 0.75;
    private double _psi;

    /// <summary>Cuanto se quita, de 0 (nada) a 1 (todo lo que calcula el estimador).</summary>
    public double Nivel { get; set; } = 0.7;

    /// <summary>Metodo de ganancia.</summary>
    public GananciaEspectral Metodo { get; set; } = GananciaEspectral.Gaussiana;

    /// <summary>Muestras de retraso que anade a una frecuencia dada.</summary>
    public static int RetrasoEnMuestras(int frecuencia) => TamanoPara(frecuencia) - 1;

    /// <summary>Tamano de trama para una frecuencia: la potencia de dos de unos 10 ms.</summary>
    public static int TamanoPara(int frecuencia)
    {
        var objetivo = Math.Max(64, (int)(frecuencia * 0.012));
        var tamano = 64;
        while (tamano * 2 <= objetivo) tamano *= 2;
        return tamano;
    }

    /// <summary>Olvida todo; la siguiente llamada empieza de cero.</summary>
    public void Reiniciar() => _frecuencia = 0;

    /// <summary>Procesa el bloque en el sitio. Sale con <see cref="RetrasoEnMuestras"/> de retraso.</summary>
    public void Procesar(Span<float> muestras, int frecuencia)
    {
        if (frecuencia <= 0) return;
        if (frecuencia != _frecuencia) Preparar(frecuencia);

        var nivel = Math.Clamp(Nivel, 0.0, 1.0);
        var metodo = Metodo;

        for (var i = 0; i < muestras.Length; i++)
        {
            // Entrada: anillo con los ultimos «tamano» puntos; _escritura apunta al mas viejo.
            _entrada[_escritura] = muestras[i];
            _escritura = (_escritura + 1) & (_tamano - 1);

            if (++_llevadas == _paso)
            {
                _llevadas = 0;
                Trama(nivel, metodo);
            }

            muestras[i] = _salidaLeida < _salidaDisponible ? _salidaLista[_salidaLeida++] : 0f;
        }
    }

    private void Preparar(int frecuencia)
    {
        _frecuencia = frecuencia;
        _tamano = TamanoPara(frecuencia);
        _paso = _tamano / Solape;
        _franjas = (_tamano / 2) + 1;
        _fft = new TransformadaCompleja(_tamano);

        // Ventana de WDSP (wintype 0): raiz de Hamming, normalizada por su ganancia coherente.
        _ventana = new double[_tamano];
        var suma = 0.0;
        for (var i = 0; i < _tamano; i++)
        {
            _ventana[i] = Math.Sqrt(0.54 - (0.46 * Math.Cos(2.0 * Math.PI * i / _tamano)));
            suma += _ventana[i];
        }

        var correccion = _tamano / suma;
        for (var i = 0; i < _tamano; i++) _ventana[i] *= correccion;

        // Ganancia de salida para que con mascara uno salga lo mismo que entra: la suma de los
        // cuadrados de la ventana solapada, por la inversa sin normalizar.
        var solapada = 0.0;
        for (var i = 0; i < _tamano; i++) solapada += _ventana[i] * _ventana[i];
        solapada /= _paso;
        _ganancia = 1.0 / (_tamano * solapada);

        _entrada = new double[_tamano];
        _escritura = 0;
        _llevadas = 0;
        _salidaAcumulada = new double[_tamano];
        _salidaLista = new float[_paso];
        _salidaLeida = 0;
        _salidaDisponible = 0;

        _re = new double[_tamano];
        _im = new double[_tamano];
        _mascara = new double[_franjas];
        _mascaraAlisada = new double[_franjas];
        _lambdaY = new double[_franjas];
        _lambdaD = new double[_franjas];
        _gammaAnterior = new double[_franjas];
        _mascaraAnterior = new double[_franjas];
        Array.Fill(_gammaAnterior, 1.0);
        Array.Fill(_mascaraAnterior, 1.0);

        var incrPorFrecuencia = (double)_paso / frecuencia;
        _alfa = Math.Exp(-incrPorFrecuencia / (-128.0 / 8000.0 / Math.Log(0.985)));

        _alfaPotencia = Math.Exp(-incrPorFrecuencia / (-128.0 / 8000.0 / Math.Log(0.8)));
        _alfaPBarra = Math.Exp(-incrPorFrecuencia / (-128.0 / 8000.0 / Math.Log(0.9)));
        _epsH1 = Math.Pow(10.0, 15.0 / 10.0);
        _epsH1r = _epsH1 / (1.0 + _epsH1);
        _sigma2N = new double[_franjas];
        _pBarra = new double[_franjas];
        Array.Fill(_sigma2N, 0.5);
        Array.Fill(_pBarra, 0.5);

        // aepf: 20 franjas de 11,7 Hz en WDSP; aqui, las mismas en hercios.
        _psi = 20.0 * _tamano / 4096.0;
    }

    private void Trama(double nivel, GananciaEspectral metodo)
    {
        for (var i = 0; i < _tamano; i++)
        {
            _re[i] = _ventana[i] * _entrada[(_escritura + i) & (_tamano - 1)];
            _im[i] = 0;
        }

        _fft!.Directa(_re, _im);
        CalcularGanancia(metodo);

        for (var k = 0; k < _franjas; k++)
        {
            var g = 1.0 - (nivel * (1.0 - _mascara[k]));
            _re[k] *= g;
            _im[k] *= g;
        }

        // Simetria hermitica para que la inversa sea real.
        for (var k = _franjas; k < _tamano; k++)
        {
            _re[k] = _re[_tamano - k];
            _im[k] = -_im[_tamano - k];
        }

        _fft.Inversa(_re, _im);

        for (var i = 0; i < _tamano; i++) _salidaAcumulada[i] += _ganancia * _ventana[i] * _re[i];

        // Las primeras «paso» muestras ya tienen todas sus tramas: salen.
        for (var i = 0; i < _paso; i++) _salidaLista[i] = (float)_salidaAcumulada[i];
        Array.Copy(_salidaAcumulada, _paso, _salidaAcumulada, 0, _tamano - _paso);
        Array.Clear(_salidaAcumulada, _tamano - _paso, _paso);
        _salidaLeida = 0;
        _salidaDisponible = _paso;
    }

    private void CalcularGanancia(GananciaEspectral metodo)
    {
        for (var k = 0; k < _franjas; k++)
        {
            _lambdaY[k] = (_re[k] * _re[k]) + (_im[k] * _im[k]);
        }

        EstimarRuido();

        for (var k = 0; k < _franjas; k++)
        {
            var lambdaD = Math.Max(_lambdaD[k], 1e-30);
            var gamma = Math.Min(_lambdaY[k] / lambdaD, GammaMaxima);
            var epsHat = (_alfa * _mascaraAnterior[k] * _mascaraAnterior[k] * _gammaAnterior[k])
                + ((1.0 - _alfa) * Math.Max(gamma - 1.0, PisoEps));
            double mascara;

            if (metodo == GananciaEspectral.Logaritmica)
            {
                var ehr = epsHat / (1.0 + epsHat);
                var v = ehr * gamma;
                mascara = Math.Min(ehr * Math.Exp(Math.Min(700.0, 0.5 * E1xb(v))), GananciaMaxima);
            }
            else
            {
                epsHat = Math.Max(epsHat, XiMinima);
                var v = epsHat / (1.0 + epsHat) * gamma;
                mascara = gamma <= 0
                    ? 0.0
                    : Gf1p5 * Math.Sqrt(v) / gamma * Math.Exp(-0.5 * v)
                        * (((1.0 + v) * BesselI0(0.5 * v)) + (v * BesselI1(0.5 * v)));
                var v2 = Math.Min(v, 700.0);
                var eta = mascara * mascara * _lambdaY[k] / lambdaD;
                var eps = eta / (1.0 - Q);
                var witchHat = (1.0 - Q) / Q * Math.Exp(v2) / (1.0 + eps);
                mascara *= witchHat / (1.0 + witchHat);
                mascara = Math.Min(mascara, GananciaMaxima);
            }

            if (double.IsNaN(mascara)) mascara = 0.01;
            _mascara[k] = mascara;
            _gammaAnterior[k] = gamma;
            _mascaraAnterior[k] = mascara;
        }

        EliminarArtefactos();

        for (var k = 0; k < _franjas; k++) _mascara[k] = Math.Clamp(_mascara[k], 0.0, 1.0);
    }

    /// <summary>LambdaDs de WDSP: ruido por probabilidad de presencia de voz.</summary>
    private void EstimarRuido()
    {
        for (var k = 0; k < _franjas; k++)
        {
            var sigma = Math.Max(_sigma2N[k], 1e-30);
            var ph1y = 1.0 / (1.0 + ((1.0 + _epsH1) * Math.Exp(-_epsH1r * _lambdaY[k] / sigma)));
            _pBarra[k] = (_alfaPBarra * _pBarra[k]) + ((1.0 - _alfaPBarra) * ph1y);
            if (_pBarra[k] > 0.99) ph1y = Math.Min(ph1y, 0.99);
            var en2y = ((1.0 - ph1y) * _lambdaY[k]) + (ph1y * sigma);
            _sigma2N[k] = Math.Max((_alfaPotencia * sigma) + ((1.0 - _alfaPotencia) * en2y), 1e-30);
            _lambdaD[k] = _sigma2N[k];
        }
    }

    /// <summary>aepf de WDSP: alisa la mascara en frecuencia cuando queda poca senal.</summary>
    private void EliminarArtefactos()
    {
        double antes = 0, despues = 0;
        for (var k = 0; k < _franjas; k++)
        {
            antes += _lambdaY[k];
            despues += _mascara[k] * _mascara[k] * _lambdaY[k];
        }

        var zeta = antes > 0 ? despues / antes : 1.0;
        var zetaT = zeta >= UmbralZeta ? 1.0 : zeta;
        var n = zetaT == 1.0 ? 1 : 1 + (2 * (int)(0.5 + (_psi * (1.0 - (zetaT / UmbralZeta)))));
        var mitad = n / 2;
        if (mitad == 0) return;

        var m = _franjas;
        for (var k = 0; k < m; k++)
        {
            int desde, hasta;
            if (k < mitad)
            {
                desde = 0;
                hasta = 2 * k;
            }
            else if (k < m - mitad)
            {
                desde = k - mitad;
                hasta = k + mitad;
            }
            else
            {
                desde = (2 * k) - m + 1;
                hasta = m - 1;
            }

            var suma = 0.0;
            for (var j = desde; j <= hasta; j++) suma += _mascara[j];
            _mascaraAlisada[k] = suma / (hasta - desde + 1);
        }

        Array.Copy(_mascaraAlisada, _mascara, m);
    }

    private static double BesselI0(double x)
    {
        if (x == 0.0) return 1.0;
        x = Math.Abs(x);
        if (x <= 3.75)
        {
            var p = x / 3.75;
            p *= p;
            return (((((((((((0.0045813 * p) + 0.0360768) * p) + 0.2659732) * p) + 1.2067492) * p) + 3.0899424) * p) + 3.5156229) * p) + 1.0;
        }

        var q = 3.75 / x;
        return Math.Exp(x) / Math.Sqrt(x)
            * ((((((((((((((((0.00392377 * q) - 0.01647633) * q) + 0.02635537) * q) - 0.02057706) * q) + 0.00916281) * q) - 0.00157565) * q) + 0.00225319) * q) + 0.01328592) * q) + 0.39894228);
    }

    private static double BesselI1(double x)
    {
        if (x == 0.0) return 0.0;
        x = Math.Abs(x);
        if (x <= 3.75)
        {
            var p = x / 3.75;
            p *= p;
            return x * ((((((((((((0.00032411 * p) + 0.00301532) * p) + 0.02658733) * p) + 0.15084934) * p) + 0.51498869) * p) + 0.87890594) * p) + 0.5);
        }

        var q = 3.75 / x;
        return Math.Exp(x) / Math.Sqrt(x)
            * ((((((((((((((((-0.00420059 * q) + 0.01787654) * q) - 0.02895312) * q) + 0.02282967) * q) - 0.01031555) * q) + 0.00163801) * q) - 0.00362018) * q) - 0.03988024) * q) + 0.39894228);
    }

    private static double E1xb(double x)
    {
        if (x == 0.0) return 1.0e300;
        if (x <= 1.0)
        {
            var e1 = 1.0;
            var r = 1.0;
            for (var k = 1; k <= 25; k++)
            {
                r = -r * k * x / ((k + 1.0) * (k + 1.0));
                e1 += r;
                if (Math.Abs(r) <= Math.Abs(e1) * 1.0e-15) break;
            }

            const double Euler = 0.5772156649015328;
            return -Euler - Math.Log(x) + (x * e1);
        }

        var m = 20 + (int)(80.0 / x);
        var t0 = 0.0;
        for (var k = m; k >= 1; k--) t0 = k / (1.0 + (k / (x + t0)));
        return Math.Exp(-x) * (1.0 / (x + t0));
    }
}
