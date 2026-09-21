using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Tipos del protocolo UDP de WSJT-X, con los dos que anade JTDX por su cuenta.
/// </summary>
/// <remarks>
/// Los numeros del 0 al 12 son comunes. Del 13 al 16 solo existen en WSJT-X: JTDX los dejo
/// reservados y nunca los implemento. El 50 y el 51 son solo de JTDX y WSJT-X no los conoce.
/// </remarks>
public enum TipoMensajeWsjt : uint
{
    /// <summary>Latido periodico, cada 15 segundos. Lo emiten todos.</summary>
    Latido = 0,

    /// <summary>Estado de la instancia. Es el mensaje que diverge entre WSJT-X y JTDX.</summary>
    Estado = 1,

    /// <summary>Una linea decodificada en el periodo que acaba de terminar.</summary>
    Decodificacion = 2,

    /// <summary>Vaciado de la ventana de decodificaciones.</summary>
    Limpiar = 3,

    /// <summary>Peticion de llamar a una estacion decodificada. Va hacia el programa.</summary>
    Respuesta = 4,

    /// <summary>Contacto dado por cerrado.</summary>
    QsoRegistrado = 5,

    /// <summary>La instancia se cierra.</summary>
    Cierre = 6,

    /// <summary>Peticion de reenvio de las decodificaciones del periodo. Va hacia el programa.</summary>
    Repetir = 7,

    /// <summary>Peticion de cortar la transmision. Va hacia el programa.</summary>
    DetenerTx = 8,

    /// <summary>Texto libre para transmitir. Va hacia el programa.</summary>
    TextoLibre = 9,

    /// <summary>Decodificacion de WSPR.</summary>
    DecodificacionWspr = 10,

    /// <summary>Cambio de localizador. Va hacia el programa. JTDX lo dejo reservado.</summary>
    Ubicacion = 11,

    /// <summary>Contacto cerrado, esta vez en texto ADIF.</summary>
    AdifRegistrado = 12,

    /// <summary>Resaltado de un indicativo en la ventana. Solo WSJT-X.</summary>
    ResaltarIndicativo = 13,

    /// <summary>Cambio de configuracion por nombre. Solo WSJT-X.</summary>
    CambiarConfiguracion = 14,

    /// <summary>Configuracion de modo, periodo y demas. Solo WSJT-X.</summary>
    Configurar = 15,

    /// <summary>Datos de anotacion para el indicativo. Solo WSJT-X.</summary>
    Anotacion = 16,

    /// <summary>Fija el desplazamiento de transmision en hercios. Solo JTDX.</summary>
    FijarTxDeltaFreq = 50,

    /// <summary>Fija direccion y periodo de la llamada general y opcionalmente la dispara. Solo JTDX.</summary>
    DispararCq = 51,
}

/// <summary>Base de todos los mensajes que se saben leer. Todos empiezan por el identificador.</summary>
/// <param name="Id">Nombre de la instancia que lo envia.</param>
/// <param name="Tipo">Tipo del mensaje.</param>
public abstract record MensajeWsjt(string Id, TipoMensajeWsjt Tipo);

/// <summary>Latido: identifica la instancia y dice hasta que esquema entiende.</summary>
/// <param name="Id">Nombre de la instancia.</param>
/// <param name="EsquemaMaximo">Esquema mas alto que admite el programa.</param>
/// <param name="Version">Version del programa, por ejemplo <c>2.7.0</c>.</param>
/// <param name="Revision">Revision del programa.</param>
public sealed record LatidoWsjt(string Id, uint EsquemaMaximo, string? Version, string? Revision)
    : MensajeWsjt(Id, TipoMensajeWsjt.Latido);

/// <summary>
/// Estado de la instancia. A partir de <see cref="ModoRapido"/> el mensaje deja de ser comun:
/// WSJT-X sigue con el modo de operacion especial y cuatro campos mas, y JTDX pone en su lugar
/// un unico logico con quien transmite primero y ahi se acaba.
/// </summary>
/// <param name="Id">Nombre de la instancia.</param>
public sealed record EstadoWsjt(string Id) : MensajeWsjt(Id, TipoMensajeWsjt.Estado)
{
    /// <summary>Frecuencia del dial en hercios.</summary>
    public ulong DialHz { get; init; }

    /// <summary>Modo en uso, tal y como lo nombra el programa.</summary>
    public string? Modo { get; init; }

    /// <summary>Indicativo con el que se esta en QSO.</summary>
    public string? IndicativoDx { get; init; }

    /// <summary>Informe en curso.</summary>
    public string? Informe { get; init; }

    /// <summary>Modo de transmision, que puede no coincidir con el de recepcion.</summary>
    public string? ModoTx { get; init; }

