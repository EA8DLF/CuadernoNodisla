using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nodisla.Cuaderno.Ui.Soporte;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// La ayuda integrada: el lector de Markdown, los capítulos incrustados (con sus capturas y sus
/// enlaces) y el modelo de la página, sin ventanas.
/// </summary>
public sealed class AyudaPruebas
{
    private static readonly LibroDeAyuda Libro = LibroDeAyuda.DelEnsamblado(typeof(VistaModeloAyuda).Assembly);

    // ── El lector de Markdown ────────────────────────────────────────────

    [Fact]
    public void Lee_titulos_parrafos_y_su_ancla_como_github()
    {
        var bloques = DocumentoMarkdown.Leer("# Primer uso\n\nUna línea\nque sigue.\n\n## Lo que hay que configurar una vez\n");

        bloques.Should().HaveCount(3);
        bloques[0].Should().BeOfType<TituloMd>().Which.Nivel.Should().Be(1);
        DocumentoMarkdown.TextoPlano(((ParrafoMd)bloques[1]).Trozos).Should().Be("Una línea que sigue.");
        ((TituloMd)bloques[2]).Ancla.Should().Be("lo-que-hay-que-configurar-una-vez");
        DocumentoMarkdown.TituloPrincipal(bloques).Should().Be("Primer uso");
    }

    [Fact]
    public void Lee_negrita_cursiva_codigo_y_enlaces()
    {
        var trozos = DocumentoMarkdown.LeerEnLinea("Pulse **Conectar**, *luego* `Ctrl` `1` y vea [la fonía](12-fonia.md#ptt).");

        trozos.Should().ContainSingle(t => t.Negrita && t.Texto == "Conectar");
        trozos.Should().ContainSingle(t => t.Cursiva && t.Texto == "luego");
        trozos.Where(t => t.Codigo).Select(t => t.Texto).Should().Equal("Ctrl", "1");
        trozos.Should().ContainSingle(t => t.Enlace == "12-fonia.md#ptt").Which.Texto.Should().Be("la fonía");
    }

    [Fact]
    public void Un_guion_bajo_dentro_de_una_palabra_no_es_cursiva()
    {
        var trozos = DocumentoMarkdown.LeerEnLinea("CUADERNO_SIMULADO y SCU_LAN10");
        trozos.Should().ContainSingle().Which.Cursiva.Should().BeFalse();
    }

    [Fact]
    public void Lee_listas_anidadas_con_lineas_de_continuacion()
    {
        var bloques = DocumentoMarkdown.Leer("""
            - **HAM**, a la izquierda:
              - **Banda**: hace lo mismo que la
                radio.
              - **Modo** sobre el VFO.
            - **CB**, a la derecha.

            1. Uno
            2. Dos
            """);

        var lista = bloques[0].Should().BeOfType<ListaMd>().Subject;
        lista.Numerada.Should().BeFalse();
        lista.Elementos.Should().HaveCount(2);
        var sub = lista.Elementos[0].Hijos.Should().ContainSingle().Which.Should().BeOfType<ListaMd>().Subject;
        sub.Elementos.Should().HaveCount(2);
        DocumentoMarkdown.TextoPlano(sub.Elementos[0].Trozos).Should().Be("Banda: hace lo mismo que la radio.");
        bloques[1].Should().BeOfType<ListaMd>().Which.Numerada.Should().BeTrue();
    }

    [Fact]
    public void Lee_tablas_citas_codigo_e_imagenes()
    {
        var bloques = DocumentoMarkdown.Leer("""
            | Tecla | Qué hace |
            |---|---|
            | `F1` | Abre la ayuda |
            | `Ctrl` `\|` | Nada |

            > **Ojo:** nada transmite solo.

            ```powershell
            dotnet build
            ```

            ![La cabina](../capturas/ayuda/operar.png)
            """);

        var tabla = bloques[0].Should().BeOfType<TablaMd>().Subject;
        tabla.Cabecera.Should().HaveCount(2);
        tabla.Filas.Should().HaveCount(2);
        bloques[1].Should().BeOfType<CitaMd>().Which.Bloques.Should().ContainSingle();
        bloques[2].Should().BeOfType<CodigoMd>().Which.Texto.Should().Be("dotnet build");
        var imagen = bloques[3].Should().BeOfType<ImagenMd>().Subject;
        imagen.Ruta.Should().Be("../capturas/ayuda/operar.png");
        imagen.Texto.Should().Be("La cabina");
    }

