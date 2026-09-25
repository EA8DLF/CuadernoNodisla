namespace Nodisla.Cuaderno.Impresion.Pdf;

/// <summary>
/// Una de las fuentes que todo lector de PDF trae de serie.
/// </summary>
/// <remarks>
/// Se usan solo las catorce fuentes estandar del formato y no se incrusta ninguna. Asi el PDF
/// pesa unos pocos kilobytes, se abre igual en cualquier maquina y no hay que arrastrar una
/// licencia tipografica, que es un problema real en cuanto se quiere repartir el programa.
/// El precio es que no hay tipografias de fantasia, que en una etiqueta de QSL no hacen falta.
/// </remarks>
public enum FamiliaDeFuente
{
    /// <summary>Helvetica, la de las etiquetas.</summary>
    Helvetica = 0,

    /// <summary>Helvetica en negrita.</summary>
    HelveticaNegrita,

    /// <summary>Helvetica en cursiva.</summary>
    HelveticaCursiva,

    /// <summary>Helvetica en negrita y cursiva.</summary>
    HelveticaNegritaCursiva,

    /// <summary>Courier, de paso fijo, para columnas de numeros que tienen que cuadrar.</summary>
    Courier,

    /// <summary>Courier en negrita.</summary>
    CourierNegrita,
}

