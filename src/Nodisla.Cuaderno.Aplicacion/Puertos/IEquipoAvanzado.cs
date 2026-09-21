using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>
/// Un mando del equipo que se puede leer y accionar desde el ordenador.
/// </summary>
/// <remarks>
/// La lista sale del juego de ordenes CAT de un equipo moderno de gama alta. Ningun equipo
/// los tiene todos, y por eso el control declara cuales admite: la interfaz se construye a
/// partir de esa declaracion y no ensena mandos que no existen.
/// </remarks>
public enum MandoDeEquipo
{
    // ── Nivel y potencia ────────────────────────────────────────────────────
    /// <summary>Potencia de salida en vatios.</summary>
    Potencia,
    /// <summary>Ganancia de radiofrecuencia.</summary>
    GananciaRf,
    /// <summary>Ganancia del microfono.</summary>
    GananciaMicrofono,
    /// <summary>Volumen de audio.</summary>
    Volumen,
    /// <summary>Nivel del monitor de audio propio.</summary>
    Monitor,
    /// <summary>Compresion de voz.</summary>
    Compresor,

    // ── Recepcion ───────────────────────────────────────────────────────────
    /// <summary>Atenuador de entrada, en decibelios.</summary>
    Atenuador,
    /// <summary>Preamplificador de entrada.</summary>
    Preamplificador,
    /// <summary>Constante de tiempo del control automatico de ganancia.</summary>
    Agc,
    /// <summary>Supresor de ruido de impulsos.</summary>
    SupresorDeRuido,
    /// <summary>Nivel del supresor de ruido.</summary>
    NivelSupresorDeRuido,
    /// <summary>Reductor de ruido digital.</summary>
    ReductorDeRuido,
    /// <summary>Nivel del reductor de ruido.</summary>
    NivelReductorDeRuido,
    /// <summary>Filtro de muesca automatico.</summary>
    MuescaAutomatica,
    /// <summary>Filtro de muesca manual.</summary>
    MuescaManual,
    /// <summary>Frecuencia de la muesca manual, en hercios.</summary>
    FrecuenciaDeMuesca,
    /// <summary>Control de contorno del audio.</summary>
    Contorno,
    /// <summary>Frecuencia del contorno, en hercios.</summary>
    FrecuenciaDeContorno,
    /// <summary>Desplazamiento de la frecuencia intermedia, en hercios.</summary>
    DesplazamientoFi,
    /// <summary>Ancho del filtro de recepcion, en hercios.</summary>
    AnchoDeFiltro,
    /// <summary>Filtro de tejado, en hercios.</summary>
    FiltroDeTejado,
    /// <summary>Silenciador.</summary>
    Silenciador,

    // ── Telegrafia ──────────────────────────────────────────────────────────
    /// <summary>Tono de escucha de telegrafia, en hercios.</summary>
    TonoCw,
    /// <summary>Velocidad del manipulador, en palabras por minuto.</summary>
    VelocidadKeyer,
    /// <summary>Escucha entre caracteres.</summary>
    BreakIn,
    /// <summary>Retardo de la escucha entre caracteres, en milisegundos.</summary>
    RetardoBreakIn,

    // ── Transmision ─────────────────────────────────────────────────────────
    /// <summary>Paso a transmision por voz.</summary>
    Vox,
    /// <summary>Ganancia del circuito de voz.</summary>
    GananciaVox,
    /// <summary>Retardo del circuito de voz, en milisegundos.</summary>
    RetardoVox,
    /// <summary>Acoplador de antena.</summary>
    Sintonizador,
    /// <summary>Antena seleccionada.</summary>
    Antena,

    // ── Frecuencia ──────────────────────────────────────────────────────────
    /// <summary>Trabajo en dos frecuencias.</summary>
    Split,
    /// <summary>Desplazamiento de recepcion.</summary>
    Rit,
    /// <summary>Desplazamiento de recepcion, en hercios.</summary>
    DesplazamientoRit,
    /// <summary>Desplazamiento de transmision.</summary>
    Xit,
    /// <summary>Desplazamiento de transmision, en hercios.</summary>
    DesplazamientoXit,
}

/// <summary>Como se acciona un mando y entre que valores se mueve.</summary>
/// <param name="Mando">Mando descrito.</param>
/// <param name="Minimo">Valor minimo admitido.</param>
/// <param name="Maximo">Valor maximo admitido.</param>
/// <param name="Paso">Salto entre valores consecutivos.</param>
/// <param name="Unidad">Unidad para mostrar junto al valor: <c>W</c>, <c>Hz</c>, <c>dB</c>, <c>ppm</c>.</param>
/// <param name="Etiquetas">
/// Nombres de los valores cuando el mando es de posiciones y no continuo, por ejemplo las
/// constantes del control automatico de ganancia. El indice es el valor.
/// </param>
public sealed record RangoDeMando(
    MandoDeEquipo Mando,
    double Minimo,
    double Maximo,
    double Paso,
    string? Unidad = null,
    IReadOnlyList<string>? Etiquetas = null)
{
    /// <summary>El mando solo admite apagado y encendido.</summary>
    public bool EsInterruptor => Minimo == 0 && Maximo == 1 && Paso == 1 && Etiquetas is null;

    /// <summary>El mando tiene posiciones con nombre en vez de una escala continua.</summary>
    public bool EsDePosiciones => Etiquetas is { Count: > 0 };

    /// <summary>Ajusta un valor al rango y al paso del mando.</summary>
    public double Ajustar(double valor)
    {
        var acotado = Math.Clamp(valor, Minimo, Maximo);
        if (Paso <= 0) return acotado;
        var pasos = Math.Round((acotado - Minimo) / Paso, MidpointRounding.AwayFromZero);
        return Math.Clamp(Minimo + (pasos * Paso), Minimo, Maximo);
    }
}

