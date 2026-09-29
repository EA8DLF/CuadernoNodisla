# Constantes de los protocolos JT65 y JT9

## Qué hay aquí

JT65 y JT9 no necesitan ningún fichero de datos: todas sus constantes caben en unas pocas
líneas y van en código, con su procedencia escrita, en `Jt65/TablasJt65.cs` y
`Jt9/TablasJt9.cs`. Esta página dice de dónde sale cada una y qué se comprobó.

**Aquí sólo hay datos del protocolo.** No se ha copiado, traducido ni consultado código de
WSJT-X (GPLv3) ni de KVASD. El codificador Reed-Solomon, el decodificador de Berlekamp-Massey
con borrados, el decodificador blando, el sincronizador, el demodulador y el empaquetado de los
mensajes están escritos desde cero en C# a partir de los libros de códigos y de los artículos
publicados que se citan abajo. El código convolucional K=32 y el decodificador de Fano que usa
JT9 son los de WSPR (`Convolucional/`), escritos por el especialista de WSPR con el mismo
criterio.

## Procedencia

| Constante | Valor | De dónde |
|---|---|---|
| Vector de sincronismo de JT65 | 126 bits, 63 unos | J. Taylor, K1JT, «The JT65 Communications Protocol», QEX, sep-oct 2005; guía de usuario de WSJT-X, apéndice «Protocol Specifications» |
| Reed-Solomon de JT65 | RS(63,12) sobre GF(64), polinomio x⁶+x+1 (0x43), raíces del generador α³..α⁵³ | Ídem, y S. Franke y J. Taylor, «Open Source Soft-Decision Decoder for the JT65 (63,12) Reed-Solomon Code», QEX, may-jun 2016 |
| Orden de los símbolos en el aire (JT65) | los 51 de paridad primero, los 12 del mensaje al final; entrelazado 7×9 (escribir por columnas, leer por filas) | Descripción del protocolo |
| Código de Gray (JT65 y JT9) | binario reflejado, `g = s XOR (s >> 1)` | Descripción del protocolo |
| Tono de datos de JT65 | `2 + Gray[s]` espaciados por encima del de sincronismo (el tono 1 no se usa) | Descripción del protocolo |
| Empaquetado de 72 bits | 28 + 28 + 16 bits; CQ = base+1, QRZ = base+2, «CQ nnn» = base+3+nnn, DE = 267 796 945; informes desde 32 400; texto libre de 13 caracteres sobre un alfabeto de 42 con el bit alto del tercer campo | Taylor, QEX 2005; guía de WSJT-X |
| Sincronismo de JT9 | 16 símbolos en el tono 0, posiciones 1, 2, 6, 13, 22, 25, 33, 35, 50, 55, 66, 72, 79, 81, 84, 85 (contando desde 1) | Guía de usuario de WSJT-X, «Protocol Specifications» |
| Código convolucional de JT9 | K=32, r=1/2, polinomios 0xF2D05351 y 0xE4613C47, 31 ceros de cola | Ídem (el mismo de WSPR) |
| Entrelazado de JT9 | inversión de los 8 bits del índice, quedándose con los menores de 206 | Ídem (el mismo esquema de WSPR) |
| Reparto en símbolos de JT9 | 206 bits + 1 de relleno = 69 símbolos de 3 bits, el más significativo primero; tono `1 + Gray[s]` | Ídem |

## Qué se comprobó

Todo esto está en `ReedSolomonPruebas`, `ProtocoloJt65Pruebas` y `Jt9Pruebas` y se vuelve a
comprobar en cada compilación:

| Comprobación | Resultado |
|---|---|
| GF(64) con 0x43 es un cuerpo y el polinomio es primitivo | sí; 0x49 (no primitivo) se rechaza |
| El código es sistemático, lineal y se anula en sus 51 raíces | sí |
| Corrige 25 errores; corrige e borrados y t errores mientras e + 2t ≤ 51 | sí, en 200 palabras al azar |
| El vector de sincronismo tiene 126 intervalos y exactamente 63 unos | sí |
| El entrelazado 7×9 es una permutación y se deshace | sí |
| Los mensajes de las gramáticas (CQ, CQ DX, CQ nnn, QRZ, informes, RRR/RO/73, localizador, texto libre) van y vuelven iguales | sí |
| El sincronismo de JT9 son 16 posiciones distintas; el entrelazado es una permutación de 206 | sí |
| Codificar, modular y decodificar sin ruido devuelve el mensaje, en los dos modos | sí |

## Lo que queda por validar contra el aire

Sin una grabación real de WSJT-X en la máquina (no había ninguna en `%LocalAppData%\WSJT-X\save`)
hay tres detalles que se han implementado según la descripción publicada pero que sólo una
señal ajena puede confirmar del todo:

1. **La primera raíz del generador Reed-Solomon** (α³). Si fuera otra, el módem seguiría
   decodificándose a sí mismo y no decodificaría a nadie más. Es un solo número en
   `CodigoReedSolomon.Jt65`.
2. **El sentido del entrelazado 7×9 de JT65** y el orden paridad-mensaje en el aire. Son dos
   tablas en `TablasJt65`.
3. **La cuenta del localizador en el tercer campo** (`ng = ((long_oeste + 180) / 2) * 180 +
   lat + 90` con truncamiento hacia cero de la longitud y hacia abajo de la latitud). Va y
   vuelve igual aquí; el hemisferio sur es donde una diferencia de convenio se notaría.

Cuando haya una grabación, la comprobación es: decodificarla con el módem y comparar con el
`ALL.TXT` de WSJT-X. Si alguna de esas tres cosas está al revés, se cambia la constante y se
vuelve a pasar el banco.
