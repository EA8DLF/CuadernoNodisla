using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nodisla.Cuaderno.Impresion.Diplomas;
using Nodisla.Cuaderno.Impresion.Qsl;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Nodisla.Cuaderno.Impresion.Pruebas;

/// <summary>
/// El diseñador de diplomas por dentro: variables, plantillas de fabrica, almacen de plantillas
/// (el mismo motor que las QSL) y el PDF con capa de texto. Datos ficticios.
/// </summary>
public sealed class DiplomasPruebas : IDisposable
{
    // PNG de 1 x 1 pixel, blanco.
    private static readonly byte[] PngDeUnPixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4//8/AAX+Av4N70a4AAAAAElFTkSuQmCC");

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-diplomas-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    // ── Rellenado de campos ────────────────────────────────────────────

    [Fact]
    public void Las_variables_del_diploma_se_rellenan_solas()
    {
        var diseno = PlantillasDeDiplomaDeFabrica.Clasico();
        diseno.PrefijoDeNumero = "EA8-";
        diseno.CifrasDelNumero = 4;
        diseno.NombreDelGestor = "Ana Gestora";
        var datos = Datos();
        datos.Numero = 7;
        var yo = new DatosDeMiEstacion("EA8ZZZ", "IL18AA", "Operador Ficticio", "Isla Ficticia");

        var v = VariablesDeDiploma.Para(datos, diseno, yo);

        v["indicativo"].Should().Be("EA1TST", "se normaliza a mayusculas");
        v["nombre"].Should().Be("Pepa Prueba");
        v["diploma"].Should().Be("Diploma de las Islas");
        v["categoria"].Should().Be("Oro");
        v["numero"].Should().Be("EA8-0007");
        v["serie"].Should().Be("NODISLA");
        v["fecha"].Should().Be("30 de septiembre de 2026");
        v["fechacorta"].Should().Be("30/09/2026");
        v["fechaiso"].Should().Be("2026-09-30");
        v["referencias"].Should().Be("2", "se cuentan las referencias distintas");
        v["qsos"].Should().Be("3");
        v["primerqso"].Should().Be("2026-01-02");
        v["ultimoqso"].Should().Be("2026-03-04");
        v["bandas"].Should().Be("20m, 40m");
        v["modos"].Should().Be("SSB, FT8");
        v["gestor"].Should().Be("Ana Gestora");
        v["miindicativo"].Should().Be("EA8ZZZ");

        diseno.NombreDelGestor = string.Empty;
        datos.Entidad = "Club Ficticio";
        VariablesDeDiploma.Para(datos, diseno, yo)["gestor"].Should().Be("Club Ficticio", "sin gestor en la plantilla firma la entidad");

        VariablesDeDiploma.Sustituir("Nº {numero} a {INDICATIVO} {raro}", v).Should().Be("Nº EA8-0007 a EA1TST {raro}");
        VariablesDeDiploma.DeFila(datos.Filas[0], 1)["fecha"].Should().Be("2026-01-02");
    }

    [Fact]
    public void Sin_numero_el_campo_numero_queda_vacio_y_los_recuentos_explicitos_mandan()
    {
        var datos = Datos();
        datos.Referencias = 25;
        datos.Qsos = 40;
        var v = VariablesDeDiploma.Para(datos, PlantillasDeDiplomaDeFabrica.Sobrio(), null);

        v["numero"].Should().BeEmpty();
        v["referencias"].Should().Be("25");
        v["qsos"].Should().Be("40");
    }

    // ── Plantillas de fabrica ──────────────────────────────────────────

