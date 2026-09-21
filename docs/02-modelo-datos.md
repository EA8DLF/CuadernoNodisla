# 02 — Modelo de datos

Análisis del modelo de datos de **Log4OM NextGen 2.40.0.0** (instalación real de EA8DLF) y
propuesta de modelo para **Cuaderno NODISLA**.

Fecha del análisis: 2026-09-21
Fuentes: `%AppData%\Log4OM2\` (solo lectura, ninguna base fue modificada).

---

## 1. Resumen ejecutivo

Log4OM reparte sus datos en **tres bases SQLite + un montón de ficheros sueltos**:

| Almacén | Tamaño | Tablas | Contenido |
|---|---:|---:|---|
| `Log4OMNG.SQLite` | 20 KB | **2** | El cuaderno: `Informations` + `Log` (97 columnas) |
| `ExternalData.SQLite` | 106 MB | **2** | `ClubMembers` (142.916) + `State` (1.601.718, padrón FCC) |
| `Activations.SQLite` | 156 MB | **4** | `Awards` (87), `AwardConfig` (315), `AwardReferences` (553.064), `AwardCustomReports` (5) |
| `mysql.sql` / `mariadb.sql` | 10 KB | **2** | El mismo esquema del cuaderno para servidor: `informations` + `log` |

**Total: 8 tablas** en el original (2 de ellas son la misma tabla `Log` replicada en el dialecto MySQL).

El resto del "modelo" vive fuera de la base: DXCC en `country.xml` y `ctyfile.json`, catálogos en CSV,
configuración en `user/config.json` (170 KB), definiciones de diploma en `temp/award_*.xml`.

Diagnóstico corto: **el esquema de QSO es sólido y merece copiarse casi tal cual**, pero Log4OM mete
tres estructuras JSON dentro de columnas de texto (`qsoconfirmations`, `contactreferences`,
`myreferences`) que en SQLite **no son consultables** — eso obliga al programa a cargar QSOs en
memoria para calcular diplomas. Es el punto donde el clon debe mejorar.

---

## 2. El cuaderno: `Log4OMNG.SQLite`

### 2.1 Tabla `Informations` (1 fila)

```sql
CREATE TABLE Informations (
  ProgramName    VARCHAR(50) NOT NULL,
  ProgramVersion VARCHAR(50),
  DBVersion      INT(20) NOT NULL DEFAULT (1)
);
```

Control de versión de esquema para las migraciones. En MySQL se inicializa con `DBVersion = 2`.

### 2.2 Tabla `Log` — 97 columnas

Clave primaria **natural y compuesta**: `PRIMARY KEY (band, callsign, mode, qsodate)`.
Además dos índices únicos: `IDX_LOG_KEYS (QsoDate, Callsign, Band, Mode)` e `IDX_LOG_QSOID (QsoId)`.

No hay ninguna otra clave. **No existe índice por `callsign` solo, ni por `dxcc`, ni por fecha suelta.**

Columnas agrupadas por función:

**Identidad y clave**
`qsoid` (VARCHAR 17, sello de tiempo `yyyyMMddHHmmssfff`), `callsign`, `band`, `mode`, `qsodate` (DATETIME), `qsoenddate`

**Estación corresponsal (DX)**
`name`, `address`, `qth`, `email`, `callsignurl`, `gridsquare`, `lat`, `lon`, `cont`, `country`, `dxcc`, `cqzone`, `ituzone`, `pfx`, `state`, `cnty`, `age`, `eqcall`, `contactedop`, `ownercallsign`, `operator`

**Mi estación**
`stationcallsign`, `myname`, `mystreet`, `mycity`, `mystate`, `mycnty`, `mypostalcode`, `mycountry`, `mydxcc`, `mycqzone`, `myituzone`, `mygridsquare`, `mylat`, `mylon`, `myrig`, `antenna`, `mysig`, `mysiginfo`

**Radio / señal**
`freq`, `freqrx`, `bandrx`, `rstsent`, `rstrcvd`, `txpwr`, `rxpwr`, `propmode`, `antpath`, `antaz`, `antel`, `distance`, `satelliteqso`, `satmode`, `satname`, `swl`

**Meteor scatter**
`maxbursts`, `nrbursts`, `nrpings`, `msshower`

**Propagación en el momento del QSO**
`aindex`, `kindex`, `sfi`, `MUFDay`, `Reliability`, `Sunspots`, `SignalToNoiseRatio`

**Concurso**
`contestid`, `class`, `precedence`, `arrlcheck`, `arrlsect`, `srx`, `srxstring`, `stx`, `stxstring`

**QSL / diplomas / notas**
`qslmsg`, `qslvia`, `qsocomplete`, `qsorandom`, `comment`, `notes`, `sig`, `siginfo`, `forceinit`,
`programid`, `programversion`,
`qsoconfirmations`, `contactreferences`, `myreferences`, `contactassociations`, `myassociations`

### 2.3 Los tres campos JSON (el punto flojo)

En SQLite son `VARCHAR(3000)`; en MySQL/MariaDB son tipo `JSON` nativo. Contenido real extraído del
backup ADIF del usuario:

**`qsoconfirmations`** — una entrada por servicio, siempre las 7:

```json
[{"CT":"QSL","S":"No","R":"No","SV":"Electronic","RV":"Electronic"},
 {"CT":"LOTW","S":"No","R":"Yes","SV":"Electronic","RV":"Electronic","RD":"2023-03-11T00:00:00Z"}]
```

`CT` = tipo (`QSL`, `EQSL`, `LOTW`, `QRZCOM`, `HAMQTH`, `HRDLOG`, `CLUBLOG`) · `S`/`R` = enviada/recibida
(`Yes`/`No`/`Requested`/`Queued`/`Ignore`) · `SV`/`RV` = vía (`Electronic`, `Bureau`, `Direct`, `Manager`) ·
`SD`/`RD` = fechas ISO-8601 (opcionales).

**`contactreferences`** y **`myreferences`** — referencias de diploma del QSO:

```json
[{"AC":"DXCC","R":"281","G":"EU","SUB":[],"GRA":[]},
 {"AC":"VUCC","R":"IN62","G":"281","SUB":[],"GRA":[]},
 {"AC":"POTA","R":"PT-0361","G":"PT-13","S":2,"SUB":[],"GRA":[]}]
```

`AC` = código de diploma · `R` = referencia · `G` = grupo · `SG` = subgrupo · `S` = puntuación ·
`SUB` = configuraciones a las que se ha enviado · `GRA` = configuraciones ya concedidas.

`contactassociations` / `myassociations` guardan el mismo tipo de dato en texto plano separado
(club, sección) y son redundantes con lo anterior.

### 2.4 Esquema MySQL / MariaDB

`mysql.sql` y `mariadb.sql` son **idénticos salvo un comentario** (`-- NEWFIELDS` vs `-- NEWFIELD`).
Crean el mismo par de tablas con `##DATABASENAME##` como plantilla, motor **InnoDB**, charset **utf8**
(no utf8mb4 — limitación real con emojis en notas), y crean el usuario `##DATABASENAME##User` con
contraseña **igual al nombre de usuario** y permisos `SELECT/INSERT/UPDATE/DELETE/INDEX` sobre las dos
tablas. Diferencias de tipos respecto a SQLite:

