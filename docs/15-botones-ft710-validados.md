# 15 — Botones del frontal del FT-710, pulsados contra la radio real

Fecha: **28-09-2026**, con el FT-710 de EA8DLF (COM15 a 115200, firmware 01.12/01.08/01.04/01.01)
y permiso de Jose para cambiar su estado y transmitir a 5 W. Ni el Cuaderno ni 710 Console
estaban abiertos. A las 13:38 hubo un **apagón** en casa de Jose; tras volver, se comprobaron los
ficheros (ninguno a medias) y se releyó la radio antes de seguir.

- Manual usado: **FT-710 CAT Operation Reference Manual** `FT-710_CAT_OM_ENG_2306-C.pdf`,
  descargado de yaesu.com y guardado en `docs/manuales/`.
- **Cómo se probó**: un banco (fuera del repositorio) que monta lo mismo que el programa
  —`VistaModeloEquipo` → `ControlEquipoConmutable` → `ControlFt710` → `CanalSerieCat` y el
  `VigilantePtt`— y ejecuta **los mismos comandos que las teclas** (`AlternarMandoCommand`,
  `PulsarTeclaCommand`, `TeclaDelAnalizadorCommand`, `CambiarFuncionCommand`, `MoxCommand`,
  `SintonizarAsync`, `GirarDialAsync`…). Tras cada pulsación se lee de vuelta la orden de consulta.
- Todo el ir y venir quedó en `tests/Nodisla.Cuaderno.Radio.Pruebas/Capturas/ft710-botones-2026-09-28.tsv`
  y el estado inicial en `Capturas/ft710-2026-09-28.tsv`.
- Además, una instancia apartada del programa (`CUADERNO_APARTADA`, `CUADERNO_CARPETA` temporal,
  nueva `CUADERNO_CONECTAR`) se conectó a la radio: «El equipo admite 51 mandos … y rechaza 0».

## Tabla por control

«Leído» es lo que contestó la radio a la consulta después de pulsar.

### Teclas

| Control | Orden CAT | Leído de vuelta en la radio | Estado |
|---|---|---|---|
| LOCK | `LK1;`/`LK0;` | `LK1` / `LK0`; con LOCK el dial dibujado no se mueve (FA igual) | Funciona |
| TUNE (clic) | `AC001;`/`AC000;` | `AC001` / `AC000`, `TX0` | Funciona, no emite |
| TUNE (clic derecho = mantener) | `AC003;` por el vigilante; al soltar `TX0;` (+`AC000;` si sigue) y `AC001;` | `RI0` pasa por `RI00010100` (TX, sintonizando) y acaba en ceros; `AC001`, `TX0` | Funciona (ver transmisiones) |
| VOX/MOX (clic) | `VX1;`/`VX0;` | `VX1` / `VX0`, `TX0` | Funciona |
| VOX/MOX (clic derecho = MOX) | `TX1;` por el vigilante, otro clic `TX0;` | `TX1`, `RI00010000` (TX); tras quitar, `TX0` | Funciona (ver transmisiones) |
| MODE | `MD0x;` (o `MD1x;` con el B activo) | `MD02`, `MD03`… | Funciona |
| ZIN/SPOT (clic = ZIN) | `ZI0;` | Aceptada; en CW y sin señal no mueve nada | Funciona (solo actúa en CW con señal) |
| ZIN/SPOT (clic derecho = SPOT) | `CS1;`/`CS0;` | `CS1` / `CS0`, `TX0` | Funciona |
| SPLIT | `ST1;`/`ST0;` | `ST1` (`FT1`) / `ST0` | Funciona |
| CLAR (clic) | `CF000` + RX + TX + `000;` (lee antes para no pisar el otro) | `CF00010000`; IF con P4 = 1 | **Funciona** (antes desactivada) |
| CLAR (rueda encima) | `CF001±nnnn;`, 10 Hz por muesca | `CF001+0030`; visor «RIT +30 Hz» | Funciona |
| CLAR (clic derecho) | `CF001+0000;` | `CF001+0000` | Funciona |
| NB | `NB01;`/`NB00;` | `NB01` / `NB00` | Funciona |
| DNR | `NR01;`/`NR00;` | `NR01` / `NR00` | Funciona |
| NAR | `NA01;`/`NA00;` | `NA01` / `NA00` | **Funciona** (antes desactivada) |
| M▶V | `MA;` | FA pasa a la memoria 001 (7.000.000 LSB) | **Funciona** |
| V/M | `VM;` | IF P7 = 1 (memoria) / 0; la tecla se ilumina en memorias | **Funciona** |
| A/B (clic) | `SV;` | `VS1` / `VS0`, FA y FB intactos | **Funciona** (ver hallazgos) |
| A/B (clic derecho = A=B) | `AB;` (pide confirmación) | FB = FA, MD1 = MD0 | Funciona |
| BAND (clic / clic derecho) | `BU0;` / `BD0;` | 10.125.000 USB (memoria de banda) y vuelta a 7.000.000 LSB | **Funciona** (antes saltaba al principio de banda con FA) |
| QMB (clic) | `QR;` | Aceptada; la pila QMB estaba vacía, así que no cambió nada | Funciona |
| QMB (clic derecho = guardar) | `QI;` (pide confirmación) | **No se mandó a la radio**: empuja la pila QMB de Jose y no se puede deshacer por CAT | Implementada, sin probar en la radio a propósito |
| DSP RESET | Sin orden CAT. Se emula: `IS00+0000; SH0000; BP00000; CO000000; CO020000;` | `IS00+0000`, `SH0020` (00 = «por omisión» del modo, 3000 Hz en USB), muesca, contorno y APF apagados | Funciona (emulada, rótulo lo dice) |
| FINE/FAST | `FN1;` → `FN2;` → `FN0;` | `FN1`, `FN2`, `FN0`; se ilumina con FINE o FAST | **Funciona** |

