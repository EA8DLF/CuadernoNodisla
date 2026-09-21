# Cuaderno NODISLA

Clon en español de Log4OM NextGen 2.40.0.0, para uso particular de EA8DLF.

## Estado — 21-09-2026

**Fase 1 (núcleo del cuaderno) completada.** La aplicación arranca, registra contactos,
importa y exporta ADIF, y el cuaderno real de EA8DLF entra entero.

Prueba de integración sobre el respaldo real (`EA8DLF_20260903_135046_backup.adi`, 3,63 MB):

```
1.838 registros leídos → 1.795 contactos, ninguno perdido
43 pares fundidos · 41 de ellos recuperan confirmaciones
1.703 contactos confirmados · 10.868 campos ajenos conservados
Reimportar: 0 nuevos, 0 cambios
TODO CORRECTO
```

### Cifras

| Capa | Pruebas | Medido |
|---|---|---|
| Dominio y DXCC | 544 | 99,67 % de acierto en entidad DXCC · 1,5 µs por resolución |
| ADIF | 87 | 1.838 QSOs en 1,3 s · ida y vuelta byte a byte en los 8 respaldos |
| Datos | 53 | alta en lote 3,8 s · duplicado 1,2 ms · página de 200 con hijas 150 ms |
| Aplicación | 92 | — |

Compilación de la solución entera: **0 avisos, 0 errores**.

### Lo que se aprendió por el camino

- **Tu cuaderno de Log4OM tiene 43 contactos duplicados.** Las dos copias de cada par difieren
  en las confirmaciones: una da la QSL por recibida y la otra no. Por eso la importación funde
  en vez de descartar; descartar habría costado 41 confirmaciones.
- El fallo más grave lo encontró la prueba de punta a punta, no las 550 pruebas por capas:
  los repositorios devolvían los contactos sin sus colecciones hijas, así que un respaldo ADIF
  hecho desde el programa habría salido **sin ninguna QSL**.

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
