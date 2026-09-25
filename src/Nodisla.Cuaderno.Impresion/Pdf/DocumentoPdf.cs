using System.Globalization;
using System.Text;

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

/// <summary>Tamano de pagina en milimetros.</summary>
/// <param name="AnchoMm">Ancho.</param>
/// <param name="AltoMm">Alto.</param>
public readonly record struct TamanoDePagina(double AnchoMm, double AltoMm)
{
    /// <summary>A4 vertical, 210 x 297 mm. Lo normal en Europa.</summary>
    public static TamanoDePagina A4 => new(210.0, 297.0);

    /// <summary>A4 apaisado.</summary>
    public static TamanoDePagina A4Apaisado => new(297.0, 210.0);

    /// <summary>Carta estadounidense, 215,9 x 279,4 mm. La usan las etiquetas Avery de EE. UU.</summary>
    public static TamanoDePagina Carta => new(215.9, 279.4);

    /// <summary>Carta apaisada.</summary>
    public static TamanoDePagina CartaApaisada => new(279.4, 215.9);
}

/// <summary>
/// Escribe ficheros PDF sin depender de ninguna biblioteca de terceros.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por que se escribe a mano.</b> Lo que hace falta aqui —texto en fuentes estandar, lineas
/// y recuadros colocados al milimetro— es la parte del formato PDF que cabe en un fichero. El
/// original usa iText 7, que es AGPL: incrustarlo obligaria a publicar el programa entero bajo
/// esa licencia. Escribirlo evita la licencia, evita una dependencia mas que actualizar y deja
/// el control exacto de las coordenadas, que es justo lo que decide si una etiqueta cuadra con
/// el papel.
/// </para>
/// <para>
/// <b>Lo que no hace.</b> No incrusta fuentes, no dibuja imagenes ni codigos QR y no comprime
/// los flujos. Si alguna vez hace falta cualquiera de esas cosas, lo sensato es cambiar a una
/// biblioteca con licencia permisiva antes que seguir ampliando esto.
/// </para>
/// </remarks>
public sealed class DocumentoPdf
{
    private const double PuntosPorMm = 72.0 / 25.4;

    private readonly List<PaginaPdf> _paginas = [];
    private readonly List<FamiliaDeFuente> _fuentes = [];

    /// <summary>Titulo del documento, el que ensena el lector en la barra.</summary>
    public string Titulo { get; set; } = "Cuaderno NODISLA";

    /// <summary>Las paginas escritas hasta ahora.</summary>
    public IReadOnlyList<PaginaPdf> Paginas => _paginas;

    /// <summary>Anade una pagina nueva y la devuelve para pintar en ella.</summary>
    /// <param name="tamano">Tamano de la pagina.</param>
    /// <returns>La pagina recien creada.</returns>
    public PaginaPdf NuevaPagina(TamanoDePagina tamano)
    {
        var pagina = new PaginaPdf(this, tamano);
        _paginas.Add(pagina);
        return pagina;
    }

    /// <summary>Escribe el documento completo en un flujo.</summary>
    /// <param name="destino">Flujo de salida.</param>
    /// <exception cref="InvalidOperationException">No hay ninguna pagina.</exception>
    public void Escribir(Stream destino)
    {
        ArgumentNullException.ThrowIfNull(destino);

        if (_paginas.Count == 0)
        {
            throw new InvalidOperationException("No se puede escribir un PDF sin páginas.");
        }

        // 1 catalogo, 2 arbol de paginas, despues una fuente por familia usada y, por cada
        // pagina, el objeto de pagina y su flujo de contenido.
        var primeraFuente = 3;
        var primeraPagina = primeraFuente + _fuentes.Count;

        var cuerpo = new MemoryStream();
        var posiciones = new List<long>();

        void Objeto(int numero, string contenido)
        {
            while (posiciones.Count < numero)
            {
                posiciones.Add(0);
            }

            posiciones[numero - 1] = cuerpo.Position;
            Escribir(cuerpo, $"{numero} 0 obj\n{contenido}\nendobj\n");
        }

        Escribir(cuerpo, "%PDF-1.4\n%âãÏÓ\n");

        var hijos = string.Join(" ",
            Enumerable.Range(0, _paginas.Count).Select(i => $"{primeraPagina + (i * 2)} 0 R"));

        Objeto(1, "<< /Type /Catalog /Pages 2 0 R >>");
        Objeto(2, $"<< /Type /Pages /Kids [ {hijos} ] /Count {_paginas.Count} >>");

        for (var i = 0; i < _fuentes.Count; i++)
        {
            Objeto(primeraFuente + i,
                "<< /Type /Font /Subtype /Type1 "
                + $"/BaseFont /{MetricaDeFuente.NombrePdf(_fuentes[i])} "
                + "/Encoding /WinAnsiEncoding >>");
        }

        var recursos = string.Join(" ",
            Enumerable.Range(0, _fuentes.Count).Select(i => $"/F{i} {primeraFuente + i} 0 R"));

        for (var i = 0; i < _paginas.Count; i++)
        {
            var pagina = _paginas[i];
            var numeroPagina = primeraPagina + (i * 2);
            var numeroContenido = numeroPagina + 1;

            var ancho = (pagina.Tamano.AnchoMm * PuntosPorMm).ToString("0.###", CultureInfo.InvariantCulture);
            var alto = (pagina.Tamano.AltoMm * PuntosPorMm).ToString("0.###", CultureInfo.InvariantCulture);

            Objeto(numeroPagina,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [ 0 0 {ancho} {alto} ] "
                + $"/Resources << /Font << {recursos} >> >> /Contents {numeroContenido} 0 R >>");

            var bytes = CodificarWinAnsi(pagina.Contenido);
            posiciones.Add(0);
            posiciones[numeroContenido - 1] = cuerpo.Position;
            Escribir(cuerpo, $"{numeroContenido} 0 obj\n<< /Length {bytes.Length} >>\nstream\n");
            cuerpo.Write(bytes, 0, bytes.Length);
            Escribir(cuerpo, "\nendstream\nendobj\n");
        }

        var inicioXref = cuerpo.Position;
        var total = posiciones.Count + 1;
        var xref = new StringBuilder();
        xref.Append(CultureInfo.InvariantCulture, $"xref\n0 {total}\n0000000000 65535 f \n");
        foreach (var posicion in posiciones)
        {
            xref.Append(CultureInfo.InvariantCulture, $"{posicion:D10} 00000 n \n");
        }

        xref.Append(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {total} /Root 1 0 R /Info << /Title ({Escapar(Titulo)}) "
            + "/Producer (Cuaderno NODISLA) >> >>\n");
        xref.Append(CultureInfo.InvariantCulture, $"startxref\n{inicioXref}\n%%EOF\n");
        Escribir(cuerpo, xref.ToString());

        cuerpo.Position = 0;
        cuerpo.CopyTo(destino);
    }

