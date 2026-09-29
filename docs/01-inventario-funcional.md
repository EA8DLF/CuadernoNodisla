# Inventario funcional de Log4OM NextGen 2.40.0.0

Documento base para construir un clon en español (proyecto **CuadernoNodisla**).

- **Fuentes analizadas**: `D:\Log4OM NextGen\` (122 archivos) y `C:\Users\<usuario>\AppData\Roaming\Log4OM2\`.
- **Método**: listado completo de archivos, extracción de cadenas ASCII/UTF-16 de todos los DLL/EXE, parseo directo de los metadatos .NET (heap `#Strings` del encabezado CLI) para recuperar nombres de clases, métodos, propiedades y enumeraciones; lectura de los ficheros de configuración, esquemas SQL, bases SQLite y notas de versión (`readme.rtf`).
- **Limitación conocida**: el ensamblado está ofuscado parcialmente. Los nombres **públicos** (clases, propiedades, enums, formularios) son legibles; los **literales de cadena** (heap `#US`) están cifrados/vacíos, por lo que los textos exactos de interfaz y algunos valores de enumeración no se pueden confirmar desde el binario. Se marcan como *a confirmar*.
- Fecha del análisis: 2026-09-21. Versión analizada: **2.40.0.0** (ProgramVersion en `config.json`, .NET Framework 4.8, solo 64 bits desde 2.37.2).

---

## 0. Arquitectura general

### 0.1. Convención de módulos

Los ensamblados propios siguen el prefijo `L4ONG` y un sufijo por capa:

| Capa | Significado | Ejemplos |
|---|---|---|
| `BE` | Business Entities (modelo de datos / DTO / configuración) | `L4ONG.BE.dll` |
| `BL` | Business Logic | `L4ONG.BL.dll`, `L4ONG.BL.CLUSTER.dll`, `L4ONG.BL.CAT.dll`… |
| `DAL` | Data Access Layer | `L4ONG.DAL.dll`, `L4ONG.DAL.SQLITE.dll`, `L4ONG.DAL.MYSQL.dll`… |
| (sin sufijo) | Presentación (WinForms) | `L4ONG.Cluster.dll`, `L4ONG.CAT.dll`, `L4ONG.AW.dll`… |
| `INTERFACES` | Contratos | `L4ONG.INTERFACES.dll` |
| `Framework`, `Common`, `Logger`, `Storage` | Infraestructura transversal | — |

### 0.2. Inventario completo de archivos de `D:\Log4OM NextGen\`

**Ejecutables propios (2)**
- `L4ONG.exe` (29,3 MB) — aplicación principal, con `L4ONG.exe.config` (culture `en-US`, .NET 4.8) y `L4ONG.visualelementsmanifest.xml`.
- `L4ONG.ConfigManager.exe` (1,2 MB) + `.config` — herramienta de configuración independiente.

**DLL propios (38)**

| DLL | Tamaño | Función deducida |
|---|---|---|
| `L4ONG.BE.dll` | 548 KB | Entidades de negocio y todo el árbol de configuración |
| `L4ONG.BL.dll` | 3,4 MB | Lógica central: QSO, ADIF, backup, subidas, estadísticas, mapas, solar, JT, scheduler |
| `L4ONG.BL.AWARDS.dll` | 91 KB | Motor de diplomas, parser de referencias, informes personalizados |
| `L4ONG.BL.CAT.dll` | 91 KB | Motores CAT: Hamlib, OmniRig, TCI |
| `L4ONG.BL.CLUSTER.dll` | 96 KB | Lógica de clúster DX, alertas, supercluster |
| `L4ONG.BL.DATASEARCH.dll` | 86 KB | Búsqueda de indicativos en fuentes externas + membresías de clubes |
| `L4ONG.BL.FLDIGI.dll` | 24 KB | Integración FLDigi por XML-RPC (marcada obsoleta en 2.38, sustituida por UDP) |
| `L4ONG.BL.INIT.dll` | 50 KB | Secuencia de arranque e inicialización |
| `L4ONG.BL.KEYERENGINE.dll` | 57 KB | Motor de manipulador CW (WinKeyer, serie, TCI) |
| `L4ONG.BL.VOACAP.dll` | 47 KB | Puente al motor de propagación |
| `L4ONG.BL.WORKEDBEFORE.dll` | 31 KB | Detección de "trabajado antes" |
| `L4ONG.AW.dll` | 4,1 MB | UI de diplomas, informes, exportación, utilidades Excel |
| `L4ONG.CAT.dll` | 1,3 MB | UI de control de equipo |
| `L4ONG.CHRON.dll` | 13,9 MB | Reloj mundial / grayline / imágenes solares (contiene los mapas base) |
| `L4ONG.Cluster.dll` | 9,2 MB | UI de clúster, band map, reglas de banda, mapa de spots |
| `L4ONG.Common.dll` | 102 KB | Utilidades comunes |
| `L4ONG.Controls.dll` | 13,5 MB | Biblioteca de ~95 controles de usuario y formularios compartidos |
| `L4ONG.CWTRAINER.dll` | 7,6 MB | Entrenador de CW |
| `L4ONG.DAL.dll` | 33 KB | Fachada de acceso a datos, despliegue y actualización de BD |
| `L4ONG.DAL.AWARDS.dll` | 55 KB | BD de diplomas (`Activations.SQLite`) |
| `L4ONG.DAL.DEPLOY.dll` | 25 KB | Creación de esquemas |
| `L4ONG.DAL.EXTDATA.dll` | 24 KB | BD de datos externos (`ExternalData.SQLite`) |
| `L4ONG.DAL.SQLITE.dll` / `.MYSQL.dll` / `.MARIADB.dll` | ~90 KB c/u | Proveedores del log principal |
| `L4ONG.FL.dll` | 2,1 MB | *LazyLog* — entrada masiva de QSO en texto libre |
| `L4ONG.Framework.dll` | 195 KB | Framework base (FwFile, FwNet, FwHam, FwXML, FwWeb, FwCompress, FwMath…) |
| `L4ONG.INTERFACES.dll` | 18 KB | Interfaces |
| `L4ONG.JT.dll` | 3,1 MB | *FTx Monitor*: decodificaciones WSJT-X / JTDX / MSHV |
| `L4ONG.Logger.dll` | 36 KB | Logging (sobre NLog) |
| `L4ONG.OUTPUT.dll` | 330 KB | Impresión: etiquetas QSL, tarjetas QSL, libro de guardia (PDF) |
| `L4ONG.Storage.dll` | 598 KB | Almacén en memoria / caché de aplicación |
| `L4ONG.TALK.dll` | 152 KB | Chat OAMS (retirado en 2.37.2, DLL aún presente) |
| `L4ONG.TCPServer.dll` | 40 KB | Servidor Telnet (supercluster) y conexiones TCP |
| `L4ONG.VOACAP.dll` | 3,1 MB | UI de propagación (gráficas, mapa de fiabilidad) |
| `L4ONG.WEATHER.dll` | 64 KB | METAR/TAF |
| `L4ONG.Webpage.dll` | 29 KB | Generación de página web/CSV + FTP/SFTP |
| `L4ONG.Winkeyer.dll` | 1,1 MB | UI del manipulador y macros |

**Bibliotecas de terceros y su función** (lo que necesitará el clon):

| Biblioteca | Para qué |
|---|---|
| `System.Data.SQLite` + `x64/x86/SQLite.Interop.dll` | Base de datos local del log |
| `MySql.Data`, `MySqlConnector`, `Ubiety.Dns.Core`, `ZstdNet`, `ZstdSharp`, `K4os.*` | Log en MySQL/MariaDB (compresión de protocolo) |
| `EntityFramework`, `EntityFramework.SqlServer`, `System.Data.SQLite.EF6/.Linq` | ORM (uso parcial; el DAL principal usa SQL directo) |
| `Newtonsoft.Json` | Configuración `config.json`, APIs JSON (METAR, NOAA, POTA…) |
| `NLog` | Registro a ficheros `log/LOG4OM2_log_UI_*.log` y `..._UDP_log_*.log` |
| `INIFileParser` | `config.ini`, `configmanager.ini`, `l4ong.status` |
| `GMap.NET.Core` + `GMap.NET.WindowsForms` | Mapas (Google / Bing / OpenStreetMap), caché en `TileDBv5` |
| `NAudio.Core/.Wasapi/.WinMM/.WinForms` | Tono lateral CW, ruido blanco del entrenador, grabación y reproducción de audio de QSO |
| `PdfSharp-gdi` (+ recursos `de/`) | PDF de etiquetas, tarjetas QSL y libro de guardia |
| `itext.kernel/.io/.layout/.commons` + adaptadores BouncyCastle + `bc-fips`, `bcpkix-fips` | PDF avanzado (añadido en 2025, coincide con la impresión de tarjetas QSL de 2.40) |
| `EPPlus`, `EPPlus-LGPL`, `EPPlus.Interfaces`, `EPPlus.System.Drawing`, `Microsoft.IO.RecyclableMemoryStream` | Exportación e "utilidades Excel" |
| `CookComputing.XmlRpcV2` | XML-RPC hacia FLDigi |
| `WebSocket4Net`, `SuperSocket.ClientEngine` | Protocolo TCI (Expert SDR) y chat OAMS |
| `Renci.SshNet` | Subida SFTP de páginas web |
| `BouncyCastle.Crypto` / `BouncyCastle.Cryptography` | Criptografía para SSH/PDF |
| `Microsoft.WindowsAPICodePack(.Shell)` | Integración con shell de Windows (jump lists, diálogos) |
| `Microsoft.Extensions.Caching.Memory/.Logging/.DependencyInjection/.Options` | Caché en memoria e inyección de dependencias |
| `ObjectListView` | Listas avanzadas (lista de usuarios de chat/NET) |
| `Google.Protobuf` | *a confirmar* (probable serialización del motor VOACAP) |
| `Voacap4OM.Engine.Interface.dll`, `voa4om_native.dll` (16 MB), `dvoa.dll` | Motor de propagación VOACAP nativo propio ("VOACAP4OM") |
| `hamlib/` (`libhamlib-4.dll`, `rigctl*.exe`, `rotctl*.exe`, `ampctl*.exe`, `libusb`, `libwinpthread`) | Control CAT/rotor/amplificador vía Hamlib |
| `normal.wav` | Sonido de alerta |
| `eula.rtf`, `readme.rtf`, `unins000.*` | Licencia, notas de versión, desinstalador |

