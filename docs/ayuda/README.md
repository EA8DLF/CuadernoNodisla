# Ayuda de Cuaderno NODISLA

Esta es la ayuda del programa, escrita para un radioaficionado que se sienta delante de la
pantalla y quiera saber qué hace cada cosa. No es un manual de referencia de cada campo: es lo
que hace falta para operar sin tropezarse.

Cuaderno NODISLA es el cuaderno de guardia propio de EA8DLF, hecho a partir de lo que hace
Log4OM pero en español y a medida. Vive en `%AppData%\CuadernoNodisla\cuaderno.sqlite`: un solo
fichero, fácil de copiar y de llevar a otro equipo.

## Índice

1. [Primer arranque](01-primer-arranque.md) — el perfil de estación y traerse el cuaderno de Log4OM.
2. [Operar](02-operar.md) — la cabina: entrada de contacto, el frontal del FT-710, el cluster, la
   matriz de novedad y la de banda por modo.
3. [Digital](03-digital.md) — el módem propio de FT8/FT4, la cascada, el reloj y las
   decodificaciones rescatadas.
4. [Cuaderno](04-cuaderno.md) — la rejilla de contactos, buscar, editar, fusión de duplicados.
5. [Diplomas](05-diplomas.md) — NEW/BAND/MODE, las vías de confirmación y qué significa que una
   cifra no sea firme.
6. [Mapa](06-mapa.md) — contactos, spots, paso gris y trayectos.
7. [Satélites](07-satelites.md) — catálogo con el próximo paso, az/el en vivo, seguimiento
   Doppler y el subpunto en el mapa.
8. [Impresión de QSL](08-impresion-qsl.md) — filtro de contactos, plantillas Avery y vista
   previa en PDF real.
9. [Ajustes](09-ajustes.md) — CAT, cluster, audio, servicios y credenciales, importar/exportar
   ADIF.
10. [Atajos de teclado](10-atajos-de-teclado.md) — todo lo que se puede hacer sin soltar el
    teclado.
11. [Ronda de control](11-ronda-de-control.md) — abrir y cerrar una ronda, participantes y
    reapertura automática.
12. [Fonía por el PC](12-fonia.md) — escuchar la radio por los altavoces del PC y hablar con el
    micrófono del PC con el PTT de fonía.
13. [Tarjeta QSL propia](13-tarjeta-qsl.md) — diseñar la tarjeta, generarla en imagen o PDF y
    mandarla por correo al corresponsal; eQSL.

## Antes de nada

Tres cosas que valen para todo el programa:

- **Nada se conecta solo.** El equipo, el cluster y el módem arrancan desconectados. Conectar es
  siempre un botón que pulsa el operador.
- **Nada transmite solo.** Emitir un mensaje de FT8, subir el PTT o llamar CQ son siempre
  acciones explícitas, con su propio botón de pánico al lado.
- **Las credenciales se cifran.** LoTW, eQSL, ClubLog, QRZ… se guardan con la protección de
  datos de la cuenta de Windows, nunca en un fichero legible, y no se vuelven a enseñar una vez
  escritas.
