# Digital

Los modos digitales tienen su propia pestaña de primer nivel, con el mismo frontal del equipo
arriba. FT8 y FT4 los hace **esta aplicación**: no hay que abrir WSJT-X ni JTDX. El audio entra
por el codec USB del equipo, la cascada se calcula aquí y las decodificaciones salen tal cual se
ven en pantalla.

![La pestaña Digital: módem propio a la izquierda, puente externo a la derecha](../capturas/ayuda/digital.png)

## El módem propio

Es el camino de casa, y ocupa la columna principal. Tres cosas mandan sobre cómo se ve:

### El reloj, antes que nada

FT8 va en ventanas de quince segundos alineadas al reloj universal (FT4, de 7,5 segundos). Un
ordenador con el reloj un segundo desviado decodifica mal; con dos segundos, ya no decodifica
nada y además **transmite fuera de ventana** — que deja de ser un problema propio y pasa a ser
un problema ajeno: ocupa el período de otra estación y le ensucia las decodificaciones sin que
nadie se entere. Por eso el reloj va arriba del todo, con un semáforo de colores, y no escondido
en Ajustes.

- **Medir** pregunta la hora a los servidores de hora y recalcula el desvío.
- **Poner el reloj en hora** mide y escribe la hora del sistema — nunca se hace solo, siempre es
  un botón que pulsa el operador.
- **Configurar el servicio de hora** deja el servicio de Windows apuntando a un buen servidor,
  que arregla el problema para siempre y no solo por hoy.

### La cascada

El espectro de audio en el tiempo, con la frecuencia en el eje horizontal. Pinchar en la cascada
pone ahí el **tono de transmisión**. Debajo, el medidor de **nivel de entrada** avisa y no solo
pinta: por encima de 0,90 el codec recorta y las señales débiles se pierden — la barra se pone
roja y lo dice con letras — y lo bueno está entre 0,20 y 0,60.

### Decodificación normal y decodificación «rescatada»

Cada línea de la lista de decodificaciones lleva la hora, la relación señal-ruido en decibelios,
el desfase respecto a la ventana, el tono en hercios y el mensaje. Los indicativos nuevos salen
en verde y en negrita; lo que va dirigido a la propia estación, en el color de acento.

Una decodificación puede salir marcada **«RESCATADA»**: son señales muy débiles que solo se han
podido leer gracias al bloque de corrección profunda del módem, con algo más de riesgo de ser
falsas que una decodificación normal. La marca existe para que se puedan mirar con más cuidado
antes de apuntar un contacto con ellas — no para desconfiar de todas, solo de saber cuáles son.

### Transmitir cuesta abrir un pestillo

Emitir un período de FT8 son **trece segundos con el equipo en antena**. Por eso hay un
interruptor, «Permitir transmitir en esta sesión», que **empieza cerrado en cada arranque y no
se guarda de una sesión a otra**: hay que abrirlo cada vez, y solo con la antena o la carga
artificial puestas. Con el pestillo cerrado, «Emitir» no hace nada. Con él abierto, «Cortar y
soltar PTT» corta la emisión y suelta el PTT sin preguntar.

Si prefiere que el pestillo arranque abierto, marque **«Recordar «Permitir transmitir» entre
sesiones»** en Ajustes › Audio y modos digitales (viene apagado).

Antes de cada emisión sale además una pregunta de confirmación. Tiene una casilla **«No volver a
preguntar»**: márquela y pulse «Sí, transmitir», y ya no se pregunta más. Para que vuelva a
preguntar, marque **«Preguntar antes de transmitir con el módem»** en Ajustes › Audio y modos
digitales. Quitar la pregunta no quita la seguridad: el vigilante del PTT (tiempo máximo en
antena, suelta si se pierde el latido o se cierra el programa), la negativa a emitir con el reloj
fuera de ventana y la negativa a emitir sin salida de audio siguen siempre.

![El diálogo de transmitir con la casilla «No volver a preguntar»](../capturas/ayuda/dialogo-transmitir.png)

El bloque «Contacto y transmisión» tiene además los datos del corresponsal — indicativo,
localizador, informes enviado y recibido — y el botón «Guardar en el cuaderno», que apunta el
contacto con la frecuencia del dial más el tono de audio, y el modo tal como lo pide ADIF: FT8
va como modo principal y FT4 como submodo de MFSK.

### El contacto se guarda solo al completarse

Con la secuencia automática, el contacto **se apunta solo en el cuaderno** en cuanto está hecho,
sin pulsar «Guardar en el cuaderno»:

- cuando el corresponsal manda RR73, RRR o 73 después de intercambiar informes, o
- cuando sale su RR73 después de haber recibido su R+informe.

Se guarda con indicativo, localizador, informes enviado y recibido, frecuencia (dial más tono de
transmisión), modo ADIF, hora de inicio (la primera emisión o recepción del contacto) y hora de
fin. En la pantalla aparece un aviso corto, por ejemplo «Guardado: EA1ABC 20m FT8», y se
refrescan el cuaderno, el mapa, los diplomas y las subidas automáticas, igual que al guardar a
mano. Si el corresponsal repite el 73 no se apunta dos veces, y un contacto que se queda a medias
no se apunta. Si no se sabe la frecuencia del dial no hay banda que apuntar: sale el aviso y se
guarda con el botón. Se apaga en Ajustes › Audio y modos digitales, con **«Guardar solo en el
cuaderno el contacto completo»**.

## El puente con WSJT-X y JTDX

Es la **alternativa**, no el camino de casa: para quien ya tenga su montaje hecho, o para
comparar decodificaciones mientras se afina el módem propio. Escucha por UDP lo que diga el
programa de fuera — WSJT-X, JTDX o compatibles — y muestra sus decodificaciones igual que si
fueran del módem propio.

Como JTDX no ofrece exactamente las mismas funciones que WSJT-X por su propia API, **cada botón
se pregunta primero qué admite el programa conectado**: lo que esa instancia nunca va a poder
hacer se esconde en vez de quedarse gris para siempre, y lo que solo falta un paso para hacer
—como elegir una decodificación— se queda a la vista, en gris, hasta que se da ese paso. Lo que
sale al aire lo dice: «Llamar CQ — TRANSMITE» va en ámbar y aparte de los botones inocuos.

Cuál de los dos caminos usa la aplicación — el módem propio o el puente — se elige en
**Ajustes → Audio y modos digitales**, y el cambio se aplica al volver a abrir el programa.