---

## 1. Base de datos del log

**Qué hace.** Almacena el cuaderno de guardia. Tres motores soportados: **SQLite** (por defecto), **MySQL** y **MariaDB** (`DatabaseTypeEnum`, `DbSqlite` / `DbMysql` / `DbMariaDb`). Hay despliegue automático del esquema (`L4ONG.DAL.DEPLOY`, plantillas `mysql.sql` y `mariadb.sql` con marcador `##DATABASENAME##`, crea también el usuario `<db>User` con permisos SELECT/INSERT/UPDATE/DELETE/INDEX) y actualización de versión de BD (`FormUpgradeDatabase`, tabla `Informations(ProgramName, ProgramVersion, DBVersion)`).

**Esquema de la tabla `Log`** (86 columnas; clave primaria compuesta `mode+qsodate+band+callsign`, índice único `qsoid`, índice único `IDX_LOG_KEYS`):

`qsoid, callsign, band, mode, qsodate, address, arrlsect, age, aindex, antaz, antel, antpath, antenna, arrlcheck, bandrx, callsignurl, class, cnty, comment, cont, contactassociations, contactedop, contactreferences (JSON), contestid, country, cqzone, distance, dxcc, eqcall, email, forceinit, freq, freqrx, gridsquare, ituzone, kindex, lat, lon, maxbursts, msshower, myassociations, mydxcc, mylat, mylon, mycity, mycnty, mycountry, mycqzone, mygridsquare, myituzone, myname, mypostalcode, myreferences (JSON), myrig, mysig, mysiginfo, mystate, mystreet, mufday, name, notes, nrbursts, nrpings, operator, ownercallsign, pfx, precedence, programid, programversion, propmode, qslmsg, qslvia, qsocomplete, qsoconfirmations (JSON), qsoenddate, qsorandom, qth, reliability, rstrcvd, rstsent, rxpwr, satelliteqso, satmode, satname, sfi, sig, siginfo, signaltonoiseratio, stationcallsign, srx, srxstring, state, stx, stxstring, swl, sunspots, txpwr`

Puntos clave para el clon:
- Tres campos son **JSON dentro de la fila**: `qsoconfirmations`, `contactreferences`, `myreferences`. Esto evita tablas hijas pero obliga a parsear en memoria para estadísticas de diplomas.
- `forceinit` y `qsorandom` son banderas internas.
- En SQLite las columnas de texto usan `COLLATE NOCASE`.

**Bases auxiliares** (en `%APPDATA%\Log4OM2`):
- `ExternalData.SQLite` (106 MB): `State(Source, Callsign, State)` — 1.601.718 filas (FCC + otras fuentes, para mostrar el estado US en clúster) y `ClubMembers(ClubCode, Callsign, MemberID)` — 142.916 filas.
- `Activations.SQLite` (156 MB): base de diplomas — `Awards` (87 filas), `AwardConfig` (315), `AwardReferences` (553.064), `AwardCustomReports` (5).
- `Log4OMNG.SQLite`: log SQLite vacío de plantilla. `LOG4gom.SQLite` en la carpeta de instalación es la BD de ejemplo/portable.

**Mantenimiento**: menú *Settings → Maintenance → Perform DB maintenance* (compactación/reorganización, solo SQLite) sobre las tres bases.

**Complejidad: alta** (tres motores, migraciones, campos JSON, volumen).

---

## 2. Entrada y edición de QSO

**Qué hace.** Registro en vivo y edición.

**Pantallas / controles**
- Ventana principal con área de QSO (controles `UCStationControl`, `UCText`, `UCTextDelayed`, `UCBand`, `UCMode`, `UCFrequency`, `UCRST`, `UCGridSquare`, `UCState`, `UCCountry`, `UCCountryFlag`, `UCReferences`, `UCAssociations`, `UCBeamDistance`, `UCDistanceControl`, `UCHours`, `UCClock`, `UCLargeClock`).
- `FormQsoDetached` — ventana flotante de entrada rápida de QSO (botón en barra superior).
- `UCSimplifiedUserEditQSO` — editor simplificado.
- `UserEditQSO` / editor completo con adjuntos (imágenes de QSL y otras, `user/attachments`, `AttachmentTypeEnum`).
- `UCQsoGrid`, `UCDataGrid`, `FormWorkedQso`, `FormWorkedBefore` (F10), `UCWkdBefore`.
- `UCMemoryReminder` — memorias rápidas (guarda indicativo + frecuencia CAT actual para recuperarla).
- `UCLazyLog` / `LazyLogEditForm` (módulo `L4ONG.FL`) — entrada masiva tipo texto: cola de QSO, `CTRL-Z` deshace el último, autoguardado cada 25 QSO, botones rápidos ±segundos, área de mensajes clicable para recuperar líneas previas.

**Comportamientos configurables detectados**
- `SearchCallsignOnTab`, `PerformCallsignCheckOnLeave`, `SetStartTimeOnCallsignLeave`, `ClearQsoOnFrequencyChange`, `RetrieveNameFromPrevious` (recupera el nombre del QSO anterior, con botón *swap* frente al dato de la fuente externa), `OverwriteGridSquare`, `CallsignCQFix`, `CallsignRealtimeStats`, `DelayedUpload` (espera 60–90 s antes de subir a servicios externos y escribir el ADIF, para permitir corregir).
- Tecla `PAUSE` bloquea/desbloquea la hora de inicio; `F12` limpia la entrada.
- `UCLockButton` para bloquear campos (banda, modo, azimut/elevación).
- Perfiles de estación múltiples: `StationConfigurations` con radio y antena, antena preferida por radio (`UCStationSelection`).
- Perfil de usuario multiconfiguración (`UserConfigs`, `RunningConfigurationId`, nombre amigable).

**Complejidad: alta** (es el corazón de la aplicación y concentra la mayoría de las reglas de negocio).

---

## 3. ADIF, importación y exportación

**Qué hace.**
- **Importación ADIF** (`ImportAdifAsync`, `ImportAdifFileAsync`, `ImportQsoAsync`): durante la importación comprueba duplicados, rellena el país que falte, valida el DXCC histórico contra el prefijo y corrige zonas ITU/CQ. Opciones para acelerar la importación de backups propios; recupera la banda desde la frecuencia si falta y el modo desde el bandplan.
- **Exportación ADIF** (`ExportAdifForm`) con selección de campos y presets guardables.
- **Exportación CSV** (`ExportCSVForm`) con campos seleccionables y ordenables.
- **Exportación Excel** (`ExportExcelForm`, EPPlus) y botón *export to excel* en todas las rejillas.
- **Exportación KML** (`KMLManagement`, `KmlColors`, `Show3DGrids`) desde las rejillas de QSO.
- Conversión de modos ADIF 2.x ↔ 3.x (`modelist.csv`: `Log4om MODE; 2.x MODE; 3.x MODE; 3.x SUBMODE`; `ModeConversion`, `V2ToInternal`, `V3ToInternal`). Soporte opcional de modos obsoletos (`EnableDeprecatedModeSupport`).
- **Monitor ADIF** (`AdifMonitorManagement`, `AdifMonitorList`): vigila ficheros ADIF de terceros y los importa; opción de importar "tal cual" o enriqueciendo con datos de Log4OM y fuentes externas.
- **Salida ADIF continua** a fichero (`AdifOutputEnabled`, `AdifOutputFile`).
- **Envío ADIF por web** (`AdifWebSendEnabled`, URL, parámetros, GET/POST, codificación, respuesta esperada, múltiples destinos `AdifWebConnectionsList`).

