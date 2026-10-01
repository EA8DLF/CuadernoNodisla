# 11 · Revisión control a control: Cuaderno, Mapa, Diplomas, cabecera, franja solar, barra de estado y atajos

Fecha: 2026-09-27. Alcance final, tras el reparto: **Cuaderno (Ctrl 3), Mapa (Ctrl 4), Diplomas (Ctrl 5), cabecera, franja solar, barra de estado y atajos de teclado**. Ajustes, Satélites, Imprimir y Ronda pasaron a otro especialista; en esas cuatro pestañas no he tocado nada. El único cambio que les afecta es la paleta de la rejilla, que la Ronda comparte con el Cuaderno.

## Cómo se ha comprobado

- **Instancia apartada** (`CUADERNO_APARTADA=1`), compilada en una carpeta propia (`--artifacts-path`) para no chocar con las compilaciones de los otros especialistas. Se ha probado de dos formas:
  - simulada (`CUADERNO_SIMULADO=1`);
  - con una **copia** del cuaderno real de Jose en la carpeta temporal, gracias a la nueva variable `CUADERNO_CARPETA`. La base de Jose solo se ha leído, en modo solo lectura; no se ha escrito nada en ella.
- **Enlaces rotos**: el rastreo de WPF (`CUADERNO_RASTREO_ENLACES`) no escribía nada sin depurador. Lo sustituye `RetratoDeLaVentana.ApuntarEnlacesRotos`: recorre lo que hay dibujado, pregunta a cada enlace su estado y deja la lista en `<captura>.enlaces.txt.rotos.txt`.
- **Atajos**: con la nueva variable `CUADERNO_TECLAS=F3|Ctrl+D5|…`, que está en `Desarrollo/TeclasDePrueba.cs`, las teclas se entregan a la ventana apartada sin tocar el teclado de nadie. Después de cada tecla, el registro apunta la pestaña en la que queda la ventana y quién tiene el foco.
- **Pruebas nuevas**:
  - `PestanaCuadernoPruebas` (12);
  - `PestanaMapaPruebas` (3);
  - `PestanaDiplomasPruebas` (2);
  - `CompletarPerfilDeEstacionPruebas` (2);
  - `FrecuenciaEnOtraUnidadPruebas` (4).

  Ninguna usa la hora del reloj.

## Enlaces rotos (fichero `.rotos.txt`)

| Pantalla | Antes | Después |
|---|---|---|
| Cuaderno | 2 (`Cuaderno.PorQueNoHayFilas`, texto y visibilidad: la propiedad no existía) | 0 |
| Mapa | 0 propios | 0 |
| Diplomas | 0 propios | 0 |
| Siempre presentes, de otras áreas | `MemoriaElegida.Etiqueta` (Equipo), `SateliteSeleccionado.Nombre` (Satélites), `EnlaceRotoDePrueba` (Equipo, enlace de prueba) y, en modo simulado, unos 50 de los apartados de Equipo y Cluster de Ajustes, sin contexto de datos | Solo queda `MemoriaElegida.Etiqueta`. Los demás los han cerrado sus dueños o quedan pendientes de ellos |

## Tabla por control

### Rejilla del Cuaderno (lo que pidió Jose primero)

