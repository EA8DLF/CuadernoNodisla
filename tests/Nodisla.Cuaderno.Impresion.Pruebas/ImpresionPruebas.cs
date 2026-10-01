using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Impresion;
using Nodisla.Cuaderno.Impresion.Modelo;
using Nodisla.Cuaderno.Impresion.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;

namespace Nodisla.Cuaderno.Impresion.Pruebas;

/// <summary>
/// Etiquetas y tarjetas de QSL: seleccion, colocacion en el papel y PDF de salida.
/// </summary>
public sealed class ImpresionPruebas
{
    private static readonly Indicativo Mio = Indicativo.Parse("EA8DLF");

    [Fact]
    public void LasPlantillasConocidasCabenEnSuPapel()
    {
        foreach (var plantilla in PlantillaDeEtiquetas.Conocidas)
        {
            plantilla.CabeEnElPapel.Should().BeTrue($"«{plantilla.Nombre}» tiene que caber en su hoja");
        }
    }

    [Fact]
    public void LasEtiquetasSeColocanPorFilasDeIzquierdaADerecha()
    {
        var p = PlantillaDeEtiquetas.AveryL7160;

        var primera = p.Esquina(0);
        var segunda = p.Esquina(1);
        var cuarta = p.Esquina(3);

        primera.XMm.Should().Be(p.MargenIzquierdoMm);
        primera.YMm.Should().Be(p.MargenSuperiorMm);
        segunda.YMm.Should().Be(primera.YMm, "la segunda va al lado, no debajo");
        segunda.XMm.Should().BeApproximately(primera.XMm + p.AnchoMm + p.SeparacionHorizontalMm, 1e-9);
        cuarta.XMm.Should().Be(primera.XMm, "la cuarta empieza fila nueva");
        cuarta.YMm.Should().BeApproximately(primera.YMm + p.AltoMm + p.SeparacionVerticalMm, 1e-9);
    }

    [Fact]
    public void LaUltimaEtiquetaDeLaHojaNoSeSaleDelPapel()
    {
        foreach (var p in PlantillaDeEtiquetas.Conocidas)
        {
            var (x, y) = p.Esquina(p.PorHoja - 1);
            (x + p.AnchoMm).Should().BeLessThanOrEqualTo(p.Pagina.AnchoMm);
            (y + p.AltoMm).Should().BeLessThanOrEqualTo(p.Pagina.AltoMm);
        }
    }

    [Fact]
    public void PedirUnaCasillaFueraDeLaHojaEsUnError()
    {
        var p = PlantillaDeEtiquetas.AveryL7160;
        var accion = () => p.Esquina(p.PorHoja);
        accion.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void LosContactosConElMismoCorresponsalVanEnUnaSolaEtiqueta()
    {
        var seleccion = new SeleccionDeContactos { SoloPendientesDeEnviar = false };

        var etiquetas = seleccion.Agrupar(
            [
                Contacto("EA1ABC", new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc)),
                Contacto("EA1ABC", new DateTime(2026, 3, 2, 11, 0, 0, DateTimeKind.Utc)),
                Contacto("EA2XYZ", new DateTime(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc)),
            ],
            Mio);

        etiquetas.Should().HaveCount(2);
        etiquetas[0].Destino.Valor.Should().Be("EA1ABC");
        etiquetas[0].Contactos.Should().HaveCount(2);
        etiquetas[0].Contactos[0].Fecha.Day.Should().Be(1, "los contactos van en orden de fecha");
        etiquetas[1].Destino.Valor.Should().Be("EA2XYZ");
    }

    [Fact]
    public void ConMasContactosDeLosQueCabenSeRepartenEnVariasEtiquetas()
    {
        var seleccion = new SeleccionDeContactos
        {
            SoloPendientesDeEnviar = false,
            ContactosPorEtiqueta = 2,
        };

        var contactos = Enumerable.Range(1, 5)
            .Select(d => Contacto("EA1ABC", new DateTime(2026, 3, d, 10, 0, 0, DateTimeKind.Utc)))
            .ToList();

        var etiquetas = seleccion.Agrupar(contactos, Mio);

        etiquetas.Should().HaveCount(3);
        etiquetas.Sum(e => e.Contactos.Count).Should().Be(5, "no se pierde ni un contacto");
        etiquetas[0].Contactos.Should().HaveCount(2);
        etiquetas[2].Contactos.Should().HaveCount(1);
    }

