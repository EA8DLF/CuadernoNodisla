# 09 — Revisión botón a botón: Operar, barra del equipo y frontal del FT-710

Fecha: 27-09-2026. Alcance: `PanelOperar`, `BarraDelEquipo`, `FrontalFt710`, `VisorDeVfos`,
`MandoGiratorio`, `ListaDeMandos`, `PanelDeEntrada`, `PanelDeCluster`, `PanelDeBandmap`,
`PanelDeRetrato` y sus modelos, más el camino real del FT-710 (`ControlFt710` detrás de
`ControlEquipoConmutable`).

## Por qué «casi ningún botón funcionaba» con la radio real

Todo se había comprobado contra `EquipoSimulado`, que se salta el camino real. Con el FT-710 de
verdad fallaban tres cosas de fondo, y las tres se veían solo por ese camino:

1. **Los mandos se construían antes de conectar.** `VistaModeloEquipo` pedía la lista de mandos
   al montarse; el FT-710 no sabe qué mandos tiene hasta que se conecta y los pregunta
   («El equipo admite 27 mandos» salía *después*). La lista quedaba **vacía** para siempre:
   TUNE, VOX, SPLIT, NB, DNR, RF GAIN y AF GAIN no tenían nada detrás y no hacían nada.
2. **`ControlFt710` no implementaba `IEquipoConDosVfos`.** El visor solo sabe pintar A y B
   desde ahí: con la radio real, **guiones en los dos VFO** aunque el CAT contestara. Además
   A/B quedaba siempre apagado.
3. **Botones «vivos» sin mando.** Las teclas del frontal se podían pulsar con solo estar
   conectado, existiera o no el mando: CLAR (el FT-710 contesta `?;` a `RT`/`XT`) salía
   encendida y no hacía nada.

Y dos más que rompían cosas en silencio:

- Al primer fallo al escribir un mando, `VistaModeloMando` lo marcaba no disponible **para
  siempre** (un byte perdido bastaba para matar la tecla).
- TUNE pedía antena al vigilante (**subía el PTT, emitía portadora**) solo para mandar `AC001;`,
  que es poner el acoplador en línea y no emite.

## Cómo se ha comprobado

- **Pruebas nuevas** `tests/Nodisla.Cuaderno.Ui.Pruebas/OperarFt710Pruebas.cs` (14): el
  `ControlFt710` real detrás de `ControlEquipoConmutable`, con un canal CAT de mentira que
  contesta lo capturado en `docs/04-ft710-cat.md` y aplica las escrituras. Se pulsa cada orden
  del modelo y se mira la orden CAT que sale. El canal hace fallar la prueba si alguien manda
  `TX1;`, `MX1;` o `PS0;`. `SV;` solo se manda en la prueba que confirma A/B, contra el canal de
  mentira.
- **Escaneo de enlaces en la app viva** (instancia apartada, `CUADERNO_CAPTURA`), con la lista de
  mandos abierta (`CUADERNO_MANDOS=1`, nueva) y en modo real sin conectar. Se verificó que el
  escaneo detecta de verdad metiendo un enlace roto a propósito (lo detectó; retirado).
  Resultado en la zona de Operar: **ningún enlace roto** salvo `MemoriaElegida.Etiqueta` sin
  memoria elegida (arreglado con valor de reserva). Los ~50 restantes son de Ajustes y Satélites
  (de otros especialistas).
- Aviso: el rastreo por `PresentationTraceSources` (`CUADERNO_RASTREO_ENLACES`) **deja el fichero
  vacío** también en la app actual, aunque haya enlaces rotos; el que vale es el
  `.rotos.txt` que escribe `RetratoDeLaVentana`.
- **No se ha probado contra la radio real**: el acceso a COM15 lo bloqueó el sistema de permisos
  y después la radio se apagó. El camino real queda cubierto por las pruebas con canal de mentira;
  la validación en el equipo la lleva el especialista de CAT.

## Tabla de controles

Leyenda: **F** funciona · **R** roto (no hace nada / hace otra cosa) · **M** enlace muerto ·
**D** desactivado a propósito con rótulo.

### Barra del equipo

