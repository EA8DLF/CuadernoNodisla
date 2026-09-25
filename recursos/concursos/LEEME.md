# Catálogo de concursos de Cuaderno NODISLA

Aquí vive el recurso que usa `src/Nodisla.Cuaderno.Concursos/` para saber qué pide
cada concurso: qué intercambio, cómo se puntúa, qué cuenta como multiplicador, en qué
bandas y modos, y cuándo se celebra.

## Qué hay en esta carpeta

| Fichero | Qué es |
|---|---|
| `reglas-concursos.tsv` | Las reglas. **Este sí se edita a mano.** |
| `concursos-nodisla.tsv` | El recurso. Se genera; no se edita a mano. |
| `generar-concursos.py` | El guion que fabrica el recurso. |

El mismo texto de `concursos-nodisla.tsv` queda comprimido en
`src/Nodisla.Cuaderno.Concursos/Recursos/catalogo-concursos.tsv.gz` e incrustado en el
ensamblado, igual que se hizo con el catálogo de diplomas. Así el motor de concursos no
depende en ejecución de ningún fichero de Log4OM ni de nada suelto en disco.

## Qué fuente se usó y por qué hay una parte escrita a mano

El original guarda su catálogo de concursos en `%AppData%\Log4OM2\contest.csv`, y lo
que guarda es exactamente esto:

```
CQ WW DX Contest (CW);CQ-WW-CW
```

242 líneas con el nombre y el identificador Cabrillo. **Ni una regla, ni una fecha, ni
un multiplicador.** Log4OM no puntúa concursos: solo etiqueta el contacto con su
`CONTEST_ID` para que salga bien en el ADIF y en el Cabrillo. Eso significa que aquí no
había nada que convertir, y que las reglas hay que escribirlas.

Por eso el recurso se fabrica con dos entradas:

- de `contest.csv`: los **242 identificadores Cabrillo y sus nombres**, que es lo que
  hace falta para que un contacto marcado en Log4OM siga significando lo mismo aquí;
- de `reglas-concursos.tsv`: las **reglas de los concursos que el operador trabaja de
  verdad**, escritas a mano.

Los concursos sin reglas escritas no desaparecen: entran con estado `SoloCatalogo`. Se
pueden seleccionar, etiquetan bien el contacto y exportan Cabrillo; lo único que no hacen
es puntuar solos.

## El aviso que hay que leer antes de enviar un log

Las reglas escritas a mano están marcadas **`Aproximado`**. Se han escrito a partir del
conocimiento general de cada concurso, **no leyendo las bases del año en curso**. Las
bases cambian todos los años: bandas, horas, categorías, multiplicadores y hasta la
puntuación. El programa enseña ese aviso al abrir una sesión cuyo estado no sea
`Verificado`, y el marcador que calcula es **la cuenta del operador, no la del
organizador**: el organizador recalcula siempre desde el Cabrillo.

Cuando se contrasten las bases de un concurso con su organizador, se cambia su estado a
`Verificado` en `reglas-concursos.tsv` y se vuelve a generar.

Lo que **sí** está verificado contra su especificación es el **formato Cabrillo**
(cabeceras y línea `QSO:`), contra la especificación pública de la WWROF. Eso es lo que
lee el robot del concurso, y es donde un error cuesta el log entero.

## Formato del recurso

Texto separado por tabuladores, una línea por registro, la primera columna dice de qué
tipo es. Las líneas que empiezan por `#` son comentarios. Los campos vacíos al final de
la línea se recortan, así que hay que leer tolerando líneas cortas. Sin tildes, porque lo
leen dos lenguajes distintos.

```
V  version  fechaDeGeneracion  numeroDeConcursos  numeroConReglas

C  codigo  nombre  patrocinador  estado  modos  bandas  enviado  recibido
   mes  ordinal  completo  dia  horaInicio  duracionHoras  ambitoDuplicado  notas

P  codigo  ambito  puntos  bandas  modos        (en orden; gana la primera que case)

M  codigo  tipo  alcance
```

| Campo | Valores |
|---|---|
| `estado` | `Verificado`, `Aproximado`, `SoloCatalogo` |
| `modos` | `CW`, `SSB`, `RTTY`, `DIGI`, `FM`, `MIXED` |
| `bandas` | lista ADIF separada por comas (`160m,80m,40m`) o vacío |
| `enviado` / `recibido` | `Informe`, `Serie`, `ZonaCq`, `ZonaItu`, `Locator`, `Seccion`, `Provincia`, `Estado`, `Nombre`, `Edad`, `Potencia`, `Socio`, `Texto` |
| `mes` | 1–12, o 0 si el recurso no fija la fecha |
| `ordinal` | 1–4, o 5 para «el último» |
| `completo` | `S` si la semana tiene que caber entera en el mes |
| `dia` | `lun`…`dom` |
| `horaInicio` | `HHMM` en UTC |
| `ambitoDuplicado` | `Banda`, `BandaYModo`, `Unico` |
| `ambito` (P) | `Cualquiera`, `MismoPais`, `MismoContinente`, `MismoContinenteNa`, `OtroContinente`, `MismaZonaCq`, `MismaZonaItu` |
| `tipo` (M) | `Dxcc`, `ZonaCq`, `ZonaItu`, `PrefijoWpx`, `Locator`, `CampoLocator`, `Seccion`, `Provincia`, `Estado`, `Sociedad` |
| `alcance` (M) | `Global`, `PorBanda`, `PorModo`, `PorBandaYModo` |

## Cómo se regenera

```
python generar-concursos.py
```

Lee `%AppData%\Log4OM2\contest.csv` **en solo lectura**. Si no está, avisa y genera el
recurso únicamente con lo escrito a mano.
