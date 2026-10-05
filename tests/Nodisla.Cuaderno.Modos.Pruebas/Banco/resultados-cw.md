# Banco de medida del decodificador de CW

Generado por `BancoCwPruebas` el 2026-10-05 (UTC). **No editar a mano.**

Telegrafía sintética (`SintetizadorCw`, rampas de 5 ms) sobre ruido blanco gaussiano. La relación
señal-ruido es la potencia **con la llave abajo** frente al ruido en 2500 Hz, como en el resto de
bancos del módem; la columna de 500 Hz suma 7 dB (la referencia habitual en telegrafía).

Cada franja pasa los cuatro textos del banco (contacto con indicativos, RST, números, puntuación y
los prosignos <KN>, <BT>, <SK> y <AR>) con un tono al azar entre 600 y 800 Hz y el decodificador en
**automático** (busca el tono solo) y con los ajustes de fábrica: filtro de 100 Hz, sensibilidad 6,
de 5 a 60 WPM. El **CER** es la distancia de edición entre lo enviado y lo leído (espacios
incluidos, cada prosigno cuenta uno) entre los caracteres enviados. Semilla fija.

Compilación **de publicación (Release)**; el banco entero tardó 27 s.

### Manipulador electrónico, por velocidad y relación señal-ruido

| Escenario | WPM | S/R en 2500 Hz (dB) | S/R en 500 Hz (dB) | Caracteres | Errores | CER (%) | WPM medida |
|---|---:|---:|---:|---:|---:|---:|---:|
| Manipulador | 12 | 10 | 17 | 183 | 10 | 5,5 | 12 |
| Manipulador | 12 | 0 | 7 | 183 | 11 | 6,0 | 12 |
| Manipulador | 12 | -4 | 3 | 183 | 5 | 2,7 | 12 |
| Manipulador | 12 | -6 | 1 | 183 | 158 | 86,3 | 21 |
| Manipulador | 12 | -8 | -1 | 183 | 183 | 100,0 | 9 |
| Manipulador | 12 | -10 | -3 | 183 | 183 | 100,0 | 9 |
| Manipulador | 12 | -12 | -5 | 183 | 183 | 100,0 | 9 |
| Manipulador | 12 | -14 | -7 | 183 | 183 | 100,0 | 9 |
| Manipulador | 20 | 10 | 17 | 183 | 11 | 6,0 | 20 |
| Manipulador | 20 | 0 | 7 | 183 | 4 | 2,2 | 20 |
| Manipulador | 20 | -4 | 3 | 183 | 7 | 3,8 | 20 |
| Manipulador | 20 | -6 | 1 | 183 | 162 | 88,5 | 12 |
| Manipulador | 20 | -8 | -1 | 183 | 183 | 100,0 | 9 |
| Manipulador | 20 | -10 | -3 | 183 | 183 | 100,0 | 9 |
| Manipulador | 20 | -12 | -5 | 183 | 183 | 100,0 | 9 |
| Manipulador | 20 | -14 | -7 | 183 | 183 | 100,0 | 9 |
| Manipulador | 30 | 10 | 17 | 183 | 4 | 2,2 | 31 |
| Manipulador | 30 | 0 | 7 | 183 | 5 | 2,7 | 30 |
| Manipulador | 30 | -4 | 3 | 183 | 14 | 7,7 | 31 |
| Manipulador | 30 | -6 | 1 | 183 | 165 | 90,2 | 15 |
| Manipulador | 30 | -8 | -1 | 183 | 183 | 100,0 | 9 |
| Manipulador | 30 | -10 | -3 | 183 | 183 | 100,0 | 9 |
| Manipulador | 30 | -12 | -5 | 183 | 183 | 100,0 | 9 |
| Manipulador | 30 | -14 | -7 | 183 | 183 | 100,0 | 9 |
| Manipulador | 40 | 10 | 17 | 183 | 5 | 2,7 | 44 |
| Manipulador | 40 | 0 | 7 | 183 | 15 | 8,2 | 42 |
| Manipulador | 40 | -4 | 3 | 183 | 53 | 29,0 | 46 |
| Manipulador | 40 | -6 | 1 | 183 | 156 | 85,2 | 18 |
| Manipulador | 40 | -8 | -1 | 183 | 183 | 100,0 | 9 |
| Manipulador | 40 | -10 | -3 | 183 | 183 | 100,0 | 9 |
| Manipulador | 40 | -12 | -5 | 183 | 183 | 100,0 | 9 |
| Manipulador | 40 | -14 | -7 | 183 | 183 | 100,0 | 9 |

