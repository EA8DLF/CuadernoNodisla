# Tabla de países de Cuaderno NODISLA

Aquí vive el recurso que usa el resolutor DXCC (`src/Nodisla.Cuaderno.Dominio/Dxcc/`)
para decir a qué entidad, zonas y continente pertenece un indicativo **en la fecha del
contacto**.

## Qué hay en esta carpeta

| Fichero | Qué es |
|---|---|
| `paises-nodisla.tsv` | El recurso. Se genera; no se edita a mano. |
| `nombres-es.tsv` | Los nombres en español de las entidades. **Este sí se edita a mano.** |
| `generar-paises.py` | El guion que fabrica el recurso a partir de los ficheros de Log4OM. |

El mismo texto de `paises-nodisla.tsv` queda incrustado como constante en
`src/Nodisla.Cuaderno.Dominio/Dxcc/DatosPaises.cs`, que también genera el guion. Así el
dominio no depende de ningún fichero suelto en disco ni de recursos del proyecto: se
compila dentro del programa y funciona igual en la aplicación y en las pruebas.

## Qué fuente se eligió y por qué

Ninguna de las dos fuentes disponibles sirve sola:

| | `cty.dat` de AD1C (aquí: `ctyfile.json`) | `country.xml` de Log4OM |
|---|---|---|
| Prefijos | 26.300, con zona CQ/ITU y coordenada **propias del prefijo** | 4.000 pares prefijo/entidad, sin zonas propias |
| Excepciones nominales (`=` de cty.dat) | 2.675 indicativos completos | ninguna |
| Nombres de las entidades | **no los trae** | sí, los 402 |
| Histórico | **no lo trae**: solo el mundo de hoy | sí: ventanas con fecha de alta y de baja |

Es decir: `cty.dat` sabe el detalle fino del presente y `country.xml` sabe la historia.
Un cuaderno que use solo `cty.dat` asigna mal todos los contactos antiguos (un QSO de
1990 con `OK1ABC` era Checoslovaquia, no la República Checa); uno que use solo
`country.xml` pierde las zonas del prefijo y las excepciones nominales, que son las que
resuelven los indicativos raros de las expediciones.

**Se usan las dos, cada una para lo que sabe:**

- de `country.xml`: el catálogo de entidades (número DXCC, nombre, continente, zonas y
  coordenada por omisión, fechas de alta y baja) y las **ventanas históricas** de prefijo;
- de `ctyfile.json`: los **prefijos vigentes** con sus zonas y coordenadas finas, y las
  **excepciones nominales**;
- de `nombres-es.tsv`: los nombres en español.

Dos detalles del cruce que conviene conocer:

- El centro de cada entidad se toma de `cty.dat` cuando está, para que coincida con el de
  sus prefijos y estos no tengan que repetirlo en cada línea.
- Log4OM marca algunas entidades como inactivas sin que la ARRL las haya borrado (le pasa
  a **Libia**). Como `cty.dat` solo lista entidades vigentes, se usa su presencia ahí para
  no darlas por borradas.

## Formato del recurso

Texto separado por tabuladores, una línea por registro, la primera columna dice de qué
tipo es. Las líneas que empiezan por `#` son comentarios. Los campos vacíos al final de
la línea se recortan, así que hay que leer tolerando líneas cortas.

```
V   version   fechaDeLosDatos   fechaDeGeneracion
E   numero  nombre  nombreEs  prefijoArrl  continente  cq  itu  lat  lon  utc  validaDesde  validaHasta
X   indicativo  dxcc  cq  itu  continente  lat  lon  desde  hasta      (excepción nominal)
P   prefijo     dxcc  cq  itu  continente  lat  lon                    (prefijo vigente de cty.dat)
H   prefijo     dxcc  cq  itu  continente  lat  lon  desde  hasta      (ventana histórica)
```

- En las líneas `X`, `P` y `H`, un campo de zona, continente o coordenada **vacío**
  significa «el de la entidad»: así la tabla ocupa la mitad.
