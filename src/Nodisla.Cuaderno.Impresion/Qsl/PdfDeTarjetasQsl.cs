using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Nodisla.Cuaderno.Impresion.Qsl;

/// <summary>
/// Pone las tarjetas ya dibujadas (PNG o JPG) en un PDF para imprimirlas o mandarlas a imprenta.
/// </summary>
/// <remarks>
/// <para>
/// <b>El PDF lleva la MISMA imagen que el correo</b>, dibujada a 300 ppp, y no un segundo dibujo
/// hecho con otra biblioteca. Asi lo que se ve en el editor, lo que recibe el corresponsal y lo
/// que sale por la impresora no pueden diferir ni en un milimetro ni en una tipografia.
/// </para>
/// <para>
/// Dos formas: <see cref="EnHojaA4"/> reparte las tarjetas en A4 con marcas de corte (dos
/// apaisadas o cuatro verticales por hoja), para la impresora de casa; <see cref="UnaPorPagina"/>
/// hace cada pagina del tamaño exacto de la tarjeta, que es lo que piden las imprentas.
/// </para>
/// </remarks>
public static class PdfDeTarjetasQsl
{
    private const double PuntosPorMm = 72.0 / 25.4;
    private const double AnchoA4Mm = 210.0;
    private const double AltoA4Mm = 297.0;
    private const double MargenMm = 10.0;
    private const double LargoDeMarcaMm = 5.0;

    /// <summary>Cada tarjeta en su pagina, del tamaño exacto de la tarjeta.</summary>
    /// <param name="imagenes">Las tarjetas, en PNG o JPG.</param>
    /// <param name="anchoMm">Ancho de la tarjeta.</param>
    /// <param name="altoMm">Alto de la tarjeta.</param>
    /// <param name="nombre">Nombre de fichero sugerido.</param>
    /// <returns>El PDF.</returns>
    public static Impreso UnaPorPagina(IReadOnlyList<byte[]> imagenes, double anchoMm, double altoMm, string nombre = "tarjetas-qsl.pdf")
    {
        Validar(imagenes, anchoMm, altoMm);
        using var documento = NuevoDocumento();
        foreach (var bytes in imagenes)
        {
            var pagina = documento.AddPage();
            pagina.Width = XUnit.FromMillimeter(anchoMm);
            pagina.Height = XUnit.FromMillimeter(altoMm);
            using var gfx = XGraphics.FromPdfPage(pagina);
            Pintar(gfx, bytes, 0, 0, anchoMm, altoMm);
        }

        var paginas = documento.PageCount;
        return new Impreso(Bytes(documento), "application/pdf", paginas, nombre);
    }

    /// <summary>Las tarjetas repartidas en hojas A4, con marcas de corte.</summary>
    /// <param name="imagenes">Las tarjetas, en PNG o JPG.</param>
    /// <param name="anchoMm">Ancho de la tarjeta.</param>
    /// <param name="altoMm">Alto de la tarjeta.</param>
    /// <param name="nombre">Nombre de fichero sugerido.</param>
    /// <returns>El PDF.</returns>
    public static Impreso EnHojaA4(IReadOnlyList<byte[]> imagenes, double anchoMm, double altoMm, string nombre = "tarjetas-qsl.pdf")
    {
        Validar(imagenes, anchoMm, altoMm);
        var (columnas, filas) = Reparto(anchoMm, altoMm);
        var porHoja = columnas * filas;

        // Centradas en la hoja: la mayoria de impresoras no llegan igual a los cuatro bordes y
        // centrar reparte el error en vez de cargarlo todo en un lado.
        var x0 = (AnchoA4Mm - (columnas * anchoMm)) / 2;
        var y0 = (AltoA4Mm - (filas * altoMm)) / 2;

        using var documento = NuevoDocumento();
        XGraphics? gfx = null;
        try
        {
            for (var i = 0; i < imagenes.Count; i++)
            {
                var casilla = i % porHoja;
                if (casilla == 0)
                {
                    gfx?.Dispose();
                    var pagina = documento.AddPage();
                    pagina.Width = XUnit.FromMillimeter(AnchoA4Mm);
                    pagina.Height = XUnit.FromMillimeter(AltoA4Mm);
                    gfx = XGraphics.FromPdfPage(pagina);
                }

                var x = x0 + ((casilla % columnas) * anchoMm);
                var y = y0 + ((casilla / columnas) * altoMm);
                Pintar(gfx!, imagenes[i], x, y, anchoMm, altoMm);
                MarcasDeCorte(gfx!, x, y, anchoMm, altoMm);
            }
        }
        finally
        {
            gfx?.Dispose();
        }

        var paginas = documento.PageCount;
        return new Impreso(Bytes(documento), "application/pdf", paginas, nombre);
    }

    /// <summary>Cuantas tarjetas caben por hoja A4 sin tocar el margen.</summary>
    /// <param name="anchoMm">Ancho de la tarjeta.</param>
    /// <param name="altoMm">Alto de la tarjeta.</param>
    /// <returns>Columnas y filas; al menos una de cada.</returns>
    public static (int Columnas, int Filas) Reparto(double anchoMm, double altoMm) =>
        (Math.Max(1, (int)((AnchoA4Mm - (2 * MargenMm)) / anchoMm)),
         Math.Max(1, (int)((AltoA4Mm - (2 * MargenMm)) / altoMm)));

    private static void Validar(IReadOnlyList<byte[]> imagenes, double anchoMm, double altoMm)
    {
        ArgumentNullException.ThrowIfNull(imagenes);
        if (imagenes.Count == 0) throw new ArgumentException("No hay ninguna tarjeta que poner en el PDF.", nameof(imagenes));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(anchoMm);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(altoMm);
    }

    private static PdfDocument NuevoDocumento()
    {
        var documento = new PdfDocument();
        documento.Info.Title = "Tarjetas QSL — Cuaderno NODISLA";
        documento.Info.Creator = "Cuaderno NODISLA";
        return documento;
    }

    private static void Pintar(XGraphics gfx, byte[] bytes, double xMm, double yMm, double anchoMm, double altoMm)
    {
        using var flujo = new MemoryStream(bytes, writable: false);
        using var imagen = XImage.FromStream(flujo);
        gfx.DrawImage(imagen, xMm * PuntosPorMm, yMm * PuntosPorMm, anchoMm * PuntosPorMm, altoMm * PuntosPorMm);
    }

    private static void MarcasDeCorte(XGraphics gfx, double x, double y, double ancho, double alto)
    {
        // Las marcas van FUERA de la tarjeta: dentro, un corte un poco torcido las dejaria a la vista.
        var lapiz = new XPen(XColors.Gray, 0.3);
        var s = 1.0;
        void Linea(double x1, double y1, double x2, double y2) =>
            gfx.DrawLine(lapiz, x1 * PuntosPorMm, y1 * PuntosPorMm, x2 * PuntosPorMm, y2 * PuntosPorMm);

        foreach (var (cx, cy, dx, dy) in new[] { (x, y, -1, -1), (x + ancho, y, 1, -1), (x, y + alto, -1, 1), (x + ancho, y + alto, 1, 1) })
        {
            Linea(cx + (dx * s), cy, cx + (dx * (s + LargoDeMarcaMm)), cy);
            Linea(cx, cy + (dy * s), cx, cy + (dy * (s + LargoDeMarcaMm)));
        }
    }

    private static byte[] Bytes(PdfDocument documento)
    {
        using var salida = new MemoryStream();
        documento.Save(salida, closeStream: false);
        return salida.ToArray();
    }
}
