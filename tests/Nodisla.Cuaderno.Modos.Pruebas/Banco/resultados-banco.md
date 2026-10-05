# Banco de medida del módem propio

Generado por `BancoDeMedidaPruebas` el 2026-10-05 (UTC). **No editar a mano.**

Señal sintética con ruido blanco gaussiano, relación señal-ruido referida a 2500 Hz,
igual que los informes de FT8. Cada ventana lleva un mensaje al azar entre ocho, en una
frecuencia al azar entre 500 y 2500 Hz y con un desfase al azar de ±0,3 s. La semilla es
fija, así que la medida se repite igual en cualquier máquina.

Las cifras son con el **código corrector LDPC(174,91) de verdad**, el mismo que usa todo
el mundo en FT8 y FT4; su tabla está en `src/Nodisla.Cuaderno.Modos/Tablas/` con su
procedencia y su licencia, y el fichero trae además la matriz generadora, que no se usa
para codificar sino para cruzarla con la de paridad en cada arranque. Lo que se mide
aquí es, por tanto, la sensibilidad real del módem contra ruido blanco.

La **recuperación profunda** está puesta y marcada aparte. Es la que hace casi todo el
trabajo en el filo: entre −21 y −19 dB, la mitad larga de lo que sale viene de ella.
Viene con dos frenos, los dos con su número sacado de medir y no de opinar: sólo se
intenta en candidatas con sincronismo de verdad, y la palabra reconstruida tiene que
parecerse a lo que oyó el demodulador. Sin esos frenos salían **doce mensajes inventados
por cada mil ventanas**; con ellos puestos, ninguno.

Hay una columna que merece más atención de la que parece: **Rechazos del CRC**. Son las
palabras que llegaron al sello y no cuadraron. No son un fallo —el sistema funcionando—
pero sí son la medida del **riesgo latente**: cada una es una tirada de una entre 16.384
de colarse. Mirar sólo la columna de falsos engaña, porque puede salir cero por suerte;
ésta dice cuántas veces se ha tentado a la suerte. Si un cambio la dispara, el cambio es
peligroso aunque esa tarde no haya salido ningún falso.

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
| 0 | 12 | 12 | 100,0 | 0 | 18 | -6,7 | 145 |
| -3 | 12 | 12 | 100,0 | 0 | 7 | -4,1 | 145 |
| -6 | 12 | 12 | 100,0 | 0 | 6 | -3,2 | 148 |
| -9 | 12 | 12 | 100,0 | 0 | 9 | -1,8 | 143 |
| -12 | 12 | 12 | 100,0 | 0 | 5 | -1,6 | 143 |
| -15 | 12 | 12 | 100,0 | 0 | 0 | -0,6 | 142 |
| -18 | 12 | 11 | 91,7 | 0 | 1 | -0,6 | 138 |
| -21 | 12 | 2 | 16,7 | 0 | 2 | +1,0 | 137 |
| -24 | 12 | 0 | 0,0 | 0 | 0 | 00 | 141 |

### FT4

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 12 | 12 | 100,0 | 0 | 9 | -3,4 | 126 |
| -3 | 12 | 12 | 100,0 | 0 | 4 | -1,8 | 130 |
| -6 | 12 | 12 | 100,0 | 0 | 5 | -1,3 | 125 |
| -9 | 12 | 12 | 100,0 | 0 | 0 | -0,7 | 128 |
| -12 | 12 | 12 | 100,0 | 0 | 0 | -0,4 | 130 |
| -15 | 12 | 6 | 50,0 | 0 | 3 | -0,8 | 125 |
| -18 | 12 | 0 | 0,0 | 0 | 0 | 00 | 126 |
| -21 | 12 | 0 | 0,0 | 0 | 1 | 00 | 125 |

### FT8 en el filo, de decibelio en decibelio

Donde el módem deja de sacar mensajes. El doble de ventanas por franja, porque aquí es
donde un cambio se nota y donde el azar engaña más.

### FT8, −16 a −23 dB

| S/R (dB) | Ventanas | Recuperados | % | Falsos | Rechazos del CRC | Error del informe (dB) | ms de CPU/ventana |
|---:|---:|---:|---:|---:|---:|---:|---:|
| -16 | 24 | 24 | 100,0 | 0 | 5 | -0,5 | 142 |
| -17 | 24 | 24 | 100,0 | 0 | 3 | -0,5 | 142 |
| -18 | 24 | 22 | 91,7 | 0 | 3 | -0,8 | 141 |
| -19 | 24 | 19 | 79,2 | 0 | 0 | -0,3 | 139 |
| -20 | 24 | 6 | 25,0 | 0 | 3 | -0,3 | 142 |
| -21 | 24 | 3 | 12,5 | 0 | 2 | +0,7 | 141 |
| -22 | 24 | 0 | 0,0 | 0 | 3 | 00 | 141 |
| -23 | 24 | 0 | 0,0 | 0 | 0 | 00 | 142 |

