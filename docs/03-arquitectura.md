# Cuaderno NODISLA — Arquitectura y stack técnico

Documento de arquitectura. Versión 1, 21-09-2026.
Destino: C# .NET 8 + WPF, Windows 10 x64 (10.0.18362). SDK 8.0.422, Visual Studio 18.
Todas las librerías citadas se han verificado contra NuGet y GitHub el 21-09-2026 (versión, fecha de publicación y licencia). No hay nada recomendado "de memoria".

---

## 0. Resumen ejecutivo

Log4OM NextGen es una aplicación **.NET Framework 4.x + WinForms**, con EF6, SQLite/MySQL/MariaDB, NLog y una capa de presentación basada en `ObjectListView` y `GMap.NET.WindowsForms`. Su separación BE/BL/DAL es sensata y merece copiarse como *idea*, pero su implementación concreta (EF6, WinForms, `System.Data.SQLite`, motor VOACAP nativo propietario) no es portable a .NET 8 y no conviene imitarla literalmente.

La propuesta es una solución en **capas concéntricas** (dominio → aplicación → infraestructura → UI), con el núcleo entero **libre de WPF** para que sea testeable sin arrancar interfaz, y con **cada integración externa detrás de un puerto (interfaz)** del proyecto de Aplicación. Esto es lo que permite que el módem FT8 propio y el adaptador WSJT-X/JTDX por UDP sean dos implementaciones intercambiables del mismo contrato, y que se pueda empezar por la barata y migrar a la cara sin reescribir la aplicación.

El cambio de alcance (modos digitales **dentro** de la app) es el mayor riesgo del proyecto y se trata en la sección 6 con datos verificados.

---

## 1. Lo que enseña el original

### 1.1 Librerías de terceros en `D:\Log4OM NextGen\`

| DLL | Qué es | Para qué lo usa Log4OM |
|---|---|---|
| `EntityFramework.dll`, `EntityFramework.SqlServer.dll` | Entity Framework **6** (no EF Core) | ORM sobre el log. Confirma que es .NET Framework |
| `System.Data.SQLite.dll`, `.EF6`, `.Linq`, `SQLite.Interop.dll` | Proveedor SQLite clásico de la System.Data.SQLite team | Base de datos por defecto (`Log4OMNG.SQLite`) |
| `MySql.Data.dll`, `MySqlConnector.dll` | Conectores MySQL (el oficial de Oracle y el libre) | Backends MySQL y MariaDB opcionales |
| `Ubiety.Dns.Core.dll` | Resolutor DNS puro .NET | Dependencia de `MySqlConnector` (SRV lookup) |
| `ObjectListView.dll` | Control ListView avanzado **de WinForms** | Rejilla de QSOs, cluster, búsquedas. Prueba de que la UI es WinForms |
| `GMap.NET.Core.dll`, `GMap.NET.WindowsForms.dll` | Mapas con teselas, control **WinForms** | Mapa de QSOs, mapa de cluster, grid locator |
| `Microsoft.WindowsAPICodePack.dll`, `.Shell.dll` | API Code Pack de Microsoft (abandonado en 2010) | Diálogos de fichero modernos, barra de tareas, jump lists |
| `CookComputing.XmlRpcV2.dll` | XML-RPC.NET de Charles Cook (abandonado, .NET Framework) | Cliente **XML-RPC de FLDigi** (`L4ONG.BL.FLDIGI`, `L4ONG.FL`) |
| `NAudio.*` (Core, Wasapi, WinForms, WinMM) | Audio en .NET | CW trainer, reproducción de alertas, keyer por tarjeta de sonido |
| `NLog.dll` | Logging | Trazas de la aplicación (`L4ONG.Logger`) |
| `Newtonsoft.Json.dll` | JSON | APIs web (ClubLog, QRZ, ctyfile.json de 8,9 MB) |
| `INIFileParser.dll` | Lectura/escritura de INI | `config.ini` y `configmanager.ini` en `%AppData%\Log4OM2` |
| `EPPlus.dll`, `EPPlus-LGPL.dll`, `EPPlus.Interfaces`, `EPPlus.System.Drawing` | Excel OOXML | Exportación a XLSX. **Llevan las dos ramas**: la v4 LGPL y la v5+ Polyform Noncommercial — señal de que el cambio de licencia les obligó a mantener ambas |
| `itext.kernel/io/layout/commons` + `itext.bouncy-castle-*` | iText 7 (PDF) | Generación de PDF: etiquetas QSL, informes, diplomas |
| `PdfSharp-gdi.dll` | PDFsharp (rama GDI+) | Segundo motor PDF, probablemente heredado |
| `BouncyCastle.Crypto.dll`, `BouncyCastle.Cryptography.dll`, `bc-fips-1.0.2.dll`, `bcpkix-fips-1.0.2.dll` | Bouncy Castle (clásico, nuevo y FIPS) | Doble uso: **certificados PKCS#12 / X.509 de LoTW** y firma de PDF vía iText |
| `Google.Protobuf.dll` | Protocol Buffers | Serialización binaria compacta, muy probablemente el caché de teselas (`TileDBv5`) y datos externos |
| `K4os.Compression.LZ4*`, `K4os.Hash.xxHash` | LZ4 + xxHash | Compresión rápida de cachés (teselas, `ExternalData.SQLite` de 106 MB) |
| `ZstdNet.dll`, `ZstdSharp.dll` | Zstandard (dos bindings distintos) | Compresión de datos externos / backups |
| `Renci.SshNet.dll` | SSH.NET | Cluster/telnet sobre SSH y posible backup remoto |
| `SuperSocket.ClientEngine.dll`, `WebSocket4Net.dll` | Cliente WebSocket (.NET Framework) | Cluster web / feeds en tiempo real (tipo DXWatch, HamAlert) |
| `Microsoft.Extensions.*` (DI, Logging, Options, Caching.Memory, Primitives) | Stack moderno de Microsoft | Ya usaban inyección de dependencias y caché en memoria |
| `Microsoft.IO.RecyclableMemoryStream` | Pool de `MemoryStream` | Evitar presión de GC en el LOH al mover ficheros grandes |
| `dvoa.dll`, `voa4om_native.dll` (16 MB), `Voacap4OM.Engine.Interface.dll` | **Motor VOACAP nativo** envuelto por un interop propio | Predicción de propagación. Es el binario original NTIA/ITS empaquetado |
| `System.Buffers/Memory/ValueTuple/…` | Paquetes de compatibilidad BCL | Confirman .NET Framework, no .NET Core |

**Conclusión técnica**: el original es .NET Framework + WinForms. **No hay nada que reutilizar binariamente**; se copia el *modelo funcional*, no el código.

### 1.2 Separación por capas del original (referencia)

```
L4ONG.BE            Business Entities  — modelo de datos (547 KB)
L4ONG.INTERFACES    contratos entre capas (17 KB) ← la pieza clave de su diseño
L4ONG.BL            Business Logic (3,4 MB) + submódulos verticales:
   BL.AWARDS  BL.CAT  BL.CLUSTER  BL.DATASEARCH  BL.FLDIGI
   BL.INIT    BL.KEYERENGINE  BL.VOACAP  BL.WORKEDBEFORE
L4ONG.DAL           acceso a datos (33 KB, fachada) + proveedores:
   DAL.SQLITE  DAL.MYSQL  DAL.MARIADB  DAL.AWARDS  DAL.EXTDATA  DAL.DEPLOY
L4ONG.Framework / Common / Logger / Storage    infraestructura
L4ONG.Controls      controles de UI (13,5 MB)
L4ONG.CHRON (13,8 MB), L4ONG.AW (4,1 MB), L4ONG.Cluster (9,1 MB)  ventanas grandes
L4ONG.CAT  L4ONG.JT  L4ONG.FL  L4ONG.TCPServer  L4ONG.OUTPUT   integraciones
L4ONG.CWTRAINER  L4ONG.Winkeyer  L4ONG.TALK  L4ONG.WEATHER  L4ONG.Webpage
L4ONG.ConfigManager.exe    aplicación de configuración SEPARADA del ejecutable
```

Dos lecciones que sí copiamos:
1. **`INTERFACES` de 17 KB entre capas de megabytes**: todo pasa por contratos pequeños. Es exactamente la arquitectura de puertos que proponemos.
2. **Un módulo BL por integración vertical** (CAT, CLUSTER, FLDIGI, VOACAP…). Se mantiene, pero agrupado en menos ensamblados para no acabar con 40 `.csproj`.

Una lección que **no** copiamos: `ConfigManager.exe` como aplicación aparte. La configuración va dentro, en una ventana de ajustes.

---

## 2. Estructura de la solución

Regla de oro: **sólo dos proyectos conocen WPF** (`…Ui` y `…Ui.Mapa`). Todo lo demás compila y se prueba sin interfaz gráfica.

```
CuadernoNodisla.sln
│
├── src/
│   ├── Nodisla.Cuaderno.Dominio               net8.0
│   ├── Nodisla.Cuaderno.Aplicacion            net8.0
│   ├── Nodisla.Cuaderno.Datos                 net8.0
│   ├── Nodisla.Cuaderno.Adif                  net8.0
│   ├── Nodisla.Cuaderno.Infraestructura       net8.0
│   ├── Nodisla.Cuaderno.Servicios             net8.0
│   ├── Nodisla.Cuaderno.Integraciones         net8.0
│   ├── Nodisla.Cuaderno.Propagacion           net8.0
│   ├── Nodisla.Cuaderno.Audio                 net8.0-windows
│   ├── Nodisla.Cuaderno.Radio                 net8.0-windows
│   ├── Nodisla.Cuaderno.Modos                 net8.0
│   ├── Nodisla.Cuaderno.Modos.Nativo          net8.0  (interop + runtimes/win-x64)
│   ├── Nodisla.Cuaderno.Ui                    net8.0-windows, UseWPF
│   └── Nodisla.Cuaderno.Ui.Mapa               net8.0-windows, UseWPF
│
├── tests/
│   ├── Nodisla.Cuaderno.Dominio.Pruebas
│   ├── Nodisla.Cuaderno.Adif.Pruebas
│   ├── Nodisla.Cuaderno.Datos.Pruebas
│   ├── Nodisla.Cuaderno.Integraciones.Pruebas
│   ├── Nodisla.Cuaderno.Modos.Pruebas
│   └── Nodisla.Cuaderno.Servicios.Pruebas
│
├── herramientas/            utilidades de desarrollo (importadores, bancos de prueba)
├── nativo/                  fuentes C de ft8_lib y script de compilación
├── recursos/                cty.dat, bandplan, diplomas, iconos, sonidos
└── docs/
```

### 2.1 Responsabilidad de cada proyecto

