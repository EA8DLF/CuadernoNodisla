using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Cual de los dos VFO es.</summary>
public enum NombreDeVfo
{
    /// <summary>VFO A.</summary>
    A,

    /// <summary>VFO B.</summary>
    B,
}

/// <summary>
/// Como esta uno de los dos VFO del equipo.
/// </summary>
/// <param name="Nombre">Cual de los dos es.</param>
/// <param name="Frecuencia">Frecuencia que tiene puesta.</param>
/// <param name="Modo">Modo que tiene puesto.</param>
/// <param name="EsElActivo">Es el VFO que el operador esta manejando.</param>
/// <param name="Transmite">Por este VFO se transmite.</param>
/// <param name="Recibe">Por este VFO se recibe.</param>
/// <param name="AnchoDeFiltroHz">Ancho del filtro de recepcion, si el equipo lo informa.</param>
public sealed record EstadoDeUnVfo(
    NombreDeVfo Nombre,
    Frecuencia Frecuencia,
    Modo Modo,
    bool EsElActivo,
    bool Transmite,
    bool Recibe,
    int? AnchoDeFiltroHz)
{
    /// <summary>Banda en la que cae. Vacia si la frecuencia no es de aficionado.</summary>
    public Banda Banda => Banda.DesdeFrecuencia(Frecuencia);

    /// <summary>Un VFO del que todavia no se sabe nada.</summary>
    /// <param name="nombre">Cual de los dos.</param>
    public static EstadoDeUnVfo SinDatos(NombreDeVfo nombre) => new(
        nombre,
        Frecuencia.Cero,
        Modo.Vacio,
        EsElActivo: false,
        Transmite: false,
        Recibe: false,
        AnchoDeFiltroHz: null);
}

/// <summary>Como esta el equipo mirando a sus dos VFO a la vez.</summary>
/// <param name="A">VFO A.</param>
/// <param name="B">VFO B.</param>
/// <param name="Split">Se transmite por un VFO y se recibe por el otro.</param>
/// <param name="Rit">El desplazamiento de recepcion esta puesto.</param>
/// <param name="DesplazamientoRitHz">Cuanto desplaza la recepcion.</param>
/// <param name="Xit">El desplazamiento de transmision esta puesto.</param>
/// <param name="DesplazamientoXitHz">Cuanto desplaza la transmision.</param>
public sealed record EstadoDeLosVfos(
    EstadoDeUnVfo A,
    EstadoDeUnVfo B,
    bool Split,
    bool Rit,
    int DesplazamientoRitHz,
    bool Xit,
    int DesplazamientoXitHz)
{
    /// <summary>Lo que se sabe de un equipo del que aun no se ha leido nada.</summary>
    public static EstadoDeLosVfos SinDatos { get; } = new(
        EstadoDeUnVfo.SinDatos(NombreDeVfo.A),
        EstadoDeUnVfo.SinDatos(NombreDeVfo.B),
        Split: false,
        Rit: false,
        DesplazamientoRitHz: 0,
        Xit: false,
        DesplazamientoXitHz: 0);

    /// <summary>Los dos VFO, en orden.</summary>
    public IReadOnlyList<EstadoDeUnVfo> Ambos => [A, B];

    /// <summary>VFO por el que se transmite.</summary>
    public EstadoDeUnVfo DeTransmision => A.Transmite ? A : B;

    /// <summary>VFO por el que se recibe.</summary>
    public EstadoDeUnVfo DeRecepcion => A.Recibe ? A : B;
}

/// <summary>
/// Equipo que sabe informar de sus <b>dos</b> VFO a la vez.
/// </summary>
/// <remarks>
/// <para>
/// Es un puerto <b>aditivo y opcional</b>, aparte de <see cref="IControlEquipo"/>, porque
/// <see cref="EstadoDelEquipo"/> solo trae el VFO activo y una frecuencia de recepcion suelta.
/// La pantalla de operacion necesita A y B enteros —frecuencia, modo, quien transmite y ancho
/// de filtro de cada uno— para poder pintarlos juntos, que es como se opera de verdad.
/// </para>
/// <para>
/// El control que no lo implemente sigue funcionando: la pantalla ensena el VFO activo y deja
/// el otro en blanco. Por eso es una interfaz suelta y no miembros nuevos de
/// <see cref="IControlEquipo"/>, que obligarian a todos los controles a fingir que tienen dos.
/// </para>
/// </remarks>
public interface IEquipoConDosVfos
{
    /// <summary>Ultimo estado conocido de los dos VFO. Nunca lanza ni bloquea.</summary>
    EstadoDeLosVfos Vfos { get; }

    /// <summary>Lee del equipo el estado de los dos VFO.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task<EstadoDeLosVfos> LeerVfosAsync(CancellationToken ct = default);

    /// <summary>Hace activo el VFO indicado.</summary>
    /// <param name="vfo">VFO que pasa a ser el activo.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task PonerVfoActivoAsync(NombreDeVfo vfo, CancellationToken ct = default);

    /// <summary>Intercambia el contenido de los dos VFO.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task IntercambiarVfosAsync(CancellationToken ct = default);

    /// <summary>Copia el VFO activo sobre el otro.</summary>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task IgualarVfosAsync(CancellationToken ct = default);

    /// <summary>Pone la frecuencia de un VFO concreto.</summary>
    /// <param name="vfo">VFO que se mueve.</param>
    /// <param name="frecuencia">Frecuencia que se le pone.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task PonerFrecuenciaDeAsync(NombreDeVfo vfo, Frecuencia frecuencia, CancellationToken ct = default);

    /// <summary>Pone el modo de un VFO concreto.</summary>
    /// <param name="vfo">VFO que cambia de modo.</param>
    /// <param name="modo">Modo que se le pone.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task PonerModoDeAsync(NombreDeVfo vfo, Modo modo, CancellationToken ct = default);
}