**Complejidad: alta** (el enriquecimiento y la deduplicación son la parte difícil).

---

## 4. Resolución DXCC, país y datos de entidad

**Qué hace.** Determina entidad DXCC, continente, zonas CQ/ITU, región IARU, coordenadas y prefijo a partir del indicativo y la fecha del QSO.

**Fuentes de datos**
- `country.xml` (1,2 MB, 402 entidades): por entidad `ArrlPrefix, Continent, CountryName, CqZone(+lista), Dxcc, ItuZone(+lista), IaruRegion, Latitude, Longitude, Active, CountryTag` y **lista de prefijos con expresión regular y rango de fechas** (`PrefixList` tipo `^A1.*`, `StartDate`, `EndDate`). Se actualiza desde `log4om.com/l4ong/resources/country.xml`.
- `cty_wt_mod.dat` de country-files.com (`ctyfile.json`, 8,9 MB; copias con marca temporal en `temp/`).
- `clublog.xml` (17 MB): excepciones, prefijos y excepciones de zona de ClubLog (`ClublogExceptions`, `ZoneExceptions`, `CTItem`, `CTItemPrefix`).
- `ITU_IARU.csv` — relación zona ITU → región IARU.
- `flags.csv` (341 banderas) y recursos `BIG_XXX` / `SML_XXX` embebidos en el ejecutable (banderas por código ISO y por subdivisión, p. ej. `BIG_AUS_NSW`, `BIG_CAN_ALB`).
- `states.csv` (39 KB) y `arrl_dxcc.csv`.
- `NonStandardCallsign.txt` — indicativos que no cumplen el patrón estándar y deben aceptarse igualmente.
- `ranking.csv` — ranking "most wanted" de ClubLog (`UpdateRanking`, `ClublogRanking`).
- `bandlist.txt` (31 bandas, de 136 kHz a submm) y `bandplan/*.xml` (regiones 1/2/3 + bandplans específicos por DXCC 108, 221, 223, 230, 291) con `SpecialFrequencies` (`1840000|FT8`, `14074000|FT8`, …) usadas para deducir el modo por frecuencia con tolerancia ±500 Hz.
- `rst.xml` — grupos de RST por tipo de modo (PHONE, CW, DIGITAL…) con valor por defecto y lista de valores válidos.
- `propagation.csv` (20 modos de propagación ADIF), `antpath.csv` (Short/Long/Grayline/Other), `satellites.csv` (56 satélites), `satmodelist.txt` (21 modos de satélite), `contest.csv` (~280 concursos ADIF).

**Complejidad: alta** (la resolución histórica por fecha y las excepciones son la fuente principal de errores).

---

## 5. Búsqueda de indicativos (fuentes externas)

**Qué hace.** Al teclear un indicativo consulta fuentes externas y rellena nombre, QTH, dirección, locator, e-mail, imagen de perfil, manager, zonas, etc. (`LookupManagement`, `DataEnrichManagement.EnrichQsoDataAsync`, `CallExternalSources`).

**Fuentes y endpoints (de `config.ini`)**

| Fuente | Endpoint |
|---|---|
| QRZ.com | `https://xmldata.qrz.com/xml/1.34/` (búsqueda), `https://logbook.qrz.com/api` (subida/descarga), referencia `https://www.qrz.com/lookup/` |
| HamQTH | `https://www.hamqth.com/xml.php`, subida `qso_realtime.php` |
| QRZCQ | `https://ssl.qrzcq.com/xml`, referencia `https://www.qrzcq.com/call/` |
| HamCall online | `https://hamcall.net/call?username=…&password=…&rawlookup=1&callsign=…&program=…` |
| HamCall DVD | lectura local (`HAMCALLDvdPath`, `HamCallDVDManagement`) |
| ClubLog (base de país) | `https://cdn.clublog.org/cty.php?api=`, namespace `https://clublog.org/cty/v1.3` |
| CTY (country-files) | `https://www.country-files.com/cty/cty_wt_mod.dat` |
| FCC (estados US) | `ftp://wirelessftp.fcc.gov/pub/uls/complete/l_amat.zip` → `EN.dat` (219 MB descomprimido) |
| LoTW (lista de usuarios) | `https://lotw.arrl.org/lotw-user-activity.csv` |
| PSK Reporter | `https://retrieve.pskreporter.info/query?senderCallsign=<CALLSIGN>&rronly=1` |

Configuración: `PrimaryExternalSource`, `SecondaryExternalSource`, `PreferredLookupSource`, `UseClublogDatabase`, `UseCTYDatabase`, `DownloadProfileImage`, prioridad fija por el sistema desde 2.x. Caché de resultados (`CacheSourceEnum`, `ImageCache`). Clic sobre la foto abre `FormShowImage` a tamaño completo.

**Membresías de clubes** (`ClubMembershipManagement`, tabla `ClubMembers`): listas descargadas por club definidas en `defaultclubconfigurations.xml` (SKCC, FISTS, 070, 1010, NAQCC, FPQRP, HSC, FH…; formato: URL, separador, posición del indicativo y del ID, saltar primera fila) más listas externas propias del usuario. `clublist.txt` añade FISTS_CC, TENTEN, USACA_COUNTIES, VUCC_GRIDS, DMC, EPC, GRA, MDXC. Los clubes se activan/desactivan pulsando su etiqueta.

**Complejidad: media-alta** (muchos protocolos distintos: XML, CSV, FTP, ZIP).

---

## 6. QSL y confirmaciones

**Qué hace.** Gestiona el estado de confirmación por cada método, la subida/descarga a servicios y la impresión.

**Tipos de confirmación** (`ConfirmationTypeEnum`, 8): `QSL` (papel), `EQSL`, `LOTW`, `QRZCOM`, `HAMQTH`, `HRDLOG`, `CLUBLOG`, `CUSTOM`. Cada una guarda estado **enviado** (`S`) y **recibido** (`R`) más la **vía** de envío y recepción (`SV`/`RV`: *Electronic*, …) con valores por defecto configurables (`DefaultConfirmations`).

**Estados y enums**: `SentStatusEnum`, `ReceivedStatusEnum` (incluye `INVALID`), `QslRouteEnum`, `QslViaEnum`, `QslMessageEnum`, `DownloadConfirmationTypeEnum`.

**Servicios**

| Servicio | Subida | Descarga |
|---|---|---|
| **LoTW** | vía ejecutable TQSL local (`LOTWExecutablePath`, `LOTWStationId`, `LOTWTQSLPassword`, modo interactivo o desatendido, argumentos `-f update`, `-c myCall`) | `https://LoTW.arrl.org/lotwuser/lotwreport.adi`, QSL de papel en `qslcards.php`; soporta varias cuentas DXCC, rango de fechas, descarga separada de confirmaciones digitales y de papel, y opción de **añadir QSO recibidos de LoTW que no están en el log** |
| **eQSL** | `https://www.eqsl.cc/qslcard/importADIF.cfm` | `DownloadInBox.cfm`, imagen de la tarjeta vía `GeteQSL.cfm` (visible en el editor de QSO); `EQSLQTHNickName`, `EQSLUpdateLocator` |
| **ClubLog** | `realtime.php` (QSO), `putlogs.php` (log completo), `delete.php` | `mostwanted.php?api=1` |
| **QRZ.com Logbook** | `https://logbook.qrz.com/api` (API key, `QRZCOMForcedCallsign` para forzar el indicativo de estación) | confirmaciones QRZ.com (exportación ADIF pendiente de la comisión ADIF) |
| **HamQTH** | `qso_realtime.php` | — |
| **HRDLog** | `robot.hrdlog.net/NewEntry.aspx`, presencia *on air* `OnAir.aspx`, mapa `map.aspx`, clúster `xml.hrdlog.net` | — |
| **Cloudlog** | URL propia del usuario + API key + Station ID | — |

Todas admiten **subida automática** (`*AutoUpload`) y se registran también como tareas del *scheduler*.

**Pantallas**: `UCConfirmation`, `UCDefaultConfirmation`, `UCUpdateConfirmation`, `UCSearchConfirmation`, `UCQsoConfGrid`, `FormConfirmationImport`, gestor de QSL (`ManageConfirmation` en layouts) con búsqueda mixta AND/OR sobre LoTW/eQSL/papel y selección múltiple de estados, y botón *Select required*.

**Complejidad: alta**.

---

## 7. Impresión: etiquetas QSL, tarjetas QSL y libro de guardia

Módulo `L4ONG.OUTPUT` (PdfSharp + iText).

