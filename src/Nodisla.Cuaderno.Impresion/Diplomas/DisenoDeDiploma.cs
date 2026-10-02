using System.Text.Json.Serialization;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Impresion.Plantillas;
using Nodisla.Cuaderno.Impresion.Qsl;

namespace Nodisla.Cuaderno.Impresion.Diplomas;

/// <summary>Tamaño de papel del diploma.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PapelDeDiploma>))]
public enum PapelDeDiploma
{
    /// <summary>A4, 210 × 297 mm.</summary>
    A4 = 0,

    /// <summary>Carta (US Letter), 215,9 × 279,4 mm.</summary>
    Carta,
}

/// <summary>Como se dibuja la orla del diploma.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EstiloDeMarco>))]
public enum EstiloDeMarco
{
    /// <summary>Sin orla.</summary>
    Ninguno = 0,

    /// <summary>Un filete fino.</summary>
    Filete,

    /// <summary>Dos filetes paralelos, el de fuera mas grueso.</summary>
    DobleFilete,

    /// <summary>Doble filete con cuadros en las esquinas, en dos colores.</summary>
    Clasico,

    /// <summary>Filete fino con esquinas en escuadra: sobrio y moderno.</summary>
    Escuadras,
}

/// <summary>Para que es una imagen del diploma.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<UsoDeImagen>))]
public enum UsoDeImagen
{
    /// <summary>Logotipo del club o de la estacion.</summary>
    Logo = 0,

    /// <summary>Firma escaneada del gestor.</summary>
    Firma,

    /// <summary>Sello, adorno o cualquier otra imagen.</summary>
    Adorno,
}

/// <summary>La orla del diploma.</summary>
public sealed class MarcoDeDiploma
{
    /// <summary>Estilo.</summary>
    public EstiloDeMarco Estilo { get; set; } = EstiloDeMarco.Clasico;

    /// <summary>Color principal, <c>#RRGGBB</c>.</summary>
    public string Color { get; set; } = "#1B3A5C";

    /// <summary>Color del filete interior y de los adornos.</summary>
    public string ColorSecundario { get; set; } = "#B08D57";

    /// <summary>Distancia del borde del papel a la orla, en milimetros.</summary>
    public double MargenMm { get; set; } = 10;

    /// <summary>Grosor del filete principal, en milimetros.</summary>
    public double GrosorMm { get; set; } = 1.2;

    /// <summary>Copia independiente.</summary>
    /// <returns>La copia.</returns>
    public MarcoDeDiploma Copiar() => (MarcoDeDiploma)MemberwiseClone();
}

/// <summary>Una imagen colocada en el diploma: logo, firma o adorno.</summary>
/// <remarks>
/// Ocupa una caja en milimetros y la imagen se encaja dentro sin deformarse. Sin fichero no se
/// dibuja nada (salvo la linea de firma, si la lleva): el hueco queda preparado en la plantilla
/// para que cada operador ponga su logo o su firma.
/// </remarks>
public sealed class ImagenDeDiploma
{
    /// <summary>Nombre con el que sale en la lista del editor.</summary>
    public string Nombre { get; set; } = Textos.T("Servicios.Impresion.Diploma.Imagen");

    /// <summary>Para que es.</summary>
    public UsoDeImagen Uso { get; set; }

    /// <summary>Nombre del fichero, junto al JSON. Vacio: hueco sin imagen.</summary>
    public string? Fichero { get; set; }

    /// <summary>Izquierda de la caja, mm.</summary>
    public double XMm { get; set; }

    /// <summary>Arriba de la caja, mm.</summary>
    public double YMm { get; set; }

    /// <summary>Ancho de la caja, mm.</summary>
    public double AnchoMm { get; set; } = 30;

    /// <summary>Alto de la caja, mm.</summary>
    public double AltoMm { get; set; } = 30;

    /// <summary>Opacidad, de 0 a 1 (un sello de agua va a 0,15).</summary>
    public double Opacidad { get; set; } = 1;

