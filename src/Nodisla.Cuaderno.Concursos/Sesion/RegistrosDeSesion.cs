using Nodisla.Cuaderno.Concursos.Catalogo;
using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Concursos.Sesion;

/// <summary>Los datos de mi estacion que hacen falta para puntuar.</summary>
/// <param name="Indicativo">Indicativo con el que se opera.</param>
/// <param name="Dxcc">Entidad DXCC propia. Sin ella no se distingue el propio pais.</param>
/// <param name="Continente">Continente propio: <c>EU</c>, <c>AF</c>, <c>NA</c>…</param>
/// <param name="ZonaCq">Zona CQ propia.</param>
/// <param name="ZonaItu">Zona ITU propia.</param>
/// <param name="Locator">Localizador propio.</param>
/// <param name="Provincia">Provincia, para los concursos que la piden.</param>
/// <param name="Estado">Estado o provincia de W/VE, si procede.</param>
/// <param name="Potencia">Potencia declarada en vatios, para los concursos que la intercambian.</param>
/// <remarks>
/// Canarias es el ejemplo de por que esto no se puede deducir del indicativo sin mas:
/// EA8 esta en Africa, no en Europa, y eso cambia la puntuacion de todos los contactos con
/// la peninsula.
/// </remarks>
public sealed record DatosDeMiEstacion(
    Indicativo Indicativo,
    int Dxcc,
    string? Continente = null,
    int? ZonaCq = null,
    int? ZonaItu = null,
    Locator Locator = default,
    string? Provincia = null,
    string? Estado = null,
    double? Potencia = null);

/// <summary>Un contacto de concurso tal y como lo teclea el operador.</summary>
/// <param name="Call">Indicativo del corresponsal.</param>
/// <param name="Banda">Banda en que se hizo.</param>
/// <param name="Modo">Modo en que se hizo.</param>
/// <remarks>
/// Todo lo que se sabe del corresponsal viene ya resuelto de fuera: quien resuelve DXCC,
/// zonas y continente es el cuaderno, no la sesion de concurso. Asi esta clase no arrastra
/// el resolutor ni la base de datos, y se puede probar con una tabla de casos.
/// </remarks>
public sealed record ApunteDeConcurso(Indicativo Call, Banda Banda, Modo Modo)
{
    /// <summary>Frecuencia del contacto.</summary>
    public Frecuencia Frecuencia { get; init; }

    /// <summary>Momento del contacto, en UTC. Si no se pone, lo pone la sesion.</summary>
    public DateTimeOffset? InicioUtc { get; init; }

    /// <summary>Informe enviado. Si no se pone, se usa el de costumbre del modo.</summary>
    public Informe InformeEnviado { get; init; }

    /// <summary>Informe recibido.</summary>
    public Informe InformeRecibido { get; init; }

    /// <summary>Numero de serie recibido, en los concursos que lo usan.</summary>
    public int? SerieRecibida { get; init; }

    /// <summary>Intercambio recibido que no es numero de serie ni informe.</summary>
    public string? IntercambioRecibido { get; init; }

    /// <summary>Entidad DXCC del corresponsal.</summary>
    public int? Dxcc { get; init; }

    /// <summary>Continente del corresponsal.</summary>
    public string? Continente { get; init; }

    /// <summary>Zona CQ del corresponsal.</summary>
    public int? ZonaCq { get; init; }

    /// <summary>Zona ITU del corresponsal.</summary>
    public int? ZonaItu { get; init; }

    /// <summary>Localizador del corresponsal.</summary>
    public Locator Locator { get; init; }

    /// <summary>Provincia del corresponsal.</summary>
    public string? Provincia { get; init; }

    /// <summary>Estado o provincia de W/VE del corresponsal.</summary>
    public string? Estado { get; init; }

    /// <summary>Seccion ARRL del corresponsal.</summary>
    public string? Seccion { get; init; }

    /// <summary>Sociedad nacional o cargo IARU del corresponsal.</summary>
    public string? Sociedad { get; init; }

    /// <summary>Nombre del corresponsal.</summary>
    public string? Nombre { get; init; }

    /// <summary>Pais del corresponsal, para escribirlo en el cuaderno.</summary>
    public string? Pais { get; init; }
}

