using System.Globalization;
using Nodisla.Cuaderno.Impresion.Modelo;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Nodisla.Cuaderno.Impresion.Pdf;

/// <summary>Como se alinea un texto dentro de su casilla.</summary>
public enum Alineacion
{
    /// <summary>Pegado a la izquierda.</summary>
    Izquierda = 0,

    /// <summary>Centrado.</summary>
    Centro,

    /// <summary>Pegado a la derecha.</summary>
    Derecha,
}

/// <summary>
/// Compone las etiquetas y las tarjetas en PDF con <b>PDFsharp</b> (MIT, sin dependencias
/// nativas, aprobado por Jose el 26-09-2026).
/// </summary>
/// <remarks>
/// <para>
/// Es la implementacion de omision de <see cref="IGeneradorDeImpresos"/>. Antes de esto el
/// modulo escribia el PDF a mano, byte a byte, con las catorce fuentes estandar y sin incrustar
/// nada: funcionaba, pero no sabia medir texto con metricas reales ni incrustar tipografias, asi
/// que cualquier cosa mas alla de lineas y texto en Helvetica habria exigido reescribirlo.
/// Con PDFsharp se dibuja igual —milimetros, alineacion, columnas medidas— pero el texto se mide
/// con la fuente de verdad (<see cref="XGraphics.MeasureString(string, XFont)"/>) y las fuentes
/// del sistema se incrustan solas, sin tocar la licencia del programa: PDFsharp es MIT y no
/// arrastra nada AGPL como iText, que es lo que usa el original y por lo que no se podia usar.
/// </para>
/// <para>
/// <b>Sigue detras de <see cref="IGeneradorDeImpresos"/> y sigue siendo una sola clase.</b> El
/// modelo de la etiqueta, la seleccion de contactos y la pantalla no saben con que biblioteca se
/// dibuja; cambiarla otra vez, si algun dia hiciera falta, vuelve a ser tocar esta clase y la
/// linea de registro en <see cref="ExtensionesDeServicio"/>.
/// </para>
/// </remarks>
public sealed class GeneradorDeImpresosPdf : IGeneradorDeImpresos
{
    private const string TipoDeMedio = "application/pdf";

    /// <summary>Aire entre dos columnas de la etiqueta, en milimetros.</summary>
    private const double SeparacionDeColumnaMm = 1.2;

    /// <summary>Cuerpo de letra por debajo del cual la etiqueta deja de leerse a simple vista.</summary>
    private const double TamanoMinimoPuntos = 5.0;

    private const double PuntosPorMm = 72.0 / 25.4;

