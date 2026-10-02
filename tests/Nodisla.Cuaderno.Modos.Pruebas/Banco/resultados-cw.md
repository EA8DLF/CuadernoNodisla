# Banco de medida del decodificador de CW

Generado por `BancoCwPruebas` el 2026-10-02 (UTC). **No editar a mano.**

Telegrafía sintética (`SintetizadorCw`, rampas de 5 ms) sobre ruido blanco gaussiano. La relación
señal-ruido es la potencia **con la llave abajo** frente al ruido en 2500 Hz, como en el resto de
bancos del módem; la columna de 500 Hz suma 7 dB (la referencia habitual en telegrafía).

Cada franja pasa los cuatro textos del banco (contacto con indicativos, RST, números, puntuación y
los prosignos <KN>, <BT>, <SK> y <AR>) con un tono al azar entre 600 y 800 Hz y el decodificador en
**automático** (busca el tono solo) y con los ajustes de fábrica: filtro de 100 Hz, sensibilidad 6,
de 5 a 60 WPM. El **CER** es la distancia de edición entre lo enviado y lo leído (espacios
incluidos, cada prosigno cuenta uno) entre los caracteres enviados. Semilla fija.

Compilación **de publicación (Release)**; el banco entero tardó 37 s.

### Manipulador electrónico, por velocidad y relación señal-ruido

| Escenario | WPM | S/R en 2500 Hz (dB) | S/R en 500 Hz (dB) | Caracteres | Errores | CER (%) | WPM medida |
|---|---:|---:|---:|---:|---:|---:|---:|
| Manipulador | 12 | 10 | 17 | 180 | 4 | 2,2 | 12 |
| Manipulador | 12 | 0 | 7 | 180 | 4 | 2,2 | 12 |
| Manipulador | 12 | -4 | 3 | 180 | 5 | 2,8 | 12 |
| Manipulador | 12 | -6 | 1 | 180 | 38 | 21,1 | 12 |
| Manipulador | 12 | -8 | -1 | 180 | 160 | 88,9 | 17 |
| Manipulador | 12 | -10 | -3 | 180 | 180 | 100,0 | 9 |
| Manipulador | 12 | -12 | -5 | 180 | 180 | 100,0 | 9 |
| Manipulador | 12 | -14 | -7 | 180 | 180 | 100,0 | 9 |
| Manipulador | 20 | 10 | 17 | 180 | 4 | 2,2 | 20 |
| Manipulador | 20 | 0 | 7 | 180 | 5 | 2,8 | 20 |
| Manipulador | 20 | -4 | 3 | 180 | 8 | 4,4 | 20 |
| Manipulador | 20 | -6 | 1 | 180 | 48 | 26,7 | 21 |
| Manipulador | 20 | -8 | -1 | 180 | 180 | 100,0 | 14 |
| Manipulador | 20 | -10 | -3 | 180 | 180 | 100,0 | 9 |
| Manipulador | 20 | -12 | -5 | 180 | 180 | 100,0 | 9 |
| Manipulador | 20 | -14 | -7 | 180 | 180 | 100,0 | 9 |
| Manipulador | 30 | 10 | 17 | 180 | 4 | 2,2 | 31 |
| Manipulador | 30 | 0 | 7 | 180 | 7 | 3,9 | 30 |
| Manipulador | 30 | -4 | 3 | 180 | 18 | 10,0 | 30 |
| Manipulador | 30 | -6 | 1 | 180 | 49 | 27,2 | 33 |
| Manipulador | 30 | -8 | -1 | 180 | 169 | 93,9 | 25 |
| Manipulador | 30 | -10 | -3 | 180 | 180 | 100,0 | 9 |
| Manipulador | 30 | -12 | -5 | 180 | 180 | 100,0 | 9 |
| Manipulador | 30 | -14 | -7 | 180 | 180 | 100,0 | 9 |
| Manipulador | 40 | 10 | 17 | 180 | 5 | 2,8 | 44 |
| Manipulador | 40 | 0 | 7 | 180 | 7 | 3,9 | 42 |
| Manipulador | 40 | -4 | 3 | 180 | 69 | 38,3 | 30 |
| Manipulador | 40 | -6 | 1 | 180 | 93 | 51,7 | 46 |
| Manipulador | 40 | -8 | -1 | 180 | 180 | 100,0 | 9 |
| Manipulador | 40 | -10 | -3 | 180 | 180 | 100,0 | 9 |
| Manipulador | 40 | -12 | -5 | 180 | 180 | 100,0 | 9 |
| Manipulador | 40 | -14 | -7 | 180 | 180 | 100,0 | 9 |