/// <summary>Por que un contacto es duplicado, o por que no lo es.</summary>
public enum EstadoDeDuplicado
{
    /// <summary>Estacion nueva: se puede trabajar.</summary>
    Nuevo,

    /// <summary>Ya trabajada en esta banda —y modo, si el concurso los separa—.</summary>
    Duplicado,

    /// <summary>
    /// Trabajada antes, pero en otra banda o en otro modo, asi que este contacto vale.
    /// Se dice aparte porque al operador le interesa saberlo: es una estacion conocida.
    /// </summary>
    TrabajadaEnOtraBanda,
}

/// <summary>Lo que hay que ensenar mientras el operador teclea el indicativo.</summary>
/// <param name="Estado">Si es duplicado o no.</param>
/// <param name="Puntos">Cuanto valdria el contacto.</param>
/// <param name="MultiplicadoresNuevos">Multiplicadores que traeria, ya escritos para ensenarlos.</param>
/// <remarks>
/// Esto se calcula con cada tecla. Por eso no lleva colecciones que haya que construir
/// cuando no hacen falta: si no hay multiplicadores nuevos, la lista es la vacia compartida.
/// </remarks>
public sealed record AvisoDeConcurso(
    EstadoDeDuplicado Estado,
    int Puntos,
    IReadOnlyList<string> MultiplicadoresNuevos)
{
    /// <summary>Aviso de estacion nueva sin nada que contar.</summary>
    public static AvisoDeConcurso Nada { get; } = new(EstadoDeDuplicado.Nuevo, 0, []);

    /// <summary>Es duplicado y no hay que trabajarlo.</summary>
    public bool EsDuplicado => Estado == EstadoDeDuplicado.Duplicado;

    /// <summary>El contacto traeria algun multiplicador nuevo.</summary>
    public bool TraeMultiplicador => MultiplicadoresNuevos.Count > 0;
}

/// <summary>Lo que devuelve la sesion al dar un contacto por bueno.</summary>
/// <param name="Qso">El contacto, ya listo para guardarlo en el cuaderno.</param>
/// <param name="Puntos">Puntos que suma.</param>
/// <param name="MultiplicadoresNuevos">Multiplicadores que ha estrenado.</param>
/// <param name="SerieEnviada">Numero de serie que se le envio, si el concurso los usa.</param>
/// <param name="EraDuplicado">Se registro aun siendo duplicado, porque el operador lo pidio.</param>
public sealed record RegistroDeConcurso(
    Qso Qso,
    int Puntos,
    IReadOnlyList<string> MultiplicadoresNuevos,
    int? SerieEnviada,
    bool EraDuplicado);

/// <summary>El marcador de la sesion.</summary>
/// <param name="Contactos">Contactos validos, sin contar los duplicados.</param>
/// <param name="Duplicados">Contactos registrados que resultaron duplicados.</param>
/// <param name="Puntos">Suma de puntos de los contactos validos.</param>
/// <param name="Multiplicadores">Cuantos multiplicadores distintos se llevan.</param>
/// <param name="Total">Puntos por multiplicadores, que es lo que se reclama.</param>
public sealed record MarcadorDeConcurso(int Contactos, int Duplicados, int Puntos, int Multiplicadores, int Total)
{
    /// <summary>Marcador de una sesion recien abierta.</summary>
    public static MarcadorDeConcurso Cero { get; } = new(0, 0, 0, 0, 0);
}

/// <summary>Como se ha de comportar una sesion de concurso.</summary>
/// <param name="Concurso">Reglas del concurso.</param>
/// <param name="MiEstacion">Datos de mi estacion.</param>
public sealed record OpcionesDeSesion(ReglaDeConcurso Concurso, DatosDeMiEstacion MiEstacion)
{
    /// <summary>Numero de serie con el que se empieza.</summary>
    public int PrimeraSerie { get; init; } = 1;

    /// <summary>
    /// Registrar un duplicado esta permitido.
    /// </summary>
    /// <remarks>
    /// Tiene que estarlo. En un pileup el operador a veces prefiere repetir el contacto a
    /// discutir con el corresponsal, y el organizador ya se encarga de descartarlo. Lo que
    /// no puede pasar es que sume puntos: se guarda con cero.
    /// </remarks>
    public bool AdmiteDuplicados { get; init; } = true;

    /// <summary>Operador a los mandos, si no es el titular.</summary>
    public string? Operador { get; init; }
}