**`Dominio`** — sin dependencias externas salvo la BCL.
Entidades (`Qso`, `Estacion`, `Perfil`, `Corresponsal`, `Diploma`, `Referencia`), objetos de valor (`Indicativo`, `Locator`, `Frecuencia`, `Banda`, `Modo`, `Informe`), reglas invariantes (un `Locator` válido, una frecuencia dentro de banda, el `Indicativo` normalizado), y el cálculo DXCC/zonas a partir de `cty.dat`. Todo lo que se puede probar con una tabla de casos y sin E/S. Aquí es donde se gana o se pierde la calidad del producto.

**`Aplicacion`** — casos de uso y **puertos**.
`RegistrarQso`, `ImportarAdif`, `SincronizarLoTW`, `BuscarTrabajadoAntes`, `CalcularDiplomas`, `ArrancarSesionDigital`. Define las interfaces que el resto implementa: `IRepositorioQso`, `IControlEquipo`, `IFuenteSpots`, `IServicioQsl`, `IConsultaIndicativo`, `IMotorModoDigital`, `IPrediccionPropagacion`. **Ningún puerto menciona una librería concreta.** Es el equivalente a `L4ONG.INTERFACES`, y es lo que hace posible cambiar de estrategia FT8 sin tocar la app.

**`Datos`** — EF Core 8 + SQLite, `DbContext`, configuraciones *fluent*, migraciones, repositorios, y las consultas de informe con Dapper. Implementa `IRepositorioQso` y compañía.

**`Adif`** — lectura y escritura de ADIF 3.x (ADI y ADX), tolerante a ficheros reales sucios (campos desconocidos, longitudes mentirosas, codificaciones mezcladas). Proyecto propio porque es el formato de intercambio de todo el ecosistema y merece su propia batería de pruebas con ficheros reales exportados de Log4OM.

**`Infraestructura`** — logging, configuración, rutas de `%AppData%`, `HttpClientFactory` con Polly, caché, copias de seguridad, cifrado de credenciales guardadas (DPAPI de usuario).

**`Servicios`** — clientes de servicios web: LoTW, eQSL, ClubLog, QRZ.com (XML + Logbook), HamQTH, HamAlert. Cada uno detrás de su puerto.

**`Integraciones`** — protocolos con software local: WSJT-X/JTDX/MSHV/JS8Call por UDP, FLDigi por XML-RPC, N1MM+ por UDP, cluster DX por Telnet, servidor TCP propio para que otros programas hablen con nosotros (equivalente a `L4ONG.TCPServer`).

**`Propagacion`** — VOACAP (proceso externo + análisis de salida), índices solares (SFI, A, K) y cálculos de trayecto (distancia, azimut, paso gris).

**`Audio`** — captura y reproducción WASAPI vía NAudio, remuestreo, medidor de nivel, enrutado de dispositivos. `net8.0-windows` porque WASAPI es de Windows.

**`Radio`** — CAT y PTT: OmniRig (COM), `rigctld` por TCP, puerto serie directo, CAT por WinKeyer. `net8.0-windows` por COM interop y `System.IO.Ports`.

**`Modos`** — el módem digital: sincronización, decodificación, codificación, máquina de estados del QSO automático, datos del waterfall. **Sin WPF**: produce matrices de magnitudes, no píxeles. Así es probable con ficheros WAV.

**`Modos.Nativo`** — envoltura P/Invoke de la DLL C compilada desde `ft8_lib`, con los binarios en `runtimes/win-x64/native/`. Separado para que `Modos` pueda funcionar con implementación gestionada o nativa.

**`Ui`** — WPF puro: vistas, ViewModels, temas, recursos en español. Aloja el `Generic Host`.

**`Ui.Mapa`** — el control de mapa, aislado. Los controles de mapa son la dependencia que más se rompe entre versiones; tenerlo aparte permite sustituirlo sin tocar la UI principal.

### 2.2 Dirección de las dependencias

```
Ui ──► Ui.Mapa ──► Aplicacion ──► Dominio
 │                    ▲
 └──► (Host) registra: Datos, Servicios, Integraciones, Propagacion,
                       Audio, Radio, Modos, Infraestructura
                       — todos ellos ──► Aplicacion ──► Dominio
```

`Dominio` no referencia a nadie. `Aplicacion` sólo a `Dominio`. La UI **no** referencia `Datos`, `Servicios` ni `Integraciones`: sólo `Aplicacion`. El único sitio donde se ven todos los ensamblados a la vez es el registro de servicios del arranque (`ConfiguracionDeServicios.cs` en `Ui`). Esto se puede verificar con una prueba de arquitectura automática.

### 2.3 Arranque por fases (no crear los 14 proyectos el primer día)

- **Fase 1** (cuaderno usable): `Dominio`, `Aplicacion`, `Datos`, `Adif`, `Infraestructura`, `Ui` + sus pruebas. Seis proyectos.
- **Fase 2** (operar): `Radio`, `Integraciones`, `Ui.Mapa`.
- **Fase 3** (servicios y diplomas): `Servicios`, `Propagacion`.
- **Fase 4** (modos digitales integrados): `Audio`, `Modos`, `Modos.Nativo`.

---

## 3. Stack

Todo verificado en nuget.org el 21-09-2026.

### 3.1 Núcleo

| Necesidad | Elección | Versión verificada | Licencia | Justificación |
|---|---|---|---|---|
| Patrón de UI | **CommunityToolkit.Mvvm** | 8.4.2 (25-03-2026) | MIT | De Microsoft, vivo, 28,6 M de descargas. Generadores de código: `[ObservableProperty]` y `[RelayCommand]` eliminan el boilerplate de `INotifyPropertyChanged`. Sin framework pesado encima (Prism, Caliburn) que ate el proyecto |
| Inyección de dependencias | **Microsoft.Extensions.DependencyInjection** + **Hosting** | fijar rama **8.0.x** | MIT | Es el estándar y ya lo usa el propio Log4OM. El `Generic Host` da además `IHostedService`, que es exactamente lo que necesitan el oyente UDP, el cliente de cluster y el bucle de audio |
| Acceso a datos | **EF Core 8 + Microsoft.Data.Sqlite** | 8.0.31 | MIT | Ver 3.2 |
| Consultas pesadas | **Dapper** | 2.1.86 | Apache-2.0 | Ver 3.2 |
| Logging | **Serilog** + `Sinks.File` + `Extensions.Hosting` | 4.4.0 / 7.0.0 | Apache-2.0 | Ver 3.3 |
| Configuración | `Microsoft.Extensions.Configuration.Json` + `IOptions<T>` | 8.0.x | MIT | JSON, no INI. Ver 3.4 |
| Validación | **FluentValidation** | 12.1.1 | Apache-2.0 | Reglas de validación de QSO declarativas y probables por separado de la UI |
| Resiliencia HTTP | **Polly** (`Microsoft.Extensions.Http.Resilience`) | 8.8.0 | BSD-3 | Reintentos con retardo exponencial para LoTW/ClubLog/QRZ, que fallan a menudo |

**Aviso importante sobre versiones**: la búsqueda en NuGet devuelve hoy `Microsoft.Extensions.*` y `Microsoft.EntityFrameworkCore.*` en **10.0.12**, que exige `net10.0`. Para .NET 8 hay que **fijar explícitamente la rama 8.0.x** (verificado: EF Core Sqlite 8.0.31, `Microsoft.Extensions.Hosting` 8.0.1, `System.IO.Ports` 8.0.0). Hacerlo con un fichero `Directory.Packages.props` (gestión central de paquetes) para que ningún `.csproj` pueda arrastrar por error una 10.x.

### 3.2 Acceso a datos: EF Core **y** Dapper, no uno u otro

**EF Core 8 + SQLite para escritura y modelo.** Razones:
- Migraciones versionadas. Un cuaderno de guardia vive 20 años y el esquema cambiará muchas veces; sin migraciones esto se convierte en un problema permanente.
- Mapeo de objetos de valor (`Indicativo`, `Locator`) con conversores, lo que mantiene el dominio limpio.
- Seguimiento de cambios para la edición de QSOs en la rejilla.
- SQLite es un fichero: copia de seguridad = copiar el fichero. Perfecto para uso personal, y es lo que ya usa Log4OM.

**Dapper para lectura analítica.** Las pantallas de estadísticas, diplomas y "trabajado antes" hacen agregaciones sobre decenas de miles de filas. EF Core genera SQL correcto pero la materialización con seguimiento es cara. Dapper sobre la **misma** conexión, con consultas escritas a mano y proyectadas a `record`, es mucho más rápido y perfectamente mantenible. Es una división limpia: EF escribe, Dapper lee informes.

**No usar `System.Data.SQLite`** (el que lleva Log4OM). `Microsoft.Data.Sqlite` es el proveedor mantenido por el equipo de .NET, más ligero y sin el `SQLite.Interop.dll` por arquitectura.

**Decisión de esquema**: SQLite, un solo fichero, en `%AppData%\CuadernoNodisla\cuaderno.sqlite`. MySQL/MariaDB del original **no se replican** (complejidad enorme para uso personal). Si alguna vez se necesita multi-puesto, el puerto `IRepositorioQso` permite añadir un proveedor sin tocar la aplicación. Activar **WAL** y crear índices sobre `(indicativo)`, `(fecha_hora)`, `(banda, modo)`, `(dxcc)` y `(grid)` desde la primera migración.

### 3.3 Logging: Serilog, no NLog

