using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Modos.Rtty;

/// <summary>Cómo terminó una transmisión de RTTY.</summary>
public enum FinDelEnvioRtty
{
    /// <summary>Se vació la cola: no quedaba nada más que mandar.</summary>
    Completo,

    /// <summary>El operador la paró, o el vigilante soltó la antena por su cuenta.</summary>
    Parado,

    /// <summary>Se llegó al tiempo máximo de transmisión.</summary>
    TiempoAgotado,

    /// <summary>Falló algo generando el audio o hablando con la salida.</summary>
    Fallo,
}

/// <summary>Foto del emisor, para la pantalla.</summary>
/// <param name="EnAntena">El PTT está arriba por esta transmisión.</param>
/// <param name="Enviando">El trozo de texto que se está generando y reproduciendo ahora.</param>
/// <param name="Pendiente">Lo que falta por mandar, encolado detrás.</param>
public sealed record EstadoDelEmisorRtty(bool EnAntena, string Enviando, string Pendiente)
{
    /// <summary>Sin transmitir.</summary>
    public static EstadoDelEmisorRtty Parado { get; } = new(false, string.Empty, string.Empty);
}

/// <summary>Tiempos, tono y parámetros de línea del emisor de RTTY.</summary>
public sealed class OpcionesDelEmisorRtty
{
    /// <summary>
    /// Tope de una transmisión de RTTY. Pasado, se para en el primer hueco entre caracteres y se
    /// suelta el PTT. Por detrás sigue el tope del vigilante, que no depende de nada de aquí.
    /// </summary>
    public TimeSpan TiempoMaximo { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Cada cuánto se late al vigilante mientras se reproduce el audio.</summary>
    public TimeSpan Latido { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Muestras por segundo del audio que se genera.</summary>
    public int FrecuenciaDeMuestreo { get; set; } = 48000;

    /// <summary>Amplitud de pico del tono generado.</summary>
    public double Amplitud { get; set; } = 0.5;
}

/// <summary>
/// Manda RTTY: AFSK generado por el propio programa (<see cref="GeneradorRtty"/>), sacado por una
/// salida de audio real con el PTT sostenido por <see cref="IVigilantePtt"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>RTTY aquí es AFSK, no telegrafía por CAT.</b> A diferencia de <c>EmisorCw</c> (que manipula
/// el equipo por el puerto serie), esto son dos tonos de audio, igual que FT8: por eso el camino
/// es el de <c>ModemPropio.EmitirAsync</c> —<see cref="ISalidaDeAudio"/> y
/// <see cref="IVigilantePtt"/>— y no hay ningún acceso a <c>IControlEquipo</c> ni al CAT.
/// </para>
/// <para>
/// <b>Continuo, no por ventanas.</b> Se teclea texto y se va encolando (<see cref="Encolar"/>)
/// mientras haya una transmisión en marcha; <see cref="TransmitirAsync"/> empieza una, pide la
/// antena <b>una sola vez</b> (no una vez por carácter), la mantiene con un latido mientras suena
/// el audio, y la suelta en cuanto la cola se vacía, se cancela o falla algo — siempre en un
/// <c>finally</c>, como manda la casa.
/// </para>
/// <para>
/// <b>Imposible transmitir por descuido.</b> Sin salida de audio y sin vigilante, igual que el
/// módem propio, este emisor se niega a emitir en vez de buscar un camino alternativo.
/// </para>
/// </remarks>
public sealed class EmisorRtty
{
    private readonly ISalidaDeAudio? _salida;
    private readonly IVigilantePtt? _vigilante;
    private readonly OpcionesDelEmisorRtty _opciones;
    private readonly ILogger _registro;
    private readonly object _candado = new();
    private readonly LinkedList<string> _cola = new();

    private CancellationTokenSource? _cts;
    private bool _enviando;
    private EstadoDelEmisorRtty _estado = EstadoDelEmisorRtty.Parado;

    /// <summary>Monta el emisor.</summary>
    /// <param name="salida">Salida de audio real. Sin ella no se puede emitir.</param>
    /// <param name="vigilante">El vigilante del PTT: el único camino para salir al aire.</param>
    /// <param name="opciones">Tiempos y tono; nulo, los de partida.</param>
    /// <param name="registro">Donde anotar.</param>
    public EmisorRtty(ISalidaDeAudio? salida, IVigilantePtt? vigilante, OpcionesDelEmisorRtty? opciones = null, ILogger? registro = null)
    {
        _salida = salida;
        _vigilante = vigilante;
        _opciones = opciones ?? new OpcionesDelEmisorRtty();
        _registro = registro ?? NullLogger.Instance;
    }

    /// <summary>Cambia algo de <see cref="Estado"/>. Puede saltar en cualquier hilo.</summary>
    public event EventHandler<EstadoDelEmisorRtty>? EstadoCambiado;

    /// <summary>Termina una transmisión. Puede saltar en cualquier hilo.</summary>
    public event EventHandler<FinDelEnvioRtty>? Terminado;

    /// <summary>Los tiempos y el tono, para cambiarlos desde los ajustes.</summary>
    public OpcionesDelEmisorRtty Opciones => _opciones;

    /// <summary>Tono de marca con el que se genera el audio.</summary>
    public double TonoDeMarcaHz { get; set; } = 1500;

    /// <summary>Baudios, desplazamiento, parada e inversión con los que se genera el audio.</summary>
    public ParametrosRtty Parametros { get; set; } = new();

    /// <summary>Hay una transmisión de RTTY en marcha.</summary>
    public bool Enviando
    {
        get
        {
            lock (_candado) return _enviando;
        }
    }

    /// <summary>La foto de ahora.</summary>
    public EstadoDelEmisorRtty Estado
    {
        get
        {
            lock (_candado) return _estado;
        }
    }

    /// <summary>El último fallo, si la última transmisión terminó en <see cref="FinDelEnvioRtty.Fallo"/>.</summary>
    public Exception? UltimoFallo { get; private set; }

    /// <summary>Por qué no se puede mandar RTTY ahora, o nulo si se puede.</summary>
    public string? PorQueNoPuede => _salida is null || _vigilante is null
        ? Textos.T("Servicios.Modos.Rtty.NoPuedeEmitir")
        : _vigilante.MotivoDeBloqueo;

    /// <summary>
    /// Añade texto a la transmisión en marcha. Devuelve falso si no hay ninguna (entonces hay que
    /// empezar una con <see cref="TransmitirAsync"/>).
    /// </summary>
    /// <param name="texto">Lo que se añade.</param>
    /// <returns>Verdadero si se añadió.</returns>
    public bool Encolar(string texto)
    {
        lock (_candado)
        {
            if (!_enviando) return false;
            if (!string.IsNullOrEmpty(texto)) _cola.AddLast(texto);
            _estado = _estado with { Pendiente = Pendiente() };
        }

        Avisar();
        return true;
    }

    /// <summary>
    /// Pide la antena al vigilante y manda el texto generando su audio con
    /// <see cref="GeneradorRtty"/>. Si <paramref name="texto"/> está vacío pero luego llega algo
    /// por <see cref="Encolar"/>, también se manda: lo que importa es que haya una transmisión
    /// abierta esperando.
    /// </summary>
    /// <param name="texto">El primer trozo a mandar (puede estar vacío si solo se abre la cola).</param>
    /// <param name="motivo">Para el registro del vigilante.</param>
    /// <param name="ct">Testigo de cancelación.</param>
    /// <returns>Cómo terminó.</returns>
    /// <exception cref="InvalidOperationException">Si ya hay una en marcha o falta salida o vigilante.</exception>
    public async Task<FinDelEnvioRtty> TransmitirAsync(string texto, string motivo, CancellationToken ct = default)
    {
        if (_salida is null || _vigilante is null)
            throw new InvalidOperationException(Textos.T("Servicios.Modos.Rtty.NoPuedeEmitir"));

        CancellationTokenSource cts;
        lock (_candado)
        {
            if (_enviando) throw new InvalidOperationException(Textos.T("Servicios.Modos.Rtty.YaEnviando"));
            _enviando = true;
            _cola.Clear();
            if (!string.IsNullOrEmpty(texto)) _cola.AddLast(texto);
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = cts;
            UltimoFallo = null;
        }

        var fin = FinDelEnvioRtty.Completo;
        ITransmisionEnCurso? antena = null;
        var inicio = DateTimeOffset.UtcNow;
        var tope = Tope();
        var juego = JuegoBaudot.Letras;

        try
        {
            antena = await _vigilante.PedirAntenaAsync(motivo, cts.Token).ConfigureAwait(false);
            Poner(e => e with { EnAntena = true });

            using var latido = new Timer(_ => antena.Latir(), null, TimeSpan.Zero, _opciones.Latido);

            var primero = true;
            while (Tomar(out var trozo))
            {
                cts.Token.ThrowIfCancellationRequested();
                if (DateTimeOffset.UtcNow - inicio >= tope)
                {
                    fin = FinDelEnvioRtty.TiempoAgotado;
                    break;
                }

                if (!antena.EnAntena)
                {
                    fin = FinDelEnvioRtty.Parado;
                    break;
                }

                Poner(e => e with { Enviando = trozo });

                float[] audio;
                try
                {
                    audio = GeneradorRtty.Generar(
                        trozo, TonoDeMarcaHz, _opciones.FrecuenciaDeMuestreo, Parametros, _opciones.Amplitud,
                        silencioDelante: primero ? 0.2 : 0, silencioDetras: 0.2, juegoInicial: juego);
                    juego = JuegoFinal(trozo, juego);
                }
                catch (FormatException ex)
                {
                    throw new ArgumentException(ex.Message, nameof(texto), ex);
                }

                primero = false;
                await _salida.ReproducirAsync(audio, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            fin = FinDelEnvioRtty.Parado;
        }
        catch (Exception ex)
        {
            fin = FinDelEnvioRtty.Fallo;
            UltimoFallo = ex;
            _registro.LogError(ex, "Falló la transmisión de RTTY.");
        }
        finally
        {
            try
            {
                // Pase lo que pase, lo que quede sonando se corta antes de soltar el PTT.
                await _salida.SilenciarAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "No se ha podido silenciar la salida de audio al terminar RTTY.");
            }

            if (antena is not null)
            {
                try
                {
                    await antena.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _registro.LogError(ex, "No se ha podido bajar el PTT al terminar RTTY.");
                }
            }

            lock (_candado)
            {
                _enviando = false;
                _cola.Clear();
                _cts = null;
                _estado = EstadoDelEmisorRtty.Parado;
            }

            cts.Dispose();
            Avisar();
            _registro.LogInformation("RTTY terminado: {Fin}.", fin);
            try
            {
                Terminado?.Invoke(this, fin);
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "Un suscriptor del fin de RTTY lanzó una excepción.");
            }
        }

        return fin;
    }

    /// <summary>
    /// Para en el acto: corta lo que esté sonando y baja el PTT por el vigilante (que prueba todas
    /// sus vías si la normal falla). Se puede pulsar siempre, haya o no transmisión.
    /// </summary>
    public async Task PararAsync()
    {
        CancellationTokenSource? cts;
        lock (_candado)
        {
            cts = _cts;
            _cola.Clear();
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ya había terminado.
        }

        if (_salida is not null)
        {
            try
            {
                await _salida.SilenciarAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "No se ha podido silenciar la salida de audio al parar RTTY.");
            }
        }

        if (_vigilante is null) return;
        try
        {
            await _vigilante.SoltarYaAsync(MotivoDeSuelta.Panico).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogError(ex, "No se ha podido soltar el PTT al parar RTTY.");
            throw;
        }
    }

    private TimeSpan Tope()
    {
        var tope = _opciones.TiempoMaximo;
        if (tope <= TimeSpan.Zero) tope = TimeSpan.FromSeconds(1);
        return _vigilante is not null && tope > _vigilante.TiempoMaximo ? _vigilante.TiempoMaximo : tope;
    }

    private bool Tomar(out string trozo)
    {
        lock (_candado)
        {
            if (_cola.First is not { } primero)
            {
                trozo = string.Empty;
                return false;
            }

            trozo = primero.Value;
            _cola.RemoveFirst();
            _estado = _estado with { Pendiente = Pendiente() };
            return true;
        }
    }

    private string Pendiente() => string.Join(' ', _cola);

    /// <summary>
    /// El juego (LETRAS/CIFRAS) en el que queda el receptor tras un trozo, con USOS aplicado:
    /// misma cuenta que lleva <see cref="GeneradorRtty.Codigos"/>, pero sin generar los códigos.
    /// Se necesita para que el trozo siguiente arranque en el juego correcto y no repita ni se
    /// salte un cambio de juego en el borde entre dos llamadas a <see cref="Encolar"/>.
    /// </summary>
    private static JuegoBaudot JuegoFinal(string texto, JuegoBaudot juegoInicial)
    {
        var juego = juegoInicial;
        foreach (var c in texto.ToUpperInvariant())
        {
            var requerido = TablaBaudot.JuegoRequerido(c, juego) ?? throw new FormatException(
                $"El carácter '{c}' no se puede mandar en RTTY: no está en la tabla Baudot.");
            juego = requerido;
            if (c == ' ' && juego == JuegoBaudot.Cifras) juego = JuegoBaudot.Letras; // USOS
        }

        return juego;
    }

    private void Poner(Func<EstadoDelEmisorRtty, EstadoDelEmisorRtty> cambio)
    {
        lock (_candado) _estado = cambio(_estado);
        Avisar();
    }

    private void Avisar()
    {
        var estado = Estado;
        try
        {
            EstadoCambiado?.Invoke(this, estado);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "Un suscriptor del estado de RTTY lanzó una excepción.");
        }
    }
}
