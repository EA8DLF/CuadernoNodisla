# 10 · Revisión de la pestaña Digital (Ctrl 2), control a control

Fecha: 27-09-2026. Ámbito: `Vistas/PestanaDigital.xaml`, `PanelDelModem.xaml`, `Cascada.xaml`,
`TiraDelReloj.xaml`, `VistaModelos/VistaModeloModemPropio.cs`, `VistaModeloRelojDigital.cs` y
`Digital/SecuenciadorDeQso.cs`. La barra del equipo y el frontal son de otro especialista: aquí
solo se ha tocado cómo se reparte el alto a su alrededor.

## Cómo se ha comprobado

- **Botón a botón en un árbol WPF de verdad**: `tests/.../PanelDelModemEnPantallaPruebas.cs` pinta
  el `PanelDelModem` real en una ventana fuera de la pantalla y sin activar, y pulsa cada control
  por el camino del clic (`OnClick`) o de accesibilidad. Falla si un botón no tiene orden, si su
  `CanExecute` no se alcanza o si no hace lo que dice.
- **Enlaces rotos**: la misma batería abre los tres desplegables y recoge el rastreo de enlaces de
  WPF (con una prueba de control que demuestra que el oyente sí oye un enlace roto). La instancia
  apartada: el rastreo de WPF deja el fichero vacío sin depurador (no vale como prueba); la
  cifra buena es el `.rotos.txt` de `RetratoDeLaVentana`: **0 enlaces rotos de la pestaña Digital**
  a 1920×1000 y 1366×768 (el único que sale, `SateliteSeleccionado.Nombre`, es de Satélites). No
  hay cifra «antes» con ese fichero: la herramienta llegó después; los controles muertos de la
  lista de abajo no eran caminos rotos sino clics que no llegaban (fila y caja se comían el clic).
- **Órdenes y secuencia**: `PestanaDigitalOrdenesPruebas.cs`, con módem, salida y reloj de mentira
  (el reloj lo pone la prueba, sin reloj de pared). Incluye un QSO completo por paridad de ventanas.
- **Disposición**: capturas de la instancia apartada (`CUADERNO_APARTADA`, `CUADERNO_SIMULADO`,
  `CUADERNO_PESTANA=1`, `CUADERNO_TAMANO`) a 1920×1000 y 1366×768, frontal desplegado y plegado.
- **Aire real, solo recepción**: `CUADERNO_AUDIO_REAL=1` (nuevo, `Desarrollo/RecepcionReal.cs`)
  monta el módem de verdad sobre el codec del FT-710 **sin salida de audio ni vigilante de PTT**:
  no puede transmitir por construcción. Equipo simulado: COM15 no se abre.

## Lo que estaba roto (resumen)

De **58 controles** interactivos, **13 estaban rotos o mentían**:

1. **Doble clic en la lista** (el gesto principal): `MouseBinding` en el `ListBox`; la fila se come
   el clic y la orden no llegaba nunca.
2. **Doble clic en Tx1–Tx6**: igual, el `TextBox` se come el clic.
3. **Llamar CQ, doble clic en Tx y contestar** emitían **en el acto**, a media ventana. Contestar
   pulsando durante la ventana del corresponsal salía **encima de él**.
4. **Emitir (mensaje libre)** decía «en la siguiente ventana» y salía en el acto.
5. **Tx habilitado** escribía la propiedad a pelo: se saltaba la pregunta de antes de transmitir, y
   encendida sin contacto no hacía nada.
6. **Secuencia automática** apagada: no salía **nada** (tenía que repetir el mensaje marcado).
7. **Radio «siguiente»**: a media secuencia no cambiaba lo que salía (mandaba la secuencia), y por
   accesibilidad/teclado no llegaba al modelo.
8. **Detener** no cortaba la emisión en curso (WSJT-X «Halt Tx» sí).
9. **«Emisión cortada y PTT soltado»** se quedaba puesto para siempre, y salía aunque no hubiera
   emisión.
10. **Escuchar** dos veces seguidas (botón + visor de VFO + arranque) abría la tarjeta dos veces.
11. **Reloj**: las órdenes de w32tm se quedaban (5 líneas) con el reloj a 3 ms.
12. **Disposición**: frontal a 300 puntos fijos; cascada en 20 px y lista en una línea a 1920×1000;
    a 1366×768 desaparecían la cascada, la lista, el pestillo y «Cortar y soltar PTT».
