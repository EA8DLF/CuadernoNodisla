# Banco de medida de WSPR

Generado por `BancoWsprPruebas` el 2026-09-29 (UTC). **No editar a mano.**

Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz, como
en los informes de WSPR. Cada ventana de dos minutos lleva un mensaje al azar entre ocho
(seis de tipo 1, dos de tipo 2), en una frecuencia al azar entre 1400 y 1600 Hz, con un
desfase al azar de ±1,5 s y una deriva al azar de ±1 Hz a lo largo de la transmisión. La
semilla es fija: la medida se repite igual en cualquier máquina.

El decodificador es el propio: sincronismo por correlación del vector de 162 en frecuencia,
tiempo y deriva; demodulación no coherente de los cuatro tonos sobre una señal estrecha de
23,4 muestras por segundo; y **decodificación secuencial de Fano** del código convolucional
K=32 (`Convolucional/`), con la cola de 31 ceros forzada. La sensibilidad publicada del
modo es de unos −28 dB; aquí se ve dónde queda éste.

Las columnas que vigilan el riesgo: **Caminos de Fano** son las veces que el decodificador
secuencial llegó al final del árbol; **Sin gramática**, cuántos de esos caminos no formaban un
mensaje válido (indicativo, localizador, potencia); **Sin coincidencia**, cuántos mensajes
válidos se tiraron porque, recodificados, no se parecían a lo recibido. **Coincidencia mínima**
es la más baja de un acierto en la franja; el umbral está en 0,30, y el margen
entre las dos cifras es lo que separa un decodificador que no inventa de uno que sí.

La columna que manda es **Falsos**: cero en todas las franjas y cero en el ruido puro. Un WSPR
falso mete un spot de propagación que nunca existió.

La columna **ms de CPU/ventana** es tiempo de procesador, no de reloj, de una compilación
**de publicación (Release)**. El hueco disponible son 120.000 ms por ventana.

### WSPR, con deriva de ±1 Hz

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Caminos de Fano | Sin gramática | Sin coincidencia | Coincidencia mínima | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -20 | 50 | 50 | 100,0 | 0 | 50 | 0 | 0 | 1,00 | 00 | 496 |
| -22 | 50 | 50 | 100,0 | 0 | 50 | 0 | 0 | 0,99 | 00 | 484 |
| -24 | 50 | 50 | 100,0 | 0 | 50 | 0 | 0 | 0,98 | 00 | 478 |
| -26 | 50 | 50 | 100,0 | 0 | 50 | 0 | 0 | 0,93 | -0,1 | 477 |
| -28 | 50 | 48 | 96,0 | 0 | 48 | 0 | 0 | 0,84 | +0,1 | 477 |
| -30 | 50 | 13 | 26,0 | 0 | 13 | 0 | 0 | 0,82 | +0,6 | 515 |
| -32 | 50 | 0 | 0,0 | 0 | 0 | 0 | 0 | 0,00 | 00 | 503 |
| -34 | 50 | 0 | 0,0 | 0 | 0 | 0 | 0 | 0,00 | 00 | 487 |

### Ruido puro

200 ventanas de ruido blanco sin ninguna señal: **0 mensajes**, 
0 caminos completos de Fano, 0 sin gramática, 
0 sin coincidencia, 488 ms de CPU por ventana.

