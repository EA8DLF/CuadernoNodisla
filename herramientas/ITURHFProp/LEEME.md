# ITURHFProp — motor de predicción de propagación

Implementación de referencia de la **Recomendación UIT-R P.533**, del Grupo de Estudio 3 de la
Unión Internacional de Telecomunicaciones. Es el motor que usa el módulo de propagación del
Cuaderno NODISLA cuando está compilado; si no lo está, el cuaderno cae a su aproximación propia y
lo dice en cada predicción.

> **En este repositorio no viene el motor.** Solo están este LEEME y `validar.sh`. Los binarios,
> los 131 MB de `datos/`, los ejemplos y las fuentes de la UIT **no se redistribuyen**: su permiso
> (abajo, en «Licencia») cubre a quien implementa la Recomendación, pero no dice nada de volver a
> repartirlo, y ante la duda se ha dejado fuera. Sin el motor el cuaderno funciona igual con su
> aproximación propia, marcada como tal.
>
> Para tenerlo: clone `https://github.com/ITU-R-Study-Group-3/ITU-R-HF` (commit indicado abajo),
> compílelo como dice «Cómo rehacerlo» y deje en esta carpeta `programa/` (ITURHFProp.exe,
> P533.dll, P372.dll), `datos/` (la carpeta `Data` del repositorio de la UIT) y, si quiere
> validarlo, `ejemplos/`. El cuaderno lo busca aquí subiendo desde su carpeta de instalación.

## Qué se trajo, y de dónde

| | |
|---|---|
| Repositorio | `https://github.com/ITU-R-Study-Group-3/ITU-R-HF` |
| Commit | `82017594a1c6cacfaa7e86954c4ae7b3a5825a3d`, del 15 de mayo de 2025 |
| Compilado con | Visual Studio Community 2026, versión 18.7.11903.348 |
| Toolset | MSVC v145 (14.51.36231), SDK de Windows 10.0.26100.0 |
| Plataforma | x64, configuración Release |
| Traído el | 25 de septiembre de 2026 |

### Por qué un commit y no la versión publicada

La última versión publicada es la **14.3**, de junio de 2022. Se revisaron los mensajes de
**todos** los commits posteriores a esa etiqueta y ninguno toca las ecuaciones: son arreglos de
compilación, avisos del compilador y formato. Entre ellos hay dos que importan justo para lo
nuestro:

- *«Fix call to GetModuleFileName»*, un fallo real en Windows.
- *«Fix some MSVC compiler warnings»* y las guardas de cabecera, que son los que permiten
  compilarlo limpio con un MSVC moderno.

Por eso se usa la punta de `master` clavada a un commit concreto: da los arreglos de Windows sin
cambiar ni un número, y clavar el SHA es más reproducible que una etiqueta.

El programa sigue declarando internamente **P533 versión 14.2** y **P372 versión 14.3**, que es lo
que dicen sus propias fuentes.

### Licencia

El repositorio **no tiene fichero LICENSE**. El permiso está escrito en el README y en las
cabeceras del código fuente, y dice:

> *The ITURHFProp, P533 and P372 software has been developed collaboratively by participants in
> ITU-R Study Group 3. It may be used by implementers in their implementation of the
> Recommendation as well as in revisions of the specific original Recommendation and in other ITU
> Recommendations, free from any copyright assertions.*

Se entrega *as is*, sin garantías. No es una licencia de código abierto al uso: es una renuncia
expresa de la UIT a reclamar derechos sobre quien implemente la Recomendación. Por eso se usa en
local pero no se publica en este repositorio.

## Qué hay en esta carpeta

```
programa/   ITURHFProp.exe, P533.dll y P372.dll compilados aquí
datos/      mapas ionosféricos, coeficientes CCIR y factores decílicos (131 MB)
ejemplos/   los nueve casos de referencia de la UIT con su salida original
fuente/     solo el código que hace falta para rehacer los binarios
validar.sh  comprueba que lo compilado reproduce los nueve casos
```

No se trajeron los ejecutables ni las DLL precompiladas que el repositorio arrastra en su
historial: los binarios de aquí están hechos en esta máquina desde estas fuentes. Tampoco se
trajo la carpeta `Antenna` completa (140 MB de patrones NEC y T13), porque el cuaderno usa antena
isotrópica con desplazamiento de ganancia; solo está el patrón que necesita uno de los ejemplos.

**Los 131 MB de `datos/` no se pueden reducir.** Son los doce mapas ionosféricos mensuales
(`ionos01..12.bin`, 11,2 MB cada uno) que `ReadIonParameters` exige, más los coeficientes CCIR.
Se comprobó: sin ellos el programa aborta, y comprimidos solo bajan un 16 %, porque son datos
densos.

