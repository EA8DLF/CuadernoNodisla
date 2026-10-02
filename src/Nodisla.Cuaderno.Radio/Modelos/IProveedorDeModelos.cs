using Microsoft.Extensions.Logging;
using Nodisla.Cuaderno.Aplicacion.Puertos;

using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Modelos;

/// <summary>Un equipo que ha contestado por un puerto.</summary>
/// <param name="Puerto">Puerto serie (<c>COM3</c>).</param>
/// <param name="Baudios">Velocidad a la que contesto.</param>
/// <param name="Protocolo">Protocolo con el que contesto.</param>
/// <param name="Identificacion">
/// Lo que dijo al identificarse, en crudo: <c>0800</c> para <c>ID0800;</c>, <c>94</c> (hex)
/// para la respuesta CI-V <c>19 00</c> de un IC-7300.
/// </param>
/// <param name="Modelo">El modelo del catalogo, o nulo si el identificador no se conoce.</param>
public sealed record EquipoIdentificado(
    string Puerto,
    int Baudios,
    ProtocoloCat Protocolo,
    string Identificacion,
    ModeloDeEquipo? Modelo)
{
    /// <summary>Para el operador.</summary>
    public string Descripcion => Modelo is not null
        ? Textos.F("Servicios.Radio.EquipoEnPuerto", Modelo.NombreCompleto, Puerto, Baudios)
        : Textos.F("Servicios.Radio.EquipoDesconocidoEnPuerto", Protocolo, Identificacion, Puerto, Baudios);
}

/// <summary>
/// Un protocolo con sus modelos: sabe identificarlos por los puertos y crear su control.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asi se registra un protocolo nuevo</b>: una clase publica, no abstracta, con constructor
/// sin parametros, que implemente esta interfaz, en el ensamblado
/// <c>Nodisla.Cuaderno.Radio</c>. <see cref="CatalogoDeModelos"/> la encuentra sola por
/// reflexion al arrancar; no hay que tocar el catalogo ni la fabrica.
/// </para>
/// <para>
/// El control que devuelve <see cref="Crear"/> debe implementar como minimo
/// <see cref="IControlEquipo"/>, <see cref="IEquipoDeModelo"/> y, si puede transmitir, las
/// interfaces del vigilante (<c>IPttDirecto</c> y <c>ISueltaDeEmergenciaPtt</c>). Todo lo demas
/// (<see cref="IEquipoAvanzado"/>, <see cref="IEquipoConDosVfos"/>, <see cref="IEquipoConTeclas"/>,
/// <see cref="IEquipoConBotonera"/>, <see cref="IEquipoConEncendido"/>) es opcional: la interfaz
/// ensena solo lo que el control implementa.
/// </para>
/// </remarks>
public interface IProveedorDeModelos
{
    /// <summary>Protocolo que habla.</summary>
    ProtocoloCat Protocolo { get; }

    /// <summary>
    /// Orden en la deteccion automatica: se prueban primero los de numero mas bajo. El CAT
    /// nuevo de Yaesu va el 0 (es el del equipo del operador).
    /// </summary>
    int OrdenDeDeteccion { get; }

    /// <summary>Los modelos de este protocolo.</summary>
    IReadOnlyList<ModeloDeEquipo> Modelos { get; }

    /// <summary>
    /// Crea el control de un modelo sobre un puerto ya conocido. No abre nada: el puerto se
    /// abre en <see cref="IControlEquipo.ConectarAsync"/>.
    /// </summary>
    /// <param name="modelo">Modelo (de <see cref="Modelos"/>).</param>
    /// <param name="puerto">Puerto serie.</param>
    /// <param name="baudios">Velocidad.</param>
    /// <param name="opciones">Todos los ajustes de radio (espera de orden, via del PTT, direccion CI-V...).</param>
    /// <param name="registro">Donde anotar.</param>
    /// <returns>El control, sin conectar.</returns>
    IControlEquipo Crear(ModeloDeEquipo modelo, string puerto, int baudios, OpcionesDeRadio opciones, ILogger? registro);

    /// <summary>
    /// Pregunta por un puerto a unas velocidades quien hay al otro lado. Nunca transmite ni
    /// cambia nada en el equipo: solo ordenes de consulta.
    /// </summary>
    /// <param name="puerto">Puerto serie.</param>
    /// <param name="velocidades">Velocidades a probar, en orden. Vacio = las de sus modelos.</param>
    /// <param name="opciones">Ajustes de radio.</param>
    /// <param name="registro">Donde anotar.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>El equipo, o nulo si nadie contesta en este protocolo.</returns>
    Task<EquipoIdentificado?> IdentificarAsync(
        string puerto,
        IReadOnlyList<int> velocidades,
        OpcionesDeRadio opciones,
        ILogger? registro,
        CancellationToken ct);
}

/// <summary>Un control que sabe de que modelo es.</summary>
/// <remarks>
/// La interfaz lo usa para elegir el frontal dibujado (<see cref="ModeloDeEquipo.Frontal"/>) y
/// para decir «programado segun el manual» cuando <see cref="ModeloDeEquipo.ProbadoConRadio"/>
/// es falso.
/// </remarks>
public interface IEquipoDeModelo
{
    /// <summary>El modelo que se maneja.</summary>
    ModeloDeEquipo Modelo { get; }
}
