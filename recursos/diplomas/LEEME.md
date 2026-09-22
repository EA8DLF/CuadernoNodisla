# Catálogo de diplomas de Cuaderno NODISLA

Aquí vive el recurso que usa el motor de diplomas
(`src/Nodisla.Cuaderno.Diplomas/`) para saber qué diplomas existen, con qué clases,
qué referencias tiene cada uno y qué confirmación exige.

Es el módulo más pesado del programa: **87 diplomas, 390 variantes y 553.064
referencias**. El original (Log4OM) lo guarda en `Activations.SQLite`, 156 MB. Aquí se
convierte a texto versionable, se incrusta comprimido en el ensamblado y **en ejecución no
se depende de ningún fichero de Log4OM**.

## Qué hay en esta carpeta

| Fichero | Qué es |
|---|---|
| `diplomas-nodisla.tsv` | Los diplomas, las variantes y los informes. Se genera; no se edita a mano. |
| `referencias/<CODIGO>.tsv` | Las referencias de cada diploma, un fichero por diploma. Se generan. |
| `ajustes-diplomas.tsv` | Nombres en español, gestor, exigencia, medios y objetivos. **Este sí se edita a mano.** |
| `generar-diplomas.py` | El guion que fabrica el recurso a partir de la base de Log4OM. |

El guion escribe además
`src/Nodisla.Cuaderno.Diplomas/Recursos/catalogo-diplomas.tsv.gz`, que es el mismo texto
concatenado y comprimido (7,4 MB frente a 50 MB en claro). Ese fichero se incrusta en el
ensamblado con `<EmbeddedResource>` y es lo único que el motor necesita en ejecución.

**Un fichero por diploma** no es capricho: con las 553.064 referencias en un solo TSV,
actualizar POTA movería un fichero de 50 MB y el `diff` sería inútil. Así, bajar la lista
nueva de POTA toca `referencias/POTA.tsv` y nada más.

## Cómo se actualiza

1. Que Log4OM actualice sus diplomas (menú *Awards*, botón de descarga), o sustituir
   `%AppData%\Log4OM2\Activations.SQLite` por una copia más nueva.
2. Desde esta carpeta:

   ```
   python generar-diplomas.py
   ```

   Sin argumentos busca `%AppData%\Log4OM2\Activations.SQLite`. Se puede pasar otra ruta
   como primer argumento. **La base se abre en solo lectura** (`mode=ro`): el guion no
   escribe ni una fila en el fichero del original.
3. El guion imprime cuántos diplomas, variantes y referencias ha convertido y cuántas
   entidades permitidas ha normalizado. Comprobar que las cifras cuadran con lo esperado.
4. Revisar el `diff`: lo normal es que solo cambien uno o dos ficheros de `referencias/`.
5. Compilar y pasar las pruebas. `CatalogoPruebas` comprueba los recuentos, así que si el
   catálogo crece hay que actualizar esos números **a conciencia**, no por inercia.
6. La primera vez que arranque el programa con el recurso nuevo, el motor detecta que la
   huella ha cambiado y recompila su base de catálogo. No hay que borrar nada a mano.

## Qué se puede tocar a mano

Solo `ajustes-diplomas.tsv`. Cada línea es `(código, variante)` y manda sobre lo que deduce
el guion; `*` en la variante vale para todas las del diploma. Columnas:

- `nombre_es` y `gestor`: para la interfaz.
- `exigencia`: `Trabajado`, `Confirmado` o `Verificado`.
- `medios`: vías de confirmación admitidas (`Papel`, `Lotw`, `Eqsl`, `ClubLog`, `QrzCom`,
  `HamQth`, `HrdLog`, `QrzCq`), separadas por comas.
- `objetivo`: cuántas referencias distintas hacen falta.
- `solo_vigentes`: `1` si las entidades DXCC borradas no cuentan para ese diploma.

### Sobre los objetivos

**Un objetivo inventado engaña al operador**, que es exactamente lo que este motor no puede
hacer: le llevaría a pedir un diploma que no tiene. Por eso solo se rellena cuando la cifra
está en el reglamento y no hay duda:

- las que dice el propio catálogo con todas sus letras, que el guion extrae solo
  (`(100 credits)` de las clases del VUCC, `Work all 52 Spanish provinces` del TPEA);
- las tres que se han puesto a mano por ser el umbral del reglamento: DXCC MIXED = 100,
  WAS = 50 y WAZ mixto = 40.

El resto quedan **sin objetivo**, y entonces `ProgresoDeDiploma.Fraccion` y `Faltan` son
nulos y la interfaz enseña el número de referencias sin barra de avance. Es la respuesta
honesta: el catálogo del original no guarda los umbrales.

## Formato de los ficheros

Texto separado por tabuladores, una línea por registro, la primera columna dice de qué tipo
es. Las líneas que empiezan por `#` son comentarios y los campos vacíos del final se
recortan, así que hay que leer tolerando líneas cortas. Las fechas van en ISO (`AAAA-MM-DD`)
y vacías cuando no hay límite; el original usa enteros `AAAAMMDD` con `99981231` de
centinela.

