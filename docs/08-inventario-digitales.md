# Inventario de opciones de operación: WSJT-X 2.7 y JTDX frente a la pestaña Digital

Fecha: 2026-09-26. Regla de la casa: **todo propio, sin puentes**. El puente UDP con WSJT-X/JTDX
se ha quitado de la interfaz (el código queda en `Integraciones/Digital` sin registrar ni ver).

Columnas: **Tenía** (antes de hoy), **Hoy** (hecho en esta tanda), **Queda** (pendiente; se dice
cómo).

## 1. Modos

| Opción | WSJT-X | JTDX | Tenía | Hoy | Queda |
|---|---|---|---|---|---|
| FT8 | sí | sí | sí | — | — |
| FT4 | sí | sí | sí | — | — |
| WSPR | sí | no | no | selector dinámico (`ModosDisponibles`) | decodificador, en `Modos` (otro especialista) |
| JT65 / JT9 / JT4 | sí (JT4 sólo WSJT-X) | JT65, JT9 | no | selector dinámico | decodificadores; JT4 no está en el enum |
| Q65 (A–E) | sí | no | no | selector dinámico | decodificador y submodos |
| MSK144 | sí | no | no | selector dinámico | decodificador |
| FST4 / FST4W | sí | no | no | selector dinámico | decodificador y periodos |
| Echo, FreqCal | sí | no | no | — | no previsto |
| Submodos por periodo (FST4 15/30/60/120/300/900/1800, Q65 15–300) | sí | — | no | — | con el decodificador |
| Modo lento "JT9+JT65" a la vez | sí | sí | no | — | no previsto |

## 2. Secuencia del QSO y mensajes

| Opción | WSJT-X | JTDX | Tenía | Hoy | Queda |
|---|---|---|---|---|---|
| Tx1–Tx6 generados de DX Call / DX Grid / mi indicativo / mi localizador / informe | sí | sí | no (un solo mensaje libre) | sí, editables | — |
| Generar mensajes estándar (botón) | sí | sí | no | sí («Generar») | — |
| Tx siguiente (radio por fila) y doble clic para enviar ese | sí | sí | no | sí | — |
| Auto Seq (secuencia automática) | sí | sí | no | sí | — |
| Doble clic en una decodificación inicia el QSO | sí | sí | preparaba sin secuencia | sí | — |
| Llamar al primero (Call 1st) | sí | sí | no | sí | — |
| Saltar Tx1 (Skip Tx1) | no | sí | no | sí | — |
| Tx4 con RRR en vez de RR73 (doble clic en Tx4) | sí | sí | no | sí (casilla) | — |
| Parar tras N ciclos sin respuesta del corresponsal | vigilante de 6 min | sí (AutoSeq stop) | no | sí, N configurable | — |
| Vigilante de Tx (Tx watchdog) | sí | sí | no | sí (tope de ciclos llamando CQ) | — |
| Halt Tx / Enable Tx | sí | sí | Emitir/Cortar | «Tx habilitado» y «Detener» | — |
| CQ dirigido (CQ DX, CQ EU, CQ POTA…) | sí | sí | no | sí (campo en Tx6) | — |
| Mensaje libre de 13 caracteres (Tx5 libre) | sí | sí | mensaje libre | sí (Tx5 editable) | — |
| Mensajes con indicativo compuesto <…> | sí | sí | analizar sí | analizar sí; generar no | generar con angulos (77 bits tipo 4) |
| Paridad par/impar (Tx even/1st) | sí | sí | no | sí (la fija el doble clic; casilla) | — |
| Registrar el QSO al enviar RR73 / recibir 73 («Prompt me to log») | sí | sí | manual | aviso de «contacto completo» y guardado con un clic | registro sin preguntar (casilla) |
| Fila «Tx» de lo emitido en la lista | sí | sí | no | sí | — |
| Respuesta automática a quien me llama (autoresponder JTDX) | no | sí | no | sí (con secuencia automática) | — |
| Filtros JTDX de indicativos (lista negra/blanca) | no | sí | no | no | no previsto |

## 3. Frecuencias y cascada

