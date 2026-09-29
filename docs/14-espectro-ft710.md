# 14 — El analizador de espectro del propio FT-710

Fecha: 28-09-2026. Corrige lo escrito en `04-ft710-cat.md` («Segunda captura», 22-09-2026):
**el puente del espectro SÍ existe en el equipo del operador.** El 22-09 no aparecía porque no
estaba activado en el menú (o no estaba el controlador); hoy sí está.

## Cómo lo hace 710 Console

No es CAT ni audio. El FT-710 lleva **dentro** un puente USB–SPI **FTDI FT4222H**, colgado del
mismo concentrador USB que el CP2105 (CAT) y el códec C-Media (audio). Por el mismo cable USB
salen las tres cosas. El DSP del equipo vuelca por ese SPI la trama de su pantalla.

- 710 Console: «The FT-710 has a USB SPI bridge (FTDI FT4222) next to its CAT port and audio
  codec. 710 Console reads the radio's own scope data through it» y «The radio only sends its
  spectrum to a PC while MENU → OPERATION SETTING → GENERAL → SCU-LAN10 is set to ON».
  Usa el controlador FTDI D2XX. <https://710console.co.uk/>
- wfview (código abierto) hace lo mismo: «Enable the SCU-LAN10. This enables the spectrum
  output. You don't need to have a SCU-LAN10 to do this, just enable the item in the radio's
  menu.» <https://wfview.org/wfview-user-manual/yaesu-ft-710-setup/>
- Código de wfview que lo lee: `src/ft4222handler.cpp`, `src/radio/yaesucommander.cpp`,
  `include/packettypes.h` en <https://gitlab.com/eliggett/wfview>.
- Propuesta equivalente (sin probar en banco) en Yaesu_Web_Control:
  <https://github.com/mm5agm/Yaesu_Web_Control/pull/188> — menciona el menú EX 03-01-26
  (SCU-LAN10 = ON) y un factor empírico de ancho de 1,0695 × span, sin confirmar.
- Petición similar: <https://github.com/Waffleslop/POTACAT/issues/91>.

**No hace falta comprar nada**: ni la SCU-LAN10, ni puente externo, ni cable. Descartados:
órdenes CAT de datos del analizador (el CAT solo da su configuración: span, modo, velocidad),
dispositivo de audio IQ, salida de vídeo o LAN.

### El protocolo (datos, tal como lo usa wfview)

- Abrir con D2XX (`ftd2xx.dll`) el dispositivo por descripción `"FT4222 A"`
  (`FT_OpenEx`, `FT_OPEN_BY_DESCRIPTION`), tiempos de espera 100/100 ms, latencia 2.
- `LibFT4222`: `FT4222_SPIMaster_Init(h, SPI_IO_SINGLE, CLK_DIV_64, CLK_IDLE_HIGH,
  CLK_LEADING, 0x01)` y `FT4222_SetClock(h, SYS_CLK_24)`. El PC es el maestro: solo lee
  (`FT4222_SPIMaster_SingleRead`); no escribe nada al equipo.
- Tramas de **4096 bytes** que acaban en `FF 01 EE 01`. Para sincronizar se lee byte a byte
  hasta ver `FF 01 EE 01` cuatro veces seguidas.
- Disposición de la trama:

| Desplazamiento | Largo | Contenido |
|---|---|---|
| 0 | 850 | Traza principal (MAIN), de frecuencia baja a alta; **bytes invertidos** (`~b`) |
| 850 | 850 | Traza del segundo receptor (no aplica al FT-710) |
| 1700 | 200/400/200/400 | FFT y osciloscopio del audio 1 y 2 |
| 2900 | 150 | Bloque de estado (abajo) |
| 3050 | 1042 | Sin uso conocido |
| 4092 | 4 | `FF 01 EE 01` |

Bloque de estado (desplazamiento dentro de los 150 bytes, según wfview; «?» = sin confirmar):

| Byte | Contenido |
|---|---|
| 17 | Modo del analizador, el mismo número que contesta `SS06` (0-2 3DSS; 4/7/A cascada; 5/8/B cascada ampliada) — **confirmado 29-09** |
| 32 | Nibble bajo, span: 0=1 kHz, 1=2, 2=5, 3=10, 4=20, 5=50, 6=100, 7=200, 8=500 kHz, 9=1 MHz; nibble alto 4 en CURSOR, 8 en FIX — **confirmado 29-09** |
| 33 | Nibble alto, SPEED como `SS00` (0 SLOW1, 1 SLOW2, 2 FAST1, 3 FAST2, 4 FAST3, 5 STOP) — **confirmado 29-09** |
| 52 | 0 = CENTER, 1 = CURSOR, 2 = FIX — **confirmado 29-09** |
| 60 | Modo del VFO A (0 LSB, 1 USB, 2 CW-L, 3 CW-U, 4 AM, 6 FM, 8 DATA-L, 9 DATA-U, C/D RTTY) |
| 64 | Frecuencia del VFO que enseña el analizador (el activo: con VS1 es la de B), 5 bytes BCD |
| 89 | Frecuencia del otro VFO, 5 bytes BCD |
| 110 | S-metro |
| 132 | Frecuencia del VFO A, 32 bits big-endian |
| 144 | Inicio de la escala en modo FIX, 32 bits big-endian — **confirmado 29-09** (18.068.000 en 17 m) |