    /// <summary>La transmision esta habilitada.</summary>
    public bool TxHabilitado { get; init; }

    /// <summary>Esta transmitiendo ahora mismo.</summary>
    public bool Transmitiendo { get; init; }

    /// <summary>Esta procesando el periodo recibido.</summary>
    public bool Decodificando { get; init; }

    /// <summary>Desplazamiento de recepcion en hercios dentro del ancho de banda de audio.</summary>
    public uint RxDf { get; init; }

    /// <summary>Desplazamiento de transmision en hercios.</summary>
    public uint TxDf { get; init; }

    /// <summary>Mi indicativo.</summary>
    public string? IndicativoDe { get; init; }

    /// <summary>Mi localizador.</summary>
    public string? LocatorDe { get; init; }

    /// <summary>Localizador del corresponsal.</summary>
    public string? LocatorDx { get; init; }

    /// <summary>El perro guardian ha cortado la transmision por falta de actividad.</summary>
    public bool PerroGuardianTx { get; init; }

    /// <summary>Submodo, cuando el modo lo tiene.</summary>
    public string? Submodo { get; init; }

    /// <summary>Esta en uno de los modos rapidos.</summary>
    public bool ModoRapido { get; init; }

    /// <summary>
    /// Modo de operacion especial de WSJT-X: concurso de campo, EU VHF, ARRL Field Day…
    /// Nulo cuando quien envia es JTDX, que en ese sitio pone otra cosa.
    /// </summary>
    public byte? ModoDeOperacionEspecial { get; init; }

    /// <summary>
    /// Se transmite en el periodo par. Es lo que JTDX pone donde WSJT-X pone el modo de
    /// operacion especial. Nulo cuando quien envia es WSJT-X.
    /// </summary>
    public bool? TxPrimero { get; init; }

    /// <summary>Tolerancia de frecuencia. Solo WSJT-X.</summary>
    public uint? ToleranciaDeFrecuencia { get; init; }

    /// <summary>Periodo de transmision y recepcion en segundos. Solo WSJT-X.</summary>
    public uint? PeriodoTr { get; init; }

    /// <summary>Nombre de la configuracion activa. Solo WSJT-X.</summary>
    public string? NombreDeConfiguracion { get; init; }

    /// <summary>Mensaje que se esta transmitiendo. Solo WSJT-X.</summary>
    public string? MensajeTx { get; init; }

    /// <summary>
    /// El mensaje termino donde termina el estado de JTDX. Es un indicio, no una prueba: un
    /// estado de WSJT-X truncado por la red tendria la misma pinta.
    /// </summary>
    public bool TerminoComoJtdx { get; init; }
}

/// <summary>Una linea decodificada.</summary>
/// <param name="Id">Nombre de la instancia.</param>
/// <param name="EsNueva">Es la primera vez que se muestra, no un reenvio.</param>
/// <param name="HoraDelDia">Momento del periodo, sin fecha: el protocolo no la manda.</param>
/// <param name="Decibelios">Relacion senal-ruido.</param>
/// <param name="DesfaseSegundos">Desfase temporal de la senal.</param>
/// <param name="DeltaFrecuenciaHz">Tono dentro del ancho de banda de audio.</param>
/// <param name="Modo">Modo, en la forma corta de una letra que usa el programa.</param>
/// <param name="Mensaje">Texto decodificado.</param>
public sealed record DecodificacionWsjt(
    string Id,
    bool EsNueva,
    TimeSpan HoraDelDia,
    int Decibelios,
    double DesfaseSegundos,
    uint DeltaFrecuenciaHz,
    string? Modo,
    string? Mensaje) : MensajeWsjt(Id, TipoMensajeWsjt.Decodificacion)
{
    /// <summary>La decodificacion es de poca confianza y puede ser falsa.</summary>
    public bool? BajaConfianza { get; init; }

    /// <summary>Procede de un fichero, no del aire.</summary>
    public bool? FueraDeAire { get; init; }
}

