// Portado a C# de WDSP: anf.c y anr.c.
// Copyright (C) 2012, 2013 Warren Pratt, NR0V.
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

/// <summary>Que se queda el filtro adaptativo de lo que predice.</summary>
public enum UsoDelFiltroLms
{
    /// <summary>
    /// Notch automatico (ANF de WDSP): sale el error de prediccion. Lo predecible —una
    /// portadora, un pitido— se resta; la voz, que cambia, pasa.
    /// </summary>
    Notch,

    /// <summary>
    /// Reductor de ruido (ANR o NR1 de WDSP): sale la prediccion. Lo predecible —voz y tonos—
    /// pasa; el ruido blanco, que no se puede predecir, se queda fuera.
    /// </summary>
    Reductor,
}

/// <summary>
/// Predictor lineal LMS normalizado con fuga adaptativa: el ANF y el NR1 de WDSP, que son el
/// mismo filtro con distinta salida.
/// </summary>
/// <remarks>
/// <para>
/// Predice cada muestra a partir de las de hace <c>retardo</c> en adelante con <c>coeficientes</c>
/// pesos. La fuga (<c>gamma</c>) se ajusta sola para que el filtro no se dispare con senales
/// fuertes ni se quede dormido con las debiles. Los valores son los de WDSP (RXA.c) con el paso
/// y la fuga que pone Thetis.
/// </para>
/// <para>
/// No anade latencia: la salida de cada muestra sale con esa misma muestra.
/// </para>
/// </remarks>
public sealed class FiltroLmsAdaptativo
{
    private const int TamanoDeLinea = 2048;
    private const int Mascara = TamanoDeLinea - 1;

    private readonly UsoDelFiltroLms _uso;
    private readonly int _coeficientes;
    private readonly int _retardo;
    private readonly double _dosMu;
    private readonly double _gamma;
    private readonly double _lidxInicial;
    private readonly double _lidxMinimo;
    private readonly double _lidxMaximo;
    private readonly double _ngammaInicial;
    private readonly double _multiplicador;
    private readonly double _subida;
    private readonly double _bajada;

    private readonly double[] _d = new double[TamanoDeLinea];
    private readonly double[] _w = new double[TamanoDeLinea];
    private int _indice;
    private double _lidx;
    private double _ngamma;

    /// <summary>Crea el filtro con los valores de WDSP para cada uso.</summary>
    /// <param name="uso">Notch o reductor.</param>
    public FiltroLmsAdaptativo(UsoDelFiltroLms uso)
    {
        _uso = uso;
        _coeficientes = 64;
        _retardo = 16;

        // Paso y fuga: los que pone Thetis al arrancar (radio.cs: nr_gain 16e-4, nr_leak 10e-7,
        // anf_gain 10e-4, anf_leak 1e-7). Los de create_anr/create_anf en RXA.c (1e-4 y 0,1)
        // son de partida y Thetis los cambia siempre; con ellos el filtro tarda decenas de
        // segundos en aprender.
        _dosMu = uso == UsoDelFiltroLms.Reductor ? 16e-4 : 10e-4;
        _gamma = uso == UsoDelFiltroLms.Reductor ? 10e-7 : 1e-7;
        _multiplicador = 6.25e-10;
        _subida = 1.0;
        _bajada = 3.0;

        if (uso == UsoDelFiltroLms.Notch)
        {
            _lidxInicial = 1.0;
            _lidxMinimo = 0.0;
            _lidxMaximo = 200.0;
            _ngammaInicial = 6.25e-12;
        }
        else
        {
            _lidxInicial = 120.0;
            _lidxMinimo = 120.0;
            _lidxMaximo = 200.0;
            _ngammaInicial = 0.001;
        }

        Reiniciar();
    }

    /// <summary>Olvida lo aprendido (flush_anf / flush_anr).</summary>
    public void Reiniciar()
    {
        Array.Clear(_d);
        Array.Clear(_w);
        _indice = 0;
        _lidx = _lidxInicial;
        _ngamma = _ngammaInicial;
    }

    /// <summary>Filtra el bloque en el sitio (xanf / xanr).</summary>
    public void Procesar(Span<float> muestras)
    {
        for (var i = 0; i < muestras.Length; i++)
        {
            _d[_indice] = muestras[i];

            double y = 0;
            double sigma = 0;
            for (var j = 0; j < _coeficientes; j++)
            {
                var idx = (_indice + j + _retardo) & Mascara;
                y += _w[j] * _d[idx];
                sigma += _d[idx] * _d[idx];
            }

            var invSigp = 1.0 / (sigma + 1e-10);
            var error = _d[_indice] - y;

            muestras[i] = (float)(_uso == UsoDelFiltroLms.Notch ? error : y);

            var nel = Math.Abs(error * (1.0 - (_dosMu * sigma * invSigp)));
            var nev = Math.Abs(_d[_indice] - ((1.0 - (_dosMu * _ngamma)) * y) - (_dosMu * error * sigma * invSigp));
            if (nev < nel)
            {
                _lidx = Math.Min(_lidx + _subida, _lidxMaximo);
            }
            else
            {
                _lidx = Math.Max(_lidx - _bajada, _lidxMinimo);
            }

            _ngamma = _gamma * (_lidx * _lidx) * (_lidx * _lidx) * _multiplicador;

            var c0 = 1.0 - (_dosMu * _ngamma);
            var c1 = _dosMu * error * invSigp;
            for (var j = 0; j < _coeficientes; j++)
            {
                var idx = (_indice + j + _retardo) & Mascara;
                _w[j] = (c0 * _w[j]) + (c1 * _d[idx]);
            }

            _indice = (_indice + Mascara) & Mascara;
        }
    }
}
