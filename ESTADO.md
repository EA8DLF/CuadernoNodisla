# Cuaderno NODISLA

Clon en español de Log4OM NextGen 2.40.0.0, para uso particular de EA8DLF.

## Estado — 25-09-2026

**Fases 1 a 4 hechas.** La aplicación guarda el cuaderno de verdad: **1.795 contactos de EA8DLF**
importados de Log4OM y viviendo en `%AppData%\CuadernoNodisla\cuaderno.sqlite`.

| Capa | Pruebas |
|---|---:|
| Dominio y DXCC | 567 |
| Aplicación | 177 |
| Integraciones (cluster, bandplan, FLDigi, WSJT-X/JTDX, N1MM) | 169 |
| Servicios de QSL | 98 |
| Propagación | 95 |
| Módem propio de FT8 y FT4 | 94 |
| ADIF | 91 |
| Radio (FT-710 y vigilante de PTT) | 90 |
| Diplomas | 80 |
| Audio | 60 |
| Datos | 59 |
| **Total** | **1.680** |

Compilación de la solución: **0 avisos, 0 errores**.

### Lo que hace hoy

- **Cuaderno**: entrada por teclado, rejilla paginada, trabajado antes, ADIF de ida y vuelta sin
  pérdida, fusión de duplicados.
- **Cabina**: frontal del FT-710 dibujado con sus 24 mandos, dos VFO, franja solar con datos
  reales, matriz de novedad y matriz de banda por modo.
- **Cluster** por Telnet con bandmap, **mapa** con paso gris y trayectos, **diplomas** con las
  553.064 referencias, **ajustes** con credenciales cifradas.
- **Módem propio de FT8/FT4**: modula, sincroniza y decodifica. Recupera el 100 % hasta −15 dB y
  el 91,7 % a −18 dB, **con cero decodificaciones falsas en todas las franjas**.
- **Decodificación AP** (05-10-2026): mientras hay un QSO en marcha con un corresponsal conocido,
  el decodificador fija los bits de los dos indicativos (ya sabidos por el secuenciador) y solo
  deja por leer de la señal real el campo del informe, igual que hace JTDX con su «AP decoding»
  — idea tomada de su comportamiento publicado, **sin mirar ni una línea de su código** (ver
  `TERCEROS.md`). Medido en `PistaApPruebas.cs`: recupera señales a −19,5 dB que a ciegas se
  pierden, y una pista equivocada —QSO con otro corresponsal, o solo ruido— no inventa ningún
  contacto: las ecuaciones de paridad no cuadran y la decodificación falla igual que sin pista.

### Nada se conecta solo

El equipo y el cluster arrancan **sin conexión**; conectar es siempre una acción del operador.
El audio y el módem se registran sin abrir ningún dispositivo.

## Pendiente de decisión de Jose

1. **Motor de propagación real** (ITURHFProp o el paquete VOACAP) o seguir con la aproximación,
   que está honestamente etiquetada como tal.
2. **Dos escrituras en el FT-710** para resolver las tablas de ancho de filtro y retardo de VOX.
   Hoy se muestran como índice en vez de en hercios y milisegundos.

**Resuelto:**
- La tabla del LDPC(174,91) real de FT8/FT4 (`ft8_lib`, licencia MIT) ya está en producción desde
  `tablas-ft8.txt`, con `TablasDelProtocolo.EsElCodigoReal=true`. Ver `TERCEROS.md` y
  `src/Nodisla.Cuaderno.Modos/Tablas/LEEME.md`.
- MSK144, MSK40 y Q65 leen sus tablas del código corrector directamente del código fuente GPLv3
  de WSJT-X, a falta de una fuente independiente con licencia permisiva (buscada y no encontrada
  el 05-10-2026). Decisión de Jose: mantener la excepción documentada tal cual en vez de retirar
  los tres modos. Detalle completo en `TERCEROS.md`, «Modos digitales».

## Lo que Jose debería arreglar en su estación

- **El reloj del PC atrasa unos 870 ms**, medido contra cuatro servidores concordantes. Con eso
  FT8 ya decodifica mal; con dos segundos, además se transmite fuera de ventana.
- **El nivel de audio de entrada llega al borde del recorte** (0,91-0,99). Con la señal saturada
  las relaciones señal-ruido son mentira y las señales débiles se pierden.
- **Su cuaderno de Log4OM tiene 43 contactos duplicados** cuyas dos copias discrepan en las
  confirmaciones. Al importar se funden y se rescatan 41 confirmaciones.

## Siguiente

**Fase 5**: concursos, satélites, manipulador y entrenador de CW, macros, net control, etiquetas
e impresión de QSL, y la ayuda en español con capturas.

## Decisiones tomadas (2026-09-21)

- **Stack**: C# .NET 8 + WPF, escritorio Windows.
- **Alcance**: paridad completa con Log4OM, por fases.
- **Servicios**: LoTW, QRZ.com, ClubLog, eQSL, cluster DX, WSJT-X y JTDX.
- **Modos digitales**: módem propio integrado (FT8/FT4 dentro de la app), después del núcleo.
- **Base de datos**: SQLite, un solo fichero. MySQL/MariaDB del original no se replica.
- **Idioma**: código en español; campos ADIF en inglés por normativos. Lo visible, desde 2026-10,
  en seis idiomas (es, en, pt, fr, it, de) con cambio en caliente; el español es la referencia y los
  registros siguen en español. Ver `docs/03-arquitectura.md`, «Idiomas».
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
