# Banco de medida de FST4 y FST4W

Generado por `BancoFst4Pruebas` el 2026-09-29 (UTC). **No editar a mano.**

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
| -12 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 664 |
| -14 | 8 | 8 | 100,0 | 0 | 0 | 0 | -0,1 | 619 |
| -16 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 613 |
| -18 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,1 | 633 |
| -20 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 623 |
| -22 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 617 |
| -24 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 613 |

### FST4-30

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -16 | 8 | 8 | 100,0 | 0 | 0 | 0 | 00 | 668 |
| -18 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 645 |
| -20 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 646 |
| -22 | 8 | 5 | 62,5 | 0 | 0 | 0 | -0,4 | 643 |
| -24 | 8 | 2 | 25,0 | 0 | 0 | 0 | 00 | 635 |
| -26 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 648 |
| -28 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 641 |

### FST4-60

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -20 | 8 | 8 | 100,0 | 0 | 0 | 0 | -0,1 | 846 |
| -22 | 8 | 8 | 100,0 | 0 | 0 | 0 | 00 | 846 |
| -24 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,4 | 850 |
| -26 | 8 | 6 | 75,0 | 0 | 0 | 0 | 00 | 838 |
| -28 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 836 |
| -30 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 850 |
| -32 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 842 |

### FST4-120

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -22 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 1119 |
| -24 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,5 | 1123 |
| -26 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 1123 |
| -28 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 1133 |
| -30 | 8 | 3 | 37,5 | 0 | 0 | 0 | 00 | 1137 |
| -32 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 1129 |
| -34 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 1127 |

### FST4W-120

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Por recuperación profunda | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| -24 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,5 | 1139 |
| -26 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 1119 |
| -28 | 8 | 8 | 100,0 | 0 | 0 | 0 | +0,3 | 1139 |
| -30 | 8 | 6 | 75,0 | 0 | 0 | 0 | -0,2 | 1152 |
| -32 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 1129 |
| -34 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 1131 |
| -36 | 8 | 0 | 0,0 | 0 | 0 | 0 | 00 | 1139 |

### Ruido puro

FST4-15, 20 ventanas sin señal: **0 mensajes**, 0 palabras válidas del corrector, 603 ms de CPU por ventana.

FST4W-120, 5 ventanas sin señal: **0 mensajes**, 0 palabras válidas, 1138 ms de CPU por ventana.

