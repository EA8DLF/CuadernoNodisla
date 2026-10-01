# Instalador de Windows

Genera un `.exe` de instalación con Inno Setup a partir de una publicación
self-contained de `src/Nodisla.Cuaderno.Ui`. Instala en Program Files (o donde
elija el operador), crea acceso directo en el menú inicio con el icono del
programa, y **nunca toca `%AppData%\CuadernoNodisla\`** (ahí viven la base de
datos y los ajustes del operador): ni el instalador ni el desinstalador tienen
ninguna sección que la referencie.

## Herramienta

- **Inno Setup 6** (gratuito, `https://jrsoftware.org/isinfo.php`). En esta
  máquina ya estaba instalado en `C:\Program Files (x86)\Inno Setup 6\`. Si
  falta, se instala con el instalador oficial de la web (no es un paquete
  NuGet, es una herramienta de compilación aparte).

## Generar el instalador desde cero

Desde la raíz del proyecto (`CuadernoNodisla`):

```powershell
# 1) Publicar la app en modo autocontenido (no hace falta .NET instalado en el PC destino)
dotnet publish "src\Nodisla.Cuaderno.Ui\Nodisla.Cuaderno.Ui.csproj" `
    -c Release -r win-x64 --self-contained true `
    -o "instalador\publicar\win-x64"

# 2) Compilar el instalador con Inno Setup
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "instalador\CuadernoNodisla.iss"
```

El `.exe` resultante queda en `instalador\salida\CuadernoNodisla-Instalador-<version>.exe`.

**Importante**: si algún otro proceso tiene abierto `dotnet` o el propio
programa mientras se compila, el `bin`/`obj` de `src\Nodisla.Cuaderno.Ui`
puede quedar bloqueado. Si el `dotnet publish` falla por eso, esperar a que
termine esa compilación en marcha antes de reintentar (no hace falta redirigir
`BaseOutputPath`/`BaseIntermediateOutputPath`: si se hace, hay que usar rutas
**absolutas** y aun así cada proyecto referenciado necesita su propia
subcarpeta, porque una ruta relativa se resuelve contra cada proyecto y crea
carpetas `instalador\` dentro de `src\*` — ya nos pasó una vez).

## De dónde sale la versión

El número de versión del instalador (`0.1.0.0` en la primera entrega) se lee
automáticamente del propio `.exe` publicado, con
`GetVersionNumbersString(...)` en el script `.iss`. Ese número, a su vez, sale
de `<Version>` en `Directory.Build.props`. Para sacar una versión nueva, basta
con subir `<Version>` ahí, volver a publicar y recompilar el `.iss`: el nombre
del instalador y el número que se ve en "Agregar o quitar programas" se
actualizan solos.

## El script `instalador/CuadernoNodisla.iss`

Puntos que no hay que romper si se toca:

- `AppId` es un GUID fijo — no cambiarlo nunca entre versiones, o Windows
  dejará de reconocer las instalaciones futuras como actualización de esta
  misma y no ofrecerá desinstalar la anterior.
- `PrivilegesRequired=lowest` + `PrivilegesRequiredOverridesAllowed=dialog`:
  el asistente deja elegir "instalar solo para mi usuario" (sin pedir
  permisos de administrador, en `%LocalAppData%\Programs\Cuaderno NODISLA`) o
  "para todos los usuarios" (Program Files, pide administrador). Es lo que
  permite instalar y probar sin ser administrador.
- La sección `[Files]` solo copia `instalador\publicar\win-x64\*` dentro de
  `{app}`. No hay ninguna sección `[Dirs]` ni `[UninstallDelete]` que apunte a
  `{userappdata}` — así se garantiza que ni instalar ni desinstalar puede
  tocar la carpeta de datos del operador. Si en el futuro hace falta tocar
  algo de `%AppData%` desde el instalador, pensarlo dos veces y, si hace
  falta, hacerlo con un `[Code]` que pregunte antes, nunca por omisión.
- Icono: `src\Nodisla.Cuaderno.Ui\Recursos\CuadernoNodisla.ico`, con el estilo
  de la casa NODISLA (la misma familia que SDR Nodisla TETRA «T» y DIGITAL
  «D»: cuadrado oscuro con filo cian, libro abierto con ondas, punto verde y la
  chapa con la «C»). Lleva 16, 24, 32, 48, 64 y 256. NO se edita a mano: sale
  del dibujo vectorial `Recursos\Marca.xaml` con
  `powershell -NoProfile -ExecutionPolicy Bypass -File herramientas\Icono\GenerarIcono.ps1`.
  Se usa en el ejecutable (`ApplicationIcon`), en la barra de título de todas
  las ventanas (va también como `Resource`), en la cabecera de la ventana
  principal (el mismo dibujo, en vectorial), en el asistente del instalador
  (`SetupIconFile`), en «Aplicaciones instaladas» (`UninstallDisplayIcon`, que
  apunta al `.ico` copiado en `{app}`) y en los accesos directos del menú
  inicio y del escritorio (este último, opcional, casilla apagada de fábrica).

## Verificación hecha antes de entregar esta primera versión (2026-09-26)

Instalación y desinstalación probadas de verdad en esta máquina, en una
carpeta de prueba aislada (no en Program Files real, para no pedir permisos
de administrador en la prueba):

1. Instalación silenciosa (`/VERYSILENT /CURRENTUSER /DIR=<ruta de prueba>`):
   copió los 591 ficheros esperados, creó el acceso directo del menú inicio
   apuntando al `.exe` instalado con su icono, y registró el desinstalador.
2. El `.exe` instalado arrancó correctamente (Serilog confirma "Cuaderno
   NODISLA arranca" y "Cuaderno listo"), sin bloquear el foco (se lanzó
   minimizado y se cerró a los pocos segundos).
3. Reinstalación encima de la misma carpeta (simulando una actualización):
   una carpeta `%AppData%\CuadernoNodisla` de prueba con ficheros simulados
   quedó con el mismo hash MD5 antes y después — la reinstalación no la tocó.
4. Desinstalación silenciosa: quitó todos los ficheros del programa y los
   accesos directos del menú inicio. La carpeta de datos de prueba, intacta.

**Aviso para quien retome esto**: en el paso 2, para comprobar el arranque
sin arriesgar los datos reales, se intentó redirigir `%AppData%` del proceso
de prueba con la variable de entorno `APPDATA`. **No funciona**: en .NET en
Windows, `Environment.SpecialFolder.ApplicationData` se resuelve vía
`SHGetKnownFolderPath`, que ignora la variable de entorno del proceso. El
`.exe` de prueba abrió sin querer la base de datos real de Jose
(`C:\Users\Jose\AppData\Roaming\CuadernoNodisla\cuaderno.sqlite`) — solo hizo
lecturas (`SELECT`, ninguna migración pendiente) y no llegó a escribir nada,
pero SQLite sí creó los ficheros auxiliares `cuaderno.sqlite-shm` y
`cuaderno.sqlite-wal` (este último vacío, 0 bytes: confirma que no hubo
ninguna escritura real). Se borraron esos dos ficheros auxiliares nada más
detectarlo y se confirmó que `cuaderno.sqlite` no cambió ni de tamaño ni de
fecha. Para volver a probar el arranque del `.exe` sin este riesgo, la única
forma limpia es con un usuario de Windows distinto (perfil separado de
verdad), no con trucos de variables de entorno.

## Sacar una versión y que el programa la avise

Desde la 0.1.0 el programa busca versiones nuevas en
`https://api.github.com/repos/EA8DLF/CuadernoNodisla/releases/latest` (anónimo, como mucho
una vez al día, en segundo plano) y, si hay una más nueva, enseña una barra con las notas y
el botón «Descargar e instalar». Ese botón **solo instala si la release trae dos adjuntos**:

