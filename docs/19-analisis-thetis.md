# 19 · Análisis de Thetis (ramdor/Thetis): qué copiar y qué no

Fecha: 02-10-2026. Este documento es **solo análisis**: no se ha tocado el código del Cuaderno ni la radio.

Thetis se revisó en el commit `852bf0e` (02-07-2026), clonado en una carpeta temporal. La última versión publicada es la v2.10.3.15 (02-05-2026). El repositorio tiene 131 estrellas, 53 forks y 82 issues abiertas.

Rutas de Thetis usadas en el documento:

- `Console\…` = `Project Files\Source\Console\`
- `wdsp\…` = `Project Files\Source\wdsp\`
- `CM\…` = `Project Files\Source\ChannelMaster\`

Los números de línea son orientativos y pueden moverse entre versiones.

---

## 0. Resumen en diez líneas

1. Thetis es una consola SDR para radios OpenHPSDR (ANAN, Hermes, Hermes Lite 2). Está escrita en **C# .NET Framework 4.8 + WinForms**, con el DSP en C (**WDSP**, de NR0V) y el pintado con **SharpDX/Direct2D**, que es un proyecto archivado.
2. Su autor, MW0LGE, la archivó en abril de 2026 por deuda técnica (.NET 4.8, SharpDX). En julio de 2026 la reactivó solo para el acceso remoto. **No es un modelo de arquitectura**, pero sí es una **mina de algoritmos y de detalles de operación** muy pulidos.
3. **La licencia permite portar.** Prácticamente todo el código lleva «GPL versión 2 **o posterior**», así que **se puede portar al Cuaderno (GPL-3.0)**, conservando los avisos de copyright y anotando los cambios.
4. Lo que más valor da **ya, con el FT-710**:
   - spots del cluster dibujados sobre el analizador y clic para sintonizar;
   - suelo de ruido y AGC de la cascada;
   - **reductor de ruido y notch en la fonía por el PC** (RNNoise, NR2 y ANF de WDSP);
   - **voice keyer y grabación**;
   - medidores con aguja calibrada;
   - un **servidor TCI y/o rigctld propio**, para que otros programas usen la radio a través del Cuaderno;
   - las **salvaguardas de TX** que aún le faltan al vigilante: ROE, plan de banda y «no rearmar hasta soltar todo».
5. **Soporte OpenHPSDR (Hermes Lite 2/ANAN): solo más adelante.** Es un proyecto de tamaño XL, porque hay que demodular IQ y llevar un panadaptador propio. Jose no tiene esa radio. Vale la pena dejarlo preparado en la arquitectura, pero no abordarlo ahora.
6. **Qué no copiar:**
   - el «objeto dios» `Console` (54.000 líneas);
   - la serialización posicional con `|`;
   - los ~270 `catch {}` vacíos;
   - la ausencia total de pruebas;
   - el TCI sin autenticación con `run_cat_ex`, que deja pasar CAT arbitrario;
   - el SDK de ASIO, cuya licencia es incompatible con la GPL.

---

## 1. Licencias: qué se puede portar y cómo

| Parte | Licencia verificada | ¿Compatible con GPL-3.0? | Nota |
|---|---|---|---|
| Thetis (`LICENSE`) | GPL-2.0 (texto) | Sí, vía «o posterior» | 347 de 452 ficheros `.cs/.c/.h` dicen «version 2 … or (at your option) any later version» |
| Contribuciones de MW0LGE | GPL-2.0+, más una declaración de doble licencia del autor (`LICENSE-DUAL-LICENSING`) | Sí | La doble licencia solo amplía los derechos del autor; a nosotros nos llega la GPL |
| **WDSP** (Warren Pratt, NR0V) | GPL-2.0+ (por ejemplo, `wdsp\anr.c:7-10`) | **Sí** | Es la pieza más valiosa |
| `display.cs` (FlexRadio, W5WC, MW0LGE) | GPL-2.0+ | Sí | |
| `TCIServer.cs`, `SpotManager2.cs`, `MeterManager.cs`, `clsAudioRecordPlayback.cs`, `clsSpectrumProcessor.cs` | GPL-2.0+ (MW0LGE) | Sí | |
| Ficheros **sin cabecera**: `CAT\TCPIPcatServer.cs`, `rxa.cs`, `wideband.cs`, `RawInput\*`, `wdsp\calculus.c`, `FDnoiseIQ.c`, `CM\cmUtilities.c` | Solo queda el `LICENSE` del repositorio (GPL-2.0, sin «o posterior») | **Dudoso** | No portar literalmente. `TCPIPcatServer.cs` dice además estar «inspirado» en un artículo de CodeProject (CPOL) |
| RNNoise (Xiph), usado por `wdsp\rnnr.c` | BSD-3 | Sí | Hay que reproducir el aviso BSD |
| libspecbleach, usado por `wdsp\sbnr.c` | LGPL-2.1+ | Sí | |
| FFTW 3 | GPL-2.0+ | Sí | Mejor no usarlo: en .NET basta una FFT gestionada |
| PortAudio 19.7 | MIT | Sí | No hace falta: NAudio/WASAPI ya están |
| **ASIO SDK 2.3.3** (`cmASIO\asiosdk_*`) | Licencia propietaria de Steinberg | **No** | No tocar |
| Tablas de datos de WDSP (`GG`/`GGS` de `emnr.c`, `zetaHat.c`) | GPL-2.0+ (Warren Pratt) | Sí | Hay que llevar las tablas tal cual |

**Ojo:** en la raíz del Cuaderno **no hay fichero `LICENSE`**, y `Directory.Build.props` tampoco declara la licencia. Antes de portar la primera línea de Thetis conviene hacer tres cosas:

1. Añadir `LICENSE` con el texto de la GPL-3.0.
2. Crear `docs/AVISOS-DE-TERCEROS.md`, o ampliar el que haya, con WDSP, Thetis, RNNoise y libspecbleach.
3. En cada fichero portado, poner una cabecera de este estilo:

```
// Portado de WDSP (wdsp/anr.c), Copyright (C) 2012, 2013 Warren Pratt, NR0V,
// GPL-2.0-or-later. Traducción a C# y cambios: NODISLA, 2026. Este fichero: GPL-3.0-or-later.
```

**Reglas prácticas:**

- **Algoritmos de WDSP: portar.** Son matemáticas que han funcionado bien durante diez años; reescribirlas desde cero solo añade errores.
- **Lógica de UI de Thetis: reimplementar.** El valor está en la idea (carriles de spots, NF, agujas calibradas), no en el código WinForms/SharpDX, que no encaja en WPF.
- **No portar ficheros sin cabecera.** Basta con estudiarlos.

---

## 2. Arquitectura de Thetis (lo esencial)

```
Console (C#, WinForms)  ──P/Invoke──►  ChannelMaster.dll (C)  ──►  wdsp.dll (C, FFTW)
  console.cs 54k líneas                  red P1/P2, anillos,          RXA/TXA: un hilo por canal,
  setup.cs 37k · MeterManager 44k        VAC (PortAudio), mezcla,      bloques de 4096 a 48 kHz,
  display.cs 12k (SharpDX)               NB/NB2 sobre IQ, TCI audio    MMCSS «Pro Audio»
```

- **ChannelMaster** (`CM\cmaster.c:xcmaster`) hace lo siguiente:
  - recibe el IQ de la red (`CM\networkproto1.c` P1, `CM\network.c` P2);
  - aplica NB/NB2 sobre IQ;
  - alimenta el panadaptador (`Spectrum0`) y los canales WDSP;
  - mezcla audio (`aamix.c`) y lo saca por VAC (`ivac.c`, PortAudio y **`rmatch`** para la deriva de reloj).
- **WDSP, cadena RX** (`wdsp\RXA.c:xrxa`, l.638-683):
  1. shift → remuestreo → **notches + filtro** (`nbp`) → S-meter;
  2. demodulación AM/FM → **SNBA** → EQ;
  3. **ANF/NR/NR2/NR3/NR4** antes o después del AGC;
  4. **AGC** (`wcpAGC`);
  5. filtros de CW (adaptado, gaussiano, APF) → **SSQL** → salida.
- **WDSP, cadena TX** (`wdsp\TXA.c:xtxa`, l.557-591):
  1. ganancia de micrófono → expansor → EQ;
  2. **leveler → CFC** (compresor multibanda) → compresor → **CESSB** (`osctrl`);
  3. **ALC** → moduladores → **PureSignal** (`iqc`).
- **Hilos:** red, buffers, mezclador, un hilo por canal de DSP y un hilo de pantalla. Se sincronizan con `CRITICAL_SECTION` y semáforos. Hay unos 73 `new Thread(` y unos 72 temporizadores en C#.
- **Persistencia:** un `DataSet` XML (`database.cs`) con tablas clave-valor.
  - Lo bueno: un gestor de **perfiles con copia de seguridad GFS** (`clsDBMan.cs`).
  - Lo malo: la serialización posicional con `|`.

**Comparación con el Cuaderno:**

- El Cuaderno ya está **mejor diseñado**: .NET 8, puertos e interfaces, `IHostedService`, 1.680 pruebas, vigilante de PTT en hilo propio y núcleo sin WPF.
- Thetis no tiene **ni una sola prueba** (ni NUnit, ni xUnit, ni MSTest).
- Thetis tiene unos 2.600 `Invoke/BeginInvoke` y unos 930 campos estáticos.
- **Conclusión:** de Thetis se toman algoritmos e ideas de operación, nunca estructura.

---

## 3. Comparación punto por punto

Leyenda de esfuerzo:

| Clave | Duración aproximada |
|---|---|
| S | Unos días |
| M | 1-2 semanas |
| L | 3-6 semanas |
| XL | Meses |

### 3.1 Panadaptador y cascada

| Aspecto | Thetis (dónde) | Cuaderno hoy |
|---|---|---|
| Cálculo | FFT propia sobre IQ, 7 ventanas normalizadas por ganancia coherente y ENB (`wdsp\analyzer.c:new_window`, l.52-177); solape calculado para los FPS pedidos (`HPSDR\specHPSDR.cs:532`) | El FT-710 entrega la traza ya calculada: 850 puntos por FT4222 a unas 11 tramas/s (`Radio\Espectro\TramaDelAnalizadorFt710.cs`). La cascada de audio de la pestaña Digital lleva FFT propia con Hann y solape (`Audio\Cascada\*`) |
| Detectores | Peak, **Rosenfell**, Average, Sample, RMS (`analyzer.c:detector`, l.283-462), independientes para panorama y cascada | Ninguno: se pinta la traza suavizada |
| Promedios | Peak hold, lineal recursivo, ventana deslizante y **log recursivo** con `b = exp(-1/(fps·τ))` (`analyzer.c:avenger`, l.464-554; `specHPSDR.cs:351-363`) | Suavizado simple en `PintorDelAnalizador` |
| Suelo de ruido (NF) | `display.cs:processNoiseFloor` (l.5872-5918): media lineal de los píxeles por debajo de NF+2 dB; si no hay bastantes, NF += 1 dB por cuadro; *lerp* con ataque de unos 2 s y **ataque rápido al cambiar de banda** | No hay |
| AGC de cascada | Umbral bajo = NF (o una EMA 0,6/0,4 del mínimo de fila) − offset, **con caché por banda** (`display.cs:6464-6474`, `2748-2829`) | Escala fija en rojos |
| Colores | 8 esquemas y una LUT de 101 colores con gradientes editables (`ucGradientDefault.cs:57-71`) | Un esquema (rojos) |
| Al resintonizar | La historia de la cascada **se desplaza** los píxeles equivalentes al salto (con resto fraccional) y se **congela** durante el llenado de la FFT (`display.cs:6212-6263`, `6364-6378`) | La cascada sigue igual; en CENTER, el salto mancha la historia |
| Rejilla | Paso automático `{10,20,25,50}·10^n` (`display.cs:8951`), bordes de banda por región, filtros y notches dibujados, escala de dB arrastrable y guardada por banda | Rótulo de CENTER/SPAN/SPEED y escala de frecuencias; sin rejilla en dB |
| Ratón | Clic para sintonizar con **ajuste al paso** (`console.cs:adjustForSnapClickTuning`, l.33400), rueda (Mayús = paso/10), arrastre de filtros y notches, **CTUN** | **No hay clic para sintonizar** sobre el analizador (`AnalizadorDelEquipo.xaml.cs` no atiende ratón) |
| Picos | Detector de picos con **histéresis de 10 dB** y top-N; etiqueta «dBm (+dB sobre NF)» (`display.cs:5291-5321`, `processMaximums` l.4463); **pico activo** con retención y caída en dB/s (`display.cs:5333-5365`) | No hay |
| SNR en pasabanda | `S − (NF − 10log RBW + 10log BW_filtro)` (`console.cs:21126-21171`) | No hay |
| Hora en cascada | Marcas HH:mm:ss alineadas según ms/línea (`display.cs:5989-6166`) | No hay |
| Rendimiento | Hilo de pantalla propio con medición de cuadros tardíos (cuadrado rojo o amarillo), `ArrayPool`, relleno con líneas verticales, se saltan puntos con la misma Y | `WriteableBitmap`; correcto para 11 tramas/s |

**Bug hallado en Thetis** (útil si se porta el NF): en `display.cs:5770-5778`, el *getter* de `NFshiftDBM` se llama a sí mismo y desborda la pila. Además, la cota inferior asigna `12` en vez de `-12`.

### 3.2 Spots del cluster sobre el panorama

- **Thetis** (`SpotManager2.cs` y `display.cs:drawSpots`, l.11230-11552):
  - **No tiene cliente de cluster propio.** Los spots le llegan **por TCI** (`spot:call,mode,freq,argb,texto|JSON`, `TCIServer.cs:4339-4505`), normalmente desde Log4OM, SDC o N1MM.
  - Asignación por **carriles voraz**: se ordena por frecuencia y cada spot va al primer carril libre (`getSpotLayer`, `display.cs:11159-11205`).
  - La etiqueta se ancla a la izquierda en LSB y a la derecha en USB; el texto sale blanco o negro según la luminancia del fondo.
  - Los spots de menos de 120 s **parpadean**.
  - Deduplicación: mismo indicativo a ±5 kHz o menos.
  - Máximo de 100 spots, caducidad de 60 minutos y TTL por spot.
  - Bocadillo con spotter, rumbo, distancia, país y edad.
  - Clic sobre el spot: sintoniza y pone el modo (USB o LSB según la frecuencia; FT8 en DIGU). Clic derecho: abre QRZ.
  - **Bandera** por el prefijo más largo de cty (`clsCountryData.cs:101`, `clsFlagAtlas.cs:104`).
- **Cuaderno:**
  - Ya tiene **lo que a Thetis le falta**: cluster Telnet con varios nodos (`Integraciones\Cluster\FuenteDeVariosNodos.cs`), bandmap (`VistaModeloBandmap`), DXCC por cty, trabajado antes y matriz de novedad.
  - Pero **no dibuja los spots sobre el analizador del FT-710**.

### 3.3 Medidores

- **Thetis** (`MeterManager.cs`, 44.516 líneas):
  - Unos 30 tipos de medidor.
  - **Calibración de aguja** con una tabla `Dictionary<float, PointF>` (valor → punto 0..1 sobre la imagen), búsqueda binaria e interpolación lineal (unas 260 llamadas a `ScaleCalibration.Add`; renderer en l.41050-41110).
  - Suavizado **asimétrico** con ataque 0,8 y caída 0,2 (`clsNeedleItem.Update`, l.~22013).
  - Retención de pico por ventana de historial.
  - Histórico en gráfica.
  - Contenedores flotantes y acoplables.
  - Texto con variables (`%READING%`, variables CAT).
  - LED con condiciones C# compiladas con Roslyn (`clsMeterScriptEngine.cs`).
  - Skins con imágenes descargables (`clsThetisSkinService.cs`, `skin_servers.json`).
- **Cuaderno:**
  - `MedidorDeArco` y `MedidorDeBarra` con S, Po, ROE, ALC, Idd y Vdd por CAT (`VistaModeloEquipo.cs:1022-1034`).
  - Sin retención de pico.
  - Sin ataque/caída separados.
  - Sin histórico.
  - Sin calibración por tabla documentada contra los valores crudos `RM` del FT-710.

### 3.4 DSP: lo aplicable al audio que sale del FT-710 por USB (48 kHz real, no IQ)

| Bloque WDSP | Algoritmo | ¿Sirve sobre audio? | Coste/latencia | Portar |
|---|---|---|---|---|
| **ANR / NR1** (`anr.c`, 239 líneas) | LMS normalizado con *leakage* adaptativo; 64 taps, retardo 16 | **Sí** | Unos 6 MFLOP/s; latencia 0 | Trivial (~100 líneas de C#) |
| **ANF** (`anf.c`) | Mismo LMS; la salida es el error, así que quita portadoras | **Sí** | Igual | Trivial |
| **EMNR / NR2** (`emnr.c`, 1.365 líneas) | STFT 4096 con solape 4; ruido por estadística mínima o SPP/MMSE; ganancia MMSE-STSA o LSA o tablas GG/GGS; postfiltro | **Sí** | Bajo; ~85 ms de latencia de bloque | M; hay que llevar las tablas GG/GGS y zetaHat |
| **RNNR / NR3** (`rnnr.c` + RNNoise) | Red neuronal recurrente en tramas de 480 muestras (10 ms a 48 kHz), con AGC previo | **Sí, ideal para SSB** | Bajo; 10 ms | S con P/Invoke a `rnnoise.dll` (BSD) o M con un port gestionado |
| **SBNR / NR4** (`sbnr.c` + libspecbleach) | Sustracción espectral adaptativa, tramas de 20 ms | Sí | Bajo | S con P/Invoke (LGPL) |
| **SNBA** (`snb.c`, 862 líneas) | NB espectral: modelo AR de orden 64 e interpolación de impulsos | **Sí** (trabaja tras el demodulador) | Medio; ~21 ms | M |
| NB / NB2 (`nob.c`, `nobII.c`) | Blanking sobre \|IQ\| | **No**: el filtro de FI de la radio ya ha ensanchado el impulso | — | No |
| Notches manuales (`nbp.c`) | FIR multibanda con hasta 1.024 notches que siguen la sintonía | Sí, como «notch manual» en la fonía | Bajo | M (convolución rápida) |
| AGC (`wcpAGC.c`) | *Look-ahead* y máquina de 5 estados con hang y fast-decay | Sí, como **nivelador de la escucha por el PC** | Bajo | S-M |
| SSQL (`ssql.c`) | Squelch de SSB por cruces por cero | Sí | Muy bajo | S |
| Filtros CW (`matchedCW.c`, `apfshadow.c`) | Filtro adaptado, gaussiano, APF | Sí, para CW por el PC **y para el decodificador CW** | Bajo | S |
| **RMATCH / VARSAMP** (`rmatch.c`, `varsamp.c`) | Remuestreo de razón variable (0,96-1,04) con lazo sobre la ocupación del anillo y *crossfade* ante under/overflow | **Sí, clave**: enlaza la tarjeta del FT-710 con los altavoces, o el micrófono del PC con el FT-710, sin cortes por deriva de reloj | Bajo | M (~1.000 líneas) |
| TX: leveler, CFC, compresor, CESSB, EQ (`wcpAGC`, `cfcomp.c`, `compress.c`, `osctrl.c`, `eq.c`) | Procesado de micrófono | Sí, para el **micrófono del PC → FT-710** | Bajo | M-L; **riesgo de salpicaduras y ALC** |
| PureSignal (`calcc.c`, `iqc.c`), diversity (`div.c`) | Predistorsión y combinación de IQ | **No** sin un SDR | — | — |

**Cuaderno hoy:**

- La fonía por el PC (`Audio\Fonia\ControlDeFonia.cs`, `PuenteDeAudioWasapi.cs`) solo tiene **ganancia y remuestreo**.
- No tiene NR, notch, AGC ni filtro.
- No compensa la deriva de reloj (habría que confirmar cómo resuelve hoy `PuenteDeAudioWasapi` el under/overflow).

### 3.5 TCI, CAT e integración con otros programas

- **TCI en Thetis** (`TCIServer.cs`, 9.342 líneas):
  - Protocolo ExpertSDR3 2.0, con emulación de SunSDR2PRO activada por defecto.
  - WebSocket **implementado a mano** sobre `TcpListener`: handshake SHA1, sin fragmentación y con ping cada 20 s.
  - Cola de salida con prioridades y **coalescencia por clave** (vfo, if, drive…), además de límite de frecuencia de mensajes (l.1640-1678, 6429-6478).
  - Unos 90 comandos.
  - **Audio e IQ binarios** con cabecera de 64 bytes (int16, int24, int32, float32), y audio de TX desde el cliente.
  - Escucha en 127.0.0.1:50001, **sin autenticación**.
  - `run_cat_ex` deja ejecutar CAT arbitrario.
- **CAT en Thetis:**
  - Emula el Kenwood TS-2000 con unos 95 comandos, más 313 `ZZxx` propios (`CAT\CATParser.cs`, validados contra `CATStructs.xml`).
  - Hasta 8 puertos serie (clases copiadas y pegadas).
  - CAT por TCP en el puerto 13013.
  - PTT y manipulación por CTS/DSR desde com0com.
  - Mini-lenguaje de macros CAT (`clsCatAtonic.cs`).
- **N1MM:** envía el espectro por UDP al puerto 13064 (`N1MM.cs:336-424`).
- **Cuaderno hoy:**
  - Es **cliente** de muchas cosas: rigctld, OmniRig, WSJT-X/JTDX por UDP, FLDigi y N1MM por UDP.
  - **No tiene ningún servidor**: ni TCI, ni rigctld, ni Kenwood. El Cuaderno abre el COM del FT-710 en exclusiva, así que **ningún otro programa puede usar la radio** mientras está abierto.

### 3.6 Grabación, voice keyer y CW

- **Grabación** (`clsAudioRecordPlayback.cs`):
  - Graba recepción (IQ o audio) y transmisión (micrófono o IQ).
  - Formatos: float32 y PCM de 32, 24, 16 y 8 bits, con *dither*; metadatos en JSON; MP3 opcional.
- **Voice keyer** (`MeterManager.clsVoiceRecordPlay`, l.10342):
  - 64 ranuras, con atajos de teclado; Mayús+clic graba.
  - **Repetición con pausa**, que da un CQ en bucle.
  - Ganancia por ranura.
  - CAT: `ZZJP`, `ZZJR`, `ZZJS`.
  - **Cualquier cambio de MOX aborta la reproducción** (`OnPreMox`, l.559), y al reproducir se apagan el EQ y el compresor.
- **CWX** (`cwx.cs`):
  - 9 memorias.
  - Temporizador multimedia `timeSetEvent` de 1 ms (l.215, 257, 404).
  - Retardo de PTT y *drop delay* ≥ 1,5 × retardo de PTT.
  - Repetición con `"`.
  - **Sin variables de indicativo ni de número de serie**: el Cuaderno ya va por delante con `ExpansorDeMacrosCw`.
  - En CAT, `KY` va a CWX y `keyer:` por TCI lleva un **vigilante de 3 s**.
- **Cuaderno hoy:**
  - Decodificador CW propio.
  - Transmisión CW por macros en desarrollo (`Ui\Telegrafia\*`, `Radio\Cw\EmisorCw.cs`, `Concursos\Telegrafia\ManipuladorPorAudio.cs`).
  - **No tiene grabación ni voice keyer.**

### 3.7 Band stack, memorias, ajustes y utilidades

| Thetis | Cuaderno |
|---|---|
| **Band stack** con entradas (frecuencia, modo, filtro, zoom, bloqueo, descripción) y filtros por banda o modo (`clsBandStackManager.cs:114-201`, `frmBandStack2.cs`) | Botonera de bandas (`IrABandaCommand`); el band stack queda en la radio |
| Memorias en XML (`Memory\MemoryList.cs`); **no hay escáner** (la columna `Scan` está oculta) | Memorias de la radio por CAT (`IEquipoAvanzado`) |
| **Quick recall**: guarda la frecuencia si se mantiene 4 s (`ucQuickRecall.cs`) | No hay |
| **Buscador de ajustes** (`frmFinder.cs`): indexa los controles de Setup con sinónimos y resalta el control | Ajustes por apartados, sin buscador |
| **Perfiles de base de datos** con copia al arrancar y al cerrar, **poda GFS**, carpeta «broken» y fusión al subir de versión (`clsDBMan.cs:1218`, `pruneForGFS` l.1929) | Un `ajustes.json` y un `cuaderno.sqlite`; hay copias de seguridad, pero la política está por confirmar |
| Perfiles de TX (`TXProfile`) | No aplica (el FT-710 guarda los suyos) |
| `clsDPISafeTools`: devuelve las ventanas a una pantalla visible | Confirmar si el Cuaderno lo hace con varios monitores |

### 3.8 Seguridad de transmisión

**Salvaguardas de Thetis:**

1. **Prioridad de las fuentes de PTT** (TCI → CAT → CW → MIC → VOX) en `PollPTT` (`console.cs:25393`, cada 1 ms). **Solo la fuente que activó el MOX puede soltarlo.**
2. **Tras un TOT no se rearma** hasta que se sueltan *todas* las fuentes (l.25413). Viene de un fallo real (issue #518: un PTT externo reactivaba la transmisión en bucle).
3. **TOT** con **«Ping ToT»** (`TimeOutTimerManager.cs`): durante TX hace ping a un host cada segundo y corta si no contesta, para detectar el enlace caído en operación remota.
4. **TX Inhibit** por entrada hardware (`PollTXInhibit`, l.25779).
5. **Plan de banda por región** antes de MOX: `CheckValidTXFreq` (l.6708) comprueba **los bordes del filtro de TX**, no solo la portadora (`BandStackManager.IsOKToTX`). También gestiona los 60 m y la prohibición de transmitir en una banda distinta de la de RX.
6. **ROE** (`PollPAPWR`, l.25911):
   - antena abierta (fwd > 10 W y fwd − rev < 1 W) → corte inmediato;
   - ROE > límite (2,0 configurable, issue #221) durante **4 lecturas seguidas** → *foldback* `limit/(swr+1)` o corte hasta el siguiente MOX;
   - en TUNE se ignora por debajo de cierta potencia.
7. Retardos de secuencia: RF 30 ms, MOX 10 ms, key-up 10 ms y salida de PTT 20 ms. Potencia máxima por banda y potencia de TUNE separada.
8. La grabación o reproducción se aborta ante cualquier cambio de MOX. El keyer TCI tiene un vigilante de 3 s.

**Cuaderno hoy** (`Radio\Ptt\VigilantePtt.cs`, 918 líneas): **más robusto que Thetis en lo esencial**.

- Es el único camino al PTT y corre en hilo propio.
- Tiene tope duro de 10 minutos (3 por omisión), latido y vías de suelta de emergencia.
- `PttPegadoException` hace que el fallo nunca quede en silencio.
- Suelta el PTT al cerrar o ante una excepción.

**Lo que le falta frente a Thetis** (comprobado: `VigilantePtt`, `GuardiaDelPtt` y `ControlDeFonia` no consultan ni la ROE ni el plan de banda):

- **(a)** Bloqueo de TX fuera de banda: hoy solo hay un aviso visual (`VistaModeloVfo.FueraDeBanda`), y no se comprueban los bordes del filtro.
- **(b)** Corte por ROE alta o antena abierta leyendo `RM6` durante la transmisión.
- **(c)** Regla de «no rearmar tras un corte hasta soltar todas las fuentes».
- **(d)** Prioridad y propiedad de la fuente. Será necesario en cuanto haya fonía, CW, digital, voice keyer y TCI a la vez.
- **(e)** Potencia máxima por banda (por ejemplo, para proteger un amplificador o una antena de 6 m).

---

## 4. Propuestas priorizadas

Formato de cada ficha: **Thetis** (qué hace y dónde) · **Cuaderno** · **Propuesta** · **Valor** (Jose / otros) · **Esfuerzo** · **Riesgo** · **Código**.

### A) Aplicables ya al FT-710 y a cualquier radio

#### A1. Salvaguardas de TX que faltan — prioridad 1

- **Thetis:** §3.8, puntos 2, 5 y 6 (`console.cs:PollPTT`, `CheckValidTXFreq`, `PollPAPWR`; `TimeOutTimerManager.cs`).
- **Cuaderno:** `VigilantePtt` sin ROE ni plan de banda.
- **Propuesta:** añadir al vigilante una lista de **condiciones previas** (`ICondicionDeTransmision`) y **condiciones en curso**:
  1. frecuencia ± ancho del filtro de TX dentro del segmento de la región, según el `Bandplan` del Cuaderno; con permiso explícito «TX extendido» para MARS o 11 m, que está la botonera CB;
  2. durante TX, sondear `RM6` (ROE) y `RM5` (Po) a ≥ 4 Hz; si ROE > límite en N lecturas seguidas → soltar y bloquear hasta que el operador lo reconozca; antena abierta → soltar al momento;
  3. «no rearmar hasta soltar todo»;
  4. potencia máxima por banda (`PC`) antes de subir el PTT.
- **Valor:** Jose alto (protege el FT-710 y un posible amplificador). Otros: alto.
- **Esfuerzo:** M.
- **Riesgo:** bajo; son pruebas con el equipo simulado. Hay que medir la latencia real de `RM6` en el FT-710 sin transmitir de más: probar en carga artificial y con permiso de Jose.
- **Código:** reimplementar; es lógica trivial.

#### A2. Spots del cluster sobre el analizador y clic para sintonizar — prioridad 2

- **Thetis:** `SpotManager2.cs` y `display.cs:drawSpots/getSpotLayer` (l.11159-11552); clic en `console.cs:pnlDisplay_MouseDown` (l.48868) con `adjustForSnapClickTuning` (l.33400).
- **Cuaderno:** tiene cluster, bandmap y trabajado antes, pero **ni spots ni clic sobre `AnalizadorDelEquipo`**.
- **Propuesta:**
  - Una capa WPF (`CapaDeSpotsDelAnalizador`) sobre el pintor que use el mismo `VistaModeloBandmap`:
    - carriles voraces;
    - anclaje según la banda lateral;
    - color por **estado de novedad del Cuaderno** (NEW, BAND, MODE y trabajado), que es **mejor que el ARGB fijo de Thetis**;
    - parpadeo de los nuevos (< 2 min);
    - bocadillo con país, rumbo, distancia y edad.
  - Clic en spot → `FA` y `MD`, y rellena el contacto nuevo. Clic en el vacío → sintonía con ajuste al paso. Rueda → paso (Mayús ÷ 10).
  - En **FIX** la escala es absoluta (byte 144 de la trama); en **CENTER**, VFO ± span/2. Falta confirmar el factor 1,0695.
- **Valor:** Jose muy alto (es lo que más se nota al operar). Otros: alto (ICOM y otros Yaesu con analizador).
- **Esfuerzo:** M.
- **Riesgo:** bajo. Solo hay que vigilar que el clic no mande `FA` en TX: bloquearlo mientras `Transmitiendo`.
- **Código:** reimplementar la idea.

#### A3. Suelo de ruido, AGC de cascada, picos y rejilla de dB — prioridad 3

- **Thetis:** `display.cs:processNoiseFloor` (l.5872-5918), AGC de cascada (l.6464-6474 y caché l.2748-2829), LUT de 101 colores (`ucGradientDefault.cs`), picos con histéresis (l.5291-5321), pico activo (l.5333-5365), desplazamiento de la historia al resintonizar (l.6212-6263) y `analyzer.c:avenger` (log recursivo).
- **Cuaderno:** cascada en rojos con escala fija y traza suavizada.
- **Propuesta:**
  - NF sobre los 850 puntos del FT-710: media de los puntos < NF+2, subida de 1 unidad por trama si faltan muestras, *lerp* de unos 2 s y ataque rápido al cambiar de banda o de span.
  - Cascada con umbral bajo = NF − offset y alto = NF + rango, con caché por banda.
  - 4-5 paletas (las de `ucGradientDefault`, como datos).
  - Promedio log recursivo con τ ajustable.
  - Pico activo.
  - Marcas de los N picos con «+x dB sobre el ruido».
  - Desplazamiento de la cascada al cambiar el VFO en CENTER.
  - Marcas de hora.
- **Valor:** Jose alto; la escala del FT-710 es relativa (0-255 invertido), así que el NF automático **resuelve el contraste sin tocar la radio**. Otros: medio.
- **Esfuerzo:** M.
- **Riesgo:** bajo; es puro pintado y se puede probar con las tramas guardadas en `tests\…\Capturas`.
- **Código:** las fórmulas son triviales; reimplementar.

#### A4. Reductor de ruido y notch en la fonía por el PC — prioridad 4

- **Thetis/WDSP:** `rnnr.c` (RNNoise), `emnr.c` (NR2), `anr.c` (NR1), `anf.c` (ANF), `snb.c` (SNBA), `nbp.c` (notches), `wcpAGC.c` (AGC) y `ssql.c`.
- **Cuaderno:** `ControlDeFonia` solo aplica ganancia.
- **Propuesta:** crear un `Audio\Procesado\CadenaDeEscucha` con bloques intercambiables en este orden:
  1. filtro pasabanda;
  2. NR (elegible: RNNoise / NR2 / NR1);
  3. ANF;
  4. notch manual;
  5. AGC suave;
  6. squelch SSQL.

  **Primero RNNoise por P/Invoke**: es lo de más impacto en SSB con menos código. Después, ANF y NR1 portados (unas 100 líneas cada uno) y NR2 portado con sus tablas.

  Reutilizar también la cadena para el **decodificador CW** (filtro adaptado de `matchedCW.c`). **No** aplicarla al módem digital, que debe recibir el audio limpio.
- **Valor:** Jose alto. El DNR del FT-710 es correcto, pero RNNoise o NR2 en el PC suelen ser claramente mejores en SSB débil, y se pueden combinar. Otros: muy alto, para cualquier radio con salida de audio.
- **Esfuerzo:** S (RNNoise) + M (ANF/NR1/NR2).
- **Riesgo:** bajo. Latencia de NR2 ~85 ms y de RNNoise 10 ms: aceptable en escucha, no en monitor de TX.
- **Código:**
  - **portar WDSP** (GPL-2+, con atribución);
  - RNNoise BSD-3, con el aviso;
  - DLL de RNNoise precompilada para x64 o compilada desde Xiph. Conviene **compilarla nosotros** y no copiar el binario de Thetis.

#### A5. Voice keyer y grabación (RX y TX) — prioridad 5

- **Thetis:** `clsAudioRecordPlayback.cs` (`OnPreMox` l.559, guardado y restauración de estado l.495-556) y `MeterManager.clsVoiceRecordPlay` (l.10342).
- **Cuaderno:** no hay.
- **Propuesta:**
  - **Voice keyer** de 6-12 ranuras (WAV en `%AppData%\CuadernoNodisla\voz\`), con F1-F12 en la cabina como las macros CW.
  - Repetición con pausa para CQ, con un tope que es el del vigilante.
  - Ganancia por ranura y normalización.
  - El PTT **siempre por `IVigilantePtt`**.
  - Abortar si el operador pulsa PTT, ESC o mueve el VFO.
  - Variables por síntesis: no.
  - **Grabador de RX** continuo con búfer circular de los últimos 30-60 s («grabar lo que acaba de pasar») y opción de **adjuntar el audio al QSO**. Esto último es un plus que Thetis no tiene y que encaja con un cuaderno.
  - Grabación de TX desde el micrófono del PC.
  - Formato WAV PCM16 a 48 kHz u Opus si se quiere ahorrar espacio.
- **Valor:** Jose alto (concursos y pileups; el FT-710 tiene memorias de voz propias, pero solo 5 y manejadas desde el panel). Otros: muy alto.
- **Esfuerzo:** M.
- **Riesgo:** medio, porque implica transmitir: reutilizar `EmisionVigilada` y el orden «camino en silencio → PTT → audio» que ya usa la fonía.
- **Código:** reimplementar (NAudio ya está).

#### A6. Servidor para otros programas: rigctld compatible y TCI — prioridad 6

- **Thetis:** `TCIServer.cs` y `CAT\TCPIPcatServer.cs`.
- **Cuaderno:** no tiene servidor; el COM del FT-710 queda bloqueado.
- **Propuesta en dos pasos:**
  1. **Servidor «rigctld compatible»** (protocolo de red de Hamlib, puerto 4532) **sobre el `IControlEquipo` del Cuaderno**:
     - órdenes `f/F`, `m/M`, `t/T`, `v/V`, `s/S`, `l STRENGTH`, `\dump_state` y `\chk_vfo`;
     - es lo que hablan WSJT-X, JTDX, fldigi, GridTracker, N1MM y casi todos.
     - Esfuerzo S-M. **Recomendado primero** por cobertura.
  2. **Servidor TCI 2.0** (WebSocket con `HttpListener` o Kestrel, **no a mano**):
     - handshake `protocol`/`device`/`trx_count:1`/`modulations_list`/`ready;`;
     - `vfo`, `dds`, `modulation`, `trx` (por el vigilante), `tune`, `drive`, `split_enable`, `rx_sensors`/`tx_sensors`, `cw_macros`/`cw_msg`/`cw_macros_stop` (al `EmisorCw` del Cuaderno);
     - `spot`/`spot_delete`/`spot_clear`, que **entran al bandmap**, y `clicked_on_spot`, que sale;
     - opcional: **audio de RX y TX por TCI**, que quita los cables virtuales a quien use WSJT-X Improved o JTDX con el FT-710.
     - Cola de salida con coalescencia por clave, como Thetis.
- **Seguridad:**
  - solo 127.0.0.1 por omisión;
  - **lista blanca** de órdenes;
  - **nada de CAT en crudo** (no copiar `run_cat_ex`);
  - `trx` y `keyer` siempre a través del vigilante, con latido;
  - apagado por omisión, coherente con «nada se conecta solo».
- **Valor:** para Jose es medio, porque la filosofía es todo propio. Aun así, permite usar **otro software puntual** (un cliente de satélite o un concurso) sin cerrar el Cuaderno, y que el Cuaderno sea el «dueño» de la radio. Para otros usuarios es **muy alto**: es lo que más pedirán.
- **Esfuerzo:** S-M (rigctld) + L (TCI con audio).
- **Riesgo:** medio; es una superficie de red y una vía más al PTT.
- **Código:** reimplementar. El protocolo TCI es público (Expert Electronics); las capturas de *handshake* comentadas en `TCIServer.cs:48-306` sirven como guion de prueba.

#### A7. Medidores con aguja calibrada, retención de pico e histórico — prioridad 7

- **Thetis:** `MeterManager.cs`, que es tabla valor→posición (`ScaleCalibration`), con ataque y caída asimétricos (`clsNeedleItem.Update`) y retención de pico por historial.
- **Cuaderno:** `MedidorDeArco` y `MedidorDeBarra`.
- **Propuesta:**
  - `TablaDeCalibracion` (pares de valor crudo `RMn` → unidad física → ángulo) por modelo, **medida contra el FT-710** (S, Po, ROE, ALC, COMP, Idd, Vdd).
  - Ataque 0,8 y caída 0,2 por sondeo.
  - Marca de pico con retención de 1-2 s.
  - Minigráfica de los últimos 60 s de ROE y Po en TX, útil para ajustar el acoplador.
  - Skins con imágenes: **no**; el Cuaderno dibuja en vector.
- **Valor:** Jose medio. Otros: medio.
- **Esfuerzo:** S-M.
- **Riesgo:** bajo; la calibración de ALC, Po y ROE requiere transmitir en carga artificial con permiso.
- **Código:** reimplementar.

#### A8. Compensación de deriva de reloj entre tarjetas (RMATCH) — prioridad 8

- **Thetis:** `wdsp\rmatch.c` + `varsamp.c`, usados en `CM\ivac.c`.
- **Cuaderno:** el puente WASAPI de la fonía remuestrea a razón fija (confirmar).
- **Propuesta:** portar RMATCH para la escucha (códec del FT-710 → altavoces) y para hablar (micrófono del PC → códec del FT-710). Así se evitan los chasquidos cada pocos minutos y la latencia que crece en sesiones largas.
- **Valor:** Jose medio-alto, en ronda de control y sesiones largas de fonía. Otros: alto.
- **Esfuerzo:** M.
- **Riesgo:** bajo.
- **Código:** **portar** (GPL-2+).

#### A9. Procesado del micrófono del PC al transmitir — prioridad 9

- **Thetis:** `TXA.c` (EQ, leveler, CFC, compresor, CESSB, ALC).
- **Cuaderno:** solo ganancia.
- **Propuesta:**
  - EQ de 3-10 bandas.
  - Leveler (AGC lento) y compresor suave **con limitador final duro a −1 dBFS**.
  - **No** CESSB ni CFC al principio: el FT-710 ya tiene su propio procesador y un ALC.
  - Medidor de ALC (`RM4`) visible mientras se habla.
- **Valor:** Jose medio. Otros: medio.
- **Esfuerzo:** M.
- **Riesgo:** **medio-alto**: un mal ajuste ensancha la señal (salpicaduras). Necesita un tope de nivel no ajustable por encima y una prueba en carga artificial.
- **Código:** portar `wcpAGC`, `compress` y `eq` (GPL-2+).

#### A10. Buscador de ajustes, quick recall y salida de espectro a N1MM — prioridad 10

- **Thetis:** `frmFinder.cs`, `ucQuickRecall.cs` y `N1MM.cs`.
- **Propuesta:**
  - Buscador en Configuración: el índice se genera desde las claves `.resx`, así que **funciona en los 6 idiomas**.
  - Quick recall de las últimas 10 frecuencias estables durante 4 s.
  - Opcional: enviar la traza del FT-710 por UDP 13064, si alguien usa N1MM con el Cuaderno.
- **Esfuerzo:** S cada una.
- **Riesgo:** bajo.

#### A11. Perfiles de ajustes con copia GFS — prioridad 11

- **Thetis:** `clsDBMan.cs` (copia al arrancar y al cerrar, `pruneForGFS`, carpeta «broken», importar y exportar perfiles).
- **Cuaderno:** un solo `ajustes.json` y un solo `cuaderno.sqlite`.
- **Propuesta:**
  - Copia automática de `cuaderno.sqlite` y `ajustes.json` al cerrar, con la `VACUUM INTO` de SQLite (sin bloquear).
  - Rotación GFS: 7 diarias, 4 semanales y 12 mensuales.
  - Si el JSON no se puede leer, apartarlo y arrancar con los valores por omisión, avisando.
  - Perfiles de estación ya existen; perfiles de *ajustes*, solo si se piden.
- **Valor:** alto para cualquiera que lleve años de log.
- **Esfuerzo:** S.
- **Riesgo:** bajo.

### B) Soporte de radios SDR OpenHPSDR (ANAN y Hermes Lite 2)

**Qué haría falta:**

1. **Protocolo.**
   - **P1** (Hermes Lite 2, ANAN antiguos): UDP en el puerto 1024, tramas de 1.032 bytes (`EF FE 01`, dos subtramas `7F 7F 7F` + C0..C4), IQ de 24 bits y rotación de registros C&C (frecuencias, MOX en el bit 0 de C0, atenuador, *drive*) (`CM\networkproto1.c`, 749 líneas).
   - **P2** (ANAN modernos): puertos 1024-1029/1035+, paquete *high priority* con PTT y frecuencias, y *keepalive* (`CM\network.c`, 1.494 líneas).
   - Descubrimiento: difusión `EF FE 02` (`Console\HPSDR\clsRadioDiscovery.cs`).
   - Estimación en C#: **P1 1.500-2.500 líneas** y **P2 2.500-3.500**.
2. **DSP de recepción y transmisión sobre IQ:**
   - demodulación, filtros, AGC, NR y NB sobre IQ;
   - modulación SSB/CW/AM/FM en TX y ALC.
   - Hay dos caminos:
     - **(i)** cargar `wdsp.dll` nativa por P/Invoke. Es rápido de integrar, **compatible con la licencia** (GPL-2+) y probado durante años; pero hay que compilarla, depende de FFTW y es C con estado global.
     - **(ii)** portar a C# los bloques necesarios. Es XL.
   - Para un primer equipo, (i) es lo razonable, aislando la DLL igual que se pensó para el módem (proceso o `AssemblyLoadContext` aparte).
3. **Panadaptador propio desde IQ:** FFT, ventanas, detectores, promedios, calibración en dBm y zoom por recorte de bins (`analyzer.c`, `specHPSDR.cs`). Encaja con el `IAnalizadorDeEspectro` existente y con todo lo de A2 y A3.
4. **Audio:** el sonido de la radio es el del PC, así que la fonía por el PC deja de ser opcional. Hacen falta RMATCH y el micrófono hacia IQ de TX.
5. **Seguridad:** el PTT va dentro del protocolo (bit MOX). Hay que llevar el vigilante al hilo de red, **detectar la pérdida de secuencia o del enlace y soltar** (Thetis apaga si pierde la sincronía), y añadir protección de ROE con las lecturas de fwd/rev del propio paquete.

**¿Merece la pena?**

- **Ahora no.** Jose no tiene la radio, no se podría validar (y la norma de la casa es verificar en la app real), y supone meses de trabajo (**XL**) con mucha superficie de riesgo de TX.
- Thetis, SparkSDR y piHPSDR ya cubren muy bien a esos usuarios.
- **Sí conviene dejarlo preparado:**
  - `IAnalizadorDeEspectro` debe aceptar trazas en dBm absolutos, no solo 0-255;
  - `IControlEquipo` debe admitir un equipo «sin CAT» cuyo audio es del propio programa;
  - la cadena de A4 debe trabajar con bloques genéricos.
- **Momento para replantearlo:** si Jose compra un **Hermes Lite 2**, unos 300 USD y solo P1. Sería el SDR más barato para empezar: solo P1, con wdsp.dll por P/Invoke y un panadaptador que reutilice A2 y A3. Estimación con la radio delante: **L-XL (2-3 meses)**.

### C) Buenas prácticas de seguridad de TX y de rendimiento que conviene copiar

**Seguridad de TX:**

1. **Propiedad de la fuente de PTT**: quien sube el PTT es quien lo baja; las demás fuentes no pueden soltarlo ni reactivarlo (`PollPTT`).
2. **No rearmar tras un corte** (TOT, ROE, enlace) hasta que *todas* las fuentes estén soltadas. Viene de un fallo real (issue #518).
3. **Plan de banda con los bordes del filtro de TX**, no solo con la portadora.
4. **ROE con N lecturas consecutivas** y caso aparte de **antena abierta**. El aviso «solo salta una vez de cada dos» (issue #538) muestra que la lógica de rearme del aviso también hay que probarla.
5. **Cualquier cambio de PTT aborta las reproducciones** de voice keyer y CW, y vigilante de 3 s en la manipulación remota (TCI `keyer`).
6. **Ping del enlace durante TX**, adaptado: el latido del CAT del FT-710 ya lo da `IAvisaDePerdidaDeComunicacion`. Hay que asegurar que el plazo **en TX** sea más corto que en RX.
7. **Retardos de secuencia configurables** (RF, PTT, hang) por equipo, sobre todo con amplificador: no emitir audio hasta que se cumpla el retardo tras el PTT.

**Rendimiento:**

8. Hilo de pantalla con **detección de cuadros tardíos visible** (el cuadrado rojo o amarillo de Thetis). En el Cuaderno: un indicador discreto si el analizador pierde tramas.
9. `ArrayPool` y cero asignaciones por cuadro; saltar puntos con la misma Y; relleno con líneas.
10. **Coalescencia por clave** en cualquier cola de salida (TCI, sondeo CAT) para no inundar a los clientes.
11. En la pantalla, viaja con cada cuadro la frecuencia central con la que se calculó (`pixel_ref`), para no pintar spots y VFO desalineados con la traza.

### D) Lo que NO conviene copiar, y por qué

1. **El objeto dios `Console`** (54.000 líneas parciales), con unos 2.400 accesos desde `setup.cs`. El Cuaderno ya tiene puertos, MVVM y DI.
2. **Serialización posicional con `|`** de medidores y ajustes (`clsIGSettings.TryParse` exige ≥ 32 campos). Se rompe al cambiar el esquema; el Cuaderno usa JSON tipado.
3. **~270 `catch {}` vacíos.** El Cuaderno tiene la norma contraria («el fallo nunca se traga en silencio»).
4. **Cero pruebas.** No hay nada que copiar en ese aspecto.
5. **WebSocket hecho a mano**: sin fragmentación y con *parsing* frágil. En .NET 8 hay `System.Net.WebSockets`.
6. **TCI sin autenticación con `run_cat_ex`**: abre la radio a cualquier proceso local y permite CAT arbitrario, incluido transmitir.
7. **Ocho clases de puerto serie copiadas y pegadas** (`SDRSerialPort`…`SDRSerialPort7`).
8. **Script de LED con Roslyn** (`clsMeterScriptEngine`): compilar C# en tiempo de ejecución es pesado y es superficie de ataque, para algo que se resuelve con 5 condiciones fijas.
9. **Bot de Discord y descarga de configuración desde GitHub cada 30 minutos** (`clsDiscord.cs`): dependencia remota sin necesidad. Con las skins descargables pasa igual: el Cuaderno dibuja en vector.
10. **SharpDX y .NET Framework**: archivados; justo la causa por la que Thetis se archivó.
11. **ASIO SDK**, por licencia incompatible.
12. **Cortafuegos con permisos de administrador** (`Firewall.cs`): un cuaderno no debe pedir elevación.
13. **Tamaño de bloque de 4.096 muestras en la fonía** (85 ms): para la escucha por el PC conviene un bloque de 10-20 ms. El NR2 se puede usar con un solape mayor o dejarse como opción de «más latencia».

---

## 5. Plan por fases recomendado

| Fase | Contenido | Esfuerzo | Por qué en este orden |
|---|---|---|---|
| **0 · Preparación (1 día)** | `LICENSE` GPL-3.0 en la raíz; `AVISOS-DE-TERCEROS.md` con WDSP, Thetis y RNNoise; convención de cabecera para ficheros portados | S | Requisito legal antes de portar nada |
| **1 · Seguridad de TX** | A1 completo: plan de banda con bordes del filtro, ROE y antena abierta por `RM6`, no rearmar hasta soltar todo, propiedad de la fuente y potencia máxima por banda. Pruebas con el equipo simulado (ROE alta, enlace perdido, fuente ajena que intenta soltar) | M | Hay compañeros metiendo la **transmisión CW**. Las salvaguardas deben estar **antes** de que haya más caminos al PTT (CW, voice keyer, TCI) |
| **2 · Analizador útil para operar** | A2 (spots sobre el analizador y clic para sintonizar) y A3 (NF, AGC de cascada, paletas, picos y desplazamiento al resintonizar). Probar con las tramas reales de `Capturas` | M + M | Mayor ganancia visible para Jose; sin TX y sin riesgo |
| **3 · Audio por el PC** | A4 (RNNoise primero, luego ANF/NR1/NR2), A8 (RMATCH) y la cadena reutilizada por el decodificador CW | S + M + M | Mejora la escucha de inmediato en el FT-710 y en cualquier radio |
| **4 · Voz** | A5 (voice keyer y grabador circular con audio adjunto al QSO) y A9 (procesado de micrófono con limitador) | M + M | Necesita las fases 1 y 3 |
| **5 · Abrir el Cuaderno a otros** | A6.1 servidor rigctld compatible; luego A6.2 TCI (control, spots y CW), con audio TCI al final | S-M + L | Mucho valor para otros usuarios; necesita la fase 1 por la nueva vía al PTT |
| **6 · Pulido** | A7 (medidores calibrados), A10 (buscador, quick recall, N1MM) y A11 (copias GFS) | S-M | Mejoras independientes que se pueden intercalar |
| **7 · (Opcional) OpenHPSDR** | B: Hermes Lite 2 por P1 con wdsp.dll por P/Invoke, solo si Jose tiene la radio | L-XL | Sin radio no se puede validar |

**Lo primero que haría:** la fase 0 y **la condición «TX fuera del plan de banda → no subir el PTT»** dentro del vigilante. Es pequeña y no necesita transmitir para probarse (equipo simulado). Además, cierra un hueco real justo cuando entra la transmisión CW. Inmediatamente después, los spots sobre el analizador con clic para sintonizar: mismo coste y es lo que Jose verá cada día.

---

## 6. Referencias rápidas (Thetis)

| Tema | Fichero (y líneas) |
|---|---|
| Espectro: ventanas, detectores, promedios, calibración | `wdsp\analyzer.c` (52-177, 283-554, 928-1064); `Console\HPSDR\specHPSDR.cs` (301-363, 529-568) |
| Pintado, NF, cascada, rejilla, spots | `Console\display.cs` (4733-4817 notch visual, 5240-5918 NF, 6212-7715 cascada, 8802-8956 rejilla, 11159-11552 spots) |
| Spots | `Console\SpotManager2.cs`; `Console\TCIServer.cs:4339-4505` |
| Medidores | `Console\MeterManager.cs` (`clsNeedleItem` ~21900, render ~41050); `wdsp\meter.c` |
| NR/ANF/NB | `wdsp\anr.c`, `anf.c`, `emnr.c`, `rnnr.c`, `sbnr.c`, `snb.c`, `nob.c`, `nobII.c`, `nbp.c` |
| AGC y TX | `wdsp\wcpAGC.c`, `compress.c`, `cfcomp.c`, `osctrl.c`, `eq.c`, `TXA.c` |
| Deriva de reloj | `wdsp\rmatch.c`, `varsamp.c`; `ChannelMaster\ivac.c` |
| TCI | `Console\TCIServer.cs` (684, 1640-1878, 2662-2702, 3594, 5259-5645, 6007, 8231) |
| CAT | `Console\CAT\CATParser.cs`, `CATCommands.cs`, `SDRSerialPortII.cs`, `TCPIPcatServer.cs`, `clsCatAtonic.cs` |
| PTT y seguridad | `Console\console.cs` (`PollPTT` 25393, `PollTXInhibit` 25779, `PollPAPWR` 25911, `CheckValidTXFreq` 6708, `chkMOX_CheckedChanged2` 29292); `Console\TimeOutTimerManager.cs` |
| Grabación y voice keyer | `Console\clsAudioRecordPlayback.cs`; `MeterManager.cs:clsVoiceRecordPlay` (10342) |
| CW | `Console\cwx.cs` (215, 257, 404, 2192) |
| Protocolos | `ChannelMaster\networkproto1.c`, `network.c`, `netInterface.c`; `Console\HPSDR\clsRadioDiscovery.cs` |
| Perfiles y copias | `Console\clsDBMan.cs` (1218, 1929); `database.cs` |
| Issues relevantes | #518 (TOT con PTT externo), #538 (aviso de ROE alterno), #221 (umbral de ROE configurable), #488 (voice keyer digital), #6 y #223 (TCI) |
