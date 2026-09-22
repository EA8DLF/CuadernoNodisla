# Cuaderno NODISLA

Clon en español de Log4OM NextGen 2.40.0.0, para uso particular de EA8DLF.

## Estado — 22-09-2026

**Fase 1 (núcleo del cuaderno) completada.**
**Fase 2 (operar) completada**: control nativo del FT-710, vigilante de PTT, cluster de DX,
bandplan, FLDigi, puente con WSJT-X y JTDX, mapa y cabina de operación.
**Fase 3 (servicios y diplomas) en curso**: servicios de QSL y propagación cerrados; falta el
motor de diplomas.

### Cifras

| Capa | Pruebas |
|---|---|
| Dominio y DXCC | 550 |
| Integraciones | 169 |
| Aplicación | 152 |
| Servicios | 98 |
| Propagación | 95 |
| Radio | 90 |
| ADIF | 87 |
| Datos | 53 |

Compilación de la solución entera: **0 avisos, 0 errores**.

### Decisiones de la Fase 3

- **Credenciales cifradas con DPAPI.** El `config.ini` del original guarda una clave de ClubLog
  en claro; aquí no.
- **LoTW descarga con `qso_qslsince`**, no `qso_qsorxsince`: con el segundo, un contacto subido
  hace años y confirmado ayer no aparecería nunca.
- **ClubLog no ofrece descarga de confirmaciones** y se dice, en vez de devolver vacío fingiendo.
- **Las confirmaciones sin pareja se devuelven al operador**: suelen significar que el cuaderno
  tiene mal la hora, la banda o el modo.
- **La predicción de bandas es una aproximación propia y lo declara.** Cada predicción firma su
  motor, porque un motor real también cae en la estimación cuando falla o se sale de rango.
- **El paso gris exige el Sol a ±6° en los dos extremos**; «cruza el terminador» a secas sería
  cierto casi siempre y no serviría de nada.

### Pendiente de decisión de Jose

- Instalar un motor de predicción real (**ITURHFProp** o el paquete **VOACAP/ITSHFBC**) o
  quedarse con la aproximación honesta.
- Dos escrituras en el FT-710 para resolver las tablas de ancho de filtro y retardo de VOX.

### Prueba de integración sobre el respaldo real

```
1.838 registros leídos → 1.795 contactos, ninguno perdido
43 pares fundidos · 41 de ellos recuperan confirmaciones
1.703 contactos confirmados · 10.868 campos ajenos conservados
Reimportar: 0 nuevos, 0 cambios
TODO CORRECTO
```

### Lo que se aprendió por el camino

- **El cuaderno de Log4OM de EA8DLF tiene 43 contactos duplicados.** Las dos copias de cada
  par difieren en las confirmaciones. Por eso la importación funde en vez de descartar;
  descartar habría costado 41 confirmaciones.
- El fallo más grave lo encontró la prueba de punta a punta, no las 550 por capas: los
  repositorios devolvían los contactos sin sus colecciones hijas, así que un respaldo ADIF
  hecho desde el programa habría salido **sin ninguna QSL**.
- **OmniRig es servidor COM fuera de proceso**, así que la aplicación no tiene que compilarse
  en 32 bits. El módem digital de la Fase 4 se salva de un estrangulamiento.
- En el FT-710, **preguntar por el PTT es ponerlo**, y `SV;` no es una consulta sino una
  acción que intercambia los VFO. La lista de órdenes seguras se escribe a mano.
- `NA`, `AF` y `OC` son **localizadores Maidenhead válidos** además de continentes: sin
  exigir 4 caracteres, un spot pondría al corresponsal en mitad del Atlántico.
- El campo que el protocolo de WSJT-X llama «modo» **no es un modo**: es el carácter que el
  programa pinta en su ventana y hay que devolvérselo intacto.

## Equipo de Jose

- **Yaesu FT-710** por USB (CP2105 dual). CAT en **COM3 a 115200**; COM4 a 4800 también
  responde. `ID;` → `ID0800;`. Ver `docs/04-ft710-cat.md`.
- Cuidado: **COM8 no es la radio**, es un Meshtastic. Identificar siempre por `ID;`.
- OmniRig 1.20 instalado · Hamlib 4.6.2 con `rigctld.exe` viene con Log4OM.

## Pendiente de comprobar con el equipo encendido

- Tabla de `SH` (ancho de filtro) y de `VD` (retardo de VOX): hoy van como índice, sin
  inventar la conversión.
- Orden de lectura del banco de memorias, que no está en la captura.
- Una escritura de prueba (leer `SH0;`, mover un paso, devolverlo) **previa autorización**.

## Decisiones tomadas (2026-09-21)

- **Stack**: C# .NET 8 + WPF, escritorio Windows.
- **Alcance**: paridad completa con Log4OM, por fases.
- **Servicios**: LoTW, QRZ.com, ClubLog, eQSL, cluster DX, WSJT-X y JTDX.
- **Modos digitales**: módem propio integrado (FT8/FT4 dentro de la app), después del núcleo.
- **Base de datos**: SQLite, un solo fichero. MySQL/MariaDB del original no se replica.
- **Idioma**: código en español; campos ADIF en inglés por normativos; todo lo visible, en español.
- **Duplicados al importar**: fundir, nunca descartar.

## Entorno

- .NET SDK 8.0.422 · Visual Studio 18 · Windows 10 x64 (10.0.18362)
- Original instalado en `D:\Log4OM NextGen\`; sus datos en `%AppData%\Log4OM2\`
- Hamlib 4.6.2 con `rigctld.exe` viene con Log4OM · OmniRig 1.20 instalado

## Cómo se prueba

```
dotnet build
dotnet test
dotnet run --project src\Nodisla.Cuaderno.Ui
dotnet run --project herramientas\Nodisla.Cuaderno.Herramientas.Importar -- <fichero.adi>
```

## Siguiente

**Fase 2 — operar**: control del equipo (CAT y PTT), cluster DX por Telnet, UDP de WSJT-X y
JTDX, XML-RPC de FLDigi, mapa y bandplan.

Antes de la primera transmisión real hay que tener el vigilante que suelta PTT ante excepción
o cuelgue. Es el único riesgo del proyecto con daño material.

## Documentos

- `docs/00-plan-maestro.md` — fases, riesgos y decisiones
- `docs/01-inventario-funcional.md` — los 27 módulos del original
- `docs/02-modelo-datos.md` — esquema del original y modelo propuesto
- `docs/03-arquitectura.md` — stack, integraciones y convenciones
- `docs/capturas/` — capturas de la ventana principal