    [Fact]
    public void Los_comentarios_html_no_salen()
    {
        DocumentoMarkdown.Leer("Antes <!-- nota interna --> después").Should().ContainSingle()
            .Which.Should().BeOfType<ParrafoMd>()
            .Which.Trozos.Should().ContainSingle().Which.Texto.Should().Be("Antes  después");
    }

    // ── Los capítulos incrustados ────────────────────────────────────────

    [Fact]
    public void Van_incrustados_todos_los_capitulos_con_la_presentacion_y_el_primer_uso_delante()
    {
        Libro.Capitulos.Should().HaveCountGreaterThan(10);
        Libro.Capitulos[0].Clave.Should().Be("README");
        Libro.Capitulos[1].Clave.Should().Be("01-primer-uso");
        Libro.Capitulos.Should().OnlyHaveUniqueItems(c => c.Clave);
        Libro.Capitulos.Should().OnlyContain(c => c.Titulo.Length > 0 && c.Titulo != c.Clave);
    }

    [Fact]
    public void Cada_captura_que_cita_un_capitulo_va_incrustada()
    {
        var faltan = new List<string>();
        foreach (var capitulo in Libro.Capitulos)
        {
            foreach (var imagen in Todos(DocumentoMarkdown.Leer(capitulo.Texto)).OfType<ImagenMd>())
            {
                imagen.Ruta.Should().StartWith("../capturas/ayuda/", $"{capitulo.Clave} solo puede citar capturas de la ayuda");
                using var flujo = Libro.AbrirImagen(imagen.Ruta);
                if (flujo is null) faltan.Add($"{capitulo.Clave}: {imagen.Ruta}");
            }
        }

        faltan.Should().BeEmpty();
    }

    [Fact]
    public void Cada_enlace_entre_capitulos_lleva_a_un_capitulo_y_apartado_que_existen()
    {
        var rotos = new List<string>();
        foreach (var capitulo in Libro.Capitulos)
        {
            foreach (var enlace in Enlaces(DocumentoMarkdown.Leer(capitulo.Texto)))
            {
                if (enlace.StartsWith("http", StringComparison.Ordinal) || enlace.StartsWith("mailto:", StringComparison.Ordinal)) continue;

                var partes = enlace.Split('#', 2);
                var destino = partes[0].Length == 0 ? capitulo : Libro.Buscar(partes[0]);
                if (destino is null)
                {
                    rotos.Add($"{capitulo.Clave} → {enlace}");
                    continue;
                }

                if (partes.Length == 2 && !Todos(DocumentoMarkdown.Leer(destino.Texto)).OfType<TituloMd>().Any(t => t.Ancla == partes[1]))
                {
                    rotos.Add($"{capitulo.Clave} → {enlace} (no hay ese apartado)");
                }
            }
        }

        rotos.Should().BeEmpty();
    }

