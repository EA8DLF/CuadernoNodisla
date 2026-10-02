using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Nodisla.Cuaderno.Idiomas;
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
        foreach (var capitulo in Textos.Idiomas.SelectMany(i => Libro.CapitulosEn(i.Codigo)))
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
        EnlacesRotos(LibroDeAyuda.IdiomaOriginal).Should().BeEmpty();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("de")]
    public void En_cada_idioma_cada_enlace_lleva_a_un_capitulo_y_apartado_que_existen(string idioma)
    {
        // Los capitulos traducidos enlazan a los que no lo estan con sus anclas espanolas, y los
        // espanoles (sin traducir) a los traducidos tambien: el libro las lleva al mismo apartado.
        EnlacesRotos(idioma).Should().BeEmpty();
    }

    [Fact]
    public void Hay_capitulos_traducidos_al_ingles_empezando_por_la_presentacion_y_el_primer_uso()
    {
        Libro.TraducidosA("en").Should().Contain(["README", "01-primer-uso"]);
        Libro.IdiomasTraducidos.Should().OnlyContain(i => i != LibroDeAyuda.IdiomaOriginal && Textos.Idiomas.Any(x => x.Codigo == i));
    }

    [Fact]
    public void Cada_traduccion_conserva_los_titulos_del_original_en_el_mismo_orden_y_nivel()
    {
        // Es lo que permite llevar un ancla espanola al mismo apartado del capitulo traducido.
        foreach (var idioma in Libro.IdiomasTraducidos)
        {
            foreach (var clave in Libro.TraducidosA(idioma))
            {
                var original = Libro.Buscar(clave, LibroDeAyuda.IdiomaOriginal)!;
                var traducido = Libro.Buscar(clave, idioma)!;
                traducido.Idioma.Should().Be(idioma);
                traducido.SinTraducir.Should().BeFalse();
                Niveles(traducido).Should().Equal(Niveles(original), $"{idioma}/{clave} tiene que tener los mismos titulos que el original");
            }
        }

        static IEnumerable<int> Niveles(CapituloDeAyuda c) => DocumentoMarkdown.Leer(c.Texto).OfType<TituloMd>().Select(t => t.Nivel);
    }

    [Fact]
    public void El_aviso_de_sin_traducir_va_debajo_del_titulo_y_no_lo_cambia()
    {
        var con = LibroDeAyuda.ConAviso("# Operar\n\nTexto.\n", "Aviso.");
        var bloques = DocumentoMarkdown.Leer(con);
        DocumentoMarkdown.TituloPrincipal(bloques).Should().Be("Operar");
        bloques[1].Should().BeOfType<CitaMd>();
        con.Should().EndWith("Texto.\n");
    }

    private static List<string> EnlacesRotos(string idioma)
    {
        var rotos = new List<string>();
        foreach (var capitulo in Libro.CapitulosEn(idioma))
        {
            foreach (var enlace in Enlaces(DocumentoMarkdown.Leer(capitulo.Texto)))
            {
                if (enlace.StartsWith("http", StringComparison.Ordinal) || enlace.StartsWith("mailto:", StringComparison.Ordinal)) continue;

                var partes = enlace.Split('#', 2);
                var destino = partes[0].Length == 0 ? capitulo : Libro.Buscar(partes[0], idioma);
                if (destino is null)
                {
                    rotos.Add($"{idioma}/{capitulo.Clave} → {enlace}");
                    continue;
                }

                var ancla = partes.Length == 2 ? Libro.Ancla(destino, partes[1]) : null;
                if (ancla is not null && !Todos(DocumentoMarkdown.Leer(destino.Texto)).OfType<TituloMd>().Any(t => t.Ancla == ancla))
                {
                    rotos.Add($"{idioma}/{capitulo.Clave} → {enlace} (no hay ese apartado)");
                }
            }
        }

        return rotos;
    }

    [Fact]
    public void Ningun_capitulo_lleva_un_correo_que_no_sea_el_publico()
    {
        var correo = new Regex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}");
        foreach (var capitulo in Textos.Idiomas.SelectMany(i => Libro.CapitulosEn(i.Codigo)))
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

