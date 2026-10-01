# Tablas del protocolo Q65

## Qué hay aquí

Un fichero de datos, `tablas-q65.txt`, con **las constantes del protocolo Q65 que no se pueden
deducir**:

- La **estructura del código QRA(65,15) sobre GF(64)**: en qué orden entran los 15 símbolos de
  información en el acumulador (51 pasos) y con qué peso del cuerpo entra cada uno. Con eso se
  construyen las 51 ecuaciones de paridad y se codifica.
- Las **repeticiones** de cada símbolo: la misma información vista de otro modo, para cruzarla.
- Las **22 posiciones de sincronismo** de la trama de 85 símbolos.
- El **polinomio del CRC de 12 bits**.

Los números que sí se deducen de la descripción publicada del protocolo (duración de símbolo por
periodo, separación de tonos por submodo, arranque nominal de la señal, tono de sincronismo = 0,
símbolo *s* en el tono *s+1*, bit de relleno) están escritos directamente en `Q65/ParametrosDeQ65.cs`
y `Q65/MensajeDeQ65.cs`, con su comentario.

## Procedencia

| | |
|---|---|
| Proyecto | [WSJT-X](https://sourceforge.net/p/wsjt/wsjtx/), de Joe Taylor (K1JT) y colaboradores |
| Autor del código QRA | Nico Palermo (IV3NWV), paquete `qracodes`, © 2020 |
| Licencia del proyecto | GPL v3 |
| Ficheros leídos | `lib/qra/q65/qra15_65_64_irr_e23.c` (tablas del código), `lib/qra/q65/genq65.f90` (sincronismo y disposición de la trama), `lib/qra/q65/q65.c` (polinomio y forma del CRC), `lib/q65_decode.f90` y `lib/q65params.f90` (duraciones y arranque) |
| Versión | rama `master` de SourceForge, leída el 26-09-2026 |
| Traído el | 26-09-2026, con autorización expresa de EA8DLF |

**Aquí sólo hay constantes.** WSJT-X es GPL v3 y de él **no se ha copiado, traducido ni compilado
una sola línea de código**. Se descargaron esos ficheros al borrador de la sesión únicamente para
leer las constantes numéricas (permutación, pesos, posiciones de sincronismo, polinomio) y
comprobar la disposición del protocolo, y se borraron después. El codificador, el decodificador
por propagación de creencias sobre GF(64), el sincronizador, el espectrograma, la transformada de
raíces mixtas, el CRC y el promediado de Cuaderno NODISLA están escritos desde cero en C#, a
partir de la descripción publicada (N. Palermo, «Q-ary Repeat-Accumulate Codes for Weak Signal
Communications», y J. Taylor y N. Palermo, «The Q65 Protocol», QEX 2021).

Las constantes de un protocolo público —como el polinomio de un CRC o los grupos de Costas de
FT8— no son código: o se tienen las mismas que todo el mundo o no se decodifica a nadie. Es el
mismo criterio que se aplicó con la tabla LDPC de FT8 (que además era MIT).

## Por qué hubo que traerlo

Un código de repetición y acumulación queda definido por tres cosas: cuántas veces se repite cada
símbolo, en qué orden se barajan las repeticiones y con qué elemento del cuerpo se pesa cada una.
Las dos últimas las eligió quien diseñó el código (con un programa de búsqueda, `npiwnarsavehc.m`
según la cabecera del fichero original), y no hay fórmula que las regenere.

## Qué se comprobó antes de fiarse

Todo esto está en `Q65Pruebas.cs` y se vuelve a comprobar en cada compilación:

| Comprobación | Resultado |
|---|---|
| Dimensiones | 15 símbolos de información, 65 de palabra, cuerpo de 64, 51 pasos, 22 posiciones de sincronismo |
| Repeticiones contra entradas | cada símbolo entra exactamente las veces que dice la línea REPETICIONES (7×3 + 6×4 + 2×3 = 51) |
| Cierre del acumulador | los pesos de cada símbolo suman cero en GF(64): un paso más devuelve el acumulador a cero, que es la propiedad de diseño del código |
| Una errata de un número se detecta | sí: cambiar un peso rompe el cierre y el fichero se rechaza entero |
| Cuerpo | las potencias de α con x⁶+x+1 coinciden con la tabla de potencias del protocolo |
| Ida y vuelta | codificar y decodificar sin ruido devuelve el mensaje idéntico; la propagación de creencias arregla hasta 14 símbolos mal |
| Cadena entera | generar, meter ruido, sincronizar, demodular y decodificar devuelve el texto emitido en 30A, 60A, 120A y 60C, también a 48 000 Hz remuestreado |

**Lo que no se ha podido comprobar todavía**: la interoperabilidad con una emisión real de
WSJT-X. Todas las constantes se han cruzado entre sí y contra la descripción publicada, pero no
se ha decodificado aún una grabación ajena. Es el siguiente paso, y hasta entonces «interopera»
es una expectativa razonada, no un dato medido.

## Cómo hacer que el fichero viaje junto al programa

`TablasDeQ65.Cargar()` busca `tablas-q65.txt` junto al ejecutable, como `tablas-ft8.txt`. Para
que se copie hay que añadir al `Nodisla.Cuaderno.Modos.csproj`, en el mismo grupo que la tabla
de FT8:

```xml
<None Update="Tablas\tablas-q65.txt">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  <Link>tablas-q65.txt</Link>
</None>
```

Se deja escrito aquí y no hecho porque el `csproj` es común a todos los modos y lo toca quien
engancha los modos en el módem. Si el fichero falta, Q65 no finge: avisa por el registro, trabaja
con un código de pruebas de la misma forma y deja `EsElCodigoReal` en falso.

## Cómo registrarlo en el módem

Una instancia por submodo y periodo; las tres de HF/VHF:

```csharp
var tablasQ65 = TablasDeQ65.Cargar(registro: registro);
registroDeModos
    .Anadir(new ModoQ65(ParametrosDeQ65.De(60, SubmodoDeQ65.A), tablasQ65, registro));
// 30A y 120A son iguales cambiando el periodo. RegistroDeModos sólo admite una entrada por
// ModoDelModem.Q65, así que el submodo activo lo elige quien monta el módem.
```

`ModoQ65.MiIndicativo` e `IndicativoDx` activan la información a priori; `UsarPromediado`
(activo por omisión) suma periodos. La señal de `Generar` hay que emitirla
`ModoQ65.ComienzoNominalSegundos` (0,5 s en 15/30 s; 1,0 s en 60/120/300 s) después del comienzo
de la ventana.