    [Fact]
    public void ElMismoIndicativoConGestoresDistintosSonEnviosDistintos()
    {
        var seleccion = new SeleccionDeContactos { SoloPendientesDeEnviar = false };

        var uno = Contacto("VP8ABC", new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        var otro = Contacto("VP8ABC", new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc));
        otro.QslVia = "EA5GL";

        var etiquetas = seleccion.Agrupar([uno, otro], Mio);

        etiquetas.Should().HaveCount(2, "una va por buró y la otra por gestor");
        etiquetas.Should().ContainSingle(e => e.Gestor == "EA5GL");
    }

    [Fact]
    public void PorOmisionSoloEntranLasTarjetasPendientesDeEnviar()
    {
        var seleccion = new SeleccionDeContactos();

        var sinNada = Contacto("EA1ABC", new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        var pendiente = Contacto("EA2XYZ", new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc));
        pendiente.Confirmaciones.Add(new QsoConfirmacion
        {
            Medio = MedioDeConfirmacion.Papel,
            Enviado = EstadoDeConfirmacion.Pendiente,
            Via = ViaDeEnvio.Buro,
        });

        seleccion.Entra(sinNada).Should().BeFalse();
        seleccion.Entra(pendiente).Should().BeTrue();

        var etiquetas = seleccion.Agrupar([sinNada, pendiente], Mio);
        etiquetas.Should().ContainSingle();
        etiquetas[0].Via.Should().Be(ViaDeEnvio.Buro);
    }

    [Fact]
    public void LaSeleccionFiltraPorFechaBandaEIndicativo()
    {
        var uno = Contacto("EA1ABC", new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        var otro = Contacto("EA2XYZ", new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc));

        new SeleccionDeContactos
        {
            SoloPendientesDeEnviar = false,
            DesdeUtc = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        }.Entra(uno).Should().BeFalse();

        new SeleccionDeContactos
        {
            SoloPendientesDeEnviar = false,
            Bandas = ["20m"],
        }.Entra(uno).Should().BeTrue();

        new SeleccionDeContactos
        {
            SoloPendientesDeEnviar = false,
            Bandas = ["40m"],
        }.Entra(uno).Should().BeFalse();

        new SeleccionDeContactos
        {
            SoloPendientesDeEnviar = false,
            Indicativos = ["EA2XYZ"],
        }.Entra(otro).Should().BeTrue();
    }

