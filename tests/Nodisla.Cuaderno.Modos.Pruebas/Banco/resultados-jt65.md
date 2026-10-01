# Banco de medida de JT65

Generado por `BancoJt65Pruebas` el 2026-09-27 (UTC). **No editar a mano.**

Señal sintética de JT65A con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz.
Cada ventana lleva un mensaje al azar entre ocho, con el tono de sincronismo al azar entre 500
y 2500 Hz y un desfase al azar de ±0,5 s respecto al segundo 1. La semilla es fija, así que la
medida se repite igual en cualquier máquina.

El decodificador es el propio: sincronismo por correlación del vector pseudoaleatorio, demodulación
no coherente de los 64 tonos por símbolo y **Reed-Solomon (63,12) con decisión blanda** —borrados
probabilísticos y Berlekamp-Massey repetido, siguiendo la idea de Franke y Taylor (QEX, 2016) con
implementación, reparto de borrados y métrica propios—. No hay CRC en JT65: lo que impide inventar
un mensaje es el criterio de aceptación del decodificador blando (distancia blanda máxima y
correcciones máximas), medido con ruido puro.

La columna **Palabras rechazadas** cuenta las palabras de código que salieron de algún intento y no
pasaron el criterio: cada una era un mensaje falso en potencia. La columna que manda es **Falsos**:
tiene que ser cero en todas las franjas y en la tanda de ruido puro.

**ms de CPU/ventana** es tiempo de procesador, no de reloj; la ventana de JT65 dura 60.000 ms y el
decodificador dispone de unos 12.000 tras el final de la señal. Compilación **de publicación (Release)**,
50 ventanas por franja.

### JT65A, de −16 a −30 dB

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Palabras rechazadas | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| -16 | 50 | 50 | 100,0 | 0 | 0 | 00 | 537 |
| -17 | 50 | 50 | 100,0 | 0 | 51 | -0,1 | 648 |
| -18 | 50 | 50 | 100,0 | 0 | 47 | 00 | 724 |
| -19 | 50 | 50 | 100,0 | 0 | 73 | -0,1 | 826 |
| -20 | 50 | 50 | 100,0 | 0 | 85 | -0,1 | 850 |
| -21 | 50 | 50 | 100,0 | 0 | 83 | -0,2 | 877 |
| -22 | 50 | 50 | 100,0 | 0 | 82 | -0,2 | 900 |
| -23 | 50 | 49 | 98,0 | 0 | 45 | -0,2 | 888 |
| -24 | 50 | 35 | 70,0 | 0 | 813 | -0,1 | 1033 |
| -25 | 50 | 11 | 22,0 | 0 | 1692 | -0,1 | 1190 |
| -26 | 50 | 2 | 4,0 | 0 | 1964 | 00 | 1252 |
| -27 | 50 | 0 | 0,0 | 0 | 1808 | 00 | 1238 |
| -28 | 50 | 0 | 0,0 | 0 | 859 | 00 | 1070 |
| -29 | 50 | 0 | 0,0 | 0 | 301 | 00 | 970 |
| -30 | 50 | 0 | 0,0 | 0 | 222 | 00 | 971 |

### Ruido puro, sin señal

| Ventanas de ruido | Falsos | Candidatas examinadas | Palabras rechazadas | ms de CPU/ventana |
|---:|---:|---:|---:|---:|
| 300 | 0 | 19 | 714 | 967 |