```
V  version  fechaDeLosDatos  fechaDeGeneracion  nDiplomas  nVariantes  nReferencias
D  codigo  clase  nombre  nombreEs  gestor  web  webRef  exigencia  medios  validaElGestor
   campo  campoLider  separador  campoExacto  cadenaInicial  cadenaFinal  prefijosRef
   multiplesRefs  referenciaLibre  validoDesde  validoHasta  valido  bandas  emision
   soloVigentes  calculable  motivo  descripcion
C  codigo  variante  descripcion  tipoContacto  modos  bandas  emision  continentes  anual
   exigeSat  excluyeSat  grantCode  validoDesde  validoHasta  objetivo  sintetica
I  informe  titulo  descripcion  proveedor  habilitado
R  codigo  referencia  alias  validoDesde  validoHasta  descripcion  grupo  subgrupo
   gridsquare  puntuacion  bonus  valido  patron  dxccPermitidos
```

## Decisiones del cruce que conviene conocer

### `AllowedDXCC` se normaliza

El original guarda en cada referencia una lista de texto de hasta 3.000 caracteres,
`#24#;#514#;#336#;…`, con las entidades desde las que se puede activar. Son **1.536.531
entidades repartidas en las 553.064 referencias** y no se pueden indexar así. El guion las
parte en números y las deja en la última columna de la línea `R` separadas por comas; el
motor, al compilar el catálogo, las vuelca en la tabla `premio_dxcc_permitido`
(clave primaria `(diploma, referencia, entidad)` más índice por entidad). Preguntar «qué
referencias puedo activar desde Canarias» pasa a ser una consulta.

Se han comprobado las 553.064 filas: **todas** las fichas tienen la forma `#N#`. Lo que no
la tenga se descarta y el guion lo avisa por pantalla.

### 75 diplomas no traen ninguna clase

De los 87, solo 12 tienen filas en `AwardConfig`; los otros 75 el original los trata como
si tuvieran una sola clase sin restricciones. El guion se lo pone explícito: les añade una
variante `GENERAL` marcada como sintética, para que el motor no tenga que adivinarlo y para
que se sepa, mirando el recurso, cuál viene del original y cuál no.

### `CUSTOM` no es una vía de confirmación

19 diplomas declaran `CUSTOM` en `ConfirmationMethod`: significa que los valida el gestor
contra su propia base de datos (SOTA, POTA, IOTA…), que el cuaderno no puede consultar. No
se pueden dar por confirmados sin más, así que quedan marcados con `validaElGestor = 1` y
sin vías declaradas. El motor entonces cuenta lo confirmado **por cualquier vía**, que es
una cota inferior de lo que el operador tiene de verdad, y lo dice en
`MotorDeDiplomas.PorQueNoSeCalculaAsync`.

### Las fechas de las entidades DXCC no salen de aquí

El diploma DXCC lista sus 402 entidades con la misma ventana para todas
(`19451115`–`99981231`), borradas incluidas. Esa ventana es inservible para decidir si un
contacto de 1990 con una entidad borrada en 1993 cuenta. Por eso el motor, al compilar el
catálogo, **sustituye la ventana de las 402 referencias del DXCC por la del resolutor de
entidades del dominio**, que es quien sabe cuándo nació y cuándo se borró cada una. Así no
hay dos lugares del programa que puedan discrepar sobre lo mismo.

### Los tipos de emisión son familias, no modos sueltos

El original guarda `CW`, `PHONE` o `DIGITAL`; el recurso los conserva tal cual y el lector los
traduce a `ClaseDeModo` del contrato. Importa porque los reglamentos hablan de familias: una
variante de fonía tiene que aceptar **SSB, AM y FM**, y exigir un modo ADIF concreto dejaría
fuera contactos que cuentan.

### Un diploma que no se sabe calcular se marca, no se calcula mal

Si el original trae un diploma que cuenta por un campo del contacto que el motor no sabe
leer, el guion lo marca `calculable = 0` con el motivo; el motor devuelve cero y lo explica en
`ProgresoDeDiploma.PorQueNoEsFirme`. Un diploma mal calculado es peor que no calcularlo.

Hay un segundo filtro que el recurso no puede ver: **que la columna exista de verdad en el
cuaderno del operador**. El catálogo se actualiza por su cuenta y la base puede ser más vieja,
así que el motor lee `pragma_table_info('qso')` al conectar y, si falta la columna que el
diploma necesita, no lanza la consulta y dice cuál falta. Le pasa a `SIOTA`, que cuenta por
`SIG_INFO`, mientras esa columna no llegue al esquema.

## Cifras de la última generación

| | |
|---|---:|
| Diplomas | 87 |
| Variantes | 390 (315 del original + 75 sintéticas) |
| Referencias | 553.064 en 83 ficheros |
| Entidades permitidas normalizadas | 1.536.531 |
| Informes | 5 |
| Recurso en claro | 50 MB |
| Recurso comprimido e incrustado | 7,4 MB |
| Catálogo compilado (SQLite indexado) | 168 MB, 14 s la primera vez |
