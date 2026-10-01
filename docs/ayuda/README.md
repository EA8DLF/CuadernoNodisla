# Ayuda de Cuaderno NODISLA

Esta es la ayuda del programa, escrita para un radioaficionado que se sienta delante de la
pantalla y quiere saber qué hace cada cosa. No es un manual de referencia de cada campo: es lo
que hace falta para operar sin tropezarse.

Cuaderno NODISLA es un cuaderno de guardia en español: opera con el equipo por CAT, hace los
modos digitales con su propio módem, sigue el cluster y los diplomas, diseña y manda las QSL y
los diplomas, y guarda todo en un solo fichero, `%AppData%\CuadernoNodisla\cuaderno.sqlite`,
fácil de copiar y de llevar a otro equipo.

**¿Es la primera vez?** Empiece por [Primer uso, paso a paso](01-primer-uso.md).

## Cómo está ordenada la ventana

Arriba, una sola barra con cuatro entradas agrupadas por uso y, a la derecha del todo, Ayuda y
Configuración. Al elegir una entrada, sus páginas salen en la misma barra, a continuación; al
volver a ella se vuelve a la página que se vio la última vez.

| Entrada | Páginas |
|---|---|
| **Operar** | Cabina (`Ctrl` `1`) · Digital (`Ctrl` `2`) · Satélites (`Ctrl` `7`) · Ronda de control (`Ctrl` `9`) |
| **Libro** | Contactos (`Ctrl` `3`) · Mapa (`Ctrl` `4`) |
| **QSL** | Tarjeta QSL (`Ctrl` `8`) · Etiquetas · Diplomas (`Ctrl` `Mayús` `5`) |
| **Diplomas** | El seguimiento de diplomas (`Ctrl` `5`) |
| **Ayuda** | Esta ayuda (`F1` abre el capítulo de la página en la que esté) |
| **Configuración** | Cuentas, equipo, audio, fonía, cluster, correo, libro y actualizaciones (`Ctrl` `6`) |

## Índice

1. [Primer uso, paso a paso](01-primer-uso.md) — instalar, el perfil de estación, el equipo por
   CAT con el FT-710, el audio y las cuentas.
2. [Operar](02-operar.md) — la cabina: el frontal, el contacto nuevo, el retrato del indicativo,
   el cluster y el bandmap.
3. [Digital](03-digital.md) — el módem propio: FT8, FT4, WSPR, JT65, JT9, Q65, MSK144, FST4 y
   FST4W; el reloj, la cascada y la secuencia automática.
4. [Contactos](04-cuaderno.md) — la rejilla del libro: buscar, editar y fundir duplicados.
5. [Diplomas](05-diplomas.md) — NEW, BAND y MODE, las vías de confirmación y cuándo una cifra
   no es firme.
6. [Mapa](06-mapa.md) — contactos, anuncios, paso gris y trayectos.
7. [Satélites](07-satelites.md) — el próximo paso, azimut y elevación en vivo y el Doppler.
8. [QSL: etiquetas del buró](08-impresion-qsl.md) — filtro, plantillas Avery y PDF.
9. [Configuración](09-ajustes.md) — todos los apartados.
10. [Atajos de teclado](10-atajos-de-teclado.md) — todo lo que se puede hacer sin ratón.
11. [Ronda de control](11-ronda-de-control.md) — dirigir una red y apuntar a los participantes.
12. [Fonía por el PC](12-fonia.md) — oír la radio por el PC y hablar con su micrófono.
13. [Tarjeta QSL propia](13-tarjeta-qsl.md) — diseñarla y mandarla por correo; eQSL.
14. [Diseñador de diplomas](14-disenador-de-diplomas.md) — diplomas propios y certificados,
    con numeración, PDF, correo e historial.
15. [Equipos](15-equipos.md) — el FT-710, los demás Yaesu e ICOM, el frontal, el espectro y
    MULTI.
16. [Ayuda, actualizaciones y fallos](16-ayuda-actualizaciones-y-fallos.md) — la ayuda, el aviso
    de versión nueva, reportar un fallo y «Acerca de».

## Antes de nada

Tres cosas que valen para todo el programa:

- **Nada se conecta solo.** El equipo, el cluster y el módem arrancan desconectados. Conectar es
  siempre un botón que pulsa el operador.
- **Nada transmite solo.** Emitir un mensaje digital, subir el PTT o llamar CQ son siempre
  acciones explícitas, con **SOLTAR PTT** siempre a mano.
- **Las credenciales se cifran.** LoTW, eQSL, Club Log, QRZ… se guardan con la protección de
  datos de la cuenta de Windows, nunca en un fichero legible, y no se vuelven a enseñar una vez
  escritas.
