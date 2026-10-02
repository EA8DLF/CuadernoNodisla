# Código de terceros en el Cuaderno NODISLA

Lo que hay dentro del programa escrito o derivado de trabajo ajeno, con su licencia y su
procedencia. Las bibliotecas que se usan como paquetes NuGet llevan su propia licencia en el
paquete y no se repiten aquí.

## WDSP — Warren Pratt, NR0V (GPL v2 o posterior)

Portado a C# desde el código de WDSP que distribuye Thetis (`Project Files/Source/wdsp`,
repositorio `ramdor/Thetis`). Cada fichero portado conserva en su cabecera el aviso de
copyright y de licencia del original.

| Fichero del Cuaderno | Original de WDSP | Copyright |
|---|---|---|
| `src/Nodisla.Cuaderno.Audio/Procesado/FiltroLmsAdaptativo.cs` | `anf.c` (ANF) y `anr.c` (ANR, «NR1») | (C) 2012, 2013 Warren Pratt, NR0V |
| `src/Nodisla.Cuaderno.Audio/Procesado/ReductorEspectral.cs` | `emnr.c` (EMNR, «NR2»): estimador de ruido por probabilidad de presencia (`LambdaDs`), ganancias MMSE gaussiana y MMSE-LSA (`calc_gain`, métodos 0 y 1), filtro de artefactos (`aepf`), ventana, funciones de Bessel I0/I1 y `e1xb` | (C) 2015, 2025 Warren Pratt, NR0V |

Cambios respecto al original: tramas de unos 10 ms en lugar de 4.096 puntos (para no retrasar
la escucha), solape de 4 como en WDSP, transformada propia en lugar de FFTW, un nivel que mezcla
la ganancia con la unidad, el alisado de `aepf` escalado al tamaño de trama, y fuera las tablas
entrenadas (`GG`, `GGS`, `zetaHat`), el estimador de mínimos y el ruido de confort (`post2`). En
el filtro LMS, el paso y la fuga son los que pone Thetis al arrancar (`radio.cs`).

Las funciones de Bessel y la integral exponencial siguen, como cita `emnr.c`, a M. Abramowitz e
I. Stegun (*Handbook of Mathematical Functions*, 1964) y a S. Zhang y J. Jin (*Computation of
Special Functions*, 1996).

> This program is free software; you can redistribute it and/or modify it under the terms of
> the GNU General Public License as published by the Free Software Foundation; either version 2
> of the License, or (at your option) any later version. This program is distributed in the hope
> that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
> MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
> more details.

**Consecuencia:** un programa que incluye código GPL v2+ y se distribuye tiene que
distribuirse bajo una licencia compatible (GPL v2 o posterior, con su código fuente). Ver
`docs/19-analisis-thetis.md`, apartado 1.

## Audio EQ Cookbook — Robert Bristow-Johnson

`src/Nodisla.Cuaderno.Audio/Procesado/FiltroBicuadratico.cs` usa las fórmulas de los filtros de
segundo orden (paso alto, estanterías y campana) publicadas por R. Bristow-Johnson en el «Audio
EQ Cookbook», de uso libre. No se copia código: solo las fórmulas.

## RNNoise — no se usa

Se estudió (BSD-3, Xiph.Org / Jean-Marc Valin) y se dejó fuera: habría que llevar una DLL nativa
o portar la red con sus pesos. El reductor espectral portado de WDSP cubre la necesidad.