Log4OM usa NLog (6.2.1, vivo, también válido). Se elige **Serilog** porque:
- **Logging estructurado**: `Log.Information("QSO {Indicativo} en {Banda} a {Frecuencia} Hz", …)` conserva los campos. Para depurar por qué un decodificador FT8 perdió una trama o por qué el cluster se desconectó, buscar por campo vale mucho más que buscar en texto plano.
- Integración de primera con `ILogger<T>` y el `Generic Host`, que es el modelo de todo lo demás del stack.
- `Serilog.Sinks.File` con rotación diaria y retención limitada, a `%AppData%\CuadernoNodisla\registros\`.
- Un *sink* en memoria alimenta una ventana "Registro de la aplicación" dentro de la app, que es lo que hace falta para dar soporte a un usuario sin pedirle que busque ficheros.

### 3.4 Configuración

Tres capas, de menor a mayor prioridad:
1. `appsettings.json` junto al ejecutable — valores por defecto, se sobrescribe al actualizar.
2. `%AppData%\CuadernoNodisla\ajustes.json` — lo que cambia el usuario. **Nunca se toca al actualizar.**
3. Variables de entorno — sólo para desarrollo y diagnóstico.

Enlazado a clases fuertemente tipadas (`AjustesEstacion`, `AjustesRadio`, `AjustesCluster`, `AjustesLoTW`) con `IOptionsMonitor<T>` para que los cambios se apliquen en caliente donde tenga sentido.

**Credenciales** (contraseña de LoTW, clave de QRZ, clave de ClubLog, contraseña de eQSL): **nunca en el JSON en claro**. Se guardan cifradas con `ProtectedData` (DPAPI, ámbito `CurrentUser`), que ata el secreto a la cuenta de Windows. Es lo correcto para una app de escritorio personal y no requiere que el usuario gestione una clave maestra.

**JSON y no INI**: el original usa `INIFileParser` sobre `config.ini`. INI no anida, no tiene tipos y no tiene listas. La configuración de un cuaderno moderno tiene perfiles de estación anidados, listas de nodos de cluster y mapas de bandas: eso es un árbol, y JSON lo representa nativamente con enlace tipado gratis.

### 3.5 UI

| Necesidad | Elección | Versión | Licencia | Nota |
|---|---|---|---|---|
| Comportamientos XAML | **Microsoft.Xaml.Behaviors.Wpf** | 1.1.161 | MIT | Interactividad declarativa sin código en el *code-behind* |
| Tema y controles | **WPF-UI** | 4.3.0 | MIT | Estética Fluent/Windows 11 moderna. Alternativa: MahApps.Metro 2.4.11 (MIT), más veterana pero con estética de Windows 10. Recomendado WPF-UI |
| Rejilla de QSOs | `DataGrid` de WPF con **virtualización** | — | — | Ver riesgo R4. No hace falta una rejilla comercial si se virtualiza y se pagina bien |
| Mapas | **Mapsui.Wpf** | **5.1.0 (27-05-2026)**, TFM `net8.0-windows7.0` | MIT | Ver 3.6 |
| Gráficas | **ScottPlot.WPF** | 5.1.59 | MIT | Estadísticas por banda/modo/año, gráfico de índices solares. Rápido con muchos puntos |
| Waterfall | `WriteableBitmap` propio, o **SkiaSharp.Views.WPF** 4.152.1 (MIT) si hace falta más | — | MIT | Ver 6.5 |
| Excel | **ClosedXML** | 0.105.1 (25-07-2026) | MIT | Ver 3.7 |
| PDF | **QuestPDF** | 2026.9.0 | Community/dual | Ver 3.7 |
| CSV | **CsvHelper** | 33.1.0 | Apache-2.0 / MS-PL | Importación de listas de referencias (SOTA, POTA, faros) |

### 3.6 Mapas: Mapsui, no GMap.NET

Log4OM usa GMap.NET. **No se recomienda**: el paquete `GMap.NET.WinPresentation` (la variante WPF) está en **2.1.7, publicada el 30-06-2022** — más de cuatro años sin publicar. Su licencia además no es una expresión SPDX estándar, sino una página de wiki.

**Mapsui.Wpf 5.1.0** se publicó el **27-05-2026**, es **MIT**, y su paquete declara explícitamente `net8.0-windows7.0` entre sus marcos objetivo. Está construido sobre SkiaSharp (rápido) y BruTile (teselas), tiene capas vectoriales para dibujar trayectos de gran círculo, cuadrículas Maidenhead y marcadores de spots, y soporta OpenStreetMap y cualquier servicio de teselas.

Alternativa de reserva verificada: **XAML.MapControl.WPF 17.1.0** (con `XAML.MapControl.SQLiteCache` para caché de teselas), más ligero si Mapsui resulta excesivo.

**En cualquier caso**: cachear las teselas en disco (`%AppData%\CuadernoNodisla\mapas\`) y respetar la política de uso de OSM (User-Agent identificativo, sin descargas masivas). Log4OM mantiene un `TileDBv5` propio por esta misma razón.

### 3.7 Ofimática: ClosedXML y QuestPDF

**Excel — ClosedXML (MIT), no EPPlus.** Verificado: EPPlus cambió de LGPL a **Polyform Noncommercial 1.0.0** en la versión 5 (2020). El uso personal de EA8DLF encaja en "hobby projects without anticipated commercial application", pero la licencia Polyform no es una licencia libre y ata el futuro del proyecto: si algún día alguien lo usa en un contexto comercial, o si se publica el código, el problema se hereda. El propio Log4OM arrastra las **dos** ramas de EPPlus en su carpeta, lo que muestra la incomodidad. ClosedXML 0.105.1 (julio 2026, MIT, 222 M de descargas) cubre de sobra lo que hace falta.

**PDF — QuestPDF.** API declarativa moderna, versión 2026.9.0, repositorio con actividad de hace días. Aviso: su licencia es **dual** (Community gratuita por debajo de un umbral de ingresos anuales, comercial por encima); GitHub la marca como `NOASSERTION`. Para uso personal es gratuita, pero **conviene leer los términos actuales antes de depender de ella**. Alternativa completamente libre si se prefiere no tener condicionantes: **PdfSharpCore / PDFsharp 6** (MIT), a costa de una API mucho más manual. iText (lo que usa Log4OM) es AGPL: **descartado**, contamina.

### 3.8 Pruebas

| Necesidad | Elección | Versión | Licencia |
|---|---|---|---|
| Marco de pruebas | **xUnit v3** | 4.0.1 | Apache-2.0 |
| Aserciones | **AwesomeAssertions** | 9.6.0 | **Apache-2.0** |
| Dobles de prueba | **NSubstitute** | 6.2.0 | BSD-3 |

**Aviso de licencia verificado**: **FluentAssertions cambió a licencia comercial (Xceed) a partir de la versión 8** — su repositorio aparece hoy como `NOASSERTION` y la última versión es 8.11.0. **No usarla.** `AwesomeAssertions` es el fork libre de la última versión Apache-2.0 y se ha confirmado hoy como **Apache-2.0**, con actividad el 16-09-2026. Es sustitución directa (mismo espacio de nombres y API).

Qué se prueba de verdad:
- **Dominio**: tablas de casos de indicativos raros (`EA8/DL1ABC/P`, `VP2E/K1ABC`, `3DA0RU`), conversión Maidenhead↔coordenadas, cálculo DXCC contra `cty.dat`, distancia y azimut de gran círculo.
- **ADIF**: ida y vuelta con ficheros reales exportados de Log4OM, incluidos los sucios.
- **Modos**: decodificar los WAV de referencia de `ft8_lib` y comparar la lista de mensajes con la esperada. Esto es una prueba automática de regresión del módem.
- **Integraciones**: tramas UDP capturadas de WSJT-X y JTDX guardadas como ficheros binarios de prueba (*fixtures*), decodificadas sin red.
- **Arquitectura**: una prueba que falla si `Ui` referencia `Datos`, o si `Dominio` referencia cualquier cosa.

---

## 4. Integraciones

Principio común: **cada integración es un `IHostedService` con su propio bucle, aislado tras un puerto, que nunca puede tumbar la aplicación**. Un cluster que se cae, un WSJT-X que envía basura o un equipo desconectado producen un aviso en la barra de estado, no una excepción no controlada.

### 4.1 Control CAT: **OmniRig primero, rigctld como alternativa**

Hechos verificados:
- **OmniRig 1.20 está instalado** en `C:\Program Files (x86)\Afreet\OmniRig\`. Es un componente **COM de 32 bits**.
- **Hamlib 4.6.2** (compilación de 32 bits, 09-02-2025) viene con Log4OM en `%AppData%\Log4OM2\hamlib\`, con `rigctld.exe`, `rigctl.exe`, `rigctlcom.exe` y `libhamlib-4.dll`.

**Recomendación: OmniRig como camino principal, `rigctld` por TCP como alternativa, serie directo sólo para casos concretos.**

| Vía | Cómo | Pros | Contras |
|---|---|---|---|
| **OmniRig (COM)** ✅ | COM interop desde .NET 8 (`ComImport` o `dynamic`) | Ya está instalado y configurado en el PC. Perfiles de equipo en `.ini` mantenidos por la comunidad. **Arbitra el puerto serie entre varias aplicaciones**, que es el problema real cuando el cuaderno y un módem quieren el mismo equipo | Componente de **32 bits**: obliga a compilar la app en **x86**, o a aislarlo en un proceso servidor de 32 bits. Abandonado (1.20 es de hace años) |
| **rigctld por TCP** ✅ alternativa | Lanzar `rigctld.exe` como proceso hijo y hablarle por TCP (texto, puerto 4532) | **Sin interop, sin problema de bitness**: es un socket. Hamlib 4.6.2 soporta prácticamente todo equipo existente. Protocolo trivial de probar | Hay que gestionar el ciclo de vida del proceso hijo. Hamlib es **LGPL/GPL**: ejecutarlo como proceso separado es limpio; enlazarlo no lo sería |
| **Serie directo** | `System.IO.Ports` 8.0.0 | Control total, sin dependencias | Hay que implementar el protocolo de cada fabricante (Icom CI-V, Yaesu CAT, Kenwood). Trabajo enorme para poca ganancia |

**Decisión de arquitectura**: definir `IControlEquipo` en `Aplicacion` con `LeerFrecuencia`, `EstablecerFrecuencia`, `LeerModo`, `EstablecerModo`, `ActivarPtt`, `LeerSMetro`, y **tres implementaciones**: `ControlEquipoOmniRig`, `ControlEquipoRigctld`, `ControlEquipoSerie`. El usuario elige en ajustes. Empezar por **rigctld** en el desarrollo (más fácil de probar, sin restricción de arquitectura) y añadir OmniRig como opción.

Existe además **OmniRig.Net** (github.com/w1ve/OmniRig.Net, MIT, activo el 20-10-2025), una reimplementación asíncrona en .NET 8 del motor CAT que **lee los mismos ficheros `.ini` de perfiles de equipo sin necesitar COM**. Resuelve elegantemente el problema de los 32 bits. Riesgo: **0 estrellas, un solo autor** — no apto como única vía, pero sí como cuarta implementación a evaluar, o como referencia de cómo interpretar los `.ini` de OmniRig.

**Sobre el bitness**: si se opta por OmniRig COM, la app entera debe ser x86, lo cual **limita la memoria a ~3,5 GB** y penalizaría el procesado DSP de la sección 6. Es un argumento fuerte a favor de `rigctld`. Si se quiere OmniRig y x64 a la vez, la solución es un **proceso puente de 32 bits** que exponga OmniRig por un canal local (named pipe o TCP). Recomendado sólo si el usuario lo exige.

**PTT**: debe ser un puerto aparte (`IControlPtt`), porque en la práctica se activa por CAT, por RTS/DTR de otro puerto serie, o por VOX. Durante la transmisión digital hace falta PTT con **latencia y determinismo** (ver 6.6).

### 4.2 WSJT-X **y JTDX** por UDP

Esta es una integración de primer nivel: JTDX **no** es un añadido de WSJT-X, sino un objetivo igual de importante.

**Lo verificado en el código fuente de ambos (`NetworkMessage.hpp`):**

| | WSJT-X | JTDX |
|---|---|---|
| `schema_number` | **3** (Qt_5_4) | **3** (Qt_5_4) — el mismo |
| Formato | `QDataStream` binario, **big-endian** | idéntico |
| Cabecera | Magic `0xADBCCBDA`, Schema, MsgType, Id (utf8) | idéntica, pero `Id` vale **`"JTDX"`** en vez de `"WSJT-X"` |
| Tipos 0–12 | Heartbeat, Status, Decode, Clear, Reply, QSOLogged, Close, Replay, HaltTx, FreeText, WSPRDecode, Location, LoggedADIF | **los mismos números y semántica** |
| Tipo 13 | `HighlightCallsign` | **no existe** |
| Tipos 14, 15 | `SwitchConfiguration`, `Configure` | **no existen** |
| Tipos 50, 51 | **no existen** | **`SetTxDeltaFreq` (50)** y **`TriggerCQ` (51)** — extensiones propias de JTDX, incluido "Directional CQ" |
| `Decode` (2) | Id, New, Time, snr, Δt, Δf, Mode, Message, LowConfidence, OffAir | **campos idénticos** |
| `Status` (1) | …, TxWatchdog, SubMode, FastMode, **SpecialOperationMode**, y en versiones recientes más campos al final | …, TxWatchdog, SubMode, FastMode, **TxFirst** — **diverge a partir de `FastMode`** |
| `QSOLogged` (5) | …, OperatorCall, MyCall, MyGrid, **ExchangeSent, ExchangeRcvd** | …, OperatorCall, MyCall, MyGrid — **sin los campos de intercambio de concurso** |
| Puerto UDP secundario | sí | **no** (confirmado en el foro de Log4OM) |

**Diseño del decodificador — cómo tolerar ambos dialectos:**

1. **Detección de dialecto por el campo `Id` de la cabecera**, que es lo primero que se lee tras Magic/Schema/MsgType: `"WSJT-X"`, `"JTDX"`, `"MSHV"`, `"JS8Call"`. Se guarda por remoto (IP:puerto) para que varias instancias simultáneas no se confundan entre sí.
2. **Lectura posicional tolerante**: los mensajes `QDataStream` son una secuencia de campos sin delimitadores. La regla es leer los campos **mientras queden bytes en el datagrama** y parar en cuanto se agote, en vez de exigir una longitud fija. Todo campo que vaya después del último campo común (p. ej. `SpecialOperationMode` o `TxFirst` en `Status`) se trata como **opcional**. Así, una versión futura de WSJT-X que añada campos al final no rompe nada, y JTDX tampoco.
3. **Mensajes desconocidos**: cualquier `MsgType` no reconocido (50, 51 de JTDX, o uno nuevo) se **descarta con una traza de depuración**, nunca con excepción.
4. **Capacidades por dialecto**: el puerto `IModoDigitalExterno` expone `PuedeResaltarIndicativo`, `PuedeCambiarConfiguracion`, `PuedeDispararCq`. `ClienteWsjtx` responde `true` a los dos primeros; `ClienteJtdx` responde `true` al tercero. La UI **desactiva** el botón correspondiente en vez de fallar en ejecución. Es la traducción práctica de "JTDX no ofrece todas las funciones de WSJT-X por limitaciones de la API".
5. **Cadenas nulas**: en `QDataStream`, una cadena vacía y una nula se codifican distinto (`0x00000000` vs `0xFFFFFFFF`). Hay que distinguirlas o se corrompe la lectura posicional de todo lo que viene detrás. Es el error clásico de las implementaciones caseras.
6. **Pruebas**: capturar datagramas reales de WSJT-X y de JTDX, guardarlos como ficheros binarios en `tests/fixtures/udp/`, y probar el decodificador sin red.

**MSHV** (Christo LZ1HV): envía tramas WSJT-X UDP con `Id = "MSHV"`. Es el mismo código, con la particularidad de que en modo multi-flujo (varios QSO en paralelo en la misma ranura) emite **varios `Decode` y varios `QSOLogged` por ciclo**. El registrador debe ser idempotente y no asumir "un QSO por ranura". **Sí conviene soportarlo con el mismo código**: coste ≈ 0.

**JS8Call**: **caso distinto**. Su API nativa es **JSON sobre UDP y TCP**, no el binario `QDataStream`. Pero JS8Call **añadió emisión del protocolo UDP de WSJT-X** precisamente para integrarse con cuadernos. Recomendación: **usar la vía WSJT-X** (funciona con el mismo decodificador, coste ≈ 0) y dejar el cliente JSON nativo para una fase posterior si se quiere manejar el tráfico de mensajería de JS8, que es su razón de ser y no cabe en el protocolo de WSJT-X.

**Aviso sobre el estado de JTDX**: el repositorio `jtdx-project/jtdx` no recibe cambios desde **abril de 2024**, y la versión 2.2.160 lleva años en estado de *release candidate* (rc7 a principios de 2025). Existe un fork activo, **"JTDX Improved"** en SourceForge. Consecuencia para nosotros: el dialecto JTDX es hoy un **objetivo estable** — no va a moverse —, lo cual es una buena noticia para la compatibilidad, y una mala para la sección 6 (ver 6.3).

### 4.3 FLDigi por XML-RPC

FLDigi expone un servidor XML-RPC en `http://127.0.0.1:7362/RPC2` con métodos como `main.get_frequency`, `modem.get_name`, `rx.get_data`, `text.add_tx`, `main.tx`, `main.rx`.