- **Etiquetas** (`LabelPrintManagement`, `LabelPrintForm`, `LabelDefinitionForm`, `UCLabelDesigner`, `UCQSOLabelStatistic`, `UCSelectionCheckbox`).
  - Plantillas en `labels/*.label.xml`: `LabelName`, `LabelDescription`, tamaño de página, márgenes (incl. `PrinterTopMargin`), `Columns`, `Rows`, factores de corrección vertical/horizontal (proporcional e invertida), `QsoPerLabel`, `SkipLastRow`.
  - Campos posicionables (`LabelField`): `FieldId`, `FieldType`, `CustomText`, `FieldLabel`, `QsoSlot`, `XMm/YMm/WidthMm/HeightMm`, `FontName`, `FontSize`, `Bold`, `Italic`, `ForeColor`, `Alignment`, `DateFormat`, `TimeFormat`.
  - `FieldType` observados: `Callsign, Manager, AddressTo, AddressName, AddressLines, AddressCountry, QslCountry, Name, Band, Mode, RstSent, QsoDate, DateTime, Comment, MyReferences, PseQslTnx, CustomText`.
  - Plantillas incluidas: Avery 8160, Avery L7160, Avery Europe 100 70x37, Brother QL570, QL750.
  - Página de *routing*: aprobación automática de MANAGER / MANAGER BUREAU, marcado de "no enviar" con estado destino configurable, impresión de etiqueta "RETURN TO" y "MY ADDRESS" para SASE, opción de omitir si la contraparte pide SASE o contribución, precarga en segundo plano de fuentes externas, botón *swap* entre dato guardado y dato externo, impresión de "VIA <manager>".
- **Tarjetas QSL** (novedad 2.40): `QslCardPrintManagement`, `QslCardDefinitionForm`, `UCQslCardDesigner`, plantillas `labels/*.qslcard.xml` con `CardWidthMm/CardHeightMm`, márgenes, `BackgroundImageData` (PNG en base64), `MaxQsoPerCard`, `HasFrontImage`/`FrontImageData` y campos posicionables análogos.
- **Libro de guardia** (`FormPrintLogbook`, `PrintLogbookAsync`) a PDF.
- Salidas en `temp/Labels_*.pdf` y `temp/QslCard_*.pdf`.

**Complejidad: media-alta** (maquetación milimétrica y calibración de impresora).

---

## 8. Diplomas (awards) y seguimiento

Módulos `L4ONG.BL.AWARDS`, `L4ONG.DAL.AWARDS`, `L4ONG.AW`.

**Modelo de datos** (`Activations.SQLite`):
- `Awards`: `AwardCode, ValidFrom, ValidTo, AwardName, Valid, Description, AwardType, AwardURL, DownloadURL, ReferenceURL, ConfirmationMethod, ValidationMethod, QsoField, QsoLeadingField, QsoFieldSeparator, QsoFieldExact, MatchField, QsoQuery, LeadingString, TrailingString, AllowedDxcc, AllowedEmissionType, AllowedModes, AllowedBands, AwardFilters, Protected, LastUpdate, Note, AwardAlias, GrantCode, ReferencePrefixes, AllowMultipleRefs, ReferenceLess, AdifGrantedOutput, LastUpdateUser`.
- `AwardConfig`: variantes por periodo (`ContactType`, `Modes`, `Bands`, `EmissionType`, `Continents`, `YearlyAward`, `RequireSatellite`, `ExcludeSatellite`, `CSVFormat`, `AllowedGroups/SubGroups`, `GrantCode`, `AliasGrantCode`).
- `AwardReferences`: `AwardCode, ReferenceCode, ReferenceCodeAlias, ValidFrom, ValidTo, ReferenceDescription, AllowedDXCC, ReferenceGroup, ReferenceSubGroup, GridSquare, ReferenceScore, ReferenceBonusScore, Valid, SearchPattern`.
- `AwardCustomReports`: `ReportId, Title, Description, ProviderId, Enabled, DefinitionXml, LastUpdate`.

**Tres tipos de diploma** (`AwardTypeEnum`): `CALLSIGN` (8 en la instalación), `QSOFIELDS` (22) y `REFERENCE` (57). Total **87 diplomas** predefinidos, entre ellos DXCC, WAS, WAZ, WAC, WPX, WITUZ, VUCC, FFMA, IOTA, SOTA, POTA, WWFF, WCA, WWBOTA, RDA, DLD, WAE, WAI, IPA, USI, etc.

**Funciones**
- `AwardParser.ParseQsoAsync/ParseLogAsync/ParseCallsignAsync/ParseStringAsync` — extrae referencias del texto del QSO, de los comentarios y **de los spots del clúster**.
- `AnalyzeConfirmations`, `AnalyzeGrantedCredits`, `MarkWorked/MarkGranted/MarkSubmitted/MarkUnchecked`, `RescanReferencesAsync`.
- Estadísticas por banda/modo/tipo de emisión (`AwardStatistics`, `BandModeAwardStatusList`), con columna de **gran total** (útil para DXCC Challenge) y filtro ALL / WORKED / NOT WORKED / WORKED NOT VALIDATED / GRANTED.
- Actualización online de listas de referencias desde `https://www.log4om.com/l4ong/resources/awards/` (`awards.txt`) y de informes desde `.../reports/` (`reports.txt`); subida de definiciones propias (`upload_file.php`).
- Descargas directas de gestores: IOTA (`iota-world.org` full list JSON + API), SOTA (`sotadata.org.uk/summitslist.csv` + `SotaAssociations.csv`), POTA (`pota.app/all_parks.csv`), WWFF (`wwff.co/wwff-data/wwff_directory.csv`), SILOS, WCA, ECA, DARC SDOK, etc. — el catálogo completo de URLs está en la tabla `Awards` (más de 120 URL).
- `awardOverride.csv`: corrige el DXCC de referencias concretas al importar (admite comodines, p. ej. `POTA;MY-*;299,046`).
- Copias de seguridad de los diplomas del usuario en `awards/*_IMPORT_CONFIRMATIONS.json` y restauración tras actualizar versión (`BackupUserAwards`, `RestoreUserAwards`).
- **Informes personalizados** (`CustomReportEngine`, `DeclarativeReportRunner`, `ICustomReportRunner`, `ReportViewDefinition`, `MetricDefinition`, `KpiBlock`, `TableBlock`, `TimeWindowDefinition`): definición en XML, salida a Excel con estructura definida y exportación de los QSO subyacentes. Informes incluidos: `DXCC_CHALLENGE`, `DX_MARATHON_SCORE`, `DX_MARATHON_CHALLENGE`, `TENTEN`, `WAI`.
- Exportaciones específicas: ADIF con `SIG/MY_SIG/SIGINFO/MY_SIGINFO` para WWFF y POTA, TSV/CSV de SOTA (Chaser/S2S/SWL).
- **Herramienta de migración de referencias** (`FormAwardRefMigration`), masiva o individual, para referencias propias y de la contraparte.
- **Utilidades Excel** (`FormExcelUtilities`) dentro del gestor de diplomas.
- Lista de diplomas ignorados por el usuario (`ExcludedAwardsList`), que además excluye del análisis del clúster.

**Pantallas**: `FormAwardManager`, `UCAward`, `UCAwardReport`, `UCReferenceList`, `UCSearchReferences`, `FormAwardsExport`, `FormCustomReportManager`, `FormCustomReports`, `UCDxccStatistic`.

**Complejidad: muy alta** (es el módulo más grande después del principal: 4,1 MB de UI y 553k referencias).

---

## 9. Clúster DX

Módulos `L4ONG.BL.CLUSTER`, `L4ONG.Cluster`, `L4ONG.TCPServer`.

**Qué hace.** Conecta por Telnet a servidores DX cluster, analiza los spots, los enriquece y los presenta con colores según el estado de trabajado/confirmado.