    /// <summary>Dibujar una linea fina debajo de la caja, para firmar encima.</summary>
    public bool LineaDeFirma { get; set; }

    /// <summary>Color de la linea de firma.</summary>
    public string ColorDeLinea { get; set; } = "#555555";

    /// <summary>Se pinta o no.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Copia independiente.</summary>
    /// <returns>La copia.</returns>
    public ImagenDeDiploma Copiar() => (ImagenDeDiploma)MemberwiseClone();
}

/// <summary>Una columna de la tabla de referencias o contactos.</summary>
public sealed class ColumnaDeTabla
{
    /// <summary>Cabecera.</summary>
    public string Titulo { get; set; } = string.Empty;

    /// <summary>Lo que se pone en cada fila, con las variables de la fila (<c>{referencia}</c>…).</summary>
    public string Texto { get; set; } = string.Empty;

    /// <summary>Peso del ancho respecto a las demas columnas.</summary>
    public double Ancho { get; set; } = 1;

    /// <summary>Alineacion dentro de la celda.</summary>
    public AlineacionDeCampo Alineacion { get; set; }

    /// <summary>Copia independiente.</summary>
    /// <returns>La copia.</returns>
    public ColumnaDeTabla Copiar() => (ColumnaDeTabla)MemberwiseClone();
}

/// <summary>La tabla que justifica el diploma: referencias o contactos.</summary>
/// <remarks>
/// Se reparte en uno o varios bloques uno al lado de otro. Lo que no cabe en la caja sale en
/// hojas de anexo del PDF, con la misma orla y la tabla ocupando la hoja entera.
/// </remarks>
public sealed class TablaDeDiploma
{
    /// <summary>Se pinta o no.</summary>
    public bool Visible { get; set; }

    /// <summary>Titulo encima de la tabla (con variables). Vacio: sin titulo.</summary>
    public string Titulo { get; set; } = "Referencias confirmadas";

    /// <summary>Izquierda de la caja, mm.</summary>
    public double XMm { get; set; } = 30;

    /// <summary>Arriba de la caja, mm.</summary>
    public double YMm { get; set; } = 120;

    /// <summary>Ancho de la caja, mm.</summary>
    public double AnchoMm { get; set; } = 150;

    /// <summary>Alto de la caja, mm.</summary>
    public double AltoMm { get; set; } = 60;

    /// <summary>Bloques de columnas uno al lado de otro (1 a 4).</summary>
    public int Bloques { get; set; } = 1;

    /// <summary>Tipografia.</summary>
    public string Fuente { get; set; } = "Segoe UI";

    /// <summary>Cuerpo de letra, en puntos.</summary>
    public double TamanoPt { get; set; } = 8;

    /// <summary>Color del texto.</summary>
    public string ColorDeTexto { get; set; } = "#222222";

    /// <summary>Color de la cabecera y del titulo.</summary>
    public string ColorDeCabecera { get; set; } = "#1B3A5C";

    /// <summary>Color de las lineas.</summary>
    public string ColorDeLineas { get; set; } = "#B8B8B8";

    /// <summary>Anexar hojas con lo que no quepa.</summary>
    public bool Anexo { get; set; } = true;

    /// <summary>Las columnas.</summary>
    public List<ColumnaDeTabla> Columnas { get; set; } =
    [
        new() { Titulo = "Referencia", Texto = "{referencia}", Ancho = 1 },
        new() { Titulo = "Nombre", Texto = "{nombrereferencia}", Ancho = 2.2 },
        new() { Titulo = "Indicativo", Texto = "{indicativo}", Ancho = 1.1 },
        new() { Titulo = "Fecha", Texto = "{fecha}", Ancho = 1 },
        new() { Titulo = "Banda", Texto = "{banda}", Ancho = 0.7 },
        new() { Titulo = "Modo", Texto = "{modo}", Ancho = 0.7 },
    ];

