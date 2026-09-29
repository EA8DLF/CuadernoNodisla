using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Catalogo;

/// <summary>Cuanto se ha contrastado lo que el programa cree saber de un concurso.</summary>
/// <remarks>
/// Existe porque las bases de un concurso cambian todos los anos y el programa no puede
/// fingir que las sabe. Lo que ensena la sesion es <b>la cuenta del operador</b>; la buena
/// la hace el organizador recalculando desde el Cabrillo.
/// </remarks>
public enum EstadoDeLasReglas
{
    /// <summary>Solo se conoce el nombre y el identificador Cabrillo: no se puntua solo.</summary>
    SoloCatalogo,

    /// <summary>
    /// Las reglas estan escritas pero no se han leido de las bases del ano en curso. La
    /// sesion avisa al abrirse.
    /// </summary>
    Aproximado,

    /// <summary>Las reglas se han contrastado con el organizador y llevan su fecha.</summary>
    Verificado,
}

/// <summary>Que campo se intercambia en un concurso.</summary>
public enum TipoDeIntercambio
{
    /// <summary>Informe RST o RS.</summary>
    Informe,
    /// <summary>Numero de serie, que avanza con cada contacto.</summary>
    Serie,
    /// <summary>Zona CQ.</summary>
    ZonaCq,
    /// <summary>Zona ITU.</summary>
    ZonaItu,
    /// <summary>Localizador Maidenhead.</summary>
    Locator,
    /// <summary>Seccion ARRL.</summary>
    Seccion,
    /// <summary>Provincia.</summary>
    Provincia,
    /// <summary>Estado de EE. UU. o provincia de Canada.</summary>
    Estado,
    /// <summary>Nombre del operador.</summary>
    Nombre,
    /// <summary>Edad o ano de la primera licencia.</summary>
    Edad,
    /// <summary>Potencia de salida.</summary>
    Potencia,
    /// <summary>Numero de socio de un club.</summary>
    Socio,
    /// <summary>Cualquier otra cosa: el programa no la interpreta, solo la guarda.</summary>
    Texto,
}

/// <summary>Contra quien se hizo el contacto, para decidir cuanto vale.</summary>
public enum AmbitoDePuntos
{
    /// <summary>Todos los contactos valen lo mismo.</summary>
    Cualquiera,
    /// <summary>El corresponsal esta en mi misma entidad DXCC.</summary>
    MismoPais,
    /// <summary>Mismo continente, entidad distinta.</summary>
    MismoContinente,
    /// <summary>
    /// Mismo continente, entidad distinta, y ese continente es Norteamerica. Hay que
    /// mirarlo antes que <see cref="MismoContinente"/>, porque es un caso suyo mas estrecho.
    /// </summary>
    MismoContinenteNa,
    /// <summary>Continente distinto.</summary>
    OtroContinente,
    /// <summary>Misma zona CQ.</summary>
    MismaZonaCq,
    /// <summary>Misma zona ITU.</summary>
    MismaZonaItu,
}

/// <summary>Que cuenta como multiplicador.</summary>
public enum TipoDeMultiplicador
{
    /// <summary>Entidad DXCC.</summary>
    Dxcc,
    /// <summary>Zona CQ.</summary>
    ZonaCq,
    /// <summary>Zona ITU.</summary>
    ZonaItu,
    /// <summary>Prefijo segun las reglas del WPX.</summary>
    PrefijoWpx,
    /// <summary>Localizador de cuatro caracteres.</summary>
    Locator,
    /// <summary>Campo del localizador: los dos primeros caracteres.</summary>
    CampoLocator,
    /// <summary>Seccion ARRL.</summary>
    Seccion,
    /// <summary>Provincia.</summary>
    Provincia,
    /// <summary>Estado de EE. UU. o provincia de Canada.</summary>
    Estado,
    /// <summary>Sociedad nacional o cargo de la IARU.</summary>
    Sociedad,
}

/// <summary>Cada cuanto se vuelve a contar un multiplicador.</summary>
public enum AlcanceDeMultiplicador
{
    /// <summary>Una sola vez en todo el concurso.</summary>
    Global,
    /// <summary>Una vez por banda.</summary>
    PorBanda,
    /// <summary>Una vez por modo.</summary>
    PorModo,
    /// <summary>Una vez por cada pareja de banda y modo.</summary>
    PorBandaYModo,
}

/// <summary>Cuando se puede repetir a una estacion sin que sea duplicado.</summary>
public enum AmbitoDeDuplicado
{
    /// <summary>Una vez por banda.</summary>
    Banda,
    /// <summary>Una vez por banda y modo.</summary>
    BandaYModo,
    /// <summary>Una sola vez en todo el concurso.</summary>
    Unico,
}

/// <summary>Una regla de puntuacion. Se miran en orden y gana la primera que case.</summary>
/// <param name="Ambito">Contra quien se hizo el contacto.</param>
/// <param name="Puntos">Cuanto vale.</param>
/// <param name="Bandas">Bandas a las que se aplica; vacio significa todas.</param>
/// <param name="Modos">Modos a los que se aplica; vacio significa todos.</param>
public sealed record ReglaDePuntos(
    AmbitoDePuntos Ambito,
    int Puntos,
    IReadOnlyList<Banda> Bandas,
    IReadOnlyList<string> Modos);