- **Lista de servidores**: `cluster.xml` (298 KB, cientos de nodos) con `ClusterName, HostAddress, Port, IsPrimary, KeepAlive, Commands[], Callsign, Password, Ssid, ClusterStatus`; lista de usuario en `user/cluster_user.xml`; actualización desde `http://www.hamcluster.net/DXCluster.ashx`.
- **Script de login** por nodo (`clusterdefaultscript.txt`: `<CALLSIGN>`, `<PASSWORD>`, `SH/DX 30`), comandos sugeridos en `clustercommands.txt` (SHOW/DX, SET/FILTER NEEDS/CW…), palabras conocidas para el resaltado de comentarios en `clusterknownwords.txt`.
- **Conexiones múltiples simultáneas** y **varias ventanas de clúster independientes**, cada una con su propia configuración y layout (`layout_cluster_*`, `FormClusterManagement`, `UCCluster`).
- **Enriquecimiento del spot**: país por prefijo con corrección ClubLog/CTY, estado US desde la base FCC, locator (incluido el que aparece tras `>` en el comentario), distancia y azimut, referencias de diploma detectadas en el texto, cálculo VOACAP por modo, estado LoTW del spotted.
- **Colores configurables** para 12 combinaciones: país no trabajado / no trabajado en banda+modo / trabajado; estación no trabajada / trabajada / trabajada en banda (Sb) / en modo (Sm) / en banda+modo (Sbm) / en estado (St) / en banda+estado (Sbt); resaltado.
- **Filtros**: banda, modo, tipo de emisión, FT4/FT8, "solo LoTW", trabajado, dirección de la antena, continente/DXCC del spotter, filtrar spots cercanos, filtro por indicativo que ignora el resto de filtros, botón de bloqueo que congela la vista.
- **Anti-flood / throttling**: `UseClusterThrottling`, `ClusterFloodThreshold`, `ClusterBatch` (nº de spots antes de refrescar la UI), `ClusterMaxAge`, `ClusterMaxCount`, `DisableMapUpdateOnThrottling`, retardo de activación de 130 s tras el arranque, reconexión automática y desconexión automática si el nodo no responde.
- **Band map / reglas** (`UCClusterRuler`, `ClusterRulerForm`, `UCBandsGrid`, `FormBandGrid`, `BandMapConfigurations`) con `ClusterRulerMaxAge/MaxCount` y escala configurable.
- **Mapa de spots** (`UCMapImage`, `UCMapControl`) con pan y zoom (2.40).
- **Alertas** (`ClusterAlertEngine`, `UCAlertConfiguration`, `UCAlertQueue`): reglas por indicativo/país/banda/modo/referencia, condición "no trabajado", "no trabajado este año", "STRICT EQUAL", omitir estaciones ya trabajadas, umbral de notificación, retrigger configurable en minutos, sonido (`ClusterAlertSound`, `normal.wav`) y **aviso por correo** (SMTP propio con SSL/TLS y autenticación) con horas de silencio configurables por franja horaria local (`NotificationHours`, 0–23).
- **Supercluster / servidor Telnet propio** (`SuperClusterManagement`, `TelnetServer`, `ClusterServerPort`, `ClusterServerAutoStart`): Log4OM reexpone los spots agregados a otros clientes de la red.
- Soporte de *skimmers* (SDC Skimmer) y de CW Skimmer (detección y lookup de indicativo/frecuencia al hacer clic, con "CWSkimmer commands" activado).
- Clic en un spot sintoniza la radio por CAT (`TuneToSpot`) y puede aplicar el offset del transverter.
- **Envío de spots** (`FormSpot`, `SendSpot`, `UCSpot`) con recuperación de datos de QSO guardados en la sesión.
- Estadísticas de concurso superpuestas al clúster (`ContestCountryStatistics`).

**Complejidad: muy alta**.

---

## 10. Control CAT de equipos

Módulos `L4ONG.BL.CAT`, `L4ONG.CAT`.

**Motores soportados** (`CATSoftwareEnum`, tres configuraciones por defecto en `config.json`):
1. **Hamlib** (`HamlibEngine`): lanza/adjunta `rigctld` de la carpeta `hamlib/`, puerto TCP 4532 por defecto; parámetros de puerto serie (COM, baudios, bits de parada, RTS/DTR, handshake, CI-V, modo PTT), polling mínimo 500 ms, **modo VFO** (doble VFO), equipos en red (FLEX y similares), Hamlib 4.2+. `hamlib_riglist.txt` (31 KB) lista los modelos.
2. **OmniRig** (`OmnirigEngine`, COM: `IOmniRigX`, `IRigX`, `RigParamX`, `Omnirig2Instance`) — parámetros `PM_FREQ, PM_FREQA, PM_FREQB, PM_VFOA/B/AA/AB/BA/BB, PM_VFOSWAP, PM_VFOEQUAL, PM_SPLITON/OFF, PM_RITON/OFF, PM_RITOFFSET, PM_XITON/OFF, PM_CW_U/L, PM_SSB_U/L, PM_DIG_U/L, PM_PITCH`.
3. **TCI** (`TCIEngine`, WebSocket, Expert SDR) — protocolo 1.7, botón TX, spots en pantalla del SDR, seguimiento del RX activo, `clicked_on_spot`, flujos de audio (`TCIAudioStream`, `IQ_STREAM`, `RX_AUDIO_STREAM`, `TX_AUDIO_STREAM`, `LINEOUT_STREAM`).

