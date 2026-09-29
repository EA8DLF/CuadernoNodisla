using Nodisla.Cuaderno.Dominio.Entidades;

namespace Nodisla.Cuaderno.Servicios.Emparejamiento;

/// <summary>
/// Cuanto se le permite discrepar a un servicio respecto del cuaderno al buscar el contacto
/// que corresponde a una confirmacion.
/// </summary>
/// <remarks>
/// <para>
/// La tolerancia se declara aqui, con nombre y con el motivo escrito al lado, y no como una
/// constante suelta enterrada en el codigo de cada servicio. Emparejar es la parte dificil de
/// todo esto: una tolerancia demasiado estrecha tira confirmaciones buenas y una demasiado
/// ancha confirma el contacto equivocado, y en los dos casos el operador no se entera.
/// </para>
/// <para>
/// Los valores por omision no son inventados:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <b>LoTW: 30 minutos.</b> Es la regla del propio LoTW: dos contactos casan si las horas de
/// inicio no distan mas de 30 minutos, la banda es la misma y el modo es el mismo o del mismo
/// grupo. Usar menos aqui significaria rechazar confirmaciones que la ARRL si da por buenas.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>eQSL, Club Log y QRZ.com: 15 minutos.</b> No publican una ventana oficial. 15 minutos
/// cubre el desfase tipico entre dos relojes y entre el inicio y el fin de un contacto largo,
/// sin llegar a abarcar dos pasadas seguidas del mismo indicativo en la misma banda.
/// </description>
/// </item>
/// </list>
/// </remarks>
public sealed record ToleranciaDeEmparejamiento
{
    /// <summary>Diferencia maxima admitida entre la hora del cuaderno y la del servicio.</summary>
    public TimeSpan Tiempo { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Exigir que la banda coincida. Una banda vacia en cualquiera de los dos lados nunca
    /// descarta: pasa cuando la frecuencia cae en el borde de la tabla de ADIF y no hay banda
    /// que deducir, y en ese caso conviene que decidan el resto de criterios.
    /// </summary>
    public bool ExigirMismaBanda { get; init; } = true;

    /// <summary>
    /// Exigir que el modo coincida. Se compara despues de resolver el par modo y submodo, de
    /// forma que <c>FT4</c> del servicio y <c>MFSK/FT4</c> del cuaderno son el mismo modo.
    /// </summary>
    public bool ExigirMismoModo { get; init; } = true;

    /// <summary>
    /// Dar por bueno el modo cuando ambos pertenecen al mismo grupo (telegrafia, fonia, datos
    /// o imagen). Es lo que hace LoTW: <c>RTTY</c> frente a <c>DATA</c> es una confirmacion
    /// valida para los diplomas digitales.
    /// </summary>
    public bool AdmitirMismoGrupoDeModo { get; init; } = true;

    /// <summary>Tolerancia de LoTW: la ventana de 30 minutos que aplica la propia ARRL.</summary>
    public static ToleranciaDeEmparejamiento Lotw { get; } = new()
    {
        Tiempo = TimeSpan.FromMinutes(30),
    };

    /// <summary>Tolerancia de eQSL.cc.</summary>
    public static ToleranciaDeEmparejamiento Eqsl { get; } = new()
    {
        Tiempo = TimeSpan.FromMinutes(15),
    };

    /// <summary>Tolerancia de Club Log.</summary>
    public static ToleranciaDeEmparejamiento ClubLog { get; } = new()
    {
        Tiempo = TimeSpan.FromMinutes(15),
    };

    /// <summary>Tolerancia del cuaderno de QRZ.com.</summary>
    public static ToleranciaDeEmparejamiento QrzCom { get; } = new()
    {
        Tiempo = TimeSpan.FromMinutes(15),
    };

    /// <summary>Tolerancia por omision para un servicio sin tolerancia propia declarada.</summary>
    public static ToleranciaDeEmparejamiento PorOmision { get; } = new();

    /// <summary>Devuelve la tolerancia que corresponde a un medio de confirmacion.</summary>
    /// <param name="medio">Servicio del que viene la confirmacion.</param>
    public static ToleranciaDeEmparejamiento Para(MedioDeConfirmacion medio) => medio switch
    {
        MedioDeConfirmacion.Lotw => Lotw,
        MedioDeConfirmacion.Eqsl => Eqsl,
        MedioDeConfirmacion.ClubLog => ClubLog,
        MedioDeConfirmacion.QrzCom => QrzCom,
        _ => PorOmision,
    };
}