| Adjunto | Contenido |
|---|---|
| `CuadernoNodisla-Instalador-<x.y.z.0>.exe` | el instalador de Inno Setup |
| `CuadernoNodisla-Instalador-<x.y.z.0>.exe.sha256` | `suma  nombre`, formato `sha256sum` |

Si falta el `.sha256` o la suma no casa, el programa borra lo descargado y no instala nada.
Mientras el repositorio sea privado la API responde 404 y el programa se calla (solo deja
una línea `Debug` en el registro).

### Pasos

1. Subir `<Version>` en `Directory.Build.props` (p. ej. `0.2.0`). La etiqueta será `v0.2.0`;
   el programa compara versiones semánticas, así que `v0.10.0` es mayor que `v0.9.0` y una
   `0.2.0-beta.1` cuenta como previa (y `releases/latest` no la ofrece).
2. Escribir las notas en un Markdown (lo que verá el operador en la barra del aviso).
3. Generar sin subir, para revisar:

   ```powershell
   .\herramientas\publicacion\Publicar-Version.ps1
   ```

   Deja en `instalador\salida\` el `.exe` y su `.exe.sha256`.
4. Publicar (necesita `gh auth login` hecho en este PC; **la ficha de GitHub nunca va dentro
   del programa**):

   ```powershell
   .\herramientas\publicacion\Publicar-Version.ps1 -SinCompilar -Subir -Notas .\notas-0.2.0.md
   ```

   Crea la release `v0.2.0` con los dos adjuntos, o los reemplaza si ya existía.
5. Comprobar en `https://github.com/EA8DLF/CuadernoNodisla/releases/latest` que están los dos
   ficheros, y en un programa con la versión anterior: Ajustes › Actualizaciones › «Buscar
   actualizaciones».

El instalador se lanza con `/SP- /CLOSEAPPLICATIONS` después de cerrar la ventana principal
de forma ordenada; `%AppData%\CuadernoNodisla\` sigue sin tocarse.

## Incidencias

`.github/ISSUE_TEMPLATE/` trae `fallo.yml`, `mejora.yml` y `config.yml`. En `config.yml`,
`blank_issues_enabled` **tiene que quedarse en `true`**: «Reportar un fallo» abre
`issues/new?title=…&body=…` con el informe relleno, y con las incidencias en blanco
desactivadas GitHub manda al selector de plantillas y se pierde lo prellenado. Si el informe
pasa de 7.500 caracteres de URL, el cuerpo va al portapapeles y el enlace lleva un aviso para
pegarlo.