### Condiciones difíciles, a 20 WPM

| Escenario | WPM | S/R en 2500 Hz (dB) | S/R en 500 Hz (dB) | Caracteres | Errores | CER (%) | WPM medida |
|---|---:|---:|---:|---:|---:|---:|---:|
| Mano (15 %) | 20 | 10 | 17 | 183 | 17 | 9,3 | 19 |
| Mano (15 %) | 20 | 0 | 7 | 183 | 17 | 9,3 | 19 |
| Mano (15 %) | 20 | -4 | 3 | 183 | 21 | 11,5 | 19 |
| Mano (15 %) | 20 | -6 | 1 | 183 | 130 | 71,0 | 21 |
| Mano (15 %) | 20 | -8 | -1 | 183 | 183 | 100,0 | 9 |
| Mano (15 %) | 20 | -10 | -3 | 183 | 183 | 100,0 | 9 |
| QSB 15 dB | 20 | 10 | 17 | 183 | 29 | 15,8 | 21 |
| QSB 15 dB | 20 | 0 | 7 | 183 | 130 | 71,0 | 19 |
| QSB 15 dB | 20 | -4 | 3 | 183 | 152 | 83,1 | 22 |
| QSB 15 dB | 20 | -6 | 1 | 183 | 183 | 100,0 | 9 |
| QSB 15 dB | 20 | -8 | -1 | 183 | 183 | 100,0 | 9 |
| QSB 15 dB | 20 | -10 | -3 | 183 | 183 | 100,0 | 9 |
| QRM a +150 Hz | 20 | 10 | 17 | 183 | 10 | 5,5 | 20 |
| QRM a +150 Hz | 20 | 0 | 7 | 183 | 4 | 2,2 | 20 |
| QRM a +150 Hz | 20 | -4 | 3 | 183 | 18 | 9,8 | 20 |
| QRM a +150 Hz | 20 | -6 | 1 | 183 | 129 | 70,5 | 12 |
| QRM a +150 Hz | 20 | -8 | -1 | 183 | 183 | 100,0 | 9 |
| QRM a +150 Hz | 20 | -10 | -3 | 183 | 183 | 100,0 | 9 |
| Cambio ×1,6 | 20 | 10 | 17 | 183 | 30 | 16,4 | 31 |
| Cambio ×1,6 | 20 | 0 | 7 | 183 | 19 | 10,4 | 33 |
| Cambio ×1,6 | 20 | -4 | 3 | 183 | 20 | 10,9 | 32 |
| Cambio ×1,6 | 20 | -6 | 1 | 183 | 113 | 61,7 | 37 |
| Cambio ×1,6 | 20 | -8 | -1 | 183 | 183 | 100,0 | 9 |
| Cambio ×1,6 | 20 | -10 | -3 | 183 | 183 | 100,0 | 9 |

### Tono fijado a mano (pitch del equipo), a 20 WPM

| Escenario | WPM | S/R en 2500 Hz (dB) | S/R en 500 Hz (dB) | Caracteres | Errores | CER (%) | WPM medida |
|---|---:|---:|---:|---:|---:|---:|---:|
| Tono fijo 700 Hz | 20 | 0 | 7 | 183 | 4 | 2,2 | 20 |
| Tono fijo 700 Hz | 20 | -4 | 3 | 183 | 8 | 4,4 | 20 |
| Tono fijo 700 Hz | 20 | -6 | 1 | 183 | 79 | 43,2 | 18 |
| Tono fijo 700 Hz | 20 | -8 | -1 | 183 | 183 | 100,0 | 9 |
| Tono fijo 700 Hz | 20 | -10 | -3 | 183 | 183 | 100,0 | 9 |
| Tono fijo 700 Hz | 20 | -12 | -5 | 183 | 183 | 100,0 | 9 |
| Tono fijo 700 Hz | 20 | -14 | -7 | 183 | 183 | 100,0 | 9 |

### Ruido puro

30 minutos de ruido blanco sin señal, con ocho canales a la vez: **0 caracteres** escritos.

### Grabaciones reales

En el repositorio no hay ninguna grabación de telegrafía real. Se usa la de FT8 de 20 m
(`Aire/ft8-20m-2026-09-27-151745.wav`) como prueba de que el audio real de otro modo no escribe
texto basura (`DecodificadorCwPruebas`).

