# 18 · Frontales dibujados de otros modelos

Además del FT-710 (`Vistas/FrontalFt710.xaml`, sin cambios), hay frontal dibujado en vector
para el **FTDX10, FTDX101D/MP, FT-991/991A, FT-891, IC-7300, IC-705 e IC-7610**, y uno
**genérico** para cualquier otro modelo. Se dibujan: no se pega ninguna foto ni logotipo; la
marca y el modelo van escritos como texto.

## Dónde está

`src/Nodisla.Cuaderno.Ui/Vistas/Frontales/`

| Fichero | Qué es |
|---|---|
| `FrontalFtdx10`, `FrontalFtdx101`, `FrontalFt991`, `FrontalFt891`, `FrontalIc7300`, `FrontalIc705`, `FrontalIc7610`, `FrontalGenerico` | Un frontal por modelo. El nombre de la clase es el de `ModeloDeEquipo.Frontal` (docs/16-modelos.md, sección 3). |
| `FrontalDibujado.cs` | Base común: POWER (`EncenderOApagarAsync`), rueda sobre CLAR/RIT (`MoverClarificador`), anillo MPVD y STEP/MCH (`GirarPasosAsync`), y `ConAnalizador`/`DosReceptores` sacados de `Modelo.Capacidades`. |
| `PiezasDeLosFrontales.xaml` | Colores, tecla de goma Yaesu (`TeclaDelFrontal`), tecla ICOM (`TeclaIcom`), serigrafía, pilotos TX y encendido, rótulo «según manual, sin probar con radio». |
| `DialDelFrontal` | El dial grande del FT-710 como control: rueda y flechas → `GirarDialAsync`. |
| `PantallaIcom` | Pantalla táctil a color de ICOM en lo esencial: indicadores, medidor de barra, modo y filtro, frecuencia grande (o MAIN/SUB a la par), analizador (`AnalizadorDelEquipo`) y franja de abajo. El modo, la banda, SPAN, CENT/FIX, EXPD y SPEED se tocan en ella. También la usa el genérico. |
| `MedidorDeBarra.cs` | Medidor S/Po de barra (ICOM y LCD del FT-891), mismos niveles que `MedidorDeArco`. |

Todos usan el lienzo común de **1040 × 335** dentro de un `Viewbox`; los modelos más estrechos
van centrados con su proporción real. Lo elige `SelectorDeFrontal` y lo pone `FrontalConBotonera`
(con la botonera HAM/CB a los lados, igual para todos).

Piezas reutilizadas del FT-710: `MandoGiratorio`, `CifrasDeSieteSegmentos` (FT-891),
`VisorDeVfos` + `AnalizadorDelEquipo` (FTDX10, FTDX101 y FT-991A, que tienen pantalla del
mismo estilo), `MedidorDeArco` (dentro del visor).

## Reglas que cumplen

- Mismas órdenes de `VistaModeloEquipo` que el FT-710: `AlternarMandoCommand`,
  `SiguientePosicionCommand` (ATT, IPO/P.AMP, AGC, R.FLT, ANT, FINE/TS), `PulsarTeclaCommand`
  (V/M, M▶V, QMB, ZIN, CLEAR), `SiguienteModoCommand`, `SiguienteBandaCommand`,
  `ActivarVfoCommand` con `Secundario` (A/B; clic derecho `IgualarVfosCommand`),
  `IntercambiarVfosCommand` (CHANGE del IC-7610), `MoxCommand` (MOX/TRANSMIT/PTT),
  `SintonizarAcopladorCommand` (clic derecho en TUNE/TUNER).
- Lo que el equipo no declara sale **apagado** (la orden no se puede ejecutar). Los pilotos se
  enlazan a `Pilotos[...]`, nunca nulos. Teclas que en la radio abren menús (MENU, FUNCTION,
  QUICK, EXIT, XFC, REF) y los mandos del receptor SUB, sin orden en el programa, salen apagados.
- Sin analizador (FT-891, o un modelo cuyo `Capacidades.Analizador` sea falso) no se dibuja.
- «según manual, sin probar con radio» mientras `SinProbarConRadio`.

## Verlos sin radio