/// <summary>
/// Contacto cerrado. Los tres ultimos campos son de WSJT-X: JTDX no los envia, y como la
/// lectura es tolerante se quedan en nulo sin mas.
/// </summary>
/// <param name="Id">Nombre de la instancia.</param>
/// <param name="FinUtc">Fin del contacto.</param>
/// <param name="IndicativoDx">Indicativo del corresponsal.</param>
/// <param name="LocatorDx">Localizador del corresponsal.</param>
/// <param name="FrecuenciaTxHz">Frecuencia de transmision en hercios.</param>
/// <param name="Modo">Modo del contacto.</param>
/// <param name="InformeEnviado">Informe que se mando.</param>
/// <param name="InformeRecibido">Informe que se recibio.</param>
/// <param name="PotenciaTx">Potencia declarada, en texto.</param>
/// <param name="Comentarios">Comentario del contacto.</param>
/// <param name="Nombre">Nombre del corresponsal.</param>
/// <param name="InicioUtc">Inicio del contacto.</param>
/// <param name="IndicativoOperador">Operador a los mandos.</param>
/// <param name="MiIndicativo">Indicativo con el que se transmitio.</param>
/// <param name="MiLocator">Mi localizador.</param>
public sealed record QsoRegistradoWsjt(
    string Id,
    DateTimeOffset FinUtc,
    string? IndicativoDx,
    string? LocatorDx,
    ulong FrecuenciaTxHz,
    string? Modo,
    string? InformeEnviado,
    string? InformeRecibido,
    string? PotenciaTx,
    string? Comentarios,
    string? Nombre,
    DateTimeOffset InicioUtc,
    string? IndicativoOperador,
    string? MiIndicativo,
    string? MiLocator) : MensajeWsjt(Id, TipoMensajeWsjt.QsoRegistrado)
{
    /// <summary>Intercambio de concurso enviado. Solo WSJT-X.</summary>
    public string? IntercambioEnviado { get; init; }

    /// <summary>Intercambio de concurso recibido. Solo WSJT-X.</summary>
    public string? IntercambioRecibido { get; init; }

    /// <summary>Modo de propagacion ADIF. Solo WSJT-X.</summary>
    public string? ModoDePropagacion { get; init; }

    /// <summary>El mensaje traia los campos de concurso, cosa que JTDX nunca hace.</summary>
    public bool TraiaCamposDeConcurso => IntercambioEnviado is not null || IntercambioRecibido is not null;
}

/// <summary>Contacto cerrado en texto ADIF, que el programa envia ademas del anterior.</summary>
/// <param name="Id">Nombre de la instancia.</param>
/// <param name="TextoAdif">Registro ADIF completo, con su cabecera.</param>
public sealed record AdifRegistradoWsjt(string Id, string? TextoAdif)
    : MensajeWsjt(Id, TipoMensajeWsjt.AdifRegistrado);

/// <summary>La instancia se cierra y deja de emitir.</summary>
/// <param name="Id">Nombre de la instancia.</param>
public sealed record CierreWsjt(string Id) : MensajeWsjt(Id, TipoMensajeWsjt.Cierre);

/// <summary>
/// Mensaje de un tipo conocido cuyo contenido no hace falta aqui, o de un tipo que no se
/// conoce. Se conserva el identificador, que es lo unico que se necesita: sirve para saber
/// que la instancia sigue viva y, en el caso de los tipos 50 y 51, para saber que es JTDX.
/// </summary>
/// <param name="Id">Nombre de la instancia.</param>
/// <param name="Tipo">Tipo tal y como venia.</param>
/// <param name="Cuerpo">Bytes del mensaje detras del identificador.</param>
public sealed record MensajeSinDetallar(string Id, TipoMensajeWsjt Tipo, byte[] Cuerpo)
    : MensajeWsjt(Id, Tipo);

/// <summary>Por que se descarto un datagrama.</summary>
public enum MotivoDeDescarte
{
    /// <summary>No se descarto: hay mensaje.</summary>
    Ninguno,

    /// <summary>El datagrama no llega ni a cabecera.</summary>
    DemasiadoCorto,

    /// <summary>Los primeros cuatro bytes no son la magia de WSJT-X.</summary>
    MagiaIncorrecta,

    /// <summary>El esquema no es uno de los que se saben leer.</summary>
    EsquemaNoAdmitido,

    /// <summary>El mensaje se corta antes del identificador, que es lo minimo exigible.</summary>
    Truncado,
}

/// <summary>Resultado de leer un datagrama.</summary>
/// <param name="Mensaje">Mensaje leido, o nulo si se descarto.</param>
/// <param name="Motivo">Por que se descarto.</param>
/// <param name="Esquema">Esquema declarado en la cabecera.</param>
/// <param name="Detalle">Explicacion para la traza.</param>
public sealed record ResultadoDeAnalisis(
    MensajeWsjt? Mensaje,
    MotivoDeDescarte Motivo,
    uint Esquema,
    string? Detalle)
{
    /// <summary>Se pudo leer un mensaje.</summary>
    public bool HayMensaje => Mensaje is not null;

    /// <summary>
    /// Dialecto que este mensaje demuestra por si solo, sin mirar el identificador. Solo los
    /// tipos propios de JTDX prueban algo; el resto no distingue a nadie.
    /// </summary>
    public DialectoDigital DialectoProbado => Mensaje?.Tipo is
        TipoMensajeWsjt.FijarTxDeltaFreq or TipoMensajeWsjt.DispararCq
        ? DialectoDigital.Jtdx
        : DialectoDigital.Desconocido;
}