Log4OM usa `CookComputing.XmlRpcV2`, que es de .NET Framework y **está abandonado**: no sirve.

**Recomendación: implementarlo a mano.** XML-RPC es trivial (un POST con un XML de `<methodCall>` y un `<methodResponse>` de vuelta) y sólo se usan una docena de métodos. Unas 200 líneas con `HttpClient` y `XDocument`, sin dependencias, sin riesgo de librería muerta, y totalmente probable con un servidor falso. Es claramente mejor que arrastrar una librería XML-RPC genérica.

### 4.4 N1MM+ por UDP

N1MM Logger+ difunde **XML en texto plano** por UDP (por defecto al 12060): `<contactinfo>`, `<contactreplace>`, `<contactdelete>`, `<RadioInfo>`, `<spot>`. Nada de binario.

Implementación: `UdpClient` + deserialización con `XmlSerializer` o `XDocument`, con la misma tolerancia que en 4.2 (elementos desconocidos ignorados). Es la integración más sencilla de todas. Nota práctica: N1MM puede emitir a **varias** direcciones; conviene documentar al usuario cómo configurarlo, porque es la causa habitual de "no me llega nada".

### 4.5 Cluster DX por Telnet

Sin librería: `TcpClient` + `NetworkStream` + lectura por líneas. El protocolo es texto conversacional (login con indicativo, comandos `sh/dx`, `set/filter`, líneas `DX de ...`).

Puntos que hay que resolver bien y que son la mitad del trabajo real:
- **Codificación**: muchos nodos siguen emitiendo Latin-1 o mezclan. Leer bytes y decodificar con `Latin1` por defecto, con detección de UTF-8 válido.
- **Reconexión automática** con retardo exponencial y tope; el usuario debe ver el estado ("Reconectando en 30 s") en la barra de estado.
- **Tolerancia a formatos**: cada nodo formatea `DX de` ligeramente distinto. Un analizador por expresión regular **con plan B** (si no casa, se muestra la línea cruda en la consola en vez de descartarla).
- **Consola visible**: el usuario debe poder escribir comandos directamente y ver la respuesta. Log4OM dedica un ensamblado de 9,1 MB a esto por algo.
- **Guiones de conexión** (`clusterdefaultscript.txt` en el original): lista de comandos que se envían tras el login.
- **Enriquecido del spot**: al llegar un spot, resolver DXCC, comprobar "trabajado antes", calcular si es nuevo para diploma, y colorearlo. Ese cálculo debe ser **en memoria y sub-milisegundo** (ver R3), no una consulta a la base por cada spot.

### 4.6 Servicios de QSL

| Servicio | Subida | Descarga | Notas verificadas |
|---|---|---|---|
| **LoTW** | Lanzar **`tqsl.exe`** como proceso con argumentos y leer su stderr | HTTPS `lotw.arrl.org/lotwuser/lotwreport.adi` con usuario/contraseña y parámetros de fecha | Ver abajo |
| **eQSL** | POST multipart del ADIF a `eqsl.cc/qslcard/ImportADIF.cfm` | Descarga de ADIF de QSLs recibidas + imagen de la tarjeta | Sencillo, sin firma |
| **ClubLog** | API REST con clave de aplicación: subida ADIF, borrado, QSO individual | Consultas de DXCC, matriz de diplomas | Requiere solicitar clave de API a ClubLog |
| **QRZ.com** | **Logbook API** (REST, POST `name=value`, QSO en ADIF en el parámetro `ADIF`) | **XML Interface** para datos de indicativo | **Ambos requieren suscripción de pago a nivel XML o superior**. Verificado en la documentación de QRZ |

