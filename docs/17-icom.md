# 17 · Control CI-V de ICOM

> **Sin probar con radio real.** No hay ninguna radio ICOM en la estación: todo está programado
> según los manuales CI-V oficiales y probado solo contra una radio simulada
> (`tests/Nodisla.Cuaderno.Radio.Pruebas/Icom/`). `ProbadoConRadio = false` en todos los modelos y
> la interfaz lo dice. Con el FT-710 el manual no bastó; aquí pasará lo mismo: ver §6.

## 1. Modelos

| Modelo | Clave | CI-V | Velocidades | A / B | ATT | Acoplador (1C 01) | XIT | APF | Memorias |
|---|---|---|---|---|---|---|---|---|---|
| IC-7300 | `icom-ic7300` | 94h | 115200 (USB), 19200, 9600, 4800 | VFO A/B | 20 dB | interno | sí | no | 1–99 |
| IC-705 | `icom-ic705` | A4h | 115200, 19200, 9600, 4800 | VFO A/B | 20 dB | AH-705 externo | sí | no | no (bancos) |
| IC-7610 | `icom-ic7610` | 98h | 115200, 19200, 9600, 4800 | MAIN/SUB | 3–45 dB | interno | sí | sí | 1–99 |
| IC-9700 | `icom-ic9700` | A2h | 115200, 19200, 9600, 4800 | VFO A/B de MAIN | 10 dB | no | no | no | no (bancos) |
| IC-7100 | `icom-ic7100` | 88h | 19200, 9600, 4800, 1200 | VFO A/B | 12 dB | AH-4 externo | no | no | no (bancos) |
| IC-7851 | `icom-ic7851` | 8Eh | 115200, 19200, 9600, 4800 | MAIN/SUB | 3–21 dB | interno | sí | sí | 1–99 |

IC-7200, IC-7410 e IC-9100 **no** están: no se han bajado sus manuales y no se meten a ciegas.

Manuales usados (icomjapan.com, 29-09-2026, en la carpeta temporal, no en el repo):
IC-7300_ENG_FM_12b (cap. 19), IC-705_ENG_CI-V_6, IC-7610_ENG_CI-V_4, IC-9700_ENG_CI-V_4,
IC-7100_ENG_FM_5 (cap. 20), IC-7850_7851_ENG_IM_3 (cap. 18).

## 2. Protocolo (`Control/Icom/`)

- `TramaCiv`, `BcdCiv`, `AnalizadorDeTramasCiv`: `FE FE dest orig cmd [sub] [datos] FD`; `FB` bien,
  `FA` no admitido, `FC` colisión (se tira). Frecuencia en BCD invertido de 5 bytes (vale hasta
  9,99 GHz: cubre el 23 cm del IC-9700; `BcdCiv.Frecuencia(hz, 6)` queda para el IC-905).
- `CanalCiv` (base): una pregunta a la vez; **el eco se tira** (trama con origen = ordenador), la
  respuesta es la trama dirigida al ordenador con `FB`/`FA` o la misma orden; lo que llega a la
  dirección 00 (y avisos al ordenador que no son la respuesta) es **transceive** → evento
  `TramaEspontanea`. `CanalSerieCiv`: lector en segundo plano que solo lee lo que `BytesToRead`
  dice que hay (lección del FT-710). DTR/RTS abren en falso.
- **Red (LAN/Wi-Fi del IC-705, IC-9700, IC-7610): no implementada.** Iría como otra hija de
  `CanalCiv` (protocolo UDP propio de ICOM).

## 3. Control (`ControlIcom`)

Implementa `IEquipoAvanzado`, `IEquipoConDosVfos`, `IEquipoConTeclas`, `IEquipoConBotonera`,
`IEquipoConEncendido`, `IEquipoDeModelo`, `IPttDirecto`, `ISueltaDeEmergenciaPtt`,
`IAvisaDePerdidaDeComunicacion`. Registro en el catálogo: `ProveedorIcom` (reflexión,
`OrdenDeDeteccion = 10`, identifica con `19 00` a la difusión y luego a cada dirección).

- Sondeo: `03`, `04`, `1A 06`, `1C 00` (consulta), `0F`, `07 D2` (MAIN/SUB), `25/26 00/01`,
  `21 01/02/00`, `14 0A`, `15 02`. Además, los avisos transceive (`00`/`01`) se pintan al llegar.
- **Reparto A/B**:
  - IC-7300/705/7100: A/B = VFO A/B. `25/26 00` = el elegido, `01` = el otro. Estas radios no
    dicen por CI-V si están en A o en B: el control lo recuerda (empieza en A; `07 00/01` al
    cambiar). Si el operador pulsa A/B en la radio, las frecuencias salen bien pero las letras
    pueden ir cambiadas hasta que se pulse A/B desde el programa.
  - IC-7610/7851: A = MAIN, B = SUB (`25/26` 00/01 fijos, `07 D0/D1`, `07 D2` para saber cuál está
    elegido, `07 B0` intercambia). Igualar copia el elegido sobre el otro con `25/26`.
  - IC-9700: A/B = VFO A/B de la banda MAIN (`25/26` solo valen para MAIN y no hay `29`); la SUB
    no se enseña. `07 B0` intercambia MAIN/SUB, así que «intercambiar» lee y reescribe A/B.