- Las fechas van en `aaaa-mm-dd`. Vacío significa «sin límite por ese lado».
- `utc` es el desfase horario aproximado, deducido de la longitud: ninguna de las dos
  fuentes lo trae, y sirve para orientar, no para fechar nada.

## Cómo resuelve el resolutor

1. **Excepción nominal.** Si la tabla nombra el indicativo entero (`X`), manda sin
   discusión. Se prueba el indicativo completo y después el mismo sin los añadidos de
   operación.
2. **`/MM` y `/AM`** no tienen entidad: se devuelve desconocido y se marca
   `NecesitaRevision`.
3. **Prefijo más largo** que encaje y esté vigente ese día, buscado en un árbol de
   prefijos (trie). Cuando varias reglas comparten prefijo, el orden es: ventana
   histórica cerrada → prefijo vigente de `cty.dat` → ventana histórica abierta. Además
   se comprueba que la entidad existiera ese día.
4. Si nada encaja, desconocido y a revisar.

## Cómo actualizar la tabla

Los ficheros de origen los actualiza el propio Log4OM (menú *Program configuration →
Update country file*), o se descargan a mano de <https://www.country-files.com/>.

```
cd recursos
python generar-paises.py                       # usa %APPDATA%\Log4OM2
python generar-paises.py D:\ruta\a\los\datos   # o una carpeta cualquiera
```

El guion reescribe `paises-nodisla.tsv` y `DatosPaises.cs` y escribe por pantalla cuántas
entidades, excepciones, prefijos y ventanas históricas ha encontrado. Después:

```
dotnet build
dotnet test tests\Nodisla.Cuaderno.Dominio.Pruebas
```

La prueba `AciertoContraLog4OmPruebas` compara el resultado contra 1.836 contactos reales
de EA8DLF ya resueltos por Log4OM y falla si el acuerdo baja del 99 %. Es el aviso de que
una actualización de la tabla ha roto algo.

Para cambiar o añadir nombres en español basta editar `nombres-es.tsv` y volver a lanzar
el guion; no hace falta nada más.

## Lo que esta tabla no cubre

- 37 patrones de `country.xml` que no son prefijos llanos (expresiones regulares como
  `KG4[A-Z]{2}` o prefijos con barra como `FO/A`). Los casos que describen los cubren las
  excepciones nominales de `cty.dat`.
### Excepciones nominales que faltan en la edición descargada

Las excepciones nominales de `cty.dat` son las de la edición que se descargó. Un
indicativo que AD1C haya añadido después —típicamente expediciones y estaciones
especiales— se resuelve por su prefijo, que a veces apunta a otra entidad. **Es la única
causa de los seis desacuerdos que quedan** contra los 1.836 contactos reales de EA8DLF
con la tabla del 2025-08-05:

| Indicativo | Log4OM dice | La tabla da | Por qué |
|---|---|---|---|
| `TX7L` | 509 Islas Marquesas | 227 Francia | `TX` es Francia salvo excepción nominal |
| `AO75IC` | 29 Islas Canarias | 281 España | prefijo especial español sin excepción |
| `AO75IB` | 21 Islas Baleares | 281 España | ídem |
| `II0IARU` | 225 Cerdeña | 248 Italia | `II0` es Italia salvo excepción nominal |
| `KL5NS` | 291 Estados Unidos | 6 Alaska | operador de Alaska que trabaja desde los 48 |
| `5017JOSES9Z` | 438 Madagascar | sin resolver | indicativo que no sigue ninguna regla |

**No se corrigen a mano.** Meter excepciones propias en el recurso lo convertiría en algo
que el guion no puede regenerar, y es justo lo que AD1C arregla en cada edición: basta con
actualizar `cty.dat` y volver a lanzar `generar-paises.py`. El último caso
(`5017JOSES9Z`) no tiene arreglo por tabla: es un indicativo inventado, y para eso está el
fichero `NonStandardCallsign.txt` de Log4OM, que aquí todavía no se usa.