| SQLite | MySQL |
|---|---|
| `qsoid VARCHAR(17)` | `qsoid BIGINT(17)` |
| `contactreferences VARCHAR(3000)` | `contactreferences JSON` |
| `COLLATE NOCASE` por columna | collation de tabla utf8 |
| `PRIMARY KEY (band, callsign, mode, qsodate)` | `PRIMARY KEY (mode, qsodate, band, callsign)` + `UNIQUE (qsoid)` |

En la instalación del usuario `DbConfiguration.DatabaseType = 1`, con `Path`, `Address`, `User` y
`DatabaseName` vacíos: está usando el SQLite local. El cuaderno real (1.838 QSOs) vive en el fichero
que la aplicación tenía abierto; el `Log4OMNG.SQLite` de `%AppData%` está vacío pero **conserva el
esquema completo**, que es lo que interesa aquí.

---

## 3. `ExternalData.SQLite` — datos de terceros

```sql
CREATE TABLE ClubMembers (
  ClubCode VARCHAR(50)  COLLATE NOCASE NOT NULL,
  Callsign VARCHAR(15)  COLLATE NOCASE NOT NULL,
  MemberID VARCHAR(20),
  PRIMARY KEY (ClubCode, Callsign)
);

CREATE TABLE State (
  Source   VARCHAR(50) COLLATE NOCASE NOT NULL,
  Callsign VARCHAR(50) COLLATE NOCASE NOT NULL,
  State    VARCHAR(50) COLLATE NOCASE NOT NULL,
  PRIMARY KEY (Source, Callsign)
);
CREATE UNIQUE INDEX State_IDX1 ON State (Source, Callsign);  -- redundante con la PK
```

`ClubMembers` (142.916 filas): `1010` 77.507 · `SKCC` 30.763 · `FISTS` 16.564 · `NAQCC` 12.310 ·
`070` 3.058 · `HSC` 2.076 · `CDXC` 638. Se descargan de `hamclubs.info` según `defaulthamclubs.csv`.
Ejemplo: `('CDXC', '2E0CEY', '2E0CEY')`.

`State` (1.601.718 filas): un único `Source = 'FCC'` — el padrón completo de licencias USA para
autorrellenar el estado. Ejemplo: `('FCC', 'KC8DNW', 'OH')`. **Esta tabla es el 90 % de los 106 MB.**

---

## 4. `Activations.SQLite` — motor de diplomas

Es el corazón del sistema de *awards*. Cuatro tablas:

### `Awards` (87 filas) — catálogo de diplomas

PK `(AwardCode, ValidFrom, ValidTo)`. Campos clave:
`AwardName`, `AwardType`, `ConfirmationMethod`, `ValidationMethod`, `QsoField`, `QsoLeadingField`,
`QsoFieldSeparator`, `QsoFieldExact`, `MatchField`, `QsoQuery`, `LeadingString`, `TrailingString`,
`AllowedDxcc`, `AllowedEmissionType`, `AllowedModes`, `AllowedBands`, `AwardFilters`, `GrantCode`,
`ReferencePrefixes`, `AllowMultipleRefs`, `ReferenceLess`, `AdifGrantedOutput`, `Protected`, `AwardURL`,
`DownloadURL`, `ReferenceURL`, `LastUpdate`, `Note`.

`AwardType` toma tres valores: **`REFERENCE`** (57 — SOTA, POTA, WWFF, WCA…), **`QSOFIELDS`** (22 —
DXCC, WAS, WAZ… se resuelven leyendo campos del QSO) y **`CALLSIGN`** (8 — se resuelven por patrón de
indicativo). Ejemplos reales:

```
AA       Antarctica Award                    REFERENCE  conf: LOTW;QSL
CATCH22  CATCH 22 award                      QSOFIELDS  QsoField: Dxcc
CCC      RSGB Commonwealth Century Club      CALLSIGN   conf: LOTW;QSL
```

### `AwardConfig` (315 filas) — variantes de un diploma

PK `(AwardCode, ConfigName, ValidFrom, ValidTo)`. Cada diploma puede tener varias "clases":
modo, banda, tipo de emisión, continente, *chaser* vs *activator*, anual o no.

```
TPEA / 5BTPEA   "Work all 52 Spanish provinces on 80,40,20,15,10m"  Bands: 80m;40m;20m;15m;10m
ARM  / CW       EmissionType: CW      ARM / DIGITAL      ARM / PHONE      ARM / MIXED
```

`ContactType` distingue **Chaser / Activator**. `CSVFormat` define el fichero de solicitud.

### `AwardReferences` (553.064 filas) — el catálogo mundial de referencias

PK `(AwardCode, ReferenceCode, ValidFrom, ValidTo)` + tres índices adicionales.
Columnas: `ReferenceCodeAlias`, `ReferenceDescription`, `AllowedDXCC`, `ReferenceGroup`,
`ReferenceSubGroup`, `GridSquare`, `ReferenceScore`, `ReferenceBonusScore`, `Valid`, `SearchPattern`.

Reparto: SOTA 182.279 · POTA 94.928 · WCA 70.940 · WWFF 66.699 · ARLHS 16.076 · DAI 14.714 ·
DCI 13.990 · WWBOTA 11.940 · DME 8.131 · DFCF 7.520 · DTMBA 7.440 · WHSA 5.262 · WAB 4.719 · DLD 4.455…

```
SOTA  3B8/MU-001  "Piton de la Petite Rivière Noire"  IOTA/grupo Mauritius  QRA LG89qo  score 10
POTA  AD-0001     "Valle del Sorteny Nature Park"     grupo AD-OR   AllowedDXCC #203#
IOTA  AF-001      "Agalega Islands"  subgrupos "Agalega Islands|North|South|"
```

`AllowedDXCC` es una lista de enteros delimitados: `#24#;#514#;#336#;…` — hasta 3.000 caracteres,
**no indexable de forma útil**. Es el otro punto débil del original.

### `AwardCustomReports` (5 filas)

`ReportId` PK + `DefinitionXml` TEXT con la definición del informe: `DXCC_CHALLENGE`,
`DX_MARATHON_SCORE`, `DX_MARATHON_CHALLENGE`, `TENTEN`, `WAI`.

---

## 5. Datos de referencia fuera de la base

