# Componentes de terceros

Cuaderno NODISLA se publica bajo la GPL-3.0 (fichero `LICENSE`). Incluye código portado de WDSP,
que es GPL v2 o posterior y por tanto compatible con la GPL-3.0 del conjunto. Este documento reúne lo que no
es código propio: qué es, de dónde sale, con qué licencia, y lo que **no** se incluye y hay que
conseguir aparte.

## Incluido en el repositorio

### Constantes y tablas de protocolos digitales

| Componente | Dónde | Procedencia | Licencia |
|---|---|---|---|
| Matrices del LDPC(174,91) y secuencia de mezcla de FT4 | `src/Nodisla.Cuaderno.Modos/Tablas/tablas-ft8.txt` | [`ft8_lib`](https://github.com/kgoba/ft8_lib), Kārlis Goba (YL3JG) | MIT, © 2018 Kārlis Goba |
| Tablas de FST4/FST4W y MSK144/MSK40 | `src/Nodisla.Cuaderno.Modos/Tablas/tablas-fst4*.txt`, `tablas-msk*.txt` | Rama de `ft8_lib` de howard0su sobre el trabajo de Kārlis Goba | MIT, © 2018 Kārlis Goba |
| Constantes de protocolo de Q65, JT65, JT9, WSPR, MSK144 y FST4 | `src/Nodisla.Cuaderno.Modos/Tablas/` y las clases `Tablas*.cs` | Especificaciones publicadas de [WSJT-X](https://sourceforge.net/projects/wsjt/) (K1JT, K9AN y colaboradores) y artículos de QEX | WSJT-X es GPL-3.0, compatible. No se ha copiado código de WSJT-X: solo constantes del protocolo, con su procedencia anotada en cada `LEEME` de esa carpeta |
| Función de resumen `lookup3` (`hashlittle`) | `src/Nodisla.Cuaderno.Modos/Wspr/HashDeIndicativo.cs` | Bob Jenkins, 2006 | Dominio público |

### Protocolos CAT de las radios

| Componente | Dónde | Procedencia |
|---|---|---|
| CAT ASCII de Yaesu (FT-710, FT-891, FT-991/991A, FTDX10, FTDX101D/MP, FTDX3000, FTDX5000, FTDX1200) y CAT binario de 5 bytes (FT-817/818, FT-857, FT-897) | `src/Nodisla.Cuaderno.Radio/Control/Ft710/`, `Control/Yaesu/` | Escrito desde cero a partir de los manuales CAT públicos de Yaesu. Solo el FT-710 está validado contra la radio real |
| CI-V de ICOM (IC-7300, IC-705, IC-7610, IC-9700, IC-7100, IC-7851) | `src/Nodisla.Cuaderno.Radio/Control/Icom/` | Escrito desde cero a partir de los manuales CI-V públicos de ICOM. Sin probar con radio real |

Los manuales no se incluyen: se descargan de yaesu.com e icomjapan.com.

### Datos de referencia

| Componente | Dónde | Procedencia | Licencia / notas |
|---|---|---|---|
| Tabla de países DXCC, prefijos y excepciones | `recursos/paises-nodisla.tsv`, `src/Nodisla.Cuaderno.Dominio/Dxcc/DatosPaises.cs` | `cty.dat` de AD1C ([country-files.com](https://www.country-files.com/)) y `country.xml` de Log4OM | Datos factuales de libre uso en programas de registro; sin licencia explícita |
| Plan de bandas | `recursos/bandplan-nodisla.tsv`, `.../Bandplan/DatosBandplan.cs` | Planes de banda IARU (regiones 1-3) tal como los trae Log4OM | Datos factuales; sin licencia explícita |
| Catálogo de concursos | `recursos/concursos/`, `.../Concursos/Recursos/catalogo-concursos.tsv.gz` | Nombres e identificadores Cabrillo del `contest.csv` de Log4OM; reglas escritas a mano | Datos factuales; sin licencia explícita |
| Catálogo de satélites | `recursos/satelites/satelites-nodisla.tsv` | Datos públicos de AMSAT y de los propios satélites, escritos a mano | Datos factuales |
| Catálogo reducido de diplomas para las pruebas | `tests/Nodisla.Cuaderno.Diplomas.Pruebas/Datos/catalogo-de-prueba.tsv.gz` | Definiciones de los 87 diplomas y solo referencias factuales: entidades DXCC, estados de EE. UU., cantones suizos, prefijos de la Commonwealth, bases antárticas y un puñado de IOTA y POTA | Solo para las pruebas; no es el catálogo del programa |
| Casos de verificación de SGP4 | `tests/Nodisla.Cuaderno.Satelites.Pruebas/Datos/SGP4-VER.TLE`, `tcppver.out` | Vallado, Crawford, Hujsak y Kelso, *Revisiting Spacetrack Report #3* (AIAA 2006), distribuidos por CelesTrak | Distribuidos libremente como banco de verificación |

### Traducciones

Los textos de la interfaz en inglés, francés, alemán, italiano y portugués
(`src/Nodisla.Cuaderno.Idiomas/Recursos/*.resx`) y la ayuda en inglés (`docs/ayuda/en`) son
traducciones propias del proyecto, bajo la misma GPL-3.0. El instalador usa los ficheros de
idioma que trae Inno Setup, que no se incluyen en el repositorio.

### Procesado de audio portado de WDSP y fórmulas de terceros

#### WDSP — Warren Pratt, NR0V (GPL v2 o posterior)

Portado a C# desde el código de WDSP que distribuye Thetis (`Project Files/Source/wdsp`,
repositorio `ramdor/Thetis`). Cada fichero portado conserva en su cabecera el aviso de
copyright y de licencia del original.

| Fichero del Cuaderno | Original de WDSP | Copyright |
|---|---|---|
| `src/Nodisla.Cuaderno.Audio/Procesado/FiltroLmsAdaptativo.cs` | `anf.c` (ANF) y `anr.c` (ANR, «NR1») | (C) 2012, 2013 Warren Pratt, NR0V |
| `src/Nodisla.Cuaderno.Audio/Procesado/ReductorEspectral.cs` | `emnr.c` (EMNR, «NR2»): estimador de ruido por probabilidad de presencia (`LambdaDs`), ganancias MMSE gaussiana y MMSE-LSA (`calc_gain`, métodos 0 y 1), filtro de artefactos (`aepf`), ventana, funciones de Bessel I0/I1 y `e1xb` | (C) 2015, 2025 Warren Pratt, NR0V |

Cambios respecto al original: tramas de unos 10 ms en lugar de 4.096 puntos (para no retrasar
la escucha), solape de 4 como en WDSP, transformada propia en lugar de FFTW, un nivel que mezcla
la ganancia con la unidad, el alisado de `aepf` escalado al tamaño de trama, y fuera las tablas
entrenadas (`GG`, `GGS`, `zetaHat`), el estimador de mínimos y el ruido de confort (`post2`). En
el filtro LMS, el paso y la fuga son los que pone Thetis al arrancar (`radio.cs`).

Las funciones de Bessel y la integral exponencial siguen, como cita `emnr.c`, a M. Abramowitz e
I. Stegun (*Handbook of Mathematical Functions*, 1964) y a S. Zhang y J. Jin (*Computation of
Special Functions*, 1996).

> This program is free software; you can redistribute it and/or modify it under the terms of
> the GNU General Public License as published by the Free Software Foundation; either version 2
> of the License, or (at your option) any later version. This program is distributed in the hope
> that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of
> MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
> more details.

**Consecuencia:** un programa que incluye código GPL v2+ y se distribuye tiene que
distribuirse bajo una licencia compatible (GPL v2 o posterior, con su código fuente). Ver
`docs/19-analisis-thetis.md`, apartado 1.

#### Audio EQ Cookbook — Robert Bristow-Johnson

`src/Nodisla.Cuaderno.Audio/Procesado/FiltroBicuadratico.cs` usa las fórmulas de los filtros de
segundo orden (paso alto, estanterías y campana) publicadas por R. Bristow-Johnson en el «Audio
EQ Cookbook», de uso libre. No se copia código: solo las fórmulas.

#### RNNoise — no se usa

Se estudió (BSD-3, Xiph.Org / Jean-Marc Valin) y se dejó fuera: habría que llevar una DLL nativa
o portar la red con sus pesos. El reductor espectral portado de WDSP cubre la necesidad.

### Material propio basado en terceros

- **Frontales de las radios** (FT-710, FT-891, FT-991A, FTDX10, FTDX101, IC-705, IC-7300,
  IC-7610 y uno genérico): dibujados en vector desde cero; no incluyen imágenes de los
  fabricantes. «Yaesu», «ICOM» y los nombres de los modelos son marcas de sus dueños y se
  nombran solo para decir con qué equipos funciona el programa.
- **Grabaciones de aire** `tests/Nodisla.Cuaderno.Modos.Pruebas/Aire/*.wav`: emisiones públicas
  de FT8 de otras estaciones, grabadas en recepción para probar el decodificador, y dos tramos de
  ruido de banda tras el filtro de CW del receptor (`cw-ruido-ft710-*.wav`), sin señales ni voz,
  para comprobar que el decodificador de CW no inventa texto.
- **Capturas de CAT y del analizador del FT-710** `tests/Nodisla.Cuaderno.Radio.Pruebas/Capturas/`:
  respuestas técnicas de la radio, sin datos personales.
- **Mapa**: los mosaicos se descargan en ejecución de OpenStreetMap
  (© colaboradores de OpenStreetMap, ODbL); no se incluye ninguno en el repositorio.

### Paquetes NuGet (se descargan al compilar, no están en el repositorio)

| Paquete | Licencia |
|---|---|
| Microsoft.EntityFrameworkCore(.Sqlite/.Design), Microsoft.Data.Sqlite, Microsoft.Extensions.*, System.IO.Ports, System.Security.Cryptography.ProtectedData | MIT |
| CommunityToolkit.Mvvm | MIT |
| Dapper | Apache-2.0 |
| Serilog, Serilog.Extensions.Hosting, Serilog.Sinks.File, Serilog.Sinks.Console | Apache-2.0 |
| FluentValidation | Apache-2.0 |
| NAudio | MS-PL |
| Mapsui.Wpf | MIT |
| PDFsharp | MIT |
| xunit, xunit.runner.visualstudio, AwesomeAssertions | Apache-2.0 |
| Microsoft.NET.Test.Sdk, coverlet.collector | MIT |

## No incluido: hay que conseguirlo aparte

| Componente | Por qué no está | Cómo conseguirlo |
|---|---|---|
| **Catálogo de diplomas** (553.064 referencias de 87 diplomas) | Sale de la base `Activations.SQLite` de Log4OM y de las listas de cada programa (IOTA, POTA, SOTA, WWFF…), sin licencia para redistribuirlo | `recursos/diplomas/generar-diplomas.py` con su propio Log4OM; ver `recursos/diplomas/LEEME.md`. Sin él la pestaña Diplomas queda vacía y dice cómo conseguirlo |
| **LibFT4222** de FTDI (`LibFT4222-64.dll`) | Su licencia prohíbe entregarla a terceros | [ftdichip.com](https://ftdichip.com/products/ft4222h/); ver `lib/ftdi/LEEME.md`. Sin ella todo funciona salvo el analizador de espectro del FT-710 |
| **ITURHFProp** (motor P.533 de la UIT) | Su permiso cubre a quien implementa la Recomendación, no la redistribución | [ITU-R-HF](https://github.com/ITU-R-Study-Group-3/ITU-R-HF); ver `herramientas/ITURHFProp/LEEME.md`. Sin él se usa la aproximación propia |
| **Manuales CAT/CI-V** de Yaesu e ICOM | Derechos de autor de los fabricantes | [yaesu.com](https://www.yaesu.com), [icomjapan.com](https://www.icomjapan.com) |
| **Hamlib** (`rigctld`) y **OmniRig** | Programas externos opcionales | [hamlib.github.io](https://hamlib.github.io/), [OmniRig](https://www.dxatlas.com/omnirig/) |
| **WSJT-X / JTDX** | Opcionales: el programa trae su módem propio | Sus webs oficiales |
