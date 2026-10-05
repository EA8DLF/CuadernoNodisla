# Banco de medida de MSK144

Generado por `BancoMsk144Pruebas` el 2026-10-05 (UTC). **No editar a mano.**

Pings sintéticos sobre ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz. Cada
ventana de 15 s lleva un solo ping de tantas tramas de 72 ms como diga la columna, con un
mensaje al azar entre ocho, en un instante al azar y con la portadora desviada hasta ±80 Hz.
Semilla fija: la medida se repite igual en cualquier máquina.

Las cifras son con el **código LDPC(128,90) de verdad** (tabla en `Tablas/tablas-msk144.txt`,
con su procedencia) y con el (32,16) de los mensajes cortos. El decodificador demodula
coherentemente y suma en fase hasta siete tramas consecutivas; la columna «Sólo por
promediado» cuenta los pings que no salieron con una trama sola.

El artículo del protocolo (QEX jul/ago 2017) da, para una trama sola con sincronismo perfecto,
casi el 100 % a 0 dB y el 40 % a −1,5 dB; el promediado de N tramas mueve la curva 10·log N
hacia abajo (8,5 dB con siete). Eso es lo que hay que comparar con estas tablas.

**Falsos** tiene que ser cero en todas las franjas, y con ruido puro no puede salir nada. La
columna «Palabras válidas» es el riesgo latente: cada una es una tirada del CRC de 13 bits, y
por eso hay un freno de errores duros antes del CRC (ver `ErroresDurosMaximos`).

Tiempos de **procesador** de una compilación **de publicación (Release)**; la ventana entera son 15.000 ms.

### Ping de una trama (72 ms), por relación señal-ruido

| S/R (dB) | Tramas del ping | Ventanas | Recuperados | % | Falsos | Palabras válidas | Rechazadas | Sólo por promediado | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 6 | 1 | 12 | 9 | 75,0 | 0 | 151 | 142 | 0 | -0,9 | 1341 |
| 4 | 1 | 12 | 6 | 50,0 | 0 | 130 | 124 | 0 | -0,5 | 1322 |
| 2 | 1 | 12 | 7 | 58,3 | 0 | 92 | 85 | 0 | -0,1 | 1313 |
| 0 | 1 | 12 | 1 | 8,3 | 0 | 72 | 71 | 0 | +1,0 | 1307 |
| -2 | 1 | 12 | 0 | 0,0 | 0 | 60 | 60 | 0 | 00 | 1309 |
| -4 | 1 | 12 | 0 | 0,0 | 0 | 55 | 55 | 0 | 00 | 1315 |
| -6 | 1 | 12 | 0 | 0,0 | 0 | 50 | 50 | 0 | 00 | 1306 |

### Ping de siete tramas (504 ms), por relación señal-ruido

| S/R (dB) | Tramas del ping | Ventanas | Recuperados | % | Falsos | Palabras válidas | Rechazadas | Sólo por promediado | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 7 | 12 | 10 | 83,3 | 0 | 248 | 216 | 2 | +0,6 | 1370 |
| -2 | 7 | 12 | 11 | 91,7 | 0 | 135 | 110 | 10 | +0,5 | 1329 |
| -4 | 7 | 12 | 8 | 66,7 | 0 | 85 | 73 | 8 | +0,8 | 1316 |
| -6 | 7 | 12 | 4 | 33,3 | 0 | 60 | 56 | 4 | +1,3 | 1310 |
| -8 | 7 | 12 | 0 | 0,0 | 0 | 63 | 63 | 0 | 00 | 1310 |
| -10 | 7 | 12 | 0 | 0,0 | 0 | 65 | 65 | 0 | 00 | 1311 |
| -12 | 7 | 12 | 0 | 0,0 | 0 | 60 | 60 | 0 | 00 | 1311 |

### A 0 dB, por duración del ping

| S/R (dB) | Tramas del ping | Ventanas | Recuperados | % | Falsos | Palabras válidas | Rechazadas | Sólo por promediado | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 1 | 12 | 1 | 8,3 | 0 | 74 | 73 | 0 | +2,0 | 1314 |
| 0 | 2 | 12 | 5 | 41,7 | 0 | 105 | 99 | 2 | +0,4 | 1315 |
| 0 | 3 | 12 | 9 | 75,0 | 0 | 118 | 107 | 3 | +0,1 | 1315 |
| 0 | 5 | 12 | 10 | 83,3 | 0 | 192 | 165 | 1 | +0,5 | 1345 |
| 0 | 7 | 12 | 12 | 100,0 | 0 | 253 | 209 | 1 | +0,8 | 1372 |
| 0 | 14 | 12 | 12 | 100,0 | 0 | 368 | 291 | 0 | +0,8 | 1415 |

### A −6 dB, por duración del ping

| S/R (dB) | Tramas del ping | Ventanas | Recuperados | % | Falsos | Palabras válidas | Rechazadas | Sólo por promediado | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -6 | 1 | 12 | 0 | 0,0 | 0 | 50 | 50 | 0 | 00 | 1310 |
| -6 | 2 | 12 | 0 | 0,0 | 0 | 59 | 59 | 0 | 00 | 1315 |
| -6 | 3 | 12 | 0 | 0,0 | 0 | 62 | 62 | 0 | 00 | 1314 |
| -6 | 5 | 12 | 3 | 25,0 | 0 | 76 | 73 | 3 | +1,3 | 1313 |
| -6 | 7 | 12 | 3 | 25,0 | 0 | 56 | 52 | 3 | +0,7 | 1306 |
| -6 | 14 | 12 | 5 | 41,7 | 0 | 74 | 65 | 5 | +0,8 | 1319 |

### Ruido puro

60 ventanas de 15 s sin señal: **0 mensajes**, 294 palabras válidas del corrector, 294 rechazadas por los frenos y el CRC, 1313 ms de CPU por ventana.