/// <summary>Lectura instantanea de los medidores del equipo.</summary>
/// <param name="UnidadesS">Medidor de senal en unidades S, en recepcion.</param>
/// <param name="PotenciaVatios">Potencia de salida, en transmision.</param>
/// <param name="Roe">Relacion de onda estacionaria.</param>
/// <param name="Alc">Nivel del control automatico de nivel.</param>
/// <param name="CorrienteAmperios">Corriente de la etapa final.</param>
/// <param name="TensionVoltios">Tension de alimentacion.</param>
/// <param name="Compresion">Compresion de voz aplicada, en decibelios.</param>
/// <param name="LeidoUtc">Momento de la lectura.</param>
public sealed record LecturaDeMedidores(
    double? UnidadesS,
    double? PotenciaVatios,
    double? Roe,
    double? Alc,
    double? CorrienteAmperios,
    double? TensionVoltios,
    double? Compresion,
    DateTimeOffset LeidoUtc);

/// <summary>Una memoria del equipo.</summary>
/// <param name="Numero">Numero de memoria.</param>
/// <param name="Frecuencia">Frecuencia guardada.</param>
/// <param name="Modo">Modo guardado.</param>
/// <param name="Etiqueta">Nombre que el operador le haya puesto.</param>
/// <param name="Ocupada">La memoria tiene contenido.</param>
public sealed record MemoriaDeEquipo(
    int Numero,
    Frecuencia Frecuencia,
    Modo Modo,
    string? Etiqueta,
    bool Ocupada);

/// <summary>
/// Control completo de un equipo, mas alla de frecuencia, modo y PTT.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IControlEquipo"/> da lo minimo que necesita un cuaderno y vale para cualquier
/// equipo. Este puerto es para operar de verdad desde el ordenador: filtros, ruido, contorno,
/// telegrafia, medidores y memorias. Lo implementan los controles que hablan el CAT nativo del
/// equipo; el control generico por Hamlib puede implementar la parte que cubra.
/// </para>
/// <para>
/// Todo pasa por <see cref="Mandos"/>: la interfaz pregunta que sabe hacer este equipo y
/// construye los controles a partir de la respuesta. Asi un equipo modesto no ensena mandos
/// que no tiene, y uno completo los ensena todos sin tocar la interfaz.
/// </para>
/// <para>
/// El PTT no esta aqui a proposito: se pide siempre a <see cref="IVigilantePtt"/>.
/// </para>
/// </remarks>
public interface IEquipoAvanzado : IControlEquipo
{
    /// <summary>Nombre comercial del equipo, para mostrarlo.</summary>
    string NombreDelEquipo { get; }

    /// <summary>Mandos que este equipo admite.</summary>
    IReadOnlySet<MandoDeEquipo> Mandos { get; }

    /// <summary>Describe el rango de un mando. Nulo si el equipo no lo admite.</summary>
    RangoDeMando? Rango(MandoDeEquipo mando);

    /// <summary>Lee el valor actual de un mando. Nulo si el equipo no lo admite.</summary>
    Task<double?> LeerMandoAsync(MandoDeEquipo mando, CancellationToken ct = default);

    /// <summary>
    /// Acciona un mando. El valor se ajusta al rango y al paso antes de enviarlo, de modo que
    /// un valor fuera de rango no llega nunca al equipo.
    /// </summary>
    Task EscribirMandoAsync(MandoDeEquipo mando, double valor, CancellationToken ct = default);

    /// <summary>Lee todos los medidores de una vez.</summary>
    Task<LecturaDeMedidores> LeerMedidoresAsync(CancellationToken ct = default);

    /// <summary>Lee las memorias del equipo.</summary>
    Task<IReadOnlyList<MemoriaDeEquipo>> LeerMemoriasAsync(CancellationToken ct = default);

    /// <summary>Lleva el equipo a una memoria.</summary>
    Task IrAMemoriaAsync(int numero, CancellationToken ct = default);

    /// <summary>
    /// Envia una orden CAT en crudo y devuelve la respuesta.
    /// </summary>
    /// <remarks>
    /// Valvula de escape para lo que el modelo de mandos no cubra y para depurar. La interfaz
    /// debe ofrecerla con cautela: una orden mal escrita puede dejar el equipo en un estado raro.
    /// </remarks>
    Task<string?> OrdenEnCrudoAsync(string orden, CancellationToken ct = default);
}
