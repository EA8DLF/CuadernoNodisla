# Banco de medida del módem propio

Generado por `BancoDeMedidaPruebas` el 2026-09-22 (UTC). **No editar a mano.**

Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz,
igual que los informes de FT8. Cada ventana lleva un mensaje al azar entre ocho, en una
frecuencia al azar entre 500 y 2500 Hz y con un desfase al azar de ±0,3 s. La semilla es
fija, así que la medida se repite igual en cualquier máquina.

> **Aviso importante.** Estas cifras se han obtenido con el **código corrector de
> pruebas**, no con el de FT8 de verdad: la tabla del LDPC(174,91) todavía no está en el
> repositorio. El código de pruebas tiene las mismas dimensiones y una densidad parecida,
> así que la curva es representativa de toda la cadena —modulación, sincronismo,
> demodulación y corrección— pero **no es la sensibilidad final**. Al poner la tabla
> real hay que volver a pasar el banco y sustituir esta tabla.

La columna que manda es **Falsos**: tiene que ser cero en todas las franjas. Un mensaje
falso mete en el cuaderno un contacto que nunca existió y contamina los diplomas para
siempre; perder decodificaciones no deja rastro.

### FT8

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 12 | 12 | 100,0 | 0 | 3 | -6,8 | 2400 |
| -3 | 12 | 12 | 100,0 | 0 | 5 | -4,1 | 2317 |
| -6 | 12 | 12 | 100,0 | 0 | 3 | -3,0 | 2195 |
| -9 | 12 | 12 | 100,0 | 0 | 9 | -1,7 | 2097 |
| -12 | 12 | 12 | 100,0 | 0 | 5 | -1,6 | 2107 |
| -15 | 12 | 12 | 100,0 | 0 | 7 | -0,6 | 2110 |
| -18 | 12 | 12 | 100,0 | 0 | 5 | -0,8 | 2293 |
| -21 | 12 | 0 | 0,0 | 0 | 4 | 00 | 2545 |
| -24 | 12 | 0 | 0,0 | 0 | 2 | 00 | 2578 |

### FT4

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 12 | 12 | 100,0 | 0 | 6 | -3,4 | 2370 |
| -3 | 12 | 12 | 100,0 | 0 | 6 | -1,8 | 2904 |
| -6 | 12 | 12 | 100,0 | 0 | 5 | -1,3 | 2745 |
| -9 | 12 | 12 | 100,0 | 0 | 2 | -0,8 | 2105 |
| -12 | 12 | 12 | 100,0 | 0 | 9 | -0,4 | 1968 |
| -15 | 12 | 3 | 25,0 | 0 | 9 | -1,3 | 1977 |
| -18 | 12 | 0 | 0,0 | 0 | 7 | 00 | 2048 |
| -21 | 12 | 0 | 0,0 | 0 | 3 | 00 | 1905 |