    /// <summary>Copia independiente.</summary>
    /// <returns>La copia.</returns>
    public TablaDeDiploma Copiar()
    {
        var copia = (TablaDeDiploma)MemberwiseClone();
        copia.Columnas = Columnas.Select(c => c.Copiar()).ToList();
        return copia;
    }
}

/// <summary>
/// Una plantilla de diploma o certificado: papel, fondo, orla, imagenes, textos y tabla.
/// </summary>
/// <remarks>
/// <para>
/// Es la hermana grande de <see cref="DisenoDeQsl"/> y comparte con ella el motor: los textos son
/// los mismos <see cref="CampoDeQsl"/> con variables entre llaves, el fondo se coloca igual
/// (<see cref="AjusteDeFondo"/>) y se guarda en el mismo almacen generico
/// (<see cref="AlmacenDePlantillas{T}"/>). Las medidas van en milimetros y la letra en puntos.
/// </para>
/// <para>
/// El numero de diploma lo pone el historial de emitidos, correlativo por <see cref="Serie"/>:
/// varias plantillas pueden compartir numeracion si comparten serie.
/// </para>
/// </remarks>
public sealed class DisenoDeDiploma : IPlantillaConImagenes
{
    /// <summary>Medidas del papel, en milimetros, en vertical.</summary>
    /// <param name="papel">El papel.</param>
    /// <returns>Ancho y alto en vertical.</returns>
    public static (double Ancho, double Alto) Medidas(PapelDeDiploma papel) =>
        papel == PapelDeDiploma.Carta ? (215.9, 279.4) : (210.0, 297.0);

    /// <summary>Identificador estable: el nombre de sus ficheros en disco.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Como se elige en pantalla.</summary>
    public string Nombre { get; set; } = Textos.T("Servicios.Impresion.Diploma.MiDiploma");

    /// <summary>Papel.</summary>
    public PapelDeDiploma Papel { get; set; }

    /// <summary>En vertical en vez de apaisado.</summary>
    public bool Vertical { get; set; }

    /// <summary>Ancho real, segun papel y orientacion.</summary>
    [JsonIgnore]
    public double AnchoMm => Vertical ? Medidas(Papel).Ancho : Medidas(Papel).Alto;

    /// <summary>Alto real, segun papel y orientacion.</summary>
    [JsonIgnore]
    public double AltoMm => Vertical ? Medidas(Papel).Alto : Medidas(Papel).Ancho;

    /// <summary>Color liso del papel, debajo de la imagen.</summary>
    public string ColorDeFondo { get; set; } = "#FFFFFF";

    /// <summary>Imagen de fondo u orla en imagen, junto al JSON. Vacio: sin imagen.</summary>
    public string? ImagenDeFondo { get; set; }

    /// <summary>Como se coloca la imagen de fondo.</summary>
    public AjusteDeFondo Ajuste { get; set; } = AjusteDeFondo.Rellenar;

    /// <summary>La orla dibujada.</summary>
    public MarcoDeDiploma Marco { get; set; } = new();

    /// <summary>Logos, firma y adornos.</summary>
    public List<ImagenDeDiploma> ImagenesColocadas { get; set; } = [];

    /// <summary>Los textos, con variables.</summary>
    public List<CampoDeQsl> Campos { get; set; } = [];

    /// <summary>La tabla de referencias o contactos.</summary>
    public TablaDeDiploma Tabla { get; set; } = new();

    /// <summary>Serie de la numeracion correlativa.</summary>
    public string Serie { get; set; } = "NODISLA";

    /// <summary>Lo que va delante del numero: <c>EA8-</c>, <c>2026/</c>…</summary>
    public string PrefijoDeNumero { get; set; } = string.Empty;

    /// <summary>Cifras del numero, rellenas con ceros a la izquierda.</summary>
    public int CifrasDelNumero { get; set; } = 4;