13. **Pie** en `WrapPanel`: a 1366 mandaba Emitir y Cortar a una línea fuera de la pantalla.

## Tabla por control

| Control | Antes | Qué se hizo | Después (prueba) |
|---|---|---|---|
| Modo (9 modos) | OK | — | `LaCascadaElModoYLosTonos` |
| Escuchar | Doble apertura posible | Guardia `_arrancando` | `TodosLosBotones…` |
| Parar | OK | — | `TodosLosBotones…` |
| Reloj · Medir | OK | — | `LosBotonesDelReloj`, `TodosLosBotones…` |
| Reloj · Poner en hora | Dejaba 5 líneas de w32tm aunque saliera bien | Instrucciones solo si falla; se borran al volver a verde | `LosBotonesDelReloj` |
| Reloj · Configurar el servicio | OK | — | `LosBotonesDelReloj` |
| Tira del reloj | 5 líneas en verde | Una línea en verde; consejo/parte/órdenes solo si hay algo que hacer | capturas |
| Cascada · clic / Mayús+clic | OK | — | `LaCascadaElModoYLosTonos` (Mantener Tx) |
| Rx / Tx (Hz) | OK | — | enlaces 0 errores |
| Mantener Tx | OK | — | `LaCascadaElModoYLosTonos` |
| Sólo CQ / Sólo a mí / Sólo nuevos | OK | — | `LosFiltrosVaciarYElWav` |
| Vaciar | OK | — | `LosFiltrosVaciarYElWav` |
| WAV… | OK | — | `LosFiltrosVaciarYElWav` |
| Lista · doble clic | **Muerto** | Evento `MouseDoubleClick` de cada fila | `ElDobleClicEnUnaDecodificacion…` |
| DX Call / DX Grid / Enviado / Recibido | OK | — | `ElContactoSeOlvida…` |
| Guardar en el cuaderno | OK (avisa si no hay dial) | — | `UnContactoEntero…` |
| Olvidar | OK | — | `ElContactoSeOlvida…`, `TodosLosBotones…` |
| Generar | OK | — | `ElContactoSeOlvida…` |
| Tx1–Tx6 · radio «siguiente» | No mandaba a media secuencia; accesibilidad no llegaba | Evento `Checked` + `SecuenciadorDeQso.ElegirSiguiente` | `ElegirElSiguiente…`, `TodosLosBotones…` |
| Tx1–Tx6 · doble clic | **Muerto** | Evento `MouseDoubleClick` | código detrás |
| Tx1–Tx6 · botón «Enviar» | No existía | Nuevo (el «Now» de WSJT-X) | `TodosLosBotones…`, `EnviarUnMensaje…` |
| Llamar CQ | Emitía en el acto | Sale al empezar la ventana propia | `LlamarCqEspera…` |
| Detener | No cortaba lo que sonaba | Corta, anula el libre pendiente | `DetenerCorta…` |
| Tx habilitado | Saltaba la pregunta; sin contacto no hacía nada | Orden: pestillo + pregunta; arranca con el marcado | `TxHabilitadoPasaPor…`, `TodosLosBotones…` |
| Secuencia automática | Apagada = nada | Apagada = repite el marcado | `SinSecuenciaAutomatica…` |
| Saltar Tx1 / Llamar al primero / Tx4 con RRR / Parar tras N | OK | — | `SecuenciadorDeQsoPruebas` |
| CQ dirigido / Operación / Intercambio | OK | — | enlaces 0 errores |
| Frecuencias · Ir a la del modo / Ir a la elegida | OK (avisa sin equipo) | — | `LasFrecuenciasDeTrabajo` |
| Frecuencias · Añadir / Quitar / Las de WSJT-X / tabla | OK | — | `LasFrecuenciasDeTrabajo`, `TodosLosBotones…` |
| Cascada · Ganancia / Cero / Promedio / Paleta / Ancho | OK | — | `LaCascadaElModoYLosTonos` |
| PSK Reporter / WAV por ventana / decodificaciones.txt | OK | — | enlaces 0 errores |
| Permitir transmitir (pestillo) | OK | — | `TodosLosBotones…` |
| Mensaje libre + Emitir | Salía en el acto | Espera al principio de ventana; «Detener» lo anula | `ElMensajeLibreSale…`, `DetenerCorta…` |
| Cortar y soltar PTT | Aviso eterno y falso | Dice si había emisión; el aviso caduca a los 8 s | `CortarYSoltarDiceLaVerdad…` |
| Aviso · ✕ | No existía | Nuevo | `TodosLosBotones…` |

