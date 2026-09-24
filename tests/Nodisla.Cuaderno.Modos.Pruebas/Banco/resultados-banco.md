# Banco de medida del módem propio

Generado por `BancoDeMedidaPruebas` el 2026-09-24 (UTC). **No editar a mano.**

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

La columna **ms/ventana** solo vale si se dice cómo se compiló, porque entre una
compilación de depuración y una de publicación hay un factor de cinco. Estas cifras son
de una compilación **de publicación (Release)**. Para hacerse una idea: FT8 da 15.000 ms por ventana y
FT4 da 7.500, así que aquí se está usando en torno al 3 % del hueco disponible.

### FT8

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 12 | 12 | 100,0 | 0 | 2 | -6,8 | 816 |
| -3 | 12 | 12 | 100,0 | 0 | 0 | -4,1 | 624 |
| -6 | 12 | 12 | 100,0 | 0 | 1 | -3,0 | 713 |
| -9 | 12 | 12 | 100,0 | 0 | 2 | -1,7 | 639 |
| -12 | 12 | 12 | 100,0 | 0 | 0 | -1,6 | 594 |
| -15 | 12 | 12 | 100,0 | 0 | 0 | -0,5 | 698 |
| -18 | 12 | 11 | 91,7 | 0 | 0 | -0,7 | 576 |
| -21 | 12 | 1 | 8,3 | 0 | 1 | +1,0 | 678 |
| -24 | 12 | 0 | 0,0 | 0 | 2 | 00 | 608 |

### FT4

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 12 | 12 | 100,0 | 0 | 2 | -3,6 | 329 |
| -3 | 12 | 12 | 100,0 | 0 | 1 | -1,8 | 277 |
| -6 | 12 | 12 | 100,0 | 0 | 1 | -1,3 | 271 |
| -9 | 12 | 12 | 100,0 | 0 | 2 | -0,8 | 290 |
| -12 | 12 | 12 | 100,0 | 0 | 2 | -0,5 | 292 |
| -15 | 12 | 4 | 33,3 | 0 | 0 | -1,5 | 351 |
| -18 | 12 | 0 | 0,0 | 0 | 1 | 00 | 579 |
| -21 | 12 | 0 | 0,0 | 0 | 0 | 00 | 581 |