| Fichero | Tamaño | Contenido |
|---|---:|---|
| `country.xml` | 1,1 MB | Entidades DXCC completas: `ArrlPrefix`, `Continent`, `CountryName`, `CqZone(+List)`, `ItuZone(+List)`, `IaruRegion`, `Dxcc`, `Latitude/Longitude`, `Active`, y `CountryPrefixList` con **regex** (`^T6.*|^YA.*`) y ventanas `StartDate`/`EndDate` |
| `ctyfile.json` | 8,9 MB | Country file de AD1C (`cty_wt_mod`) convertido: por DXCC, zonas, coordenadas, QRA y lista de prefijos con `ExactMatch` |
| `clublog.xml` | 17 MB | Excepciones de ClubLog (prefijos, indicativos, ventanas temporales) |
| `lotw.txt` | 6,3 MB | Lista de usuarios de LoTW |
| `arrl_dxcc.csv` | 7,7 KB | `dxcc;NOMBRE` (con `(DELETED)`) |
| `states.csv` | 40 KB | `DXCC;CODE;DESCRIPTION` — subdivisiones primarias (estados, provincias, cantones) |
| `ITU_IARU.csv` | 535 B | `ITU,IARU` |
| `modelist.csv` | 3,7 KB | `Log4om MODE;ADIF 2.x;ADIF 3.x;ADIF 3.x SUBMODE` — traducción de modos |
| `bandlist.txt` | 142 B | Bandas ADIF de `136kHz` a microondas |
| `contest.csv` | 9,3 KB | `Nombre;CONTEST_ID` |
| `propagation.csv` | 415 B | `Nombre;PROP_MODE` |
| `satellites.csv` | 1,2 KB | `Nombre;SAT_NAME` |
| `antpath.csv` | 50 B | `Short Path;S` … |
| `rst.xml` | 24 KB | Valores RST por tipo de modo |
| `flags.csv` | 8,5 KB | `Index,Country_Name,Abbrev,DXCC` para banderas |
| `ranking.csv` | 2,9 KB | `dxcc;puesto` (ranking de rareza) |
| `awardOverride.csv` | 481 B | `AwardCode;Reference;Dxcc` — correcciones manuales |
| `defaulthamclubs.csv` | 367 B | URLs de descarga de listas de club |
| `bandplan/*.xml` | 8 ficheros | Band plan por región IARU y por DXCC |
| `temp/award_*.xml` | hasta 34 MB | Descargas de referencias por diploma |
| `user/config.json` | 170 KB | Toda la configuración, con **perfiles múltiples** (`UserConfigs[]`, ~200 claves cada uno) |

Detalle importante de `config.json`: la configuración es un **array de perfiles** con
`ConfigurationId`, `FriendlyDescription`, `StationConfigs`, `MyReferences`, `Reference1..6`,
`Association1/2`, credenciales de LoTW/eQSL/QRZ/ClubLog/HRDLog/HamQTH/Cloudlog, CAT, cluster, macros,
`BandMapConfigs`, `Alerts`, `WebPages`. **Las contraseñas van en claro en el JSON.**

---

## 6. Mapeo contra ADIF 3.1.5

Verificado contra el backup real del usuario (`EA8DLF_20260903_135046_backup.adi`, 1.838 QSOs,
**92 campos ADIF distintos** usados).

### 6.1 Cobertura directa (columna ↔ campo ADIF)

| Columna Log4OM | Campo ADIF | Columna Log4OM | Campo ADIF |
|---|---|---|---|
| `callsign` | `CALL` | `mygridsquare` | `MY_GRIDSQUARE` |
| `band` / `bandrx` | `BAND` / `BAND_RX` | `mylat` / `mylon` | `MY_LAT` / `MY_LON` |
| `mode` | `MODE` (+ `SUBMODE`) | `myrig` | `MY_RIG` |
| `qsodate` | `QSO_DATE` + `TIME_ON` | `antenna` | **`MY_ANTENNA`** |
| `qsoenddate` | `QSO_DATE_OFF` + `TIME_OFF` | `mysig` / `mysiginfo` | `MY_SIG` / `MY_SIG_INFO` |
| `freq` / `freqrx` | `FREQ` / `FREQ_RX` | `txpwr` / `rxpwr` | `TX_PWR` / `RX_PWR` |
| `rstsent` / `rstrcvd` | `RST_SENT` / `RST_RCVD` | `propmode` | `PROP_MODE` |
| `name`, `address`, `qth`, `email` | ídem | `antpath`, `antaz`, `antel` | `ANT_PATH`, `ANT_AZ`, `ANT_EL` |
| `gridsquare`, `lat`, `lon` | ídem | `distance` | `DISTANCE` |
| `cont`, `country`, `dxcc` | `CONT`, `COUNTRY`, `DXCC` | `satmode`, `satname` | `SAT_MODE`, `SAT_NAME` |
| `cqzone` / `ituzone` | `CQZ` / `ITUZ` | `maxbursts`, `nrbursts`, `nrpings`, `msshower` | `MAX_BURSTS`, `NR_BURSTS`, `NR_PINGS`, `MS_SHOWER` |
| `pfx` | `PFX` | `aindex`, `kindex`, `sfi` | `A_INDEX`, `K_INDEX`, `SFI` |
| `state` / `cnty` | `STATE` / `CNTY` | `contestid` | `CONTEST_ID` |
| `age`, `swl` | `AGE`, `SWL` | `class`, `precedence`, `arrlcheck`, `arrlsect` | `CLASS`, `PRECEDENCE`, `CHECK`, `ARRL_SECT` |
| `eqcall`, `contactedop`, `ownercallsign`, `operator` | `EQ_CALL`, `CONTACTED_OP`, `OWNER_CALLSIGN`, `OPERATOR` | `srx`, `srxstring`, `stx`, `stxstring` | `SRX`, `SRX_STRING`, `STX`, `STX_STRING` |
| `stationcallsign` | `STATION_CALLSIGN` | `qslmsg`, `qslvia` | `QSLMSG`, `QSL_VIA` |
| `myname`, `mystreet`, `mycity`, `mystate`, `mycnty`, `mypostalcode`, `mycountry` | `MY_*` | `qsocomplete`, `qsorandom`, `forceinit` | `QSO_COMPLETE`, `QSO_RANDOM`, `FORCE_INIT` |
| `mydxcc`, `mycqzone`, `myituzone` | `MY_DXCC`, `MY_CQ_ZONE`, `MY_ITU_ZONE` | `comment`, `notes`, `sig`, `siginfo` | `COMMENT`, `NOTES`, `SIG`, `SIG_INFO` |
| `programid`, `programversion` | `PROGRAMID`, `PROGRAMVERSION` (cabecera, guardados por QSO) | `callsignurl` | `WEB` (aprox.) |

### 6.2 Campos ADIF que Log4OM **no** guarda en columna, sino en JSON

Se serializan al exportar y se reconstruyen al importar:

- **QSL tradicional**: `QSL_SENT`, `QSL_RCVD`, `QSL_SENT_VIA`, `QSL_RCVD_VIA`, `QSLSDATE`, `QSLRDATE`
- **eQSL**: `EQSL_QSL_SENT`, `EQSL_QSL_RCVD`, `EQSL_QSLSDATE`, `EQSL_QSLRDATE`
- **LoTW**: `LOTW_QSL_SENT`, `LOTW_QSL_RCVD`, `LOTW_QSLSDATE`, `LOTW_QSLRDATE`
- **Subidas**: `CLUBLOG_QSO_UPLOAD_STATUS/_DATE`, `QRZCOM_*`, `HRDLOG_*`, `HAMQTH_*`
- **Referencias**: `IOTA`, `IOTA_ISLAND_ID`, `POTA_REF`, `SOTA_REF`, `WWFF_REF` y sus `MY_*`,
  además de DXCC/VUCC/WAC/WAE calculados

### 6.3 Campos ADIF 3.1.5 **no soportados** por el modelo original

