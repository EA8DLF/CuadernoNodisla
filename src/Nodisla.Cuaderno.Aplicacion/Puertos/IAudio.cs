namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Un dispositivo de audio del sistema.</summary>
/// <param name="Id">Identificador con el que Windows lo abre.</param>
/// <param name="Nombre">Nombre para mostrar.</param>
/// <param name="EsDeEntrada">Es un dispositivo de captura.</param>
/// <param name="EsDelEquipo">
/// Corresponde al codec del equipo de radio conectado. Lo determina el control del equipo
/// cruzando el identificador de contenedor del USB, no el nombre: en esta maquina hay mas de
/// veinte dispositivos de sonido y varios se llaman parecido.
/// </param>
public sealed record DispositivoDeAudio(string Id, string Nombre, bool EsDeEntrada, bool EsDelEquipo);

/// <summary>Un bloque de muestras recien capturadas.</summary>
/// <param name="Muestras">Muestras en coma flotante, de -1 a 1, un solo canal.</param>
/// <param name="FrecuenciaDeMuestreo">Muestras por segundo.</param>
/// <param name="InstanteUtc">Momento en que empieza el bloque, segun el reloj corregido.</param>
public sealed record BloqueDeAudio(ReadOnlyMemory<float> Muestras, int FrecuenciaDeMuestreo, DateTimeOffset InstanteUtc);

/// <summary>Un hueco en el flujo de audio.</summary>
/// <param name="Muestras">Cuantas muestras se perdieron.</param>
/// <param name="InstanteUtc">Cuando empezo el hueco, segun el reloj corregido.</param>
public sealed record HuecoDeAudio(int Muestras, DateTimeOffset InstanteUtc);

/// <summary>
/// Captura de audio del equipo.
/// </summary>
/// <remarks>
/// El modem propio toma el audio del codec USB de la radio. Dos cosas que no son negociables:
/// el flujo <b>no puede perder bloques</b> —un hueco en mitad de un periodo de FT8 se lleva por
/// delante todas las decodificaciones de esos quince segundos— y el instante de cada bloque
/// tiene que venir del <b>reloj corregido</b>, no de la hora del sistema: FT8 se sincroniza a
/// ventanas de quince segundos y un reloj desviado dos segundos no decodifica nada.
/// </remarks>
public interface IEntradaDeAudio : IAsyncDisposable
{
    /// <summary>Dispositivos de captura disponibles.</summary>
    IReadOnlyList<DispositivoDeAudio> Dispositivos { get; }

    /// <summary>Dispositivo abierto, o nulo si no hay ninguno.</summary>
    DispositivoDeAudio? Abierto { get; }

    /// <summary>Nivel de entrada de los ultimos bloques, de 0 a 1, para el medidor.</summary>
    double Nivel { get; }

    /// <summary>Salta con cada bloque capturado.</summary>
    event EventHandler<BloqueDeAudio>? BloqueCapturado;

    /// <summary>
    /// Salta cuando se pierden muestras. Hay que ensenarselo al operador: explica por que un
    /// periodo no decodifico nada, y si pasa a menudo es que el ordenador no da abasto.
    /// </summary>
    /// <remarks>
    /// Lleva el instante del hueco y no solo la cuenta, para poder decir <b>que ventana</b>
    /// quedo tocada. Saber que se perdieron mil muestras no sirve de nada; saber que fue en la
    /// ventana de las 14:32:15 explica por que esa no decodifico.
    /// </remarks>
    event EventHandler<HuecoDeAudio>? MuestrasPerdidas;

    Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default);

    Task CerrarAsync(CancellationToken ct = default);
}

/// <summary>Reproduccion de audio hacia el equipo.</summary>
public interface ISalidaDeAudio : IAsyncDisposable
{
    /// <summary>
    /// Ultimo momento en que el audio <b>avanzo de verdad</b>, o nulo si no se puede saber.
    /// </summary>
    /// <remarks>
    /// Es lo que hace honesto el latido al vigilante de PTT. Latir por el mero hecho de estar
    /// dentro del bucle de emision no demuestra nada: si la tarjeta se cuelga con la cola
    /// llena, el bucle sigue vivo y el equipo se queda en antena. Latir solo cuando esto
    /// avanza convierte un cuelgue de audio en una suelta de PTT.
    ///
    /// Si la implementacion no puede saberlo, devuelve nulo y quien emita <b>no debe latir a
    /// ciegas</b>: mas vale que salte el tiempo maximo de transmision.
    /// </remarks>
    DateTimeOffset? UltimoAvanceUtc { get; }

