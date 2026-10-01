# 13 · Revisión botón a botón: Ajustes, Satélites, Imprimir y Ronda

Fecha: 27-09-2026. Pestañas Ajustes (Ctrl 6), Satélites (Ctrl 7), Imprimir (Ctrl 8) y Ronda (Ctrl 9).

## Cómo se ha revisado

- **Instancia apartada y simulada**: `CUADERNO_APARTADA=1`, `CUADERNO_SIMULADO=1`, `CUADERNO_PESTANA=5..8`,
  `CUADERNO_CARPETA` en una carpeta temporal (ni `cuaderno.sqlite` ni `ajustes.json` de Jose) y la red
  cortada con un proxy muerto. Sin radio, sin COM15, sin salida de audio y sin robar el foco.
- **Enlaces rotos**: el fichero de `CUADERNO_RASTREO_ENLACES` sale vacío aunque haya enlaces rotos
  (el rastreo de WPF no escribe sin depurador). La medida buena es el `.rotos.txt` que deja
  `RetratoDeLaVentana` junto a la captura, que pregunta a cada enlace vivo cómo está.
- **Pruebas**: cada orden, con dobles de mentira (almacén de credenciales, equipo de dos VFO,
  red, generador de impresos, repositorio de rondas, hora fija). Las pestañas se pintan de
  verdad en una ventana fuera de la pantalla, a los dos tamaños, y se pulsan por los caminos de
  la accesibilidad. Ningún reloj de pared: la hora de Satélites se inyecta (`TimeProvider`).
- **CAT y conexión al cluster** no se ven con los puertos simulados (no hay equipo ni nodo de
  verdad que configurar). Se han revisado pintando sus vistas con modelos de mentira en las
  pruebas; la prueba contra el FT-710 real es cosa del especialista del CAT.

## Cifras

| | Antes | Después |
|---|---|---|
| Controles revisados | 119 | 119 |
| Rotos (no hacían nada o hacían mal) | 18 | 0 |
| A medias (hacían su trabajo pero la pantalla mentía o dejaba pulsar sin sentido) | 20 | 0 |
| Enlaces rotos (`.rotos.txt`) con la ventana en Ajustes | **50** | **0** |
| Enlaces rotos en Satélites, Imprimir y Ronda | 1 (título del satélite, sin nada elegido) | 0 |
| Pruebas de estas pestañas | solo las del CAT y las del audio | 38 nuevas (órdenes y pantalla) |

Los 50 enlaces rotos de Ajustes eran las vistas del CAT y de la conexión al cluster, que se
montaban siempre —ocultas y con el contexto nulo— aunque con los simulados no hubiera modelo
detrás. Ahora cada apartado se pinta por el tipo de su modelo: sin modelo, no se monta nada.

## Ajustes (Ctrl 6)

