# Banco de medida de Q65

Generado por `BancoDeQ65Pruebas` el 2026-09-30 (UTC), compilación **de publicación (Release)**. **No editar a mano.**

Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz. Cada
ventana lleva un mensaje al azar entre ocho, en un tono base al azar entre 500 y 2500 Hz y
con un desfase al azar de ±0,3 s. Semilla fija. Sin promediado (cada ventana es independiente).
El código es el **QRA(65,15) de verdad** del protocolo; la tabla y su procedencia están en
`src/Nodisla.Cuaderno.Modos/Tablas/tablas-q65.txt` y `LEEME-q65.md`.

**Con AP** es con el indicativo propio (EA8DLF) y el del corresponsal (K1ABC) dados al modo:
se prueba además suponiendo que el mensaje empieza por CQ, por EA8DLF, o por EA8DLF K1ABC.
La columna **Con AP** cuenta cuántos de los recuperados necesitaron esa ayuda. **Rechazos por
verosimilitud** son palabras con sello bueno que el freno anti-falsos tiró por no parecerse a lo
oído; con señal de verdad tiene que ser cero o casi. La columna que manda es **Falsos**.

Los **ms de CPU/ventana** son tiempo de procesador del proceso, no de reloj. Para situarlos: la
ventana de Q65-30A dura 30.000 ms, la de 60A 60.000 y la de 120A 120.000.

### Q65-30A sin AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -18 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,0 | 369 |
| -20 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 291 |
| -22 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 230 |
| -24 | 10 | 6 | 60,0 | 0 | 1 | 0 | 0 | -0,7 | 223 |
| -26 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 222 |
| -28 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 242 |
| -30 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 219 |

### Q65-30A con AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -18 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,0 | 452 |
| -20 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -1,1 | 381 |
| -22 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -1,1 | 300 |
| -24 | 10 | 8 | 80,0 | 0 | 3 | 0 | 0 | -0,9 | 305 |
| -26 | 10 | 3 | 30,0 | 0 | 3 | 0 | 1 | -0,7 | 313 |
| -28 | 10 | 1 | 10,0 | 0 | 1 | 0 | 0 | 00 | 331 |
| -30 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 305 |

### Q65-60A sin AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -18 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,0 | 513 |
| -20 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -1,2 | 505 |
| -22 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 486 |
| -24 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -0,9 | 453 |
| -26 | 10 | 9 | 90,0 | 0 | 0 | 0 | 0 | -0,4 | 452 |
| -28 | 10 | 1 | 10,0 | 0 | 1 | 0 | 0 | 00 | 448 |
| -30 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 422 |
| -32 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 436 |
| -34 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 388 |

### Q65-60A con AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -18 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,0 | 598 |
| -20 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -1,2 | 597 |
| -22 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 563 |
| -24 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -0,9 | 523 |
| -26 | 10 | 10 | 100,0 | 0 | 1 | 0 | 0 | -0,6 | 528 |
| -28 | 10 | 6 | 60,0 | 0 | 6 | 0 | 0 | -1,2 | 536 |
| -30 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 519 |
| -32 | 10 | 0 | 0,0 | 0 | 0 | 1 | 0 | 00 | 534 |
| -34 | 10 | 0 | 0,0 | 0 | 0 | 1 | 0 | 00 | 489 |

### Q65-120A sin AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -24 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 708 |
| -26 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 672 |
| -28 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -0,8 | 625 |
| -30 | 10 | 8 | 80,0 | 0 | 0 | 0 | 0 | -0,4 | 661 |
| -32 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 647 |
| -34 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 638 |
| -36 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 659 |

### Q65-120A con AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -24 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 781 |
| -26 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 739 |
| -28 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -0,8 | 694 |
| -30 | 10 | 9 | 90,0 | 0 | 1 | 0 | 0 | -0,4 | 722 |
| -32 | 10 | 4 | 40,0 | 0 | 4 | 0 | 0 | -0,8 | 738 |
| -34 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 759 |
| -36 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 744 |

### Ruido puro

Ventanas sin señal, con el umbral de sincronismo bajado a 2,5 desviaciones para obligar al
corrector a pelear con el ruido en muchas candidatas. **Palabras cerradas** son las que el
código dio por válidas sobre ruido; el sello y la verosimilitud tienen que tirarlas todas.
**Verosimilitud máxima** es la más alta que alcanzó una palabra rechazada: el umbral (10) tiene
que quedar claramente por encima.

| Modo | AP | Ventanas | Falsos | Palabras cerradas | Rechazos del CRC | Rechazos por verosimilitud | Verosimilitud máxima |
|---|---|---:|---:|---:|---:|---:|---:|
| Q65-30A | no | 40 | 0 | 0 | 0 | 0 | — |
| Q65-30A | sí | 40 | 0 | 3 | 3 | 0 | — |
| Q65-60A | no | 40 | 0 | 0 | 0 | 0 | — |
| Q65-60A | sí | 40 | 0 | 1 | 1 | 0 | — |

### Promediado de periodos (Q65-60A)

El mismo mensaje repetido periodo tras periodo a una relación en la que un periodo solo no
suele llegar. Se cuenta en cuántas tandas acabó saliendo y cuántos periodos hicieron falta.

| S/R (dB) | Tandas | Salieron | Periodos medios | Falsos |
|---:|---:|---:|---:|---:|
| -30 | 6 | 6 | 3,7 | 0 |
| -32 | 6 | 3 | 5,7 | 0 |