    /// <summary>Dispositivos de reproduccion disponibles.</summary>
    IReadOnlyList<DispositivoDeAudio> Dispositivos { get; }

    /// <summary>Dispositivo abierto, o nulo si no hay ninguno.</summary>
    DispositivoDeAudio? Abierto { get; }

    Task AbrirAsync(string idDispositivo, int frecuenciaDeMuestreo = 48000, CancellationToken ct = default);

    Task CerrarAsync(CancellationToken ct = default);

    /// <summary>
    /// Reproduce un bloque de muestras y espera a que termine de sonar.
    /// </summary>
    /// <remarks>
    /// Esto <b>emite</b>. Como todo lo que pone el equipo en antena, va dentro de una
    /// transmision pedida a <see cref="IVigilantePtt"/>. El vigilante debe recibir un latido
    /// mientras suena, para que un cuelgue aqui no deje la radio transmitiendo.
    /// </remarks>
    Task ReproducirAsync(ReadOnlyMemory<float> muestras, CancellationToken ct = default);

    /// <summary>Corta lo que este sonando y vacia la cola.</summary>
    Task SilenciarAsync(CancellationToken ct = default);
}

/// <summary>Como esta el reloj del ordenador frente a la hora de verdad.</summary>
/// <param name="DesvioMs">
/// Cuanto adelanta el reloj del sistema, en milisegundos. Positivo significa adelantado.
/// </param>
/// <param name="Fuente">De donde salio la medida.</param>
/// <param name="MedidoUtc">Cuando se midio.</param>
/// <param name="EsFiable">La medida es de fiar y no un valor de partida.</param>
public sealed record DesvioDelReloj(double DesvioMs, string Fuente, DateTimeOffset MedidoUtc, bool EsFiable);

/// <summary>Como de bien esta el reloj para los modos digitales.</summary>
public enum CalidadDelReloj
{
    /// <summary>Todavia no se ha podido medir.</summary>
    SinMedir,
    /// <summary>El desvio no estorba.</summary>
    Bien,
    /// <summary>Se decodifica peor de lo que se podria. Conviene sincronizar.</summary>
    Regular,
    /// <summary>
    /// Ademas de decodificar mal, <b>se transmite fuera de ventana</b>.
    /// </summary>
    /// <remarks>
    /// Este escalon no es una molestia propia: es una molestia <b>a los demas</b>. Quien
    /// transmite desalineado ocupa el periodo de otros y ensucia sus decodificaciones sin
    /// enterarse. Por eso se dice con esas palabras y no con un tecnicismo.
    /// </remarks>
    FueraDeVentana,
}

/// <summary>El estado del reloj, ya interpretado y listo para mostrar.</summary>
/// <param name="Desvio">La medida en bruto.</param>
/// <param name="Calidad">En que escalon cae.</param>
/// <param name="Veredicto">Una frase en espanol que dice como esta.</param>
/// <param name="Consejo">Que hacer al respecto, o vacio si no hay nada que hacer.</param>
/// <param name="DesvioParaMostrar">El desvio escrito para leer: «-1,29 s», «+350 ms».</param>
public sealed record EstadoDelReloj(
    DesvioDelReloj Desvio,
    CalidadDelReloj Calidad,
    string Veredicto,
    string Consejo,
    string DesvioParaMostrar)
{
    /// <summary>Hay algo que el operador deberia hacer.</summary>
    public bool HayQueHacerAlgo => Calidad is CalidadDelReloj.Regular or CalidadDelReloj.FueraDeVentana;

    /// <summary>La medida es de fiar.</summary>
    public bool EsFiable => Desvio.EsFiable;
}

/// <summary>Por que via se puso el reloj en hora.</summary>
public enum ViaDeSincronizacion
{
    /// <summary>Se escribio la hora del sistema una vez.</summary>
    HoraPuestaAMano,
    /// <summary>Se configuro el servicio de hora de Windows contra un servidor.</summary>
    ServicioConfigurado,
}

