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

## Modos digitales — tablas de los códigos correctores

El módem propio (`src/Nodisla.Cuaderno.Modos/`) reimplementa cada protocolo desde cero en C#:
codificador, decodificador, sincronizador, modulador y CRC son escritos propios. Lo único que no
se puede escribir desde cero son las **constantes del código corrector** de cada protocolo (qué
bits suman cero, la generadora, la permutación de un código QRA): las elige quien diseña el
protocolo y no hay fórmula que las regenere. Son del mismo tipo que el polinomio de un CRC o los
grupos de Costas de FT8: o se tienen las mismas que todo el mundo, o no se decodifica a nadie. Esta
sección documenta de dónde sale cada tabla, fichero por fichero.

### FT8, FT4, FST4 y FST4W — licencia MIT, de `ft8_lib`

| Modo | Fichero del Cuaderno | Código | Fuente |
|---|---|---|---|
| FT8 / FT4 | `Tablas/tablas-ft8.txt` | LDPC(174,91) | [`kgoba/ft8_lib`](https://github.com/kgoba/ft8_lib), `ft8/constants.c` — MIT, Copyright (c) 2018 Kārlis Goba (YL3JG) |
| FST4 / FST4W | `Tablas/tablas-fst4.txt`, `Tablas/tablas-fst4w.txt` | LDPC(240,101) y LDPC(240,74) | [`howard0su/ft8_lib`](https://github.com/howard0su/ft8_lib), `ft8/fst4_constants.c` — MIT, misma raíz que el anterior |

Detalle completo, con la comprobación bit a bit hecha antes de convertir el fichero, en
`src/Nodisla.Cuaderno.Modos/Tablas/LEEME.md` y `LEEME-msk144-fst4.md`.

### MSK144, MSK40 y Q65 — constantes de WSJT-X, GPLv3

Para estos tres no existe ninguna copia con licencia permisiva de las tablas: hoy por hoy sólo
están publicadas en el código fuente de WSJT-X (K1JT, K9AN, IV3NWV y colaboradores, GPLv3). Se han
traído **únicamente los números** —las constantes del protocolo, que no son código— con esta
procedencia:

| Modo | Fichero del Cuaderno | Código | Ficheros de WSJT-X leídos (sólo para copiar las constantes) |
|---|---|---|---|
| MSK144 | `Tablas/tablas-msk144.txt` | LDPC(128,90) | `lib/ldpc_128_90_reordered_parity.f90`, `lib/ldpc_128_90_generator.f90` |
| MSK40 (mensajes cortos de MSK144) | `Tablas/tablas-msk40.txt` | LDPC(32,16) | `lib/bpdecode40.f90`, `lib/encode_msk40.f90` |
| Q65 | `Tablas/tablas-q65.txt` | QRA(65,15) sobre GF(64) | `lib/qra/q65/qra15_65_64_irr_e23.c`, `lib/qra/q65/genq65.f90`, `lib/qra/q65/q65.c` |

**No se ha copiado, traducido ni compilado una sola línea de código de WSJT-X.** Lo que se ha
leído de esos ficheros son exclusivamente las constantes numéricas (qué bits suman cero en cada
ecuación de paridad, la generadora, la permutación y los pesos del código QRA, las posiciones de
sincronismo, el polinomio del CRC), igual que se ha hecho siempre con el polinomio de un CRC
estándar. El razonamiento es el mismo que para la tabla de FT8 (que además es MIT): son datos de
un estándar de facto, no una expresión creativa de código.

**Comprobado el 05-10-2026, a petición expresa de EA8DLF, que no existe alternativa limpia.** Se
buscó explícitamente una fuente independiente con licencia permisiva (MIT/BSD/dominio público)
que publicara estas mismas tablas sin pasar por el código de WSJT-X, y no se encontró ninguna:

- El artículo de QEX de 2017 sobre MSK144 (Franke y Taylor) describe la versión antigua del
  protocolo (72 bits, CRC-8, LDPC(128,80)); no la de 77 bits/CRC-13/LDPC(128,90) que está en uso
  desde 2018 y es la de estas tablas. No se ha encontrado ningún artículo posterior con las
  tablas de la versión actual.
- El artículo de QEX de 2021 sobre Q65 (Taylor, Somerville, Franke y Palermo) describe el
  protocolo pero, según la documentación ya existente en `Tablas/LEEME-q65.md`, no publica la
  permutación ni los pesos del código QRA(65,15): esos los eligió quien diseñó el código con un
  programa de búsqueda y no hay fórmula ni tabla publicada que los regenere.
- `qracodes` ([`Microtelecom/qracodes`](https://github.com/Microtelecom/qracodes)), el paquete
  original de Nico Palermo (IV3NWV) del que WSJT-X tomó el código QRA de Q65, está en GitHub pero
  **sin ningún fichero de licencia**: no es de dominio público ni tiene licencia permisiva
  declarada, así que leerlo no resuelve nada (y además es, de hecho, el código original, no una
  reimplementación independiente).
- Se revisaron varios proyectos de terceros que dicen decodificar MSK144, MSK40 o Q65
  (`alexander-sholohov/msk144decoder`, `jl1nie/mfsk-core`, `iu8lmc/Decodium`, forks de WSJT-X como
  `sq9fve/wsjt-z` o `paulh002/wsjtx_lib`, el firmware de `g4eml/RP2040_Synth`). **Todos**, sin
  excepción, o enlazan WSJT-X como dependencia de compilación (p. ej. `msk144decoder` trae
  `deps/wsjtx` como submódulo git), o declaran su propio código como GPLv3 por depender de estas
  tablas (`mfsk-core`), o son simplemente una copia literal de los ficheros de WSJT-X con el mismo
  comentario de cabecera. Ninguno es una reimplementación independiente con licencia propia
  limpia: todos vuelven, en último término, a estos mismos ficheros de WSJT-X.

**Decisión de Jose (05-10-2026)**: se mantiene esta excepción documentada tal cual. MSK144, MSK40
y Q65 siguen en el programa con las constantes leídas de WSJT-X por las razones de arriba — no
hay fuente limpia hoy, y la alternativa (retirar los tres modos) se descarta mientras siga así. Si
algún día aparece una reimplementación independiente de estas tablas, se cambia la procedencia sin
tocar ni un bit del contenido (ver el caso ya resuelto de FT8/FST4/FST4W arriba).

Detalle completo de cada tabla, con las comprobaciones hechas antes de fiarse (dimensiones, pesos,
cruce generadora/paridad, ida y vuelta sin ruido), en `Tablas/LEEME-msk144-fst4.md` y
`Tablas/LEEME-q65.md`.
