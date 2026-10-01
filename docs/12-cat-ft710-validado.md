# 12 — CAT del FT-710 validado contra la radio real

Validado el **27-09-2026** con el FT-710 de EA8DLF encendido, **en solo lectura**: ni `TX1`, ni
`SV`, ni `AB`/`BA`, ni escrituras de frecuencia, modo, VFO o memorias. La unica orden que no es
consulta es el `TX0;` que el control manda siempre al desconectar (bajar el PTT, que ya estaba
abajo).

- Puerto: **COM15 a 115200** (el 25-09 estaba a 38400: la velocidad sigue cambiando, la
  autodeteccion por `ID;` la encuentra).
- Firmware: principal 01.12, pantalla 01.08, SDR 01.04, DSP 01.01.
- Respuestas en crudo guardadas como fixture de pruebas:
  `tests/Nodisla.Cuaderno.Radio.Pruebas/Capturas/ft710-2026-09-27.tsv`.
- Pruebas contra esa captura: `tests/Nodisla.Cuaderno.Radio.Pruebas/CapturaRealFt710Pruebas.cs`
  (canal en memoria, sin reloj).

## Los dos VFO

`ControlFt710` implementa `IEquipoConDosVfos`. En cada pasada del sondeo pregunta `VS; FA; FB;
ST; FT; MD0; MD1;` (mas `PC;` y `SM0;` para el estado). Leido de la radio durante 20 s con el
control de verdad:

| Consulta | Respuesta real | Interpretacion |
|---|---|---|
| `FA;` | `FA027555000;` | VFO A = 27.555.000 Hz |
| `FB;` | `FB027555000;` | VFO B = 27.555.000 Hz |
| `VS;` | `VS0;` | Activo: A |
| `MD0;` / `MD1;` | `MD02;` / `MD12;` | A en USB, B en USB |
| `ST;` | `ST0;` | Sin split |
| `FT;` | `FT0;` | Se transmite por A |
| `IF;` | `IF000027555000+000000200000;` | Coherente con lo anterior (no se usa) |
| `SM0;` | `SM0000;`…`SM0003;` | Medidor S en vivo, casi a cero |
| `PC;` | `PC005;` | 5 W |

37 avisos de cambio de estado en 20 s (el medidor S se mueve); ningun fallo de comunicacion.

Escrituras (solo cuando el operador pulsa, **no probadas contra la radio** por la regla de solo
lectura; probadas contra el canal de mentira): `VS0;`/`VS1;`, `FA`/`FB` + 9 cifras, `MD0x;`/`MD1x;`,
`AB;` (con A activo) / `BA;` (con B activo). `SV;` solo desde la accion de intercambio.

Pendiente de ver en la radio: que contesta `FT;` con el VFO B activo (hay que cambiar de VFO para
verlo). Si no contesta `FT0`/`FT1`, el control lo deduce del VFO activo y del split.

## Los 27 mandos

Todos contestan en el VFO principal (27 de 27). «Escritura» es el formato que genera el control;
ninguna se ha mandado a la radio.