    [Fact]
    public void Las_plantillas_de_fabrica_caben_en_su_papel_y_solo_usan_variables_que_existen()
    {
        var todas = PlantillasDeDiplomaDeFabrica.Todas();
        todas.Should().HaveCountGreaterThanOrEqualTo(4);
        todas.Select(d => d.Id).Should().OnlyHaveUniqueItems();
        todas.Should().Contain(d => d.Papel == PapelDeDiploma.Carta).And.Contain(d => d.Vertical).And.Contain(d => !d.Vertical);

        var conocidas = VariablesDeDiploma.Conocidas.Select(v => v.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deFila = VariablesDeDiploma.DeLaFila.Select(v => v.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var d in todas)
        {
            d.Imagenes().Should().BeEmpty($"«{d.Nombre}» no trae logos de nadie");
            foreach (var c in d.Campos)
            {
                c.XMm.Should().BeInRange(0, d.AnchoMm, $"{d.Nombre} · {c.Nombre}");
                c.YMm.Should().BeInRange(0, d.AltoMm, $"{d.Nombre} · {c.Nombre}");
                Variables(c.Texto).Should().BeSubsetOf(conocidas, $"{d.Nombre} · {c.Nombre}");
            }

            foreach (var i in d.ImagenesColocadas)
            {
                (i.XMm + i.AnchoMm).Should().BeLessThanOrEqualTo(d.AnchoMm);
                (i.YMm + i.AltoMm).Should().BeLessThanOrEqualTo(d.AltoMm);
            }

            (d.Tabla.XMm + d.Tabla.AnchoMm).Should().BeLessThanOrEqualTo(d.AnchoMm);
            (d.Tabla.YMm + d.Tabla.AltoMm).Should().BeLessThanOrEqualTo(d.AltoMm);
            foreach (var col in d.Tabla.Columnas) Variables(col.Texto).Should().BeSubsetOf(deFila);
            d.Campos.Should().Contain(c => c.Texto.Contains("{indicativo}"));
            d.Campos.Should().Contain(c => c.Texto.Contains("{numero}"));
        }
    }

    [Theory]
    [InlineData(PapelDeDiploma.A4, false, 297, 210)]
    [InlineData(PapelDeDiploma.A4, true, 210, 297)]
    [InlineData(PapelDeDiploma.Carta, false, 279.4, 215.9)]
    [InlineData(PapelDeDiploma.Carta, true, 215.9, 279.4)]
    public void Papel_y_orientacion_dan_su_tamano_y_recolocan_en_proporcion(PapelDeDiploma papel, bool vertical, double ancho, double alto)
    {
        var d = PlantillasDeDiplomaDeFabrica.Clasico();
        var indicativo = d.Campos.Single(c => c.Nombre == "Indicativo");
        var xRelativa = indicativo.XMm / d.AnchoMm;

        d.CambiarFormato(papel, vertical);

        d.AnchoMm.Should().BeApproximately(ancho, 0.01);
        d.AltoMm.Should().BeApproximately(alto, 0.01);
        (indicativo.XMm / d.AnchoMm).Should().BeApproximately(xRelativa, 1e-9, "sigue centrado");
    }

    // ── Plantillas de ida y vuelta ─────────────────────────────────────

    [Fact]
    public void Sin_nada_guardado_se_ofrecen_las_de_fabrica_sin_tocar_el_disco()
    {
        var almacen = new AlmacenDeDisenosDeDiploma(_carpeta);

        almacen.Listar().Should().HaveCount(PlantillasDeDiplomaDeFabrica.Todas().Count);
        almacen.IdPorOmision().Should().Be("nodisla-clasico");
        Directory.Exists(almacen.Carpeta).Should().BeFalse();
    }

    [Fact]
    public void Guardar_y_leer_conserva_toda_la_plantilla()
    {
        var almacen = new AlmacenDeDisenosDeDiploma(_carpeta);
        var d = PlantillasDeDiplomaDeFabrica.Atlantico();
        d.Id = "club";
        d.Nombre = "Diploma del club";
        d.Serie = "CLUB";
        d.PrefijoDeNumero = "2026/";
        d.Marco.Estilo = EstiloDeMarco.Escuadras;
        d.Tabla.Bloques = 2;
        d.Tabla.Columnas.Add(new ColumnaDeTabla { Titulo = "RST", Texto = "{rst}", Alineacion = AlineacionDeCampo.Centro });
        d.Campos[0].AnchoMaximoMm = 120;
        d.CambiarFormato(PapelDeDiploma.Carta, vertical: true);

        almacen.Guardar(d);
        almacen.PonerPorOmision("club");
        var leida = almacen.PorOmision();

        leida.Should().BeEquivalentTo(d, o => o.Excluding(x => x.AnchoMm).Excluding(x => x.AltoMm));
        leida.AnchoMm.Should().BeApproximately(215.9, 0.01);
        almacen.Listar().Should().HaveCount(PlantillasDeDiplomaDeFabrica.Todas().Count + 1);

        almacen.Borrar(leida);
        almacen.IdPorOmision().Should().Be("nodisla-clasico");
    }

    [Fact]
    public void Duplicar_copia_las_imagenes_con_otro_nombre()
    {
        var almacen = new AlmacenDeDisenosDeDiploma(_carpeta);
        var d = PlantillasDeDiplomaDeFabrica.Clasico();
        var logo = Foto("logo.png");
        almacen.PonerImagen(d, d.ImagenesColocadas[0], logo);
        almacen.PonerFondo(d, Foto("papel.png"));
        almacen.Guardar(d);

        var copia = almacen.Duplicar(d, "Copia");

        copia.Id.Should().NotBe(d.Id);
        copia.Nombre.Should().Be("Copia");
        copia.ImagenesColocadas[0].Fichero.Should().NotBe(d.ImagenesColocadas[0].Fichero).And.StartWith(copia.Id + "-logo-");
        almacen.RutaDeImagen(copia.ImagenesColocadas[0].Fichero).Should().NotBeNull();
        almacen.RutaDeImagen(copia.ImagenDeFondo).Should().NotBeNull();
        copia.Campos.Should().NotBeSameAs(d.Campos);
        copia.Campos[0].Should().NotBeSameAs(d.Campos[0]);
    }

    [Fact]
    public void Exportar_e_importar_lleva_la_plantilla_y_sus_imagenes_en_un_solo_fichero()
    {
        var origen = new AlmacenDeDisenosDeDiploma(Path.Combine(_carpeta, "equipo1"));
        var d = PlantillasDeDiplomaDeFabrica.Sobrio();
        d.Id = "mia";
        d.Nombre = "Mi certificado";
        origen.PonerImagen(d, d.ImagenesColocadas.Single(i => i.Uso == UsoDeImagen.Firma), Foto("firma.png"));
        origen.Guardar(d);
        var zip = Path.Combine(_carpeta, "mi-certificado" + AlmacenDeDisenosDeDiploma.ExtensionExportada);

        origen.Exportar(d, zip);
        var destino = new AlmacenDeDisenosDeDiploma(Path.Combine(_carpeta, "equipo2"));
        var importada = destino.Importar(zip);

        importada.Id.Should().NotBe("mia", "se guarda con un identificador nuevo para no pisar nada");
        importada.Nombre.Should().Be("Mi certificado");
        importada.Campos.Should().BeEquivalentTo(d.Campos);
        var firma = importada.ImagenesColocadas.Single(i => i.Uso == UsoDeImagen.Firma);
        destino.RutaDeImagen(firma.Fichero).Should().NotBeNull("la firma viaja dentro del fichero");
        destino.Listar().Should().Contain(p => p.Id == importada.Id);

        var roto = Path.Combine(_carpeta, "roto.zip");
        using (var z = ZipFile.Open(roto, ZipArchiveMode.Create)) z.CreateEntry("otra-cosa.txt");
        var importarRoto = () => destino.Importar(roto);
        importarRoto.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Borrar_una_de_fabrica_sobrescrita_la_devuelve_a_su_estado_original()
    {
        var almacen = new AlmacenDeDisenosDeDiploma(_carpeta);
        var d = almacen.Obtener("nodisla-volcan")!;
        d.Nombre = "Tocada";
        almacen.Guardar(d);
        almacen.Obtener("nodisla-volcan")!.Nombre.Should().Be("Tocada");

        almacen.Borrar(d);

        almacen.Obtener("nodisla-volcan")!.Nombre.Should().Be(PlantillasDeDiplomaDeFabrica.Volcan().Nombre);
    }

    [Fact]
    public void La_QSL_tambien_se_exporta_e_importa_con_el_motor_comun()
    {
        var almacen = new AlmacenDeDisenosDeQsl(_carpeta);
        var d = DisenoDeQsl.PorOmision();
        d.Id = "mia";
        almacen.PonerFondo(d, Foto("teide.png"));
        almacen.Guardar(d);
        var zip = Path.Combine(_carpeta, "qsl.zip");

        almacen.Exportar(d, zip);
        var importada = new AlmacenDeDisenosDeQsl(Path.Combine(_carpeta, "otro")).Importar(zip);

        importada.Campos.Should().HaveCount(d.Campos.Count);
        importada.ImagenDeFondo.Should().StartWith(importada.Id + "-fondo-");
    }

    // ── PDF ────────────────────────────────────────────────────────────

    [Fact]
    public void El_PDF_de_paginas_lleva_el_tamano_de_cada_hoja_y_el_texto_buscable()
    {
        var paginas = new[]
        {
            new PaginaDeImagen(PngDeUnPixel, 297, 210, [new TextoDePagina("EA1TST", 100, 80, 40), new TextoDePagina("Diploma de las Islas\nCategoría Oro", 60, 50, 18)]),
            new PaginaDeImagen(PngDeUnPixel, 297, 210, [new TextoDePagina("ED8-001  EA8AAA  2026-01-02", 20, 20, 8)]),
        };

        var pdf = PdfDeTarjetasQsl.Paginas(paginas, "diploma.pdf", "Diploma de las Islas nº 0001 — EA1TST", "Diploma", "diploma, EA1TST");

        pdf.Paginas.Should().Be(2);
        pdf.NombreSugerido.Should().Be("diploma.pdf");
        using var leido = PdfReader.Open(new MemoryStream(pdf.Bytes), PdfDocumentOpenMode.Import);
        leido.PageCount.Should().Be(2);
        leido.Pages[0].Width.Millimeter.Should().BeApproximately(297, 0.1);
        leido.Pages[0].Height.Millimeter.Should().BeApproximately(210, 0.1);
        leido.Info.Title.Should().Be("Diploma de las Islas nº 0001 — EA1TST");
        TextoDe(pdf.Bytes).Should().Contain("EA1TST").And.Contain("Categor").And.Contain("ED8-001");
    }

    /// <summary>El texto de las ordenes de pintar de un PDF sin comprimir.</summary>
    /// <param name="pdf">El PDF.</param>
    /// <returns>Lo que se pinta con Tj, en claro.</returns>
    internal static string TextoDe(byte[] pdf)
    {
        // WinAnsi: las cadenas van como (texto) o como <hex>; se aceptan las dos.
        var bruto = Encoding.Latin1.GetString(pdf);
        var salida = new StringBuilder();
        foreach (Match m in Regex.Matches(bruto, @"\((?<t>(?:\\.|[^\\)])*)\)\s*Tj|<(?<h>[0-9A-Fa-f]+)>\s*Tj"))
        {
            if (m.Groups["t"].Success) salida.Append(m.Groups["t"].Value.Replace("\\(", "(").Replace("\\)", ")"));
            else salida.Append(Encoding.Latin1.GetString(Convert.FromHexString(m.Groups["h"].Value)));
            salida.Append('\n');
        }

        return salida.ToString();
    }

    private static IEnumerable<string> Variables(string texto) =>
        Regex.Matches(texto, @"\{([^{}]+)\}").Select(m => m.Groups[1].Value.Trim());

    private static DatosDeDiploma Datos() => new()
    {
        Indicativo = "ea1tst",
        Nombre = "Pepa Prueba",
        NombreDelDiploma = "Diploma de las Islas",
        Categoria = "Oro",
        Fecha = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero),
        Filas =
        [
            new() { Referencia = "ISL-01", Indicativo = "EA8AAA", FechaUtc = new DateTimeOffset(2026, 1, 2, 10, 0, 0, TimeSpan.Zero), Banda = "20m", Modo = "SSB" },
            new() { Referencia = "ISL-02", Indicativo = "EA8BBB", FechaUtc = new DateTimeOffset(2026, 2, 3, 10, 0, 0, TimeSpan.Zero), Banda = "40m", Modo = "FT8" },
            new() { Referencia = "isl-01", Indicativo = "EA8CCC", FechaUtc = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero), Banda = "20m", Modo = "FT8" },
        ],
    };

    private string Foto(string nombre)
    {
        Directory.CreateDirectory(_carpeta);
        var ruta = Path.Combine(_carpeta, nombre);
        File.WriteAllBytes(ruta, PngDeUnPixel);
        return ruta;
    }
}
