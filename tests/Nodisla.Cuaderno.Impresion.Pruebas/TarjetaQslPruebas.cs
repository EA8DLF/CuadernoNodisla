using FluentAssertions;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Impresion.Qsl;
using PdfSharp.Pdf.IO;

namespace Nodisla.Cuaderno.Impresion.Pruebas;

/// <summary>La tarjeta QSL propia: variables, plantillas en disco, PDF y marca de envio.</summary>
public sealed class TarjetaQslPruebas : IDisposable
{
    // PNG de 1 x 1 pixel, blanco.
    private static readonly byte[] PngDeUnPixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4//8/AAX+Av4N70a4AAAAAElFTkSuQmCC");

    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "cuaderno-qsl-" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_carpeta)) Directory.Delete(_carpeta, recursive: true);
    }

    [Fact]
    public void Las_variables_salen_del_contacto_y_lo_que_falta_del_perfil()
    {
        var qso = Contacto();
        var yo = new DatosDeMiEstacion("EA8XXX", "IL18", "José", "Santa Cruz de Tenerife");

        var v = VariablesDeQsl.Para(qso, yo);

        v["miindicativo"].Should().Be("EA8DLF", "manda el indicativo con el que se hizo el contacto");
        v["milocalizador"].Should().Be("IL28FK");
        v["minombre"].Should().Be("José", "el contacto no lo trae y se coge del perfil");
        v["indicativo"].Should().Be("EA1ABC");
        v["fecha"].Should().Be("2026-09-29");
        v["hora"].Should().Be("14:32");
        v["banda"].Should().Be("20m");
        v["frecuencia"].Should().Be("14.074");
        v["modo"].Should().Be("FT8");
        v["rst"].Should().Be("-10");
        v["pse"].Should().Be("PSE QSL");
    }

    [Fact]
    public void Con_su_tarjeta_recibida_dice_TNX()
    {
        var qso = Contacto();
        qso.Confirmaciones.Add(new QsoConfirmacion { Medio = MedioDeConfirmacion.Papel, Recibido = EstadoDeConfirmacion.Confirmado });
        VariablesDeQsl.Para(qso, null)["pse"].Should().Be("TNX QSL");
    }

    [Theory]
    [InlineData("To Radio {indicativo}", "To Radio EA1ABC")]
    [InlineData("{INDICATIVO} {Banda}", "EA1ABC 20m")]
    [InlineData("{noexiste} queda", "{noexiste} queda")]
    [InlineData("llave sin cerrar {indicativo", "llave sin cerrar {indicativo")]
    [InlineData("", "")]
    public void Sustituir(string plantilla, string esperado) =>
        VariablesDeQsl.Sustituir(plantilla, VariablesDeQsl.Para(Contacto(), null)).Should().Be(esperado);

    [Fact]
    public void Sin_nada_guardado_se_ofrece_la_plantilla_del_programa_sin_escribir_en_disco()
    {
        var almacen = new AlmacenDeDisenosDeQsl(_carpeta);

        almacen.Listar().Should().ContainSingle().Which.Id.Should().Be("nodisla");
        almacen.PorOmision().Campos.Should().Contain(c => c.Texto.Contains("{miindicativo}"));
        Directory.Exists(almacen.Carpeta).Should().BeFalse();
    }

    [Fact]
    public void Guardar_listar_poner_por_omision_y_borrar()
    {
        var almacen = new AlmacenDeDisenosDeQsl(_carpeta);
        var mia = DisenoDeQsl.PorOmision();
        mia.Id = "mia";
        mia.Nombre = "Mi foto del Teide";
        mia.Vertical = true;
        mia.Campos[0].XMm = 45;

        almacen.Guardar(mia);
        almacen.PonerPorOmision("mia");

        var leida = almacen.PorOmision();
        leida.Nombre.Should().Be("Mi foto del Teide");
        leida.Vertical.Should().BeTrue();
        leida.AnchoMm.Should().Be(90);
        leida.AltoMm.Should().Be(140);
        leida.Campos[0].XMm.Should().Be(45);
        almacen.Listar().Should().HaveCount(2);

        almacen.Borrar(leida);
        almacen.IdPorOmision().Should().Be("nodisla");
        almacen.Listar().Should().ContainSingle();
    }

    [Fact]
    public void La_foto_de_fondo_se_copia_a_la_carpeta_de_datos()
    {
        var almacen = new AlmacenDeDisenosDeQsl(_carpeta);
        Directory.CreateDirectory(_carpeta);
        var foto = Path.Combine(_carpeta, "teide.png");
        File.WriteAllBytes(foto, PngDeUnPixel);
        var diseno = DisenoDeQsl.PorOmision();

        almacen.PonerFondo(diseno, foto);
        File.Delete(foto);

        almacen.RutaDelFondo(diseno).Should().NotBeNull("borrar la foto original no deja la tarjeta sin fondo");
        var ajena = () => almacen.PonerFondo(diseno, Path.Combine(_carpeta, "virus.exe"));
        ajena.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Un_JSON_roto_no_tumba_la_lista()
    {
        var almacen = new AlmacenDeDisenosDeQsl(_carpeta);
        Directory.CreateDirectory(almacen.Carpeta);
        File.WriteAllText(Path.Combine(almacen.Carpeta, "rota.json"), "{ esto no es json");

        almacen.Listar().Should().ContainSingle().Which.Id.Should().Be("nodisla");
    }

    [Fact]
    public void Un_identificador_con_ruta_no_se_acepta()
    {
        var almacen = new AlmacenDeDisenosDeQsl(_carpeta);
        var mala = DisenoDeQsl.PorOmision();
        mala.Id = "..\\..\\fuera";
        var guardar = () => almacen.Guardar(mala);
        guardar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void El_PDF_pone_tres_tarjetas_apaisadas_por_A4_o_una_por_pagina()
    {
        var cinco = Enumerable.Repeat(PngDeUnPixel, 5).ToList();

        PdfDeTarjetasQsl.Reparto(140, 90).Should().Be((1, 3));
        PdfDeTarjetasQsl.Reparto(90, 140).Should().Be((2, 1));

        var a4 = PdfDeTarjetasQsl.EnHojaA4(cinco, 140, 90);
        a4.Paginas.Should().Be(2);
        a4.TipoDeMedio.Should().Be("application/pdf");

        var imprenta = PdfDeTarjetasQsl.UnaPorPagina(cinco, 140, 90);
        imprenta.Paginas.Should().Be(5);
        using var leido = PdfReader.Open(new MemoryStream(imprenta.Bytes), PdfDocumentOpenMode.Import);
        leido.Pages[0].Width.Millimeter.Should().BeApproximately(140, 0.1);
        leido.Pages[0].Height.Millimeter.Should().BeApproximately(90, 0.1);
    }

    [Fact]
    public void Marcar_deja_QSL_SENT_Y_con_fecha_y_via_electronica_sin_tocar_lo_recibido()
    {
        var qso = Contacto();
        qso.Confirmaciones.Add(new QsoConfirmacion { Medio = MedioDeConfirmacion.Papel, Recibido = EstadoDeConfirmacion.Solicitado, Via = ViaDeEnvio.Buro });
        var cuando = new DateTimeOffset(2026, 9, 29, 18, 0, 0, TimeSpan.FromHours(1));

        MarcaDeQslEnviada.YaEnviada(qso).Should().BeFalse();
        MarcaDeQslEnviada.Marcar(qso, cuando);

        var tarjeta = qso.Confirmaciones.Should().ContainSingle().Which;
        tarjeta.Enviado.Should().Be(EstadoDeConfirmacion.Confirmado);
        tarjeta.EnviadoUtc.Should().Be(cuando.ToUniversalTime());
        tarjeta.Via.Should().Be(ViaDeEnvio.Electronico);
        tarjeta.Recibido.Should().Be(EstadoDeConfirmacion.Solicitado);
        MarcaDeQslEnviada.YaEnviada(qso).Should().BeTrue();
    }

    private static Qso Contacto() => new()
    {
        Call = Indicativo.Parse("EA1ABC"),
        StationCallsign = Indicativo.Parse("EA8DLF"),
        MyGridsquare = Locator.Parse("IL28FK"),
        InicioUtc = new DateTimeOffset(2026, 9, 29, 14, 32, 0, TimeSpan.Zero),
        Freq = Frecuencia.DesdeMegahercios(14.074m),
        Band = Banda.Parse("20m"),
        Mode = Modo.Parse("FT8"),
        RstSent = Informe.Parse("-10"),
    };
}