    /// <summary>Escribe el documento en un fichero.</summary>
    /// <param name="ruta">Ruta del fichero.</param>
    public void Guardar(string ruta)
    {
        var carpeta = Path.GetDirectoryName(ruta);
        if (!string.IsNullOrEmpty(carpeta))
        {
            Directory.CreateDirectory(carpeta);
        }

        using var fichero = File.Create(ruta);
        Escribir(fichero);
    }

    /// <summary>Devuelve el documento como una tira de bytes.</summary>
    /// <remarks>
    /// Es lo que se le pasa a la vista previa. <b>La vista previa y la impresion son el mismo
    /// fichero</b>: no hay un dibujo para la pantalla y otro para el papel, que es donde nacen
    /// las sorpresas.
    /// </remarks>
    public byte[] ABytes()
    {
        using var memoria = new MemoryStream();
        Escribir(memoria);
        return memoria.ToArray();
    }

    internal int IndiceDeFuente(FamiliaDeFuente familia)
    {
        var indice = _fuentes.IndexOf(familia);
        if (indice >= 0)
        {
            return indice;
        }

        _fuentes.Add(familia);
        return _fuentes.Count - 1;
    }

    internal static string Escapar(string texto) => texto
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("(", "\\(", StringComparison.Ordinal)
        .Replace(")", "\\)", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

    private static void Escribir(Stream destino, string texto)
    {
        var bytes = CodificarWinAnsi(texto);
        destino.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Pasa el texto a WinAnsi, que es la codificacion de las fuentes estandar del PDF.
    /// </summary>
    /// <remarks>
    /// Se hace a mano y no con <c>Encoding.GetEncoding(1252)</c> porque esa pagina de codigos
    /// no viene en .NET moderno sin anadir un paquete. De 0xA0 a 0xFF, WinAnsi y Latin-1 son
    /// iguales; lo unico que hay que traducir a mano es la franja de 0x80 a 0x9F, donde viven
    /// las comillas tipograficas, la raya y el simbolo del euro.
    /// </remarks>
    private static byte[] CodificarWinAnsi(string texto)
    {
        var bytes = new byte[texto.Length];
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            bytes[i] = c switch
            {
                <= (char)0xFF => (byte)c,
                '€' => 0x80,
                '‚' => 0x82,
                'ƒ' => 0x83,
                '„' => 0x84,
                '…' => 0x85,
                '†' => 0x86,
                '‡' => 0x87,
                'ˆ' => 0x88,
                '‰' => 0x89,
                'Š' => 0x8A,
                '‹' => 0x8B,
                'Œ' => 0x8C,
                'Ž' => 0x8E,
                '‘' => 0x91,
                '’' => 0x92,
                '“' => 0x93,
                '”' => 0x94,
                '•' => 0x95,
                '–' => 0x96,
                '—' => 0x97,
                '˜' => 0x98,
                '™' => 0x99,
                'š' => 0x9A,
                '›' => 0x9B,
                'œ' => 0x9C,
                'ž' => 0x9E,
                'Ÿ' => 0x9F,
                _ => (byte)'?',
            };
        }

        return bytes;
    }
}
