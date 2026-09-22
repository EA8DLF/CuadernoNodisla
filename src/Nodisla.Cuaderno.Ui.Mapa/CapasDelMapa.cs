using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Ui.Mapa;

/// <summary>
/// Construye lo que se pinta sobre el mapa: marcas, trayectos y la sombra de la noche.
/// </summary>
/// <remarks>
/// Todo esto se hace fuera del hilo de la interfaz, asi que aqui no se toca ningun control:
/// se devuelven listas de figuras que despues la vista cuelga de sus capas. El motivo es que
/// un cuaderno de veinte mil contactos tarda decimas de segundo en agruparse y dibujarse, y
/// esas decimas no pueden salir del reloj de la ventana.
/// </remarks>
internal static class CapasDelMapa
{
    /// <summary>Clave con la que cada figura se guarda la marca que representa.</summary>
    public const string ClaveDeLaMarca = "marca";

    /// <summary>Clave con la que cada figura guarda cuantas marcas ha juntado.</summary>
    public const string ClaveDeCuantos = "cuantos";

    /// <summary>
    /// Lo que se atenua el velo de la noche.
    /// </summary>
    /// <remarks>
    /// Va en el ESTILO y con el color macizo, no en el canal alfa del color. Con el color
    /// translucido el motor de mapas componia la mancha de otra manera y el mapa de debajo
    /// desaparecia: quedaban dos bandas macizas que tapaban continentes, mosaicos y contactos.
    /// Este numero esta ajustado mirando la captura: con el se sigue viendo la costa, el mar y
    /// las marcas dentro de la noche, y a la vez se distingue de un vistazo donde es de dia.
    /// </remarks>
    private const float OpacidadDeLaNoche = 0.55f;

    /// <summary>A partir de estas marcas ya no se escriben los indicativos: no cabrian.</summary>
    /// <remarks>
    /// Un indicativo ocupa seis o siete letras al lado de su punto. Con mas de estas marcas a
    /// la vez, los rotulos se pisan unos a otros y el mapa se vuelve una mancha. Por debajo de
    /// este numero caben; por encima, el indicativo se ve al pasar el raton por encima, que
    /// para eso esta la ayuda emergente.
    /// </remarks>
    private const int MarcasConEtiqueta = 40;

    /// <summary>
    /// A partir de estos grupos, solo se escribe el numero de los mas poblados.
    /// </summary>
    /// <remarks>
    /// Con el cuaderno entero encima, escribir la cuenta de cada grupo tapa el mapa: quedan
    /// cientos de numeros y no se ve ni la costa. Con muchos grupos solo se rotulan los que
    /// de verdad dicen algo, que son los que juntan muchos contactos.
    /// </remarks>
    private const int GruposConCuenta = 16;

    /// <summary>Cuando hay muchos grupos, solo se rotulan los mas poblados.</summary>
    private const int GruposRotulados = 6;

