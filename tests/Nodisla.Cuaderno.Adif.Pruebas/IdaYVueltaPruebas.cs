using System.Text;
using FluentAssertions;
using Nodisla.Cuaderno.Adif;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Adif.Pruebas;

/// <summary>
/// La regla innegociable del proyecto: importar un ADIF y volver a exportarlo no puede perder
/// ni un solo dato. Se comprueba campo a campo sobre los respaldos reales del cuaderno.
/// </summary>
public class IdaYVueltaPruebas
{
    [Fact]
    public async Task El_respaldo_real_se_lee_entero()
    {
        if (RespaldosReales.MasReciente is not { } ruta) return;

        var lectura = await LeerFicheroAsync(ruta);
        var crudos = await Ayudas.CamposCrudosDeFicheroAsync(ruta);

        lectura.Qsos.Should().HaveCount(crudos.Count);
        lectura.Qsos.Should().HaveCountGreaterThan(1000, "el cuaderno de EA8DLF tiene casi dos mil contactos");
        lectura.Avisos.Should().NotContain(a => a.EsFatal);
        lectura.Avisos.Should().OnlyContain(
            a => a.Nivel == NivelDeAviso.Informativo,
            "un respaldo sano de Log4OM solo levanta avisos de tramite");
        lectura.ProgramaOrigen.Should().Be("LOG4OM2");
    }

    [Fact]
    public async Task Todos_los_respaldos_reales_sobreviven_a_la_ida_y_vuelta_en_adi()
    {
        if (!RespaldosReales.Hay) return;

        foreach (var ruta in RespaldosReales.Todos)
        {
            var lectura = await LeerFicheroAsync(ruta);
            var original = await Ayudas.CamposCrudosDeFicheroAsync(ruta);
            var exportado = await Ayudas.ExportarBytesAsync(lectura.Qsos);

            using var flujo = new MemoryStream(exportado);
            var devuelto = await Ayudas.CamposCrudosAsync(flujo);

            ComprobarSinPerdida(original, devuelto, Path.GetFileName(ruta));
        }
    }

    [Fact]
    public async Task El_respaldo_real_sobrevive_a_la_ida_y_vuelta_pasando_por_adx()
    {
        if (RespaldosReales.MasReciente is not { } ruta) return;

        var lectura = await LeerFicheroAsync(ruta);
        var original = await Ayudas.CamposCrudosDeFicheroAsync(ruta);

        var adx = await Ayudas.ExportarBytesAsync(lectura.Qsos, new OpcionesAdif { Adx = true });
        var desdeAdx = await Ayudas.LeerAsync(adx);
        desdeAdx.Qsos.Should().HaveCount(lectura.Qsos.Count);

        var adi = await Ayudas.ExportarBytesAsync(desdeAdx.Qsos);
        using var flujo = new MemoryStream(adi);
        var devuelto = await Ayudas.CamposCrudosAsync(flujo);

        // Por ADX se compara el valor y no el literal: el XML no declara longitudes, asi que un
        // rotulo de adorno detras del dato no tiene donde meterse sin volverse parte del dato.
        ComprobarSinPerdida(original, devuelto, "ADX", conLiteral: false);
    }

    [Fact]
    public async Task El_rotulo_de_adorno_del_condado_ni_ensucia_el_dato_ni_se_pierde()
    {
        if (RespaldosReales.MasReciente is not { } ruta) return;

        var crudos = await Ayudas.CamposCrudosDeFicheroAsync(ruta);
        var indice = crudos.FindIndex(r => r.Any(c =>
            c.Nombre == "CNTY" && c.Literal is not null && c.Literal.Contains("//", StringComparison.Ordinal)));
        indice.Should().BeGreaterThanOrEqualTo(0, "el respaldo real trae condados con rotulo de adorno");

        var campoOriginal = crudos[indice].Single(c => c.Nombre == "CNTY");
        campoOriginal.Valor.Should().NotContain("//");
        campoOriginal.Literal.Should().Contain("//");

        var lectura = await LeerFicheroAsync(ruta);
        var qso = lectura.Qsos[indice];

        // El dato que llega al cuaderno es el codigo limpio: es lo que cuenta para los diplomas.
        qso.Cnty.Should().Be(campoOriginal.Valor);
        qso.Cnty.Should().NotContain("//");

        // Y el fichero sale con el campo tal y como entro, longitud declarada incluida.
        var salida = Encoding.UTF8.GetString(await Ayudas.ExportarBytesAsync([qso]));
        var comoEntro = $"<CNTY:{Encoding.UTF8.GetByteCount(campoOriginal.Valor)}>{campoOriginal.Literal}";
        salida.Should().Contain(comoEntro);

        // Y leerlo otra vez devuelve exactamente el mismo reparto, sin ensuciarse por el camino.
        var vuelta = await Ayudas.LeerAsync(Encoding.UTF8.GetBytes(salida));
        vuelta.Qsos.Single().Cnty.Should().Be(campoOriginal.Valor);
    }