| Control | Antes | Qué se hizo | Después |
|---|---|---|---|
| 8 × casilla de credencial + **Guardar** | Guardaba, pero los puntos se quedaban en la casilla: parecía que no se había guardado | La casilla escucha al modelo y se vacía al guardar o borrar; Guardar solo se enciende con algo tecleado | Guarda cifrado, vacía la casilla y la pastilla pasa a «Guardada y cifrada» |
| 8 × **Borrar** | Encendido aunque no hubiera nada guardado | Solo se enciende si hay algo guardado | Correcto |
| Volver a comprobar (LoTW) | Funcionaba | — | Probado: trae el motivo y la cifra del cuaderno |
| **Importar ADIF…** | Con los simulados el lector era de mentira: siempre fallaba | Orden del modelo; lector ADIF real también con los simulados (lee ficheros, no toca red ni equipo); apagado mientras trabaja | Importa y da el parte completo |
| **Exportar ADIF…** | **No hacía nada**: decía «el fichero no se ha escrito» | Exporta el cuaderno entero con el escritor ADIF, por páginas, a un temporal que solo se pone en su sitio si todo va bien (un fallo no destroza un respaldo con ese nombre). Sin escritor: apagado y rotulado | Escribe el fichero; probado que se vuelve a leer con los 300 contactos |
| Datos que no cuadraban (desplegable) | Funcionaba | — | Correcto |
| CAT · Vía, detección, puerto, velocidad, rigctld, OmniRig, tiempos, PTT, Probar, Aplicar | Funcionaban | — | Probados con montaje de mentira; Probar con «Ninguna» no monta nada |
| CAT · **Descartar** | No devolvía el puerto serie a lo guardado | Recoge también el puerto | Vuelve vía, velocidad, puerto y tiempos |
| Audio · entrada, salida, muestras, Probar el nivel, Parar, Volver a buscar, Guardar, Descartar | Funcionaban | — | Probados con la entrada simulada: abrir y cerrar, guardar en el fichero y descartar |
| Cluster · **Nodos conocidos** | Tras borrar o cambiar el servidor, volver a elegir el mismo nodo no hacía nada | Al teclear otro servidor o puerto el desplegable suelta el nodo | Elegir el nodo rellena servidor y puerto siempre |
| Cluster · **Indicativo** | La pastilla «con qué indicativo se entra» no se enteraba de lo tecleado | Aviso de cambio | Se actualiza al teclear (sufijo incluido) |
| Cluster · contraseña + **Guardar/Borrar** | Igual que las credenciales: puntos que se quedaban, botones siempre encendidos | Igual que las credenciales | Correcto |
| Cluster · Aplicar, Descartar, guion, tiempos, reconectar | Aplicar sin servidor no encendía el recuadro del parte | El parte avisa al cambiar | Probado: avisa en rojo sin servidor, guarda con él, Descartar vuelve |
| Cluster con los simulados | Desaparecía sin decir nada | Rótulo honesto: con los simulados no hay nodo que configurar | Se lee por qué |
| Consola del cluster (Conectar/Desconectar, orden, Enviar) | Funcionaba | — | Sin enlaces rotos |
| Disposición | Credenciales legibles a los dos tamaños | Prueba que exige más de 200 px de casilla y nada cortado por la derecha a 1920×1000 y 1366×768 | Correcto |

## Satélites (Ctrl 7)

| Control | Antes | Qué se hizo | Después |
|---|---|---|---|
| Actualizar elementos orbitales… | Funcionaba (única puerta a la red) | Apagado mientras descarga, por su orden | Probado con red de mentira: carga, guarda la caché y la siguiente vez se lee del disco |
| Actualizar pasos | Funcionaba | — | Probado: no sale a la red |
| Lista de satélites (elegir) | Título enlazado a `SateliteSeleccionado.Nombre`: camino roto sin nada elegido | Propiedad propia | 0 enlaces rotos |
| Transpondedor | Funcionaba | — | Correcto |
| VFO de subida / bajada | Se podía poner el mismo en los dos: se escribía la bajada y encima la subida, y el receptor quedaba en la frecuencia de transmisión | «Seguir» se niega y lo dice | Correcto; el reparto se guarda |
| **Seguir** | Encendido con un equipo sin dos VFO; sin elementos decía «Siguiendo» y no movía nada | Apagado sin dos VFO; sin elementos no empieza y dice qué hacer | Probado: escribe bajada (B) y subida (A), nunca PTT |
| Soltar | Funcionaba | — | Probado: soltado ya no escribe |
| Aviso de posición | Decía «Por debajo del horizonte» también cuando no había elementos | Distingue «sin elementos» de «bajo el horizonte» | Correcto |

## Imprimir (Ctrl 8)

| Control | Antes | Qué se hizo | Después |
|---|---|---|---|
| Desde, Hasta, Bandas, Indicativos, vías, pendientes, excluir | Funcionaban | — | Probados; un filtro vacío lo dice |
| **Buscar** | Tras la búsqueda automática se quedaba gris | Aviso de cambio al terminar | Correcto |
| **Casilla de cada etiqueta** | Desmarcar a mano no cambiaba el resumen ni apagaba la vista previa | Cada casilla avisa | El resumen y el botón siguen a las casillas |
| Marcar todas / Ninguna | Encendidos sin etiquetas | Solo con etiquetas | Correcto |
| Plantilla | Funcionaba | — | Correcto |
| **Empezar en la etiqueta número** | Un 0 o un 99 se quedaba en pantalla | Se acota en la propia casilla | Correcto |
| **Frase al pie** | Cambiarla después de Buscar no llegaba al PDF | La vista previa usa la frase de ahora | Probado |
| Vista previa / Imprimir… | Solo con etiquetas; con todas desmarcadas seguía encendido | Solo con alguna marcada; el visor se inyecta en las pruebas (no se abre nada) | Probado: PDF escrito y abierto una vez |

