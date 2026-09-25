namespace Nodisla.Cuaderno.Concursos.Telegrafia;

/// <summary>Ajustes del manipulador y del entrenador.</summary>
public sealed record OpcionesDeTelegrafia
{
    /// <summary>Velocidad de las letras, en palabras por minuto.</summary>
    public int Ppm { get; init; } = 25;

    /// <summary>Velocidad aparente. Si es menor que <see cref="Ppm"/>, se manipula a la Farnsworth.</summary>
    public int? PpmEfectivas { get; init; }

    /// <summary>Tono de escucha en hercios.</summary>
    public double Tono { get; init; } = 700;

    /// <summary>Amplitud del tono, de 0 a 1.</summary>
    public double Amplitud { get; init; } = 0.5;

    /// <summary>Muestras por segundo del audio generado.</summary>
    public int FrecuenciaDeMuestreo { get; init; } = 48000;

    /// <summary>Construye el manipulador que describen estos ajustes.</summary>
    /// <returns>Un manipulador con la velocidad configurada.</returns>
    public Manipulador Manipulador() => new(Ppm, PpmEfectivas);

    /// <summary>Construye el generador de tonos que describen estos ajustes.</summary>
    /// <returns>Un generador con el tono y la amplitud configurados.</returns>
    public GeneradorDeTonos Generador() => new(FrecuenciaDeMuestreo, Tono, Amplitud);
}

/// <summary>
/// Saca unas muestras por la antena poniendo el equipo en transmision.
/// </summary>
/// <remarks>
/// <para>
/// Esta firma es la <b>unica</b> puerta de salida del manipulador de telegrafia hacia el aire, y
/// existe para que este modulo no pueda transmitir por su cuenta. Quien la conecta es el
/// arranque de la aplicacion, y la conecta a la emision vigilada del modulo de audio, que pide
/// la antena a <c>IVigilantePtt</c>, late solo mientras el audio avanza de verdad y suelta el
/// PTT pase lo que pase.
/// </para>
/// <para>
/// Si nadie la conecta, el manipulador <b>no transmite</b> y lo dice. Es lo correcto: un modulo
/// de concursos no tiene por que saber bajar un PTT, y desde luego no debe intentarlo si el
/// vigilante no esta puesto.
/// </para>
/// </remarks>
/// <param name="muestras">Muestras a emitir, un canal, coma flotante.</param>
/// <param name="motivo">Para que se transmite, para el registro.</param>
/// <param name="ct">Testigo de cancelacion; al cancelarse, se corta y se suelta el PTT.</param>
/// <returns>Cuando ha terminado de sonar y el equipo ya no esta en antena.</returns>
public delegate Task EmisionVigiladaDeMuestras(
    ReadOnlyMemory<float> muestras, string motivo, CancellationToken ct);

/// <summary>
/// Manipula telegrafia.
/// </summary>
/// <remarks>
/// Hay dos maneras de manipular y las dos caben detras de esta interfaz: generando audio y
/// sacandolo por el codec del equipo, o mandandole el texto al manipulador interno del propio
/// equipo por CAT. La segunda suena mejor y no ocupa la tarjeta de sonido, pero depende de lo
/// que sepa hacer cada radio. Hoy solo esta hecha la primera; la del equipo necesita un puerto
/// que aun no existe y esta anotado como pendiente.
/// </remarks>
public interface IManipuladorDeTelegrafia
{
    /// <summary>Velocidad actual, en palabras por minuto.</summary>
    int Ppm { get; }

    /// <summary>Hay algo manipulandose ahora mismo.</summary>
    bool Manipulando { get; }

    /// <summary>Manipula un texto y espera a que termine.</summary>
    /// <param name="texto">Texto a manipular, con las senales de procedimiento entre angulos.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Cuando ha terminado de manipularse.</returns>
    Task EnviarAsync(string texto, CancellationToken ct = default);

    /// <summary>
    /// Corta lo que se este manipulando ahora mismo.
    /// </summary>
    /// <remarks>
    /// Hace falta y se usa constantemente: en mitad de una llamada aparece el corresponsal y
    /// hay que callarse a media palabra. Un manipulador que no se pueda cortar no sirve para
    /// operar.
    /// </remarks>
    /// <returns>Cuando ha dejado de manipular.</returns>
    Task AbortarAsync();

    /// <summary>Cambia la velocidad.</summary>
    /// <param name="ppm">Velocidad nueva, en palabras por minuto.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Cuando el cambio esta hecho.</returns>
    Task CambiarVelocidadAsync(int ppm, CancellationToken ct = default);
}
