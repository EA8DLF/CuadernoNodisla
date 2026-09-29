# 05 — El frontal del FT-710: referencia para dibujarlo

El operador envió una **fotografía oficial del producto** y dijo: «esta imagen quiero». Este documento
describe lo que se ve en ella, para poder dibujarlo. Descripción tomada de la foto el
**22-09-2026** por el team lead.

## Regla previa, no negociable

**Se dibuja de cero en vector. No se usa la fotografía dentro del programa, ni se reproduce el
logotipo ni la marca «YAESU» ni «FT-710» como rótulo de marca.** Esto es una herramienta propia
para uso particular, no una imitación del producto. El parecido se consigue con la disposición,
las proporciones y los colores, que es lo que hace que el operador reconozca su radio.

En el sitio donde la foto lleva la marca, escribimos lo nuestro o no escribimos nada.

## Lo que se ve

### Conjunto

Cuerpo **negro mate**, apaisado. En la foto, a la izquierda hay un bloque perforado de rejilla
de altavoz que **hay que verificar si forma parte del equipo o es un altavoz externo aparte**
(las medidas del manual —239 × 80 × 247 mm— dan un frente de unos 3:1, y el conjunto de la foto
es bastante más ancho). **Si no se confirma, se dibuja solo el cuerpo con los mandos.**

De izquierda a derecha, el cuerpo tiene tres zonas: columna de botones, pantalla en color, y
bloque de mandos a la derecha.

### Columna izquierda (botones verticales, uno debajo de otro)

`⏻ LOCK` (con un piloto azul debajo) · `TUNE` (con piloto) · `VOX MOX` · `🎧 PHONES` (jack) ·
`MIC` (conector rectangular).

Botones **rectangulares alargados, gris muy oscuro, con el rótulo en blanco pequeño**.

### La pantalla — es el corazón, y ahora está medio vacía en nuestro dibujo

Pantalla **TFT en color sobre fondo negro**, apaisada. Lo que muestra, por zonas:

**Franja superior**
- A la izquierda, un **medidor analógico en arco** con dos escalas: `S 1 3 5 7 9 +20 +40 +60 dB`
  arriba y `PO 0 10 50 100 150 W` debajo, con **aguja roja**.
- A la derecha del medidor: etiqueta `VFO-A`, el modo en **recuadro azul** (`USB`), y la
  frecuencia en **cifras blancas grandes con puntos de millar**: `14.195.000`.
- Debajo: `VFO-B`, modo en **amarillo** (`CW-U`), frecuencia en **gris más pequeña**
  (`3.550.000`), y un recuadro rojo `LEVEL`.

**Segunda franja** — cuatro indicadores con el rótulo en **naranja** y el valor en blanco:
`ATT OFF` · `IPO AMP1` · `DNF OFF` · `AGC AUTO`. A su derecha, en azul:
`CENTER FAST2 SPAN 100kHz`.

**Zona central — el analizador de espectro y la cascada.** Es lo que más ocupa: señales en
**azul y rojo sobre negro**, con escala horizontal `-40k · -20k · 14.195.000 · +20k · +40k` y un
**marcador rojo en forma de triángulo** sobre la frecuencia sintonizada.

**Franja inferior** — botonera táctil: `CENTER` · `3DSS` (en azul, el activo) · `MULTI` ·
`EXPAND` · `SPAN` · `SPEED`.

### Bloque derecho

**Dos filas de botones** arriba, del mismo estilo que la columna izquierda:
- `MODE` · `ZIN/SPOT` · `SPLIT` · `CLAR` · `NB`
- `M▸V` · `V/M (W)` · `A/B` · `BAND` · `QMB`

**Debajo**, de izquierda a derecha: mando `FUNC`, botón `DNR`, piloto `BUSY/TX`, botón `NAR`,
mando `RF GAIN/SQL`.

**El dial principal**: knob **grande, negro, con hendidura para el dedo**, rodeado de un
**anillo iluminado en azul**. Es el elemento visual dominante de la mitad derecha. Rotulado
`MAIN` en la foto por el texto de la imagen, no en el equipo.

**Abajo**: mando `STEP/MCH` con la etiqueta `DSP` en recuadro, botón `DSP RESET`, mando
`AF GAIN` y botón `FINE/FAST`.

### Colores

- Cuerpo: negro mate, casi carbón.
- Botones: gris muy oscuro, un punto más claro que el cuerpo, rótulo blanco.
- Mandos: negro con el canto **estriado** y brillo metálico en el borde.
- Anillo del dial principal: **azul intenso**.
- Pantalla: fondo negro; blanco para la frecuencia principal, azul para el modo activo y los
  ajustes del espectro, naranja para los indicadores de recepción, amarillo para el VFO-B,
  rojo para la aguja del medidor y el marcador de frecuencia.

## Qué hay que hacer con esto

1. **Que el frontal se vea siempre.** Hoy no se ve: el repliegue que existía para la letra
   grande salta también con la ventana a su tamaño normal, y en su lugar sale un cartel que
   dice «con este tamaño el frontal dibujado no cabe entero». El dibujo tiene que **escalar**
   como vector, no desaparecer.
2. **Llenar la pantalla del equipo.** Los dos VFO con la tipografía y los colores de arriba, el
   medidor en arco con su aguja, los indicadores en naranja y, donde el equipo pone el
   analizador de espectro, lo nuestro: de momento el estado de operación; en la Fase 4, la
   cascada de verdad del módem propio.
3. **Densidad.** La interfaz actual está muy espaciada y con la letra muy grande, y eso es la
   mitad de por qué «no parece operativa». Cabina significa mucha información útil de un
   vistazo, no pocos controles muy grandes.

## Disposición de la pestaña «Operar» (confirmada por el operador)

```
┌──────────────────────────────────────────────────────┐
│  FRONTAL DEL FT-710  (todo el ancho)                 │
├───────────────────────┬──────────────────────────────┤
│  Contacto nuevo       │  Cluster de DX               │
└───────────────────────┴──────────────────────────────┘
```

Textual suyo: «el cluster debajo de eso y a la izquierda del cluster para agregar un contacto».