`ALTITUDE`, `MY_ALTITUDE`, `MY_ARRL_SECT`, `REGION`, `MY_REGION`, `DARC_DOK`, `MY_DARC_DOK`,
`USACA_COUNTIES`, `MY_USACA_COUNTIES`, `SILENT_KEY`, `RIG` (del corresponsal), `PUBLIC_KEY`,
`FISTS`, `FISTS_CC`, `MY_FISTS`, `SKCC`, `TEN_TEN`, `UKSMG`, `CREDIT_GRANTED`, `CREDIT_SUBMITTED`,
`AWARD_GRANTED`, `AWARD_SUBMITTED`, `DCL_*`, `HAMLOGEU_*`, `MORSE_KEY_TYPE`, `MORSE_KEY_INFO` y
`MY_MORSE_KEY_*` (3.1.4), `GRIDSQUARE_EXT` / `MY_GRIDSQUARE_EXT`, y **toda la familia `*_INTL`**
(Log4OM guarda UTF-8 directamente en los campos normales).

### 6.4 Campos propios (`APP_L4ONG_*`)

`APP_L4ONG_QSO_CONFIRMATIONS`, `APP_L4ONG_QSO_AWARD_REFERENCES`, `APP_L4ONG_SATELLITE_QSO`,
`APP_L4ONG_CONTEST`, `APP_L4ONG_SUNSPOTS`, `APP_L4ONG_MUFDAY`, `APP_L4ONG_SIGNALTONOISERATIO`.
Más las columnas internas sin equivalente: `qsoid`, `contactassociations`, `myassociations`, `Reliability`.

### 6.5 Trampas de formato detectadas en el ADIF real

- `LAT`/`LON` en formato ADIF `N027 58.950` / `W015 23.583`, **no** en grados decimales.
- `FREQ` en **MHz** con 6 decimales (`14.187000`); `0.433000` para 70 cm es un valor escrito mal por
  el propio Log4OM en QSOs de VHF/UHF importados.
- Acentos rotos en campos como `MY_CNTY:6>ESPAÑA` → el fichero se escribe en UTF-8 pero sin BOM y con
  longitudes declaradas en **bytes**, no en caracteres. El parser del clon debe contar bytes.
- `QSO_DATE_OFF`/`TIME_OFF` presentes en el 100 % de los QSOs.

---

## 7. Modelo propuesto para Cuaderno NODISLA

### 7.1 Principios

1. **Nombres ADIF para los campos de QSO** (en minúsculas), nombres en español para las entidades
   propias. Motivo: la importación/exportación ADIF es la operación más frecuente y crítica; traducir
   `gridsquare` a `localizador` añade una capa de error por cero beneficio real.
2. **Clave primaria sintética** `id INTEGER PRIMARY KEY` + índice único sobre la clave natural.
   El original usa la clave natural como PK: eso impide registrar dos QSOs con el mismo indicativo,
   banda, modo y segundo exacto, y encarece todos los índices secundarios (que replican 4 columnas).
3. **Nada de JSON en columnas consultables.** Confirmaciones y referencias pasan a tablas hijas. Esto
   convierte "¿cuántos DXCC confirmados por LoTW en 20 m?" en una consulta SQL indexada en vez de un
   recorrido en memoria.
4. **Fechas en TEXT ISO-8601 UTC** (`'2026-05-23 09:56:31'`), siempre UTC, sin zona local guardada.
   Ordenación lexicográfica = ordenación cronológica, y compatible con `datetime()` de SQLite y
   `DATETIME` de MySQL.
5. **Una sola base de datos**, no tres. Los datos externos (clubes, padrón FCC, referencias de diploma)
   van en un fichero aparte **adjuntado con `ATTACH DATABASE`** solo cuando hacen falta — así el
   cuaderno del usuario (el fichero valioso, de pocos MB) se respalda solo, y los 260 MB de datos
   descargables se pueden borrar y volver a bajar.
6. **Perfiles de estación en tabla**, no en JSON de configuración. Log4OM repite los `MY_*` en cada
   QSO; nosotros los repetimos también (ADIF lo exige y protege el histórico si cambias de QTH), pero
   además normalizamos el perfil en `estacion` para poder rellenar y corregir en bloque.

### 7.2 Entidades

```
cuaderno.db  (el fichero del usuario)
├── esquema_info           control de versión de la base
├── estacion               perfiles de estación (mi QTH, equipo, credenciales referenciadas)
├── qso                    el contacto (tabla ancha, campos ADIF)
├── qso_confirmacion       1..N por QSO — QSL/LoTW/eQSL/QRZ/HamQTH/HRDLog/ClubLog
├── qso_referencia         1..N por QSO — DXCC/IOTA/POTA/SOTA/WWFF/WCA/… propias y del DX
├── qso_campo_extra        clave/valor para campos ADIF no modelados y APP_* de terceros
├── qso_adjunto            fotos de QSL, audio del contacto
├── premio_progreso        caché de progreso calculado por diploma+configuración
├── cola_sincronizacion    subidas pendientes a cada servicio
└── qso_fts                índice FTS5 sobre indicativo/nombre/QTH/notas

referencia.db  (descargable, ATTACH bajo demanda)
├── dxcc_entidad           country.xml normalizado
├── dxcc_prefijo           patrones con ventana temporal
├── subdivision            states.csv (estados, provincias, cantones)
├── banda / modo / submodo / propagacion / satelite / contest / antpath   catálogos
├── club / club_miembro    hamclubs.info
├── indicativo_estado      padrón FCC
├── premio / premio_config / premio_referencia / premio_informe   motor de diplomas
└── premio_dxcc_permitido  AllowedDXCC normalizado (lo que el original no hace)
```

### 7.3 DDL propuesto — SQLite (dialecto de referencia)