| Opción | WSJT-X | JTDX | Tenía | Hoy | Queda |
|---|---|---|---|---|---|
| Frecuencia Rx y Tx separadas (Hz) | sí | sí | sólo Tx | sí | — |
| Mantener Tx (Hold Tx Freq) | sí | sí | no | sí | — |
| Clic en cascada pone Rx; Mayús+clic pone Tx; ambas si no está fijado | sí | sí | clic ponía Tx | sí | Ctrl+clic |
| Tabla de frecuencias de trabajo por banda y modo | sí | sí | no | sí, editable, con las de WSJT-X | — |
| Ir a la frecuencia del modo por CAT | sí | sí | no | sí | — |
| Split (Fake It / Rig) para mover el dial y dejar el tono en 1500–2000 | sí | sí | no | no | requiere CAT en cada emisión; pendiente |
| Ganancia y cero de la cascada (Start, Palette Gain/Zero) | sí | sí | no | sí | — |
| Promedio de columnas (N Avg) | sí | sí | no | sí | — |
| Paleta | sí | sí | no | sí (4 paletas) | paleta de usuario |
| Ancho visible (Bins/Pixel + Start) | sí | sí | 0–3000 fijo | sí (1000–5000 Hz) | inicio distinto de 0 |
| Espectro lineal / acumulado / gráfica de nivel | sí | sí | no | no | pendiente |
| Marcas de ancho de Tx en la cascada | sí | sí | cursor Tx | cursores Rx (verde) y Tx (rojo) | ancho de la señal |
| Nivel de entrada con aviso de saturación | sí | sí | sí | — | — |

## 4. Lista de decodificaciones

| Opción | WSJT-X | JTDX | Tenía | Hoy | Queda |
|---|---|---|---|---|---|
| Banda entera / frecuencia Rx (dos listas) | sí | sí | una lista | una lista + resalte de lo cercano a Rx | segunda lista |
| Sólo CQ | (filtro JTDX) | sí | «sólo llamadas» | sí | — |
| Sólo los que me llaman | sí (resalte) | sí | resalte | filtro y resalte | — |
| Sólo nuevos | sí (resalte) | sí | sí | unificado con los colores | — |
| Colores por novedad: entidad nueva, nueva en banda, cuadrícula nueva, trabajado antes | sí (Colors) | sí | «nuevo» a secas | sí, con el retrato del indicativo | por modo; por continente |
| Mostrar distancia/acimut en cada fila | sí (opcional) | sí | no | sí en DX Grid; en fila no | en fila |
| Decodificaciones de la corrección profunda marcadas | (a priori) | sí | sí | — | — |
| Marcar «Tarde» cuando el ordenador no da abasto | no | sí | sí | — | — |
| Decodificar de nuevo la ventana (Decode) | sí | sí | no | no | pendiente en el puerto |
| Erase / Clear Avg | sí | sí | Vaciar | Vaciar | — |
| Búsqueda de indicativo en la lista | no | sí | no | no | pendiente |

## 5. Ficheros y red

| Opción | WSJT-X | JTDX | Tenía | Hoy | Queda |
|---|---|---|---|---|---|
| Guardar WAV de cada ventana (Save all / Save decoded) | sí | sí | no | sí (casilla; carpeta wav/) | «sólo las decodificadas» |
| Abrir WAV y decodificar | sí | sí | puerto sí | sí (botón) | — |
| PSK Reporter por UDP (puerto 4739, IPFIX) | sí | sí | no | sí, desactivado por omisión | antena en el mensaje |
| ALL.TXT | sí | sí | no | sí (`decodificaciones.txt`) | — |
| Servidor UDP para otros programas | sí | sí | no (era lo del puente) | no | no previsto (filosofía NODISLA) |
| N1MM / DXLab | sí | sí | no | no | no previsto |

## 6. Fox/Hound y concursos

| Opción | WSJT-X | JTDX | Tenía | Hoy | Queda |
|---|---|---|---|---|---|
| Hound (llamar a un DX en modo F/H, Tx > 1000 Hz, seguir al fox al responder) | sí | no | no | sí (gramática y secuenciador) | comprobar con tráfico real |
| Fox (ser el DX, varias señales por ventana) | sí | no | no | marco: gramática de «A RR73; B <FOX> -08» analizada | secuenciador de fox |
| Super Fox (2.7) | sí | no | no | no | no previsto (clave privada de ARRL) |
| NA VHF Contest | sí | no | no | sí (gramática + secuencia) | — |
| EU VHF Contest | sí | no | no | sí (gramática + secuencia) | número de serie automático |
| ARRL Field Day | sí | no | no | sí | — |
| ARRL RTTY Roundup | sí | no | no | sí | — |
| WW Digi | sí | no | no | sí | — |
| ARRL Digi (2.6+) | sí | no | no | no | igual que WW Digi con CQ TEST; falta confirmar |
| Registro de concurso (Cabrillo) | sí | no | no | no | va en el módulo Concursos |

## 7. Reloj y estación

| Opción | WSJT-X | JTDX | Tenía | Hoy | Queda |
|---|---|---|---|---|---|
| Desvío del reloj y semáforo | no (externo) | sí (DT) | sí | — | — |
| Poner en hora | no | no | sí | — | — |
| Mi indicativo y localizador desde el perfil | sí | sí | indicativo | indicativo y localizador | — |
| Pestillo de transmisión por sesión | no | no | sí | — | — |
| Vigilante de PTT que suelta pase lo que pase | (watchdog) | (watchdog) | sí | — | — |