    /// <summary>
    /// Convierte las marcas en figuras, agrupando las que caen encima unas de otras.
    /// </summary>
    /// <param name="marcas">Marcas a pintar.</param>
    /// <param name="paleta">Colores del tema en uso.</param>
    /// <param name="maximo">Cuantas figuras se permiten como mucho.</param>
    /// <param name="tamanoDeLetra">Tamano de letra de la ventana, para que las etiquetas crezcan con ella.</param>
    /// <param name="ladoEnGrados">
    /// Lado de la casilla de agrupacion, en grados, calculado por la vista a partir del zoom.
    /// Cero o menos deja que se ajuste solo al tope de marcas.
    /// </param>
    /// <returns>Las figuras listas para colgar de una capa.</returns>
    /// <remarks>
    /// El lado lo pone la vista y no esta clase porque solo la vista sabe cuantos metros mide
    /// un punto de pantalla en este momento. Agrupar por distancia en el mundo y no por
    /// distancia en la pantalla es justo lo que hacia que a vista de mundo salieran veinte
    /// burbujas amontonadas sobre Europa: en el mundo estaban separadas, en la pantalla no.
    /// </remarks>
    public static IReadOnlyList<IFeature> Marcas(
        IReadOnlyList<MarcaDelMapa> marcas,
        PaletaDelMapa paleta,
        int maximo,
        double tamanoDeLetra,
        double ladoEnGrados = 0)
    {
        if (marcas.Count == 0) return [];

        var grupos = ladoEnGrados > 0
            ? ConTopeDeMarcas(AgrupacionDePuntos.Agrupar(marcas, m => m.Donde, ladoEnGrados), marcas, maximo)
            : AgrupacionDePuntos.AgruparHasta(marcas, m => m.Donde, maximo);
        var conEtiqueta = grupos.Count <= MarcasConEtiqueta;

        // Los grupos vienen del mas poblado al menos poblado, asi que cuando hay muchos basta
        // con rotular los primeros: son los unicos cuyo numero dice algo.
        var rotulados = grupos.Count <= GruposConCuenta ? grupos.Count : GruposRotulados;

        var figuras = new List<IFeature>(grupos.Count);
        var orden = 0;
        foreach (var grupo in grupos)
        {
            var cabeza = MasImportante(grupo.Elementos);
            var punto = Proyeccion.A(grupo.Centro);

            var figura = new PointFeature(punto.X, punto.Y)
            {
                [ClaveDeLaMarca] = cabeza,
                [ClaveDeCuantos] = grupo.Cuantos,
            };

            figura.Styles.Add(SimboloDe(cabeza, grupo.Cuantos, paleta));

            var conCuenta = orden++ < rotulados;

            var texto = grupo.EsSuelto
                ? (conEtiqueta ? cabeza.Etiqueta : null)
                : (conCuenta ? grupo.Cuantos.ToString(System.Globalization.CultureInfo.CurrentCulture) : null);

            if (texto is { Length: > 0 })
            {
                figura.Styles.Add(EtiquetaDe(texto, grupo.EsSuelto, paleta, tamanoDeLetra));
            }

            figuras.Add(figura);
        }

        return figuras;
    }

    /// <summary>
    /// Traza los caminos de circulo maximo, cortados por el antimeridiano.
    /// </summary>
    /// <param name="trayectos">Trayectos a dibujar.</param>
    /// <param name="paleta">Colores del tema en uso.</param>
    /// <param name="detalle">Como de finas salen las curvas.</param>
    /// <returns>Las figuras de linea listas para colgar de una capa.</returns>
    public static IReadOnlyList<IFeature> Trayectos(
        IReadOnlyList<TrayectoDelMapa> trayectos,
        PaletaDelMapa paleta,
        DetalleDelMapa detalle)
    {
        if (trayectos.Count == 0) return [];

        var puntos = PuntosPorCurva(detalle);
        var figuras = new List<IFeature>(trayectos.Count * 4);

        var corto = new VectorStyle { Line = new Pen(paleta.CaminoCorto, 2.2) };
        var largo = new VectorStyle
        {
            Line = new Pen(paleta.CaminoLargo, 1.6) { PenStyle = PenStyle.Dash },
        };

        foreach (var trayecto in trayectos)
        {
            Anadir(figuras, trayecto, CaminoDelTrayecto.Corto, corto, puntos);
            if (trayecto.ConCaminoLargo)
            {
                Anadir(figuras, trayecto, CaminoDelTrayecto.Largo, largo, puntos);
            }
        }

        return figuras;
    }

    /// <summary>
    /// La sombra de la noche para un instante dado.
    /// </summary>
    /// <param name="instante">Momento que se representa.</param>
    /// <param name="paleta">Colores del tema en uso.</param>
    /// <param name="detalle">Como de fino sale el contorno.</param>
    /// <returns>El poligono de la noche, o nada si no lo hay.</returns>
    /// <remarks>
    /// El color viene macizo a proposito: <b>la transparencia la pone la capa</b>, no el color.
    /// Con el color translucido el motor de mapas componia el relleno de manera que el mapa de
    /// debajo desaparecia —quedaban dos bandas macizas que tapaban continentes y contactos—.
    /// Atenuando la capa entera, el velo se comporta como lo que es: una sombra por encima del
    /// mapa que deja ver lo que hay debajo.
    /// </remarks>
    public static IReadOnlyList<IFeature> SombraDeLaNoche(
        DateTimeOffset instante,
        PaletaDelMapa paleta,
        DetalleDelMapa detalle)
    {
        if (Proyeccion.APoligono(PasoGris.ZonaNocturna(instante, PuntosDelTerminador(detalle))) is not { } noche)
        {
            return [];
        }

        return
        [
            new GeometryFeature(noche)
            {
                Styles =
                {
                    new VectorStyle { Opacity = OpacidadDeLaNoche, Fill = new Brush(paleta.Noche), Outline = null, Line = null },
                },
            },
        ];
    }

