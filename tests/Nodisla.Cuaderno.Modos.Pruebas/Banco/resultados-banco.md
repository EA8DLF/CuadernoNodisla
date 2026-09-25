# Banco de medida del módem propio

Generado por `BancoDeMedidaPruebas` el 2026-09-25 (UTC). **No editar a mano.**

Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz,
igual que los informes de FT8. Cada ventana lleva un mensaje al azar entre ocho, en una
frecuencia al azar entre 500 y 2500 Hz y con un desfase al azar de ±0,3 s. La semilla es
fija, así que la medida se repite igual en cualquier máquina.

Las cifras son con el **código corrector LDPC(174,91) de verdad**, el mismo que usa todo
el mundo en FT8 y FT4; su tabla está en `src/Nodisla.Cuaderno.Modos/Tablas/` con su
procedencia y su licencia. Lo que se mide aquí es, por tanto, la sensibilidad real del
módem contra ruido blanco. **El escalón que queda con señal muy débil es el hueco de la
recuperación profunda**, que todavía no está puesta: es lo que el programa de referencia
usa por debajo de −20 dB y lo que le permite seguir sacando mensajes ahí.

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
| 0 | 12 | 12 | 100,0 | 0 | 1 | -6,7 | 379 |
| -3 | 12 | 12 | 100,0 | 0 | 0 | -4,1 | 353 |
| -6 | 12 | 12 | 100,0 | 0 | 0 | -3,0 | 342 |
| -9 | 12 | 12 | 100,0 | 0 | 0 | -1,7 | 342 |
| -12 | 12 | 12 | 100,0 | 0 | 0 | -1,6 | 346 |
| -15 | 12 | 12 | 100,0 | 0 | 0 | -0,6 | 331 |
| -18 | 12 | 12 | 100,0 | 0 | 0 | -0,8 | 350 |
| -21 | 12 | 0 | 0,0 | 0 | 0 | 00 | 323 |
| -24 | 12 | 0 | 0,0 | 0 | 0 | 00 | 353 |

### FT4

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 12 | 12 | 100,0 | 0 | 0 | -3,4 | 309 |
| -3 | 12 | 12 | 100,0 | 0 | 0 | -1,8 | 315 |
| -6 | 12 | 12 | 100,0 | 0 | 1 | -1,3 | 326 |
| -9 | 12 | 12 | 100,0 | 0 | 0 | -0,6 | 292 |
| -12 | 12 | 12 | 100,0 | 0 | 1 | -0,6 | 279 |
| -15 | 12 | 7 | 58,3 | 0 | 0 | -0,4 | 294 |
| -18 | 12 | 0 | 0,0 | 0 | 0 | 00 | 293 |
| -21 | 12 | 0 | 0,0 | 0 | 0 | 00 | 299 |

### FT8 en el filo, de decibelio en decibelio

Donde el módem deja de sacar mensajes. El doble de ventanas por franja, porque aquí es
donde un cambio se nota y donde el azar engaña más.

### FT8, −16 a −23 dB

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| -16 | 24 | 24 | 100,0 | 0 | 2 | -0,5 | 339 |
| -17 | 24 | 24 | 100,0 | 0 | 2 | -0,5 | 331 |
| -18 | 24 | 23 | 95,8 | 0 | 0 | -0,8 | 338 |
| -19 | 24 | 13 | 54,2 | 0 | 1 | -0,2 | 343 |
| -20 | 24 | 6 | 25,0 | 0 | 0 | 00 | 352 |
| -21 | 24 | 1 | 4,2 | 0 | 0 | +1,0 | 339 |
| -22 | 24 | 0 | 0,0 | 0 | 1 | 00 | 349 |
| -23 | 24 | 0 | 0,0 | 0 | 0 | 00 | 338 |

