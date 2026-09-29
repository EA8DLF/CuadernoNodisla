# Plan de bandas de Cuaderno NODISLA

Aquí vive el recurso que usa el bandplan
(`src/Nodisla.Cuaderno.Integraciones/Bandplan/`, que implementa `IBandplan`) para decir,
dada una frecuencia, en qué tramo cae, qué modo le corresponde y si el operador puede
transmitir ahí. **Avisa, no impide**: hay motivos legítimos para estar fuera del plan.

## Qué hay en esta carpeta

| Fichero | Qué es |
|---|---|
| `bandplan-nodisla.tsv` | El recurso. Se genera; no se edita a mano. |
| `generar-bandplan.py` | El guion que lo fabrica a partir de los XML de Log4OM. |

El mismo texto queda incrustado como constante en
`src/Nodisla.Cuaderno.Integraciones/Bandplan/DatosBandplan.cs`, que también genera el
guion. Así la consulta no depende de ningún fichero suelto en disco: se compila
dentro del programa y funciona igual en la aplicación y en las pruebas. Es el mismo
patrón que usa la tabla de países del dominio (`recursos/LEEME.md`).

## Qué fuente se eligió y por qué

Los ocho ficheros de `%APPDATA%\Log4OM2\bandplan\`:

| Fichero | Plan |
|---|---|
| `bandplan_r1.xml` | IARU Región 1 |
| `bandplan_r2.xml` | IARU Región 2 |
| `bandplan_r3.xml` | IARU Región 3 |
| `bandplan_dxcc108.xml` | Brasil |
| `bandplan_dxcc221.xml` | Dinamarca |
| `bandplan_dxcc223.xml` | Inglaterra |
| `bandplan_dxcc230.xml` | Alemania |
| `bandplan_dxcc291.xml` | Estados Unidos |

Son los planes que Log4OM mantiene y actualiza, con los tramos ya cerrados y encadenados
(el final de un tramo es el principio del siguiente), lo cual permite buscar por bisección
sin huecos. **La región importa**: EA8 es Región 1, no Región 2, y las diferencias no son
cosméticas. En 40 metros la Región 1 llega a 7.200 kHz y la Región 2 a 7.300; en 80 la
Región 1 termina en 3.800 y la Región 2 en 4.000. Quien mire el plan equivocado transmite
fuera de banda creyendo que va sobrado.

Lo que la fuente **no** trae, y por tanto aquí tampoco está:

- **Clases de licencia.** Ningún XML de Log4OM las distingue, ni siquiera el de Estados
  Unidos, que es donde de verdad hacen falta (Extra, Advanced, General, Technician). El
  formato reserva una columna para ellas y la consulta ya la respeta; hoy va vacía en
  todas las líneas, lo que significa «todas las clases».
- **Potencia máxima** por tramo.
- **Planes nacionales fuera de esos cinco países.** España no tiene uno: a EA8 se le
  aplica el de la Región 1. Cuando se quiera meter el CNAF, se añade un plan `dxcc281`
  sin tocar ni el formato ni el código.

## Formato del recurso

Texto separado por tabuladores, una línea por registro, la primera columna dice de qué
tipo es. Las líneas que empiezan por `#` son comentarios. Los campos vacíos al final se
recortan, así que hay que leer tolerando líneas cortas.

```
V   versionFormato   fechaDeLosDatos   fechaDeGeneracion
P   idPlan   region   dxcc   nombre
S   idPlan   banda   desdeKhz   hastaKhz   emision   modulacion   clases
F   idPlan   khz   modo   nota
```

- Las líneas `S` (segmento) y `F` (frecuencia señalada) pertenecen al último plan `P`
  declarado, así que el orden de las líneas importa y los planes van en bloques.
- **Todas las frecuencias van en kilohercios**, que es la unidad de los `Start`/`End` de
  Log4OM. Las `SpecialFrequencies` del XML vienen en hercios y el guion las convierte, para
  que el fichero no mezcle unidades.
- `emision` es `CW`, `DIGITAL` o `PHONE`, que es lo único que distingue la fuente; se
  traducen a `UsoDelTramo.Cw`, `DigitalEstrecho` y `Fonia`. El formato admite también
  `DIGITALANCHO`, `IMAGEN`, `BALIZA` y `RESERVADO` para cuando se meta un plan que los
  separe. `modulacion` es `USB` o `LSB` y es una convención, no una norma.
- `clases` es una lista separada por comas de las clases de licencia que pueden usar el
  tramo. **Vacía significa «todas»**.
- `idPlan` es `r1`, `r2`, `r3` para las regiones IARU y `dxcc<numero>` para los planes
  nacionales.

## Cómo se consulta

- `BandplanNodisla.Para(region, dxcc)` elige el plan: el nacional si está publicado y, si
  no, el de la región.
- `IBandplan.Consultar(frecuencia, modo)` devuelve el tramo, si la frecuencia está en
  banda, si el modo cuadra con el uso del tramo y un aviso ya redactado para el operador
  (nulo cuando no hay nada que decir).
- El límite inferior de un tramo entra y el superior no, salvo en el último tramo de cada
  banda, donde sí entra. Con ese criterio una frecuencia nunca cae en dos tramos.

## Cómo actualizar el recurso

Los XML los actualiza el propio Log4OM. Después:

```
cd recursos
python generar-bandplan.py                        # usa %APPDATA%\Log4OM2\bandplan
python generar-bandplan.py D:\ruta\a\bandplan     # o una carpeta cualquiera

dotnet build src\Nodisla.Cuaderno.Integraciones
dotnet test tests\Nodisla.Cuaderno.Integraciones.Pruebas
```

El guion escribe por pantalla cuántos planes, tramos y frecuencias señaladas ha
encontrado. Las pruebas comprueban los límites de banda que más duelen (7.200 frente a
7.300, 3.800 frente a 4.000) y fallan si una actualización los mueve.