    /// <summary>
    /// Las dos lineas del paso gris: la de dia y noche, y la del crepusculo.
    /// </summary>
    /// <param name="instante">Momento que se representa.</param>
    /// <param name="paleta">Colores del tema en uso.</param>
    /// <param name="detalle">Como de fina sale la linea.</param>
    /// <returns>Las lineas listas para colgar de una capa.</returns>
    /// <remarks>
    /// Van en capa aparte de la sombra porque la sombra se atenua entera —es un velo— y las
    /// lineas no: atenuadas al treinta por ciento no se verian, y son justamente lo que hay
    /// que mirar, porque entre las dos queda la franja donde abre la propagacion.
    /// </remarks>
    public static IReadOnlyList<IFeature> LineasDelPasoGris(
        DateTimeOffset instante,
        PaletaDelMapa paleta,
        DetalleDelMapa detalle)
    {
        var puntos = PuntosDelTerminador(detalle);
        var figuras = new List<IFeature>(2);

        // La linea de dia y noche va maciza; el borde del crepusculo, a trazos.
        AnadirLinea(figuras, PasoGris.Linea(instante, puntos), new Pen(paleta.LineaDelPasoGris, 1.1));
        AnadirLinea(
            figuras,
            PasoGris.Linea(instante, puntos, PasoGris.GradosDeCrepusculo),
            new Pen(paleta.LineaDelPasoGris, 0.8) { PenStyle = PenStyle.Dot });

        return figuras;
    }

    private static int PuntosDelTerminador(DetalleDelMapa detalle) => detalle switch
    {
        DetalleDelMapa.Ligero => 61,
        DetalleDelMapa.Fino => 361,
        _ => 181,
    };

