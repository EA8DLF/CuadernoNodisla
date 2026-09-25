# Banco de medida del módem propio

Generado por `BancoDeMedidaPruebas` el 2026-09-25 (UTC). **No editar a mano.**

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

La columna **ms de CPU/ventana** es tiempo de **procesador**, no de reloj: cuenta los
ciclos que el sistema apunta a este proceso, así que no se estira porque la máquina
esté ocupada con otra cosa. Aun así solo vale si se dice cómo se compiló, porque entre
depuración y publicación hay un factor de cinco; estas cifras son de una compilación
**de publicación (Release)**. Para hacerse una idea: FT8 da 15.000 ms por ventana y FT4 da 7.500, así
que aquí se está usando en torno al 3 % del hueco disponible.

### FT8

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 12 | 12 | 100,0 | 0 | 2 | -6,8 | 348 |
| -3 | 12 | 12 | 100,0 | 0 | 0 | -4,1 | 359 |
| -6 | 12 | 12 | 100,0 | 0 | 1 | -3,0 | 354 |
| -9 | 12 | 12 | 100,0 | 0 | 2 | -1,7 | 350 |
| -12 | 12 | 12 | 100,0 | 0 | 0 | -1,6 | 335 |
| -15 | 12 | 12 | 100,0 | 0 | 0 | -0,5 | 344 |
| -18 | 12 | 11 | 91,7 | 0 | 0 | -0,7 | 345 |
| -21 | 12 | 1 | 8,3 | 0 | 1 | +1,0 | 352 |
| -24 | 12 | 0 | 0,0 | 0 | 2 | 00 | 353 |

### FT4

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 12 | 12 | 100,0 | 0 | 2 | -3,6 | 301 |
| -3 | 12 | 12 | 100,0 | 0 | 1 | -1,8 | 314 |
| -6 | 12 | 12 | 100,0 | 0 | 1 | -1,3 | 307 |
| -9 | 12 | 12 | 100,0 | 0 | 2 | -0,8 | 318 |
| -12 | 12 | 12 | 100,0 | 0 | 2 | -0,5 | 305 |
| -15 | 12 | 4 | 33,3 | 0 | 0 | -1,5 | 323 |
| -18 | 12 | 0 | 0,0 | 0 | 1 | 00 | 323 |
| -21 | 12 | 0 | 0,0 | 0 | 0 | 00 | 307 |