## Ronda (Ctrl 9)

| Control | Antes | Qué se hizo | Después |
|---|---|---|---|
| **Banda / Modo** | Salían **vacíos y no se podía escribir**: el estilo de la casa no admite desplegables editables | Desplegables normales con la banda y el modo elegidos | Se ven «40m» y «SSB» |
| Nombre, club, frecuencia, perfil, notas | La frecuencia mal tecleada se convertía en cero en silencio | Se avisa y no se abre | Correcto |
| **Abrir ronda** | Los errores («falta el nombre», «el modo no vale») se escribían en una barra que no se ve sin ronda abierta | Mensaje también en la tarjeta de abrir, con su color; sin nombre el botón está apagado | Correcto |
| Cerrar ronda | Funcionaba | — | Probado: pasa al historial con su fin |
| Indicativo + Añadir (e Intro) | Funcionaba; rótulo «Anadir» | Rótulo «Añadir», mayúsculas, aviso al entrar | Probado, también con indicativo malo |
| **RST TX, RST RX, Comentario** en la lista | **No se podían escribir**: la rejilla heredaba «solo lectura» del estilo del cuaderno | Rejilla editable (las demás columnas siguen de solo lectura) | Se escriben en la lista |
| **Guardar RST/comentario** | No tenía nada que guardar; tampoco decía nada | Con lo anterior, guarda y lo dice | Probado |
| **Confirmar trabajado** | Seguía encendido tras confirmar: un segundo clic metía el contacto otra vez | Se apaga al confirmar | Probado: un solo QSO en el cuaderno, con RST, comentario y banda |
| Quitar | Funcionaba, sin aviso | Aviso (y si ya estaba confirmado, que el contacto sigue en el cuaderno) | Correcto |
| Cabecera de la ronda | «Club o evento:» vacío; « · » colgando sin frecuencia | Se oculta vacío; frecuencia con «MHz» solo si la hay | Correcto |
| Historial | Fechas en crudo | `dd-MM-yyyy HH:mm`, «abierta» sin fin | Correcto |

## Capturas

En `docs/capturas/`, prefijo `rev13-`:

- Antes (1366×768): `rev13-antes-ajustes-1366.png`, `rev13-antes-satelites-1366.png`,
  `rev13-antes-imprimir-1366.png`, `rev13-antes-ronda-1366.png` (banda y modo vacíos).
- Después, a 1920×1000 y 1366×768: `rev13-ajustes-*.png`, `rev13-satelites-*.png`,
  `rev13-imprimir-*.png` (con la búsqueda hecha), `rev13-ronda-*.png`, y
  `rev13-satelites-so50-1366.png` (satélite elegido y «Seguir» pulsado sin elementos: lo dice).

## Pruebas

- `tests/Nodisla.Cuaderno.Ui.Pruebas/AjustesSatelitesImpresionRondaOrdenesPruebas.cs`: cada orden.
- `tests/Nodisla.Cuaderno.Ui.Pruebas/AjustesSatelitesImpresionRondaEnPantallaPruebas.cs`: las
  cuatro pestañas pintadas a 1920×1000 y 1366×768; sin enlaces rotos (preguntando a cada enlace),
  todo botón con orden, nada cortado por la derecha, y los botones pulsados.
- `tests/Nodisla.Cuaderno.Ui.Pruebas/DoblesDeAjustesSatelitesImpresionRonda.cs`: los dobles.

## Lo que queda fuera

- **Con los simulados el programa pide los índices solares a la NOAA al arrancar** (no es de
  estas pestañas). En esta revisión se cortó con un proxy muerto.
- El CAT contra el FT-710 real y la conexión a un nodo de verdad no se han probado aquí (reglas:
  ni COM15 ni red). Sus vistas y órdenes sí, con dobles.
- El historial de rondas usa la rejilla clara del cuaderno, igual que el Cuaderno: es el aspecto
  elegido, no un fallo.