    /// <summary>Nombre del gestor que firma (variable <c>{gestor}</c>).</summary>
    public string NombreDelGestor { get; set; } = string.Empty;

    /// <summary>Nombre del diploma que se propone al emitir (variable <c>{diploma}</c>).</summary>
    public string DiplomaPorOmision { get; set; } = string.Empty;

    /// <summary>Categoria que se propone al emitir.</summary>
    public string CategoriaPorOmision { get; set; } = string.Empty;

    /// <summary>Asunto del correo, con variables.</summary>
    public string Asunto { get; set; } = Textos.F("Servicios.Impresion.Diploma.Asunto");

    /// <summary>Texto del correo, con variables.</summary>
    public string TextoDelCorreo { get; set; } = Textos.F("Servicios.Impresion.Diploma.TextoDelCorreo");

    /// <inheritdoc />
    public override string ToString() => Nombre;

    /// <summary>El numero con su prefijo y sus ceros.</summary>
    /// <param name="numero">El numero correlativo; nulo da cadena vacia.</param>
    /// <returns>El numero para imprimir.</returns>
    public string FormatearNumero(int? numero) =>
        numero is { } n
            ? PrefijoDeNumero + n.ToString("D" + Math.Clamp(CifrasDelNumero, 1, 9), System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

    /// <inheritdoc />
    public IEnumerable<string> Imagenes()
    {
        if (!string.IsNullOrWhiteSpace(ImagenDeFondo)) yield return ImagenDeFondo;
        foreach (var imagen in ImagenesColocadas)
        {
            if (!string.IsNullOrWhiteSpace(imagen.Fichero)) yield return imagen.Fichero;
        }
    }

    /// <inheritdoc />
    public void RenombrarImagen(string viejo, string nuevo)
    {
        var valor = string.IsNullOrEmpty(nuevo) ? null : nuevo;
        if (string.Equals(ImagenDeFondo, viejo, StringComparison.OrdinalIgnoreCase)) ImagenDeFondo = valor;
        foreach (var imagen in ImagenesColocadas)
        {
            if (string.Equals(imagen.Fichero, viejo, StringComparison.OrdinalIgnoreCase)) imagen.Fichero = valor;
        }
    }

    /// <summary>Copia independiente, con todo duplicado.</summary>
    /// <returns>La plantilla duplicada, con el mismo identificador.</returns>
    public DisenoDeDiploma Copiar()
    {
        var copia = (DisenoDeDiploma)MemberwiseClone();
        copia.Marco = Marco.Copiar();
        copia.ImagenesColocadas = ImagenesColocadas.Select(i => i.Copiar()).ToList();
        copia.Campos = Campos.Select(c => c.Copiar()).ToList();
        copia.Tabla = Tabla.Copiar();
        return copia;
    }

    /// <summary>
    /// Cambia papel u orientacion y recoloca todo en proporcion, para que nada se quede fuera.
    /// </summary>
    /// <param name="papel">Papel nuevo.</param>
    /// <param name="vertical">Orientacion nueva.</param>
    public void CambiarFormato(PapelDeDiploma papel, bool vertical)
    {
        var (ancho, alto) = (AnchoMm, AltoMm);
        Papel = papel;
        Vertical = vertical;
        var fx = AnchoMm / ancho;
        var fy = AltoMm / alto;
        if (Math.Abs(fx - 1) < 1e-9 && Math.Abs(fy - 1) < 1e-9) return;

        foreach (var c in Campos)
        {
            c.XMm *= fx;
            c.YMm *= fy;
            if (c.AnchoMaximoMm > 0) c.AnchoMaximoMm *= fx;
        }

        foreach (var i in ImagenesColocadas)
        {
            i.XMm *= fx;
            i.YMm *= fy;
        }

        Tabla.XMm *= fx;
        Tabla.YMm *= fy;
        Tabla.AnchoMm *= fx;
        Tabla.AltoMm *= fy;
    }
}
