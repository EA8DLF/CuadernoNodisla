using Nodisla.Cuaderno.Dominio.Entidades;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Resultado de subir contactos a un servicio.</summary>
/// <param name="Enviados">Contactos que el servicio acepto.</param>
/// <param name="Rechazados">Contactos que el servicio rechazo.</param>
/// <param name="Motivos">Por que rechazo cada uno, por clave natural del contacto.</param>
/// <param name="Duracion">Lo que tardo.</param>
public sealed record ResultadoDeSubida(
    int Enviados,
    int Rechazados,
    IReadOnlyDictionary<string, string> Motivos,
    TimeSpan Duracion)
{
    /// <summary>
    /// Avisos del servicio que no se pueden atribuir a un contacto concreto.
    /// </summary>
    /// <remarks>
    /// Existe porque hay servicios que avisan por registro sin decir de cual: eQSL da la fecha
    /// pero no el indicativo, asi que el aviso no se puede devolver a su QSO. Meterlos en
    /// <see cref="Motivos"/> con una clave inventada seria mentir sobre a que contacto se
    /// refieren; aqui salen tal cual y el operador los lee.
    /// </remarks>
    public IReadOnlyList<string> Avisos { get; init; } = [];
}

/// <summary>Una confirmacion descargada de un servicio.</summary>
/// <param name="Call">Indicativo del corresponsal.</param>
/// <param name="Band">Banda.</param>
/// <param name="Mode">Modo principal.</param>
/// <param name="InicioUtc">Instante del contacto, en UTC.</param>
/// <param name="Medio">Servicio que la emite.</param>
/// <param name="ConfirmadaUtc">Cuando el servicio la dio por buena.</param>
/// <param name="Verificada">El servicio la da por verificada, no solo por registrada.</param>
public sealed record ConfirmacionDescargada(
    Indicativo Call,
    Banda Band,
    string Mode,
    DateTimeOffset InicioUtc,
    MedioDeConfirmacion Medio,
    DateTimeOffset? ConfirmadaUtc,
    bool Verificada)
{
    /// <summary>Datos que el servicio aporta del corresponsal y que el cuaderno puede no tener.</summary>
    public IReadOnlyDictionary<string, string> CamposExtra { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Como fue una sincronizacion con un servicio.</summary>
/// <param name="Descargadas">Confirmaciones traidas.</param>
/// <param name="Emparejadas">Confirmaciones que casaron con un contacto del cuaderno.</param>
/// <param name="SinPareja">
/// Confirmaciones que el servicio da por buenas y que <b>no casan con ningun contacto</b>.
/// No son basura: suelen significar que el cuaderno tiene mal la hora, la banda o el modo,
/// o que falta el contacto. Hay que ensenarselas al operador, no tirarlas.
/// </param>
/// <param name="Duracion">Lo que tardo.</param>
public sealed record ResultadoDeSincronizacion(
    int Descargadas,
    int Emparejadas,
    IReadOnlyList<ConfirmacionDescargada> SinPareja,
    TimeSpan Duracion);

/// <summary>
/// Servicio donde se confirman contactos: LoTW, eQSL, ClubLog, QRZ.com…
/// </summary>
/// <remarks>
/// <para>
/// Dos reglas que valen para todos:
/// </para>
/// <para>
/// <b>Las credenciales no se guardan en claro.</b> El <c>config.ini</c> de Log4OM lleva una
/// clave de ClubLog legible por cualquiera que abra el fichero; aqui van cifradas con la
/// proteccion de datos del usuario de Windows y nunca aparecen en el registro ni en la interfaz.
/// </para>
/// <para>
/// <b>Subir no borra nada y bajar no pisa nada.</b> Una confirmacion descargada anade o mejora
/// el estado de un contacto; jamas empeora lo que ya habia ni modifica los datos del QSO. Si un
/// servicio contradice al cuaderno, se avisa y decide el operador.
/// </para>
/// </remarks>
public interface IServicioQsl
{
    /// <summary>Servicio que representa.</summary>
    MedioDeConfirmacion Medio { get; }

    /// <summary>Nombre para mostrar.</summary>
    string Nombre { get; }

    /// <summary>Hay credenciales configuradas y validas.</summary>
    bool EstaConfigurado { get; }

    /// <summary>
    /// El servicio admite subir contactos.
    /// </summary>
    /// <remarks>
    /// No todos hacen las dos cosas, y no siempre por la misma razon. LoTW puede descargar con
    /// usuario y contrasena pero <b>no puede subir sin TQSL instalado</b>, asi que esto puede
    /// ser falso en una maquina y cierto en otra. La interfaz tiene que preguntarlo, no
    /// suponerlo.
    /// </remarks>
    bool PuedeSubir { get; }

    /// <summary>
    /// El servicio admite descargar confirmaciones.
    /// </summary>
    /// <remarks>
    /// Falso, por ejemplo, en un servicio que solo acepta subidas. Devolver una lista vacia
    /// fingiendo que se ha mirado seria peor: el operador creeria que no tiene confirmaciones
    /// nuevas cuando lo que pasa es que nadie ha preguntado.
    /// </remarks>
    bool PuedeDescargar { get; }

    /// <summary>Comprueba las credenciales sin subir ni bajar nada.</summary>
    Task<bool> ComprobarCredencialesAsync(CancellationToken ct = default);

    /// <summary>Sube contactos al servicio.</summary>
    /// <param name="qsos">Contactos a subir.</param>
    /// <param name="progreso">Avisos de avance, para la barra de progreso.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task<ResultadoDeSubida> SubirAsync(
        IReadOnlyList<Qso> qsos,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default);

    /// <summary>
    /// Descarga las confirmaciones nuevas.
    /// </summary>
    /// <param name="desdeUtc">
    /// Momento a partir del cual pedir. Nulo para pedirlo todo, que en LoTW con muchos anos
    /// de cuaderno es lento y conviene evitar salvo la primera vez.
    /// </param>
    /// <param name="progreso">
    /// Avisos de avance. Una descarga de LoTW con anos de cuaderno tarda minutos: sin esto la
    /// interfaz solo puede ensenar un reloj de arena.
    /// </param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task<IReadOnlyList<ConfirmacionDescargada>> DescargarAsync(
        DateTimeOffset? desdeUtc,
        IProgress<ProgresoDeSincronizacion>? progreso = null,
        CancellationToken ct = default);
}

/// <summary>Estado de una tarea de sincronizacion en marcha.</summary>
/// <param name="Servicio">Servicio que se esta sincronizando.</param>
/// <param name="Hecho">Cuantas unidades se llevan.</param>
/// <param name="Total">Cuantas hay en total, si se sabe.</param>
/// <param name="Mensaje">Que esta pasando, en espanol, para la barra de progreso.</param>
public sealed record ProgresoDeSincronizacion(string Servicio, int Hecho, int? Total, string Mensaje);
