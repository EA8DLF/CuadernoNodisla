# Tablas de los protocolos MSK144, FST4 y FST4W

## Qué hay aquí

Cuatro ficheros de datos con las **matrices de los códigos correctores** que no se pueden deducir:

| Fichero | Código | Modo | Ecuaciones | Pesos de fila | Peso de columna |
|---|---|---|---|---:|---:|
| `tablas-msk144.txt` | LDPC(128,90) | MSK144 (77 bits + CRC-13) | 38 | 10 y 11 | 3 |
| `tablas-msk40.txt` | LDPC(32,16) | mensajes cortos de MSK144 (resumen 12 + informe 4) | 16 | 5, 6 y 7 | 3 |
| `tablas-fst4.txt` | LDPC(240,101) | FST4 (77 bits + CRC-24) | 139 | 5 y 6 | 3 |
| `tablas-fst4w.txt` | LDPC(240,74) | FST4W (50 bits + CRC-24) | 166 | 4 y 5 | 3 |

Cada fichero trae la matriz de paridad (una línea por ecuación) **y** la generadora, para cruzarlas
al cargar: si discrepan en un solo bit se rechaza el fichero entero, igual que en `tablas-ft8.txt`.
Ninguna de las cuatro tiene ciclos de longitud cuatro. Las cuatro se comprueban en cada compilación
en `LdpcGeneralizadoPruebas`.

El código corto de MSK144 no lleva el mensaje en los 16 primeros bits de la palabra: la línea
`MENSAJE-EN` dice en qué posición de la palabra emitida viaja cada bit del mensaje. Es lo que hizo
falta generalizar en `Ldpc/CodigoLdpc` (posiciones de mensaje arbitrarias, de forma aditiva) y el
cargador general está en `Ldpc/TablaLdpc`.

## Procedencia

### FST4 y FST4W — licencia MIT

| | |
|---|---|
| Proyecto | [`ft8_lib`](https://github.com/howard0su/ft8_lib), rama con FST4/FST4W de howard0su sobre el `ft8_lib` de Kārlis Goba (YL3JG) |
| Licencia | MIT — **Copyright (c) 2018 Kārlis Goba** |
| Fichero | `ft8/fst4_constants.c` |
| Versión | `20a9a9a78d26fb43e42b0e6c9269bcde6f998dfb`, del 22-04-2026 |
| Traído el | 26-09-2026 |

**Sólo datos.** El sincronismo (S1 = 0 1 3 2 1 0 2 3, S2 = 2 3 1 0 3 2 0 1, en los símbolos 0, 38,
76, 114 y 152), el polinomio del CRC-24 (0x100065B), la mezcla de 77 bits y las velocidades de cada
periodo salen de la *Quick-Start Guide to FST4 and FST4W* (Franke, Somerville y Taylor), apéndice A,
y están escritos como constantes en `Fst4/ParametrosFst4.cs`.

### MSK144 y MSK40 — constantes del protocolo, de WSJT-X (GPLv3)

No existe copia con licencia permisiva de estas dos matrices: sólo están en el código fuente de
WSJT-X. Se han traído **únicamente los números** —las constantes del protocolo, que no son código—
con esta procedencia:

| | |
|---|---|
| Proyecto | [WSJT-X](https://sourceforge.net/projects/wsjt/), de Joe Taylor (K1JT), Steve Franke (K9AN) y otros |
| Licencia del proyecto | GPLv3. **No se ha copiado, traducido ni compilado una sola línea de código.** |
| Ficheros | `lib/ldpc_128_90_reordered_parity.f90`, `lib/ldpc_128_90_generator.f90`, `lib/bpdecode40.f90` (ecuaciones y orden de columnas del (32,16)), `lib/encode_msk40.f90` (generadora del (32,16)) |
| Versión | rama `master` del repositorio de SourceForge, leída el 26-09-2026 |
| Descripción del protocolo | S. Franke y J. Taylor, «The MSK144 Protocol for Meteor-Scatter Communication», *QEX* julio/agosto 2017 |

De la misma fuente, y por la misma razón (constantes, no código), salen la palabra de sincronismo
(0 1 1 1 0 0 1 0), el polinomio del CRC-13 (0x15D7) con sus 83 bits protegidos, la semilla 146 del
resumen de los mensajes cortos y la tabla de los dieciséis informes. Todo está escrito como
constantes en `Msk144/`.

El artículo de QEX de 2017 describe la versión anterior del protocolo (72 bits, CRC-8, código
(128,80)); la de 77 bits con CRC-13 y código (128,90), que es la que se usa desde 2018 y la de
estas tablas, no tiene artículo propio. La modulación, la trama {S8, D48, S8, D80}, el mensaje
corto y el decodificador coherente con promediado de tramas sí están en el artículo y son de donde
sale la implementación.

## Qué se comprobó antes de fiarse

Antes de convertir nada, cada matriz se verificó en Python codificando trescientos mensajes al azar
con la generadora y comprobando que cumplían todas las ecuaciones de paridad (0 fallos en los cuatro
códigos), y que la lista «ecuaciones por bit» y la lista «bits por ecuación» de cada fuente decían lo
mismo. Después, ya en C#, `LdpcGeneralizadoPruebas` repite en cada compilación: dimensiones, pesos,
unos totales, ausencia de ciclos de longitud cuatro, cruce de las dos tablas, e ida y vuelta sin ruido.

## Descargas hechas para esto (26-09-2026)

Al *scratchpad* de la sesión, no al repositorio: los cuatro ficheros de constantes de WSJT-X y
`genmsk_128_90.f90`, `genmsk40.f90`, `encode_128_90.f90`, `crc13.cpp`, `hash.f90` y `fst4sim.f90`
(sólo para leer las constantes: sincronismo, relleno del CRC, semilla del hash, instante de
arranque); de `ft8_lib` los `fst4_constants.c`, `fst4_ldpc.h`, `crc.c`, `encode.c`, `fst4_decode.c`
y `LICENSE`; el artículo de QEX de MSK144 y la *Quick-Start Guide* de FST4 (PDF, de
wsjt.sourceforge.io); y `lookup3.c` de Bob Jenkins (dominio público) para contrastar el hash que ya
tenía el módulo de WSPR.

## Cómo se registran en el módem (lo hace el integrador)

```csharp
registro
    .Anadir(new ModoMsk144(
        TablaLdpc.Cargar(ParametrosMsk144.FicheroDeTablas, 128, 90),
        TablaLdpc.Cargar(ParametrosMsk144.FicheroDeTablasCortas, 32, 16)))
    .Anadir(new ModoFst4(TablaLdpc.Cargar(ParametrosFst4.FicheroDeTablas, 240, 101), esFst4w: false, periodoSegundos: 60))
    .Anadir(new ModoFst4(TablaLdpc.Cargar(ParametrosFst4.FicheroDeTablasFst4w, 240, 74), esFst4w: true, periodoSegundos: 120));
```

- MSK144: `ModoMsk144.DecodificarTrozo` decodifica bloques con su segundo de inicio para enseñar
  los pings en tiempo real (solapar ≥ 0,5 s entre bloques); los mensajes cortos sólo salen tras
  `Decodificador.RecordarPar(llamado, propio)`.
- FST4: el periodo es un ajuste (`CambiarPeriodo`), no un modo; `FrecuenciaDeAnalisis` cambia con él.
- `EsElCodigoReal` de cada modo es falso si falta su fichero de tablas.
