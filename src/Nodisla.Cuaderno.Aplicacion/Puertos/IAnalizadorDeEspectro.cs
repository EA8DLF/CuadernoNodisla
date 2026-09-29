namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Como coloca el equipo la escala de su analizador.</summary>
public enum ModoDelAnalizador
{
    /// <summary>No se sabe (trama sin ese dato).</summary>
    Desconocido = 0,

    /// <summary>CENTER: la frecuencia del VFO en el centro.</summary>
    Centro,

    /// <summary>CURSOR: la escala se mueve con el cursor.</summary>
    Cursor,

    /// <summary>FIX: escala fija, empieza en una frecuencia dada.</summary>
    Fijo,
}

/// <summary>En que punto esta el analizador.</summary>
public enum EstadoDelAnalizador
{
    /// <summary>No se ha arrancado, o se ha parado.</summary>
    Parado = 0,

    /// <summary>Falta la biblioteca de FTDI junto al programa.</summary>
    SinBiblioteca,

    /// <summary>El puente del equipo no aparece por USB (radio apagada o desconectada).</summary>
    SinDispositivo,

    /// <summary>Abierto, pero la radio no manda tramas (menu SCU-LAN10 en OFF).</summary>
    SinTramas,

    /// <summary>Llegan tramas.</summary>
    Recibiendo,

    /// <summary>Otro fallo; el motivo va en el texto.</summary>
    Fallo,
}

/// <summary>Una pasada del analizador de espectro del equipo.</summary>
/// <param name="Niveles">Niveles de 0 (nada) a 255 (tope), de frecuencia baja a alta.</param>
/// <param name="InicioHz">Frecuencia del primer punto.</param>
/// <param name="FinHz">Frecuencia del ultimo punto.</param>
/// <param name="VfoHz">Frecuencia del VFO que recibe, segun el propio equipo.</param>
/// <param name="SpanHz">Ancho que el equipo tiene elegido (SPAN).</param>
/// <param name="Modo">CENTER, CURSOR o FIX.</param>
/// <param name="Velocidad">
/// Velocidad (SPEED) como el <c>SS00</c> del equipo: 0 SLOW1, 1 SLOW2, 2 FAST1, 3 FAST2, 4 FAST3, 5 STOP.
/// </param>
/// <param name="TresD">La radio tiene puesta la vista 3DSS (si no, cascada).</param>
/// <param name="Ampliado">La radio tiene el analizador ampliado (EXPAND).</param>
public sealed record TrazaDeEspectro(
    byte[] Niveles,
    long InicioHz,
    long FinHz,
    long VfoHz,
    int SpanHz,
    ModoDelAnalizador Modo,
    int Velocidad,
    bool TresD = false,
    bool Ampliado = false);

/// <summary>
/// El analizador de espectro de la propia radio (no el del audio).
/// </summary>
/// <remarks>
/// Solo lee. Ninguna implementacion puede transmitir ni cambiar nada del equipo: el span, el
/// modo CENTER/FIX y la velocidad se eligen en la radio y aqui se muestran tal cual llegan.
/// El evento <see cref="TrazaRecibida"/> se lanza desde un hilo de fondo.
/// </remarks>
public interface IAnalizadorDeEspectro : IAsyncDisposable
{
    /// <summary>De donde salen las trazas, para decirlo en pantalla.</summary>
    string Origen { get; }

    /// <summary>Estado actual.</summary>
    EstadoDelAnalizador Estado { get; }

    /// <summary>Explicacion para el operador del estado actual (vacio si todo va bien).</summary>
    string Motivo { get; }

    /// <summary>Llega una traza nueva. Hilo de fondo.</summary>
    event EventHandler<TrazaDeEspectro>? TrazaRecibida;

    /// <summary>Cambia <see cref="Estado"/> o <see cref="Motivo"/>. Hilo de fondo.</summary>
    event EventHandler? EstadoCambiado;

    /// <summary>Empieza a leer. Si no puede, lo deja dicho en el estado; no lanza.</summary>
    void Iniciar();

    /// <summary>Deja de leer y suelta el dispositivo.</summary>
    Task DetenerAsync();
}