### Condiciones difíciles, a 20 WPM

| Escenario | WPM | S/R en 2500 Hz (dB) | S/R en 500 Hz (dB) | Caracteres | Errores | CER (%) | WPM medida |
|---|---:|---:|---:|---:|---:|---:|---:|
| Mano (15 %) | 20 | 10 | 17 | 180 | 17 | 9,4 | 20 |
| Mano (15 %) | 20 | 0 | 7 | 180 | 18 | 10,0 | 19 |
| Mano (15 %) | 20 | -4 | 3 | 180 | 14 | 7,8 | 19 |
| Mano (15 %) | 20 | -6 | 1 | 180 | 51 | 28,3 | 20 |
| Mano (15 %) | 20 | -8 | -1 | 180 | 157 | 87,2 | 13 |
| Mano (15 %) | 20 | -10 | -3 | 180 | 180 | 100,0 | 9 |
| QSB 15 dB | 20 | 10 | 17 | 180 | 36 | 20,0 | 20 |
| QSB 15 dB | 20 | 0 | 7 | 180 | 103 | 57,2 | 20 |
| QSB 15 dB | 20 | -4 | 3 | 180 | 127 | 70,6 | 21 |
| QSB 15 dB | 20 | -6 | 1 | 180 | 161 | 89,4 | 34 |
| QSB 15 dB | 20 | -8 | -1 | 180 | 180 | 100,0 | 16 |
| QSB 15 dB | 20 | -10 | -3 | 180 | 180 | 100,0 | 9 |
| QRM a +150 Hz | 20 | 10 | 17 | 180 | 4 | 2,2 | 20 |
| QRM a +150 Hz | 20 | 0 | 7 | 180 | 4 | 2,2 | 20 |
| QRM a +150 Hz | 20 | -4 | 3 | 180 | 9 | 5,0 | 20 |
| QRM a +150 Hz | 20 | -6 | 1 | 180 | 30 | 16,7 | 21 |
| QRM a +150 Hz | 20 | -8 | -1 | 180 | 153 | 85,0 | 20 |
| QRM a +150 Hz | 20 | -10 | -3 | 180 | 180 | 100,0 | 9 |
| Cambio ×1,6 | 20 | 10 | 17 | 180 | 7 | 3,9 | 31 |
| Cambio ×1,6 | 20 | 0 | 7 | 180 | 19 | 10,6 | 32 |
| Cambio ×1,6 | 20 | -4 | 3 | 180 | 19 | 10,6 | 32 |
| Cambio ×1,6 | 20 | -6 | 1 | 180 | 78 | 43,3 | 34 |
| Cambio ×1,6 | 20 | -8 | -1 | 180 | 151 | 83,9 | 25 |
| Cambio ×1,6 | 20 | -10 | -3 | 180 | 180 | 100,0 | 9 |

### Tono fijado a mano (pitch del equipo), a 20 WPM

| Escenario | WPM | S/R en 2500 Hz (dB) | S/R en 500 Hz (dB) | Caracteres | Errores | CER (%) | WPM medida |
|---|---:|---:|---:|---:|---:|---:|---:|
| Tono fijo 700 Hz | 20 | 0 | 7 | 180 | 4 | 2,2 | 20 |
| Tono fijo 700 Hz | 20 | -4 | 3 | 180 | 10 | 5,6 | 20 |
| Tono fijo 700 Hz | 20 | -6 | 1 | 180 | 38 | 21,1 | 21 |
| Tono fijo 700 Hz | 20 | -8 | -1 | 180 | 157 | 87,2 | 19 |
| Tono fijo 700 Hz | 20 | -10 | -3 | 180 | 180 | 100,0 | 9 |
| Tono fijo 700 Hz | 20 | -12 | -5 | 180 | 180 | 100,0 | 9 |
| Tono fijo 700 Hz | 20 | -14 | -7 | 180 | 180 | 100,0 | 9 |

### Ruido puro

30 minutos de ruido blanco sin señal, con ocho canales a la vez: **0 caracteres** escritos.

### Grabaciones reales

En el repositorio no hay ninguna grabación de telegrafía real. Se usa la de FT8 de 20 m
(`Aire/ft8-20m-2026-09-27-151745.wav`) como prueba de que el audio real de otro modo no escribe
texto basura (`DecodificadorCwPruebas`).

