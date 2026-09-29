# Cuaderno NODISLA — Plan maestro

Clon en español de **Log4OM NextGen 2.40.0.0**, para uso particular de EA8DLF.
Team lead: sesión de Claude Code. Fecha de arranque: **2026-09-21**.

---

## 1. Decisiones cerradas

| Asunto | Decisión | Fecha |
|---|---|---|
| Tecnología | **C# .NET 8 + WPF**, escritorio Windows x64 | 21-09-2026 |
| Alcance | **Paridad completa** con Log4OM, construida por fases | 21-09-2026 |
| Servicios externos | LoTW, QRZ.com, ClubLog, eQSL, cluster DX, WSJT-X **y JTDX** | 21-09-2026 |
| Modos digitales | **Módem propio integrado** (FT8/FT4 dentro de la app, no dos programas) | 21-09-2026 |
| Momento del módem | **Después del núcleo del cuaderno** | 21-09-2026 |
| Modo de trabajo | Team lead + especialistas; planificar y construir la v1 | 21-09-2026 |
| Base de datos | **SQLite** un solo fichero. MySQL/MariaDB no se replica | 21-09-2026 |
| Idioma | Código e identificadores en inglés técnico; **todo lo visible, en español** | 21-09-2026 |

### Sobre el módem propio

El operador eligió la **vía 1** (decodificador propio de verdad) frente a la mixta. Se respeta.
Matiz de ingeniería que **no cambia la decisión** y no cuesta nada: el módem se construye detrás
del puerto `IMotorModoDigital`. Eso permite que, mientras el decodificador propio se afina,
exista un segundo implementador del mismo puerto (UDP contra JTDX) sin que la aplicación se entere.
El objetivo declarado sigue siendo el módem propio; el puerto es sólo una red de seguridad gratis.

---

## 2. Lo que hay que replicar

27 módulos funcionales detectados en el original (detalle en `01-inventario-funcional.md`):

**Muy alta complejidad (4)** — diplomas, clúster DX, propagación VOACAP, base de datos.
**Alta (7)** — CAT, integración de red UDP/TCP, ADIF, QSL/servicios web, mapas, estadísticas, concursos.
**Media-alta (7)** — entrada de QSO, búsqueda de indicativos, worked-before, satélites, etiquetas e
impresión, scheduler, copias de seguridad.
**Media/baja (8)** — keyer CW, entrenador CW, bandplan, net control, macros, temas, actualizaciones, ayuda.
**Descartado (1)** — chat OAMS, retirado por el propio Log4OM.

---

## 3. Fases

### Fase 0 — Reconocimiento ✅ COMPLETADA
Inventario funcional, modelo de datos y arquitectura. Tres informes en `docs/`.

### Fase 1 — Núcleo del cuaderno (en curso)
Objetivo: **una aplicación que ya sirve para loguear**, aunque le falte todo lo demás.

1. Esqueleto de solución, gestión central de paquetes, CI local de compilación y pruebas.
2. `Dominio`: `Qso`, `Estacion`, objetos de valor (`Indicativo`, `Locator`, `Frecuencia`, `Banda`,
   `Modo`, `Informe`), resolución **DXCC desde `cty.dat`** con ventanas temporales.
3. `Datos`: EF Core 8 + SQLite, migración inicial con el esquema de 9 tablas + FTS5, WAL, índices.
4. `Adif`: lector y escritor ADI/ADX tolerante a ficheros reales sucios.
5. `Aplicacion`: casos de uso y **puertos** (`IRepositorioQso`, `IControlEquipo`, `IFuenteSpots`,
   `IServicioQsl`, `IConsultaIndicativo`, `IMotorModoDigital`, `IPrediccionPropagacion`).
6. `Ui`: ventana principal, entrada de QSO, rejilla del log con filtros, edición, estadísticas básicas.
7. **Importar los 1.838 QSOs reales** del backup ADIF de Log4OM y cuadrar cifras contra el original.

Criterio de aceptación: El operador loguea un QSO, lo edita, lo busca, exporta ADIF y el fichero se
reimporta en Log4OM sin pérdida de datos.

### Fase 2 — Operar
`Radio` (CAT y PTT), `Integraciones` (cluster DX por Telnet, UDP de WSJT-X/JTDX/N1MM, XML-RPC de
FLDigi), `Ui.Mapa`, worked-before en tiempo real, bandplan.

### Fase 3 — Servicios y diplomas
`Servicios` (LoTW vía `tqsl.exe`, eQSL, ClubLog, QRZ.com), `Propagacion` (VOACAP e índices solares),
motor de diplomas con las 553.064 referencias, etiquetas e impresión de QSL, estadísticas completas.

### Fase 4 — Módem digital propio
`Audio` (WASAPI/NAudio), sincronización de reloj, PTT con vigilante, waterfall, transmisión GFSK,
y el decodificador FT8/FT4. Banco de pruebas por franjas de SNR contra WSJT-X **obligatorio**
antes de darlo por bueno.

### Fase 5 — Resto
Concursos, satélites, keyer y entrenador CW, net control, macros, scheduler, temas, ayuda en español
con capturas, actualizador.

---

## 4. Riesgos vivos

| # | Riesgo | Gravedad | Mitigación |
|---|---|---|---|
| R1 | **PTT pegado quema la etapa final o el amplificador** | Daño material | Vigilante que suelta PTT ante excepción, cierre o bloqueo del hilo de audio. Implementado **antes** de la primera transmisión real |
| R2 | El decodificador FT8 propio no alcanza la calidad de JTDX | Alta | `ft8_lib` no lleva OSD; banco de medición por SNR y P/Invoke en proceso hijo aislado para que un fallo nativo no tumbe el cuaderno |
| R3 | Paquetes NuGet saltando a la rama 10.x (exige net10) | Media | `Directory.Packages.props` fijando la rama 8.0.x |
| R4 | Pérdida de datos del cuaderno | Alta | SQLite en WAL, copia automática antes de cada migración e importación, y export ADIF verificable |
| R5 | El motor de diplomas es el módulo más pesado del original | Media | Base de referencia separada con `ATTACH`, `AllowedDXCC` normalizado, progreso cacheado |

### Nota de seguridad
El `config.ini` de Log4OM guarda **una API key de ClubLog en claro**. En Cuaderno NODISLA las
credenciales van cifradas con **DPAPI de usuario** y nunca en el fichero de configuración legible.

---

## 5. Estado

Ver `../ESTADO.md` para el estado al día.
