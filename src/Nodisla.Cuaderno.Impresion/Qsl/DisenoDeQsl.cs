using System.Text.Json.Serialization;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Impresion.Plantillas;

namespace Nodisla.Cuaderno.Impresion.Qsl;

/// <summary>Como se coloca la imagen de fondo en la tarjeta.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AjusteDeFondo>))]
public enum AjusteDeFondo
{
    /// <summary>Cubre la tarjeta entera; lo que sobra de la foto se recorta por los lados.</summary>
    Rellenar = 0,

    /// <summary>Se ve la foto entera; lo que falta se queda con el color de fondo.</summary>
    Encajar,

    /// <summary>Se estira a la tarjeta aunque se deforme.</summary>
    Estirar,
}

/// <summary>Donde cae el punto de anclaje del campo respecto a su texto.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AlineacionDeCampo>))]
public enum AlineacionDeCampo
{
    /// <summary>El punto es la esquina izquierda: el texto crece hacia la derecha.</summary>
    Izquierda = 0,

    /// <summary>El punto es el centro: el texto crece a los dos lados.</summary>
    Centro,

    /// <summary>El punto es la esquina derecha: el texto crece hacia la izquierda.</summary>
    Derecha,
}

/// <summary>
/// Un texto colocado en la tarjeta: mi indicativo, el «To Radio», la fecha, el 73…
/// </summary>
/// <remarks>
/// <b>Todo campo es un texto con variables.</b> «Mi indicativo» es <c>{miindicativo}</c>, el
/// destinatario es <c>To Radio {indicativo}</c>, la fecha es <c>{fecha}</c>. Asi el operador
/// puede escribir lo que quiera —«Tnx fer QSO {nombre}»— sin que el programa tenga que prever
/// cada combinacion, y el asunto y el texto del correo usan las mismas variables.
/// </remarks>
public sealed class CampoDeQsl
{
    /// <summary>Nombre con el que sale en la lista del editor.</summary>
    public string Nombre { get; set; } = Textos.T("Servicios.Impresion.Qsl.Texto");

    /// <summary>Texto que se pinta, con variables entre llaves.</summary>
    public string Texto { get; set; } = string.Empty;

    /// <summary>Distancia del punto de anclaje al borde izquierdo, en milimetros.</summary>
    public double XMm { get; set; }

    /// <summary>Distancia del borde superior del texto al de la tarjeta, en milimetros.</summary>
    public double YMm { get; set; }

    /// <summary>Tipografia, por su nombre de familia de Windows.</summary>
    public string Fuente { get; set; } = "Arial";

    /// <summary>Cuerpo de letra, en puntos tipograficos.</summary>
    public double TamanoPt { get; set; } = 12;

    /// <summary>Negrita.</summary>
    public bool Negrita { get; set; }

    /// <summary>Cursiva.</summary>
    public bool Cursiva { get; set; }

    /// <summary>Color del texto, en <c>#RRGGBB</c> o <c>#AARRGGBB</c>.</summary>
    public string Color { get; set; } = "#000000";

    /// <summary>Recuadro detras del texto, para que se lea encima de una foto. Vacio: sin recuadro.</summary>
    public string? ColorDeRecuadro { get; set; }

    /// <summary>Donde cae el punto de anclaje respecto al texto.</summary>
    public AlineacionDeCampo Alineacion { get; set; }

    /// <summary>
    /// Ancho maximo del texto, en milimetros: lo que no quepa pasa a la linea siguiente. Cero, sin
    /// limite (una linea por cada salto que se escriba).
    /// </summary>
    /// <remarks>Para los parrafos de los diplomas («Por haber confirmado…»).</remarks>
    public double AnchoMaximoMm { get; set; }

    /// <summary>Se pinta o no.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Es un dato del contacto y no de mi estacion.
    /// </summary>
    /// <remarks>
    /// Sirve para exportar el diseño a eQSL: eQSL pone el contacto por su cuenta encima del
    /// diseño que se le sube, asi que esos campos se quitan de la imagen que se le manda.
    /// </remarks>
    public bool EsDelContacto { get; set; }

    /// <summary>Copia independiente del campo.</summary>
    /// <returns>El campo duplicado.</returns>
    public CampoDeQsl Copiar() => (CampoDeQsl)MemberwiseClone();
}

/// <summary>
/// Una plantilla de tarjeta QSL: el tamaño, el fondo y los campos colocados.
/// </summary>
/// <remarks>
/// Se guarda como JSON junto a su imagen en la carpeta de datos del programa (ver
/// <see cref="AlmacenDeDisenosDeQsl"/>). Las medidas van en milimetros y los tamaños de letra
/// en puntos: la misma plantilla sale igual en la pantalla, en un PNG para el correo y en el PDF
/// para la imprenta, sea cual sea la resolucion.
/// </remarks>
public sealed class DisenoDeQsl : IPlantillaConImagenes
{
    /// <summary>Ancho de la tarjeta internacional, en milimetros.</summary>
    public const double AnchoInternacionalMm = 140.0;

    /// <summary>Alto de la tarjeta internacional, en milimetros.</summary>
    public const double AltoInternacionalMm = 90.0;

    /// <summary>Identificador estable: el nombre de sus ficheros en disco.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Como se elige en pantalla.</summary>
    public string Nombre { get; set; } = Textos.T("Servicios.Impresion.Qsl.MiTarjeta");

    /// <summary>Tarjeta en vertical (90 × 140) en vez de apaisada (140 × 90).</summary>
    public bool Vertical { get; set; }

    /// <summary>Ancho real, segun la orientacion.</summary>
    [JsonIgnore]
    public double AnchoMm => Vertical ? AltoInternacionalMm : AnchoInternacionalMm;