### Mandos

| Control | Orden CAT | Leído de vuelta | Estado |
|---|---|---|---|
| Dial (rueda / flechas) | `FA`/`FB` + 9 cifras desde IF/OI (sin clarificador) | Paso del equipo 20 Hz (menú 03-05-01 = 2): una muesca **+200 Hz** normal, **+20 Hz** FINE, **+2000 Hz** FAST; con LOCK, nada | **Funciona** |
| STEP/MCH (anillo) | VFO: `FA` a la rejilla de CH STEP (03-05-03 = 5 kHz); memorias: `CH0;`/`CH1;` | 7.002.020 → 7.005.000 → 7.000.000; en memorias `CH0` (solo hay una memoria, MC001 sigue) | **Funciona** |
| DSP (centro de STEP/MCH) | Función con `SF1n;` (clic = siguiente, clic derecho = anterior); gira el mando de esa función (IS, SH, BP01, CO01, CO03) | `SF13` (NOTCH) → `BP01151` / `BP01150` | **Funciona** |
| FUNC | Función con `SF0n;` (clic / clic derecho); gira el mando de esa función (PC, MG, PL, AO, VG, VD, AV, ML1, KS, KP, SD, SS04, SS01, SS03, DA) | `SF0E` (MONI LEVEL) → `ML1001` / `ML1000`; `SF0D` (RF POWER) | **Funciona**; M-GROUP no tiene orden CAT y se salta |
| RF GAIN/SQL | Anillo `RG0nnn;`, centro `SQ0nnn;` | `RG0192`/`RG0193`, `SQ0001`/`SQ0000` | Funciona |
| AF GAIN | `AG0nnn;` (un solo mando, como en la radio) | `AG0074`/`AG0073` | Funciona |

### Teclas de la pantalla

| Tecla | Orden CAT | Leído de vuelta | Estado |
|---|---|---|---|
| CENTER | `SS06` CENTER → CURSOR → FIX | `SS0670000`, `SS06A0000`, `SS0640000`; el rótulo dice el que está | **Funciona** |
| 3DSS | `SS06` 3DSS ↔ cascada | `SS0600000` / `SS0640000`; se ilumina en 3DSS | **Funciona** |
| EXPAND | `SS06` ampliado ↔ normal | `SS0630000` → se lee `SS0650000`; en 3DSS no hay ampliado por CAT (`SS06C/D/E` no hacen nada, 29-09): pasa a la cascada ampliada | **Funciona** |
| SPAN (clic / clic derecho) | `SS05n0000;` | `SS0580000` / `SS0570000` | **Funciona** |
| SPEED (clic / clic derecho) | `SS00n0000;` | `SS0030000` / `SS0020000` | **Funciona** |
| MULTI | — (sin orden CAT; no hace falta) | — | **Funciona en la pantalla del programa** (29-09): como la radio (manual de operación, pág. 23), el analizador arriba y el **osciloscopio** (10 ms/div) y el **AF-FFT** (0–4 kHz) del audio de recepción abajo. Salen del audio del codec USB que ya abre el módem (solo se escuchan sus bloques). Con el audio cerrado lo dice y ofrece abrirlo. No manda nada a la radio; su atenuador y su nivel/barrido no tienen CAT: aquí se ajustan solos |