- Mandos (`MandosIcom`): AF/RF/SQL/NR/NB/MON/VOX/antiVOX/MIC en % (`14 xx`, 0–255), potencia en W
  (`14 0A`, % del máximo), tono CW (`14 09`), velocidad (`14 0C`), compresor (`14 0E`), ATT (`11`),
  preamp (`16 02`), AGC (`16 12`), NB/NR/ANF/VOX/BK-IN/MN/bloqueo (`16 22/40/41/46/47/48/50`),
  APF (`16 32`), ancho de filtro (`1A 03`, índice), split (`0F`), RIT/XIT (`21 01/02`, desplazamiento
  `21 00` compartido), acoplador (`1C 01`). Al conectar se pregunta cada uno; el que contesta `FA`
  queda fuera de `Mandos`.
- Teclas: M▶V (`0A`), V/M (`07`/`08`, recordado), banda ±, A/B, borrar clarificador (`21 00 00 00 00`).
  Sin QMB, ZIN ni DSP RESET (no hay orden CI-V).
- Bandas: ICOM no tiene «ve a la banda». Se **lee** el registro 01 de la pila (`1A 01 bb 01`, nunca
  se escribe) y se pone su frecuencia (`05`), modo y filtro (`06`) y datos (`1A 06`). IC-7100: pila
  no verificada → frecuencia fija por banda.
- Medidores (`15 02/11/12/13/14/15/16`) con las escalas de cada manual (S9 = 120, Po 100 % = 213,
  ROE 1,5 = 48, Vd/Id por modelo).
- Memorias: solo lectura `1A 00 cc cc` (IC-7300/7610/7851); ir a memoria `08 cc cc`.
- Encendido: `N×FE + 18 01` (150 FE a 115200… 7 a 4800, según manual). Apagado `18 00` solo por
  `ApagarAsync` (confirmación del operador).

## 4. Seguridad (`OrdenesIcom`)

Prohibidas siempre: `18 00` (salvo `ConApagadoAutorizadoAsync`), `09`, `0B`, `1A 00`/`1A 01` con
datos, `1A 02`, `1E 03` con datos, `17` (CW) y `28` (voz): transmiten o escriben. `1C 00 01` y
`1C 01 02` solo dentro de `ConTransmisionAutorizadaAsync`, que abre únicamente `IPttDirecto`
(vigilante). En crudo, además, nada que transmita y nada de escribir en el menú (`1A 05` con
datos). `IControlEquipo.PonerPttAsync(true)` se rechaza con `GuardiaDelPtt`.

TUNE: `PrepararSintonia()` + transmisión del vigilante → `1C 01 02`; `EsperarFinDeSintoniaAsync`
mira `1C 01` hasta que deja de valer 02; al soltar, `1C 00 00` y, si sigue sintonizando, `1C 01 01`.
`ControlIcom` implementa `IEquipoConSintonia` (sí ve el fin de la sintonía); el botón TUNE sale donde el mando `Sintonizador` está disponible (no en el IC-9700).

## 5. Pruebas

`ControlIcomPruebas` (76 casos): BCD y tramas partidas, eco con y sin, cada modelo conecta y
enseña solo sus mandos, `FA` deja fuera el mando, MAIN/SUB del IC-7610, split, transceive, **cada
botón manda su trama** y la radio simulada nunca queda en antena, PTT solo por el vigilante, TUNE
solo dentro del vigilante, vías de suelta, orden en crudo peligrosa rechazada, apagar/encender,
medidores, memorias sin escribir, catálogo por reflexión e identificación `19 00`.

## 6. Qué confirmar con cada radio real

1. **Eco**: por USB con «CI-V USB Echo Back» ON/OFF y por REMOTE (CT-17). Que ninguna respuesta
   se pierda ni se duplique.
2. **Transceive**: que los avisos llegan a 00 (y no a E0) al girar el dial, y en MAIN/SUB, de cuál.
3. **A/B en IC-7300/705/7100**: confirmar que no hay forma de leer si está en A o B.
4. **Pila de banda**: formato real de `1A 01` (byte de datos), qué es el registro 01 en el IC-705
   («display on left side»), y los códigos del IC-7100.
5. **Escalas**: Po→vatios, ROE, ALC, Vd/Id; potencia mínima real con `14 0A = 0`.
6. **1C 00** en consulta no transmite (manual: «Send/read»). Probar con carga ficticia.
7. **TUNE**: que `1C 01 02` arranca y que `1C 01` vuelve a 01 al acabar; que `1C 01 01` lo para.
8. **Encendido** `18 01` con la radio apagada por USB (en el IC-7300 el puerto sigue vivo; en otras puede desaparecer).
9. **Pasos del dial** (`10`) del IC-705 (se ha supuesto la tabla del IC-7100).
10. IC-9700: qué pasa con `25/26` y `03` cuando la SUB está elegida.
