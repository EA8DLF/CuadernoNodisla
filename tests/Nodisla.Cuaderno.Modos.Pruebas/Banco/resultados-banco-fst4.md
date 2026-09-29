# Banco de medida de FST4 y FST4W

Generado por `BancoFst4Pruebas` el 2026-09-28 (UTC). **No editar a mano.**

Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz. Cada
ventana lleva un mensaje al azar, en una frecuencia al azar dentro de la banda que busca el
decodificador (100–3000 Hz hasta 120 s; 1400–1600 en FST4W) y con un desfase al azar de
hasta ±0,5 s. Semilla fija.

Las cifras son con los **códigos LDPC(240,101) y (240,74) de verdad** (tablas en
`Tablas/tablas-fst4.txt` y `tablas-fst4w.txt`, de `ft8_lib`, MIT). Umbrales publicados
(50 % de decodificación, Quick-Start Guide): FST4-15 −20,7 dB, FST4-30 −24,2, FST4-60 −28,1,
FST4-120 −31,3, FST4W-120 −32,8. Sin decodificación *a priori*, que aquí no se usa.

**Falsos** tiene que ser cero en todas las franjas; el CRC de 24 bits deja una entre diecisiete
millones a cada palabra de ruido, así que la columna de rechazos del CRC es el riesgo latente.

Tiempos de **procesador** de una compilación **de publicación (Release)**.

### FST4-15

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -12 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,1 | 352 |
| -14 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,1 | 342 |
| -16 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,1 | 338 |
| -18 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 340 |
| -20 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 354 |
| -22 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 354 |
| -24 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 352 |

### FST4-30

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -16 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,1 | 615 |
| -18 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 607 |
| -20 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,5 | 607 |
| -22 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 611 |
| -24 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 623 |
| -26 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 607 |
| -28 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 611 |

### FST4-60

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -20 | 8 | 8 | 100,0 | 0 | 0 | 0 | -0,1 | 2182 |
| -22 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,1 | 2217 |
| -24 | 8 | 5 | 62,5 | 0 | 0 | 0 | +0,2 | 2170 |
| -26 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 2184 |
| -28 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 2189 |
| -30 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 2223 |
| -32 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 2180 |

### FST4-120

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -22 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 4311 |
| -24 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,6 | 4291 |
| -26 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,4 | 4326 |
| -28 | 8 | 2 | 25,0 | 0 | 0 | 0 | +0,5 | 4275 |
| -30 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 4330 |
| -32 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 4324 |
| -34 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 4316 |

### FST4W-120

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -24 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,6 | 4287 |
| -26 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,4 | 4318 |
| -28 | 8 | 5 | 62,5 | 0 | 0 | 0 | +0,6 | 4275 |
| -30 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 4322 |
| -32 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 4295 |
| -34 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 4340 |
| -36 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 4307 |

### Ruido puro

FST4-15, 20 ventanas sin señal: **0 mensajes**, 0 palabras válidas del corrector, 347 ms de CPU por ventana.

FST4W-120, 5 ventanas sin señal: **0 mensajes**, 0 palabras válidas, 4341 ms de CPU por ventana.

