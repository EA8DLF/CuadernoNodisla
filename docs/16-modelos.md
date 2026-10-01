# 16 · Varios modelos de equipo (Yaesu e ICOM)

Arquitectura común para manejar por CAT nativo más equipos que el FT-710. **Todo lo que no es
el FT-710 está programado según el manual de cada modelo y sin probar con la radio real**; la
interfaz lo dice («según manual, sin probar con radio») mientras `ModeloDeEquipo.ProbadoConRadio`
sea falso. Con el FT-710 el manual no bastó (VFO activo en `MD0`, medidores ×1000, CLAR con
otra orden, espectro...): la lista de lo que hay que confirmar en cada modelo está al final.

## 1. Catálogo (`src/Nodisla.Cuaderno.Radio/Modelos/`)

| Tipo | Qué es |
|---|---|
| `ModeloDeEquipo` | Clave estable (`yaesu-ft710`, `icom-ic7300`), fabricante, nombre, `ProtocoloCat`, `IdentificadorYaesu` (`ID;`), `DireccionCiv` de fábrica, velocidades, `CapacidadesDelModelo`, **`Frontal`** (nombre de la clase de la vista, o nulo = genérico), `ProbadoConRadio`, `Notas`. |
| `ProtocoloCat` | `YaesuAscii` (CAT nuevo, `;`), `YaesuBinario` (CAT antiguo de 5 bytes), `IcomCiv`. |
| `IProveedorDeModelos` | Un protocolo con sus modelos: `Modelos`, `Crear(...)`, `IdentificarAsync(...)`, `OrdenDeDeteccion`. |
| `CatalogoDeModelos` | Junta los proveedores **por reflexión** (clases públicas del ensamblado Radio que implementan `IProveedorDeModelos` con constructor sin parámetros). `Todos`, `De(fabricante)`, `Buscar(clave)`, `PorIdentificadorYaesu`, `PorDireccionCiv`, `ProveedorDe`. |
| `IEquipoDeModelo` | Lo implementa todo control del catálogo: `ModeloDeEquipo Modelo { get; }`. |

Ajustes: `OpcionesDeRadio.Modelo` = clave del catálogo, o `auto`/nulo (detección automática;
los ajustes viejos traen nulo y siguen encontrando el FT-710 igual que antes).
`OpcionesDeRadio.DireccionCiv` = dirección CI-V si el operador la cambió (nula = la de fábrica).
Puerto, velocidad, espera de orden, vía del PTT e intervalo de sondeo siguen en
`OpcionesDeRadio.Ft710` (alias `OpcionesDeRadio.Cat`), válidos para cualquier modelo.

`FabricaDeControlEquipo.CrearAsync`: con modelo elegido, crea el control de su proveedor (y si
no hay puerto, lo busca con `IdentificarAsync` por todos los puertos). Con `auto`, prueba el
puerto guardado y luego todos, proveedor por proveedor en `OrdenDeDeteccion` (Yaesu ASCII = 0,
ICOM = 10, Yaesu binario no se autodetecta: no tiene orden de identificación). `ID0800` sigue
dando exactamente el `ControlFt710` de siempre.

## 2. Contrato para el especialista de ICOM (`Control/Icom/`)

1. Una clase pública `ProveedorIcom : IProveedorDeModelos` con constructor sin parámetros:
   - `Protocolo => ProtocoloCat.IcomCiv`, `OrdenDeDeteccion => 10`.
   - `Modelos`: un `ModeloDeEquipo` por equipo, con `Clave = "icom-ic7300"` (minúsculas, sin
     guion interno), `Fabricante.Icom`, `IdentificadorYaesu = null`, `DireccionCiv` de fábrica,
     velocidades (la de fábrica primero), `Frontal` = `"FrontalIc7300"` si el especialista de
     frontales lo dibuja (si la clase no existe se pone el genérico solo, no rompe), y
     `ProbadoConRadio = false`.
   - `Crear(modelo, puerto, baudios, opciones, registro)`: devuelve el control **sin conectar**.
     Dirección: `opciones.DireccionCiv ?? modelo.DireccionCiv`. Espera, sondeo y vía del PTT:
     `opciones.Cat.EsperaDeOrden`, `.IntervaloDeSondeo`, `.ViaDePtt`.
   - `IdentificarAsync(puerto, velocidades, opciones, registro, ct)`: abre, manda `19 00` a la
     dirección de difusión `00` (y si no contesta, a la de cada modelo), devuelve
     `EquipoIdentificado(puerto, baudios, ProtocoloCat.IcomCiv, "94", CatalogoDeModelos.PorDireccionCiv(0x94))`
     o nulo. **Solo consultas; nunca transmitir.** Cerrar el puerto al acabar.
