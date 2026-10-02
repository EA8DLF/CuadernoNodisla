using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Nodisla.Cuaderno.Aplicacion.CasosDeUso;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Propagacion.Prediccion;

namespace Nodisla.Cuaderno.Ui.VistaModelos;

/// <summary>Como esta la banda hacia la estacion anunciada, para pintar la celda.</summary>
public enum NivelDePropagacion
{
    /// <summary>No hay con que calcularlo: celda vacia.</summary>
    SinDato,

    /// <summary>Por debajo del 20 %: la banda esta cerrada hacia alli.</summary>
    Cerrada,

    /// <summary>Entre el 20 % y el 50 %: puede que si, puede que no.</summary>
    Dudosa,

    /// <summary>Del 50 % para arriba: deberia oirse.</summary>
    Abierta,
}

/// <summary>Una linea de la lista de spots, ya escrita como se lee en pantalla.</summary>
/// <remarks>
/// Todo es fijo menos la propagacion, que se rehace cada cuarto de hora y cuando llegan
/// indices solares nuevos: por eso la fila avisa de sus cambios.
/// </remarks>
public sealed class FilaDeSpot : ObservableObject
{
    /// <summary>Fiabilidad a partir de la cual la banda se da por abierta.</summary>
    public const double UmbralAbierta = 0.5;

    /// <summary>Fiabilidad a partir de la cual la banda se da por dudosa.</summary>
    public const double UmbralDudosa = 0.2;

    /// <summary>Monta la fila a partir del anuncio recibido.</summary>
    /// <param name="anuncio">Anuncio ya marcado y con sus repeticiones juntas.</param>
    public FilaDeSpot(AnuncioDelCluster anuncio)
    {
        ArgumentNullException.ThrowIfNull(anuncio);

        Anuncio = anuncio;
        var spot = anuncio.Spot;
        Spot = spot;
        Clave = ClaveDeAnuncio.De(spot);

        Oyen = anuncio.Veces > 1
            ? $"×{anuncio.Veces.ToString(Textos.Cultura)}"
            : string.Empty;

        DesdeDonde = anuncio.Continentes.Count > 1
            ? string.Join(" ", anuncio.Continentes)
            : string.Empty;

        Hora = spot.RecibidoUtc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        Indicativo = spot.Indicativo.Valor;
        // En columna, con los ceros puestos: si no, 14.27 encima de 14.3011 encima de 7.203 y la
        // coma bailando de renglon en renglon. Ver TextoDeFrecuencia.EscribirEnColumna.
        Frecuencia = TextoDeFrecuencia.EscribirEnColumna(spot.Frecuencia);
        Banda = spot.Banda.EsVacia ? string.Empty : spot.Banda.Nombre;
        Modo = spot.ModoAnunciado.EsVacio
            ? FiltroDeSpots.ModoProbable(spot.Frecuencia)
            : spot.ModoAnunciado.NombreUsual;
        Localizador = spot.Locator.EsVacio ? string.Empty : spot.Locator.Valor;
        Referencias = string.Join(
            " · ",
            spot.Referencias.Select(r => $"{r.Tipo.ToString().ToUpperInvariant()} {r.Codigo}"));
        PalabrasPorMinuto = spot.PalabrasPorMinuto is { } ppm
            ? $"{ppm.ToString(Textos.Cultura)} ppm"
            : string.Empty;
        Pais = spot.Pais ?? string.Empty;
        Continente = spot.Continente ?? string.Empty;
        Anunciante = spot.Anunciante.Valor;
        Comentario = spot.Comentario ?? string.Empty;

        Decibelios = spot.Decibelios is { } db
            ? db.ToString("+00;-00;+00", CultureInfo.InvariantCulture)
            : string.Empty;

        Nodos = string.Join(", ", anuncio.Nodos);
        NodosCortos = string.Join(" ", anuncio.Nodos.Select(NombreCorto));
    }

    /// <summary>Nodos por los que llego el anuncio, con su nombre entero.</summary>
    public string Nodos { get; }

    /// <summary>Nodos por los que llego, con el nombre corto: «EA4RCH DXFun RBN».</summary>
    public string NodosCortos { get; }

    /// <summary>Por cuantos nodos llego.</summary>
    public int NumeroDeNodos => Anuncio.NumeroDeNodos;

