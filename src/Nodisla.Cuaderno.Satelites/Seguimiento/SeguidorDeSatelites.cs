using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Dominio.Valores;
using Nodisla.Cuaderno.Satelites.Catalogo;
using Nodisla.Cuaderno.Satelites.Doppler;
using Nodisla.Cuaderno.Satelites.Orbital;
using Nodisla.Cuaderno.Satelites.Prediccion;

namespace Nodisla.Cuaderno.Satelites.Seguimiento;

/// <summary>Donde esta el satelite ahora mismo y como se ve desde la estacion.</summary>
/// <param name="Satelite">Nombre del satelite.</param>
/// <param name="Instante">Momento al que corresponde, en UTC.</param>
/// <param name="Vista">Azimut, elevacion, distancia y velocidad radial.</param>
/// <param name="Subpunto">Punto de la superficie sobre el que vuela.</param>
/// <param name="AlturaKm">Altura sobre el elipsoide.</param>
/// <param name="EpocaDeLosElementos">Epoca de los elementos con los que se calculo.</param>
public sealed record EstadoDeSeguimiento(
    string Satelite,
    DateTimeOffset Instante,
    VistaDesdeTierra Vista,
    Coordenada Subpunto,
    double AlturaKm,
    DateTimeOffset EpocaDeLosElementos)
{
    /// <summary>Localizador del subpunto, util para ensenarlo junto al mapa.</summary>
    public Locator LocalizadorDelSubpunto => Subpunto.ALocator();

    /// <summary>Frase corta para la barra de estado.</summary>
    /// <param name="ahoraUtc">Momento actual, para decir la antiguedad de los elementos.</param>
    /// <param name="frescura">Plazo dentro del cual los elementos se dan por buenos.</param>
    public string Describir(DateTimeOffset ahoraUtc, TimeSpan frescura)
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        var texto = Vista.SobreElHorizonte
            ? $"{Satelite}: azimut {Vista.AzimutGrados.ToString("F1", es)}°, "
              + $"elevación {Vista.ElevacionGrados.ToString("F1", es)}°, "
              + $"{Vista.DistanciaKm.ToString("N0", es)} km"
            : $"{Satelite}: por debajo del horizonte "
              + $"({Vista.ElevacionGrados.ToString("F1", es)}°), sobre {LocalizadorDelSubpunto}";

        var edad = ahoraUtc - EpocaDeLosElementos;
        if (edad > frescura)
        {
            texto += $". Elementos de hace {ElementosOrbitales.TextoDeEdad(edad)}";
        }

        return texto + ".";
    }
}

/// <summary>Ajustes del seguimiento.</summary>
public sealed class OpcionesDeSatelites
{
    /// <summary>La estacion desde la que se observa.</summary>
    public Observador Observador { get; set; }

    /// <summary>
    /// Plazo dentro del cual unos elementos se consideran frescos.
    /// </summary>
    /// <remarks>
    /// Tres dias. Un TLE de orbita baja se degrada aproximadamente un kilometro por dia, que a
    /// mil kilometros de distancia son unas decimas de grado: a los tres dias el error de
    /// apuntamiento empieza a notarse en una antena directiva y la hora del paso se corre
    /// varios segundos.
    /// </remarks>
    public TimeSpan FrescuraDeLosElementos { get; set; } = TimeSpan.FromDays(3);

    /// <summary>Ajustes de la busqueda de pasos.</summary>
    public OpcionesDePrediccion Prediccion { get; set; } = new();
}

/// <summary>
/// Sigue satelites en vivo y predice sus pasos a partir de los elementos que se le den.
/// </summary>
/// <remarks>
/// Es la cara visible del modulo: guarda los elementos cargados, construye los propagadores
/// una sola vez por satelite y responde a las tres preguntas que se hacen de verdad: donde
/// esta, cuando pasa y a que frecuencia hay que sintonizar.
/// </remarks>
public sealed class SeguidorDeSatelites
{
    private readonly Dictionary<string, Sgp4> _propagadores = new(StringComparer.OrdinalIgnoreCase);
    private readonly OpcionesDeSatelites _opciones;
    private readonly ILogger _registro;

    /// <summary>Crea el seguidor.</summary>
    /// <param name="opciones">Ajustes; si no se dan, se usan los de omision.</param>
    /// <param name="registro">Traza; por omision no se traza nada.</param>
    public SeguidorDeSatelites(OpcionesDeSatelites? opciones = null, ILogger<SeguidorDeSatelites>? registro = null)
    {
        _opciones = opciones ?? new OpcionesDeSatelites();
        _registro = registro ?? (ILogger)NullLogger<SeguidorDeSatelites>.Instance;
    }

    /// <summary>Los satelites cargados, por nombre.</summary>
    public IReadOnlyCollection<string> Cargados => _propagadores.Keys;