**29-09-2026: las teclas cambian también el dibujo.** Hasta ahora la radio cambiaba pero la
pantalla del programa no: tras SPAN o SS06 las tramas del FT4222 llegan corridas un bit y el
analizador se congelaba (docs/14). Ahora cada tecla cambia al momento el rótulo, la escala
(en FIX, frecuencias absolutas), la vista 3DSS/cascada, el EXPAND (el analizador gana alto) y
la velocidad de la cascada; y lo que se toca en la propia radio llega por la trama (11/s) y
por el sondeo CAT. Además 3DSS y EXPAND no se iluminaban nunca (disparador mal enlazado en
`VisorDeVfos.xaml`): arreglado. Pruebas: `FrontalFt710Pruebas.Analizador`,
`AnalizadorDelEquipoPruebas`, `AnalizadorFt710Pruebas`, `TeclasTactilesDelVisorPruebas`,
`AnalizadorMultiplePruebas`. Capturas: `docs/capturas/analizador-teclas-antes-despues.png` y
`docs/capturas/analizador-multi.png`.

### Botonera lateral (29-09-2026): HAM y canales CB

**BS probada en la radio real el 29-09-2026** (sin transmitir, VFO B activo: VS1): `BS03;` → FB
7.074.000 DATA-U (`MD0C`, la pila de 40 m), `BS10;` → FB 50.000.000, `BS11;` (GEN) → FB
27.555.800, `BS05;` → FB 14.256.000 USB, como estaba. **FA no se movió nunca**: BS va al VFO
activo. Modos, memorias y canales CB: solo con el canal de captura (la prueba en la radio no se
pudo hacer). Pruebas: `BotoneraFt710Pruebas`, `FrontalFt710Pruebas`.

| Control | Orden CAT | Estado |
|---|---|---|
| Banda 1.8 … 50 | `BS00;` … `BS10;` (manual 2306-C; sin VFO: va al activo, pila de banda como BAND) | **Funciona** (radio real) |
| GEN | `BS11;` («70 MHz/GEN»; el FT-710 no tiene 70 MHz) | **Funciona** (radio real) |
| LSB · USB · CW · FM · AM | `MD01;` `MD02;` `MD03;` (CW-U) `MD04;` `MD05;` (MD0 = VFO activo) | Implementada |
| DATA | `MD08;` (DATA-L) por debajo de 10 MHz, `MD0C;` (DATA-U) por encima | Implementada |
| M1 … M6 | `MC001;` … `MC006;` | Implementada |
| Canal CB | `FA`/`FB` + 9 cifras al VFO activo (según `VS;`), tabla del plan elegido | Implementada |

### Lo que se cambia en la radio se ve en el frontal

Se cambió la radio por CAT en crudo, fuera del modelo (como si se tocaran sus teclas):
NB, DNR, NAR, LOCK, VOX, SPAN 100 kHz, 3DSS, FUNC = MIC GAIN, FINE/FAST, SPLIT. Tras una vuelta
del sondeo del frontal el dibujo lo enseñaba todo (`REFLEJO` en la captura). El frontal vuelve
a leer cada medio segundo los mandos que se ven (21) y tres del resto de la lista por vuelta;
un mando que el operador está moviendo en el programa no se relee durante 1,5 s.

## Resumen

- **Funcionan 36 controles** de 37: todas las teclas, los seis mandos (dial, FUNC, STEP/MCH,
  DSP, RF GAIN/SQL, AF GAIN) y cinco de las seis teclas de la pantalla, más los gestos de
  «mantener» (clic derecho) de TUNE, VOX/MOX, ZIN/SPOT, CLAR, A/B, BAND y QMB.
- **Sin orden CAT: MULTI** (rótulo honesto). DSP RESET tampoco tiene orden propia y se emula.
- **Implementado sin mandar a la radio a propósito: guardar en QMB (`QI;`)**.
- Pruebas: `BotonesFt710Pruebas` (Radio, 33) y `FrontalFt710Pruebas` (Ui, 40), con las
  respuestas reales; incluyen la garantía de que ningún botón salvo MOX/TUNE manda `TX1`, `TX2`,
  `MX1` o `AC003`.

## Hallazgos (lo que el manual no dice o dice mal)

1. **CLAR**: `RT`, `XT` y `RC` contestan `?;`; la orden es **`CF`** (`CF000` interruptores RX/TX,
   `CF001` desplazamiento). Con el clarificador de recepción puesto, **`FA;` devuelve la
   frecuencia con el desplazamiento sumado** (14.155.120 con +120 Hz); `IF;` la da sin sumar.
   El dial dibujado parte de IF/OI por eso.
