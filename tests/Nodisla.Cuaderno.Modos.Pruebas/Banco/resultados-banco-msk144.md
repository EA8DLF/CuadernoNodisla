# Banco de medida de MSK144

Generado por `BancoMsk144Pruebas` el 2026-09-27 (UTC). **No editar a mano.**

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
| 6 | 1 | 12 | 9 | 75,0 | 0 | 140 | 131 | 0 | -0,9 | 1612 |
| 4 | 1 | 12 | 6 | 50,0 | 0 | 121 | 115 | 0 | -0,5 | 1581 |
| 2 | 1 | 12 | 7 | 58,3 | 0 | 94 | 87 | 0 | -0,1 | 1578 |
| 0 | 1 | 12 | 1 | 8,3 | 0 | 65 | 64 | 0 | +1,0 | 1570 |
| -2 | 1 | 12 | 0 | 0,0 | 0 | 57 | 57 | 0 | 00 | 1574 |
| -4 | 1 | 12 | 0 | 0,0 | 0 | 55 | 55 | 0 | 00 | 1589 |
| -6 | 1 | 12 | 0 | 0,0 | 0 | 50 | 50 | 0 | 00 | 1578 |

### Ping de siete tramas (504 ms), por relación señal-ruido

| S/R (dB) | Tramas del ping | Ventanas | Recuperados | % | Falsos | Palabras válidas | Rechazadas | Sólo por promediado | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 7 | 12 | 10 | 83,3 | 0 | 228 | 197 | 2 | +0,6 | 1651 |
| -2 | 7 | 12 | 11 | 91,7 | 0 | 136 | 112 | 10 | +0,4 | 1591 |
| -4 | 7 | 12 | 8 | 66,7 | 0 | 87 | 75 | 8 | +0,8 | 1603 |
| -6 | 7 | 12 | 4 | 33,3 | 0 | 58 | 54 | 4 | +1,3 | 1570 |
| -8 | 7 | 12 | 0 | 0,0 | 0 | 62 | 62 | 0 | 00 | 1572 |
| -10 | 7 | 12 | 0 | 0,0 | 0 | 62 | 62 | 0 | 00 | 1579 |
| -12 | 7 | 12 | 0 | 0,0 | 0 | 58 | 58 | 0 | 00 | 1570 |

### A 0 dB, por duración del ping

| S/R (dB) | Tramas del ping | Ventanas | Recuperados | % | Falsos | Palabras válidas | Rechazadas | Sólo por promediado | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 1 | 12 | 1 | 8,3 | 0 | 73 | 72 | 0 | +2,0 | 1574 |
| 0 | 2 | 12 | 6 | 50,0 | 0 | 102 | 95 | 3 | +0,7 | 1589 |
| 0 | 3 | 12 | 9 | 75,0 | 0 | 125 | 112 | 3 | +0,4 | 1577 |
| 0 | 5 | 12 | 11 | 91,7 | 0 | 195 | 167 | 1 | +0,6 | 1603 |
| 0 | 7 | 12 | 12 | 100,0 | 0 | 242 | 202 | 1 | +0,8 | 1639 |
| 0 | 14 | 12 | 12 | 100,0 | 0 | 390 | 314 | 0 | +0,8 | 1682 |

### A −6 dB, por duración del ping

| S/R (dB) | Tramas del ping | Ventanas | Recuperados | % | Falsos | Palabras válidas | Rechazadas | Sólo por promediado | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -6 | 1 | 12 | 0 | 0,0 | 0 | 51 | 51 | 0 | 00 | 1566 |
| -6 | 2 | 12 | 0 | 0,0 | 0 | 59 | 59 | 0 | 00 | 1569 |
| -6 | 3 | 12 | 0 | 0,0 | 0 | 58 | 58 | 0 | 00 | 1574 |
| -6 | 5 | 12 | 3 | 25,0 | 0 | 86 | 83 | 3 | +1,3 | 1586 |
| -6 | 7 | 12 | 3 | 25,0 | 0 | 54 | 50 | 3 | +0,3 | 1577 |
| -6 | 14 | 12 | 5 | 41,7 | 0 | 69 | 60 | 5 | +0,8 | 1583 |

### Ruido puro

60 ventanas de 15 s sin señal: **0 mensajes**, 294 palabras válidas del corrector, 294 rechazadas por los frenos y el CRC, 1576 ms de CPU por ventana.