    /// <summary>Carga o sustituye los elementos de un satelite.</summary>
    /// <param name="elementos">Elementos orbitales.</param>
    /// <returns><c>true</c> si se ha podido preparar el propagador.</returns>
    /// <remarks>
    /// Devuelve <c>false</c> en vez de reventar cuando el satelite es de espacio profundo: un
    /// fichero de Celestrak trae de todo y no puede tumbar la carga entera.
    /// </remarks>
    public bool Cargar(ElementosOrbitales elementos)
    {
        ArgumentNullException.ThrowIfNull(elementos);

        try
        {
            _propagadores[elementos.Nombre] = new Sgp4(elementos);
            return true;
        }
        catch (NotSupportedException ex)
        {
            _registro.LogWarning(
                "No se carga «{Satelite}»: {Motivo}", elementos.Nombre, ex.Message);
            return false;
        }
    }

    /// <summary>Carga todos los elementos de un fichero.</summary>
    /// <param name="texto">Contenido del fichero de elementos.</param>
    /// <returns>Cuantos satelites se han cargado.</returns>
    public int CargarFichero(string texto)
    {
        var cargados = 0;
        foreach (var elementos in LectorDeElementos.Leer(texto))
        {
            if (Cargar(elementos))
            {
                cargados++;
            }
        }

        _registro.LogInformation("Cargados {Cargados} satélites de elementos orbitales.", cargados);
        return cargados;
    }

    /// <summary>Los elementos con los que se esta siguiendo un satelite.</summary>
    /// <param name="satelite">Nombre del satelite.</param>
    /// <returns>Los elementos, o <c>null</c> si no esta cargado.</returns>
    public ElementosOrbitales? ElementosDe(string satelite) =>
        _propagadores.TryGetValue(satelite, out var p) ? p.Elementos : null;

    /// <summary>Donde esta el satelite en un instante dado.</summary>
    /// <param name="satelite">Nombre del satelite.</param>
    /// <param name="instanteUtc">Momento; normalmente, ahora.</param>
    /// <returns>El estado, o <c>null</c> si el satelite no esta cargado o no se puede propagar.</returns>
    public EstadoDeSeguimiento? Donde(string satelite, DateTimeOffset instanteUtc)
    {
        if (!_propagadores.TryGetValue(satelite, out var propagador))
        {
            return null;
        }

        if (!propagador.TryPropagar(instanteUtc, out var estado, out var fallo))
        {
            _registro.LogWarning(
                "No se puede situar «{Satelite}»: {Motivo}", satelite, Sgp4.Descripcion(fallo));
            return null;
        }

        var vista = Topocentrico.Mirar(estado, _opciones.Observador);
        var (subpunto, altura) = Topocentrico.Subpunto(estado);

        return new EstadoDeSeguimiento(
            satelite, instanteUtc, vista, subpunto, altura, propagador.Elementos.Epoca);
    }

    /// <summary>Los pasos de un satelite dentro de una ventana de tiempo.</summary>
    /// <param name="satelite">Nombre del satelite.</param>
    /// <param name="desdeUtc">Principio de la ventana.</param>
    /// <param name="duracion">Cuanto se mira hacia delante.</param>
    /// <returns>Los pasos encontrados, en orden.</returns>
    public IReadOnlyList<PasoDeSatelite> Pasos(string satelite, DateTimeOffset desdeUtc, TimeSpan duracion)
    {
        if (!_propagadores.TryGetValue(satelite, out var propagador))
        {
            return [];
        }

        var predictor = new PredictorDePasos(_opciones.Prediccion);
        return predictor.Buscar(propagador, _opciones.Observador, desdeUtc, desdeUtc + duracion);
    }

    /// <summary>Los pasos de todos los satelites cargados, mezclados y ordenados por hora.</summary>
    /// <param name="desdeUtc">Principio de la ventana.</param>
    /// <param name="duracion">Cuanto se mira hacia delante.</param>
    /// <returns>Los pasos de todos, en orden de salida.</returns>
    public IReadOnlyList<PasoDeSatelite> PasosDeTodos(DateTimeOffset desdeUtc, TimeSpan duracion)
    {
        var predictor = new PredictorDePasos(_opciones.Prediccion);
        return _propagadores.Values
            .SelectMany(p => predictor.Buscar(p, _opciones.Observador, desdeUtc, desdeUtc + duracion))
            .OrderBy(p => p.Salida.Instante)
            .ToList();
    }

    /// <summary>A que frecuencias hay que sintonizar un transpondedor en este instante.</summary>
    /// <param name="satelite">Nombre del satelite tal y como se cargo.</param>
    /// <param name="transpondedor">El transpondedor del catalogo.</param>
    /// <param name="instanteUtc">Momento.</param>
    /// <returns>Las frecuencias corregidas, o <c>null</c> si no se puede calcular.</returns>
    public SintoniaCorregida? Sintonia(
        string satelite,
        Transpondedor transpondedor,
        DateTimeOffset instanteUtc)
    {
        ArgumentNullException.ThrowIfNull(transpondedor);

        var estado = Donde(satelite, instanteUtc);
        if (estado is null)
        {
            return null;
        }

        var subida = transpondedor.SubidaCentral ?? Frecuencia.Cero;
        var bajada = transpondedor.BajadaCentral ?? Frecuencia.Cero;
        if (subida.EsCero && bajada.EsCero)
        {
            return null;
        }

        return CorreccionDoppler.Corregir(subida, bajada, estado.Vista);
    }
}