**Funciones**: lectura y escritura de frecuencia/modo por VFO, split, PTT (tecla configurable, por defecto `Oem5` = `\`; modo *toggle* opcional), puerto COM independiente para PTT, inversión de banda lateral por tipo de modo (CW/PHONE/DIGITAL), inversión del orden frecuencia/modo en el mensaje al equipo, retardo configurable, **offsets de transverter por VFO** (`offsetValuesVFOA.txt` / `VFOB.txt`, `UcOffsetControl`, aplicables también a los spots), modo digital por defecto, selección de radio activa, colores de dígitos enteros/decimales, estados `ST_DISABLED / ST_NOTCONFIGURED / ST_NOTRESPONDING / ST_PORTBUSY`.

**Pantallas**: `CATSettingsForm`, `ExternalCATForm`, `HamlibForm`, `UCCat`, `UCHamlib`, `UCFrequency`, `UcSevenSegments`, `ucSmallCAT`.

**Rotor y amplificador** (`RotatorManagement`, `RotatorTypeEnum`): envío de azimut y opcionalmente elevación, integración **PstRotator** (envío de indicativo y de frecuencia, SteppIR con radio seleccionable, frecuencia enviada cuando cambia más de 10 kHz), `ParkRotator`, `StopRotator`. Hamlib aporta `rotctl(d)` y `ampctl(d)`.

**Complejidad: alta**.

---

## 11. Integración por red (UDP/TCP) con WSJT-X, JTDX, MSHV, N1MM, QARTest, FLDigi

Módulo `L4ONG.BL` (`UDPInboundService`, `UDPOutboundService`, `UDPProxyService`) + `L4ONG.JT`.

**Tipos de conexión** (`NetworkConnectionTypeEnum`): `UDP_INBOUND`, `UDP_OUTBOUND`, `UDP_PROXY`, `TCP_INBOUND`, `TCP_OUTBOUND`. Detección y soporte **multicast automático** según el rango de IP (p. ej. 224.0.0.1).

**Mensajes de entrada** (`NetworkInboundMessageTypeEnum`): `ADIF_MESSAGE`, `JT_MESSAGE`, `N1MM_CONTACT`, `QARTEST_MESSAGE`, `MESSAGE_LISTENER`, `DB_UPDATED`, `SAVE_NEW_QSO`.
**Mensajes de salida** (`NetworkOutboundMessageTypeEnum`): `ADIF_MESSAGE`, `JT_MESSAGE`, `JT_COMMAND`, `N1MM_CONTACT`, `N1MM_MESSAGE`, `N1MM_QSO`, `QARTEST_MESSAGE`, `DB_UPDATED`, `UDP_Generic`, `CAT_Generic`.

**Proxy UDP**: reenvía el tráfico entrante a otras aplicaciones (entrada, salida o ambas) para compartir el flujo de WSJT-X con JTAlert/GridTracker.

**FTx Monitor** (`L4ONG.JT`, novedad 2.39/2.40): decodifica el protocolo binario de WSJT-X/JTDX/MSHV (`DecodeQUINT32/QUINT64/QDOUBLE/QUTF8/QTime/QUBoolean`, `JTDecodeParser`, `JTDecodeCycleManager`, `JTInstanceRegistry` para varias instancias simultáneas). Presenta las decodificaciones como tarjetas (`UCCallsignCard`, `UCJtFlowPanel`, `UCDecodeDetail`, `UCJTListbox`, `UCGlobeControl`, `UCJtStatistics`, `UCSourceColorPanel`) coloreadas por estado (nuevo DXCC, nueva banda, nuevo tipo de emisión, país trabajado/confirmado) y por fuente. Envía respuestas y comandos (`SendReplyAsync`, `SendEnableTxAsync`, `SendHaltTxAsync`). Soporta mensajes **FOX y SUPERFOX** (experimental) y muestra la información de **PSK Reporter** sobre la propia estación. Lee los mensajes según llegan, sin esperar al fin de ciclo.

**FLDigi**: histórico por XML-RPC (`IFldigiManagement`: `GetFrequency`, `SetFrequency`, `GetMode`, `SetMode`, `SetCall`, `SetName`, `GetTrxStatus`, `GetTxFreq`, `FldigiTx`); desde 2.38 la vía recomendada es UDP.

**Control remoto propio** (`RemoteControlManagement`, `RemoteControlMessageEnum`, `RemoteControlRequest`, `RCAnswer`): servicio en el puerto **2241** (entrada) y **2242** (salida de datos), dirección configurable, modo broadcast, salida de datos temporizada. Interfaz reescrita por completo para gobernar Log4OM desde aplicaciones externas; existe soporte específico para **RF-Kit**. *El listado exacto de comandos no es recuperable del binario (cadenas cifradas): a confirmar con el manual oficial.*

**Complejidad: alta**.

---

## 12. Mapas y visualización geográfica

- Servicios: **Google Maps, Bing Maps, OpenStreetMap** (`MapServiceEnum`, `MapSelection`, `MapImage`), caché de teselas en `TileDBv5`, comando `/RESETMAPCACHE`.
- `FormMap`, `UCMapControl`, `UCMapImage`, `UCContactMap`, `UCGlobeControl`, `UCGraphics`.
- Pan y zoom (2.40), arrastre con botón izquierdo.
- Superposiciones: sol y luna (`DrawSunOverlay`, `DrawMoonOverlay`, `SetSubSolarPointPosition`), grayline (`FWGrayLineManagement`), ruta de círculo máximo (`DrawGreatCircleRoute`), mapa de calor (`DrawHeatMapAsync`, `HeatPoint`), estrella de QSO (`GenerateQsoStar`), mapa de locators (`GenerateQsoLocatorMap`).
- **Mapa de contactos** en todas las rejillas de QSO: cuadrículas de 4 y 6 dígitos, doble clic lista los QSO de esa cuadrícula; colores configurables (`KmlColors`: trabajado / confirmado / no confirmado, texto, cuadrícula y borde) y `Show3DGrids`.
- Exportación **KML** para Google Earth.

**Complejidad: media-alta**.

---

## 13. Propagación (VOACAP4OM) y datos solares

Módulos `L4ONG.BL.VOACAP`, `L4ONG.VOACAP` + motor nativo propio (`Voacap4OM.Engine.Interface.dll`, `voa4om_native.dll` 16 MB, `dvoa.dll`).

**Parámetros configurables**: SNR requerido (`VoacapSNR`), ancho de banda en Hz (`VoacapBandWidthHz`, por modo), figura de ruido del receptor en dB, **entorno de ruido** (`NoiseEnvironmentEnum`: `QuietRural`, `Residential`, `Downtown`, …), antena de TX y de RX de una lista de 21 modelos (`VoacapAntennas.txt`: isotrópica, omni 6 dB, dipolo horizontal, V invertida, Windom OCF, NVIS ancho/estrecho, Yagi 3/5 el, cuadro 2/3 el, loops, Ultrabeam 6-20 m, Diamond CP6S, Butternut HF6V/HF9V, HyGain AV620, delta loop 7B), pérdidas de sistema TX/RX en dB, salida `.voa`, cálculo detallado por spot del clúster, activación global.

**Salidas** (`Prediction`): `Reliability_pct`, `MedianSnr_dB`, `MaxSNR90`, `Snr10_dB_Calc`, `Snr90_dB_Calc`, `SnrSigma_dB`, `Signal_dBm_Calc`, `Noise_dBm_Calc`, `MedianTakeoff_Deg`, `MedianArrival_Deg`, `Muf_MHz_Median`, `Fot_MHz_Median`, `Hpf_MHz_Median`, `DominantMode`, `DominantHopCount`, `MedianTxGain_dB`, `MedianRxGain_dB`, `MaxAvailability`; cálculos **por modo** (`VoacapHourlyCW`, `VoacapHourlyPhone`, `VoacapHourlyDigital`, `VoacapHourlyFTxx`), evaluación de índices Kp/Ap y de trayectos polar y ecuatorial, "insights" (`MultiInsightsUi`, `MultiInsightsRisksUi`, `MultiInsightsSpaceWeatherUi`, `MultiInsightsBestUi`).

**Vistas**: `UCVoacap`, `UCVoacapMap`, `UCVoacapImage`, `UCVoacapRepeater`, `FormVoacapMapParams` (permite sobreescribir parámetros solo para el mapa), gráficas con `System.Windows.Forms.DataVisualization`, mapa de fiabilidad global (`CalculatePropagationMapAsync`, `FWGeoProbabilityMap`, interpolación `CubicKernel`).

**Datos solares y geomagnéticos** (`SolarDataManagement`, ficheros `DGD.txt`, `DSD.txt`, `predicted-solar-cycle.json`):
- `https://services.swpc.noaa.gov/text/daily-geomagnetic-indices.txt` (DGD)
- `https://services.swpc.noaa.gov/text/daily-solar-indices.txt` (DSD)
- `https://services.swpc.noaa.gov/json/solar-cycle/predicted-solar-cycle.json`
- Imágenes solares: SDO `latest_512_0193.jpg` y `latest_512_HMIIC.jpg`, aurora ovation norte y sur de NOAA.
- Entidades: `SolarInfo`, `GeomagneticData(Detail/Series)`, `DailySolarData`, `PredictedSunspot`, con SFI, SSN, A/K/Ap/Kp (planetario, latitud media y alta), flujo de rayos X y clases de fulguración (C/M/X/S), fulguraciones ópticas.
- Controles: `UCSolarFlux`, `UCSolarFluxK`, `UCSunspots`, `UCSunspotPredict`, `UCSolarImage`.

**Complejidad: muy alta** (motor nativo propio; el clon necesitaría VOACAP/ITURHFPROP externo).

---

## 14. Reloj mundial, grayline y salida/puesta de sol

Módulo `L4ONG.CHRON` (13,9 MB, incluye las imágenes base del mundo).

- `FormGeoClock`, `UCChron`, `UCMainChron`, `UCClock`, `UCLargeClock` (reloj UTC/local reubicable, opción "siempre encima", colores UTC/local para tema claro y oscuro).
- `TheWorldImageWithSun`, `MakeSunImage`, `MakeSunShader`, `MergeImage` — mapa mundial con terminador.
- `CalcSunRiseUTC/Loc`, `CalcSunSetUTC/Loc`, `TwilightType` — desde 2.38 usa el ángulo **civil (96°)** en lugar del astronómico (108°), parametrizable. Muestra orto y ocaso del operador en la barra superior y del QSO seleccionado bajo el mapa de grayline.
- Clic en el mapa fija posición (`MapClickEvent`).

**Complejidad: media**.

---

## 15. Meteorología (METAR/TAF)

Módulo `L4ONG.WEATHER`: decodificador METAR/TAF completo escrito a mano (`MetarDecoder`: viento de superficie con variaciones, visibilidad y visibilidad mínima con dirección, RVR por pista, fenómenos presentes y recientes, obscurecimientos, precipitaciones, capas de nubes con tipo y altura de base, CAVOK, temperatura y punto de rocío, presión en hPa/pulgadas de mercurio, tendencia, categoría de vuelo, cizalladura por pista, unidades múltiples: nudos, km/h, m/s, metros, pies, millas terrestres).

- Proveedor: `aviationweather.gov` API JSON — `stationinfo` por bbox (aeropuerto más cercano) o por ICAO, `metar` y `taf` por estación; fallback a parseo de la cadena METAR.
- El usuario puede fijar un ICAO concreto (2.40) en vez de usar el más cercano.
- Actualización horaria vía scheduler (`UpdateStationMetar`, `RetrieveNearestStationsAsync`).
- Controles: `UCWeatherInfo` (pantalla inferior de la UI principal, **F11**), `UCSmallWeatherInfo`.
- Los valores meteorológicos están disponibles como macros del manipulador CW (`<PRESSURE_MMHG>`, `<PRESSURE_HP>`, viento, temperatura, cobertura nubosa, altura de base de nubes).

**Complejidad: media**.

---

## 16. Manipulador CW (keyer) y entrenador

**Keyer** (`L4ONG.BL.KEYERENGINE`, `L4ONG.Winkeyer`):
- Motores (`KeyerSoftwareEnum`): **WinKeyer** hardware (`WinKeyerHardware`, protocolo completo: `WK_ModeReg`, `WK_Speed`, `WK_Sidetone`, `WK_Weight`, `WK_LeadIn`, `WK_Tail`, `WK_WpmMin/Max`, `WK_X1mode/X2mode`, `WK_Kcomp`, `WK_Farns`, `WK_SampAdj`, `WK_DitDahRatio`, `WK_PinCfg`, `DisableSpeedPot`, DTR alto para dispositivos serie, Iambic A/B, `PaddleSwap`, `PaddleEcho`, `AutoSpace`, `CTSpace`, `UsePTT`, `UseSideTone`), **manipulador por puerto serie** (`SerialPortKeyer`, puertos COM separados para clave y PTT, prosignos), **TCI** (`TCIEngineKeyer`) y tono generado por software (NAudio `SignalGenerator`).
- Macros: 10+ cadenas `F1String`…`F10String` (`macro/defaultMacro.txt`, formato `texto###ETIQUETA`), variables `*` (mi indicativo), `!` (indicativo de la contraparte), `<STTX>`, `<STTXF>`, `<NAME>`, `<MY_QTH>`, `<n>` para repetir la línea n veces (2–10), variables meteorológicas. `WinkeyerMacros`, `KeyerLastMacroFile`.
- Ventana del keyer con mapa integrado, "siempre encima" opcional, atajos `CTRL-F9`/`CTRL-F10`, `ESC` aborta la transmisión y opcionalmente limpia el indicativo (`CWKeyerEscapeClearQso`).
- Pantallas: `UCWinkeyerSettings`, `UCSerialKeyerSettings`, `WinKeyerConfiguration`.

**Entrenador de CW** (`L4ONG.CWTRAINER`, `FormCWTrainer`, menú VIEW): tono lateral configurable (`TrainerSideTone`, por defecto 444 Hz), velocidad y velocidad Farnsworth, selección de caracteres (`UCCharacterSelector`), generación de palabras aleatorias (número y longitud), generación de indicativos aleatorios a partir de la base de usuarios LoTW (`GenerateRandomCallsign`, `LOTWUsers`), ruido blanco con ganancia ajustable (`WhiteNoiseSignalGenerator`), simulación de desvanecimiento (`TrainerFading`), dispositivo de salida seleccionable.

**Complejidad: media-alta** (el protocolo WinKeyer y el temporizado del CW son delicados).

---

## 17. Concursos

- No hay ventana de concurso separada desde 2.x ("Contest interface has been removed. All functions are now managed by main interface"). Se gestiona con `ContestMode` y `ContestConfiguration` (`ContestId`, fechas de inicio y fin, intercambio fijo o serial, prefijo/sufijo del número, valor actual).
- Campos ADIF asociados: `contestid`, `stx`, `stxstring`, `srx`, `srxstring`, `class`, `precedence`, `arrlcheck`, `arrlsect`.
- `contest.csv`: ~280 identificadores ADIF de concursos con su nombre.
- Controles: `UCContest`, `UCContestManager`, layout específico `layout_datagrid_Contest.json`.
- Estadísticas de concurso: `ContestCountryStatistics`, `ContestGridStatistics`, superponibles en la vista de clúster.
- Integración N1MM/QARTest por UDP (ver módulo 11) para usar un logger de concurso externo.

**Complejidad: media** (es un modo del log, no un módulo independiente).

---

## 18. Satélites

No hay módulo de seguimiento orbital. El soporte es **de registro**:
- Campos `satelliteqso`, `satmode`, `satname`, `propmode=SAT`, `antaz`, `antel`, `bandrx`, `freqrx`.
- Catálogos: `satellites.csv` (56 satélites, nombre largo y designador: AO-7, AO-73, QO-100, RS-44, SO-50, XW-2A…F, ARISS, FO-29…) y `satmodelist.txt` (21 modos: A, AU, B, J, JL, K, KT, L, LS, R, RA, RK, RT, S, SX, T, US, UV, VS, VU).
- La etiqueta QSL "SAT MODE" muestra también el nombre del satélite.

**Complejidad: baja** (si el clon no añade seguimiento orbital).

---

## 19. Estadísticas

- `StatisticsManagement`: `CalculateCountryStatistics`, `CalculateGridStatistics`, `CalculateCallsignStatisticsAsync`, `CalculateAwardStatisticsAsync`, `RecalculateStatisticsAsync`.
- Entidades: `CountryStatistic(Data)`, `GridSquareStatistic(Data)`, `AwardStatistic`, `ShortStatistic`, `BandModeConfirmationStatus(List)`, `BandModeAwardStatus(List)`, `StatCounter`, `StatisticTypeEnum`, `StatisticAxisEnum`.
- Vistas: pestaña de estadísticas en la UI principal (**F6**, ocultable), `UCQsoStatistic`, `UCStatistics`, `UCGraphicalStatistics`, `UCDxccStatistic`, `FilteredStatisticForm`, `UCBandsGrid` / `FormBandGrid` (matriz banda × modo).
- La matriz banda/modo muestra estado de país (naranja/verde) y de indicativo (cian/azul); azul para contactos previos con el mismo indicativo en otra banda/modo; respeta la "vista de confirmación preferida" del usuario.
- `StatisticGridLength` (4 o 6 dígitos de locator), estadísticas en tiempo real del indicativo tecleado (`CallsignRealtimeStats`).

**Complejidad: media-alta** (rendimiento sobre logs grandes).

---

## 20. Búsqueda y gestión masiva de QSO

- Motor de consulta genérico: `QueryParameter(s)`, `SortParameter(s)`, `DatabaseParameterJunctionEnum` (AND/OR) y operadores `EQUAL, NOT_EQUAL, GREATER, GREATER_OR_EQUAL, SMALLER, SMALLER_OR_EQUAL, LIKE, LIKE_STARTING_WITH, LIKE_ENDING_IN, IN, NULL, NOT_NULL`.
- `UCQueryParameters`, filtros guardables/cargables en `user/filters`, layouts de rejilla por pantalla en `layout/` y `user/layout_datagrid_*_user.json` (Home, Contest, QsoEditor, ManageConfirmation, LabelSelectionGrid, WkdBefore, WorkedQso_Stat, cluster…).
- Consulta SQL directa (`ExecuteArbitraryQueryWithList`, `ExecuteQueryOnLog`) con guardar/cargar.
- **QSO Management** — actualizaciones masivas: PFX, fecha/hora, `my references`, `my associations`, verificación de datos (`QSO Checks`, `SafetyCheck`), `UPDATE_CQ_ITUZONE`, `UPDATE_GRIDSQUARE`, `USE_EXTERNAL_DATA`, `EXPORT_CONFIRMATIONS`, `IMPORT_CONFIRMATIONS`; la selección se conserva entre actualizaciones.
- Hasta **6 referencias de diploma** seleccionables por QSO en la rejilla (antes 2) y hasta 2 diplomas personalizables mostrados por QSO (por defecto IOTA y SOTA).

**Complejidad: media-alta**.

---

## 21. Copias de seguridad

`BackupManagement`:
- Copia **principal** y copia **alternativa** independientes, cada una con carpeta propia, rotación configurable (`BackupRotation`, `BackupRotationAlternate`), formato **ADIF** o **SQLite nativo** (`NativeBackup`, `NativeAlternateBackup`) y compresión ZIP opcional.
- `BackupOnClosure` (copia automática al cerrar, desactivable), copia mensual adicional.
- Se copia también `config.json` junto al backup (`EA8DLF_<fecha>_config.json` y `..._backup.adi` en `backup/`), además de `user/last_valid_config.json` y una copia en la carpeta Documentos por si Windows bloquea `AppData\Roaming`.
- Recuperación automática de configuración: si falta `config.json` busca `last_valid_config.json` y luego el backup más reciente; opción *HELP → RESTORE CONFIGURATION*.
- Copia de seguridad de los diplomas del usuario en cada actualización (`awards/`).

**Complejidad: baja-media**.

---

## 22. Planificador de tareas (scheduler)

`ProgramScheduler` con definiciones en `scheduler.xml` (y `scheduler_clean.xml` de fábrica, `user/scheduler_*_user.xml`).

Estructura de cada tarea: `ScheduleId (GUID), ScheduleName, Active, RunOnStart, NextRun, Schedule (Interval), SpanTime (s), MinimumSpanTime (s), Activity, ExecutePath, ExecuteParameters, ProcessName, SystemProcess`.

**Actividades** (`ScheduleActivityEnum`, 22): `UpdateSolarData`, `UpdateSolarImage`, `UpdateMetarData`, `UpdateCTYFile`, `UpdateCountryFile`, `UpdateModes`, `UpdateBands`, `UpdateNonStandardCalls`, `UpdateLOTWList`, `UpdateClubLog`, `UpdateClubMembership`, `UpdateClubMembershipList`, `UpdateAwardsList`, `UpdateIOTA`, `UpdateSOTA`, `UpdateFCC`, `BackupUserAwards`, `CheckInternetConnection`, `SendADIF`, `SendQSO`, `HRDLogOnline`, `GenerateWebpage`.

Además permite lanzar **procesos externos** (`ExecutePath` + `ExecuteParameters`, `SystemProcess=false`). Control `UCSchedulerControl`. Reinicio a fábrica con `L4ONG.exe /SCHEDULER`.

**Complejidad: baja-media**.

---

## 23. NET Control (control de red / ronda)

Entidades `NetControl`, `NetControlId`, `NetUser`; carpeta de datos `netcontrol/`; constante `Log4OM2NetControlFolder`.

Funciones confirmadas por las notas de versión: gestión de la lista de participantes de una ronda, lista de usuarios **ordenable**, edición de datos básicos del QSO activo seleccionado (RST enviado, RST recibido y comentario), apertura automática al reiniciar. Se apoya en la rejilla de QSO y en el motor de log normal.

*Detalle fino de la pantalla: a confirmar con el manual (la UI está en `L4ONG.Controls`/`L4ONG.exe` con nombres ofuscados).*

**Complejidad: media**.

---

## 24. Página web / exportación publicada

Módulo `L4ONG.Webpage`:
- `WebpageConfiguration`: `WebpageId, Enabled, ExportAsCSV, SourceFile, OutputFolder, OutputFileName, RefreshInterval, ExportRange, NoTimeDisplay, ExportFields, EnableFtp, LocalFtp, FtpServer, FtpPort, RemoteFolder, RemoteFileName, FtpUserName, FtpPassword, UseSFTP`.
- Plantilla `log4om.htm`: HTML con marcador `##table##` que se sustituye por la tabla de QSO (clases CSS `even`/`odd`, cabecera "Log4OM realtime log", pie "Generated with Log4OM").
- Generación programada (`GenerateWebpage`) y subida por **FTP** o **SFTP** (`FtpUploadAsync`, `SFtpUploadAsync` con SSH.NET).
- Alternativa de salida en CSV con campos seleccionables.

**Complejidad: baja**.

---

## 25. Audio y adjuntos

- `AudioManagement` con NAudio: dispositivos separados de **recepción, transmisión, grabación y reproducción** (`ReceiveDevice`, `TransmitDevice`, `RecordDevice`, `PlayDevice`); `BeginRecord`, `StartRecording`, `TerminateRecord`, `BeginPlay`, `StopPlay`, `BeginTune`; carpeta `audio/`, `AudioFiles` en configuración, `GetAudioFileName`.
- `AttachmentFolder` y `user/attachments` para imágenes de QSL y otras (visualizables en el editor de QSO); `AttachmentTypeEnum`.

**Complejidad: media**.

---

## 26. Chat OAMS (retirado)

`L4ONG.TALK` — cliente WebSocket del *Off-Air Message Service* compartido con GridTracker: `ws://oams.space`, puertos 18360 + rango 10 (debug 18280). `FormTalk`, `ChatUser(s)`, `ChatMessage`, lista de amigos, `UCOams`, `TalkServer`. **Las notas de la 2.37.2 indican que el motor de chat fue eliminado por cese del proveedor**, aunque el DLL y la sección `[CHAT]` de `config.ini` siguen presentes.

**Complejidad: media** — recomendación: **no replicar**.

---

## 27. Configuración, perfiles y utilidades del sistema

- **Configuración**: `user/config.json` (170 KB) con `UserConfigs[]` (6 perfiles en la instalación analizada), `RunningConfigurationId`, `WinPositions` (posición y estado de cada ventana por nombre de clase), `ConfigurationVersion` (2040000000), `DistanceMetric`, `DefaultRowLimit` (5000), `DefaultLogLevel`, opciones globales (comprobación de versión, beta pública, envío de datos de uso).
- **Seguridad**: `AllowTls1_0`, `AllowTls1_1`, `AllowSSL3`, `AllowInsecureCerts` para poder acceder a fuentes externas con certificados caducados.
- **Correo**: SMTP propio (`EmailSmtp`, puerto, SSL, autenticación) para las alertas de clúster.
- **Rendimiento**: sección PERFORMANCE con desactivación selectiva de funciones, herramienta de *benchmark* (`perfTestCalls.txt` con indicativos de prueba), monitorización de la UI (etiqueta PERFORMANCE con log a nivel DEBUG).
- **Editor de recursos** (*Settings → resource editor*): permite editar los ficheros de configuración listados en `programresources.txt` (18 ficheros: `antpath.csv`, `awardOverride.csv`, `bandlist.txt`, `clublist.txt`, `clustercommands.txt`, `clusterdefaultscript.txt`, `clusterknownwords.txt`, `contest.csv`, `defaulthamclubs.csv`, `ITU_IARU.csv`, `modelist.csv`, `NonStandardCallsign.txt`, `offsetValuesVFOA/B.txt`, `propagation.csv`, `rst.xml`, `satellites.csv`, `satmodelist.txt`, `states.csv`).
- **Diagnóstico** (menú Help): pantalla `JT CHECK` (comprobación de la comunicación JT) y pantalla `WARNING` (errores de login en fuentes externas y fallos de subida de QSO).
- **Solicitud de soporte**: empaqueta configuración y logs, con opción de eliminar contraseñas (las credenciales de correo nunca se incluyen).
- **Actualizaciones**: `http://www.log4om.com/l4ong/version/version.php` (+ `usage.php` para telemetría), manuales BÁSICO y AVANZADO desde `log4om.com/l4ong/usermanual/` (`Log4OMNG_*.pdf` y `Log4OMNG_*_ADV.pdf`), canal beta pública.
- **Argumentos de línea de comandos**: `/RESETLAYOUT`, `/REMOVEUSERFILES`, `/RESETMAPCACHE`, `/SCHEDULER`, `/FACTORYRESET`.
- **Varias instancias** simultáneas soportadas; modo *portable* (`IsPortable`) e *instalado*.
- **Herramienta separada**: `L4ONG.ConfigManager.exe` con su propio `configmanager.ini`.
- **Idioma**: la aplicación es solo en inglés (`Culture = en-US`, sin ensamblados satélite propios); existe `ManualLanguages` para los manuales.
- **Temas**: pares de color claro/oscuro para reloj (`UTCDark/UTCLight`, `LocDark/LocLight`) y colores de celda de rejilla; no hay tema oscuro global.

**Complejidad: media**.

---

## 28. Resumen de dependencias externas de red (para el clon)

| Categoría | Servicios |
|---|---|
| Datos de país | log4om.com (country.xml, modelist, rst, bandlist, NonStandardCallsign, clubs), country-files.com, ClubLog CTY |
| Lookup | QRZ.com, HamQTH, QRZCQ, HamCall (online y DVD), FCC ULS |
| Confirmaciones | LoTW (TQSL), eQSL.cc, ClubLog, QRZ.com Logbook, HamQTH, HRDLog, Cloudlog |
| Diplomas | log4om.com/resources/awards + reports, IOTA, SOTA, POTA, WWFF, WCA, ARLHS, y ~120 URL más en la tabla `Awards` |
| Clúster | hamcluster.net (lista), cientos de nodos Telnet |
| Solar | NOAA SWPC (DGD, DSD, ciclo predicho, auroras), NASA SDO |
| Meteo | aviationweather.gov (stationinfo, metar, taf) |
| Digimodos | PSK Reporter |
| Clubes | hamclubs.info, skccgroup.com, fists.co.uk, Google Sheets… |

---

## 29. Resumen y recuento

**Módulos funcionales identificados: 27** (excluyendo la sección 0 de arquitectura y la 28 de dependencias):

1. Base de datos del log (SQLite/MySQL/MariaDB) — alta
2. Entrada y edición de QSO (+ LazyLog) — alta
3. ADIF, importación y exportación (ADIF/CSV/Excel/KML, monitor ADIF) — alta
4. Resolución DXCC/país/bandplan/RST — alta
5. Búsqueda de indicativos y membresías de club — media-alta
6. QSL y confirmaciones (8 métodos, 7 servicios) — alta
7. Impresión de etiquetas, tarjetas QSL y libro de guardia — media-alta
8. Diplomas, referencias e informes personalizados — muy alta
9. Clúster DX, alertas y supercluster — muy alta
10. Control CAT (Hamlib/OmniRig/TCI) + rotor — alta
11. Integración de red UDP/TCP (WSJT-X, JTDX, MSHV, N1MM, QARTest, FLDigi) y control remoto — alta
12. Mapas y visualización geográfica — media-alta
13. Propagación VOACAP4OM y datos solares — muy alta
14. Reloj mundial, grayline y orto/ocaso — media
15. Meteorología METAR/TAF — media
16. Manipulador CW y entrenador de CW — media-alta
17. Concursos (modo integrado) — media
18. Satélites (solo registro) — baja
19. Estadísticas — media-alta
20. Búsqueda y actualización masiva de QSO — media-alta
21. Copias de seguridad — baja-media
22. Planificador de tareas (22 actividades) — baja-media
23. NET Control — media
24. Página web / exportación publicada (FTP/SFTP) — baja
25. Audio y adjuntos — media
26. Chat OAMS (retirado; no replicar) — media
27. Configuración, perfiles, diagnóstico y utilidades — media

**Distribución de complejidad**: 4 muy alta, 7 alta, 7 media-alta, 5 media, 3 baja-media/baja, 1 descartable.

### Puntos que quedan *a confirmar*
- Lista exacta de comandos del interfaz de control remoto (puertos 2241/2242) — cadenas cifradas en el binario.
- Valores numéricos exactos de varias enumeraciones (`CATSoftwareEnum` 10/20/30 se corresponden con Hamlib/OmniRig/TCI por el orden y los puertos por defecto 4532 / — / 40001, pero no está confirmado en metadatos).
- Uso concreto de `Google.Protobuf` (probable serialización con el motor VOACAP nativo).
- Textos literales de menús y mensajes de la interfaz (no recuperables; deberán redactarse de nuevo en español).
- Detalle de la pantalla de NET Control.
- Formato binario exacto de los mensajes N1MM/QARTest que emite (se sabe que existe `N1MM_CONTACT`, `N1MM_MESSAGE`, `N1MM_QSO` y que la fecha se corrigió en 2.38).