/// <summary>Una regla de multiplicador.</summary>
/// <param name="Tipo">Que cuenta.</param>
/// <param name="Alcance">Cada cuanto se vuelve a contar.</param>
public sealed record ReglaDeMultiplicador(TipoDeMultiplicador Tipo, AlcanceDeMultiplicador Alcance);

/// <summary>Cuando se celebra un concurso, dicho como lo dicen sus bases.</summary>
/// <param name="Mes">Mes, de 1 a 12. Cero significa que el recurso no lo fija.</param>
/// <param name="Ordinal">Cual de ese mes, de 1 a 4; 5 significa «el ultimo».</param>
/// <param name="SemanaCompleta">
/// La semana tiene que caber entera en el mes. Es lo que quiere decir «el ultimo fin de
/// semana completo», que no siempre es el ultimo sabado del mes.
/// </param>
/// <param name="Dia">Dia de la semana en que empieza.</param>
/// <param name="HoraInicioUtc">Hora de comienzo, en UTC.</param>
/// <param name="Duracion">Cuanto dura la ventana.</param>
public sealed record CelebracionDeConcurso(
    int Mes,
    int Ordinal,
    bool SemanaCompleta,
    DayOfWeek Dia,
    TimeOnly HoraInicioUtc,
    TimeSpan Duracion)
{
    /// <summary>El recurso no fija la fecha de este concurso.</summary>
    public bool EsDesconocida => Mes is < 1 or > 12;
}

/// <summary>Todo lo que el programa sabe de un concurso.</summary>
/// <param name="Codigo">Identificador Cabrillo, que es el que va en <c>CONTEST_ID</c>.</param>
/// <param name="Nombre">Nombre para ensenar al operador.</param>
/// <param name="Patrocinador">Quien lo organiza.</param>
/// <param name="Estado">Cuanto se ha contrastado lo que hay aqui.</param>
public sealed record ReglaDeConcurso(
    string Codigo,
    string Nombre,
    string? Patrocinador,
    EstadoDeLasReglas Estado)
{
    /// <summary>Modos admitidos, en la forma de Cabrillo: <c>CW</c>, <c>SSB</c>, <c>RTTY</c>…</summary>
    public IReadOnlyList<string> Modos { get; init; } = [];

    /// <summary>Bandas admitidas. Vacio significa que el recurso no las limita.</summary>
    public IReadOnlyList<Banda> Bandas { get; init; } = [];

    /// <summary>Campos que se envian, en el orden en que se dicen.</summary>
    public IReadOnlyList<TipoDeIntercambio> Enviado { get; init; } = [];

    /// <summary>Campos que se reciben, en el orden en que se dicen.</summary>
    public IReadOnlyList<TipoDeIntercambio> Recibido { get; init; } = [];

    /// <summary>Reglas de puntuacion, en orden de evaluacion.</summary>
    public IReadOnlyList<ReglaDePuntos> Puntuacion { get; init; } = [];

    /// <summary>Reglas de multiplicador.</summary>
    public IReadOnlyList<ReglaDeMultiplicador> Multiplicadores { get; init; } = [];

    /// <summary>Cuando se celebra.</summary>
    public CelebracionDeConcurso? Celebracion { get; init; }

    /// <summary>Cuando deja de ser duplicado repetir a una estacion.</summary>
    public AmbitoDeDuplicado Duplicado { get; init; } = AmbitoDeDuplicado.Banda;

    /// <summary>Lo que hay que saber de este concurso y no cabe en los campos.</summary>
    public string? Notas { get; init; }

    /// <summary>Se envia un numero de serie que hay que ir aumentando.</summary>
    public bool UsaNumeroDeSerie => Enviado.Contains(TipoDeIntercambio.Serie);

    /// <summary>El programa sabe puntuar este concurso por su cuenta.</summary>
    public bool SabePuntuar => Puntuacion.Count > 0;

    /// <summary>
    /// Hay que avisar al operador de que el marcador es orientativo.
    /// </summary>
    /// <remarks>
    /// Se avisa siempre que las reglas no esten contrastadas con el organizador. Es
    /// preferible un aviso de mas a que alguien mande un log fiandose de una cuenta que
    /// nadie ha comprobado.
    /// </remarks>
    public bool NecesitaAviso => Estado != EstadoDeLasReglas.Verificado;

    /// <summary>La banda esta admitida por las bases que conoce el programa.</summary>
    /// <param name="banda">Banda del contacto.</param>
    public bool AdmiteBanda(Banda banda) => Bandas.Count == 0 || Bandas.Contains(banda);

    /// <summary>El modo esta admitido por las bases que conoce el programa.</summary>
    /// <param name="modo">Modo del contacto.</param>
    public bool AdmiteModo(Modo modo)
    {
        if (Modos.Count == 0 || Modos.Contains("MIXED", StringComparer.OrdinalIgnoreCase)) return true;
        var clase = ClaseDeModo.De(modo);
        return Modos.Any(m => ClaseDeModo.Equivale(m, clase));
    }
}

/// <summary>Cabecera del recurso: de cuando son los datos y cuantos hay.</summary>
/// <param name="Version">Version del formato.</param>
/// <param name="GeneradoEl">Cuando se fabrico el recurso.</param>
/// <param name="Concursos">Cuantos concursos trae.</param>
/// <param name="ConReglas">Cuantos de ellos traen reglas escritas.</param>
public sealed record CabeceraDelCatalogo(int Version, DateOnly GeneradoEl, int Concursos, int ConReglas);