| Mando | Consulta | Escritura | Respuesta real | Interpretacion | Estado |
|---|---|---|---|---|---|
| Volumen | `AG0;` | `AG0nnn;` | `AG0000;` | 0 (0-255) | Bien |
| Ganancia RF | `RG0;` | `RG0nnn;` | `RG0255;` | 255 | Bien |
| Ganancia de micro | `MG;` | `MGnnn;` | `MG096;` | 96 | Bien |
| Potencia | `PC;` | `PCnnn;` | `PC005;` | 5 W | Bien |
| Silenciador | `SQ0;` | `SQ0nnn;` | `SQ0000;` | 0 | Bien |
| Atenuador | `RA0;` | `RA0n;` | `RA00;` | Apagado | Bien |
| Preamplificador | `PA0;` | `PA0n;` | `PA00;` | Sin preamplificador (IPO) | Bien |
| AGC | `GT0;` | `GT0n;` | `GT06;` | Automatico lento | **Arreglado**: escribir 5 o 6 mandaba `GT05`/`GT06`, que el manual no admite; ahora 4-6 → `GT04` (automatico) |
| Supresor de ruido | `NB0;` | `NB0n;` | `NB01;` | Encendido | Bien |
| Nivel del supresor | `NL0;` | `NL0nnn;` | `NL0000;` | 0 | Bien |
| Reductor de ruido | `NR0;` | `NR0n;` | `NR00;` | Apagado | Bien |
| Nivel del reductor | `RL0;` | `RL0nn;` | `RL001;` | 1 | Bien |
| Muesca automatica | `BC0;` | `BC0n;` | `BC00;` | Apagada | Bien |
| Muesca manual | `BP00;` | `BP00nnn;` | `BP00000;` | Apagada | Bien |
| Frecuencia de la muesca | `BP01;` | `BP01nnn;` | `BP01001;` | 10 Hz | **Arreglado**: se ensenaba el indice (1); el manual CAT la da en pasos de 10 Hz y las dos capturas son coherentes (22-09: `BP01150`/`CO011500`; hoy `BP01001`/`CO010010`) |
| Contorno | `CO00;` | `CO00nnnn;` | `CO000000;` | Apagado | Bien |
| Frecuencia del contorno | `CO01;` | `CO01nnnn;` | `CO010010;` | 10 Hz | Bien |
| Ancho de filtro | `SH0;` | `SH0nnn;` | `SH0020;` | Indice 20 = 3000 Hz en USB | Bien |
| Desplazamiento de FI | `IS0;` | (solo lectura) | `IS00+0000;` | 0 Hz | Bien |
| Tono de CW | `KP;` | `KPnn;` | `KP40;` | 700 Hz | Bien |
| Velocidad del manipulador | `KS;` | `KSnnn;` | `KS020;` | 20 ppm | Bien |
| Break-in | `BI;` | `BIn;` | `BI0;` | Apagado | Bien |
| VOX | `VX;` | `VXn;` | `VX0;` | Apagado | Bien |
| Ganancia de VOX | `VG;` | `VGnnn;` | `VG070;` | 70 | Bien |
| Retardo de VOX | `VD;` | `VDnn;` | `VD08;` | 500 ms | Bien |
| Acoplador | `AC;` | `ACnnn;` | `AC001;` | Encendido | Bien (`AC002` sintoniza y exige PTT del vigilante) |
| Split | `ST;` | `STn;` | `ST0;` | Apagado | Bien |

**Resultado: 25 bien a la primera, 2 mal y arreglados** (AGC al escribir, frecuencia de la
muesca).

### Segundo VFO

| Consulta | Respuesta real | Conclusion |
|---|---|---|
| `AG1;` | `AG1000;` | Mando propio del B |
| `NB1;` | `NB11;` | Mando propio del B |
| `SQ1;` | `SQ0000;` | Contesta con el indice del A |
| `PA1;` | `PA00;` | Contesta con el indice del A |
| `IS1;` | `IS00+0000;` | Contesta con el indice del A |
| `SH1;` `NA1;` `RG1;` `GT1;` `RA1;` | `?;` | No existen |

**Arreglado**: el control daba por buenos en el B el silenciador, el preamplificador y el
desplazamiento de FI, pero la radio contesta con el valor del A. Ahora solo se ofrecen en el B los
que contestan con su propio indice: volumen y supresor de ruido.

## Otros fallos encontrados con la radio real

| Que | Antes | Ahora |
|---|---|---|
| Medidores crudos (`RM1`…`RM7`) | `RM1008000;` se leia como **8000** (seis cifras) | 8: tres cifras de valor y tres de relleno |
| `FA;`/`FB;` | Sin comprobar prefijo: una respuesta atrasada (`SM0003`) se leia como 3 Hz | Se exige `FA`/`FB` + 9 cifras; si no, se mantiene lo anterior |
| `VS;`/`ST;` | Un `?;` se tomaba como VFO A / sin split | Se mantiene lo que se sabia |
