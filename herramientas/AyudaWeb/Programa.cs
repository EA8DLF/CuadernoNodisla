using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Nodisla.Cuaderno.Ui.Soporte;

namespace Nodisla.Cuaderno.Herramientas.AyudaWeb;

/// <summary>
/// Convierte <c>docs/ayuda/*.md</c> en una web estática en <c>docs/web-ayuda</c>: una página por
/// capítulo, el índice a la izquierda, un buscador sencillo y las capturas al lado.
/// </summary>
/// <remarks>
/// Usa el mismo <see cref="DocumentoMarkdown"/> que la ayuda del programa, así que la web y el
/// programa cuentan exactamente lo mismo. No publica nada: solo deja los ficheros.
/// </remarks>
public static class Programa
{
    public static int Main(string[] argumentos)
    {
        var raiz = argumentos.Length > 0 ? Path.GetFullPath(argumentos[0]) : BuscarRaiz();
        if (raiz is null)
        {
            Console.Error.WriteLine("No encuentro CuadernoNodisla.sln; pase la raíz del proyecto.");
            return 1;
        }

        var origen = Path.Combine(raiz, "docs", "ayuda");
        var capturas = Path.Combine(raiz, "docs", "capturas", "ayuda");
        var destino = Path.Combine(raiz, "docs", "web-ayuda");

        if (Directory.Exists(destino)) Directory.Delete(destino, recursive: true);
        Directory.CreateDirectory(Path.Combine(destino, "capturas"));

        var capitulos = Directory.GetFiles(origen, "*.md")
            .Select(f => (Clave: Path.GetFileNameWithoutExtension(f), Texto: File.ReadAllText(f, Encoding.UTF8)))
            .OrderBy(c => c.Clave.Equals("README", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Clave, StringComparer.Ordinal)
            .Select(c => (c.Clave, c.Texto, Bloques: DocumentoMarkdown.Leer(c.Texto)))
            .ToList();

        var usadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indice = new List<object>();
        foreach (var (clave, _, bloques) in capitulos)
        {
            var titulo = DocumentoMarkdown.TituloPrincipal(bloques) ?? clave;
            var html = new Html(usadas);
            foreach (var b in bloques) html.Bloque(b);
            File.WriteAllText(Path.Combine(destino, Pagina(clave)), Plantilla(titulo, clave, capitulos.Select(c => (c.Clave, DocumentoMarkdown.TituloPrincipal(c.Bloques) ?? c.Clave)).ToList(), html.ToString()), new UTF8Encoding(false));
            indice.Add(new { pagina = Pagina(clave), titulo, texto = Plano(bloques) });
        }

        foreach (var nombre in usadas)
        {
            var fichero = Path.Combine(capturas, nombre);
            if (File.Exists(fichero)) File.Copy(fichero, Path.Combine(destino, "capturas", nombre), overwrite: true);
            else Console.Error.WriteLine($"Falta la captura {nombre}");
        }

        File.WriteAllText(Path.Combine(destino, "indice.js"), "window.INDICE_AYUDA = " + JsonSerializer.Serialize(indice) + ";\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(destino, "estilos.css"), Estilos, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(destino, "buscar.js"), Buscador, new UTF8Encoding(false));
        Console.WriteLine($"Ayuda web: {capitulos.Count} capítulos y {usadas.Count} capturas en {destino}");
        return 0;
    }

    private static string? BuscarRaiz()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CuadernoNodisla.sln"))) dir = dir.Parent;
        return dir?.FullName;
    }

    private static string Pagina(string clave) => clave.Equals("README", StringComparison.OrdinalIgnoreCase) ? "index.html" : clave + ".html";

    private static string Plano(IEnumerable<BloqueMd> bloques)
    {
        var sb = new StringBuilder();
        foreach (var b in bloques)
        {
            switch (b)
            {
                case TituloMd t: sb.Append(DocumentoMarkdown.TextoPlano(t.Trozos)).Append(' '); break;
                case ParrafoMd p: sb.Append(DocumentoMarkdown.TextoPlano(p.Trozos)).Append(' '); break;
                case ListaMd l:
                    foreach (var e in l.Elementos)
                    {
                        sb.Append(DocumentoMarkdown.TextoPlano(e.Trozos)).Append(' ');
                        sb.Append(Plano(e.Hijos));
                    }

                    break;
                case CitaMd c: sb.Append(Plano(c.Bloques)); break;
                case TablaMd t:
                    foreach (var fila in t.Filas.Prepend(t.Cabecera))
                    {
                        foreach (var celda in fila) sb.Append(DocumentoMarkdown.TextoPlano(celda)).Append(' ');
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    private static string Plantilla(string titulo, string clave, List<(string Clave, string Titulo)> todos, string cuerpo)
    {
        var nav = new StringBuilder();
        foreach (var (c, t) in todos)
        {
            var actual = c == clave ? " class=\"actual\" aria-current=\"page\"" : string.Empty;
            nav.Append(CultureInfo.InvariantCulture, $"<li><a href=\"{Pagina(c)}\"{actual}>{WebUtility.HtmlEncode(t)}</a></li>\n");
        }

        return $$"""
            <!doctype html>
            <html lang="es">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{WebUtility.HtmlEncode(titulo)}} · Ayuda de Cuaderno NODISLA</title>
            <link rel="stylesheet" href="estilos.css">
            </head>
            <body>
            <header class="cabecera"><a href="index.html" class="marca">Cuaderno NODISLA</a><span>Ayuda</span></header>
            <div class="marco">
            <nav class="indice" aria-label="Capítulos">
            <input id="buscar" type="search" placeholder="Buscar en la ayuda" aria-label="Buscar en la ayuda">
            <p id="resultado" class="tenue"></p>
            <ul id="capitulos">
            {{nav}}</ul>
            </nav>
            <main class="capitulo">
            {{cuerpo}}
            </main>
            </div>
            <footer class="pie">Cuaderno NODISLA · GPL-3.0 · <a href="https://github.com/EA8DLF/CuadernoNodisla">github.com/EA8DLF/CuadernoNodisla</a> · <a href="mailto:nodisla@nodisla.org">nodisla@nodisla.org</a></footer>
            <script src="indice.js"></script>
            <script src="buscar.js"></script>
            </body>
            </html>
            """;
    }

    private sealed class Html(HashSet<string> usadas)
    {
        private readonly StringBuilder _sb = new();

        public override string ToString() => _sb.ToString();

        public void Bloque(BloqueMd bloque)
        {
            switch (bloque)
            {
                case TituloMd t:
                    _sb.Append(CultureInfo.InvariantCulture, $"<h{t.Nivel} id=\"{WebUtility.HtmlEncode(t.Ancla)}\">{EnLinea(t.Trozos)}</h{t.Nivel}>\n");
                    break;
                case ParrafoMd p:
                    _sb.Append("<p>").Append(EnLinea(p.Trozos)).Append("</p>\n");
                    break;
                case ListaMd l:
                    var etiqueta = l.Numerada ? "ol" : "ul";
                    _sb.Append('<').Append(etiqueta).Append(">\n");
                    foreach (var e in l.Elementos)
                    {
                        _sb.Append("<li>").Append(EnLinea(e.Trozos));
                        foreach (var h in e.Hijos) Bloque(h);
                        _sb.Append("</li>\n");
                    }

                    _sb.Append("</").Append(etiqueta).Append(">\n");
                    break;
                case CitaMd c:
                    _sb.Append("<blockquote>\n");
                    foreach (var b in c.Bloques) Bloque(b);
                    _sb.Append("</blockquote>\n");
                    break;
                case CodigoMd c:
                    _sb.Append("<pre><code>").Append(WebUtility.HtmlEncode(c.Texto)).Append("</code></pre>\n");
                    break;
                case TablaMd t:
                    _sb.Append("<div class=\"tabla\"><table>\n<thead><tr>");
                    foreach (var c in t.Cabecera) _sb.Append("<th>").Append(EnLinea(c)).Append("</th>");
                    _sb.Append("</tr></thead>\n<tbody>\n");
                    foreach (var fila in t.Filas)
                    {
                        _sb.Append("<tr>");
                        foreach (var c in fila.Take(t.Cabecera.Count)) _sb.Append("<td>").Append(EnLinea(c)).Append("</td>");
                        _sb.Append("</tr>\n");
                    }

                    _sb.Append("</tbody></table></div>\n");
                    break;
                case ImagenMd i:
                    var nombre = Path.GetFileName(i.Ruta.Replace('\\', '/'));
                    usadas.Add(nombre);
                    var alt = WebUtility.HtmlEncode(i.Texto);
                    _sb.Append(CultureInfo.InvariantCulture, $"<figure><a href=\"capturas/{nombre}\"><img src=\"capturas/{nombre}\" alt=\"{alt}\" loading=\"lazy\"></a><figcaption>{alt}</figcaption></figure>\n");
                    break;
                case RayaMd:
                    _sb.Append("<hr>\n");
                    break;
            }
        }

        private static string EnLinea(IEnumerable<TrozoMd> trozos)
        {
            var sb = new StringBuilder();
            foreach (var t in trozos)
            {
                var texto = WebUtility.HtmlEncode(t.Texto);
                if (t.Codigo) texto = "<code>" + texto + "</code>";
                if (t.Cursiva) texto = "<em>" + texto + "</em>";
                if (t.Negrita) texto = "<strong>" + texto + "</strong>";
                if (t.Enlace is { } destino) texto = $"<a href=\"{WebUtility.HtmlEncode(Destino(destino))}\">{texto}</a>";
                sb.Append(texto);
            }

            return sb.ToString();
        }

        private static string Destino(string destino)
        {
            if (destino.Contains("://", StringComparison.Ordinal) || destino.StartsWith("mailto:", StringComparison.Ordinal)) return destino;
            var partes = destino.Split('#', 2);
            var fichero = partes[0];
            if (fichero.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                var clave = Path.GetFileNameWithoutExtension(fichero);
                fichero = Pagina(clave);
            }

            return partes.Length == 2 ? fichero + "#" + partes[1] : fichero;
        }
    }

    private const string Estilos = """
        :root { --fondo:#f6f7f9; --panel:#ffffff; --texto:#1c2430; --tenue:#5b6676; --acento:#1f6fb2; --borde:#d9dee5; --alterno:#eef1f5; --cromo:#0f141a; }
        @media (prefers-color-scheme: dark) { :root { --fondo:#14181e; --panel:#1b2129; --texto:#e3e8ef; --tenue:#9aa6b5; --acento:#5aaef0; --borde:#2f3844; --alterno:#222a33; --cromo:#0a0d11; } }
        * { box-sizing: border-box; }
        body { margin:0; background:var(--fondo); color:var(--texto); font:16px/1.6 "Segoe UI", system-ui, sans-serif; }
        a { color:var(--acento); }
        .cabecera { background:var(--cromo); color:#fff; padding:10px 16px; display:flex; gap:12px; align-items:baseline; }
        .cabecera .marca { color:#5aaef0; font-weight:700; font-size:1.2rem; text-decoration:none; }
        .marco { display:flex; gap:16px; max-width:1300px; margin:0 auto; padding:16px; }
        .indice { flex:0 0 260px; position:sticky; top:12px; align-self:flex-start; max-height:calc(100vh - 24px); overflow:auto; }
        .indice input { width:100%; padding:7px 9px; border:1px solid var(--borde); border-radius:4px; background:var(--panel); color:var(--texto); font:inherit; }
        .indice ul { list-style:none; padding:0; margin:6px 0; }
        .indice li a { display:block; padding:5px 8px; border-radius:4px; text-decoration:none; color:var(--texto); }
        .indice li a:hover { background:var(--alterno); }
        .indice li a.actual { background:var(--alterno); border-left:3px solid var(--acento); font-weight:600; }
        .tenue { color:var(--tenue); font-size:.9rem; margin:4px 0; min-height:1em; }
        .capitulo { flex:1; min-width:0; background:var(--panel); border:1px solid var(--borde); border-radius:6px; padding:8px 28px 28px; }
        h1, h2 { color:var(--acento); } h2 { border-bottom:1px solid var(--borde); padding-bottom:3px; margin-top:1.8em; }
        code { font-family:Consolas, "Cascadia Mono", monospace; background:var(--alterno); padding:0 4px; border-radius:3px; font-size:.93em; }
        pre { background:var(--alterno); border:1px solid var(--borde); padding:10px; overflow:auto; }
        pre code { background:none; padding:0; }
        blockquote { margin:8px 0; padding:6px 14px; border-left:4px solid var(--acento); background:var(--alterno); }
        .tabla { overflow-x:auto; } table { border-collapse:collapse; margin:6px 0 14px; width:100%; }
        th, td { border:1px solid var(--borde); padding:5px 9px; text-align:left; vertical-align:top; }
        th { background:var(--alterno); } tr:nth-child(even) td { background:var(--alterno); }
        figure { margin:10px 0 18px; } figure img { max-width:100%; height:auto; border:1px solid var(--borde); }
        figcaption { color:var(--tenue); font-style:italic; font-size:.9rem; }
        mark { background:#ffe58a; color:#1c2430; }
        .pie { text-align:center; color:var(--tenue); font-size:.85rem; padding:16px; }
        @media (max-width: 800px) { .marco { flex-direction:column; padding:16px; } .indice { position:static; flex:none; max-height:none; } .capitulo { padding:4px 16px 20px; } }
        """;

    private const string Buscador = """
        (function () {
          var caja = document.getElementById('buscar'), lista = document.getElementById('capitulos'), res = document.getElementById('resultado');
          var normal = function (t) { return t.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase(); };
          var datos = (window.INDICE_AYUDA || []).map(function (c) { return { pagina: c.pagina, buscable: normal(c.titulo + ' ' + c.texto) }; });
          function filtrar() {
            var palabras = normal(caja.value).split(/\s+/).filter(Boolean), n = 0;
            Array.prototype.forEach.call(lista.querySelectorAll('li'), function (li) {
              var href = li.querySelector('a').getAttribute('href');
              var c = datos.find(function (d) { return d.pagina === href; });
              var ok = !palabras.length || (c && palabras.every(function (p) { return c.buscable.indexOf(p) >= 0; }));
              li.style.display = ok ? '' : 'none'; if (ok) n++;
            });
            res.textContent = !palabras.length ? '' : n === 0 ? 'Ningún capítulo lo menciona.' : n === 1 ? 'Lo menciona 1 capítulo.' : 'Lo mencionan ' + n + ' capítulos.';
            try { sessionStorage.setItem('buscarAyuda', caja.value); } catch (e) { }
          }
          try { caja.value = sessionStorage.getItem('buscarAyuda') || ''; } catch (e) { }
          caja.addEventListener('input', filtrar); filtrar();
        })();
        """;
}