    static GeneradorDeImpresosPdf()
    {
        // La aplicacion es de escritorio para Windows y corre siempre en la maquina de Jose, asi
        // que basta con las fuentes del sistema (Arial, Arial Bold/Italic, Courier New) sin
        // escribir un resolutor de fuentes propio. PDFsharp avisa que para repartir el programa
        // a otras maquinas conviene un resolutor a medida; aqui no hace falta, porque no se
        // reparte: es para uso particular de Jose en su propio equipo.
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    /// <inheritdoc/>
    public Impreso Etiquetas(
        IReadOnlyList<EtiquetaDeQsl> etiquetas,
        PlantillaDeEtiquetas plantilla,
        OpcionesDeImpresion? opciones = null)
    {
        ArgumentNullException.ThrowIfNull(etiquetas);
        ArgumentNullException.ThrowIfNull(plantilla);

        var ajustes = opciones ?? new OpcionesDeImpresion();
        if (!plantilla.CabeEnElPapel)
        {
            throw new ArgumentException(
                $"La plantilla «{plantilla.Nombre}» no cabe en su hoja: revisa márgenes y separaciones.",
                nameof(plantilla));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(ajustes.PrimeraCasilla);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            ajustes.PrimeraCasilla, plantilla.PorHoja, nameof(ajustes.PrimeraCasilla));

        using var documento = new PdfDocument();
        documento.Info.Title = ajustes.Titulo;
        documento.Info.Creator = "Cuaderno NODISLA";

        // Si no hay ni una etiqueta se saca una hoja en blanco con el aviso. Devolver un PDF sin
        // paginas reventaria al escribirlo, y devolver nada dejaria al operador sin saber si el
        // filtro no encontro nada o si fallo el programa.
        if (etiquetas.Count == 0)
        {
            var vacia = NuevaPagina(documento, plantilla.Pagina);
            using (var gfxVacia = XGraphics.FromPdfPage(vacia))
            {
                // PDFsharp solo vuelca el contenido de la pagina cuando se hace Dispose de su
                // XGraphics; hay que cerrarlo aqui, antes de leer los bytes, no al salir del
                // metodo con un «return» en la misma linea que lo abrio.
                Texto(
                    gfxVacia, "Ningún contacto cumple los criterios de la selección.",
                    plantilla.MargenIzquierdoMm, plantilla.MargenSuperiorMm, Fuente(11));
            }

            return new Impreso(ABytes(documento), TipoDeMedio, 1, "etiquetas-qsl.pdf");
        }

        PdfPage? pagina = null;
        XGraphics? gfx = null;
        var esLaPrimeraPagina = true;
        var casilla = ajustes.PrimeraCasilla;

        foreach (var etiqueta in etiquetas)
        {
            if (pagina is null || casilla >= plantilla.PorHoja)
            {
                gfx?.Dispose();
                pagina = NuevaPagina(documento, plantilla.Pagina);
                gfx = XGraphics.FromPdfPage(pagina);
                casilla = esLaPrimeraPagina ? ajustes.PrimeraCasilla : 0;
                esLaPrimeraPagina = false;
            }

            var (x, y) = plantilla.Esquina(casilla);
            DibujarEtiqueta(gfx!, etiqueta, x, y, plantilla.AnchoMm, plantilla.AltoMm, ajustes);
            casilla++;
        }

        gfx?.Dispose();

        // El numero de paginas se lee antes de ABytes: Save() dejar el documento en solo
        // lectura, y PdfDocument.PageCount lanza si se consulta despues.
        var paginas = documento.PageCount;
        return new Impreso(ABytes(documento), TipoDeMedio, paginas, "etiquetas-qsl.pdf");
    }

    /// <inheritdoc/>
    public Impreso Tarjetas(
        IReadOnlyList<TarjetaDeQsl> tarjetas,
        PlantillaDeTarjetas plantilla,
        OpcionesDeImpresion? opciones = null)
    {
        ArgumentNullException.ThrowIfNull(tarjetas);
        ArgumentNullException.ThrowIfNull(plantilla);

        var ajustes = opciones ?? new OpcionesDeImpresion { Titulo = "Tarjetas de QSL — Cuaderno NODISLA" };
        using var documento = new PdfDocument();
        documento.Info.Title = ajustes.Titulo;
        documento.Info.Creator = "Cuaderno NODISLA";

        if (tarjetas.Count == 0)
        {
            var vacia = NuevaPagina(documento, plantilla.Pagina);
            using (var gfxVacia = XGraphics.FromPdfPage(vacia))
            {
                Texto(
                    gfxVacia, "Ningún contacto cumple los criterios de la selección.",
                    plantilla.MargenIzquierdoMm + 10, plantilla.MargenSuperiorMm, Fuente(11));
            }

            return new Impreso(ABytes(documento), TipoDeMedio, 1, "tarjetas-qsl.pdf");
        }

        PdfPage? pagina = null;
        XGraphics? gfx = null;
        var casilla = 0;

        foreach (var tarjeta in tarjetas)
        {
            if (pagina is null || casilla >= plantilla.PorHoja)
            {
                gfx?.Dispose();
                pagina = NuevaPagina(documento, plantilla.Pagina);
                gfx = XGraphics.FromPdfPage(pagina);
                casilla = 0;
            }

            var (x, y) = plantilla.Esquina(casilla);
            DibujarTarjeta(gfx!, tarjeta, x, y, plantilla, ajustes);
            casilla++;
        }

        gfx?.Dispose();

        var paginas = documento.PageCount;
        return new Impreso(ABytes(documento), TipoDeMedio, paginas, "tarjetas-qsl.pdf");
    }

    private static PdfPage NuevaPagina(PdfDocument documento, TamanoDePagina tamano)
    {
        var pagina = documento.AddPage();
        pagina.Width = XUnit.FromMillimeter(tamano.AnchoMm);
        pagina.Height = XUnit.FromMillimeter(tamano.AltoMm);
        return pagina;
    }

    private static byte[] ABytes(PdfDocument documento)
    {
        using var memoria = new MemoryStream();
        documento.Save(memoria, closeStream: false);
        return memoria.ToArray();
    }

    private static void DibujarEtiqueta(
        XGraphics gfx,
        EtiquetaDeQsl etiqueta,
        double xMm,
        double yMm,
        double anchoMm,
        double altoMm,
        OpcionesDeImpresion ajustes)
    {
        if (ajustes.DibujarContorno)
        {
            Rectangulo(gfx, xMm, yMm, anchoMm, altoMm, 0.1, 0.6);
        }

        var margen = ajustes.MargenInteriorMm;
        var x = xMm + margen;
        var ancho = anchoMm - (2 * margen);
        var y = yMm + margen;

        // ── Encabezado: a quien va la tarjeta. Es lo unico que se lee de lejos.
        var tamanoEncabezado = ajustes.TamanoDeTextoPuntos + 2.5;
        TextoEnCasilla(gfx, etiqueta.Encabezado, x, y, ancho, Fuente(tamanoEncabezado, negrita: true));
        y += (tamanoEncabezado * 25.4 / 72.0) + 0.8;

        Linea(gfx, x, y, x + ancho, y, 0.15, 0.3);
        y += 1.0;

        // ── Las columnas se miden por lo que llevan dentro, no por porcentajes fijos.
        //    Un reparto fijo tiene que elegir entre cortar la fecha —que es lo que identifica el
        //    contacto— o cortar el satelite —sin el cual una QSL de satelite no confirma nada—.
        //    Midiendo, cada columna ocupa lo suyo, y si aun asi no cabe se baja el cuerpo de
        //    letra hasta que quepa, con un suelo por debajo del cual dejaria de leerse.
        var filas = etiqueta.Contactos.Select(Celdas).ToList();
        var (tamanoTexto, anchos) = Encajar(gfx, filas, ancho, ajustes.TamanoDeTextoPuntos);
        var alturaLinea = (tamanoTexto * 25.4 / 72.0) + 0.5;

        foreach (var fila in filas)
        {
            var cx = x;
            for (var i = 0; i < fila.Length; i++)
            {
                // La marca de satelite es la ultima y va pegada a la derecha, donde el ojo la
                // busca; las demas, alineadas a la izquierda para que las columnas cuadren.
                var alineacion = i == fila.Length - 1 ? Alineacion.Derecha : Alineacion.Izquierda;
                TextoEnCasilla(
                    gfx, fila[i], cx, y, anchos[i] - SeparacionDeColumnaMm,
                    Fuente(tamanoTexto), alineacion);
                cx += anchos[i];
            }

            y += alturaLinea;
        }

        // ── Pie: mi indicativo, mi localizador y la frase. Se ancla al fondo de la etiqueta y no
        //    detras del ultimo contacto, para que todas las etiquetas de la hoja queden iguales.
        var tamanoPie = tamanoTexto - 0.5;
        var yPie = yMm + altoMm - margen - (tamanoPie * 25.4 / 72.0);
        if (yPie <= y)
        {
            return;
        }

        var pie = etiqueta.MiIndicativo.EsVacio ? string.Empty : etiqueta.MiIndicativo.Valor;
        if (!etiqueta.MiLocalizador.EsVacio)
        {
            pie = $"{pie}  {etiqueta.MiLocalizador.Valor}";
        }

        TextoEnCasilla(gfx, pie, x, yPie, ancho * 0.5, Fuente(tamanoPie, negrita: true));

        var derecha = etiqueta.Mensaje;
        if (string.IsNullOrWhiteSpace(derecha) && etiqueta.TextoDeLaVia.Length > 0)
        {
            derecha = $"QSL {etiqueta.TextoDeLaVia}";
        }

        TextoEnCasilla(
            gfx, derecha, x + (ancho * 0.5), yPie, ancho * 0.5,
            Fuente(tamanoPie, cursiva: true), Alineacion.Derecha);
    }

    /// <summary>Las celdas de una linea de contacto, con la marca de satelite al final.</summary>
    /// <remarks>
    /// <c>internal</c> y no <c>private</c> a proposito: las pruebas comprueban directamente que
    /// <see cref="Encajar"/> nunca recorta el nombre del satelite, midiendo con la misma fuente
    /// de verdad con la que se dibuja. Es una prueba mas fuerte que buscar el texto en los
    /// bytes del PDF, que con una fuente incrustada en Unicode ya no aparece como texto llano.
    /// </remarks>
    internal static string[] Celdas(LineaDeContacto linea)
    {
        var columnas = EtiquetaDeQsl.Columnas(linea);
        var marca = linea.EsPorSatelite
            ? string.IsNullOrWhiteSpace(linea.ModoDelSatelite)
                ? linea.Satelite!
                : $"{linea.Satelite} {linea.ModoDelSatelite}"
            : string.Empty;

        return [.. columnas, marca];
    }

    /// <summary>
    /// Busca el cuerpo de letra y los anchos de columna con los que las lineas caben enteras.
    /// </summary>
    /// <param name="gfx">Contexto con el que se mide el texto.</param>
    /// <param name="filas">Celdas de cada linea.</param>
    /// <param name="anchoMm">Ancho disponible dentro de la etiqueta.</param>
    /// <param name="tamanoDeseado">Cuerpo de letra que se preferiria.</param>
    /// <returns>El cuerpo de letra que se usa y el ancho de cada columna.</returns>
    /// <remarks>
    /// Se mide a un tamano y se escala: la anchura de un texto es proporcional al cuerpo, asi
    /// que basta una medicion y una regla de tres, sin iterar. La medicion usa
    /// <see cref="XGraphics.MeasureString(string, XFont)"/> con la fuente de verdad instalada en
    /// el sistema, no una tabla de metricas escrita a mano.
    /// </remarks>
    internal static (double Tamano, double[] Anchos) Encajar(
        XGraphics gfx,
        IReadOnlyList<string[]> filas,
        double anchoMm,
        double tamanoDeseado)
    {
        var columnas = filas.Count == 0 ? 0 : filas[0].Length;
        if (columnas == 0 || anchoMm <= 0)
        {
            return (tamanoDeseado, []);
        }

        var fuenteDeMedida = Fuente(tamanoDeseado);
        var necesarios = new double[columnas];
        for (var i = 0; i < columnas; i++)
        {
            foreach (var fila in filas)
            {
                if (fila[i].Length == 0)
                {
                    continue;
                }

                var w = AMm(gfx.MeasureString(fila[i], fuenteDeMedida).Width);
                necesarios[i] = Math.Max(necesarios[i], w);
            }

            // Una columna vacia en todas las lineas —el satelite cuando no hay ninguno— no
            // ocupa nada y tampoco lleva separacion.
            if (necesarios[i] > 0)
            {
                necesarios[i] += SeparacionDeColumnaMm;
            }
        }

        var total = necesarios.Sum();
        if (total <= anchoMm || total <= 0)
        {
            // Sobra sitio: el hueco se le da a la ultima columna con contenido, para que la
            // marca de satelite quede pegada al borde derecho y las demas no bailen.
            var ultima = Array.FindLastIndex(necesarios, w => w > 0);
            if (ultima >= 0)
            {
                necesarios[ultima] += anchoMm - total;
            }

            return (tamanoDeseado, necesarios);
        }

        var factor = anchoMm / total;
        var tamano = Math.Max(TamanoMinimoPuntos, tamanoDeseado * factor);
        var escala = tamano / tamanoDeseado;

        for (var i = 0; i < columnas; i++)
        {
            necesarios[i] *= escala;
        }

        // Si ni con el cuerpo minimo cabe, se reparte lo que hay: las celdas que sobresalgan las
        // recorta TextoEnCasilla, que es lo menos malo y no se sale de la etiqueta.
        var restante = anchoMm - necesarios.Sum();
        var ultimaConTexto = Array.FindLastIndex(necesarios, w => w > 0);
        if (ultimaConTexto >= 0)
        {
            necesarios[ultimaConTexto] = Math.Max(0, necesarios[ultimaConTexto] + restante);
        }

        return (tamano, necesarios);
    }

    private static void DibujarTarjeta(
        XGraphics gfx,
        TarjetaDeQsl tarjeta,
        double xMm,
        double yMm,
        PlantillaDeTarjetas plantilla,
        OpcionesDeImpresion ajustes)
    {
        var ancho = plantilla.AnchoMm;
        var alto = plantilla.AltoMm;

        if (plantilla.ConMarcasDeCorte)
        {
            MarcasDeCorte(gfx, xMm, yMm, ancho, alto);
        }

        var margen = Math.Max(ajustes.MargenInteriorMm, 4.0);
        var x = xMm + margen;
        var anchoUtil = ancho - (2 * margen);
        var y = yMm + margen;

        // ── Cabecera: el indicativo en grande. Sin esto la tarjeta no vale para nada.
        TextoEnCasilla(
            gfx, tarjeta.MiIndicativo.Valor, x, y, anchoUtil, Fuente(28, negrita: true),
            Alineacion.Centro);
        y += 12;

        var cabecera = new List<string>();
        if (!string.IsNullOrWhiteSpace(tarjeta.MiNombre)) cabecera.Add(tarjeta.MiNombre);
        if (!string.IsNullOrWhiteSpace(tarjeta.MiQth)) cabecera.Add(tarjeta.MiQth);
        if (!tarjeta.Etiqueta.MiLocalizador.EsVacio) cabecera.Add(tarjeta.Etiqueta.MiLocalizador.Valor);
        if (!string.IsNullOrWhiteSpace(tarjeta.Dxcc)) cabecera.Add(tarjeta.Dxcc);

        TextoEnCasilla(
            gfx, string.Join(" · ", cabecera), x, y, anchoUtil, Fuente(9), Alineacion.Centro);
        y += 5;

        var zonas = new List<string>();
        if (tarjeta.Cq is { } cq) zonas.Add($"CQ {cq.ToString(CultureInfo.InvariantCulture)}");
        if (tarjeta.Itu is { } itu) zonas.Add($"ITU {itu.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(tarjeta.MiEquipo)) zonas.Add(tarjeta.MiEquipo);
        if (!string.IsNullOrWhiteSpace(tarjeta.MiAntena)) zonas.Add(tarjeta.MiAntena);

        TextoEnCasilla(
            gfx, string.Join(" · ", zonas), x, y, anchoUtil, Fuente(8), Alineacion.Centro, 0.35);
        y += 7;

        // ── A quien confirma.
        TextoEnCasilla(gfx, "Confirmando QSO con", x, y, anchoUtil, Fuente(8), Alineacion.Centro, 0.4);
        y += 4;
        TextoEnCasilla(
            gfx, tarjeta.Etiqueta.Encabezado, x, y, anchoUtil, Fuente(16, negrita: true),
            Alineacion.Centro);
        y += 9;

        // ── Tabla de contactos, esta vez con cabecera: una tarjeta se lee sin contexto.
        string[] titulos = ["Fecha (UTC)", "Hora", "Banda", "Modo", "RST"];
        double[] pesos = [0.26, 0.14, 0.18, 0.22, 0.12];
        var anchos = pesos.Select(p => p * anchoUtil).ToArray();

        var cx = x;
        for (var i = 0; i < titulos.Length; i++)
        {
            TextoEnCasilla(gfx, titulos[i], cx, y, anchos[i] - 1, Fuente(8, negrita: true));
            cx += anchos[i];
        }

        y += 4;
        Linea(gfx, x, y, x + anchoUtil, y, 0.2);
        y += 1.5;

        foreach (var linea in tarjeta.Etiqueta.Contactos)
        {
            var columnas = EtiquetaDeQsl.Columnas(linea);
            cx = x;
            for (var i = 0; i < columnas.Length; i++)
            {
                TextoEnCasilla(gfx, columnas[i], cx, y, anchos[i] - 1, Fuente(9));
                cx += anchos[i];
            }

            y += 5;

            if (linea.EsPorSatelite)
            {
                var marca = string.IsNullOrWhiteSpace(linea.ModoDelSatelite)
                    ? $"Vía satélite {linea.Satelite}"
                    : $"Vía satélite {linea.Satelite}, modo {linea.ModoDelSatelite}";
                TextoEnCasilla(
                    gfx, marca, x + 2, y - 0.5, anchoUtil - 2, Fuente(7, cursiva: true),
                    Alineacion.Izquierda, 0.3);
                y += 4.0;
            }
        }

        // ── Pie, anclado al fondo.
        var yPie = yMm + alto - margen - 3;
        Linea(gfx, x, yPie - 2, x + anchoUtil, yPie - 2, 0.15, 0.5);
        TextoEnCasilla(
            gfx, tarjeta.Etiqueta.Mensaje ?? "¡Gracias por el contacto!  73",
            x, yPie, anchoUtil * 0.6, Fuente(9, cursiva: true));

        var via = tarjeta.Etiqueta.TextoDeLaVia;
        TextoEnCasilla(
            gfx, via.Length == 0 ? "PSE QSL" : $"QSL {via}",
            x + (anchoUtil * 0.6), yPie, anchoUtil * 0.4,
            Fuente(9, negrita: true), Alineacion.Derecha);
    }

    /// <summary>Marcas de corte cortas en las cuatro esquinas, fuera de la tarjeta.</summary>
    /// <remarks>
    /// Se dibujan fuera y no en el borde: una linea sobre el borde se corta por la mitad con la
    /// guillotina y deja medio trazo negro en la tarjeta acabada.
    /// </remarks>
    private static void MarcasDeCorte(XGraphics gfx, double x, double y, double ancho, double alto)
    {
        const double largo = 4.0;
        const double aire = 1.5;
        const double gris = 0.5;

        foreach (var (mx, signo) in new[] { (x, -1.0), (x + ancho, 1.0) })
        {
            Linea(gfx, mx + (signo * aire), y, mx + (signo * (aire + largo)), y, 0.1, gris);
            Linea(gfx, mx + (signo * aire), y + alto, mx + (signo * (aire + largo)), y + alto, 0.1, gris);
        }

        foreach (var (my, signo) in new[] { (y, -1.0), (y + alto, 1.0) })
        {
            Linea(gfx, x, my + (signo * aire), x, my + (signo * (aire + largo)), 0.1, gris);
            Linea(gfx, x + ancho, my + (signo * aire), x + ancho, my + (signo * (aire + largo)), 0.1, gris);
        }
    }

    // ── Trazado en milimetros sobre XGraphics ──────────────────────────────────────────────
    //
    // PDFsharp mide y coloca en puntos con el origen que se le pida; el resto del modulo —la
    // plantilla, la etiqueta, la tarjeta— piensa en milimetros con origen arriba a la izquierda,
    // que es como vienen las hojas de especificaciones del fabricante y como se pensó todo esto
    // desde el principio. Estos ocho metodos son la unica conversion, y con
    // XPageDirection.Downwards (el que trae XGraphics.FromPdfPage por omision) no hace falta
    // ademas invertir el eje Y a mano, que es lo que si hacia falta escribiendo el PDF a mano.

    /// <summary>Fuente del sistema para el cuerpo y el estilo pedidos.</summary>
    /// <remarks>
    /// Arial en vez de Helvetica: no son la misma fuente, pero son metricamente compatibles
    /// —es el sustituto de toda la vida— y Arial viene instalada en cualquier Windows. Al no
    /// incrustar mas que el subconjunto de caracteres usado, el peso que anade es minimo.
    /// </remarks>
    internal static XFont Fuente(double tamanoPuntos, bool negrita = false, bool cursiva = false)
    {
        var estilo = (negrita, cursiva) switch
        {
            (true, true) => XFontStyleEx.BoldItalic,
            (true, false) => XFontStyleEx.Bold,
            (false, true) => XFontStyleEx.Italic,
            _ => XFontStyleEx.Regular,
        };

        return new XFont("Arial", tamanoPuntos, estilo);
    }

    private static double PtMm(double mm) => mm * PuntosPorMm;

    private static double AMm(double puntos) => puntos / PuntosPorMm;

    /// <summary>Escribe un texto con su esquina superior izquierda en el punto dado.</summary>
    private static void Texto(XGraphics gfx, string? texto, double xMm, double yMm, XFont fuente)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return;
        }

        var alto = fuente.GetHeight() * 2.0;
        var rect = new XRect(PtMm(xMm), PtMm(yMm), gfx.PageSize.Width - PtMm(xMm), alto);
        gfx.DrawString(texto, fuente, XBrushes.Black, rect, XStringFormats.TopLeft);
    }

