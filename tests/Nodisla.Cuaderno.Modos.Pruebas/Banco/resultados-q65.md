# Banco de medida de Q65

Generado por `BancoDeQ65Pruebas` el 2026-10-05 (UTC), compilación **de publicación (Release)**. **No editar a mano.**

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
| -18 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,0 | 325 |
| -20 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 250 |
| -22 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 200 |
| -24 | 10 | 6 | 60,0 | 0 | 1 | 0 | 0 | -0,7 | 200 |
| -26 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 184 |
| -28 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 209 |
| -30 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 188 |

### Q65-30A con AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -18 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,0 | 389 |
| -20 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -1,1 | 311 |
| -22 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -1,1 | 258 |
| -24 | 10 | 8 | 80,0 | 0 | 3 | 0 | 0 | -0,9 | 266 |
| -26 | 10 | 3 | 30,0 | 0 | 3 | 0 | 1 | -0,7 | 269 |
| -28 | 10 | 1 | 10,0 | 0 | 1 | 0 | 0 | 00 | 284 |
| -30 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 261 |

### Q65-60A sin AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -18 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,0 | 438 |
| -20 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -1,2 | 431 |
| -22 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 419 |
| -24 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -0,9 | 389 |
| -26 | 10 | 9 | 90,0 | 0 | 0 | 0 | 0 | -0,4 | 386 |
| -28 | 10 | 1 | 10,0 | 0 | 1 | 0 | 0 | 00 | 381 |
| -30 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 369 |
| -32 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 388 |
| -34 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 338 |

### Q65-60A con AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -18 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,0 | 505 |
| -20 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -1,2 | 497 |
| -22 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 483 |
| -24 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -0,9 | 452 |
| -26 | 10 | 10 | 100,0 | 0 | 1 | 0 | 0 | -0,6 | 447 |
| -28 | 10 | 6 | 60,0 | 0 | 6 | 0 | 0 | -1,2 | 458 |
| -30 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 444 |
| -32 | 10 | 0 | 0,0 | 0 | 0 | 1 | 0 | 00 | 458 |
| -34 | 10 | 0 | 0,0 | 0 | 0 | 1 | 0 | 00 | 414 |

### Q65-120A sin AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -24 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 608 |
| -26 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 570 |
| -28 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -0,8 | 527 |
| -30 | 10 | 8 | 80,0 | 0 | 0 | 0 | 0 | -0,4 | 548 |
| -32 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 552 |
| -34 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 539 |
| -36 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 553 |

### Q65-120A con AP

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Con AP | Rechazos del CRC | Rechazos por verosimilitud | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -24 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 664 |
| -26 | 10 | 10 | 100,0 | 0 | 0 | 0 | 0 | -1,1 | 628 |
| -28 | 10 | 10 | 100,0 | 0 | 0 | 1 | 0 | -0,8 | 588 |
| -30 | 10 | 9 | 90,0 | 0 | 1 | 0 | 0 | -0,4 | 616 |
| -32 | 10 | 4 | 40,0 | 0 | 4 | 0 | 0 | -0,8 | 625 |
| -34 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 627 |
| -36 | 10 | 0 | 0,0 | 0 | 0 | 0 | 0 | 00 | 636 |

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

