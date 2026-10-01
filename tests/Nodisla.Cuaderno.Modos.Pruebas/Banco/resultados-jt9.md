# Banco de medida de JT9

Generado por `BancoJt9Pruebas` el 2026-09-27 (UTC). **No editar a mano.**

Señal sintética de JT9 con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz.
Cada ventana lleva un mensaje al azar entre ocho, con el tono de sincronismo al azar entre 500
y 2500 Hz y un desfase al azar de ±0,5 s respecto al segundo 1. La semilla es fija, así que la
medida se repite igual en cualquier máquina.

El decodificador es el propio: sincronismo por los 16 símbolos del tono 0, demodulación no coherente
de los 9 tonos con valores blandos por bit (razón de verosimilitudes de la FSK no coherente),
desentrelazado y **decodificación secuencial de Fano** del código convolucional K=32 r=1/2, que se
comparte con WSPR en `Convolucional/`. No hay CRC en JT9: lo que impide inventar un mensaje es que
Fano llegue al final con la cola de 31 ceros casando, que los 72 bits tengan gramática y que el
mensaje recodificado se parezca a lo recibido (coincidencia mínima), medido con ruido puro.

La columna **Caminos rechazados** cuenta los caminos completos de Fano que se tiraron por no tener
gramática o por no parecerse a lo recibido: cada uno era un mensaje falso en potencia. La columna que
manda es **Falsos**: tiene que ser cero en todas las franjas y en la tanda de ruido puro.

**ms de CPU/ventana** es tiempo de procesador, no de reloj; la ventana de JT9 dura 60.000 ms y el
decodificador dispone de unos 10.000 tras el final de la señal. Compilación **de publicación (Release)**,
50 ventanas por franja.

### JT9, de −16 a −30 dB

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Caminos rechazados | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| -16 | 50 | 50 | 100,0 | 0 | 0 | -0,9 | 581 |
| -17 | 50 | 50 | 100,0 | 0 | 0 | -0,8 | 570 |
| -18 | 50 | 50 | 100,0 | 0 | 0 | -0,9 | 606 |
| -19 | 50 | 50 | 100,0 | 0 | 0 | -0,8 | 700 |
| -20 | 50 | 50 | 100,0 | 0 | 0 | -0,7 | 744 |
| -21 | 50 | 50 | 100,0 | 0 | 0 | -0,8 | 832 |
| -22 | 50 | 50 | 100,0 | 0 | 0 | -0,7 | 948 |
| -23 | 50 | 50 | 100,0 | 0 | 0 | -1,0 | 1280 |
| -24 | 50 | 50 | 100,0 | 0 | 0 | -1,1 | 1812 |
| -25 | 50 | 48 | 96,0 | 0 | 0 | -0,8 | 2107 |
| -26 | 50 | 32 | 64,0 | 0 | 0 | -0,8 | 2283 |
| -27 | 50 | 8 | 16,0 | 0 | 0 | -0,8 | 2432 |
| -28 | 50 | 0 | 0,0 | 0 | 0 | 00 | 2447 |
| -29 | 50 | 0 | 0,0 | 0 | 0 | 00 | 2454 |
| -30 | 50 | 0 | 0,0 | 0 | 0 | 00 | 2480 |

### Ruido puro, sin señal

| Ventanas de ruido | Falsos | Candidatas examinadas | Caminos rechazados | ms de CPU/ventana |
|---:|---:|---:|---:|---:|
| 300 | 0 | 8450 | 0 | 2476 |