| Control | Enlace | Antes | Qué se hizo | Después |
|---|---|---|---|---|
| SOLTAR PTT | `Equipo.SoltarPttCommand` | F | — | F |
| Conectar / Desconectar | `ConectarCommand` / `DesconectarCommand` | R con FT-710 (conectaba, pero dejaba los mandos vacíos) | Rehace los mandos tras conectar y refresca las teclas | F (prueba) |
| Todos los mandos | `ListaDeMandosVisible` | F (lista vacía con FT-710) | ídem | F |
| Ocultar / Mostrar equipo | `FrontalDesplegado` → `FrontalDibujadoVisible` | R (arreglado hoy por Jose: faltaba el aviso) | Comprobado en el código y en captura | F |
| Estado, nombre, aviso | solo lectura | F | — | F |
| Cartel «Todavía no hay equipo conectado» | `FrontalDibujadoVisible` negado | R: salía también con el equipo CONECTADO y el frontal solo plegado | Sale solo sin equipo avanzado (`SinEquipoAvanzado`); plegado y conectado muestra una línea «Equipo plegado — sigue conectado» con «Mostrar equipo» (`FrontalPlegado`) | F (3 capturas) |

### Frontal del FT-710

| Control | Enlace / orden CAT | Antes | Qué se hizo | Después |
|---|---|---|---|---|
| LOCK | — | D | — | D |
| TUNE | `AlternarMando(Sintonizador)` → `AC001;`/`AC000;` | R (sin mando; y habría subido el PTT) | Mandos tras conectar; en línea/fuera sin PTT; «Sintonizar» (AC002) no se ofrece: rótulo «mantenga TUNE en el equipo» | F (prueba: `AC001;` sin `TX1;`) |
| VOX MOX | `Vox` → `VX1;` | R (sin mando) | Mandos tras conectar | F (prueba) |
| MODE | `SiguienteModoCommand` → `MD0x;` | R con VFO B activo (cambiaba el A) | Usa `MD1` si el B es el activo | F (prueba) |
| ZIN/SPOT, M▸V, V/M, QMB, NAR, DSP RESET, FINE/FAST | — | D | Rótulo visible también con la tecla apagada (`ShowOnDisabled`) | D |
| SPLIT | `Split` → `ST1;` | R (sin mando) | Mandos tras conectar | F (prueba) |
| CLAR | `Rit` | R (encendida sin hacer nada) | Se apaga si el equipo no tiene el mando; rótulo explica `RT`/`XT` → `?;` | D (con FT-710) |
| NB | `SupresorDeRuido` → `NB01;` | R (sin mando) | Mandos tras conectar | F (prueba) |
| DNR | `ReductorDeRuido` → `NR01;` | R (sin mando) | ídem | F (prueba) |
| A/B | `IntercambiarVfosCommand` → `SV;` | R (siempre apagado: sin `IEquipoConDosVfos`) | `IEquipoConDosVfos` en `ControlFt710`; **pide confirmación** (sin ella no manda nada) | F (prueba) |
| BAND | `SiguienteBandaCommand` → `FA…;` | F | — | F (prueba) |
| RF GAIN/SQL | `MandoDeGananciaRf` → `RG0nnn;` | R (sin mando) | Mandos tras conectar + aviso de las propiedades | F (prueba) |
| AF GAIN | `MandoDeVolumen` → `AG0nnn;` | R (sin mando) | ídem | F (prueba) |
| FUNC, STEP/MCH·DSP | — | D | — | D |
| Dial principal | — (decorativo, rótulo honesto) | D | — | D |
| Indicadores VFO A/B | `A`, `B` | M con FT-710 (guiones) | Ver visor | F |

### Visor (pantalla del equipo)

| Elemento | Antes | Qué se hizo | Después |
|---|---|---|---|
| VFO A / VFO B: frecuencia, modo, banda, TX | M con FT-710 (guiones) | `ControlFt710` lee `FA;` `FB;` `VS;` `ST;` `FT;` `MD0;` `MD1;` en cada sondeo y avisa aunque solo cambie el VFO inactivo; si un equipo no da los dos, el activo sale de su estado | F (prueba) |
| Zona del espectro | Lista del cluster | **Cascada del audio de recepción** (la del módem propio), eje en kHz desde el dial (USB suma, LSB resta). Sin audio: «ESPECTRO SIN AUDIO» + botón **Abrir el audio de recepción** (`Modem.EscucharCommand`, no transmite) | F (captura) |
| Memoria elegida | M sin memoria | `FallbackValue`/`TargetNullValue` «—» | F |
| Medidores, ATT/IPO/DNF/AGC, SPLIT/RIT/XIT, ROE, tensión | F | — | F |

### Lista de todos los mandos

| Control | Antes | Qué se hizo | Después |
|---|---|---|---|
| Casilla (interruptores) | R con FT-710 (lista vacía) | Mandos tras conectar | F |
| Desplegable (posiciones) | R: el acoplador ofrecía «Sintonizar», el control lo rechazaba y el mando moría | No se ofrece la posición que emite; un fallo de escritura vuelve a leer el mando en vez de apagarlo | F |
| Deslizador (escala) | R: `Value` antes que `Minimum`/`Maximum`; riesgo de recortar al máximo por omisión (10) y mandarlo al equipo; una orden CAT por píxel | `Minimum`/`Maximum` primero, `Delay=150`, ajuste a paso | F (captura: 100 W y 89 se conservan) |
| Agrupación por familia | R al rehacer (se agrupaba dos veces) | Se limpia antes de agrupar | F |