2. El control (`ControlIcom`, o uno por familia) implementa:
   - **Obligatorio**: `IControlEquipo`, `IEquipoDeModelo`, `IPttDirecto` (interna, del
     ensamblado) y `ISueltaDeEmergenciaPtt` (vías para bajar el PTT pase lo que pase: línea
     RTS/DTR si se usa, `1C 00 00` por el canal abierto, reabrir y `1C 00 00`),
     `IAvisaDePerdidaDeComunicacion`. `IControlEquipo.PonerPttAsync(true)` **debe rechazarse**
     con `GuardiaDelPtt.Rechazar(...)`: el PTT solo lo sube el vigilante.
   - **Opcional, según pueda el modelo**: `IEquipoAvanzado` (mandos con `Rango`, medidores,
     memorias, orden en crudo que rechace cualquier orden de transmitir), `IEquipoConDosVfos`,
     `IEquipoConTeclas`, `IEquipoConBotonera` (teclas de banda y modo), `IEquipoConEncendido`.
   - Un mando que el equipo no admite **no se pone en `Mandos`**: la interfaz no enseña botones
     muertos. Una tecla que no existe no va en `Teclas`.
3. Nada más: no hay que tocar el catálogo, la fábrica ni los Ajustes. Las pruebas con doble
   (el equipo simulado) en `tests/Nodisla.Cuaderno.Radio.Pruebas/Icom/`.

## 3. Contrato para el especialista de frontales (`Ui/Vistas/Frontales/`)

- Cada frontal es un `UserControl` con constructor sin parámetros, en el espacio de nombres
  `Nodisla.Cuaderno.Ui.Vistas.Frontales`, cuyo **nombre de clase es el de
  `ModeloDeEquipo.Frontal`** (`FrontalFtdx10`, `FrontalFt991`, `FrontalIc7300`...). El genérico
  se llama **`FrontalGenerico`** y es el que sale para cualquier modelo sin dibujo propio y sin
  equipo. Mientras no exista `FrontalGenerico`, sale el del FT-710 (lo de antes).
- Lo elige `SelectorDeFrontal` (`Ui/Vistas/SelectorDeFrontal.cs`) y lo pone
  `FrontalConBotonera` en un `ContentControl` llamado `Frontal`, al que da el alto máximo.
  Lienzo de **1040 × 335** (`FrontalConBotonera.ProporcionDelFrontal`) dentro de un `Viewbox`,
  como `FrontalFt710.xaml`, para que el reparto con la botonera HAM/CB siga cuadrando.
- `DataContext` heredado = `VistaModeloEquipo`. Lo que hay para enlazar:
  - Modelo: `Modelo` (`ModeloDeEquipo?`), `NombreDelFrontal`, `NombreDelEquipo`,
    `SinProbarConRadio` (para el rótulo «según manual»), `TieneDosVfos`, `EsAvanzado`,
    `HayBotonera`, `PuedeEncenderOApagar`, `TieneTecla(tecla)`.
  - Estado: `Conectado`, `Transmitiendo`, `Frecuencia`, `Modo`, `Banda`, `Vfo`, `Split`,
    `Rit`/`DesplazamientoRit`, `Xit`/`DesplazamientoXit`, `TextoDeSplit`, `TextoDeRit`,
    `EnMemoria`, `EnMox`, `A`/`B`/`Principal`/`Secundario` (`VistaModeloVfo`:
    `FrecuenciaDelVisor`, `Modo`, `Banda`, `AnchoDeFiltro`, `EsElActivo`, `Papel`).
  - Medidores: `MedidorS`, `MedidorPotencia`, `MedidorRoe`, `MedidorAlc`, `MedidorCorriente`,
    `MedidorTension`, `NivelDeSenal`, `NivelDePotencia`.
  - Mandos: `Frontal[nombreDelMando]` (un `VistaModeloMando`, o nulo) y
    `Pilotos[nombreDelMando]` (el mando o `MandoAusente`, con `Disponible = false`). Nombres
    = los de `MandoDeEquipo` (`Potencia`, `Agc`, `Atenuador`, `Preamplificador`,
    `SupresorDeRuido`, `ReductorDeRuido`, `AnchoDeFiltro`, `Split`...). `VistaModeloMando`:
    `Valor`, `ValorTexto`, `Encendido`, `Posicion`, `Posiciones`, `Disponible`, `Minimo`,
    `Maximo`, `Paso`. Atajos: `MandoDeVolumen`, `MandoDeGananciaRf`, `MandoDePotencia`,
    `MandoDeAnchoDeFiltro`.
  - Órdenes: `PulsarTeclaCommand` (parámetro `TeclaDelEquipo`), `AlternarMandoCommand`
    (`MandoDeEquipo`), `SiguientePosicionCommand` (`MandoDeEquipo`), `SiguienteModoCommand`,
    `SiguienteBandaCommand`, `IntercambiarVfosCommand`, `IgualarVfosCommand`,
    `ActivarVfoCommand` (`VistaModeloVfo`), `MoxCommand`, `SintonizarAcopladorCommand`,
    `EncenderOApagarCommand`, `IrABandaCommand`/`PonerModoDeTeclaCommand` (botonera), y los
    métodos `GirarDialAsync(muescas)`, `GirarPasosAsync(muescas)`, `MoverClarificador(muescas)`
    (desde el code-behind, como `FrontalFt710.xaml.cs`).