2. **Poner SPLIT apaga el clarificador de recepción** (visto: `ST1` → `CF000` pasa a `00000000`).
3. **`SV;` no intercambia los VFO en el FT-710**: deja FA y FB igual y alterna `VS0`/`VS1`,
   que es lo que hace la tecla A/B. `IntercambiarVfosAsync` hacía creer que intercambiaba; ahora
   escribe cada VFO en el otro (modo primero, frecuencia después).
4. **Modo del analizador**: los ampliados se escriben 3/6/9 (manual) pero **se leen 5/8/B**;
   mandar 5/8/B directamente da `?;`. Tabla en `ModosDelAnalizadorFt710`.
5. **Acoplador**: «Tuning Start» es **`AC003`**; el programa mandaba `AC002`, que el manual deja
   en guion (no hacía nada). Mientras sintoniza, `RI0` trae P4 = 1 (TX) y P6 = 1; el indicador P6
   está en la **posición 7** de la respuesta.
6. Cambiar de LSB a USB **corre el dial** (14.155.000 → 14.153.600): al escribir modo y
   frecuencia, primero el modo.
7. `EX0501…` contestan `?;`: el menú de sintonía es el grupo **03-05** (`EX030501` paso del dial,
   `EX030503` CH STEP).
8. `BAND` del programa usaba `FA` al principio de la banda; ahora `BU0;`/`BD0;` como la tecla,
   con la memoria de banda del equipo.

## Transmisiones hechas

Todas por el camino del programa (vigilante del PTT), **a 5 W** (`PC005;` puesto con el mando
de potencia y comprobado antes de transmitir), y **`TX;` → `TX0;` comprobado tras cada una**:

| # | Qué | Duración en el aire | Resultado |
|---|---|---|---|
| 1 | MOX | 1,73 s | `TX1` durante, `TX0` después |
| 2 | TUNE (con el índice P6 mal leído) | < 0,5 s según `RI0` (la sesión vigilada duró 10,4 s, sin emitir) | `TX0`, acoplador apagado → corregido |
| 3 | TUNE | ≈ 7,6 s (de `AC003` a `RI00000000`), dentro del tope de 10 s | Sintonizado, `AC001`, `TX0` |

## Estado de la radio

- Estado inicial guardado a las 13:36 (antes de tocar nada). Tras el apagón la radio volvió con
  7 diferencias que no eran de las pruebas (PA, BP01, CO01, AG0/AG1, AC, SF1).
- Al terminar (15:21) se restauró todo lo que tocaron las pruebas: FB 18.100.000 USB, potencia
  98 W, IPO, muesca, contorno, SF1, clarificador a +0, etc. Lectura final de las 83 consultas del
  estado inicial: **iguales todas salvo FA (14.026.360 USB) y el volumen (AG0/AG1 = 73)**, que se
  cambiaron en la propia radio mientras tanto (las pruebas dejaron FA en 7.000.000 LSB y el
  volumen en su valor); se han dejado como las puso Jose. `TX;` → `TX0;`.

## VFO activo, spots y split (29-09-2026)

Jose veía que los cambios de frecuencia desde el cluster se mezclaban entre A y B, y que con el B
elegido en la radio el cuaderno seguía enseñando el A. Validado en la radio, sin transmitir:

- `FA`/`FB` son siempre el A y el B, pero **`MD0` y `FT0` son la banda principal**, que es el
  VFO elegido con `VS`. Con VS1, `MD01` cambió el modo del **B**. El programa leía `MD0` como
  modo del A y, con el B activo, mandaba el modo por `MD1`: cambiaba el VFO que no era. Y
  tomaba `FT0` por «transmite el A». Arreglado: modo del activo por `MD0`, del otro por `MD1`,
  y el VFO de transmisión según `FT` y `VS`.
- Cambiar `VS` **quita el split** (VS0+ST1 → VS1 → `ST0`).
- El spot pregunta `VS;` en el momento (no se fía del último sondeo) y escribe `FA` o `FB`.
- Cambiar de modo corre el dial (spot FT8 en 14.074.000 quedaba en 14.074.700): el spot pone
  frecuencia, modo y otra vez la frecuencia. En la radio: A activo → `FA014074000`, `MD0C`, B
  intacto; B activo → `FB007074000`, `MD0C` (B), A intacto.
- Carrera con el sondeo: la lectura del estado y cada «escribir y releer» van ahora de una en una.
  12 spots seguidos con el sondeo en marcha: **0** lecturas viejas publicadas.
- Visor: en grande el VFO activo (`Principal`) y en pequeño el otro (`Secundario`).
- Con split, `FREQ` = transmisión y `FREQ_RX` = recepción (el activo) en el contacto; el bandmap
  sigue la de recepción.
- Radio restaurada: las 83 consultas del estado de las 12:21 iguales; `TX0`.