/// <summary>
/// Mide el texto de las fuentes estandar para poder centrar, alinear a la derecha y recortar.
/// </summary>
/// <remarks>
/// Sin anchos de caracter no hay forma de centrar nada ni de saber si un indicativo largo se
/// sale de su casilla. Las tablas son las de los ficheros de metricas de Adobe para las
/// catorce fuentes estandar, en milesimas de la altura tipografica.
/// <para>
/// Las letras acentuadas toman el ancho de su letra base, que es exacto en estas fuentes
/// (la tilde no ensancha la caja). Para los signos que no son letras hay tabla propia.
/// </para>
/// </remarks>
public static class MetricaDeFuente
{
    // Anchos de los caracteres 32 a 126 en milesimas de em.
    private static readonly short[] AnchosHelvetica =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];

    private static readonly short[] AnchosHelveticaNegrita =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    /// <summary>Nombre de la fuente tal y como lo espera el fichero PDF.</summary>
    /// <param name="familia">La familia.</param>
    public static string NombrePdf(FamiliaDeFuente familia) => familia switch
    {
        FamiliaDeFuente.Helvetica => "Helvetica",
        FamiliaDeFuente.HelveticaNegrita => "Helvetica-Bold",
        FamiliaDeFuente.HelveticaCursiva => "Helvetica-Oblique",
        FamiliaDeFuente.HelveticaNegritaCursiva => "Helvetica-BoldOblique",
        FamiliaDeFuente.Courier => "Courier",
        FamiliaDeFuente.CourierNegrita => "Courier-Bold",
        _ => "Helvetica",
    };

    /// <summary>Elige la familia a partir de si va en negrita o en cursiva.</summary>
    /// <param name="negrita">Negrita.</param>
    /// <param name="cursiva">Cursiva.</param>
    /// <param name="pasoFijo">Paso fijo (Courier) en vez de Helvetica.</param>
    public static FamiliaDeFuente Elegir(bool negrita, bool cursiva, bool pasoFijo = false)
    {
        if (pasoFijo)
        {
            return negrita ? FamiliaDeFuente.CourierNegrita : FamiliaDeFuente.Courier;
        }

        return (negrita, cursiva) switch
        {
            (true, true) => FamiliaDeFuente.HelveticaNegritaCursiva,
            (true, false) => FamiliaDeFuente.HelveticaNegrita,
            (false, true) => FamiliaDeFuente.HelveticaCursiva,
            _ => FamiliaDeFuente.Helvetica,
        };
    }

    /// <summary>Anchura de un texto, en puntos tipograficos.</summary>
    /// <param name="texto">El texto.</param>
    /// <param name="familia">La fuente.</param>
    /// <param name="tamanoPuntos">Tamano en puntos.</param>
    public static double AnchuraPuntos(string? texto, FamiliaDeFuente familia, double tamanoPuntos)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return 0;
        }

        var milesimas = 0;
        foreach (var c in texto)
        {
            milesimas += AnchoDe(c, familia);
        }

        return milesimas * tamanoPuntos / 1000.0;
    }

    /// <summary>Anchura de un texto, en milimetros.</summary>
    /// <param name="texto">El texto.</param>
    /// <param name="familia">La fuente.</param>
    /// <param name="tamanoPuntos">Tamano en puntos.</param>
    public static double AnchuraMm(string? texto, FamiliaDeFuente familia, double tamanoPuntos) =>
        AnchuraPuntos(texto, familia, tamanoPuntos) * 25.4 / 72.0;

    /// <summary>
    /// Recorta el texto para que quepa en un ancho dado, poniendo puntos suspensivos.
    /// </summary>
    /// <param name="texto">El texto.</param>
    /// <param name="familia">La fuente.</param>
    /// <param name="tamanoPuntos">Tamano en puntos.</param>
    /// <param name="anchoMm">Ancho disponible en milimetros.</param>
    /// <returns>El texto que cabe.</returns>
    /// <remarks>
    /// Recortar es preferible a dejar que se salga: en una etiqueta lo que se sale se imprime
    /// encima de la etiqueta de al lado y estropea las dos.
    /// </remarks>
    public static string Recortar(string? texto, FamiliaDeFuente familia, double tamanoPuntos, double anchoMm)
    {
        if (string.IsNullOrEmpty(texto) || anchoMm <= 0)
        {
            return string.Empty;
        }

        if (AnchuraMm(texto, familia, tamanoPuntos) <= anchoMm)
        {
            return texto;
        }

        const string puntos = "…";
        var anchoPuntos = AnchuraMm(puntos, familia, tamanoPuntos);
        if (anchoPuntos > anchoMm)
        {
            return string.Empty;
        }

        var corte = texto.Length;
        while (corte > 0 && AnchuraMm(texto[..corte], familia, tamanoPuntos) + anchoPuntos > anchoMm)
        {
            corte--;
        }

        return corte == 0 ? string.Empty : texto[..corte] + puntos;
    }

    private static int AnchoDe(char c, FamiliaDeFuente familia)
    {
        if (familia is FamiliaDeFuente.Courier or FamiliaDeFuente.CourierNegrita)
        {
            return 600;
        }

        var negrita = familia is FamiliaDeFuente.HelveticaNegrita
            or FamiliaDeFuente.HelveticaNegritaCursiva;
        var tabla = negrita ? AnchosHelveticaNegrita : AnchosHelvetica;

        if (c is >= ' ' and <= '~')
        {
            return tabla[c - ' '];
        }

        var equivalente = LetraBase(c);
        if (equivalente != '\0')
        {
            return tabla[equivalente - ' '];
        }

        return AnchoDeSigno(c, negrita);
    }

    /// <summary>
    /// Letra sin tilde equivalente a una letra acentuada. En Helvetica la caja de la letra
    /// acentuada mide exactamente lo mismo que la de su letra base.
    /// </summary>
    private static char LetraBase(char c) => c switch
    {
        'À' or 'Á' or 'Â' or 'Ã' or 'Ä' or 'Å' => 'A',
        'Ç' => 'C',
        'È' or 'É' or 'Ê' or 'Ë' => 'E',
        'Ì' or 'Í' or 'Î' or 'Ï' => 'I',
        'Ñ' => 'N',
        'Ò' or 'Ó' or 'Ô' or 'Õ' or 'Ö' or 'Ø' => 'O',
        'Ù' or 'Ú' or 'Û' or 'Ü' => 'U',
        'Ý' or 'Ÿ' => 'Y',
        'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' => 'a',
        'ç' => 'c',
        'è' or 'é' or 'ê' or 'ë' => 'e',
        'ì' or 'í' or 'î' or 'ï' => 'i',
        'ñ' => 'n',
        'ò' or 'ó' or 'ô' or 'õ' or 'ö' or 'ø' => 'o',
        'ù' or 'ú' or 'û' or 'ü' => 'u',
        'ý' or 'ÿ' => 'y',
        'ß' => 'b',
        _ => '\0',
    };

    private static int AnchoDeSigno(char c, bool negrita) => c switch
    {
        '¡' => 333,
        '¿' => negrita ? 611 : 611,
        '«' or '»' => 556,
        '°' => 400,
        'ª' => 370,
        'º' => 365,
        '©' or '®' => 737,
        '·' => 278,
        '€' => 556,
        '–' => 556,
        '—' => 1000,
        '“' or '”' => negrita ? 500 : 333,
        '‘' or '’' => negrita ? 278 : 222,
        '…' => 1000,
        '±' => 584,
        'µ' => 556,
        '¢' or '£' or '¥' => 556,
        _ => 556,
    };
}