```sql
PRAGMA journal_mode = WAL;
PRAGMA foreign_keys = ON;

-- ───────────────────────── control de esquema ─────────────────────────
CREATE TABLE esquema_info (
    aplicacion    TEXT    NOT NULL,
    version_app   TEXT    NOT NULL,
    version_db    INTEGER NOT NULL,
    creada_utc    TEXT    NOT NULL,
    migrada_utc   TEXT
);

-- ───────────────────────── perfiles de estación ───────────────────────
CREATE TABLE estacion (
    id                INTEGER PRIMARY KEY,
    nombre_perfil     TEXT    NOT NULL UNIQUE COLLATE NOCASE,
    station_callsign  TEXT    NOT NULL COLLATE NOCASE,
    operator          TEXT    COLLATE NOCASE,
    owner_callsign    TEXT    COLLATE NOCASE,
    my_name           TEXT,
    my_street         TEXT,
    my_city           TEXT,
    my_postal_code    TEXT,
    my_state          TEXT COLLATE NOCASE,
    my_cnty           TEXT COLLATE NOCASE,
    my_country        TEXT COLLATE NOCASE,
    my_dxcc           INTEGER,
    my_cq_zone        INTEGER,
    my_itu_zone       INTEGER,
    my_iota           TEXT COLLATE NOCASE,
    my_gridsquare     TEXT COLLATE NOCASE,
    my_lat            REAL,          -- grados decimales, no formato ADIF
    my_lon            REAL,
    my_altitude       REAL,
    my_rig            TEXT,
    my_antenna        TEXT,
    my_sig            TEXT,
    my_sig_info       TEXT,
    tx_pwr_defecto    REAL,
    predeterminado    INTEGER NOT NULL DEFAULT 0,
    activo            INTEGER NOT NULL DEFAULT 1
);

-- ───────────────────────── el contacto ────────────────────────────────
CREATE TABLE qso (
    id                INTEGER PRIMARY KEY,
    uuid              TEXT    NOT NULL UNIQUE,   -- GUID, estable entre bases y sincronizaciones
    estacion_id       INTEGER REFERENCES estacion(id) ON DELETE SET NULL,

    -- clave natural (compatible Log4OM)
    call              TEXT    NOT NULL COLLATE NOCASE,
    band              TEXT    NOT NULL COLLATE NOCASE,
    mode              TEXT    NOT NULL COLLATE NOCASE,
    qso_inicio_utc    TEXT    NOT NULL,          -- 'YYYY-MM-DD HH:MM:SS'
    qso_fin_utc       TEXT,

    submode           TEXT COLLATE NOCASE,
    band_rx           TEXT COLLATE NOCASE,
    freq              REAL,                      -- MHz
    freq_rx           REAL,
    rst_sent          TEXT,
    rst_rcvd          TEXT,
    tx_pwr            REAL,
    rx_pwr            REAL,

    -- corresponsal
    name              TEXT,
    address           TEXT,
    qth               TEXT,
    email             TEXT,
    web               TEXT,
    gridsquare        TEXT COLLATE NOCASE,
    gridsquare_ext    TEXT COLLATE NOCASE,
    lat               REAL,
    lon               REAL,
    altitude          REAL,
    cont              TEXT COLLATE NOCASE,
    country           TEXT COLLATE NOCASE,
    dxcc              INTEGER NOT NULL DEFAULT 0,
    cqz               INTEGER,
    ituz              INTEGER,
    pfx               TEXT COLLATE NOCASE,
    state             TEXT COLLATE NOCASE,
    cnty              TEXT COLLATE NOCASE,
    region            TEXT COLLATE NOCASE,
    darc_dok          TEXT COLLATE NOCASE,
    age               REAL,
    rig               TEXT,
    silent_key        INTEGER NOT NULL DEFAULT 0,
    eq_call           TEXT COLLATE NOCASE,
    contacted_op      TEXT COLLATE NOCASE,

    -- copia congelada de mi estación en el momento del QSO (ADIF lo exige)
    station_callsign  TEXT NOT NULL COLLATE NOCASE,
    operator          TEXT COLLATE NOCASE,
    owner_callsign    TEXT COLLATE NOCASE,
    my_name           TEXT,
    my_street         TEXT,
    my_city           TEXT,
    my_postal_code    TEXT,
    my_state          TEXT COLLATE NOCASE,
    my_cnty           TEXT COLLATE NOCASE,
    my_country        TEXT COLLATE NOCASE,
    my_dxcc           INTEGER,
    my_cq_zone        INTEGER,
    my_itu_zone       INTEGER,
    my_gridsquare     TEXT COLLATE NOCASE,
    my_lat            REAL,
    my_lon            REAL,
    my_altitude       REAL,
    my_rig            TEXT,
    my_antenna        TEXT,
    my_sig            TEXT,
    my_sig_info       TEXT,
    my_arrl_sect      TEXT COLLATE NOCASE,

    -- propagación y geometría
    prop_mode         TEXT COLLATE NOCASE,
    ant_path          TEXT COLLATE NOCASE,
    ant_az            REAL,
    ant_el            REAL,
    distance          REAL,                      -- km
    sat_qso           INTEGER NOT NULL DEFAULT 0,
    sat_mode          TEXT COLLATE NOCASE,
    sat_name          TEXT COLLATE NOCASE,
    swl               INTEGER NOT NULL DEFAULT 0,

    -- meteor scatter
    max_bursts        REAL,
    nr_bursts         REAL,
    nr_pings          REAL,
    ms_shower         TEXT,

    -- condiciones solares en el momento del QSO
    a_index           REAL,
    k_index           REAL,
    sfi               REAL,
    manchas_solares   INTEGER,
    muf_day           REAL,
    fiabilidad        REAL,
    snr               REAL,

    -- concurso
    contest_id        TEXT COLLATE NOCASE,
    es_contest        INTEGER NOT NULL DEFAULT 0,
    class             TEXT COLLATE NOCASE,
    precedence        TEXT COLLATE NOCASE,
    arrl_check        TEXT COLLATE NOCASE,
    arrl_sect         TEXT COLLATE NOCASE,
    srx               INTEGER,
    srx_string        TEXT,
    stx               INTEGER,
    stx_string        TEXT,

    -- texto y control
    comment           TEXT,
    notes             TEXT,
    sig               TEXT,
    sig_info          TEXT,
    qslmsg            TEXT,
    qsl_via           TEXT,
    qso_complete      TEXT,                      -- Y / N / NIL / ? 
    qso_random        INTEGER NOT NULL DEFAULT 1,
    force_init        INTEGER NOT NULL DEFAULT 0,
    programid         TEXT,
    programversion    TEXT,

    -- auditoría propia
    creado_utc        TEXT NOT NULL,
    modificado_utc    TEXT NOT NULL,
    origen            TEXT,                      -- MANUAL / ADIF / WSJTX / JTDX / LOTW / CLUSTER
    origen_fichero    TEXT
);

-- ───────────────────────── confirmaciones ─────────────────────────────
CREATE TABLE qso_confirmacion (
    qso_id            INTEGER NOT NULL REFERENCES qso(id) ON DELETE CASCADE,
    servicio          TEXT    NOT NULL COLLATE NOCASE,  -- QSL EQSL LOTW QRZCOM HAMQTH HRDLOG CLUBLOG DCL
    enviada           TEXT    NOT NULL DEFAULT 'N',     -- Y N R Q I
    recibida          TEXT    NOT NULL DEFAULT 'N',     -- Y N R I V
    via_envio         TEXT,                             -- B D E M
    via_recepcion     TEXT,
    fecha_envio_utc   TEXT,
    fecha_recep_utc   TEXT,
    estado_subida     TEXT,                             -- Y N M (ADIF *_QSO_UPLOAD_STATUS)
    fecha_subida_utc  TEXT,
    PRIMARY KEY (qso_id, servicio)
);

-- ───────────────────────── referencias de diploma ─────────────────────
CREATE TABLE qso_referencia (
    id                INTEGER PRIMARY KEY,
    qso_id            INTEGER NOT NULL REFERENCES qso(id) ON DELETE CASCADE,
    propia            INTEGER NOT NULL DEFAULT 0,   -- 0 = del DX, 1 = mía (activación)
    award_code        TEXT    NOT NULL COLLATE NOCASE,
    referencia        TEXT    NOT NULL COLLATE NOCASE,
    grupo             TEXT COLLATE NOCASE,
    subgrupo          TEXT COLLATE NOCASE,
    puntuacion        REAL NOT NULL DEFAULT 0,
    enviada_a         TEXT,        -- configuraciones a las que se ha solicitado (lista)
    concedida_en      TEXT         -- configuraciones ya concedidas
);

-- ───────────────────────── extensión abierta ──────────────────────────
CREATE TABLE qso_campo_extra (
    qso_id            INTEGER NOT NULL REFERENCES qso(id) ON DELETE CASCADE,
    campo             TEXT    NOT NULL COLLATE NOCASE,  -- nombre ADIF o APP_*
    valor             TEXT,
    PRIMARY KEY (qso_id, campo)
);

CREATE TABLE qso_adjunto (
    id                INTEGER PRIMARY KEY,
    qso_id            INTEGER NOT NULL REFERENCES qso(id) ON DELETE CASCADE,
    tipo              TEXT NOT NULL,      -- QSL_FRENTE QSL_DORSO AUDIO FOTO OTRO
    ruta              TEXT NOT NULL,
    descripcion       TEXT,
    creado_utc        TEXT NOT NULL
);

-- ───────────────────────── operaciones diferidas ──────────────────────
CREATE TABLE cola_sincronizacion (
    id                INTEGER PRIMARY KEY,
    qso_id            INTEGER NOT NULL REFERENCES qso(id) ON DELETE CASCADE,
    servicio          TEXT    NOT NULL COLLATE NOCASE,
    operacion         TEXT    NOT NULL,   -- SUBIR / BORRAR / DESCARGAR
    estado            TEXT    NOT NULL DEFAULT 'PENDIENTE',
    intentos          INTEGER NOT NULL DEFAULT 0,
    ultimo_error      TEXT,
    creado_utc        TEXT    NOT NULL,
    procesado_utc     TEXT
);

-- caché de progreso: se recalcula, nunca es la verdad
CREATE TABLE premio_progreso (
    award_code        TEXT NOT NULL COLLATE NOCASE,
    config_name       TEXT NOT NULL COLLATE NOCASE,
    referencia        TEXT NOT NULL COLLATE NOCASE,
    band              TEXT COLLATE NOCASE,
    mode              TEXT COLLATE NOCASE,
    trabajado         INTEGER NOT NULL DEFAULT 0,
    confirmado        INTEGER NOT NULL DEFAULT 0,
    qso_id_primero    INTEGER,
    qso_id_confirmado INTEGER,
    calculado_utc     TEXT NOT NULL,
    PRIMARY KEY (award_code, config_name, referencia, band, mode)
);
```