/// <summary>
/// Como fue un intento de poner el reloj en hora.
/// </summary>
/// <remarks>
/// No se llama «resultado de sincronizacion» a secas porque ese nombre ya es el de las
/// sincronizaciones con los servicios de QSL, y son cosas distintas.
/// </remarks>
/// <param name="Hecho">Se consiguio.</param>
/// <param name="Via">Por que camino.</param>
/// <param name="Mensaje">Que paso, en espanol, para ensenarselo al operador.</param>
/// <param name="Detalle">
/// Informacion de apoyo: cuando no se ha podido, las ordenes exactas para hacerlo a mano.
/// </param>
/// <param name="DesvioAntesMs">Desvio antes de corregir.</param>
/// <param name="DesvioDespuesMs">Desvio despues, si se pudo volver a medir.</param>
public sealed record ResultadoDePuestaEnHora(
    bool Hecho,
    ViaDeSincronizacion Via,
    string Mensaje,
    string? Detalle,
    double? DesvioAntesMs,
    double? DesvioDespuesMs);

/// <summary>
/// Pone en hora el reloj del ordenador, si el operador lo pide.
/// </summary>
/// <remarks>
/// <b>Nunca por su cuenta.</b> Es el reloj de la maquina de quien opera, no el nuestro: lo
/// corregimos solo cuando lo pulsa. Cambiar la hora del sistema exige permisos de
/// administrador, y cuando no los hay <b>no se falla con un error de acceso denegado</b>: se
/// explica en espanol y se ofrece la alternativa, que es dejar el servicio de hora de Windows
/// configurado contra un buen servidor —lo que ademas arregla el problema para siempre y no
/// solo hoy—.
/// </remarks>
public interface ISincronizadorDeHora
{
    /// <summary>Se puede poner el reloj en hora desde aqui, con los permisos que hay.</summary>
    bool SePuedePonerEnHora { get; }

    /// <summary>Cuando se sincronizo por ultima vez desde el programa.</summary>
    DateTimeOffset? UltimaSincronizacion { get; }

    /// <summary>Ordenes para hacerlo a mano, por si no hay permisos.</summary>
    string InstruccionesParaHacerloAMano { get; }

    /// <summary>Mide y escribe la hora del sistema.</summary>
    Task<ResultadoDePuestaEnHora> PonerElRelojEnHoraAsync(CancellationToken ct = default);

    /// <summary>Deja el servicio de hora de Windows apuntando a un buen servidor.</summary>
    Task<ResultadoDePuestaEnHora> ConfigurarServicioDeHoraAsync(CancellationToken ct = default);
}

/// <summary>
/// La hora, corregida, que usa el modem digital.
/// </summary>
/// <remarks>
/// FT8 trabaja en ventanas de quince segundos y FT4 de siete y medio, todas alineadas al reloj
/// universal. Un ordenador con el reloj un segundo desviado decodifica mal; con dos, no
/// decodifica nada y ademas <b>transmite fuera de ventana</b>, molestando a los demas sin
/// enterarse. Por eso el desvio se mide, se corrige y <b>se ensena al operador</b>: es la
/// primera cosa que hay que mirar cuando el modem no saca nada.
/// </remarks>
public interface IRelojDelModem
{
    /// <summary>Hora corregida, en UTC.</summary>
    DateTimeOffset Ahora { get; }

    /// <summary>Ultimo desvio medido.</summary>
    DesvioDelReloj Desvio { get; }

    /// <summary>El estado del reloj ya interpretado, listo para pintar sin calcular nada.</summary>
    EstadoDelReloj Estado { get; }

    /// <summary>
    /// Salta cuando se mide un desvio nuevo, con el veredicto ya hecho.
    /// </summary>
    /// <remarks>
    /// Lleva el estado y no solo la medida para que la ventana no tenga que saber cuantos
    /// milisegundos son demasiados. Esa regla es del reloj, no de la pantalla.
    /// </remarks>
    event EventHandler<EstadoDelReloj>? DesvioMedido;

    /// <summary>
    /// Mide el desvio contra la fuente configurada.
    /// </summary>
    /// <param name="forzar">
    /// Medir ahora aunque haya una medida reciente guardada. Hace falta antes de corregir el
    /// reloj: corregir con una medida de hace media hora mete un error nuevo en vez de quitarlo.
    /// </param>
    /// <param name="ct">Testigo de cancelacion.</param>
    Task<DesvioDelReloj> MedirAsync(bool forzar = false, CancellationToken ct = default);

    /// <summary>Momento en que empieza la siguiente ventana del periodo indicado.</summary>
    /// <param name="periodo">Duracion de la ventana: 15 s en FT8, 7,5 s en FT4.</param>
    DateTimeOffset ProximaVentana(TimeSpan periodo);
}