    /// <summary>La marca de la estacion propia, que siempre va sola y encima de todo.</summary>
    /// <param name="donde">Donde esta la estacion.</param>
    /// <param name="etiqueta">Indicativo propio.</param>
    /// <param name="paleta">Colores del tema en uso.</param>
    /// <param name="tamanoDeLetra">Tamano de letra de la ventana.</param>
    /// <returns>Una sola figura.</returns>
    public static IReadOnlyList<IFeature> EstacionPropia(
        Coordenada donde,
        string etiqueta,
        PaletaDelMapa paleta,
        double tamanoDeLetra)
    {
        var punto = Proyeccion.A(donde);
        var marca = new MarcaDelMapa(donde, etiqueta, ClaseDeMarca.EstacionPropia);

        var figura = new PointFeature(punto.X, punto.Y)
        {
            [ClaveDeLaMarca] = marca,
            [ClaveDeCuantos] = 1,
        };

        figura.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Triangle,
            SymbolScale = 0.7,
            Fill = new Brush(paleta.EstacionPropia),
            Outline = new Pen(paleta.BordeDeMarca, 2),
        });

        figura.Styles.Add(EtiquetaDe(etiqueta, esIndicativo: true, paleta, tamanoDeLetra));
        return [figura];
    }

    /// <summary>
    /// De un grupo de marcas, la que le da la cara: primero lo que seria nuevo, despues la
    /// estacion propia y los spots, y solo al final un contacto cualquiera.
    /// </summary>
    private static MarcaDelMapa MasImportante(IReadOnlyList<MarcaDelMapa> marcas)
    {
        var mejor = marcas[0];
        foreach (var marca in marcas)
        {
            if (Peso(marca) > Peso(mejor)) mejor = marca;
        }

        return mejor;
    }

    private static int Peso(MarcaDelMapa marca) => marca switch
    {
        { Destacada: true } => 4,
        { Clase: ClaseDeMarca.EstacionPropia } => 3,
        { Clase: ClaseDeMarca.Spot } => 2,
        _ => 1,
    };

    /// <summary>
    /// Deja los grupos por debajo del tope de marcas sin perder ninguna.
    /// </summary>
    /// <remarks>
    /// Si la casilla del zoom deja mas burbujas de las que la vista admite, se vuelve al
    /// reparto que se ajusta solo. Nunca se recortan grupos por la cola: un contacto que
    /// desaparece del mapa sin avisar es peor que una burbuja de mas.
    /// </remarks>
    private static IReadOnlyList<Grupo<MarcaDelMapa>> ConTopeDeMarcas(
        IReadOnlyList<Grupo<MarcaDelMapa>> grupos,
        IReadOnlyList<MarcaDelMapa> marcas,
        int maximo) =>
        grupos.Count <= maximo ? grupos : AgrupacionDePuntos.AgruparHasta(marcas, m => m.Donde, maximo);

    private static SymbolStyle SimboloDe(MarcaDelMapa marca, int cuantos, PaletaDelMapa paleta)
    {
        var color = marca switch
        {
            { Clase: ClaseDeMarca.EstacionPropia } => paleta.EstacionPropia,
            { Clase: ClaseDeMarca.Spot, Destacada: true } => paleta.SpotNuevo,
            { Clase: ClaseDeMarca.Spot } => paleta.Spot,
            { Destacada: true } => paleta.SpotNuevo,
            _ => paleta.Contacto,
        };

        // El grupo crece con el numero de contactos, pero muy poco a poco: con logaritmo, mil
        // contactos son el doble de grande que uno, no mil veces.
        var escala = 0.17 + (Math.Log10(Math.Max(1, cuantos)) * 0.085);

        return new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            SymbolScale = Math.Min(0.5, escala),
            Fill = new Brush(color),
            Outline = new Pen(paleta.BordeDeMarca, cuantos > 1 ? 1.2 : 0.8),
        };
    }

    private static LabelStyle EtiquetaDe(
        string texto,
        bool esIndicativo,
        PaletaDelMapa paleta,
        double tamanoDeLetra) =>
        new()
        {
            Text = texto,
            // La letra del mapa sigue a la de la ventana: al 200 % de escala se lee igual de
            // bien que el resto, que es justo lo que se pide de esta pantalla.
            Font = new Font { Size = Math.Round(tamanoDeLetra * 0.78, 1), Bold = !esIndicativo },
            ForeColor = paleta.Texto,
            BackColor = null,
            Halo = new Pen(paleta.Halo, 2),
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center,
            VerticalAlignment = esIndicativo
                ? LabelStyle.VerticalAlignmentEnum.Top
                : LabelStyle.VerticalAlignmentEnum.Center,
            Offset = new Offset(0, esIndicativo ? tamanoDeLetra * 0.9 : 0),
            CollisionDetection = true,
        };

    private static void Anadir(
        List<IFeature> figuras,
        TrayectoDelMapa trayecto,
        CaminoDelTrayecto camino,
        VectorStyle estilo,
        int puntos)
    {
        var trazo = TrazadoDeCirculoMaximo.Trazar(trayecto.Origen, trayecto.Destino, camino, puntos);

        foreach (var tramo in TrazadoDeCirculoMaximo.PartirPorElAntimeridiano(trazo))
        {
            if (Proyeccion.ALinea(tramo) is not { } linea) continue;
            figuras.Add(new GeometryFeature(linea) { Styles = { estilo } });
        }
    }

    private static void AnadirLinea(List<IFeature> figuras, IReadOnlyList<Coordenada> puntos, Pen pluma)
    {
        foreach (var tramo in TrazadoDeCirculoMaximo.PartirPorElAntimeridiano(puntos))
        {
            if (Proyeccion.ALinea(tramo) is not { } linea) continue;
            figuras.Add(new GeometryFeature(linea) { Styles = { new VectorStyle { Line = pluma } } });
        }
    }

    private static int PuntosPorCurva(DetalleDelMapa detalle) => detalle switch
    {
        DetalleDelMapa.Ligero => 48,
        DetalleDelMapa.Fino => 256,
        _ => TrazadoDeCirculoMaximo.PuntosPorOmision,
    };
}