### 7.4 Índices

```sql
-- clave natural, compatible con Log4OM (evita duplicados en importación)
CREATE UNIQUE INDEX ux_qso_natural   ON qso (call, band, mode, qso_inicio_utc);

-- ¿le he trabajado antes?  — la consulta más frecuente de toda la aplicación
CREATE INDEX ix_qso_call             ON qso (call, qso_inicio_utc DESC);
CREATE INDEX ix_qso_call_band_mode   ON qso (call, band, mode, qso_inicio_utc DESC);

-- worked-before por entidad: matriz DXCC × banda × modo
CREATE INDEX ix_qso_dxcc_band_mode   ON qso (dxcc, band, mode);
CREATE INDEX ix_qso_dxcc_fecha       ON qso (dxcc, qso_inicio_utc);

-- estadísticas y listados
CREATE INDEX ix_qso_fecha            ON qso (qso_inicio_utc DESC);
CREATE INDEX ix_qso_band_mode_fecha  ON qso (band, mode, qso_inicio_utc DESC);
CREATE INDEX ix_qso_grid             ON qso (gridsquare)          WHERE gridsquare IS NOT NULL;
CREATE INDEX ix_qso_estado           ON qso (dxcc, state)         WHERE state IS NOT NULL;
CREATE INDEX ix_qso_cq               ON qso (cqz, band, mode)     WHERE cqz IS NOT NULL;
CREATE INDEX ix_qso_itu              ON qso (ituz, band, mode)    WHERE ituz IS NOT NULL;
CREATE INDEX ix_qso_contest          ON qso (contest_id, qso_inicio_utc) WHERE contest_id IS NOT NULL;
CREATE INDEX ix_qso_estacion         ON qso (estacion_id, qso_inicio_utc DESC);
CREATE INDEX ix_qso_pendiente_qsl    ON qso (id) WHERE qso_complete <> 'N';

-- confirmaciones: "todo lo confirmado por LoTW", "QSL enviadas sin respuesta"
CREATE INDEX ix_conf_servicio_rec    ON qso_confirmacion (servicio, recibida, qso_id);
CREATE INDEX ix_conf_servicio_env    ON qso_confirmacion (servicio, enviada, qso_id);
CREATE INDEX ix_conf_subida          ON qso_confirmacion (servicio, estado_subida) WHERE estado_subida = 'N';

-- referencias: "¿tengo ya EA8/GC-001?", listados por diploma
CREATE INDEX ix_ref_award_ref        ON qso_referencia (award_code, referencia, propia);
CREATE INDEX ix_ref_qso              ON qso_referencia (qso_id);
CREATE INDEX ix_ref_propia           ON qso_referencia (propia, award_code, referencia);

-- cola
CREATE INDEX ix_cola_pendiente       ON cola_sincronizacion (servicio, estado) WHERE estado = 'PENDIENTE';

-- búsqueda libre
CREATE VIRTUAL TABLE qso_fts USING fts5 (
    call, name, qth, comment, notes, country,
    content = 'qso', content_rowid = 'id', tokenize = 'unicode61'
);
```

Los índices parciales (`WHERE …`) son SQLite/PostgreSQL. En MySQL se sustituyen por índices completos.

### 7.5 DDL de referencia (base descargable)

