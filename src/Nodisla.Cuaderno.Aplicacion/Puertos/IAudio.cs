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

    /// <summary>Salta cuando se mide un desvio nuevo.</summary>
    event EventHandler<DesvioDelReloj>? DesvioMedido;

    /// <summary>Mide el desvio contra la fuente configurada.</summary>
    Task<DesvioDelReloj> MedirAsync(CancellationToken ct = default);

    /// <summary>Momento en que empieza la siguiente ventana del periodo indicado.</summary>
    /// <param name="periodo">Duracion de la ventana: 15 s en FT8, 7,5 s en FT4.</param>
    DateTimeOffset ProximaVentana(TimeSpan periodo);
}