    [Fact]
    public async Task Todos_los_condados_con_rotulo_del_respaldo_real_vuelven_enteros()
    {
        if (RespaldosReales.MasReciente is not { } ruta) return;

        var crudos = await Ayudas.CamposCrudosDeFicheroAsync(ruta);
        var conRotulo = crudos.Count(r => r.Any(c => c.Nombre == "CNTY" && c.Literal is not null));
        conRotulo.Should().BeGreaterThan(0);

        var lectura = await LeerFicheroAsync(ruta);
        var salida = await Ayudas.ExportarBytesAsync(lectura.Qsos);
        using var flujo = new MemoryStream(salida);
        var devuelto = await Ayudas.CamposCrudosAsync(flujo);

        devuelto.Count(r => r.Any(c => c.Nombre == "CNTY" && c.Literal is not null))
            .Should().Be(conRotulo, "ningun rotulo se queda por el camino");
        lectura.Qsos.Should().NotContain(q => q.Cnty != null && q.Cnty.Contains("//"));
    }

    [Fact]
    public async Task Una_segunda_vuelta_da_exactamente_el_mismo_fichero()
    {
        if (RespaldosReales.MasReciente is not { } ruta) return;

        var primera = await LeerFicheroAsync(ruta);
        var texto1 = await Ayudas.ExportarBytesAsync(primera.Qsos);

        var segunda = await Ayudas.LeerAsync(texto1);
        var texto2 = await Ayudas.ExportarBytesAsync(segunda.Qsos);

        SinCabecera(texto1).Should().Be(SinCabecera(texto2));
    }

    [Fact]
    public async Task Los_acentos_sobreviven_con_la_longitud_contada_en_bytes()
    {
        const string adif =
            "<ADIF_VER:5>3.1.5 <EOH>\n"
            + "<CALL:6>EA8DLF <QSO_DATE:8>20260101 <TIME_ON:6>120000 <BAND:3>20m <MODE:3>SSB "
            + "<MY_CNTY:7>ESPAÑA <QTH:11>MOGÁN ÁÉ <EOR>\n";

        var lectura = await Ayudas.LeerAsync(adif);
        lectura.Qsos.Should().ContainSingle();
        lectura.Qsos[0].MyCnty.Should().Be("ESPAÑA");
        lectura.Qsos[0].Qth.Should().Be("MOGÁN ÁÉ");

        var exportado = await Ayudas.ExportarBytesAsync(lectura.Qsos);
        Encoding.UTF8.GetString(exportado).Should().Contain("<MY_CNTY:7>ESPAÑA");

        var vuelta = await Ayudas.LeerAsync(exportado);
        vuelta.Qsos[0].MyCnty.Should().Be("ESPAÑA");
        vuelta.Qsos[0].Qth.Should().Be("MOGÁN ÁÉ");
    }

    private static async Task<LecturaAdif> LeerFicheroAsync(string ruta)
    {
        await using var flujo = File.OpenRead(ruta);
        return await new LectorAdif().LeerAsync(flujo);
    }

    /// <summary>
    /// Comprueba que cada campo del fichero original sigue estando y con el mismo texto. Se
    /// compara el literal, no el valor interpretado: si el fichero traia un rotulo de adorno
    /// detras del dato, el rotulo tiene que volver tambien.
    /// </summary>
    private static void ComprobarSinPerdida(
        List<List<CampoAdif>> original,
        List<List<CampoAdif>> devuelto,
        string etiqueta,
        bool conLiteral = true)
    {
        devuelto.Should().HaveCount(original.Count, "el fichero {0} tiene esos registros", etiqueta);

        static string Texto(CampoAdif c, bool conLiteral) => conLiteral ? c.TextoParaEscribir : c.Valor;

        for (var i = 0; i < original.Count; i++)
        {
            var salida = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in devuelto[i]) salida[c.Nombre] = Texto(c, conLiteral);

            foreach (var campo in original[i])
            {
                salida.Should().ContainKey(
                    campo.Nombre,
                    "el registro {0} de {1} traia el campo {2}", i + 1, etiqueta, campo.Nombre);
                salida[campo.Nombre].Should().Be(
                    Texto(campo, conLiteral),
                    "el campo {0} del registro {1} de {2} ha de volver igual", campo.Nombre, i + 1, etiqueta);
            }
        }
    }

    /// <summary>Quita la cabecera, que lleva la marca de tiempo de la exportacion.</summary>
    private static string SinCabecera(byte[] adif)
    {
        var texto = Encoding.UTF8.GetString(adif);
        var i = texto.IndexOf("<EOH>", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? texto : texto[(i + 5)..];
    }
}
