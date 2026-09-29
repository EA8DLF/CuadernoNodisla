namespace Nodisla.Cuaderno.Radio.Control.Rigctld;

/// <summary>Donde vive el <c>rigctld</c> de Hamlib y como se habla con el.</summary>
/// <remarks>
/// Se admiten las dos formas de trabajar: conectarse a un <c>rigctld</c> que ya este corriendo
/// —lo habitual si el operador comparte el equipo con otros programas, como WSJT-X o Log4OM— o
/// lanzarlo nosotros. Lo primero es lo de partida, porque dos programas peleandose por el mismo
/// puerto serie no acaba bien.
/// </remarks>
public sealed class OpcionesRigctld
{
    /// <summary>Puerto TCP de costumbre de <c>rigctld</c>.</summary>
    public const int PuertoDeCostumbre = 4532;

    /// <summary>Maquina donde escucha <c>rigctld</c>.</summary>
    public string Maquina { get; set; } = "127.0.0.1";

    /// <summary>Puerto TCP donde escucha <c>rigctld</c>.</summary>
    public int Puerto { get; set; } = PuertoDeCostumbre;

    /// <summary>
    /// Si lanzamos nosotros el proceso <c>rigctld</c>. Si es falso, hay que tenerlo ya arrancado.
    /// </summary>
    public bool LanzarElDemonio { get; set; }

    /// <summary>
    /// Ruta de <c>rigctld.exe</c>. Si no se indica, se busca en la carpeta de Hamlib que trae
    /// Log4OM, junto al programa y en el <c>PATH</c>.
    /// </summary>
    public string? RutaDeRigctld { get; set; }

    /// <summary>
    /// Numero de modelo de Hamlib (<c>rigctl -l</c>). El 1 es el equipo ficticio, que es lo
    /// unico que se debe usar mientras no haya radio de verdad conectada.
    /// </summary>
    public int ModeloDeEquipo { get; set; } = 1;

    /// <summary>Puerto serie del equipo, por ejemplo <c>COM3</c>. Vacio para el equipo ficticio.</summary>
    public string? PuertoSerie { get; set; }

    /// <summary>Velocidad del puerto serie. Cero deja la del modelo.</summary>
    public int Baudios { get; set; }

    /// <summary>Argumentos extra para <c>rigctld</c>, tal cual.</summary>
    public string? ArgumentosExtra { get; set; }

    /// <summary>Lo que se espera a que el equipo conteste una orden.</summary>
    public TimeSpan EsperaDeOrden { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Cada cuanto se pregunta al equipo como esta. Medio segundo va sobrado para seguir el
    /// dial sin inundar el puerto serie.
    /// </summary>
    public TimeSpan IntervaloDeSondeo { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Lo que se espera antes de reintentar la conexion tras un corte.</summary>
    public TimeSpan EsperaDeReconexion { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Tope de la espera entre reintentos de conexion.</summary>
    public TimeSpan EsperaMaximaDeReconexion { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Si, como ultimo recurso para soltar un PTT pegado, se mata el proceso <c>rigctld</c>
    /// que hayamos lanzado nosotros. Cerrar el puerto serie devuelve a recepcion a casi todos
    /// los equipos y es preferible a dejarlo transmitiendo.
    /// </summary>
    public bool MatarElDemonioComoUltimoRecurso { get; set; } = true;

    /// <summary>Como traducir los modos entre el equipo y el cuaderno.</summary>
    public TraductorDeModos Traductor { get; set; } = TraductorDeModos.PorOmision;
}