En CENTER la traza va de `VFO − span/2` a `VFO + span/2`.

## Lo que hay en la máquina del operador (28-09-2026, solo lectura)

Con la radio encendida, `Get-PnpDevice` y `HKLM\SYSTEM\CurrentControlSet\Enum\USB`:

| Dispositivo | Identificador | Controlador |
|---|---|---|
| CP2105 (COM15 Enhanced, COM16 Standard) | `VID_10C4&PID_EA70` | `silabser` |
| Códec de audio C-Media | `VID_0D8C&PID_0013` | `usbaudio` + HID |
| **FT4222H Interface A y B** | **`VID_0403&PID_601C`** (MI_00, MI_01) | **`FTDIBUS`** (oem60.inf) |

- `ftd2xx.dll` 3.2.21.1 en `System32` y `SysWOW64` (controlador D2XX instalado).
- **710 Console 0.2.34 está instalado** en `C:\Program Files\710 Console` (no se ha ejecutado).
  Es .NET 8; trae `ftd2xx.dll` 3.2.16.1, `LibFT4222-64.dll` 1.4.8.0 y en `drivers\` los
  paquetes del CP210x y del FTDI. Sus avisos de terceros citan «FTDI D2XX and LibFT4222».
- Que el FT4222 aparezca indica que el menú SCU-LAN10 está en ON (según 710 Console, sin él
  el equipo no manda el espectro).

## Prueba en vivo (28-09-2026, solo lectura, con permiso del operador)

Con 710 Console cerrado y el Cuaderno cerrado: apertura de «FT4222 A» en maestro SPI,
sincronía tras 4096 bytes, **40/40 y luego 160/160 tramas buenas**, unas 11 por segundo
(4096 bytes a 375 kHz de reloj SPI). La radio estaba en 18.100.000, DATA-U, CENTER, span
200 kHz. Bloque de estado observado: byte 32 = 7 (200 kHz), 33 = 0x24, 52 = 0 (CENTER),
60 = 9 (DATA-U), BCD del VFO A y B = `0018100000`, 132 = 18.100.000, 144 = 18.100.000.
Niveles de la traza (ya invertidos): suelo ~68, señales hasta ~100 en una banda tranquila.
Tres tramas guardadas como prueba en `tests/Nodisla.Cuaderno.Radio.Pruebas/Capturas/`.

Sin confirmar todavía: el factor de ancho 1,0695 que cita la PR de Yaesu_Web_Control (aquí se
usa el span tal cual).

## Teclas de la pantalla contra la radio real (29-09-2026, sin transmitir)

Con el Cuaderno y 710 Console cerrados, una sonda mandó por COM15 solo `SS00`/`SS05`/`SS06`
(y consultas `PS`, `FA`, `FB`, `VS`) mientras leía las tramas; la radio quedó como estaba
(`SS0010000`, `SS0590000`, `SS0640000`). Lo encontrado:

- **Tras tocar SPAN o SS06 las tramas llegan retrasadas UN BIT** (cada byte, el de antes
  desplazado a la derecha con el último bit del anterior delante) y así siguen, aunque se
  cierre y se vuelva a abrir el puente; a veces vuelven solas a su sitio. Sin enderezarlas
  ninguna trama valía: **por eso el analizador se quedaba congelado al pulsar SPAN, 3DSS…** y
  la pantalla del programa no reflejaba el cambio. `TramaDelAnalizadorFt710.IntentarEnderezar`
  busca el retraso que deja la cola (`FF 01 EE 01` × 4) en su sitio; `IntentarDescifrar` ya
  lo hace solo. Con eso, 11,3 tramas/s y 0 malas en todas las pruebas.
- Cada lectura de 4096 bytes es una trama entera y alineada (el puente enmarca por lectura);
  una lectura mayor devuelve ceros.
- La radio manda unas 11 tramas por segundo con **cualquier** SPEED: la velocidad de la
  cascada la hace el programa (`PintorDelAnalizador.FilasPorPasada`: SLOW1 ¼ de fila por
  pasada … FAST3 3; STOP congela).
- En FIX la radio guarda su propio span (se lee `SS05` = 6, 100 kHz) y el byte 144 da el
  comienzo de la escala.
- **EXPAND en 3DSS no existe por CAT**: `SS06C/D/E` no hacen nada. Desde 3DSS, EXPAND pasa a
  la cascada ampliada de la misma posición.
- Tramas crudas guardadas (con el bit de retraso) en `Capturas/analizador-ft710-*.bin`.

## Lo implementado

- `Aplicacion/Puertos/IAnalizadorDeEspectro.cs`: el puerto (solo lectura).
- `Radio/Espectro/TramaDelAnalizadorFt710.cs`: descifrado de la trama (código puro, probado
  con las tramas reales).
- `Radio/Espectro/PuenteFt4222.cs`: P/Invoke a `ftd2xx.dll` (del controlador) y
  `LibFT4222-64.dll` (junto al ejecutable), cargadas en tiempo de ejecución; si faltan, lo dice.
- `Radio/Espectro/AnalizadorFt710.cs`: hilo de lectura, sincronía, reintento cada 3 s si la
  radio se apaga; estados SinBiblioteca / SinDispositivo / SinTramas (menú SCU-LAN10) / Recibiendo.
- `Ui/Recursos/PintorDelAnalizador.cs` y `Ui/VistaModelos/VistaModeloAnalizador.cs`: traza
  (suavizada), cascada en rojos que corre según SPEED, vista 3DSS en perspectiva, marca roja
  del VFO, rótulo «CENTER  SPEED FAST1  SPAN 200kHz» (en FIX, «FIX 18068kHz …» y la escala
  con frecuencias absolutas). El estado sale de cada trama y, al pulsar una tecla o al ver
  el sondeo un cambio en la radio, del CAT (`VistaModeloEquipo.AjusteDelAnalizador`), que
  manda 0,8 s para que una trama vieja no deshaga lo pulsado. EXPAND da más alto al
  analizador (tapa la fila ATT/IPO/DNF/AGC, como la radio).
- MULTI (`VistaModeloAnalizador.Multiple.cs`, `Ui/Recursos/PintorDelAudio.cs`): osciloscopio y
  AF-FFT del audio de recepción bajo el analizador, como la pantalla MULTI de la radio
  (manual de operación del FT-710, pág. 23). Escucha los bloques de `IEntradaDeAudio` (la
  misma que abre el módem) solo mientras se ve MULTI.
- Puertos simulados: `Ui/Desarrollo/AnalizadorSimulado.cs` hace de radio con lo que tenga el
  equipo simulado; `CUADERNO_TECLAS_ANALIZADOR=3DSS|SPAN+` (solo simulado) pulsa las teclas
  para el retrato.
- `Ui/Vistas/AnalizadorDelEquipo.xaml`: va en el hueco `AreaDelAnalizador` del visor del
  frontal; se ve en cualquier modo. La cascada del audio queda solo en la pestaña Digital.
- Captura con tramas reales: `docs/capturas/analizador-ft710-real.png`.

## La biblioteca de FTDI

`lib/ftdi/LibFT4222-64.dll` 1.4.8.0, del paquete oficial `LibFT4222-v1.4.8.zip`
(`https://ftdichip.com/wp-content/uploads/2025/06/LibFT4222-v1.4.8.zip`). La web de FTDI
devuelve 403 (Cloudflare) a cualquier descarga automática, así que se tomó la copia byte a
byte de web.archive.org (captura 20260215034428). Firma Authenticode **válida** de «Future
Technology Devices International Ltd» (DigiCert), SHA-256
`9B9E381E87B44084E03EB9D22D1C87A5F9EDBAAEED897E749031506307C390B5`. Licencia en
`lib/ftdi/LICENCIA-FTDI.txt`; notas de versión en `lib/ftdi/FT4222H_Lib_1.4.8_Release_info.pdf`.
El proyecto Radio la copia junto al ejecutable (y a `publicar`, así que entra en el instalador).

**Ojo con la licencia de FTDI**: la cláusula 1.3 deja al *usuario* de un dispositivo con chip
FTDI instalarla en su ordenador, y solo al *fabricante o distribuidor* del dispositivo
distribuirla con él; la 3.1.7 prohíbe ponerla a disposición de terceros. Para el Cuaderno de
El operador en sus máquinas vale; si el instalador se publica para otros, lo prudente es no meter el
DLL y pedir al usuario que instale el paquete de FTDI (el programa ya avisa si falta).