/// <summary>
/// La ayuda en otro idioma: el capitulo traducido si lo hay, el espanol con aviso si no, y la
/// recarga en caliente. Cambian el idioma de todo el programa, asi que van en la coleccion que
/// corre sola y lo dejan en espanol al acabar.
/// </summary>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed class AyudaEnOtrosIdiomasPruebas
{
    private static readonly LibroDeAyuda Libro = LibroDeAyuda.DelEnsamblado(typeof(VistaModeloAyuda).Assembly);

    [Fact]
    public void En_ingles_se_carga_el_capitulo_ingles()
    {
        try
        {
            Textos.Cambiar(new CultureInfo("en-GB", useUserOverride: false));
            var ayuda = new VistaModeloAyuda(Libro, version: "0.3.0");

            var capitulo = ayuda.CapituloElegido!;
            capitulo.Clave.Should().Be("01-primer-uso");
            capitulo.Idioma.Should().Be("en");
            capitulo.SinTraducir.Should().BeFalse();
            capitulo.Titulo.Should().Be("Getting started, step by step");
            capitulo.Texto.Should().NotContain(Textos.T("Ayuda.CapituloSinTraducir"));
            ayuda.CapitulosVisibles[0].Titulo.Should().Be("Cuaderno NODISLA Help");

            // Uno sin traducir sale en espanol, con el aviso en ingles al principio.
            var operar = Libro.Buscar("02-operar")!;
            operar.SinTraducir.Should().BeTrue();
            operar.Texto.Should().Contain("This chapter has not been translated into English yet");
        }
        finally
        {
            Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));
        }
    }

    [Fact]
    public void En_aleman_sin_traduccion_sale_el_espanol_con_el_aviso()
    {
        try
        {
            Textos.Cambiar(new CultureInfo("de-DE", useUserOverride: false));
            var ayuda = new VistaModeloAyuda(Libro, version: "0.3.0");

            var capitulo = ayuda.CapituloElegido!;
            capitulo.Clave.Should().Be("01-primer-uso");
            capitulo.SinTraducir.Should().BeTrue();
            capitulo.Idioma.Should().Be("es");
            capitulo.Titulo.Should().Be("Primer uso, paso a paso");
            capitulo.Texto.Should().Contain(Textos.T("Ayuda.CapituloSinTraducir"))
                .And.Contain("Dieses Kapitel ist noch nicht ins Deutsche übersetzt");
            ayuda.CapitulosVisibles.Should().OnlyContain(c => c.SinTraducir);
        }
        finally
        {
            Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));
        }
    }

    [Fact]
    public void En_espanol_no_hay_aviso()
    {
        Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));
        var aviso = Textos.T("Ayuda.CapituloSinTraducir");

        Libro.Capitulos.Should().OnlyContain(c => !c.SinTraducir && c.Idioma == "es");
        Libro.Capitulos.Should().OnlyContain(c => !c.Texto.Contains(aviso, StringComparison.Ordinal));
        new VistaModeloAyuda(Libro, version: "0.3.0").CapituloElegido!.Titulo.Should().Be("Primer uso, paso a paso");
    }

    [Fact]
    public void Al_cambiar_de_idioma_la_ayuda_se_recarga_y_conserva_el_capitulo_abierto()
    {
        try
        {
            Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));
            var ayuda = new VistaModeloAyuda(Libro, version: "0.3.0");
            ayuda.AbrirCapitulo("README").Should().BeTrue();

            Textos.Cambiar(new CultureInfo("en-GB", useUserOverride: false));
            ayuda.CapituloElegido!.Clave.Should().Be("README");
            ayuda.CapituloElegido.Idioma.Should().Be("en");
            ayuda.CapitulosVisibles.Should().Contain(ayuda.CapituloElegido);

            ayuda.AbrirCapitulo("13-tarjeta-qsl");
            Textos.Cambiar(new CultureInfo("de-DE", useUserOverride: false));
            ayuda.CapituloElegido!.Clave.Should().Be("13-tarjeta-qsl");
            ayuda.CapituloElegido.Texto.Should().Contain("Dieses Kapitel");

            // Lo que se esta viendo tampoco se pierde.
            ayuda.VerAcercaDe();
            Textos.Cambiar(new CultureInfo("en-GB", useUserOverride: false));
            ayuda.Vista.Should().Be(VistaDeLaAyuda.AcercaDe);
            ayuda.TituloDeLaVista.Should().Be("About Cuaderno NODISLA");
            ayuda.TextoDeLicencia.Should().Contain("GPL-3.0").And.Contain("GNU General Public License");

            Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));
            ayuda.CapituloElegido!.Clave.Should().Be("13-tarjeta-qsl");
            ayuda.CapituloElegido.SinTraducir.Should().BeFalse();
            ayuda.TituloDeLaVista.Should().Be("Acerca de Cuaderno NODISLA");
        }
        finally
        {
            Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));
        }
    }

    [Fact]
    public void Un_ancla_espanola_lleva_al_mismo_apartado_del_capitulo_traducido()
    {
        try
        {
            Textos.Cambiar(new CultureInfo("en-GB", useUserOverride: false));
            var ayuda = new VistaModeloAyuda(Libro, version: "0.3.0");

            // Asi enlaza el capitulo de equipos (sin traducir) al paso 4 del primer uso.
            ayuda.SeguirEnlace("01-primer-uso.md#paso-4--configuración--equipo-cat-con-el-ft-710");

            ayuda.CapituloElegido!.Idioma.Should().Be("en");
            var titulos = DocumentoMarkdown.Leer(ayuda.CapituloElegido.Texto).OfType<TituloMd>().ToList();
            var paso4 = titulos.Single(t => DocumentoMarkdown.TextoPlano(t.Trozos).StartsWith("Step 4", StringComparison.Ordinal));
            ayuda.AnclaPedida.Should().Be(paso4.Ancla);
        }
        finally
        {
            Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));
        }
    }
}