### Contacto nuevo, cluster, bandmap, retrato

Enlaces comprobados en código (todos los miembros existen) y en el escaneo de la app viva: sin
enlaces rotos. Sin cambios.

| Panel | Controles | Estado |
|---|---|---|
| Contacto nuevo | Intro (registrar), Menos datos (F7), indicativo, banda, modo, frecuencia, RST env/rec, Registrar, Limpiar, fecha, hora, hora automática, Ahora (F8), nombre, QTH, localizador, comentario (18) | F |
| Cluster | Conectar/Desconectar, filtros de banda/modo/continente, Solo lo nuevo, Sin escucha automática, Quitar filtros, Vaciar, lista (selección y doble clic), Ir a este spot, pestañas Cluster/Bandmap (12) | F |
| Bandmap | Solo lo que aporta, Solo mi modo, Sin escuchas automáticas, clic en marca (4) | F |
| Retrato | solo lectura | F |

## Recuento

- **74 controles** interactivos inventariados (sin contar las casillas/deslizadores de la lista,
  que salen uno por mando declarado: 27 con el FT-710).
- **Rotos antes, con la radio real: 17** (TUNE, VOX, MODE con VFO B, SPLIT, CLAR, NB, DNR, A/B,
  RF GAIN, AF GAIN, Conectar —mandos vacíos—, visor A/B, los tres tipos de control de la lista,
  la agrupación y la memoria elegida). Causa común: el camino real no se había probado nunca.
- **Arreglados: 15.** **Desactivados con rótulo honesto: CLAR** (no hay `RT`/`XT` en el FT-710)
  y la posición «Sintonizar» del acoplador (se hace en el equipo).

## Ficheros tocados

- `src/Nodisla.Cuaderno.Radio/Control/Ft710/ControlFt710.cs` — `IEquipoConDosVfos`, MODE en el
  VFO activo, acoplador en línea sin PTT, aviso al cambiar el VFO inactivo.
- `src/Nodisla.Cuaderno.Ui/VistaModelos/VistaModeloEquipo.cs`, `VistaModeloMando.cs`.
- `src/Nodisla.Cuaderno.Ui/Vistas/FrontalFt710.xaml`, `VisorDeVfos.xaml`, `ListaDeMandos.xaml`.
- `src/Nodisla.Cuaderno.Ui/Conversores/EtiquetaDelEspectro.cs` (nuevo).
- Comunes, edición mínima: `VentanaPrincipal.xaml.cs` (confirmación de A/B y A=B),
  `VistaModeloPrincipal.cs` (`CUADERNO_MANDOS`).
- `tests/Nodisla.Cuaderno.Ui.Pruebas/OperarFt710Pruebas.cs` (nuevo).

## Pendiente / avisos

- **Fallo de seguridad encontrado, no arreglado aquí (es del CAT):** al **cerrar** el programa sin
  haber conectado nunca, `ControlFt710.DesconectarAsync` recorre las vías de suelta del PTT y la
  de «reabrir el puerto y TX0» **abre el puerto serie configurado** y manda `TX0;`. Visto en el
  registro de la instancia de verificación (COM16 a 4800). No transmite, pero toca un puerto que
  nadie pidió abrir. Avisado al coordinador.
- Validar en la radio real (lo lleva el especialista de CAT): `FT;` como indicador de VFO de
  transmisión, y `AB;`/`BA;`/`SV;` solo desde el botón y confirmados.
- El dial grande no gira con la rueda (rótulo honesto: se gira en el equipo).

## Capturas

- `capturas/operar-estado-sin-equipo.png` — sin equipo CAT: cartel «Todavía no hay equipo conectado».
- `capturas/operar-estado-conectado-plegado.png` — conectado y plegado: línea «Equipo plegado», sin el cartel falso.
- `capturas/operar-estado-conectado-desplegado.png` — conectado y desplegado: frontal con A/B y espectro.

- `capturas/operar-rev-frontal-espectro.png` — simulado: A y B con frecuencia y la cascada con el
  eje del dial en la pantalla del frontal.
- `capturas/operar-rev-lista-de-mandos.png` — simulado con la lista de mandos abierta.
- `capturas/operar-rev-sin-conexion.png` — FT-710 configurado sin conectar: teclas apagadas y
  «ESPECTRO SIN AUDIO» con su botón.