| Control / defecto | Antes | Qué se hizo | Después |
|---|---|---|---|
| Aspecto de la rejilla | Fondo oscuro con todo el texto en **negrita** blanca y monoespaciada | La negrita no venía de la rejilla. El estilo de `TabItem` ponía `FontWeight=Bold` a la pestaña elegida y **todo su contenido la heredaba**, en todas las pestañas. La negrita y el color van ahora solo en el rótulo de la cabecera (`TextoDePestana`, sin negrita para que quepa a 1366). La paleta `Rejilla*` es clara en los dos temas: blanco roto, alternancia sutil, cabecera gris medio y texto oscuro de peso normal. El texto va en Segoe UI y las cifras en Consolas sin negrita. El indicativo va en SemiBold | Contenido claro como en Log4OM, con el cromo oscuro (`rev11-cuaderno-despues.png`, `-claro.png`) |
| Columna QSL | Vacía en todas las filas | La consulta paginada **sí** carga las confirmaciones: `AsSplitQuery` trae `qso_confirmacion`, verificado en el registro SQL. Pasaba que la página 1 solo tiene envíos sin respuesta, y la pastilla solo pintaba lo recibido. En la base de Jose hay 12.565 filas de confirmación: eQSL recibidas 1.051, LoTW 247, papel 643 y QRZ 1.382. Ahora hay 4 estados: nada, **enviada** (letra gris), confirmada (contorno de color) y verificada (rellena). Una ayuda emergente lo explica | Se ven «E» en gris en los envíos a eQSL y L/E/Q de color en las confirmadas (`rev11-cuaderno-pastillas-qsl.png`) |
| 70 cm a 0,43300 | EG1CPI y EH1MDC con 0,433 MHz | Viene así del ADIF de Log4OM (`FREQ:0.433`, banda 70cm), no es un fallo nuestro de unidades. Al importar, si la frecuencia no cae en su banda y multiplicada o dividida por 1000 sí cae, se corrige y se avisa (`MapeoAdif.CorregirUnidadesDeFrecuencia`). Lo mismo con `FREQ_RX` | Las importaciones nuevas entran bien. **Los dos contactos que ya están en la base de Jose siguen a 0,433**: no he escrito en su base. Se arreglan con F2 o reimportando |
| Nombre y QTH con «…» | Anchos fijos. Solo crecía el comentario | Nombre (1,2*), QTH (1,4*) y comentario (1,6*) se reparten el sitio, con un mínimo en letras | Se leen enteros a 1920 |
| Frecuencia a escala 125 % | La cabecera salía cortada («FRECUENC…») | Ancho de 7,4 a 8,4 letras | Cabe |
| Flecha de orden | Salía doble en FECHA y HORA | Se marca solo la primera columna del campo | Una sola |
| Aviso de rejilla vacía | Enlazado a `PorQueNoHayFilas`, una propiedad que no existía | Se crea: dice si el cuaderno está vacío o si el filtro no deja pasar nada | Probado |
| Buscar (F3) | Funcionaba, pero la primera pulsación de F3 no ponía el foco en la búsqueda | Se espera a que el campo se vea y se enfoca con prioridad `Loaded` | Probado con teclas: el foco llega a `CampoBusqueda` |
| Banda, Modo, Por página | Funcionaban | Además, por página y las columnas elegidas **se guardan** en `paneles.json`. Antes se perdían al cerrar | Probado |
| Quitar filtro | Funcionaba | — | Probado |
| Modificar (F2), doble clic, Intro en la rejilla | Siempre encendido. Cargaba el contacto en el formulario de **Operar**, pero se quedaba en la pestaña Cuaderno, así que en pantalla no pasaba nada | Solo se enciende con una fila elegida. Al modificar se salta a Operar | Probado |
| Borrar (Supr) | Siempre encendido. Tras borrar, el contador de la barra de estado, el mapa y los diplomas seguían con el contacto | Solo se enciende con una fila elegida. Avisa `CuadernoCambiado` y se refresca todo | Probado (confirmar y cancelar) |
| Primera, Anterior, Siguiente, Última | Siempre encendidos, aunque no hubiera a dónde ir | `CanExecute` por página | Probado |
| Casillas de «Columnas del cuaderno» (17) | Funcionaban, pero no se guardaban | Se guardan y se recuperan | Probado |

### Mapa

| Control | Antes | Qué se hizo | Después |
|---|---|---|---|
| Casillas Contactos, Spots, Paso gris y Mapa de fondo | Funcionaban y se guardaban | — | Probado (filtro de contactos) |
| Contactos nuevos | El mapa solo se cargaba al arrancar. Lo registrado o borrado después no aparecía hasta reiniciar | `RefrescarTodoAsync` recarga el mapa y los diplomas | Probado |
| Quitar trayecto | Siempre encendido | Solo se enciende con un trayecto trazado | Probado |
| Ver el mundo | Funcionaba | — | — |
| Estación propia (triángulo) | Faltaba con el perfil real (ver Cabecera) | — | Sale EA8DLF en IL27HX |

### Diplomas

| Control | Antes | Qué se hizo | Después |
|---|---|---|---|
| Casillas del catálogo | Funcionaban: guardan en `mis-diplomas.txt` y calculan | — | Probado (el elegido sobrevive a reabrir) |
| Recontar, Ver detalle, ◀ ▶ | Funcionaban | — | Probado |
| Progreso tras registrar un contacto | No se recalculaba | Se recalcula al cambiar el cuaderno | — |

El `mis-diplomas.txt` de Jose está vacío, sin ningún diploma elegido. No es un fallo: todavía no ha marcado ninguno.

### Cabecera, franja solar y barra de estado