**LoTW — la decisión importante: no reimplementar la firma.** La subida a LoTW exige firmar cada QSO con el certificado X.509 del operador (PKCS#12). Log4OM lleva Bouncy Castle y podría estar haciéndolo por su cuenta. **No lo hagamos.** Razones:
- **TQSL** (Trusted QSL, de la ARRL) tiene **interfaz de línea de comandos documentada y pensada exactamente para esto**: la documentación oficial de la ARRL dice literalmente que las opciones de línea de comandos existen "para permitir que otros programas lancen TQSL para firmar ficheros de QSOs".
- Ejemplo real de la documentación: `tqsl -d -u -a new -x -l "Ubicación" registro.adi 2>resultado.txt` — firma, sube y termina, escribiendo el resultado en stderr.
- Opciones relevantes: `-a` (acción ante diálogos: `abort`/`all`/`compliant`/`ask`), `-l` (ubicación de estación), `-b`/`-e` (rango de fechas), `-d` (omitir duplicados), `-u` (subir), `-x` (salir al terminar), `-p` (contraseña), `-f ignore|report|update` (qué hacer con discrepancias de QTH).
- Ventaja decisiva: **la gestión de certificados, su renovación anual y las ubicaciones de estación las lleva TQSL**, que es software de la ARRL y se actualiza cuando LoTW cambia. Reimplementarlo significaría perseguir esos cambios para siempre.

Diseño: `ServicioLoTW` genera el ADIF del lote, invoca `tqsl.exe` con `ProcessStartInfo` (sin ventana, con redirección), **analiza stderr** para distinguir éxito de error, y marca los QSOs como enviados sólo si TQSL confirma. La ruta de `tqsl.exe` es configurable y se detecta automáticamente en `Program Files`.

**Descarga de LoTW**: HTTPS con `HttpClient`, ADIF de vuelta, y **almacenar la fecha del último informe descargado** para pedir sólo el incremento. LoTW es lento y limita la frecuencia de peticiones: hay que hacerlo en segundo plano, con Polly, y nunca bloquear la UI.

**Principio común a los cuatro servicios**: `IServicioQsl` con `SubirAsync(IEnumerable<Qso>, CancellationToken)` y `DescargarConfirmacionesAsync(DateTime desde, CancellationToken)`. **Siempre en lote, siempre cancelable, siempre con una cola persistente** — si la app se cierra a mitad de una subida de 20.000 QSOs, al volver a abrir debe continuar donde estaba y no duplicar.

### 4.7 Propagación: VOACAP

Log4OM empaqueta un motor nativo propio (`voa4om_native.dll`, 16 MB, con `Voacap4OM.Engine.Interface.dll` como interop). Nosotros **no** podemos usar ese binario.

**Recomendación: invocar el motor original como proceso externo.**
- El motor **VOACAP de NTIA/ITS** es **obra del gobierno de EE. UU. distribuida como freeware**, y funciona **desde línea de comandos sin interfaz gráfica**.
- En Windows el ejecutable es **`voacapw.exe`**, con la sintaxis `voacapw <directorio> <entrada> <salida>`.
- En Linux existe el port **`voacapl`** (jawatson/voacapl), con sintaxis `voacapl ~/itshfbc entrada.dat salida.out`.
- Requiere la estructura de directorios **`itshfbc`** con sus ficheros de datos (coeficientes ionosféricos, antenas); los ficheros de entrada y salida van en `itshfbc/run`.

Diseño: `MotorVoacap` implementa `IPrediccionPropagacion`; genera el fichero `.dat` de entrada (formato de columnas fijas de Fortran, delicado), ejecuta el proceso, analiza la salida y devuelve una matriz hora×frecuencia con fiabilidad y SNR. **Toda la fealdad del formato Fortran queda encerrada en una clase con sus propias pruebas.**

Alternativa más barata para la versión 1: consumir predicciones ya calculadas de servicios web (voacap.com ofrece consultas en línea) y dejar el motor local para una fase posterior. Más simple, pero depende de Internet.

Complementos que son mucho más baratos que VOACAP y dan casi tanto valor percibido: **índices solares** (SFI, A, K, viento solar) desde hamqsl.com o NOAA, **línea gris** (calculable con trigonometría, sin dependencias), y **distancia/azimut de gran círculo**. Hacer esto primero.

---

## 5. Modos digitales **dentro** de la aplicación

Esta sección responde al cambio de alcance: el módem FT8/FT4 (captura de audio, decodificación, transmisión, automatización del QSO y waterfall) debe formar parte de Cuaderno NODISLA, no ser un programa aparte.

**Hay que decirlo con claridad antes de entrar en detalle: esto convierte el proyecto en dos proyectos.** Un cuaderno de guardia es un problema de datos y de interfaz; un módem FT8 es un problema de **procesado digital de señal en tiempo real** con corrección de errores. Son disciplinas distintas, y el segundo es, con diferencia, el más difícil y el que más puede arruinar la percepción del conjunto: un cuaderno que registra mal un QSO molesta; un módem que decodifica un 20 % menos que JTDX **se nota en cada ciclo de 15 segundos** y hace que el usuario vuelva a JTDX.

Por eso la recomendación final (5.4) es **la vía 3, por fases**, y por eso la arquitectura de la sección 2 pone el módem detrás del mismo puerto que el adaptador UDP.

### 5.1 Vía 1 — Módem propio en .NET

**Qué existe, verificado hoy:**

| Proyecto | Licencia | Estado | Qué aporta |
|---|---|---|---|
| **kgoba/ft8_lib** (Kārlis Goba, YL3JG) | **MIT** (verificado en el fichero LICENSE) | 299 estrellas, **último commit 24-08-2025** ("non-standard callsigns; special CQ"), **no archivado** pero con ritmo lento (commits en 2022, 2023, 2025) | C puro, **sin dependencias externas** (lleva kissfft dentro). **Codifica y decodifica FT8 y FT4**. Sincronización por matriz de Costas (búsqueda gruesa y fina), LDPC(174,91), CRC, empaquetado/desempaquetado de mensajes, clase Waterfall, búsqueda de candidatos. Incluye aplicaciones de consola que decodifican un WAV de 15 s |
| **rtmrtmrtmrtm/ft8mon** (Robert Morris, AB1HL) | **MIT** (LICENSE.txt) | 34 estrellas, **último push 19-11-2021** | C++ con FFTW. **Calidad superior**: incluye **OSD** (Ordered Statistics Decoding), sustracción espectral iterativa y decodificación multipasada. **SDRangel lo integró** y su documentación afirma que "hace al decodificador equiparable o incluso algo mejor que el código original de WSJT-X", y que con 3 hilos da resultados "comparables si no mejores" que WSJT-X |
| **SOTAmat/ft8_lib_csharp** (AB6D) | **MIT** | **1 estrella**, último push 27-04-2025, un solo autor | Port **a C#** de ft8_lib: `Ft8Lib.Ft8` (mensajes, LDPC, CRC, modulación FSK), `Ft8Lib.Common` (WAV), demos de generación y decodificación. Existe, compila, y hay `IMPLEMENTATION_NOTES.md` |

**La diferencia que importa: ft8_lib vs. ft8mon.** `ft8_lib` está diseñado para microcontroladores — "eficiencia de memoria y cómputo", probado en STM32F7. Eso significa que **sacrifica sensibilidad por recursos**. En concreto: `ft8_lib` hace LDPC con propagación de creencias, pero **no tiene OSD**, que es justamente el bloque que WSJT-X usa para rescatar las tramas más débiles. En un PC no hay ninguna razón para aceptar esa pérdida. **Si se hace módem propio, la referencia de calidad es ft8mon, no ft8_lib.** El problema es que ft8mon lleva **cinco años sin tocarse** y depende de FFTW (GPL/LGPL con opción comercial) y PortAudio.

**P/Invoke a DLL nativa vs. port puro a C#:**

| | P/Invoke a DLL nativa | Port puro a C# |
|---|---|---|
| Esfuerzo inicial | **Bajo-medio**: compilar `ft8_lib` con MSVC o MinGW a `ft8.dll` x64, escribir ~10 firmas `DllImport` | **Alto**: 4.000–6.000 líneas de C traducidas, con aritmética de punto fijo y manipulación de bits donde un error silencioso cuesta días |
| Calidad | La del C original, intacta | Riesgo alto de introducir errores sutiles que sólo aparecen con señales débiles |
| Rendimiento | Nativo | .NET 8 con `Span<T>`, `System.Numerics.Vector<T>` y punteros en `unsafe` llega al **80–95 %** del C. Suficiente |
| Despliegue | Hay que distribuir la DLL por arquitectura y firmarla. Si la app es x86 por OmniRig, compilar también x86 | Un solo ensamblado gestionado. **Sin problema de bitness.** Depuración completa desde Visual Studio |
| Riesgo operativo | Un fallo en el C **tumba el proceso entero** (`AccessViolation` no se captura). Un módem que cierra el cuaderno a media sesión es inaceptable | Una excepción gestionada se captura y se registra. **Mucho más seguro para una app que debe estar abierta 12 horas** |
| Licencia | MIT: sin problema. Sólo hay que conservar el aviso de copyright | Igual |

**Recomendación dentro de la vía 1**: **P/Invoke a una DLL nativa compilada desde `ft8_lib`, pero ejecutada en un proceso hijo aislado**, no en el proceso de la UI. Así se conserva la calidad del C y un fallo nativo mata sólo al módem, que se reinicia solo. El coste es un canal de comunicación entre procesos (memoria compartida o pipe) para pasar los bloques de audio. Si se quiere evitar esa complejidad, el port C# de SOTAmat es un punto de partida legítimo — pero **hay que validarlo contra los WAV de referencia antes de confiar en él**: un solo autor, una estrella, y ninguna prueba independiente de su sensibilidad.

**Coste de la transmisión (síntesis GFSK)**: esta es **la parte fácil**, y conviene subrayarlo porque cambia la estrategia. Generar FT8 es: empaquetar el mensaje en 77 bits → CRC de 14 bits → LDPC(174,91) → 79 símbolos de 8-FSK (58 de datos + 21 de sincronía Costas en tres bloques) → modulación **GFSK** con un filtro gaussiano (BT = 2,0) para suavizar las transiciones de fase → 12,64 s de audio a 12.000 muestras/s. **Es aritmética directa, sin búsqueda ni estimación**, y `ft8_lib` ya lo hace (y el port C# también). Cabe en menos de un día de trabajo y se verifica trivialmente: se genera un WAV y se decodifica con WSJT-X o con `ft8_lib` para comprobar que sale el mensaje correcto.

Lo difícil es **siempre la recepción**: buscar candidatos en todo el ancho de banda, sincronizar en tiempo y frecuencia con precisión de sub-símbolo, estimar la fase, calcular verosimilitudes blandas por bit, correr LDPC hasta 20 iteraciones, aplicar OSD cuando LDPC no converge, y repetir el proceso restando las señales ya decodificadas para sacar las que quedaban debajo. Ahí es donde WSJT-X lleva ocho años de ajuste fino y donde se pierde la comparación.

**Esfuerzo estimado, vía 1 (sólo FT8/FT4, sin UI):**
- Transmisión completa: **1 semana**
- Recepción vía P/Invoke a `ft8_lib` compilado, con banco de pruebas WAV: **3–4 semanas**
- Recepción con calidad equiparable a JTDX (OSD, multipasada, sustracción): **3–6 meses**, y con resultado incierto
- Port puro a C# validado: **2–3 meses** adicionales al anterior

**Riesgo técnico**: **alto**. **Calidad esperada frente a JTDX**: con `ft8_lib` tal cual, **claramente inferior** en señales débiles (sin OSD). Con el enfoque de ft8mon bien portado, **equiparable**. **Licencia**: **sin problema** — MIT en los tres proyectos.

### 5.2 Vía 2 — Embeber JTDX o WSJT-X como proceso hijo

La idea: lanzar `jtdx.exe` o `wsjtx.exe` oculto o acoplado dentro de una ventana de Cuaderno NODISLA, y controlarlo por UDP, para que el usuario abra **una** aplicación.

**Licencia — el punto crítico, verificado**: **WSJT-X es GPLv3. JTDX es GPL** (su repositorio declara licencia GPL; GitHub la marca como `NOASSERTION` por modificaciones en el aviso). **JS8Call es GPL-3.0** (verificado).

**Lo que la GPL sí permite**: distribuir el binario de WSJT-X/JTDX **junto** a nuestra aplicación, y **ejecutarlo como proceso separado** comunicándose por UDP. Esto es "mera agregación" más comunicación a distancia: no crea una obra derivada, y por tanto **no obliga a publicar Cuaderno NODISLA bajo GPL**. Es exactamente lo que hacen JTAlert, GridTracker y el propio Log4OM. **Condiciones**: hay que distribuir también el código fuente de WSJT-X/JTDX (o una oferta escrita de dónde obtenerlo), conservar sus avisos de copyright y licencia, y **no modificar sus binarios**.

**Lo que la GPL no permite**: enlazar su código en nuestro proceso (P/Invoke a una DLL derivada de WSJT-X, por ejemplo) sin hacer GPL toda la aplicación.

**Limitaciones prácticas, que son el verdadero problema:**

1. **Ventana propia**. WSJT-X y JTDX son aplicaciones Qt con su propia ventana. Se puede *reparentar* la ventana nativa dentro de un `HwndHost` de WPF con `SetParent`, y funciona... hasta que no: menús que se dibujan fuera, diálogos modales que aparecen detrás, escalado DPI inconsistente entre Qt y WPF, y el redimensionado que se pelea. **Es frágil y se rompe en cada actualización de WSJT-X.** La alternativa honesta es ejecutarlo visible y renunciar a la fusión visual, que es justo lo que el propietario quiere evitar.
2. **Configuración duplicada.** El usuario tendría que configurar el equipo, el audio y su indicativo **dos veces**: una en Cuaderno NODISLA y otra en WSJT-X. Se puede mitigar escribiendo su fichero `.ini`, pero es frágil y hostil.
3. **Conflicto por los recursos.** Los dos procesos querrían el mismo dispositivo de audio y el mismo puerto serie. Aquí OmniRig o `rigctld` dejan de ser una opción y pasan a ser **obligatorios** como árbitro.
4. **Waterfall imposible de integrar.** El waterfall se dibuja dentro de la ventana Qt de WSJT-X. **El protocolo UDP no transporta el espectro**: sólo decodificaciones y estado. Por tanto **es técnicamente imposible mostrar el waterfall de WSJT-X dentro de nuestra UI** sin capturar su ventana o reimplementar el análisis espectral. Si se reimplementa el análisis espectral, ya hace falta capturar el audio uno mismo — y si se captura el audio, se está a medio camino de la vía 1.
5. **JTDX está congelado** (sin commits desde abril de 2024, 2.2.160 en RC permanente). Embeberlo significa embeber software sin mantenimiento.

**Esfuerzo**: **1–2 semanas** para lanzarlo, gestionar su ciclo de vida y hablarle por UDP. **3–5 semanas más** para intentar la fusión visual, con resultado frágil.
**Riesgo técnico**: **bajo** en lo funcional, **alto** en lo visual y en el mantenimiento.
**Calidad frente a JTDX**: **idéntica, porque es JTDX**.
**Licencia**: **viable con condiciones** (distribuir fuentes, no modificar binarios, procesos separados).

### 5.3 Vía 3 — Mixta

**Módem propio para FT8/FT4 dentro de la aplicación, y delegación en WSJT-X/JTDX/FLDigi para todo lo demás.**

Justificación por los hechos:
- FT8 y FT4 son **la abrumadora mayoría** del tráfico digital actual. Resolver esos dos modos resuelve casi todo el uso real.
- **Son justamente los dos que `ft8_lib` cubre** (codificación y decodificación, verificado), con licencia MIT.
- Los demás modos JT (JT65, JT9, MSK144, Q65, WSPR, FST4) tienen **decodificadores mucho más complejos y menos documentados**, y un volumen de uso comparativamente marginal. Reimplementarlos sería meses de trabajo para un puñado de QSOs.
- Los modos de conversación (PSK31, RTTY, CW por sonido) tienen otra naturaleza: son **continuos**, no por ranuras, y FLDigi ya los hace excelentemente y **se controla completamente por XML-RPC** (ver 5.7).

**Esfuerzo**: el de la vía 1 para FT8/FT4 + el de la vía 2 reducido (sin intentar fusión visual) para el resto.
**Riesgo técnico**: **medio**, y —esto es lo importante— **escalonado**: si el módem propio no alcanza la calidad esperada, el usuario sigue teniendo FT8 vía WSJT-X, porque **ambos implementan el mismo puerto `IMotorModoDigital`**. No hay un punto de no retorno.
**Calidad frente a JTDX**: **igual** en los modos delegados; **por determinar** en FT8/FT4, medible objetivamente desde el primer día (ver 5.8).
**Licencia**: sin problema (MIT propio + GPL en procesos separados).

### 5.4 Recomendación

**Vía 3, construida en este orden:**

1. **Primero el adaptador UDP** (sección 4.2). Da FT8 funcionando en la versión 1 con dos semanas de trabajo, y define el puerto `IMotorModoDigital` que todo lo demás implementará.
2. **Después la infraestructura común** (5.5 a 5.9): audio, reloj, PTT, waterfall. **Hace falta en las tres vías**, así que no es trabajo perdido decida lo que se decida después. Además, el waterfall propio ya es una mejora visible sobre no tener ninguno.
3. **Después el transmisor FT8/FT4 propio** (una semana) — barato, verificable, y permite responder a una llamada sin abrir WSJT-X.
4. **Por último el receptor**, con `ft8_lib` por P/Invoke en proceso aislado, midiendo continuamente contra WSJT-X en paralelo sobre el mismo audio (5.8). Si iguala, se convierte en el motor por defecto. Si no, se queda como opción y el adaptador UDP sigue siendo el principal.

Esto entrega valor en cada paso, no tiene punto de no retorno, y el peor caso es "tenemos un waterfall bonito y seguimos usando WSJT-X por debajo", que es exactamente donde se estaría hoy.

### 5.5 Audio en .NET 8 — hace falta en las tres vías

**NAudio**, verificado: versión **3.1.0**, licencia **MIT**, repositorio con actividad el **20-09-2026** (ayer), 6.228 estrellas. Es la elección correcta y no tiene rival serio en .NET.

- `WasapiCapture` / `WasapiLoopbackCapture` para entrada, `WasapiOut` para salida. **Modo compartido** (no exclusivo) para no bloquear el dispositivo a otras aplicaciones.
- FT8 trabaja a **12.000 muestras/s, mono**. Las tarjetas suelen dar 48.000: hay que **diezmar por 4** con filtro anti-alias. `MediaFoundationResampler` de NAudio sirve, pero para una relación entera un filtro FIR propio es más predecible y no depende de códecs del sistema.
- **Nada de bloqueo en el hilo de captura**: los bloques entran en un `Channel<float[]>` sin espera y se procesan en otro hilo. Un `await` mal puesto en el callback de audio produce cortes.
- Alternativas verificadas por si NAudio fallara: `CSCore` 1.2.1.2 (sin actividad reciente), `PortAudioSharp2` 1.0.6 (109 k descargas, wrapper de PortAudio). **Ninguna mejor que NAudio.**
- Log4OM también usa NAudio (`NAudio.Core/Wasapi/WinMM/WinForms`), lo cual confirma la elección.

**Selección de dispositivo**: imprescindible que el usuario pueda elegir entrada y salida independientemente, porque el flujo típico es CODEC USB del equipo (SignaLink, IC-7300 integrado) para radio y altavoces del PC para avisos.

### 5.6 Sincronización de reloj a 15 s / 7,5 s

FT8 transmite en ranuras de **15 s** alineadas al minuto UTC (0, 15, 30, 45), con 12,64 s de señal y ~2,36 s de margen. FT4 usa **7,5 s** (6,0 s de señal). **Un error de más de ~1–2 s arruina la decodificación**, y es la causa número uno de "no decodifico nada".

Diseño:
- **No usar `DateTime.Now`** para la temporización del ciclo. Usar `Stopwatch` (monótono, inmune a saltos de reloj) para medir, y `DateTime.UtcNow` sólo para anclar el inicio de ranura.
- **Medir el desfase del reloj del PC** contra NTP (`pool.ntp.org` o `hora.roa.es`) al arrancar y cada hora, con una consulta SNTP propia (es un datagrama de 48 bytes, no hace falta librería). **Mostrar el desfase en la barra de estado en todo momento**, en segundos con un decimal, y avisar en rojo si supera 1 s. Esto vale oro en soporte.
- **No cambiar el reloj del sistema** (requiere privilegios y es intrusivo): aplicar el desfase como **corrección interna** al calcular los límites de ranura. Es lo que hace JTDX con su ajuste manual, pero automático.
- El `Δt` que reporta cada decodificación es información valiosa: si **todas** las estaciones aparecen con el mismo `Δt` sistemático, el reloj desviado es el nuestro. Se puede usar como realimentación y avisar al usuario.
- Las etiquetas de tiempo de los QSOs registrados siempre en **UTC**, sin excepción, y almacenadas como tales.

### 5.7 PTT y control del equipo durante la transmisión

Secuencia obligatoria de cada transmisión, y el orden importa:
1. Comprobar que **no** hay otra transmisión en curso (candado global).
2. Fijar frecuencia y modo por CAT si hace falta (`USB` o `DATA-U`) y **esperar a que el equipo confirme**.
3. **Activar PTT** y esperar el tiempo de conmutación del equipo (configurable, típicamente 50–200 ms; con amplificador y relés más).
4. Empezar a reproducir el audio.
5. Al terminar el audio, esperar el vaciado del buffer de salida (**`WasapiOut` no termina cuando se acaba de escribir**: hay latencia pendiente) y sólo entonces **soltar PTT**.
6. Registrar la transmisión en el log de depuración con marcas de tiempo.

Puntos críticos:
- **`IControlPtt` separado de `IControlEquipo`**: en la práctica el PTT va por CAT, por RTS/DTR de **otro** puerto serie, o por VOX. Son ciclos de vida distintos.
- **Vigilante (watchdog) obligatorio**: si algo falla, **soltar PTT pasado un tiempo máximo** (p. ej. 20 s para FT8). Un PTT pegado quema la etapa final del equipo o el amplificador. Esto debe estar en un `finally` **y** en un temporizador independiente que no dependa del hilo principal, **y** en el manejador de cierre de la aplicación y de `AppDomain.ProcessExit`. Es el único punto de este documento que puede causar **daño material**.
- **Conflicto con otro software**: si WSJT-X también está abierto, los dos querrán el PTT. Aquí OmniRig o `rigctld` dejan de ser preferencia y pasan a ser obligatorios.
- **Control de potencia**: no ajustar la potencia automáticamente. Es responsabilidad del operador.

### 5.8 Banco de pruebas de calidad del decodificador (no negociable)

Si se hace módem propio, **la comparación con WSJT-X debe ser automática y continua desde el primer día**, no una impresión subjetiva:

- Grabar sesiones reales completas en WAV de 12 kHz (una hora en 20 m un sábado por la tarde = material de sobra).
- Decodificar cada fichero con `jt9.exe`/`wsjtx` y con nuestro motor.
- Producir una métrica: **mensajes decodificados por ciclo**, desglosados por franja de SNR (−24 a −20, −20 a −15, −15 a −10, >−10 dB). **La diferencia siempre está en la franja más débil**, que es donde entra OSD.
- Convertirlo en una prueba automatizada con un umbral: si el motor propio baja del 95 % de WSJT-X en cualquier franja, la prueba falla.
- Los WAV de referencia que trae `ft8_lib` sirven como prueba de regresión rápida en cada compilación.

Sin esta medición no hay forma honesta de decidir si el módem propio está listo, y se acabaría descubriendo el problema en el aire.

### 5.9 Waterfall en WPF

**No es difícil, y no hace falta librería.** El waterfall es una imagen que se desplaza una fila por cada nuevo espectro.

- Calcular la FFT sobre el flujo de audio (ventana Hann, solapamiento del 50 %). Para 12 kHz con resolución de ~6,25 Hz, FFT de 2048 puntos. Usar `MathNet.Numerics` o la FFT de `ft8_lib`.
- Dibujar sobre un **`WriteableBitmap`** de formato `Bgra32`: reservar el buffer una vez, escribir la fila nueva con punteros en un bloque `unsafe`, y **desplazar el resto con `Buffer.MemoryCopy`**. Llamar a `AddDirtyRect` sólo sobre la zona cambiada. Esto va sobrado a 20 fotogramas por segundo sin tocar la GPU.
- **Nunca crear un `Bitmap` nuevo por fotograma** — es el error que hace que el waterfall consuma el 40 % de CPU y llene el montón de objetos grandes.
- Paleta de color configurable, como en WSJT-X, con una tabla de consulta de 256 entradas precalculada.
- Superponer: escala de frecuencia, marcador de frecuencia de recepción, marcador de transmisión, y los mensajes decodificados situados en su frecuencia.
- Si más adelante hacen falta efectos o escalado por hardware, **SkiaSharp.Views.WPF 4.152.1** (MIT, verificado) es el paso siguiente natural, pero **empezar sin él**.

### 5.10 Los otros modos digitales

| Modo | Recomendación | Por qué |
|---|---|---|
| **FT8, FT4** | **Módem propio** (a partir de `ft8_lib`, MIT) | Son el 90 % del uso. `ft8_lib` los cubre. Es lo único que justifica el esfuerzo |
| **JT65, JT9, MSK144, Q65, FST4, WSPR** | **Delegar en WSJT-X/JTDX** por UDP | Decodificadores mucho más complejos, sin implementación libre y limpia equivalente a `ft8_lib`, y con uso marginal frente a FT8 |
| **PSK31, PSK63, RTTY** | **Delegar en FLDigi** por XML-RPC | FLDigi los hace muy bien, es el estándar de facto, y **su XML-RPC permite control completo**: cambiar de modo, leer el texto recibido, poner texto en el buffer de transmisión, activar TX/RX. El usuario ni siquiera tiene por qué ver FLDigi si se le hace una ventana propia que lea `rx.get_data` y escriba en `text.add_tx`. **Esta es la forma barata de "tenerlo dentro"**: la ventana es nuestra, el módem es de FLDigi |
| **CW por sonido (decodificación)** | **Módem propio, prioridad baja** | Un decodificador de CW decente (detección de envolvente, estimación adaptativa de la velocidad, árbol Morse) son ~500 líneas y es divertido, pero ningún operador de CW serio lo usa. Hacerlo si sobra tiempo |
| **CW transmitido (manipulador)** | **Módem propio + WinKeyer** | Generar CW por tarjeta de sonido es trivial con NAudio. Para CW con temporización perfecta, **WinKeyer** por serie (Log4OM dedica `L4ONG.Winkeyer.dll` a esto). Los mensajes de concurso y macros son puro cuaderno, no DSP |
| **JS8Call** | **Delegar**, vía su emisión WSJT-X UDP | Su valor es la mensajería, que no cabe en el protocolo de WSJT-X. Si se quiere de verdad, su API JSON nativa es fácil, pero es una fase posterior |

**La observación estratégica sobre FLDigi**: si el objetivo del propietario es "una sola aplicación", FLDigi lo permite **hoy y barato**. Su XML-RPC expone todo lo necesario para construir una ventana de PSK/RTTY dentro de Cuaderno NODISLA en la que FLDigi funcione oculto. Es la misma idea de la vía 2, pero **sin el problema del waterfall** —porque el waterfall de PSK/RTTY lo podemos dibujar nosotros con nuestra propia FFT del mismo audio— y **sin el problema de licencia**, porque sigue siendo proceso separado. Merece la pena probarlo antes que el módem FT8 propio: enseña toda la mecánica (audio, PTT, ventana de modo digital) con una fracción del riesgo.

---

## 6. Riesgos técnicos, por gravedad

**R1 — PTT pegado: daño material al equipo o al amplificador.** *Gravedad: crítica.*
Un fallo del software que deje el transmisor activado indefinidamente puede destruir la etapa final. **Mitigación**: vigilante independiente que suelte PTT pasado un máximo absoluto (20 s), implementado en un temporizador propio que no dependa del hilo de audio ni del de UI; `try/finally` en toda ruta de transmisión; manejador de `ProcessExit` y de excepción no controlada que suelte PTT antes de cerrar; y una **prueba de integración con equipo simulado** que verifique que PTT se suelta ante excepción, ante cierre y ante bloqueo del hilo de audio. **Esto se implementa antes que la primera transmisión real, no después.**

**R2 — Calidad del decodificador FT8 propio por debajo de JTDX.** *Gravedad: alta.*
`ft8_lib` está optimizado para microcontroladores y **no tiene OSD**, el bloque que WSJT-X usa para las señales más débiles. El resultado previsible es decodificar menos en la franja de −20 a −24 dB, que es precisamente la que el operador de DX valora. **Mitigación**: el banco de medición de 5.8 desde el primer día; mantener el adaptador UDP como motor alternativo permanente detrás del mismo puerto; tomar el enfoque de ft8mon (OSD + multipasada + sustracción espectral) como referencia de calidad, no `ft8_lib` tal cual; y **no retirar WSJT-X como opción hasta que la medición diga que se puede**.

**R3 — Pérdida de datos del cuaderno.** *Gravedad: alta.*
El cuaderno de guardia es un registro legal y sentimental de décadas. Una migración de EF Core mal hecha o un fallo a media importación puede destruirlo. **Mitigación**: copia de seguridad automática del fichero SQLite **antes de cada migración** y diaria, con retención de 30 días en `%AppData%\CuadernoNodisla\copias\`; toda importación en una transacción con vista previa y posibilidad de deshacer; **exportación a ADIF siempre disponible** como formato de salida no propietario; verificación de integridad (`PRAGMA integrity_check`) al arrancar; y una prueba de restauración documentada. El fichero de Log4OM (`Log4OMNG.SQLite`) es intocable: **sólo se lee, jamás se escribe**.

**R4 — Rendimiento de la UI con cuadernos grandes.** *Gravedad: alta.*
El `DataGrid` de WPF es notoriamente lento con decenas de miles de filas si se materializa todo. Con un cuaderno de 100.000 QSOs y filtros activos, la app se vuelve inutilizable. **Mitigación**: virtualización de filas **y de columnas** activada explícitamente (`VirtualizingPanel.IsVirtualizing`, `VirtualizationMode="Recycling"`, `ScrollUnit="Item"`); **paginación y filtrado en SQL, nunca en memoria**; índices desde la primera migración; consultas de informe con Dapper y proyección a `record` sin seguimiento; y una **prueba de rendimiento con un cuaderno sintético de 200.000 QSOs** como puerta de calidad. El cálculo de "trabajado antes" para colorear spots del cluster debe resolverse contra una **estructura en memoria** (conjuntos por banda y modo), no consultando la base por cada spot.

**R5 — Bitness: OmniRig de 32 bits contra un módem DSP que quiere memoria.** *Gravedad: media-alta.*
OmniRig 1.20 es COM de 32 bits; usarlo obliga a compilar en x86 y limita la memoria a ~3,5 GB, justo cuando el módem quiere buffers grandes y varios hilos. **Mitigación**: elegir `rigctld` por TCP como vía principal (sin restricción de arquitectura), compilar en **x64**, y ofrecer OmniRig a través de un **proceso puente de 32 bits** si el usuario lo exige. Evaluar `OmniRig.Net` (MIT, .NET 8, lee los mismos `.ini`) como alternativa, con la reserva de que tiene un solo autor y ninguna adopción.

**R6 — Fragilidad del protocolo UDP ante versiones nuevas.** *Gravedad: media.*
WSJT-X añade campos al final de `Status` en cada versión; JTDX diverge a partir de `FastMode`; MSHV y JS8Call tienen sus propias particularidades. Un decodificador rígido falla con la siguiente versión. **Mitigación**: la lectura posicional tolerante de 4.2 (parar al agotar el datagrama, campos finales opcionales, tipos desconocidos descartados con traza), detección de dialecto por `Id`, negociación de capacidades, y **ficheros binarios de prueba capturados de cada programa real**.

**R7 — Dependencias abandonadas o con licencia envenenada.** *Gravedad: media.*
Log4OM es el aviso: `CookComputing.XmlRpcV2`, `ObjectListView`, `WindowsAPICodePack` y `GMap.NET` están todos abandonados, y EPPlus le obligó a llevar dos ramas. **Mitigación**: ya aplicada en este documento — cada paquete verificado hoy en fecha y licencia; Mapsui (mayo 2026) en vez de GMap.NET (junio 2022); ClosedXML (MIT) en vez de EPPlus (Polyform); AwesomeAssertions (Apache-2.0) en vez de FluentAssertions (comercial desde la v8); XML-RPC implementado a mano en vez de librería muerta. Además: `Directory.Packages.props` para versión central, y **revisión de dependencias cada seis meses** anotada en `docs/`.

**R8 — Reloj del PC desincronizado.** *Gravedad: media, pero de altísima frecuencia.*
Es la causa número uno de "FT8 no me funciona". **Mitigación**: consulta SNTP propia al arrancar y cada hora, **desfase siempre visible en la barra de estado**, aviso destacado por encima de 1 s, corrección interna sin tocar el reloj del sistema, y análisis del `Δt` sistemático de las decodificaciones como confirmación.

**R9 — Alcance desbordado.** *Gravedad: media, y muy probable.*
Log4OM NextGen son ~29 MB de ejecutable y más de 30 ensamblados propios, fruto de más de una década. "Paridad completa" más un módem DSP propio es, realistamente, **varios años-persona**. **Mitigación**: el orden por fases de 2.3 y 5.4; una versión 1 que sea **un cuaderno excelente** (registrar, importar/exportar ADIF, buscar, DXCC, LoTW, cluster, CAT) antes que un cuaderno mediocre con un módem a medias; y medir el éxito por "¿ha dejado EA8DLF de abrir Log4OM?", no por número de funciones.

**R10 — Licencia GPL mal aplicada al embeber WSJT-X/JTDX.** *Gravedad: baja si se respeta, alta si no.*
**Mitigación**: procesos **siempre separados**, comunicación **sólo** por UDP/TCP, binarios **sin modificar**, avisos de copyright y licencia conservados, y una oferta de código fuente incluida en la documentación. **Nunca** enlazar código derivado de WSJT-X/JTDX en nuestro proceso.

**R11 — Formato de entrada de VOACAP.** *Gravedad: baja.*
El fichero `.dat` es de columnas fijas estilo Fortran: un espacio de más y el motor falla o, peor, calcula mal en silencio. **Mitigación**: encapsular la generación en una clase con pruebas contra ficheros de entrada conocidos y su salida esperada; validar la salida antes de mostrarla; y dejar VOACAP para una fase tardía, empezando por índices solares y línea gris, que dan el 70 % del valor percibido por el 5 % del esfuerzo.

**R12 — Corrupción de la base por acceso concurrente.** *Gravedad: baja con SQLite bien configurado.*
El oyente UDP, el cluster y la UI escriben a la vez. **Mitigación**: **WAL** activado, un único `DbContext` por unidad de trabajo (nunca compartido entre hilos), escrituras serializadas a través de una cola, `busy_timeout` configurado, y nunca mantener transacciones abiertas mientras se espera a la red.

---

## 7. Estructura de carpetas y convenciones

### 7.1 Carpetas del repositorio

```
CuadernoNodisla/
├── CuadernoNodisla.sln
├── Directory.Build.props          propiedades comunes (LangVersion, Nullable, TFM)
├── Directory.Packages.props       versiones centralizadas de NuGet ← clave para R7
├── .editorconfig                  estilo y analizadores
├── .gitignore
├── ESTADO.md
├── docs/
│   ├── 01-inventario-funcional.md
│   ├── 02-modelo-datos.md
│   ├── 03-arquitectura.md         ← este documento
│   ├── 04-decisiones/             una ficha corta por decisión (ADR) con fecha y motivo
│   └── ayuda/                      material de la ayuda publicada, con capturas
├── src/                           los 14 proyectos de la sección 2
├── tests/                         los proyectos de prueba
│   └── fixtures/
│       ├── adif/                  ficheros ADIF reales, incluidos los sucios
│       ├── udp/                   datagramas capturados de WSJT-X, JTDX, MSHV, N1MM
│       └── audio/                 WAV de referencia FT8/FT4 para regresión del módem
├── nativo/                        fuentes C de ft8_lib + script de compilación de la DLL
├── herramientas/                  utilidades de desarrollo (generador de cuaderno sintético,
│                                  comparador de decodificadores, capturador de UDP)
├── recursos/                      cty.dat, bandplan, plantillas de diploma, iconos, sonidos
└── artefactos/                    salida de compilación y publicación (ignorada por git)
```

### 7.2 Datos del usuario en tiempo de ejecución

```
%AppData%\CuadernoNodisla\
├── cuaderno.sqlite            base principal (+ -wal, -shm)
├── ajustes.json               configuración del usuario
├── credenciales.dat           secretos cifrados con DPAPI
├── copias\                    copias automáticas, retención 30 días
├── registros\                 Serilog, rotación diaria
├── mapas\                     caché de teselas
├── externos\                  cty.dat, clublog.xml, listas de referencias
└── perfiles\                  perfiles de estación y de concurso
```

**Nunca escribir junto al ejecutable**: en `Program Files` no hay permiso, y rompe la actualización.

### 7.3 Idioma: **español en el código propio, inglés solo donde es normativo**

> **Corregido el 21-09-2026 por el team lead.** La redacción anterior recomendaba el código
> íntegramente en inglés y fijaba un glosario que contradecía lo que después se decidió y se
> construyó. Manda lo que dice esta versión.

**Regla: identificadores y comentarios del código propio en español; los nombres de campo ADIF,
en inglés y literales; todo lo visible por el operador, en español correcto y con tildes.**

**Por qué el código propio en español**: lo escribe y lo mantiene Jose, y el proyecto es suyo.
`RegistrarQso`, `TrabajadoAntesAsync`, `ResolutorDxcc` o `EscritorAdif` se leen igual de bien que
su equivalente inglés y no obligan a traducir mentalmente al hablar del programa. Los
identificadores van **sin tildes ni eñes** (`Estacion`, no `Estación`; `ResolucionAnio`, no
`ResoluciónAño`) para no depender de la codificación de ninguna herramienta.

**Por qué los campos ADIF se quedan en inglés**: `CALL`, `QSO_DATE`, `TIME_ON`, `RST_SENT`,
`GRIDSQUARE` son **normativos**. Si las propiedades se llaman igual que el campo, el mapeo es
trivial y se puede leer la especificación al lado del código; si se traducen, hace falta un
diccionario que se desincroniza con cada revisión del estándar. Por eso `Qso` tiene `Call`,
`Band`, `Freq` y `RstSent`, y no se traducen. La misma excepción vale para lo que venga impuesto
por un protocolo ajeno (WSJT-X, Hamlib, `ft8_lib`).

**Espacios de nombres en español** (`Nodisla.Cuaderno.Integraciones`, `Nodisla.Cuaderno.Propagacion`),
por identidad de producto.

**Qué va obligatoriamente en español:**
- **Toda cadena que el operador pueda ver**: etiquetas, botones, menús, títulos, encabezados de
  columna, ayudas emergentes, mensajes de error, confirmaciones, barra de estado, informes, ayuda.
- **Los comentarios del código y los documentos de `docs/`**.
- **Los mensajes de los commits**.

**Dónde viven los textos de la interfaz**: hoy, directamente en el XAML. La recomendación anterior
de meterlos en `Textos.resx` se descarta mientras solo haya un idioma: añade una indirección que
hay que mantener a cambio de un beneficio que nadie ha pedido. Si alguna vez se quiere una segunda
lengua, se extraen entonces, que es trabajo mecánico.

**Formatos numéricos — la trampa de la cultura española.** La aplicación corre en `es-ES`, así que
los millares llevan punto y los decimales coma: **20.000 contactos**. Con dos excepciones
deliberadas, que se escriben y se muestran **siempre con punto decimal**, en cultura invariante:

- **La frecuencia** (`14.200 MHz`). Es la convención de la radio, la del dial del equipo, la de
  ADIF y la de todos los cuadernos del mundo. Escribirla `14,200` sería ilegible para un operador
  y ambiguo al exportar.
- **Los informes en decibelios** de FT8 y similares (`-15`, `+03`).

Al teclear, los dos campos aceptan también la coma: quien escribe `14,200` quiere decir `14.200`.
Esta regla está fijada con pruebas a propósito, porque es de las que alguien «corrige» más
adelante sin conocer el motivo.

**Terminología de la interfaz — glosario fijo, para no decir lo mismo de dos maneras:**

| Concepto | Término en la interfaz | **No** usar |
|---|---|---|
| Callsign | **Indicativo** | Distintivo, señal |
| Logbook | **Cuaderno** (o *cuaderno de guardia* al presentarlo) | Diario, bitácora, log |
| QSO / contact | **Contacto** en textos corridos; **QSO** cuando hace falta el término técnico | — |
| Band | **Banda** | — |
| Mode | **Modo** | Modalidad |
| Grid square | **Localizador** | Cuadrícula, rejilla, locator |
| Report (RST) | **Informe** | Reporte, señal |
| Spot | **Spot** | Anuncio, aviso |
| Rig / transceiver | **Equipo** | Radio, transceptor, rig |
| Rig control (CAT) | **Control del equipo (CAT)** | — |
| Award | **Diploma** | Premio, galardón |
| Worked before | **Trabajado antes** | Ya contactado |
| Upload / Download | **Subir** / **Descargar** | Cargar, bajar |
| Settings | **Ajustes** | Configuración, preferencias, opciones |
| Waterfall | **Cascada** | Waterfall |

**Nombres de fichero y rutas**: sin tildes ni eñes en los ficheros del repositorio
(`03-arquitectura.md`), para evitar problemas de codificación entre herramientas y en git. Los
ficheros que genera el usuario sí pueden llevar acentos.

### 7.4 Convenciones de código

- **`Nullable` activado** en toda la solución y **avisos como errores** en `Dominio` y `Aplicacion`. Un cuaderno está lleno de campos opcionales; el compilador debe obligar a tratarlos.
- **`record` para objetos de valor y DTO**, clases para entidades con identidad.
- **Todo lo asíncrono lleva `CancellationToken`** y sufijo `Async`. Sin excepciones: cada integración se debe poder cancelar al cerrar la app.
- **Nada de `async void`** salvo manejadores de evento, y ésos con `try/catch` completo.
- **Fechas siempre `DateTimeOffset` en UTC** dentro del dominio; la conversión a hora local sólo en la capa de presentación.
- **Un `.editorconfig`** con los analizadores de .NET activados y las reglas de estilo fijadas, para que no haya discusiones de formato.
- **Prueba de arquitectura** automática que falla si `Ui` referencia `Datos`/`Servicios`/`Integraciones` o si `Dominio` referencia algo.
- **Commits en español**, en imperativo y concretos: "Añade decodificador UDP tolerante a dialecto JTDX".
- **Una ficha de decisión (ADR) corta en `docs/04-decisiones/`** cada vez que se elija entre dos caminos. Dentro de seis meses nadie recordará por qué se descartó EPPlus, y estará en un fichero de quince líneas.

---

## 8. Resumen del stack elegido

1. **.NET 8 + WPF con MVVM** (CommunityToolkit.Mvvm 8.4.2, MIT) sobre el **Generic Host** de `Microsoft.Extensions` **rama 8.0.x** (fijada con `Directory.Packages.props`), con arquitectura en capas donde sólo `Ui` y `Ui.Mapa` conocen WPF y cada integración vive detrás de un puerto de la capa `Aplicacion`.
2. **Datos**: EF Core 8.0.31 + `Microsoft.Data.Sqlite` para escritura, modelo y migraciones, y **Dapper** 2.1.86 para las consultas de informe; un único fichero SQLite en `%AppData%` con WAL, copia automática antes de cada migración y exportación ADIF siempre disponible.
3. **Apoyo**: Serilog 4.4.0 (logging estructurado con visor dentro de la app), configuración en JSON con `IOptions` y secretos cifrados con DPAPI, **Mapsui.Wpf 5.1.0** para mapas (no GMap.NET, sin publicar desde 2022), ScottPlot 5 para gráficas, **ClosedXML** para Excel (no EPPlus, licencia Polyform), QuestPDF para PDF, y pruebas con xUnit v3 + **AwesomeAssertions** (no FluentAssertions, comercial desde la v8).
4. **Integraciones**: CAT por **`rigctld` de Hamlib 4.6.2 por TCP** como vía principal (evita la atadura a x86 de OmniRig, que queda como segunda implementación del mismo puerto), **LoTW delegado en `tqsl.exe` por línea de comandos** en lugar de reimplementar la firma, XML-RPC de FLDigi escrito a mano (~200 líneas, sin librería muerta), N1MM+ por XML sobre UDP, cluster por Telnet propio, y VOACAP como proceso externo en una fase tardía.
5. **Modos digitales**: decodificador UDP **único para WSJT-X, JTDX, MSHV y JS8Call**, con detección de dialecto por el campo `Id`, lectura posicional tolerante (campos finales opcionales, tipos 13/14/15 de WSJT-X y 50/51 de JTDX tratados como capacidades opcionales) y negociación de capacidades; y **vía mixta por fases** para el módem propio — primero UDP, después la infraestructura común (NAudio 3.1.0, sincronización SNTP, PTT con vigilante contra PTT pegado, waterfall propio con `WriteableBitmap`), luego el transmisor FT8/FT4 (una semana), y sólo al final el receptor basado en **`ft8_lib` (MIT) por P/Invoke en proceso aislado**, medido de forma continua contra WSJT-X por franjas de SNR y adoptado como motor por defecto únicamente si iguala la medición.