    /// <summary>Alto real, segun la orientacion.</summary>
    [JsonIgnore]
    public double AltoMm => Vertical ? AnchoInternacionalMm : AltoInternacionalMm;

    /// <summary>Color liso de la tarjeta, debajo de la foto.</summary>
    public string ColorDeFondo { get; set; } = "#FFFFFF";

    /// <summary>Nombre del fichero de la imagen de fondo, junto al JSON. Vacio: sin imagen.</summary>
    public string? ImagenDeFondo { get; set; }

    /// <summary>Como se coloca la imagen.</summary>
    public AjusteDeFondo Ajuste { get; set; } = AjusteDeFondo.Rellenar;

    /// <summary>Los textos de la tarjeta.</summary>
    public List<CampoDeQsl> Campos { get; set; } = [];

    /// <inheritdoc />
    public override string ToString() => Nombre;

    /// <inheritdoc />
    public IEnumerable<string> Imagenes() =>
        string.IsNullOrWhiteSpace(ImagenDeFondo) ? [] : [ImagenDeFondo];

    /// <inheritdoc />
    public void RenombrarImagen(string viejo, string nuevo)
    {
        if (string.Equals(ImagenDeFondo, viejo, StringComparison.OrdinalIgnoreCase)) ImagenDeFondo = string.IsNullOrEmpty(nuevo) ? null : nuevo;
    }

    /// <summary>Copia independiente, con los campos duplicados.</summary>
    /// <returns>La plantilla duplicada, con el mismo identificador.</returns>
    public DisenoDeQsl Copiar()
    {
        var copia = (DisenoDeQsl)MemberwiseClone();
        copia.Campos = Campos.Select(c => c.Copiar()).ToList();
        return copia;
    }

    /// <summary>
    /// La tarjeta que trae el programa: sin foto, con todo colocado y listo para usar.
    /// </summary>
    /// <returns>Una plantilla nueva.</returns>
    /// <remarks>
    /// Arriba, grande, mi indicativo; debajo mi nombre, QTH y localizador. Abajo, en un recuadro
    /// blanco para que se lea sobre cualquier foto, el bloque del contacto en el orden en que lo
    /// buscan los que clasifican tarjetas: a quien va, fecha, hora UTC, banda, modo e informe.
    /// </remarks>
    public static DisenoDeQsl PorOmision()
    {
        const string Recuadro = "#E6FFFFFF";
        return new DisenoDeQsl
        {
            Id = "nodisla",
            Nombre = Textos.T("Servicios.Impresion.Qsl.NodislaClasica"),
            ColorDeFondo = "#1B3A5C",
            Campos =
            [
                new() { Nombre = Textos.T("Servicios.Impresion.Qsl.Campo.MiIndicativo"), Texto = "{miindicativo}", XMm = 70, YMm = 8, TamanoPt = 54, Negrita = true, Color = "#FFFFFF", Alineacion = AlineacionDeCampo.Centro },
                new() { Nombre = Textos.T("Servicios.Impresion.Qsl.Campo.MiNombreYQth"), Texto = "{minombre} · {miqth}", XMm = 70, YMm = 32, TamanoPt = 12, Color = "#FFFFFF", Alineacion = AlineacionDeCampo.Centro },
                new() { Nombre = Textos.T("Servicios.Impresion.Qsl.Campo.MiLocalizador"), Texto = "Locator {milocalizador}", XMm = 70, YMm = 39, TamanoPt = 10, Color = "#D0E4F5", Alineacion = AlineacionDeCampo.Centro },
                new() { Nombre = "To Radio", Texto = "To Radio {indicativo}", XMm = 8, YMm = 52, TamanoPt = 16, Negrita = true, Color = "#1B3A5C", ColorDeRecuadro = Recuadro, EsDelContacto = true },
                new() { Nombre = Textos.T("Servicios.Impresion.Qsl.Campo.Fecha"), Texto = "Date {fecha}", XMm = 8, YMm = 62, TamanoPt = 10, Color = "#1B3A5C", ColorDeRecuadro = Recuadro, EsDelContacto = true },
                new() { Nombre = Textos.T("Servicios.Impresion.Qsl.Campo.HoraUtc"), Texto = "UTC {hora}", XMm = 44, YMm = 62, TamanoPt = 10, Color = "#1B3A5C", ColorDeRecuadro = Recuadro, EsDelContacto = true },
                new() { Nombre = Textos.T("Servicios.Impresion.Qsl.Campo.BandaFrecuencia"), Texto = "{banda} · {frecuencia} MHz", XMm = 70, YMm = 62, TamanoPt = 10, Color = "#1B3A5C", ColorDeRecuadro = Recuadro, EsDelContacto = true },
                new() { Nombre = Textos.T("Servicios.Impresion.Qsl.Campo.Modo"), Texto = "Mode {modo}", XMm = 8, YMm = 69, TamanoPt = 10, Color = "#1B3A5C", ColorDeRecuadro = Recuadro, EsDelContacto = true },
                new() { Nombre = "RST", Texto = "RST {rst}", XMm = 44, YMm = 69, TamanoPt = 10, Color = "#1B3A5C", ColorDeRecuadro = Recuadro, EsDelContacto = true },
                new() { Nombre = "PSE/TNX QSL", Texto = "{pse}", XMm = 70, YMm = 69, TamanoPt = 10, Negrita = true, Color = "#1B3A5C", ColorDeRecuadro = Recuadro, EsDelContacto = true },
                new() { Nombre = "73", Texto = "73 de {miindicativo}", XMm = 132, YMm = 80, TamanoPt = 12, Cursiva = true, Color = "#FFFFFF", Alineacion = AlineacionDeCampo.Derecha },
            ],
        };
    }
}