## Cómo rehacerlo

Desde la línea de órdenes, **sin abrir el IDE**:

```
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ^
    fuente\ITURHFProp\Win32\Combined\Combined.sln ^
    -p:Configuration=Release -p:Platform=x64 ^
    -p:PlatformToolset=v145 -p:WindowsTargetPlatformVersion=10.0.26100.0 -m
```

Los proyectos vienen apuntando al toolset **v143** (Visual Studio 2022), que en esta máquina no
está instalado; por eso hay que pasarle `PlatformToolset=v145`. No hizo falta tocar ningún
`.vcxproj`: es código C puro y se reorienta sin pelea.

Después hay que **renombrar las DLL**. La compilación de 64 bits las deja como `P533_x64.dll` y
`P372_x64.dll`, pero el ejecutable las carga con `LoadLibrary("P533.dll")`, sin sufijo. Si no se
renombran, el programa termina con código 51 y el mensaje `p533.dll Not Found`.

```
copy ...\x64\Release\ITURHFProp_x64.exe programa\ITURHFProp.exe
copy ...\x64\Release\P533_x64.dll       programa\P533.dll
copy ...\x64\Release\P372_x64.dll       programa\P372.dll
```

La carpeta se llama `programa` y **no `bin`** a propósito: el `.gitignore` de la raíz ignora
`bin/` para no guardar compilados de .NET, y con ese nombre el motor se quedaría fuera del
repositorio sin que nadie se enterase hasta clonarlo en otra máquina. Por el mismo motivo esta
carpeta tiene su propio `.gitattributes`: la raíz fuerza `eol=crlf` y eso reescribiría los mapas
de la UIT y las salidas de referencia con las que se validan.

## Cómo comprobar que vale

```
bash validar.sh
```

Ejecuta los nueve casos de referencia de la UIT y compara con la salida que ella misma publica,
descartando solo las dos líneas que cambian siempre y con razón: la fecha de compilación del
binario y la hora del análisis. **Todo lo demás tiene que ser idéntico.** Con el binario que hay
en `programa/` lo es, incluidos los 198 KB de números del caso de Caracas.

Si algún caso no coincide, el motor no vale y no se debe usar: estaría enseñando al operador
números que nadie ha validado.

## Trampas del formato, que no avisan

Las tres están esquivadas en el código de `Nodisla.Cuaderno.Propagacion`, con una prueba cada una.
Se apuntan aquí porque ninguna da error: las tres producen un resultado equivocado en silencio.

1. **`Path.hour 0` mata el proceso.** No lo rechaza: se cae con una violación de segmento y no
   deja informe. La medianoche es la **hora 24**. El rango válido es 1 a 24.

2. **La lista de `RptFileFormat` se corta en la primera opción con un dígito.** El analizador lee
   el resto de la línea con un `%[a-z,A-Z _|]` que no admite números, así que al llegar a algo
   como `RPT_N0_F2` se pierde esa opción y **todas las que vengan detrás**, y el informe sale con
   menos columnas sin que nadie avise. Por eso se pide `RPT_ALL`, que no tiene lista que analizar,
   y se leen las columnas por el nombre que da la propia cabecera del informe.

3. **`DataFilePath` necesita barra final.** El programa pega el nombre del fichero justo detrás
   sin poner separador; sin la barra busca `...datosionos09.bin` y no lo encuentra.

Y una cuarta que solo afecta a `validar.sh`: el ejecutable es de Windows y **no entiende las rutas
de Git Bash** (`/c/Users/...`). Hay que traducirlas con `cygpath -m`. Si se le cuela una ruta así,
no protesta: deja un informe a medias.

## Límites del modelo

- **De 1,6 a 30 MHz.** Seis metros se le sale del rango, así que esa banda la calcula siempre la
  aproximación propia del cuaderno y sale marcada como tal.
- **Manchas solares de 1 a 311** (R12). El cuaderno recorta a ese rango.
- Es un proceso externo: puede no estar, tardar de más o terminar mal. En todos esos casos las
  bandas afectadas caen a la aproximación propia y **se marcan una a una**, nunca se enseñan como
  si las hubiera calculado la P.533.

## Y una advertencia que conviene no olvidar

**ITURHFProp no es VOACAP.** Son modelos distintos y dan números distintos entre ellos. Si algún
día alguien compara las cifras del cuaderno con las de un VOACAP en línea y no cuadran, eso no es
un fallo: son dos modelos que no tienen por qué coincidir.