```sql
CREATE TABLE dxcc_entidad (
    dxcc            INTEGER PRIMARY KEY,
    nombre          TEXT NOT NULL COLLATE NOCASE,
    nombre_es       TEXT COLLATE NOCASE,       -- traducción propia del clon
    arrl_prefix     TEXT COLLATE NOCASE,
    continente      TEXT COLLATE NOCASE,
    cq_zone         INTEGER,
    itu_zone        INTEGER,
    iaru_region     INTEGER,
    lat             REAL,
    lon             REAL,
    activa          INTEGER NOT NULL DEFAULT 1,
    valido_desde    TEXT,
    valido_hasta    TEXT,
    ranking         INTEGER,                   -- ranking.csv (rareza)
    bandera         TEXT                       -- flags.csv
);

CREATE TABLE dxcc_prefijo (
    id              INTEGER PRIMARY KEY,
    dxcc            INTEGER NOT NULL REFERENCES dxcc_entidad(dxcc),
    patron          TEXT NOT NULL,             -- regex de country.xml
    exacto          INTEGER NOT NULL DEFAULT 0,
    cq_zone         INTEGER,
    itu_zone        INTEGER,
    continente      TEXT,
    gridsquare      TEXT,
    lat             REAL,
    lon             REAL,
    valido_desde    TEXT,
    valido_hasta    TEXT,
    fuente          TEXT NOT NULL              -- COUNTRY_XML / CTY / CLUBLOG
);
CREATE INDEX ix_pfx_dxcc ON dxcc_prefijo (dxcc);
CREATE INDEX ix_pfx_fuente ON dxcc_prefijo (fuente, exacto);

CREATE TABLE subdivision (
    dxcc            INTEGER NOT NULL,
    codigo          TEXT NOT NULL COLLATE NOCASE,
    descripcion     TEXT NOT NULL,
    PRIMARY KEY (dxcc, codigo)
);

CREATE TABLE banda (
    band            TEXT PRIMARY KEY COLLATE NOCASE,
    freq_min_mhz    REAL NOT NULL,
    freq_max_mhz    REAL NOT NULL,
    orden           INTEGER NOT NULL
);

CREATE TABLE modo (
    modo            TEXT PRIMARY KEY COLLATE NOCASE,
    submodo         TEXT COLLATE NOCASE,
    modo_adif2      TEXT COLLATE NOCASE,       -- modelist.csv
    tipo_emision    TEXT NOT NULL              -- CW / PHONE / DIGITAL / IMAGE
);

CREATE TABLE club (
    club_code       TEXT PRIMARY KEY COLLATE NOCASE,
    nombre          TEXT,
    url_lista       TEXT,
    actualizado_utc TEXT
);
CREATE TABLE club_miembro (
    club_code       TEXT NOT NULL COLLATE NOCASE REFERENCES club(club_code) ON DELETE CASCADE,
    call            TEXT NOT NULL COLLATE NOCASE,
    numero_socio    TEXT,
    PRIMARY KEY (club_code, call)
);
CREATE INDEX ix_club_call ON club_miembro (call);   -- el original NO tiene este índice

CREATE TABLE indicativo_estado (
    fuente          TEXT NOT NULL COLLATE NOCASE,  -- FCC
    call            TEXT NOT NULL COLLATE NOCASE,
    estado          TEXT NOT NULL COLLATE NOCASE,
    PRIMARY KEY (fuente, call)
) WITHOUT ROWID;                                    -- ahorra ~30 % en 1,6 M de filas

CREATE TABLE premio (
    award_code      TEXT NOT NULL COLLATE NOCASE,
    valido_desde    TEXT NOT NULL,
    valido_hasta    TEXT NOT NULL,
    nombre          TEXT NOT NULL,
    nombre_es       TEXT,
    tipo            TEXT NOT NULL,                  -- REFERENCE / QSOFIELDS / CALLSIGN
    descripcion     TEXT,
    metodo_confirm  TEXT NOT NULL,                  -- 'LOTW;QSL'
    metodo_valid    TEXT,
    qso_field       TEXT,
    qso_leading     TEXT,
    qso_separador   TEXT,
    qso_exacto      INTEGER NOT NULL DEFAULT 0,
    match_field     TEXT,
    qso_query       TEXT,
    prefijo_ref     TEXT,
    multiples_refs  INTEGER NOT NULL DEFAULT 0,
    sin_referencia  INTEGER NOT NULL DEFAULT 0,
    modos_permit    TEXT,
    bandas_permit   TEXT,
    emision_permit  TEXT,
    url             TEXT,
    url_descarga    TEXT,
    url_referencia  TEXT,
    protegido       INTEGER NOT NULL DEFAULT 0,
    valido          INTEGER NOT NULL DEFAULT 1,
    actualizado_utc TEXT,
    PRIMARY KEY (award_code, valido_desde, valido_hasta)
);

CREATE TABLE premio_config (
    award_code      TEXT NOT NULL COLLATE NOCASE,
    config_name     TEXT NOT NULL COLLATE NOCASE,
    valido_desde    TEXT NOT NULL,
    valido_hasta    TEXT NOT NULL,
    descripcion     TEXT,
    tipo_contacto   TEXT NOT NULL,                  -- Chaser / Activator
    modos           TEXT,
    bandas          TEXT,
    tipo_emision    TEXT,
    continentes     TEXT,
    anual           INTEGER NOT NULL DEFAULT 0,
    requiere_sat    INTEGER NOT NULL DEFAULT 0,
    excluye_sat     INTEGER NOT NULL DEFAULT 0,
    formato_csv     TEXT,
    grant_code      TEXT,
    PRIMARY KEY (award_code, config_name, valido_desde, valido_hasta)
);

CREATE TABLE premio_referencia (
    award_code      TEXT NOT NULL COLLATE NOCASE,
    referencia      TEXT NOT NULL COLLATE NOCASE,
    valido_desde    TEXT NOT NULL,
    valido_hasta    TEXT NOT NULL,
    alias           TEXT COLLATE NOCASE,
    descripcion     TEXT NOT NULL,
    grupo           TEXT COLLATE NOCASE,
    subgrupo        TEXT COLLATE NOCASE,
    gridsquare      TEXT COLLATE NOCASE,
    puntuacion      REAL NOT NULL DEFAULT 0,
    bonus           REAL NOT NULL DEFAULT 0,
    patron_busqueda TEXT,
    valido          INTEGER NOT NULL DEFAULT 1,
    PRIMARY KEY (award_code, referencia, valido_desde, valido_hasta)
);
CREATE INDEX ix_pref_busqueda ON premio_referencia (referencia);
CREATE INDEX ix_pref_grupo    ON premio_referencia (award_code, grupo, subgrupo);
CREATE INDEX ix_pref_grid     ON premio_referencia (gridsquare) WHERE gridsquare <> '';

-- AllowedDXCC normalizado: lo que el original guarda como '#24#;#514#;…'
CREATE TABLE premio_dxcc_permitido (
    award_code      TEXT NOT NULL COLLATE NOCASE,
    referencia      TEXT NOT NULL COLLATE NOCASE,
    dxcc            INTEGER NOT NULL,
    PRIMARY KEY (award_code, referencia, dxcc)
) WITHOUT ROWID;
CREATE INDEX ix_pdxcc_dxcc ON premio_dxcc_permitido (dxcc, award_code);

CREATE TABLE premio_informe (
    report_id       TEXT PRIMARY KEY COLLATE NOCASE,
    titulo          TEXT,
    descripcion     TEXT,
    proveedor       TEXT,
    habilitado      INTEGER NOT NULL DEFAULT 1,
    definicion_xml  TEXT NOT NULL,
    actualizado_utc TEXT
);
```

Catálogos menores (`propagacion`, `satelite`, `contest`, `antpath`, `rst`) siguen el mismo patrón
`codigo` / `descripcion` / `descripcion_es` y se siembran desde los CSV del original.

### 7.6 Consultas críticas que el modelo debe resolver rápido

```sql
-- 1) Worked-before instantáneo al teclear el indicativo (< 1 ms con ix_qso_call)
SELECT band, mode, qso_inicio_utc, name, qth
  FROM qso WHERE call = ?1 ORDER BY qso_inicio_utc DESC LIMIT 50;

-- 2) ¿Nuevo DXCC? ¿Nuevo en esta banda? ¿Nuevo en este modo?
SELECT EXISTS(SELECT 1 FROM qso WHERE dxcc = ?1)                                  AS visto,
       EXISTS(SELECT 1 FROM qso WHERE dxcc = ?1 AND band = ?2)                    AS visto_banda,
       EXISTS(SELECT 1 FROM qso WHERE dxcc = ?1 AND band = ?2 AND mode = ?3)      AS visto_slot;

-- 3) Matriz DXCC × banda con estado de confirmación (para el panel de diplomas)
SELECT q.dxcc, q.band,
       COUNT(*)                                                  AS trabajados,
       SUM(c.recibida = 'Y')                                     AS confirmados
  FROM qso q
  LEFT JOIN qso_confirmacion c ON c.qso_id = q.id AND c.servicio = 'LOTW'
 GROUP BY q.dxcc, q.band;

-- 4) Progreso de un diploma de referencias (POTA/SOTA) — imposible de indexar en el original
SELECT pr.referencia, pr.descripcion,
       MIN(q.qso_inicio_utc) AS primera_vez,
       MAX(c.recibida = 'Y') AS confirmado
  FROM premio_referencia pr
  LEFT JOIN qso_referencia r ON r.award_code = pr.award_code
                            AND r.referencia = pr.referencia AND r.propia = 0
  LEFT JOIN qso q            ON q.id = r.qso_id
  LEFT JOIN qso_confirmacion c ON c.qso_id = q.id AND c.servicio IN ('LOTW','QSL')
 WHERE pr.award_code = 'POTA'
 GROUP BY pr.referencia;

-- 5) Cola de subida a LoTW
SELECT q.* FROM qso q
  JOIN qso_confirmacion c ON c.qso_id = q.id
 WHERE c.servicio = 'LOTW' AND c.estado_subida = 'N'
 ORDER BY q.qso_inicio_utc;
```