## La regla de la ventana (lo más importante)

Nada sale ya «en el acto». Se emite solo si **ahora** es el principio de una ventana de la paridad
propia y se llega a tiempo: como mucho `periodo / 7,5` tarde (2 s en FT8, 1 s en FT4). Si no, se
espera a la siguiente ventana decodificada. Si el decodificador tarda más de la cuenta se pierde
ese turno en vez de salir tarde encima de otro. `UnContactoEntero…` recorre un QSO completo
(CQ oído → Tx1 → R-12 → Tx3 → RR73 → Tx5 → guardado) y comprueba que las tres emisiones caen
en la ventana impar y en sus dos primeros segundos.

## Disposición

- El frontal cede el alto: mide lo que sobra tras dar al módem lo mínimo para operar (430 puntos),
  hasta 300. Por debajo de 150 no se lee y se pliega solo (a 1366×768; sigue entero en Operar).
- Cascada (90) y lista (100) con alto mínimo garantizado. Si aun así no cabe, el panel se desplaza
  en vez de recortar el pie.
- Cabecera en una línea, reloj en una línea cuando está en verde, pie en una línea con «Cortar y
  soltar PTT» anclado a la derecha.

Capturas en `docs/capturas/revision-digital/`: `antes-1920-desplegado.png`,
`antes-1366-desplegado.png`, `despues-{1920,1366}-{desplegado,plegado}.png`, `aire-real-1920.png`
(codec en vivo, reloj en verde en una línea), `grabacion-real-en-la-lista.png` (la grabación real de
20 m por «WAV…» con el módem de verdad: 7 estaciones, colores de novedad, Suiza como entidad
nueva), `wav-12k-subido-a-96k.png` y `ventana-en-vivo-0-decodificaciones.png`.

## Pendiente (fuera de esta pestaña)

- **Con el aire real la lista sale vacía** («0 decodificaciones en 3,7 s») aunque la cascada enseña
  señales FT8. La grabación de prueba (12 kHz, 16 bits) sí decodifica. Diferencias del camino en
  vivo: el codec va a **44 100 Hz** y el módem trabaja a **96 000** (los ajustes del operador), con
  conversión por interpolación, y la entrada está **saturando (0,92)**. Es de `Audio`/`Modos`, que
  no se tocan en esta revisión; ver la entrega para lo que se averiguó.
- La barra del equipo y el frontal son del especialista de Operar.

### Lo averiguado sobre el aire en vivo (para quien lleve `Audio`/`Modos`)

| Entrada al decodificador del módem de verdad | Decodificaciones |
|---|---|
| Grabación de prueba, 12 kHz 16 bits | 7 |
| La misma, subida a 96 kHz por interpolación lineal | 7 |
| Ventana grabada en vivo del codec (44,1 kHz → 96 kHz de la app, `260927_170945.wav`), solo 54 muestras recortadas de 1,44 M | 0 |
| La misma leída como 88 200 y 104 490 Hz (por si la conversión escalaba mal) | 0 y 0 |

El decodificador a 96 kHz funciona; lo que llega del codec en vivo no decodifica, y el recorte no
lo explica. Sospechosos: la conversión 44 100 → 96 000 de la entrada, o el troceado de la
ventana en vivo. Las ventanas grabadas están en el directorio temporal de esta revisión
(`260927_170930.wav`, `260927_170945.wav`); no quedan en la carpeta de datos del operador.

### Otros

- Los WAV decodificados no traen hora (llegaban como 00:00:00); ahora la fila dice «fichero».
- `Nodisla.Cuaderno.Modos.Pruebas.Banco.BancoMsk144Pruebas` falló tras 8 min 46 s en la pasada
  completa; es de `Modos`, no se ha tocado.
- Para desbloquear la compilación se corrigió un salto de línea literal dentro de una cadena en
  `VistaModelos/FilaDeQso.cs` (línea 193, de otro especialista): `string.Join("\n", partes)`.

### Variables de verificación nuevas

`CUADERNO_TAMANO=1366x768` y `CUADERNO_RASTREO_ENLACES` (con la apartada), `CUADERNO_AUDIO_REAL`
(módem y codec reales, sin salida ni PTT), `CUADERNO_ESCUCHAR`, `CUADERNO_GUARDAR_WAV` y
`CUADERNO_DECODIFICAR_WAV=fichero`. La instancia apartada ya no guarda el estado de los paneles
ni los ajustes al cerrarse.
