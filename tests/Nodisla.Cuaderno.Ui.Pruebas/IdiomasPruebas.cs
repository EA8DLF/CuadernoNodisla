using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using FluentAssertions;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Ui.Ajustes;
using Nodisla.Cuaderno.Ui.VistaModelos;

namespace Nodisla.Cuaderno.Ui.Pruebas;

/// <summary>
/// Que la pantalla habla el idioma elegido: ningún texto escrito a fuego en los XAML, ninguna
/// clave usada que no exista, y el cambio de idioma en caliente, sin reabrir nada.
/// </summary>
/// <remarks>
/// Va en la colección de la ventana, que corre sola y después de las demás: cambiar de idioma es
/// cosa de todo el proceso, y una prueba de otra clase que mirase un texto en español a la vez lo
/// vería en alemán.
/// </remarks>
[Collection(nameof(ColeccionDeLaVentana))]
public sealed partial class IdiomasPruebas
{
    /// <summary>Atributos que llevan texto para el operador.</summary>
    [GeneratedRegex(@"(?<![\w.])(?<atributo>Text|Content|Header|ToolTip|Title|AutomationProperties\.Name|AutomationProperties\.HelpText|Tag|Description|PlaceholderText|ContentStringFormat|HeaderStringFormat)\s*=\s*""(?<valor>[^""]*)""")]
    private static partial Regex AtributoConTexto();

    /// <summary>Texto suelto dentro de un elemento que lo enseña: <c>&lt;Run&gt;Hola&lt;/Run&gt;</c>.</summary>
    [GeneratedRegex(@"<(?<elemento>TextBlock|Run|Label|Button|ToggleButton|RepeatButton|CheckBox|RadioButton|Hyperlink|Span|Bold|Italic|Underline|ToolTip|TabItem|ComboBoxItem|ListBoxItem|MenuItem|Paragraph|GroupBox|Expander|TextBox|sys:String)\b[^>]*(?<!/)>(?<texto>[^<]+)<")]
    private static partial Regex TextoDentro();

    /// <summary>Formatos de enlace con palabras dentro: <c>StringFormat='{}{0} contactos'</c>.</summary>
    [GeneratedRegex(@"StringFormat\s*=\s*(?<comilla>['""]?)(?<valor>\{\}[^'""]*?|[^'"",}]*\{0[^'""]*?)\k<comilla>(?=[,}\s""])")]
    private static partial Regex FormatoConTexto();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comentario();

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex Hueco();

    [GeneratedRegex(@"\p{L}{2,}")]
    private static partial Regex Palabra();

    [GeneratedRegex(@"\p{Ll}")]
    private static partial Regex Minuscula();

    /// <summary>Claves usadas en el código y en los XAML.</summary>
    [GeneratedRegex(@"(?:Textos\.(?:T|F)\(\s*""|Texto\.Enlace\(\s*""|\{loc:Texto\s+(?:Clave=)?)(?<clave>[A-Z][A-Za-z0-9]*\.[A-Za-z0-9_.]+)")]
    private static partial Regex ClaveUsada();

    /// <summary>La raíz del proyecto, buscada desde ESTE fichero fuente (las pruebas pueden compilarse fuera del árbol).</summary>
    private static string Raiz => BuscarRaiz();

