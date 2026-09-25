# Tablas del protocolo FT8 y FT4

## Qué hay aquí

Un solo fichero de datos, `tablas-ft8.txt`, con **las constantes del protocolo que no se pueden
deducir**:

- La **matriz de paridad del código corrector LDPC(174,91)**: 83 ecuaciones que dicen qué bits de
  la palabra emitida tienen que sumar cero.
- La **secuencia de mezcla de FT4**: diez bytes con los que FT4 revuelve el mensaje antes de
  codificarlo, para que una señal de FT4 no pueda decodificarse como si fuera de FT8.

## Procedencia

| | |
|---|---|
| Proyecto | [`ft8_lib`](https://github.com/kgoba/ft8_lib), de Karlis Goba (YL3JG) |
| Licencia | MIT |
| Fichero | `ft8/constants.c` |
| Versión | `cbc656c3757f1c19b5887e6d6008494b53654117`, del 14-12-2021 |
| Traído el | 25-09-2026, con autorización expresa de EA8DLF |

**Aquí sólo hay datos.** No se ha incorporado ni compilado una sola línea de código de ese
proyecto. El codificador, el decodificador, el sincronizador, el demodulador, la propagación de
creencias y el CRC de Cuaderno NODISLA están escritos desde cero en C#.

La licencia MIT permite el uso con la única condición de conservar el aviso de copyright, y eso
es lo que hace esta página. El aviso va también dentro del propio `tablas-ft8.txt`, para que
viaje con el fichero aunque alguien lo copie suelto.

## Por qué hubo que traerlo

Casi todo FT8 se puede escribir a partir de la descripción del protocolo: los grupos de Costas,
el código de Gray, la modulación suavizada, el sincronismo, el CRC de 14 bits y el empaquetado
de los mensajes en 77 bits. De hecho, los grupos de Costas y el código de Gray de este módem se
escribieron por separado y **coincidieron exactamente** con los de esta tabla, lo que sirvió de
comprobación cruzada.

El LDPC no. Sus 83 ecuaciones las eligió a mano quien diseñó el protocolo; no hay fórmula que
las genere. O se tienen las mismas que todo el mundo o no se decodifica a nadie. Son constantes
del estándar de facto, del mismo tipo que el polinomio de un CRC o la tabla de sustitución de un
cifrado.

## Lo que **no** hace falta que esté

**La matriz generadora**, que es la otra tabla que suelen traer las implementaciones. Se despeja
de estas mismas ecuaciones por eliminación gaussiana en módulo dos al cargar el código, de una
vez y para siempre. La ventaja no es ahorrar espacio: es que **sólo hay una tabla que verificar**,
y por tanto no puede haber dos que discrepen entre sí sin que nadie se entere.

## Qué se comprobó antes de fiarse

Todo esto está en `TablasDelProtocoloPruebas` y se vuelve a comprobar en cada compilación:

| Comprobación | Resultado |
|---|---|
| Dimensiones | 174 bits por palabra, 91 de mensaje, 83 ecuaciones |
| Peso de las filas | 24 ecuaciones de 7 bits y 59 de 6 |
| Peso de las columnas | los 174 bits aparecen en exactamente 3 ecuaciones |
| Unos en total | 522, por los dos caminos (24·7 + 59·6 = 174·3) |
| Ciclos de longitud cuatro | ninguno: no hay dos ecuaciones que compartan más de un bit |
| Ida y vuelta | codificar y decodificar sin ruido devuelve el mensaje idéntico |

Además, el fichero de origen trae la misma matriz **escrita dos veces** —por filas y por
columnas— y las dos representaciones se cruzaron entre sí antes de convertir nada: coincidieron
en los 174 bits sin una sola discrepancia.

## Si algún día hay que sustituirla

El formato es texto a propósito, para que se pueda mirar, comparar y corregir a mano, y para que
un error de transcripción se vea. Lo carga `TablasDelProtocolo.Cargar`. Si el fichero falta, el
módem **no finge**: avisa por el registro, trabaja con un código de pruebas de las mismas
dimensiones y deja `EsElCodigoReal` en falso para que la pantalla pueda advertir de que no se
está decodificando a nadie más. Si el fichero está a medias, se descarta entero: una tabla
incompleta decodificaría basura con el CRC cuadrando de vez en cuando, que es peor que no tener
tabla.