`CUADERNO_MODELO=<modelo>` hace que el equipo simulado (`Desarrollo/EquipoSimulado.cs`,
`Desarrollo/ModeloSimulado.cs`) se presente como ese modelo: se busca en `CatalogoDeModelos`
por clave o por nombre (`ic7300`, `IC-7300`, `icom-ic7300`, `FT-991A`...) y, si el catálogo aún no
lo tiene, en una tabla propia. `generico` da un modelo sin dibujo. Sin la variable, el FT-710 de
siempre. El modelo sin sintonizador o sin analizador no declara esos mandos.

Capturas (instancia apartada: `CUADERNO_APARTADA=1`, `CUADERNO_SIMULADO=1`,
`CUADERNO_CARPETA=<temporal>`, `CUADERNO_MODELO`, `CUADERNO_FRONTAL=1`, `CUADERNO_CAPTURA`,
`CUADERNO_ESPERA=9`, `CUADERNO_TAMANO=1600x1000`, `CUADERNO_RASTREO_ENLACES`), todas con
`.rotos.txt` vacío (0 enlaces rotos):

`docs/capturas/frontal-ftdx10.png`, `frontal-ftdx101.png`, `frontal-ft991a.png`,
`frontal-ft891.png`, `frontal-ic7300.png`, `frontal-ic705.png`, `frontal-ic7610.png`,
`frontal-generico.png`.

## Referencias (solo URLs; no se guardan fotos en el repositorio)

La colocación se ha sacado de las fotos del fabricante y de la lista de mandos del frontal de
cada manual. Es **aproximada**: no se ha medido sobre una radio real como el FT-710 (la de
Jose). Cuando haya una radio delante conviene compararla y ajustar coordenadas.

**Yaesu**

- FTDX10 —
  manual de operación: https://www.manualslib.com/manual/2150765/Yaesu-Ftdx10.html ·
  CAT: https://www.yaesu.com/downloadFile.cfm?FileID=17849&FileCatID=158&FileName=FTDX10_CAT_OM_ENG_2308-F.pdf&FileContentType=application/pdf
- FTDX101D/MP — ficha: https://yaesu.com/product-detail.aspx?Model=FTDX101D&CatName=HF+Transceivers%2FAmplifiers
- FT-991A — manual de operación: https://www.manualslib.com/manual/2717394/yaesu-ft-991a.html ·
  CAT: https://yaesu.com/Files/4CB893D7-1018-01AF-FA97E9E9AD48B50C/FT-991A_CAT_OM_ENG_1711-D.pdf
- FT-891 — manual de operación: https://www.manualslib.com/manual/1390610/Yaesu-Ft-891.html ·
  manual rápido: https://public2024.yaesu.com/FileLibraryF/4CB893D7-1018-01AF-FA97E9E9AD48B50C/FT-891_QM_ENG_EH065H550.pdf ·
  manual avanzado: https://www.yaesu.com/downloadFile.cfm?FileCatID=158&FileContentType=application%2Fpdf&FileID=14759&FileName=FT-891_Advance_Manual_ENG_1806-F.pdf

**ICOM**

- IC-7300 — ficha: https://www.icomjapan.com/lineup/products/IC-7300_USA/ ·
  manuales: https://www.icomjapan.com/support/manual/2271/ ·
  manual básico: https://www.manualslib.com/manual/1022466/Icom-Ic-7300.html
- IC-705 — ficha: https://www.icomjapan.com/lineup/products/IC-705/
- IC-7610 — ficha: https://www.icomjapan.com/lineup/products/IC-7610/ ·
  manuales: https://www.icomjapan.com/support/manual/1718/ ·
  manual básico: https://www.manualslib.com/manual/1313711/Icom-Ic-7610.html

## Pendiente de confirmar con cada radio

- Posiciones y tamaños exactos de teclas y mandos (medir sobre foto propia, como el FT-710).
- Color real del LCD del FT-891 (se ha dibujado ámbar con cifras oscuras).
- Qué ajusta el mando MULTI en cada modelo: aquí va a la potencia (FTDX10, FT-991A, FT-891,
  IC-7300, IC-705) o al ancho de filtro (IC-7610), porque el programa no sigue su función.
- Mandos del receptor SUB (FTDX101, IC-7610): no hay orden propia; salen apagados.