    private static string BuscarRaiz([System.Runtime.CompilerServices.CallerFilePath] string fuente = "")
    {
        foreach (var inicio in new[] { Path.GetDirectoryName(fuente), AppContext.BaseDirectory })
        {
            var carpeta = string.IsNullOrEmpty(inicio) ? null : new DirectoryInfo(inicio);
            while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "CuadernoNodisla.sln"))) carpeta = carpeta.Parent;
            if (carpeta is not null) return carpeta.FullName;
        }

        throw new InvalidOperationException("No se encuentra la raíz del proyecto.");
    }

    private static IEnumerable<string> Ficheros(string carpeta, string patron) =>
        Directory.EnumerateFiles(carpeta, patron, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// Lo que puede ir escrito tal cual: los rótulos de la radio, unidades, nombres propios...
    /// Uno por línea en <c>Idiomas\Permitidos\*.txt</c>; <c>#</c> empieza un comentario.
    /// </summary>
    private static HashSet<string> Permitidos()
    {
        var permitidos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fichero in Directory.EnumerateFiles(Path.Combine(Raiz, "tests", "Nodisla.Cuaderno.Ui.Pruebas", "Idiomas", "Permitidos"), "*.txt"))
        {
            foreach (var linea in File.ReadAllLines(fichero, Encoding.UTF8))
            {
                if (linea.Length == 0 || linea.StartsWith('#')) continue;
                permitidos.Add(linea);
            }
        }

        return permitidos;
    }

    /// <summary>
    /// Un literal se puede quedar si no dice nada en ningún idioma: sin letras (símbolos, cifras),
    /// todo en mayúsculas como el panel de la radio (MODE, SPLIT, VFO A) o en la lista de permitidos.
    /// </summary>
    private static bool PuedeQuedarse(string literal, HashSet<string> permitidos)
    {
        var texto = System.Net.WebUtility.HtmlDecode(literal).Trim();
        if (texto.Length == 0) return true;
        if (permitidos.Contains(texto)) return true;
        var sinHuecos = Hueco().Replace(texto, " ");
        if (permitidos.Contains(sinHuecos.Trim())) return true; // «{0} Hz»: una unidad con su cifra
        if (!Palabra().IsMatch(sinHuecos)) return true;
        return !Minuscula().IsMatch(sinHuecos);
    }

    [Fact]
    public void No_queda_ningun_texto_escrito_a_fuego_en_los_XAML()
    {
        var permitidos = Permitidos();
        var encontrados = new List<string>();

        foreach (var fichero in Ficheros(Path.Combine(Raiz, "src"), "*.xaml"))
        {
            var xaml = Comentario().Replace(File.ReadAllText(fichero, Encoding.UTF8), m => new string('\n', m.Value.Count(c => c == '\n')));
            var nombre = Path.GetRelativePath(Path.Combine(Raiz, "src"), fichero);

            foreach (Match m in AtributoConTexto().Matches(xaml))
            {
                var valor = m.Groups["valor"].Value;
                if (valor.StartsWith('{') && !valor.StartsWith("{}", StringComparison.Ordinal)) continue;
                if (m.Groups["atributo"].Value == "Tag") continue;
                if (!PuedeQuedarse(valor.StartsWith("{}", StringComparison.Ordinal) ? valor[2..] : valor, permitidos))
                {
                    encontrados.Add($"{nombre}:{Linea(xaml, m.Index)} {m.Groups["atributo"].Value}=\"{valor}\"");
                }
            }

            foreach (Match m in TextoDentro().Matches(xaml))
            {
                var texto = m.Groups["texto"].Value;
                if (!PuedeQuedarse(texto, permitidos))
                {
                    encontrados.Add($"{nombre}:{Linea(xaml, m.Index)} <{m.Groups["elemento"].Value}>{texto.Trim()}<");
                }
            }

            foreach (Match m in FormatoConTexto().Matches(xaml))
            {
                var valor = m.Groups["valor"].Value;
                if (!PuedeQuedarse(valor.StartsWith("{}", StringComparison.Ordinal) ? valor[2..] : valor, permitidos))
                {
                    encontrados.Add($"{nombre}:{Linea(xaml, m.Index)} StringFormat={valor}");
                }
            }
        }

        encontrados.Should().BeEmpty("todo lo que lee el operador va en los .resx; lo de la radio, en la lista de permitidos");
    }

    [Fact]
    public void Cada_clave_usada_en_el_codigo_existe()
    {
        var existentes = Textos.Apartados.SelectMany(a => Textos.TextosDe(a, "es").Keys).ToHashSet(StringComparer.Ordinal);
        var faltan = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var fichero in Ficheros(Path.Combine(Raiz, "src"), "*.cs").Concat(Ficheros(Path.Combine(Raiz, "src"), "*.xaml")))
        {
            foreach (Match m in ClaveUsada().Matches(File.ReadAllText(fichero, Encoding.UTF8)))
            {
                var clave = m.Groups["clave"].Value.TrimEnd('.');
                if (!existentes.Contains(clave)) faltan.Add($"{clave} ({Path.GetFileName(fichero)})");
            }
        }

        faltan.Should().BeEmpty();
    }

    [Fact]
    public void Los_permitidos_no_esconden_frases()
    {
        // La lista de permitidos es para rótulos, unidades y nombres propios. Una frase con
        // minúsculas de más de tres palabras es casi seguro un texto que se ha escapado.
        Permitidos().Where(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 3 && Minuscula().IsMatch(p))
            .Should().BeEmpty();
    }

    [Fact]
    public Task Un_texto_de_XAML_cambia_de_idioma_en_caliente() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            const string Xaml = """
                <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            xmlns:loc="clr-namespace:Nodisla.Cuaderno.Ui.Idiomas;assembly=Nodisla.Cuaderno.Ui">
                    <TextBlock x:Name="Uno" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Text="{loc:Texto Comun.Cerrar}" />
                    <Button Content="{loc:Texto Comun.Guardar}" ToolTip="{loc:Texto Comun.Indicativo}" />
                    <TextBlock Text="{loc:Texto Ajustes.Idioma.DelSistema, Valor={Binding}}" />
                </StackPanel>
                """;

            // Lo mismo que hace el programa al arrancar: así las ventanas siguen al idioma.
            Nodisla.Cuaderno.Ui.Idiomas.IdiomaDeLaInterfaz.Arrancar("es");
            var panel = (StackPanel)XamlReader.Parse(Xaml);
            panel.DataContext = "Deutsch";
            var ventana = VentanaApartada(panel);
            try
            {
                await EsperaALaVentana.DrenarAsync();
                var texto = (TextBlock)panel.Children[0];
                var boton = (Button)panel.Children[1];
                var compuesto = (TextBlock)panel.Children[2];

                texto.Text.Should().Be("Cerrar");
                boton.Content.Should().Be("Guardar");
                compuesto.Text.Should().Be("Idioma del sistema (Deutsch)");

                Textos.Cambiar("de");
                await EsperaALaVentana.DrenarAsync();

                texto.Text.Should().Be("Schließen");
                boton.Content.Should().Be("Speichern");
                boton.ToolTip.Should().Be("Rufzeichen");
                compuesto.Text.Should().Be("Systemsprache (Deutsch)");
                ventana.Language.IetfLanguageTag.Should().StartWith("de", "los StringFormat de la ventana siguen el idioma");

                Textos.Cambiar("pt");
                await EsperaALaVentana.DrenarAsync();
                texto.Text.Should().Be("Fechar");
            }
            finally
            {
                VolverAlEspanol();
                ventana.Close();
            }
        });

    [Fact]
    public Task Elegir_el_idioma_en_la_configuracion_lo_cambia_y_lo_guarda() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            var carpeta = Directory.CreateTempSubdirectory("cuaderno-idioma-").FullName;
            try
            {
                var ajustes = new AjustesDelPrograma();
                var modelo = new VistaModeloIdioma(ajustes, carpeta);
                modelo.Elegida.Codigo.Should().BeNull("sin elegir, manda el idioma del sistema");
                modelo.Opciones.Select(o => o.Codigo).Should().Equal(null, "es", "en", "pt", "fr", "it", "de");
                modelo.Opciones.Single(o => o.Codigo == "de").Nombre.Should().Be("Deutsch", "cada idioma se nombra en su propio idioma");

                var nombreDelSistema = modelo.Opciones[0].Nombre;
                modelo.Elegida = modelo.Opciones.Single(o => o.Codigo == "en");
                await EsperaALaVentana.DrenarAsync();

                Textos.Codigo.Should().Be("en");
                modelo.Opciones[0].Nombre.Should().StartWith("System language").And.NotBe(nombreDelSistema);
                AjustesDelPrograma.Leer(carpeta).Idioma.Should().Be("en", "se guarda en los ajustes");

                var otraVez = new VistaModeloIdioma(AjustesDelPrograma.Leer(carpeta), null);
                otraVez.Elegida.Codigo.Should().Be("en", "al volver a abrir sale lo guardado");
            }
            finally
            {
                VolverAlEspanol();
                Directory.Delete(carpeta, recursive: true);
            }
        });

    [Fact]
    public Task La_configuracion_tiene_su_apartado_de_idioma() =>
        HiloDeVentana.Ejecutar(async () =>
        {
            VistaModeloAjustes.Apartados.Should().HaveCount(VistaModeloAjustes.ApartadoServidor + 1);
            VistaModeloAjustes.ClavesDeLosApartados[VistaModeloAjustes.ApartadoIdioma].Should().Be("Ajustes.Apartado.Idioma");
            await Task.CompletedTask;
        });

    private static Window VentanaApartada(FrameworkElement contenido)
    {
        var ventana = new Window
        {
            Content = contenido,
            Width = 600,
            Height = 300,
            Left = -6000,
            Top = 0,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        ventana.Show();
        return ventana;
    }

    private static void VolverAlEspanol() => Textos.Cambiar(new CultureInfo("es-ES", useUserOverride: false));

    private static int Linea(string texto, int indice) => texto.AsSpan(0, indice).Count('\n') + 1;
}