    [Fact]
    public void Ningun_capitulo_lleva_un_correo_que_no_sea_el_publico()
    {
        var correo = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}");
        foreach (var capitulo in Libro.Capitulos)
        {
            correo.Matches(capitulo.Texto).Select(m => m.Value)
                .Where(c => !c.EndsWith("@ejemplo.org", StringComparison.OrdinalIgnoreCase))
                .Should().OnlyContain(c => c == "nodisla@nodisla.org", $"en {capitulo.Clave}");
        }
    }

    // ── El modelo de la página ───────────────────────────────────────────

    [Fact]
    public void Abre_por_el_primer_uso_y_la_busqueda_filtra_sin_tildes_ni_mayusculas()
    {
        var ayuda = new VistaModeloAyuda(Libro, version: "0.3.0");
        ayuda.CapituloElegido!.Clave.Should().Be("01-primer-uso");

        ayuda.Busqueda = "FONIA";
        ayuda.CapitulosVisibles.Should().Contain(c => c.Clave == "12-fonia");
        ayuda.CapitulosVisibles.Should().OnlyContain(c => c.ParaBuscar.Contains("fonia"));
        ayuda.ResultadoDeLaBusqueda.Should().StartWith("Lo menciona");

        ayuda.Busqueda = "palabraquenoexisteenningunsitio";
        ayuda.CapitulosVisibles.Should().BeEmpty();
        ayuda.ResultadoDeLaBusqueda.Should().Be("Ningún capítulo lo menciona.");
    }

    [Fact]
    public void Un_enlace_a_otro_capitulo_lo_abre_y_pide_bajar_al_apartado()
    {
        var ayuda = new VistaModeloAyuda(Libro, version: "0.3.0");
        ayuda.SeguirEnlace("13-tarjeta-qsl.md#lo-que-hay-que-configurar-una-vez");

        ayuda.CapituloElegido!.Clave.Should().Be("13-tarjeta-qsl");
        ayuda.AnclaPedida.Should().Be("lo-que-hay-que-configurar-una-vez");
        ayuda.Vista.Should().Be(VistaDeLaAyuda.Capitulo);
    }

    [Fact]
    public void Los_enlaces_de_fuera_van_al_navegador_y_no_cambian_de_capitulo()
    {
        Uri? abierta = null;
        var ayuda = new VistaModeloAyuda(Libro, acciones: new AccionesDelSistema { AbrirEnNavegador = u => abierta = u }, version: "0.3.0");
        var antes = ayuda.CapituloElegido;

        ayuda.SeguirEnlace("https://github.com/EA8DLF/CuadernoNodisla");

        abierta.Should().Be(new Uri("https://github.com/EA8DLF/CuadernoNodisla"));
        ayuda.CapituloElegido.Should().BeSameAs(antes);
    }

    [Fact]
    public void Reportar_un_fallo_monta_un_formulario_nuevo_cada_vez()
    {
        var hechos = 0;
        var ayuda = new VistaModeloAyuda(Libro, nuevoReporte: () =>
        {
            hechos++;
            return new VistaModeloReportarFallo(() => "entorno", () => string.Empty);
        }, version: "0.3.0");

        ayuda.ReportarUnFallo();
        var primero = ayuda.ReporteDeFallo;
        ayuda.VolverAlCapitulo();
        ayuda.ReportarUnFallo();

        hechos.Should().Be(2);
        ayuda.ReporteDeFallo.Should().NotBeSameAs(primero);
        ayuda.Vista.Should().Be(VistaDeLaAyuda.ReportarFallo);
        ayuda.TituloDeLaVista.Should().Be("Reportar un fallo");
    }

    [Fact]
    public void Acerca_de_lleva_version_licencia_repositorio_y_el_correo_publico()
    {
        Uri? abierta = null;
        var ayuda = new VistaModeloAyuda(Libro, acciones: new AccionesDelSistema { AbrirEnNavegador = u => abierta = u }, version: "0.3.0");
        ayuda.VerAcercaDe();

        ayuda.Vista.Should().Be(VistaDeLaAyuda.AcercaDe);
        ayuda.Version.Should().Be("0.3.0");
        ayuda.TextoDeLicencia.Should().Contain("GPL-3.0");
        ayuda.Repositorio.Should().Be("https://github.com/EA8DLF/CuadernoNodisla");
        ayuda.Correo.Should().Be("nodisla@nodisla.org");

        ayuda.EscribirUnCorreo();
        abierta.Should().Be(new Uri("mailto:nodisla@nodisla.org"));
    }

    [Fact]
    public void La_ayuda_de_la_web_esta_al_dia_con_los_capitulos()
    {
        // docs/web-ayuda la genera herramientas/AyudaWeb; si un capitulo cambia y no se
        // regenera, la web contaria otra cosa que el programa.
        var raiz = BuscarRaiz();
        if (raiz is null) return;
        var web = Path.Combine(raiz, "docs", "web-ayuda");
        Directory.Exists(web).Should().BeTrue("la ayuda web va en docs/web-ayuda");
        foreach (var capitulo in Libro.Capitulos)
        {
            var nombre = capitulo.Clave == "README" ? "index.html" : capitulo.Clave + ".html";
            File.Exists(Path.Combine(web, nombre)).Should().BeTrue($"falta {nombre} en la ayuda web");
        }
    }

    private static string? BuscarRaiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CuadernoNodisla.sln"))) dir = dir.Parent;
        return dir?.FullName;
    }

    private static IEnumerable<BloqueMd> Todos(IEnumerable<BloqueMd> bloques)
    {
        foreach (var b in bloques)
        {
            yield return b;
            var hijos = b switch
            {
                CitaMd c => c.Bloques,
                ListaMd l => l.Elementos.SelectMany(e => e.Hijos),
                _ => [],
            };
            foreach (var h in Todos(hijos)) yield return h;
        }
    }

    private static IEnumerable<string> Enlaces(IEnumerable<BloqueMd> bloques)
    {
        foreach (var b in Todos(bloques))
        {
            var trozos = b switch
            {
                TituloMd t => t.Trozos,
                ParrafoMd p => p.Trozos,
                ListaMd l => l.Elementos.SelectMany(e => e.Trozos),
                TablaMd t => t.Cabecera.SelectMany(c => c).Concat(t.Filas.SelectMany(f => f.SelectMany(c => c))),
                _ => [],
            };
            foreach (var t in trozos)
            {
                if (t.Enlace is { } e) yield return e;
            }
        }
    }
}