- Regla: un botón cuyo mando o tecla no exista en el equipo conectado se dibuja **apagado y
  deshabilitado** (enlazar a `Pilotos[...].Disponible` o `TieneTecla`), nunca muerto.

## 4. Modelos Yaesu

**Todo lo que no es el FT-710 está programado según el manual CAT de cada modelo y sin probar
con la radio.** Pruebas con dobles que contestan como dice el manual:
`tests/Nodisla.Cuaderno.Radio.Pruebas/ModelosYaesuPruebas.cs` y `YaesuBinarioPruebas.cs`.

Manuales usados (descargados de yaesu.com a una carpeta temporal, no al repo):
FTDX10_CAT_OM_ENG_2308-F, FTDX101D_CAT_OM_ENG_1909-C, FT-991_CAT_OM_ENG_1612-D0,
FT-991A_CAT_OM_ENG_1711-D, FT-891_CAT_OM_ENG_1909-C, FTDX3000_CAT_OM_ENG, FTDX1200_CAT_OM_ENG,
FTDX5000_CAT_OM_ENG_1907-D, FT-817ND_OM_ENG_E13771011, FT-818ND_OM_ENG_E13772004,
FT-857D_OM_ENG_EH007M108. El del FT-897D no está en inglés en yaesu.com: usa el mismo juego de
órdenes que el FT-857D.

### 4.1 CAT nuevo (ASCII con `;`): un solo control

`ControlFt710` sirve a toda la familia con un `PerfilYaesu` (`Control/Yaesu/PerfilesYaesu.cs`)
que dice lo que cambia. Sin perfil es el FT-710 de siempre (las pruebas del FT-710 siguen igual).
Al conectar se pregunta por cada mando de la tabla del modelo y **el que contesta `?;` no se
ofrece**; las teclas que el modelo no tiene no están en `Teclas` (la interfaz las apaga).

| Modelo | ID | Frec. | MD0 / FT | Split | Clarificador | Acoplador | Frontal |
|---|---|---|---|---|---|---|---|
| FT-710 (validado) | 0800 | 9 | VFO activo | ST | CF | AC003, fin por RI P6 | FrontalFt710 |
| FTDX10 | 0761 | 9 | VFO activo | ST | RT/XT, RC | AC002, sin indicador | FrontalFtdx10 |
| FTDX101D / MP | 0681 / 0682 | 9 | MD0 = A, MD1 = B; FT absoluto | ST | RT/XT, RC | AC002 | FrontalFtdx101 |
| FT-991 / FT-991A | 0570 / **0670** | 9 | solo MD0 (sin VS ni MD1) | por FT (FT2/FT3) | RT/XT, RC | AC002 | FrontalFt991 |
| FT-891 | 0650 | 9 | solo MD0 | ST | CF, RC | AC (FC-50 externo) | FrontalFt891 |
| FTDX3000 | 0460 (*) | **8** | solo MD0 | por FT | RT/XT, RC | AC002 | genérico |
| FTDX1200 | 0583 (0582 con FFT-1) | **8** | solo MD0 | por FT | RT/XT, RC | AC002 | genérico |
| FTDX5000 | 0362 | **8** | MD0 = A, MD1 = B | por FT | RT/XT, RC | AC002 | genérico |