| Control | Antes | Qué se hizo | Después |
|---|---|---|---|
| Perfil de estación | El perfil «Casa» de Jose **no tiene localizador**, aunque sus contactos de Log4OM sí lo llevan (IL27HX). Cada contacto nuevo se guardaba sin `MY_GRIDSQUARE`, sin equipo y sin antena. La franja solar ponía «Orto — Ocaso —» y el mapa no sabía desde dónde trazar | Al arrancar, si al perfil le falta el localizador, se **completan solo los campos vacíos** con el último contacto de la misma estación y se guarda (`CompletarPerfilDeEstacion`). La ayuda emergente del selector explica lo que se hizo | Orto 06:52Z y Ocaso 18:52Z, EA8DLF en el mapa. **Se escribirá en la base de Jose la primera vez que abra esta versión** |
| Escala de letra, Tema oscuro (F9) | Funcionaban | — | Probado con teclas |
| Franja solar | Solo muestra datos; no tiene mandos | — | — |
| Pastillas Equipo, Cluster, Digital y LoTW | Solo muestran estado; no tienen mandos | — | — |
| Letra −, + y 100 % | Funcionaban | — | Probado (Ctrl+, Ctrl 0) |
| Leyenda de atajos | Faltaban Ctrl5, Ctrl6, Ctrl9 y Supr. A 1920 ocupaba dos renglones | «… F9 tema · Supr borra · Ctrl1…Ctrl9 pestañas (Ctrl9 ronda) · Ctrl+ y Ctrl− escala» | Un solo renglón a 1920 y a 1366 |

### Atajos (probados con `CUADERNO_TECLAS`)

| Tecla | Resultado |
|---|---|
| Ctrl 1 … Ctrl 9 | Cada una lleva a su pestaña (0…8). Correcto |
| F3 | Lleva al Cuaderno y pone el foco en la búsqueda. Antes, la primera vez no lo ponía; arreglado |
| F4 | Lleva a Operar y pone el foco en el indicativo. Correcto |
| F2 | Modifica la fila elegida y salta a Operar. Arreglado |
| F5 | Refresca el cuaderno. Correcto |
| F9 | Cambia el tema. Correcto |
| Ctrl + / Ctrl − / Ctrl 0 | Cambian la escala de letra. Correcto |
| Supr, Intro (en la rejilla) | Borrar, con confirmación, y modificar. Correcto |
| Esc, F7, F8, Intro en el formulario | Son del formulario de Operar (otro especialista). Enlazados a comandos que existen; no se han cambiado |

## Disposición

Se ha capturado a 1920×1000 y a 1366×768 el Cuaderno, el Mapa y los Diplomas. No hay nada cortado. A 1366, el rótulo en negrita de la pestaña elegida se cortaba («Diplomas (Ctrl 5»); ya no lleva negrita. El mapa del mundo ocupa el alto y deja franjas a los lados, que es como se ajusta a la ventana.

## Ficheros tocados

- **Rejilla y aspecto**:
  - `Recursos/Estilos.Cuaderno.xaml`;
  - `Recursos/Tema.Oscuro.xaml` y `Recursos/Tema.Claro.xaml` (paleta `Rejilla*`);
  - `Recursos/Estilos.Operacion.xaml` (estilo de `TabItem` y `TextoDePestana`, compartido);
  - `Vistas/PanelDelCuaderno.xaml`. Se ha reescrito: el corte de la sesión anterior lo dejó a ceros.
- **Cuaderno**: `Vistas/PanelDelCuaderno.xaml.cs`, `VistaModelos/FilaDeQso.cs`, `VistaModelos/VistaModeloCuaderno.cs`.
- **Mapa**: `VistaModelos/VistaModeloMapa.cs`.
- **Ficheros comunes**, con ediciones mínimas:
  - `VistaModelos/VistaModeloPrincipal.cs`;
  - `Vistas/VentanaPrincipal.xaml` y `Vistas/VentanaPrincipal.xaml.cs`;
  - `App.xaml.cs` (`CUADERNO_CARPETA`, y el rastreo se enciende antes de crear la ventana);
  - `Ajustes/EstadoDeLosPaneles.cs`.
- **Herramientas de desarrollo**: `Desarrollo/RetratoDeLaVentana.cs`, `Desarrollo/TeclasDePrueba.cs` (nuevo).
- **Importación y perfil**:
  - `Nodisla.Cuaderno.Adif/MapeoAdif.cs`;
  - `Nodisla.Cuaderno.Aplicacion/CasosDeUso/CompletarPerfilDeEstacion.cs` (nuevo).
- **Pruebas**:
  - las cinco clases nuevas;
  - `IdaYVueltaPruebas`: el aviso de `FREQ` en el respaldo real ya es de esperar.

## Pendiente o para decidir

- Los dos contactos de 70 cm que ya están en la base de Jose: F2 y poner 433,000, o reimportar.
- No hay pantalla para **editar un perfil de estación**; solo se crea en el primer arranque. El completado automático tapa el caso de Jose, pero un editor de perfiles haría falta.
- Enlace roto `MemoriaElegida.Etiqueta` en `VistaModeloEquipo`: es del especialista de Operar.