---

## 8. SQLite frente a MySQL / MariaDB

### Recomendación: **SQLite como motor principal, MySQL/MariaDB como opción**

Razones a favor de SQLite por defecto:

- El cuaderno de un radioaficionado son decenas de miles de QSOs (el de EA8DLF: **1.838**). Un cuaderno
  de 100.000 QSOs con este esquema ocupa ~120 MB y responde en microsegundos. No hay caso de uso que
  justifique un servidor.
- Cero instalación, cero servicio, cero contraseñas. El original ya crea un usuario MySQL con
  contraseña igual al nombre de usuario: un patrón que no vamos a copiar.
- Copia de seguridad = copiar un fichero. Con `VACUUM INTO` se hace en caliente y sin bloquear.
- `ATTACH DATABASE` permite unir cuaderno y referencia en una sola consulta SQL.
- **WAL** permite que el importador ADIF, el cluster y la interfaz lean a la vez mientras se escribe.

Cuándo hace falta MySQL/MariaDB: varios operadores registrando a la vez sobre el mismo cuaderno
(concurso multi-operador, activación en grupo), o cuaderno compartido entre varios PCs. En ese caso
SQLite no sirve (bloqueo de escritor único, y el fichero en red se corrompe).

### Cómo dar soporte a los dos sin duplicar código

1. Capa de acceso con **interfaz de repositorio** (`IQsoRepository`, `IPremioRepository`, …), una
   implementación por motor. Nada de SQL disperso por la interfaz de usuario.
2. **Dapper** sobre `Microsoft.Data.Sqlite` y `MySqlConnector`. EF Core es tentador, pero el modelo
   tiene una tabla de 100+ columnas y consultas de agregación pesadas para los diplomas; Dapper con SQL
   escrito a mano da control y rendimiento. EF Core solo para las migraciones si se quiere.
3. Aislar las diferencias conocidas:

| Aspecto | SQLite | MySQL / MariaDB |
|---|---|---|
| PK autoincremental | `INTEGER PRIMARY KEY` | `BIGINT AUTO_INCREMENT` |
| Texto | `TEXT` | `VARCHAR(n)` / `TEXT` |
| Insensible a mayúsculas | `COLLATE NOCASE` por columna | `utf8mb4_0900_ai_ci` en la tabla |
| Fecha | `TEXT` ISO UTC | `DATETIME` |
| Booleano | `INTEGER 0/1` | `TINYINT(1)` |
| Índices parciales | sí | no — usar índice completo |
| Búsqueda libre | `FTS5` | índice `FULLTEXT` |
| Upsert | `ON CONFLICT … DO UPDATE` | `ON DUPLICATE KEY UPDATE` |
| Charset | UTF-8 siempre | **`utf8mb4`**, nunca `utf8` (el original usa `utf8`: rompe emojis) |

4. **La verdad del esquema es un juego de scripts de migración numerados** (`001_inicial.sql`,
   `002_…`) con una variante por motor, y `esquema_info.version_db` como marcador. Copiar el patrón
   `Informations`/`DBVersion` del original, que funciona bien.
5. Importación/exportación ADIF idéntica en ambos motores — es la vía de migración entre ellos.

---

## 9. Migración desde Log4OM

Dos caminos, y conviene implementar ambos:

1. **Vía ADIF** (recomendada, y la única que preserva los datos del usuario sin tocar su instalación):
   leer el `.adi` de backup, mapear campos según §6, reconstruir `qso_confirmacion` desde
   `APP_L4ONG_QSO_CONFIRMATIONS` y `qso_referencia` desde `APP_L4ONG_QSO_AWARD_REFERENCES`, y volcar
   cualquier campo desconocido en `qso_campo_extra` para no perder nada. Ojo a las tres trampas de §6.5.
2. **Vía SQLite directa**: `ATTACH` del `Log4OMNG.SQLite` original **en modo solo lectura** e
   `INSERT … SELECT` con `json_each()` para desplegar los tres campos JSON. Más rápido y más fiel, pero
   solo sirve si el usuario usa SQLite (no si tiene el cuaderno en MySQL).

Las bases `Activations.SQLite` y `ExternalData.SQLite` se pueden **importar tal cual** a
`referencia.db` con un `INSERT … SELECT` por tabla — ahorra descargar 260 MB en el primer arranque.
`AllowedDXCC` se explota a `premio_dxcc_permitido` durante esa importación.

---

## 10. Resumen

- **El original tiene 8 tablas** repartidas en 3 bases SQLite (2 + 2 + 4), más un esquema MySQL/MariaDB
  que replica las 2 del cuaderno. Todo lo demás —DXCC, catálogos, definiciones de diploma,
  configuración— vive en ficheros XML, JSON y CSV fuera de la base.
- La tabla `Log` tiene **97 columnas**, clave primaria natural `(band, callsign, mode, qsodate)` y solo
  dos índices. Tres de sus columnas son JSON serializado: confirmaciones, referencias del DX y
  referencias propias. Ahí está el cuello de botella del cálculo de diplomas.
- **La propuesta para Cuaderno NODISLA son 27 tablas**: **9 en el cuaderno del usuario**
  (`esquema_info`, `estacion`, `qso`, `qso_confirmacion`, `qso_referencia`, `qso_campo_extra`,
  `qso_adjunto`, `cola_sincronizacion`, `premio_progreso`, más el índice virtual FTS5) y **18 en la
  base de referencia descargable** (`dxcc_entidad`, `dxcc_prefijo`, `subdivision`, `banda`, `modo`,
  `club`, `club_miembro`, `indicativo_estado`, las cinco del motor de diplomas —`premio`,
  `premio_config`, `premio_referencia`, `premio_dxcc_permitido`, `premio_informe`— y cinco catálogos
  menores: `propagacion`, `satelite`, `contest`, `antpath`, `rst`). Separar ambas bases permite
  respaldar solo lo irreemplazable.
- El cambio de fondo respecto al original: **sacar los tres JSON a tablas hijas indexadas** y
  **normalizar `AllowedDXCC`**. Con eso, las preguntas que de verdad hace un radioaficionado —"¿le he
  trabajado antes?", "¿me falta este parque?", "¿cuántos DXCC llevo confirmados en 20 m?"— se
  responden con una consulta indexada en lugar de recorriendo el cuaderno en memoria.
- **SQLite es la elección correcta** (WAL, `ATTACH`, FTS5, copia = un fichero). MySQL/MariaDB se
  soporta como opción para multi-operador, detrás de una interfaz de repositorio, con scripts de
  migración paralelos y `utf8mb4` — no el `utf8` del original.