    /// <summary>El anuncio llego por ese nodo.</summary>
    /// <param name="nodo">Nombre del nodo.</param>
    /// <returns>Cierto si llego por el.</returns>
    public bool LlegoPor(string nodo) =>
        Anuncio.Nodos.Any(n => string.Equals(n, nodo, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Nombre corto de un nodo para la columna: lo de antes del primer parentesis o punto
    /// medio. «DXFun (internacional)» queda en «DXFun» y «RBN · CW y RTTY» en «RBN».
    /// </summary>
    /// <param name="nombre">Nombre del nodo.</param>
    /// <returns>El nombre corto.</returns>
    public static string NombreCorto(string nombre)
    {
        ArgumentNullException.ThrowIfNull(nombre);
        var corte = nombre.IndexOfAny(['(', '·']);
        var corto = (corte > 0 ? nombre[..corte] : nombre).Trim();
        return corto.Length > 0 ? corto : nombre.Trim();
    }

    /// <summary>Anuncio del que sale la fila, con sus repeticiones juntas.</summary>
    public AnuncioDelCluster Anuncio { get; }

    /// <summary>Spot mas reciente, que es el que manda.</summary>
    public Spot Spot { get; }

    /// <summary>Lo que identifica a esta estacion para saber si un anuncio se repite.</summary>
    public ClaveDeAnuncio Clave { get; }

    /// <summary>«×N» cuando la oyen N estaciones distintas; vacio si solo una.</summary>
    public string Oyen { get; } = string.Empty;

    /// <summary>Cuantas estaciones la estan oyendo, cuando es mas de una.</summary>
    public string QuienesLoOyen => Anuncio.Veces > 1 ? Textos.F("Principal.Spot.LoOyen", Anuncio.Veces) : string.Empty;

    /// <summary>Continentes desde los que se la oye, cuando es mas de uno.</summary>
    public string DesdeDonde { get; } = string.Empty;

    /// <summary>La estan oyendo desde varios sitios: senal de que esta entrando bien.</summary>
    public bool LaOyenVarios => Anuncio.Veces > 1;

    /// <summary>Hora UTC en la que llego el anuncio.</summary>
    public string Hora { get; }

    /// <summary>Indicativo anunciado.</summary>
    public string Indicativo { get; }

    /// <summary>Frecuencia en megahercios, siempre con punto decimal.</summary>
    public string Frecuencia { get; }

    /// <summary>Banda del anuncio.</summary>
    public string Banda { get; }

    /// <summary>Modo del anuncio, deducido del plan de bandas si el cluster no lo dice.</summary>
    public string Modo { get; }

    /// <summary>Entidad DXCC de la estacion anunciada.</summary>
    public string Pais { get; }

    /// <summary>Continente de la entidad.</summary>
    public string Continente { get; }

    /// <summary>Quien puso el anuncio.</summary>
    public string Anunciante { get; }

    /// <summary>Texto libre del anuncio.</summary>
    public string Comentario { get; }

    /// <summary>Relacion senal-ruido, cuando la fuente la da.</summary>
    public string Decibelios { get; }

    /// <summary>Localizador de la estacion, cuando el nodo lo manda.</summary>
    public string Localizador { get; } = string.Empty;

    /// <summary>Referencias de activacion que venian en el comentario.</summary>
    public string Referencias { get; } = string.Empty;

    /// <summary>Velocidad de manipulacion, cuando la fuente la da.</summary>
    public string PalabrasPorMinuto { get; } = string.Empty;

    /// <summary>El anuncio lo puso una estacion automatica de escucha, no una persona.</summary>
    public bool EsDeEscuchaAutomatica => Spot.EsDeEscuchaAutomatica;

    /// <summary>Por que este spot merece atencion, o vacio si no la merece.</summary>
    public string Novedad => Spot switch
    {
        { EsEntidadNueva: true } => Textos.T("Principal.Cluster.NovedadEntidad"),
        { EsNuevoEnBandaYModo: true } => Textos.T("Principal.Spot.NuevaEnBandaYModo"),
        _ => string.Empty,
    };

    /// <summary>La entidad no esta en el cuaderno en ninguna banda.</summary>
    public bool EsEntidadNueva => Spot.EsEntidadNueva;

    /// <summary>Esta en el cuaderno, pero no en esta banda y este modo.</summary>
    public bool EsHuecoNuevo => !Spot.EsEntidadNueva && Spot.EsNuevoEnBandaYModo;

    /// <summary>El spot aporta algo: o es entidad nueva o llena un hueco.</summary>
    public bool EsInteresante => EsEntidadNueva || EsHuecoNuevo;

    /// <summary>Texto largo para la sugerencia.</summary>
    public string Detalle
    {
        get
        {
            var partes = new List<string> { Textos.F("Principal.Spot.EnFrecuencia", Indicativo, Frecuencia) };
            if (Banda.Length > 0 || Modo.Length > 0) partes.Add($"{Banda} {Modo}".Trim());
            if (Pais.Length > 0) partes.Add(Pais);
            if (Localizador.Length > 0) partes.Add(Localizador);
            if (Referencias.Length > 0) partes.Add(Referencias);
            if (PalabrasPorMinuto.Length > 0) partes.Add(PalabrasPorMinuto);
            if (QuienesLoOyen.Length > 0) partes.Add(QuienesLoOyen);
            if (DesdeDonde.Length > 0) partes.Add(Textos.F("Principal.Spot.Desde", DesdeDonde));
            if (Anuncio.NumeroDeNodos > 1) partes.Add(Textos.F("Principal.Spot.PorNodos", Anuncio.NumeroDeNodos, Nodos));
            if (Novedad.Length > 0) partes.Add(Novedad);
            if (Comentario.Length > 0) partes.Add($"«{Comentario}»");
            partes.Add(EsDeEscuchaAutomatica
                ? Textos.F("Principal.Spot.EscuchaAutomatica", Anunciante, Hora)
                : Textos.F("Principal.Spot.AnunciadoPor", Anunciante, Hora));
            return string.Join(" · ", partes);
        }
    }

    /// <summary>Prevision de mi propagacion hacia esta estacion, o nula si no se pudo calcular.</summary>
    public PrevisionHaciaEstacion? Prevision { get; private set; }

    /// <summary>De donde salio la posicion de la estacion, para el rotulo.</summary>
    public string OrigenDeLaPosicion { get; private set; } = string.Empty;

    /// <summary>Por que la celda esta vacia, cuando lo esta.</summary>
    public string PorQueSinPropagacion { get; private set; } = string.Empty;

    /// <summary>Fiabilidad de 0 a 1, o -1 sin dato: para ordenar la columna.</summary>
    public double FiabilidadParaOrdenar => Prevision?.Fiabilidad ?? -1.0;

    /// <summary>Porcentaje de 0 a 100 para la barrita; cero sin dato.</summary>
    public double PorcentajeDePropagacion => Prevision is { } p ? Math.Round(p.Fiabilidad * 100.0) : 0.0;

    /// <summary>La cifra que se lee en la celda, o vacio.</summary>
    public string Propagacion => Prevision is { } p
        ? $"{Math.Round(p.Fiabilidad * 100.0).ToString("0", Textos.Cultura)} %"
        : string.Empty;

    /// <summary>Verde, ambar o gris; o nada.</summary>
    public NivelDePropagacion NivelDePropagacion => Prevision switch
    {
        null => NivelDePropagacion.SinDato,
        { Fiabilidad: >= UmbralAbierta } => NivelDePropagacion.Abierta,
        { Fiabilidad: >= UmbralDudosa } => NivelDePropagacion.Dudosa,
        _ => NivelDePropagacion.Cerrada,
    };

    /// <summary>La banda esta abierta o dudosa hacia alli: pasa el filtro «solo con propagación».</summary>
    public bool ConPropagacion => NivelDePropagacion is NivelDePropagacion.Abierta or NivelDePropagacion.Dudosa;

    /// <summary>Rotulo emergente de la celda de propagacion.</summary>
    public string PropagacionDetalle
    {
        get
        {
            if (Prevision is not { } p)
            {
                return PorQueSinPropagacion.Length > 0 ? PorQueSinPropagacion : Textos.T("Principal.Spot.SinPrevision");
            }

            var cultura = Textos.Cultura;
            var saltos = p.Saltos == 1 ? Textos.T("Principal.Prevision.UnSalto") : Textos.F("Principal.Prevision.Saltos", p.Saltos);
            var lineas = new List<string>
            {
                Textos.F("Principal.Prevision.Fiabilidad", Math.Round(p.Fiabilidad * 100.0).ToString("0", cultura), Indicativo, Frecuencia.Trim()),
                p.RelacionSenalRuido is { } sr
                    ? Textos.F("Principal.Prevision.SenalRuido", sr.ToString("+0;-0;0", cultura), Modo)
                    : Textos.T("Principal.Prevision.SenalRuidoNoLlega"),
                $"MUF {p.MufMhz.ToString("0.0", cultura)} MHz · {saltos}",
                Textos.F("Principal.Prevision.DistanciaYRumbo", p.DistanciaKm.ToString("N0", cultura), p.RumboGrados.ToString("000", cultura)),
                Textos.F("Principal.Prevision.Posicion", OrigenDeLaPosicion),
                Textos.F(
                    p.IndicesDeCopia ? "Principal.Prevision.CalculadoConCopia" : "Principal.Prevision.Calculado",
                    p.CalculadaUtc.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture)),
                p.Motor,
            };
            return string.Join(Environment.NewLine, lineas);
        }
    }

    /// <summary>Pone la prevision calculada y avisa a la pantalla.</summary>
    /// <param name="prevision">Prevision, o nula si no se pudo calcular.</param>
    /// <param name="origenDeLaPosicion">De donde salio la posicion de la estacion.</param>
    /// <param name="porQueNo">Motivo de que no haya prevision, si no la hay.</param>
    public void PonerPropagacion(PrevisionHaciaEstacion? prevision, string origenDeLaPosicion, string porQueNo)
    {
        var motivo = prevision is null ? porQueNo : string.Empty;
        if (Equals(prevision, Prevision)
            && origenDeLaPosicion == OrigenDeLaPosicion
            && motivo == PorQueSinPropagacion)
        {
            return;
        }

        Prevision = prevision;
        OrigenDeLaPosicion = origenDeLaPosicion;
        PorQueSinPropagacion = motivo;

        OnPropertyChanged(nameof(Prevision));
        OnPropertyChanged(nameof(OrigenDeLaPosicion));
        OnPropertyChanged(nameof(PorQueSinPropagacion));
        OnPropertyChanged(nameof(FiabilidadParaOrdenar));
        OnPropertyChanged(nameof(PorcentajeDePropagacion));
        OnPropertyChanged(nameof(Propagacion));
        OnPropertyChanged(nameof(NivelDePropagacion));
        OnPropertyChanged(nameof(ConPropagacion));
        OnPropertyChanged(nameof(PropagacionDetalle));
    }

    /// <summary>
    /// Donde esta la estacion para calcular la propagacion, y de donde sale el dato.
    /// </summary>
    /// <remarks>
    /// Por orden de precision: el localizador que manda el nodo, el que consta en el cuaderno
    /// de un contacto anterior, y por ultimo el prefijo o el centro de la entidad DXCC.
    /// </remarks>
    /// <param name="resolutor">Resolutor de entidades DXCC.</param>
    /// <param name="delCuaderno">Localizador de un contacto anterior, o vacio.</param>
    public (Coordenada? Donde, string DeDonde) Posicion(
        Nodisla.Cuaderno.Dominio.Dxcc.IResolutorDxcc resolutor,
        Locator delCuaderno)
    {
        ArgumentNullException.ThrowIfNull(resolutor);

        if (!Spot.Locator.EsVacio)
        {
            return (Coordenada.Desde(Spot.Locator), Textos.F("Principal.Spot.LocatorDelAnuncio", Spot.Locator.Valor));
        }

        if (!delCuaderno.EsVacio)
        {
            return (Coordenada.Desde(delCuaderno), Textos.F("Principal.Spot.LocatorDelCuaderno", delCuaderno.Valor));
        }

        var resuelto = resolutor.Resolver(Spot.Indicativo, DateOnly.FromDateTime(Spot.RecibidoUtc.UtcDateTime));
        var nombre = resuelto.Entidad?.NombreParaMostrar ?? Pais;
        if (resuelto.Coordenada is { } porPrefijo) return (porPrefijo, Textos.F("Principal.Spot.PrefijoDelIndicativo", nombre));
        if (resuelto.Entidad?.Coordenada is { } centro) return (centro, Textos.F("Principal.Spot.CentroDeLaEntidad", nombre));
        return (null, string.Empty);
    }

    /// <summary>
    /// Donde cae la estacion en el mapa, cuando se sabe.
    /// </summary>
    /// <remarks>
    /// Manda el localizador si el nodo lo mando: son unos kilometros de precision, frente a
    /// los cientos que da el centro de una entidad. Solo cuando no hay localizador se cae al
    /// prefijo y, en ultimo termino, al centro del pais.
    /// </remarks>
    /// <param name="resolutor">Resolutor de entidades DXCC.</param>
    /// <returns>La coordenada, o nulo si no hay manera de situarla.</returns>
    public Coordenada? EnElMapa(Nodisla.Cuaderno.Dominio.Dxcc.IResolutorDxcc resolutor)
    {
        ArgumentNullException.ThrowIfNull(resolutor);

        if (!Spot.Locator.EsVacio) return Coordenada.Desde(Spot.Locator);

        var resuelto = resolutor.Resolver(Spot.Indicativo, DateOnly.FromDateTime(Spot.RecibidoUtc.UtcDateTime));
        return resuelto.Coordenada ?? resuelto.Entidad?.Coordenada;
    }
}