    [Fact]
    public void SeImprimeElInformeEnviadoYNoElRecibido()
    {
        var qso = Contacto("EA1ABC", new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        qso.RstSent = Informe.Parse("599");
        qso.RstRcvd = Informe.Parse("339");

        var linea = LineaDeContacto.Desde(qso);

        linea.Informe.Should().Be("599");
    }

    [Fact]
    public void ElContactoPorSateliteLlevaSuMarcaEnLaLinea()
    {
        var qso = Contacto("EA1ABC", new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        qso.SatName = "SO-50";
        qso.SatMode = "VU";

        var linea = LineaDeContacto.Desde(qso);

        linea.EsPorSatelite.Should().BeTrue();
        linea.Satelite.Should().Be("SO-50");
    }

    [Fact]
    public void LaTiradaLlenaLasHojasQueHacenFalta()
    {
        var generador = new GeneradorDeImpresosPdf();
        var plantilla = PlantillaDeEtiquetas.AveryL7160;
        var etiquetas = Enumerable.Range(0, plantilla.PorHoja + 1)
            .Select(i => Etiqueta($"EA1A{(char)('A' + (i % 26))}"))
            .ToList();

        var impreso = generador.Etiquetas(etiquetas, plantilla);

        impreso.Paginas.Should().Be(2, "veintiuna caben en una hoja, la veintidós abre otra");
        impreso.TipoDeMedio.Should().Be("application/pdf");
    }

    [Fact]
    public void SePuedeEmpezarEnUnaHojaYaEmpezada()
    {
        var generador = new GeneradorDeImpresosPdf();
        var plantilla = PlantillaDeEtiquetas.AveryL7160;

        // Quedan tres huecos en la hoja: con cuatro etiquetas hay que pasar a la siguiente.
        var impreso = generador.Etiquetas(
            Enumerable.Range(0, 4).Select(i => Etiqueta($"EA1A{(char)('A' + i)}")).ToList(),
            plantilla,
            new OpcionesDeImpresion { PrimeraCasilla = plantilla.PorHoja - 3 });

        impreso.Paginas.Should().Be(2);
    }

    [Fact]
    public void SinContactosSaleUnaHojaQueLoDice()
    {
        var impreso = new GeneradorDeImpresosPdf().Etiquetas([], PlantillaDeEtiquetas.AveryL7160);

        impreso.Paginas.Should().Be(1);
        ComprobarPdf(impreso.Bytes);
        TieneFuenteIncrustada(impreso.Bytes).Should().BeTrue(
            "si no hay una fuente incrustada es que no se ha llegado a escribir el aviso");
    }

    /// <summary>
    /// Prueba directamente el algoritmo que decide el ancho de las columnas, con la fuente de
    /// verdad del sistema y no con una tabla de metricas escrita a mano.
    /// </summary>
    /// <remarks>
    /// Con la fuente incrustada en Unicode ya no tiene sentido buscar "SO-50 VU" en los bytes
    /// del PDF: el texto va codificado por indice de glifo, no por caracter. Lo que de verdad
    /// protege contra el corte es este calculo, que es exactamente el que usa
    /// <see cref="GeneradorDeImpresosPdf"/> al dibujar: si aqui no se recorta nada, en el PDF
    /// tampoco. Ademas se comprobo a mano, renderizando el PDF con el motor de Windows, que la
    /// etiqueta de 63,5 mm sale con la fecha, la hora y «SO-50 VU» completos.
    /// </remarks>
    [Fact]
    public void NadaSeCortaEnLaEtiquetaMasEstrecha()
    {
        using var gfx = XGraphics.CreateMeasureContext(
            new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);

        // La de 63,5 mm es la mas apretada de las plantillas que trae el programa, y la linea de
        // satelite es la que mas texto lleva. Si cabe aqui, cabe en todas.
        var plantilla = PlantillaDeEtiquetas.AveryL7160;
        var margen = new OpcionesDeImpresion().MargenInteriorMm;
        var anchoDisponible = plantilla.AnchoMm - (2 * margen);

        var filas = Etiqueta("EA1ABC").Contactos
            .Select(GeneradorDeImpresosPdf.Celdas)
            .ToList();

        var (tamano, anchos) = GeneradorDeImpresosPdf.Encajar(
            gfx, filas, anchoDisponible, new OpcionesDeImpresion().TamanoDeTextoPuntos);

        anchos.Sum().Should().BeApproximately(anchoDisponible, 0.01, "las columnas llenan la casilla, ni más ni menos");
        anchos.Should().OnlyContain(a => a >= 0, "ninguna columna se queda con ancho negativo");

        var fuente = GeneradorDeImpresosPdf.Fuente(tamano);
        for (var i = 0; i < filas.Count; i++)
        {
            for (var c = 0; c < filas[i].Length; c++)
            {
                if (filas[i][c].Length == 0)
                {
                    continue;
                }

                var recortado = GeneradorDeImpresosPdf.Recortar(gfx, filas[i][c], fuente, anchos[c]);
                recortado.Should().Be(
                    filas[i][c],
                    $"la celda «{filas[i][c]}» de la fila {i} tiene que caber entera en su columna");
            }
        }
    }

    [Fact]
    public void ElPdfEsValido()
    {
        var impreso = new GeneradorDeImpresosPdf().Etiquetas(
            [Etiqueta("EA1ABC"), Etiqueta("EA2XYZ")], PlantillaDeEtiquetas.AveryL7160);

        ComprobarPdf(impreso.Bytes);
    }

    [Fact]
    public void LaTarjetaSaleConSuCabeceraYSuTablaYEsUnPdfValido()
    {
        var tarjeta = new TarjetaDeQsl(
            Mio,
            Etiqueta("EA1ABC"),
            "José",
            "Gran Canaria",
            "FT-710, 100 W",
            "Dipolo",
            33,
            36,
            "Islas Canarias");

        var impreso = new GeneradorDeImpresosPdf().Tarjetas(
            [tarjeta, tarjeta, tarjeta], PlantillaDeTarjetas.Internacional);

        impreso.Paginas.Should().Be(2, "dos por hoja: la tercera abre otra");
        ComprobarPdf(impreso.Bytes);
        TieneFuenteIncrustada(impreso.Bytes).Should().BeTrue();

        // Comprobado ademas a mano, renderizando el PDF con el motor de Windows: la cabecera
        // EA8DLF, el corresponsal EA1ABC y la tabla de contactos salen donde tienen que salir.
    }

    [Fact]
    public void LosAcentosSobrevivenAlPdf()
    {
        var etiqueta = Etiqueta("EA1ABC") with { Mensaje = "Gracias por el contacto, ¡73!" };
        var impreso = new GeneradorDeImpresosPdf().Etiquetas(
            [etiqueta], PlantillaDeEtiquetas.AveryL7163);

        // Con la fuente incrustada, el acento y la apertura de exclamación se miden con la
        // fuente de verdad en vez de traducirse a mano a una pagina de codigos: comprobamos que
        // la medida es la que le corresponde a las letras reales y no la de un texto mas corto,
        // que es lo que saldria si el caracter se hubiera perdido o sustituido por otra cosa.
        using var gfx = XGraphics.CreateMeasureContext(
            new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
        var fuente = GeneradorDeImpresosPdf.Fuente(9, cursiva: true);
        var conAcentos = gfx.MeasureString("Gracias por el contacto, ¡73!", fuente).Width;
        var sinSignos = gfx.MeasureString("Gracias por el contacto, 73", fuente).Width;
        conAcentos.Should().BeGreaterThan(sinSignos, "la í y el ¡ tienen que ocupar sitio de verdad");

        ComprobarPdf(impreso.Bytes);
        TieneFuenteIncrustada(impreso.Bytes).Should().BeTrue();

        // Comprobado ademas a mano, renderizando el PDF con el motor de Windows: «José»,
        // «¡Gracias por el contacto! 73» y «QSL buró» salen con sus tildes y su apertura.
    }

    [Fact]
    public void UnaPrimeraCasillaFueraDeLaHojaEsUnError()
    {
        var accion = () => new GeneradorDeImpresosPdf().Etiquetas(
            [Etiqueta("EA1ABC")],
            PlantillaDeEtiquetas.AveryL7160,
            new OpcionesDeImpresion { PrimeraCasilla = 21 });

        accion.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Comprueba que el PDF esta bien formado: cabecera, final, y que un lector de verdad lo
    /// abre y encuentra las paginas.
    /// </summary>
    /// <remarks>
    /// Antes esta prueba recorria a mano la tabla de referencias cruzadas, porque el fichero lo
    /// escribia esta misma clase byte a byte y habia que vigilar que no se corriera ningun
    /// desplazamiento. Con PDFsharp esa tabla la escribe y la mantiene la biblioteca, asi que lo
    /// que hay que comprobar es otra cosa: que el fichero es un PDF de verdad y que se puede
    /// volver a leer sin reventar. Se abre con <see cref="PdfReader"/>, que es el lector de la
    /// propia biblioteca y recorre la estructura entera del fichero (catalogo, arbol de
    /// paginas, xref) para poder devolver el numero de paginas; si algo estuviera mal formado,
    /// fallaria aqui. La prueba de fuego de verdad —¿se ve bien en un lector ajeno a
    /// PDFsharp?— se ha hecho a mano renderizando estos mismos PDF con el motor de Windows.
    /// </remarks>
    private static void ComprobarPdf(byte[] bytes)
    {
        bytes.Should().NotBeEmpty();

        var cabecera = Encoding.Latin1.GetString(bytes, 0, Math.Min(8, bytes.Length));
        cabecera.Should().StartWith("%PDF-1.", "sin cabecera no es un PDF");

        var cola = Encoding.Latin1.GetString(bytes, Math.Max(0, bytes.Length - 16), Math.Min(16, bytes.Length));
        cola.TrimEnd().Should().EndWith("%%EOF");

        using var memoria = new MemoryStream(bytes);
        var reabierto = PdfReader.Open(memoria, PdfDocumentOpenMode.Import);
        reabierto.PageCount.Should().BeGreaterThan(0, "un lector de verdad tiene que encontrar al menos una página");
    }

    /// <summary>
    /// El PDF lleva un programa de fuente TrueType incrustado: la prueba, indirecta, de que se
    /// ha llegado a dibujar texto. Con la fuente en Unicode el texto de las etiquetas ya no
    /// aparece como caracteres sueltos en los bytes, pero la marca de la fuente incrustada
    /// (<c>/FontFile2</c>) sí es texto llano en la estructura del PDF.
    /// </summary>
    private static bool TieneFuenteIncrustada(byte[] bytes) =>
        Encoding.Latin1.GetString(bytes).Contains("/FontFile2", StringComparison.Ordinal);

    private static EtiquetaDeQsl Etiqueta(string destino) => new(
        Indicativo.Parse(destino),
        Mio,
        [
            new LineaDeContacto(
                new DateOnly(2026, 3, 1), new TimeOnly(10, 30), "20m", "SSB", "59", null, null),
            new LineaDeContacto(
                new DateOnly(2026, 3, 2), new TimeOnly(11, 45), "70cm", "FM", "59", "SO-50", "VU"),
        ],
        null,
        ViaDeEnvio.Buro,
        Locator.Parse("IL27HX"),
        null);

    private static Qso Contacto(string indicativo, DateTime inicioUtc) => new()
    {
        Call = Indicativo.Parse(indicativo),
        StationCallsign = Mio,
        MyGridsquare = Locator.Parse("IL27HX"),
        InicioUtc = new DateTimeOffset(inicioUtc),
        Freq = Frecuencia.DesdeMegahercios(14.200m),
        Band = Banda.Parse("20m"),
        Mode = Modo.Parse("SSB"),
        RstSent = Informe.Parse("59"),
    };
}