    /// <summary>Escribe un texto alineado dentro de una casilla, recortandolo si no cabe.</summary>
    /// <remarks>
    /// Recortar con puntos suspensivos es preferible a dejar que se salga: en una etiqueta lo
    /// que se sale se imprime encima de la etiqueta de al lado y estropea las dos. Con las
    /// columnas ya medidas por <see cref="Encajar"/> esto casi nunca entra en juego; queda como
    /// red de seguridad para datos fuera de lo normal.
    /// </remarks>
    private static void TextoEnCasilla(
        XGraphics gfx,
        string? texto,
        double xMm,
        double yMm,
        double anchoMm,
        XFont fuente,
        Alineacion alineacion = Alineacion.Izquierda,
        double gris = 0.0)
    {
        if (string.IsNullOrEmpty(texto) || anchoMm <= 0)
        {
            return;
        }

        var cabe = Recortar(gfx, texto, fuente, anchoMm);
        if (cabe.Length == 0)
        {
            return;
        }

        var formato = alineacion switch
        {
            Alineacion.Centro => XStringFormats.TopCenter,
            Alineacion.Derecha => XStringFormats.TopRight,
            _ => XStringFormats.TopLeft,
        };

        var alto = fuente.GetHeight() * 2.0;
        var rect = new XRect(PtMm(xMm), PtMm(yMm), PtMm(anchoMm), alto);
        var pincel = gris <= 0.0 ? XBrushes.Black : new XSolidBrush(XColor.FromGrayScale(gris));
        gfx.DrawString(cabe, fuente, pincel, rect, formato);
    }