(*) El manual CAT del FTDX3000 no documenta `ID`; 0460 es el que usa Hamlib. Ojo: el FT-991A
contesta **0670**, no 0570 (ese es el FT-991 sin A).

Funciona en todos (según manual): frecuencia de A y B, modo, VFO A/B (`VS` donde existe),
intercambiar (`SV` si no hay `MD1`), A=B, split, bandas `BS` (con 2 m/70 cm en el FT-991 y 4 m
en el FTDX101), BAND arriba/abajo, M▶V, V/M, QMB, ZIN (no en FTDX3000/5000), borrar
clarificador, dial y mando de canales (paso fijo: 10 Hz / 1 kHz; el menú de pasos es solo del
FT-710), memorias `MR`/`MC` (rótulos `MT` solo en los modernos), medidores `RM`/`SM`, potencia
(hasta 200 W en FTDX101MP y FTDX5000), ganancias, AGC, ATT (solo encendido/apagado en
FT-991/891), IPO/AMP (IPO/AMP en el FT-891), NB/DNR/NAR, muesca, contorno, desplazamiento de FI
(`IS` con su formato: `IS0+1000` en los antiguos, `IS0P2±` en el FT-891), ancho de filtro por
índice, telegrafía, VOX y retardos (`VD`/`SD` en ms de 4 cifras en FT-991/891 y antiguos),
encender/apagar (`PS`), PTT con el vigilante y sintonía del acoplador dentro de una transmisión
vigilada. Solo del FT-710: analizador de espectro (FT4222), mandos FUNC/DSP, pantalla,
anchos de filtro en hercios, paso del dial por menú, DSP RESET y el aviso de radio encendida por
el códec USB.

**Confirmar con la radio real en cada modelo** (con el FT-710 el manual no bastó):
- A qué VFO se refieren `MD0` y `FT` de verdad (en el FT-710 `MD0` sigue a `VS`, contra lo que
  parece decir el manual). En el FTDX10, si `SV` intercambia o solo cambia de VFO.
- Escala y formato de `RM` y `SM` (en el FT-710 hubo que cortar a 3 cifras) y dónde cae S9.
- Máximo del índice de `SH` (se ha puesto 21 donde el manual no está claro) y su formato.
- Que la sintonía arranca con `AC002` y cómo pararla; ninguno salvo el FT-710 dice por CAT
  cuándo termina (se suelta a los 5 s y se avisa).
- El rango de `PC` en FTDX101MP y FTDX5000 (el manual da 5-100 y 0-255).
- `VD` del FTDX10/101 (el manual dice 4 cifras con índice de 2; se deja como en el FT-710).
- En los de 8 cifras, que el `IF` y el `MR` traen la frecuencia en 8 cifras (así lo dice el manual).

### 4.2 CAT antiguo (binario, 5 bytes): FT-817/818/857/897

`ControlYaesuBinario` (`Control/Yaesu/`), canal serie 8N2. Lo que da su CAT: leer frecuencia y
modo (03), poner frecuencia (01, BCD en decenas de hercio), modo (07), PTT (08/88, solo por el
vigilante), VFO A/B (81, alterna), split (02/82), clarificador (05/85), bloqueo (00/80), medidor
S (E7) y estado de transmisión (F7, de latido en antena). Split, clarificador y bloqueo **no se
pueden leer**: se enseña lo último que se puso desde el programa. Sin memorias, sin BAND (la
botonera pone una frecuencia de partida por banda), sin orden en crudo (es binario). No se
autodetecta (no tiene identificación): el modelo se elige en Ajustes.
Confirmar con la radio: velocidad (menú CAT RATE), 2 bits de parada, bits del E7 y códigos
DIG/PKT (0A/0C).

## 5. Ajustes › CAT

Vía «CAT nativo (Yaesu e ICOM)», **Fabricante** (Detección automática / Yaesu / ICOM) y
**Modelo** (el desplegable marca «según manual, sin probar con radio» y debajo sale lo que hay
que confirmar). Con ICOM, dirección CI-V en hexadecimal (vacía = la de fábrica). Se guarda en
`AjustesDeEquipo.Modelo` (`auto` o la clave) y `DireccionCiv`.
