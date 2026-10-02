using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Impresion.Qsl;

namespace Nodisla.Cuaderno.Impresion.Diplomas;

/// <summary>
/// Las plantillas de diploma que trae el programa: sobrias, en los colores de NODISLA y sin
/// marcas ni logos de nadie. Los huecos de logo y firma van vacios para que cada cual ponga los suyos.
/// </summary>
/// <remarks>
/// Solo usan tipografias que vienen con Windows (Palatino Linotype, Georgia, Segoe UI): una
/// plantilla de fabrica no puede depender de una fuente que el operador no tenga.
/// </remarks>
public static class PlantillasDeDiplomaDeFabrica
{
    /// <summary>Azul de NODISLA.</summary>
    public const string Azul = "#1B3A5C";

    /// <summary>Oro viejo.</summary>
    public const string Oro = "#B08D57";

    private const string Serif = "Palatino Linotype";
    private const string Texto = "Georgia";
    private const string Sans = "Segoe UI";

    /// <summary>Todas, la primera es la de omision.</summary>
    /// <returns>Plantillas nuevas.</returns>
    public static IReadOnlyList<DisenoDeDiploma> Todas() => [Clasico(), Atlantico(), Sobrio(), Volcan()];

    /// <summary>A4 apaisado, marfil, orla clasica azul y oro.</summary>
    /// <returns>La plantilla.</returns>
    public static DisenoDeDiploma Clasico()
    {
        const double C = 148.5;
        return new DisenoDeDiploma
        {
            Id = "nodisla-clasico",
            Nombre = Textos.T("Servicios.Impresion.Fabrica.Plantilla.Clasico"),
            Papel = PapelDeDiploma.A4,
            ColorDeFondo = "#FBF8F1",
            Marco = new MarcoDeDiploma { Estilo = EstiloDeMarco.Clasico, Color = Azul, ColorSecundario = Oro, MargenMm = 10, GrosorMm = 1.4 },
            DiplomaPorOmision = "Diploma NODISLA",
            CategoriaPorOmision = "Única",
            Campos =
            [
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Numero"), "Nº {numero}", 272, 20, Serif, 10, "#6B6B6B", AlineacionDeCampo.Derecha),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Encabezado"), "DIPLOMA", C, 28, Serif, 46, Azul, AlineacionDeCampo.Centro, negrita: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.NombreDelDiploma"), "{diploma}", C, 52, Serif, 20, "#8A6A3A", AlineacionDeCampo.Centro, cursiva: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.SeConcedeA"), "se concede a", C, 68, Texto, 12, "#555555", AlineacionDeCampo.Centro, cursiva: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Indicativo"), "{indicativo}", C, 76, Serif, 48, Azul, AlineacionDeCampo.Centro, negrita: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Nombre"), "{nombre}", C, 100, Texto, 16, "#333333", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Motivo"), "por haber acreditado {referencias} referencias confirmadas en {qsos} contactos, en la categoría {categoria}.",
                    C, 115, Texto, 13, "#333333", AlineacionDeCampo.Centro, ancho: 190),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Fecha"), "{fecha}", 70, 164, Texto, 11, "#333333", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.RotuloDeLaFecha"), "Fecha de emisión", 70, 171, Sans, 8, "#7A7A7A", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Gestor"), "{gestor}", 227, 164, Texto, 11, "#333333", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.RotuloDelGestor"), "Gestor del diploma", 227, 171, Sans, 8, "#7A7A7A", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Emisor"), "{miindicativo} · {miqth}", C, 186, Sans, 8, "#8A8A8A", AlineacionDeCampo.Centro),
            ],
            ImagenesColocadas =
            [
                new() { Nombre = Textos.T("Servicios.Impresion.Fabrica.Elemento.Logo"), Uso = UsoDeImagen.Logo, XMm = C - 15, YMm = 140, AnchoMm = 30, AltoMm = 30 },
                new() { Nombre = Textos.T("Servicios.Impresion.Fabrica.Elemento.Firma"), Uso = UsoDeImagen.Firma, XMm = 197, YMm = 142, AnchoMm = 60, AltoMm = 20, LineaDeFirma = true },
            ],
            Tabla = new TablaDeDiploma { Visible = false, XMm = 40, YMm = 130, AnchoMm = 217, AltoMm = 30, Bloques = 3 },
        };
    }

    /// <summary>A4 apaisado, blanco, doble filete azul mar, con la tabla de referencias a la derecha.</summary>
    /// <returns>La plantilla.</returns>
    public static DisenoDeDiploma Atlantico()
    {
        const string Mar = "#0E4D7A";
        const double C = 88;
        return new DisenoDeDiploma
        {
            Id = "nodisla-atlantico",
            Nombre = Textos.T("Servicios.Impresion.Fabrica.Plantilla.Atlantico"),
            Papel = PapelDeDiploma.A4,
            ColorDeFondo = "#FFFFFF",
            Marco = new MarcoDeDiploma { Estilo = EstiloDeMarco.DobleFilete, Color = Mar, ColorSecundario = "#7FA7C4", MargenMm = 9, GrosorMm = 1.0 },
            DiplomaPorOmision = "Diploma NODISLA",
            CategoriaPorOmision = "Única",
            Campos =
            [
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Encabezado"), "CERTIFICADO", C, 30, Serif, 30, Mar, AlineacionDeCampo.Centro, negrita: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.NombreDelDiploma"), "{diploma}", C, 46, Serif, 18, Mar, AlineacionDeCampo.Centro, cursiva: true, ancho: 130),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Categoria"), "Categoría {categoria}", C, 60, Sans, 11, "#5A7D99", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.OtorgadoA"), "otorgado a", C, 74, Texto, 11, "#555555", AlineacionDeCampo.Centro, cursiva: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Indicativo"), "{indicativo}", C, 81, Serif, 40, Mar, AlineacionDeCampo.Centro, negrita: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Nombre"), "{nombre}", C, 101, Texto, 14, "#333333", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Motivo"), "{referencias} referencias en {qsos} contactos\nentre {primerqso} y {ultimoqso}", C, 114, Texto, 11, "#333333", AlineacionDeCampo.Centro, ancho: 130),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Numero"), "Nº {numero}", C, 134, Serif, 11, Mar, AlineacionDeCampo.Centro, negrita: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Gestor"), "{gestor}", C, 171, Texto, 10, "#333333", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Fecha"), "{fecha}", C, 177, Sans, 8, "#7A7A7A", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Emisor"), "{miindicativo}", 272, 190, Sans, 7, "#9AA9B5", AlineacionDeCampo.Derecha),
            ],
            ImagenesColocadas =
            [
                new() { Nombre = Textos.T("Servicios.Impresion.Fabrica.Elemento.Logo"), Uso = UsoDeImagen.Logo, XMm = 20, YMm = 20, AnchoMm = 24, AltoMm = 24 },
                new() { Nombre = Textos.T("Servicios.Impresion.Fabrica.Elemento.Firma"), Uso = UsoDeImagen.Firma, XMm = C - 30, YMm = 148, AnchoMm = 60, AltoMm = 20, LineaDeFirma = true },
            ],
            Tabla = new TablaDeDiploma
            {
                Visible = true,
                Titulo = "Referencias que lo justifican",
                XMm = 163,
                YMm = 26,
                AnchoMm = 114,
                AltoMm = 158,
                Bloques = 1,
                Fuente = Sans,
                TamanoPt = 7.5,
                ColorDeCabecera = Mar,
                ColorDeLineas = "#C9D8E4",
                Columnas =
                [
                    new() { Titulo = "Ref.", Texto = "{referencia}", Ancho = 1 },
                    new() { Titulo = "Indicativo", Texto = "{indicativo}", Ancho = 1.2 },
                    new() { Titulo = "Fecha", Texto = "{fecha}", Ancho = 1.1 },
                    new() { Titulo = "Banda", Texto = "{banda}", Ancho = 0.6 },
                    new() { Titulo = "Modo", Texto = "{modo}", Ancho = 0.6 },
                ],
            },
        };
    }

    /// <summary>A4 vertical, blanco, filete fino con escuadras, tabla a dos bloques: el certificado de un diploma conseguido.</summary>
    /// <returns>La plantilla.</returns>
    public static DisenoDeDiploma Sobrio()
    {
        const double C = 105;
        return new DisenoDeDiploma
        {
            Id = "nodisla-sobrio",
            Nombre = Textos.T("Servicios.Impresion.Fabrica.Plantilla.Sobrio"),
            Papel = PapelDeDiploma.A4,
            Vertical = true,
            ColorDeFondo = "#FFFFFF",
            Marco = new MarcoDeDiploma { Estilo = EstiloDeMarco.Escuadras, Color = "#333333", ColorSecundario = Oro, MargenMm = 12, GrosorMm = 0.5 },
            DiplomaPorOmision = "Diploma NODISLA",
            Campos =
            [
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Encabezado"), "CERTIFICADO", C, 56, Serif, 30, "#222222", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.NombreDelDiploma"), "{diploma}", C, 72, Serif, 16, "#8A6A3A", AlineacionDeCampo.Centro, cursiva: true, ancho: 160),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Categoria"), "{categoria}", C, 84, Sans, 11, "#555555", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.SeCertifica"), "Se certifica que la estación", C, 97, Texto, 11, "#555555", AlineacionDeCampo.Centro, cursiva: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Indicativo"), "{indicativo}", C, 104, Serif, 36, Azul, AlineacionDeCampo.Centro, negrita: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Nombre"), "{nombre}", C, 122, Texto, 13, "#333333", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Motivo"), "ha confirmado {referencias} referencias en {qsos} contactos.", C, 132, Texto, 11, "#333333", AlineacionDeCampo.Centro, ancho: 150),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Numero"), "Nº {numero}", 30, 256, Serif, 10, "#333333", AlineacionDeCampo.Izquierda),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Fecha"), "{fechacorta}", 30, 262, Sans, 9, "#7A7A7A", AlineacionDeCampo.Izquierda),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Gestor"), "{gestor}", 150, 262, Texto, 10, "#333333", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Emisor"), "{miindicativo} · {milocalizador}", C, 278, Sans, 7, "#9A9A9A", AlineacionDeCampo.Centro),
            ],
            ImagenesColocadas =
            [
                new() { Nombre = Textos.T("Servicios.Impresion.Fabrica.Elemento.Logo"), Uso = UsoDeImagen.Logo, XMm = C - 14, YMm = 20, AnchoMm = 28, AltoMm = 28 },
                new() { Nombre = Textos.T("Servicios.Impresion.Fabrica.Elemento.Firma"), Uso = UsoDeImagen.Firma, XMm = 122, YMm = 242, AnchoMm = 56, AltoMm = 18, LineaDeFirma = true },
            ],
            Tabla = new TablaDeDiploma
            {
                Visible = true,
                Titulo = "Relación de referencias",
                XMm = 24,
                YMm = 146,
                AnchoMm = 162,
                AltoMm = 88,
                Bloques = 2,
                Fuente = Sans,
                TamanoPt = 7.5,
                ColorDeCabecera = "#333333",
                Columnas =
                [
                    new() { Titulo = "Ref.", Texto = "{referencia}", Ancho = 1 },
                    new() { Titulo = "Indicativo", Texto = "{indicativo}", Ancho = 1.2 },
                    new() { Titulo = "Fecha", Texto = "{fecha}", Ancho = 1.1 },
                    new() { Titulo = "Banda", Texto = "{banda}", Ancho = 0.7 },
                ],
            },
        };
    }

    /// <summary>Carta apaisada, papel crema, orla clasica en piedra volcanica y teja.</summary>
    /// <returns>La plantilla.</returns>
    public static DisenoDeDiploma Volcan()
    {
        const string Piedra = "#3A2E2A";
        const string Teja = "#A8452F";
        const double C = 139.7;
        return new DisenoDeDiploma
        {
            Id = "nodisla-volcan",
            Nombre = Textos.T("Servicios.Impresion.Fabrica.Plantilla.Volcan"),
            Papel = PapelDeDiploma.Carta,
            ColorDeFondo = "#FFFDF8",
            Marco = new MarcoDeDiploma { Estilo = EstiloDeMarco.Clasico, Color = Piedra, ColorSecundario = Teja, MargenMm = 11, GrosorMm = 1.3 },
            DiplomaPorOmision = "Diploma NODISLA",
            CategoriaPorOmision = "Única",
            Campos =
            [
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Encabezado"), "DIPLOMA", C, 28, Serif, 40, Piedra, AlineacionDeCampo.Centro, negrita: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.NombreDelDiploma"), "{diploma}", C, 50, Serif, 18, Teja, AlineacionDeCampo.Centro, cursiva: true, ancho: 220),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.ConcedidoA"), "concedido a", C, 66, Texto, 12, "#6A5A52", AlineacionDeCampo.Centro, cursiva: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Indicativo"), "{indicativo}", C, 74, Serif, 46, Piedra, AlineacionDeCampo.Centro, negrita: true),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Nombre"), "{nombre}", C, 97, Texto, 15, "#333333", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Motivo"), "Categoría {categoria} · {referencias} referencias confirmadas", C, 112, Texto, 12, "#333333", AlineacionDeCampo.Centro, ancho: 200),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.NumeroYFecha"), "Nº {numero} · {fecha}", C, 124, Sans, 10, "#7A6A60", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Gestor"), "{gestor}", 210, 172, Texto, 11, "#333333", AlineacionDeCampo.Centro),
                T(Textos.T("Servicios.Impresion.Fabrica.Elemento.Emisor"), "{miindicativo} · {miqth}", C, 192, Sans, 8, "#8A7A70", AlineacionDeCampo.Centro),
            ],
            ImagenesColocadas =
            [
                new() { Nombre = Textos.T("Servicios.Impresion.Fabrica.Elemento.Logo"), Uso = UsoDeImagen.Logo, XMm = 52, YMm = 146, AnchoMm = 28, AltoMm = 28 },
                new() { Nombre = Textos.T("Servicios.Impresion.Fabrica.Elemento.Firma"), Uso = UsoDeImagen.Firma, XMm = 180, YMm = 148, AnchoMm = 60, AltoMm = 22, LineaDeFirma = true, ColorDeLinea = Piedra },
            ],
            Tabla = new TablaDeDiploma { Visible = false, XMm = 40, YMm = 132, AnchoMm = 200, AltoMm = 40, Bloques = 3, ColorDeCabecera = Piedra },
        };
    }

    private static CampoDeQsl T(
        string nombre,
        string texto,
        double x,
        double y,
        string fuente,
        double tamano,
        string color,
        AlineacionDeCampo alineacion,
        bool negrita = false,
        bool cursiva = false,
        double ancho = 0) => new()
        {
            Nombre = nombre,
            Texto = texto,
            XMm = x,
            YMm = y,
            Fuente = fuente,
            TamanoPt = tamano,
            Color = color,
            Alineacion = alineacion,
            Negrita = negrita,
            Cursiva = cursiva,
            AnchoMaximoMm = ancho,
        };
}