    /// <summary>Recorta el texto para que quepa en un ancho dado, poniendo puntos suspensivos.</summary>
    internal static string Recortar(XGraphics gfx, string texto, XFont fuente, double anchoMm)
    {
        var anchoPt = PtMm(anchoMm);
        if (gfx.MeasureString(texto, fuente).Width <= anchoPt)
        {
            return texto;
        }

        const string puntos = "…";
        var anchoPuntosPt = gfx.MeasureString(puntos, fuente).Width;
        if (anchoPuntosPt > anchoPt)
        {
            return string.Empty;
        }

        var corte = texto.Length;
        while (corte > 0 && gfx.MeasureString(texto[..corte], fuente).Width + anchoPuntosPt > anchoPt)
        {
            corte--;
        }

        return corte == 0 ? string.Empty : texto[..corte] + puntos;
    }

    /// <summary>Dibuja una linea recta.</summary>
    private static void Linea(
        XGraphics gfx, double x1Mm, double y1Mm, double x2Mm, double y2Mm,
        double grosorMm = 0.2, double gris = 0.0)
    {
        var pluma = new XPen(XColor.FromGrayScale(gris), PtMm(grosorMm));
        gfx.DrawLine(pluma, PtMm(x1Mm), PtMm(y1Mm), PtMm(x2Mm), PtMm(y2Mm));
    }

    /// <summary>Dibuja el contorno de un rectangulo.</summary>
    private static void Rectangulo(
        XGraphics gfx, double xMm, double yMm, double anchoMm, double altoMm,
        double grosorMm = 0.2, double gris = 0.0)
    {
        var pluma = new XPen(XColor.FromGrayScale(gris), PtMm(grosorMm));
        gfx.DrawRectangle(pluma, PtMm(xMm), PtMm(yMm), PtMm(anchoMm), PtMm(altoMm));
    }
}
