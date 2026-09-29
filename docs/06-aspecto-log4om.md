# 06 — El aspecto de Log4OM: qué copiar y qué no

El operador dijo, tras ver nuestra interfaz: **«no me gusta mucho como está, me gusta más Log4OM»**.
Preguntado por qué, respondió que lo que prefiere es **el aspecto: colores, iconos, barras de
herramientas** — no la disposición ni los paneles acoplables.

Con su permiso abrí su Log4OM 2.40.0.0 y lo capturé (la captura no se publica: enseña su cuaderno real).
Esto describe lo que se ve ahí. Observado el **22-09-2026** por el team lead.

## Regla previa

**Nada se copia literalmente.** Ni sus iconos, ni sus imágenes, ni su nombre. Lo que se toma es
el **lenguaje visual**: qué es oscuro y qué claro, qué información se enseña a la vez, cómo se
agrupa y con qué densidad. Eso no es de nadie; los dibujos sí.

## Lo que define su aspecto

### 1. Cromo oscuro, contenido claro

Barra de menú, barra de herramientas y barra de estado son **gris muy oscuro, casi negro**, con
texto claro. Los paneles de contenido van en **gris medio**, y los campos editables en blanco o
gris muy claro. El contraste entre el marco oscuro y el contenido claro es lo primero que se ve.

Lo nuestro es todo claro, con acentos azules. Ahí está la mitad de la diferencia.

### 2. El visor de frecuencia de siete segmentos

Arriba a la izquierda, un **visor tipo LCD ámbar sobre negro** con dígitos de siete segmentos,
que ocupa una cuarta parte del ancho. Cuando no hay radio pone `OFFLinE` con esa misma
tipografía. Es el elemento más reconocible de la pantalla.

Nosotros ya tenemos el frontal dibujado, que cumple ese papel y va más allá. **No hay que
añadir un visor aparte**, pero sí conviene que el nuestro mantenga esa presencia: grande,
oscuro, con la frecuencia dominante.

### 3. La franja solar, verde sobre negro

Arriba a la derecha, en una sola línea sobre fondo negro y en verde:

```
Kp: 2 (Quiet)   A: 3   SFI: 104   Sunspot: 85   -   SR: 06:25Z   SS: 19:23Z
```

Índice Kp con su calificación en palabras, índice A, flujo solar, manchas, y las horas de orto
y ocaso. **Esto ya lo tenemos calculado** en el módulo de propagación, con su aviso de
antigüedad, y no se está enseñando. Es barato y es de lo que más mira un operador.

### 4. Pestañas rotuladas con su tecla de función

Dos filas de pestañas, cada una con su atajo en el propio rótulo:

- `Stats (F1)` · `Info (F2)` · `Awards (F3)` · `My (F4)` · `Extended (F5)`
- `Main (F6)` · `Recent QSO's (F7)` · `Cluster (F8)` · `Propagation (F9)` · `Worked before (F10)` · `Weather (F11)`

Poner la tecla en el rótulo es lo que hace que el operador acabe usándola. Nosotros tenemos los
atajos solo en la barra de estado, donde no se miran.

### 5. La matriz de novedad, que es lo mejor que tienen

Una rejilla de tres columnas —`NEW ONE`, `NEW BAND`, `NEW MODE`— cruzada con cuatro filas de
vías de confirmación —`QSL`, `EQSL`, `LOTW`, `QRZCOM`—, y debajo los rótulos `Country - Call`,
`Band - Call`, `Mode - Call`.

De un vistazo dice **si el indicativo que estás tecleando es nuevo**, y por qué vía lo tienes
confirmado. Es la respuesta a «¿le llamo o no?» sin leer una sola frase.

**Nuestro motor de diplomas ya calcula esto** (`QueAportaAsync`, en centésimas de milisegundo)
y no se enseña en ninguna parte.

### 6. La matriz de banda por modo

Debajo, una rejilla con las bandas en columnas (`160 80 60 40 30 20 17 15 12 10 6 4 V U`) y tres
filas: `PH`, `CW`, `DIG`. Cada casilla se colorea según lo trabajado y lo confirmado con ese
indicativo.

Es la vista de «trabajado antes» condensada en catorce por tres casillas. Lo nuestro lo dice con
una frase.

### 7. La columna del bandmap

A la derecha, una **escala vertical de frecuencia** en tono tierra con las marcas numeradas
(`24.700`, `24.800`, `24.890`, `24.900`, `24.990`, `25.000`, `25.100`, `25.200`) y los anuncios
del cluster colocados a su altura. Abajo, `Scale 1x` y los filtros `WKD` · `BAND` · `MODE`.

Es una forma de ver el cluster **por frecuencia** en vez de por lista. Nosotros solo tenemos la
lista.

### 8. La barra de estado con testigos de colores

Abajo: `QSO Count 1838` y luego los servicios como **pastillas de color**, verde cuando están
conectados: `Cluster` · `Cluster server` · `Super Cluster` · `CAT` · `FLDigi`. A la derecha, la
ruta del fichero del cuaderno.

Se ve de un golpe qué está conectado y qué no. Lo nuestro lo dice con texto suelto.

### 9. Barra de iconos

Una fila de iconos pequeños y de colores, cada uno con su función: globo, antena, trofeo,
cámara, micrófono, radio. Iconos con color, no siluetas grises.

## Qué NO copiar

- **Sus iconos y sus imágenes.** Se dibujan los nuestros.
- **El gris de Windows Forms clásico** de sus marcos y recuadros hundidos. Esa parte es vejez,
  no diseño; se puede tener cromo oscuro y denso sin parecer de 2003.
- **Los rótulos en inglés.** Todo lo nuestro va en español.
- **Los paneles acoplables.** Preguntado expresamente, el operador eligió el aspecto, no la
  disposición.

## Lo que este documento pide de verdad

Ordenado por lo que más cambia la sensación:

1. **Tema oscuro por omisión**, con cromo oscuro y contenido claro.
2. **La franja solar** arriba a la derecha, con datos que ya tenemos.
3. **La matriz de novedad** junto a la entrada de contacto, con datos que ya tenemos.
4. **La matriz de banda por modo** para el trabajado antes.
5. **Los atajos en los rótulos** de las pestañas.
6. **La barra de estado con pastillas de color** por servicio.
7. **Iconos propios con color** en una barra de herramientas.
8. **El bandmap** por frecuencia, más adelante.

Los puntos 2, 3 y 4 no son trabajo de diseño: son **datos que el programa ya calcula y que nadie
está enseñando**. Eso es lo que hace que lo nuestro parezca más pobre de lo que es.
