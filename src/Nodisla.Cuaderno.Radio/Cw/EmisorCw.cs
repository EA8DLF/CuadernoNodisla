using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Radio.Cw;

/// <summary>Un trozo de texto que se manda a una velocidad.</summary>
/// <param name="Texto">El texto, con prosignos entre angulos.</param>
/// <param name="Wpm">Palabras por minuto.</param>
public sealed record TrozoCw(string Texto, int Wpm);

/// <summary>Como termino una transmision de telegrafia.</summary>
public enum FinDelEnvioCw
{
    /// <summary>Salio todo el texto.</summary>
    Completo,

    /// <summary>El operador la paro (Esc o «PARAR CW») o el vigilante corto la antena.</summary>
    Parado,

    /// <summary>Se llego al tiempo maximo de transmision.</summary>
    TiempoAgotado,

    /// <summary>Fallo algo hablando con el equipo.</summary>
    Fallo,
}

/// <summary>Foto del emisor, para la pantalla.</summary>
/// <param name="EnAntena">El PTT esta arriba por esta transmision.</param>
/// <param name="Enviando">El trozo que esta manipulando el equipo ahora.</param>
/// <param name="Pendiente">Lo que falta por mandar.</param>
/// <param name="Wpm">Velocidad del trozo en curso.</param>
public sealed record EstadoDelEmisorCw(bool EnAntena, string Enviando, string Pendiente, int Wpm)
{
    /// <summary>Sin transmitir.</summary>
    public static EstadoDelEmisorCw Parado { get; } = new(false, string.Empty, string.Empty, 0);
}

/// <summary>Tiempos del emisor de telegrafia.</summary>
public sealed class OpcionesDelEmisorCw
{
    /// <summary>
    /// Tope de una transmision de telegrafia. Pasado, se para el manipulador y se baja el PTT.
    /// Por detras sigue el tope del vigilante, que no depende de nada.
    /// </summary>
    public TimeSpan TiempoMaximo { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Lo que se espera de mas tras lo calculado para cada trozo (lo que tarda la orden en llegar).</summary>
    public TimeSpan Margen { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Por cuanto se multiplica lo calculado, por si el peso del manipulador alarga las rayas.</summary>
    public double Holgura { get; set; } = 1.06;

    /// <summary>Cada cuanto se late al vigilante mientras el equipo manipula.</summary>
    public TimeSpan Paso { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>Como se espera. Las pruebas lo cambian por un reloj de mentira.</summary>
    public Func<TimeSpan, CancellationToken, Task> Esperar { get; set; } = Task.Delay;

    /// <summary>Tiempo transcurrido desde un instante cualquiera. Las pruebas lo cambian.</summary>
    public Func<TimeSpan> Reloj { get; set; } = () => Stopwatch.GetElapsedTime(0);
}

/// <summary>
/// Manda telegrafia con el manipulador interno del equipo, siempre dentro de una transmision
/// pedida al vigilante del PTT.
/// </summary>
/// <remarks>
/// <para>
/// <b>El audio no es del PC.</b> El texto se trocea en lo que admite una orden del equipo
/// (50 caracteres en el FT-710, 30 en ICOM), cortando por palabras, y cada trozo se le pasa al
/// manipulador de la radio. Entre trozo y trozo se espera lo que tarda en salir —ningun equipo
/// avisa por CAT de que ha terminado—, latiendo al vigilante.
/// </para>
/// <para>
/// <b>Se corta siempre</b>: con <see cref="PararAsync"/> (Esc o «PARAR CW»), al pasar el
/// <see cref="OpcionesDelEmisorCw.TiempoMaximo"/>, si el vigilante suelta la antena (su propio
/// tope, el latido, el panico, el equipo perdido) y si falla algo. En todos los casos se manda
/// la orden de parar al manipulador y se baja el PTT; el control, ademas, vuelve a parar el
/// manipulador antes de bajar el PTT por cualquier via.
/// </para>
/// <para>
/// Mientras sale una transmision se puede seguir añadiendo texto (<see cref="Encolar"/>): es el
/// «escribir mientras se envia».
/// </para>
/// </remarks>
public sealed class EmisorCw
{
    private readonly IControlEquipo _control;
    private readonly IVigilantePtt _vigilante;
    private readonly OpcionesDelEmisorCw _opciones;
    private readonly ILogger _registro;
    private readonly object _candado = new();
    private readonly LinkedList<TrozoCw> _cola = new();

    private CancellationTokenSource? _cts;
    private bool _enviando;
    private EstadoDelEmisorCw _estado = EstadoDelEmisorCw.Parado;

    /// <summary>Monta el emisor.</summary>
    /// <param name="control">El equipo. Tiene que ser <see cref="IManipuladorCw"/> para poder mandar.</param>
    /// <param name="vigilante">El vigilante del PTT: el unico camino para salir al aire.</param>
    /// <param name="opciones">Tiempos; nulo, los de partida.</param>
    /// <param name="registro">Donde anotar.</param>
    public EmisorCw(IControlEquipo control, IVigilantePtt vigilante, OpcionesDelEmisorCw? opciones = null, ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(vigilante);
        _control = control;
        _vigilante = vigilante;
        _opciones = opciones ?? new OpcionesDelEmisorCw();
        _registro = registro ?? NullLogger.Instance;
    }

    /// <summary>Cambia algo de <see cref="Estado"/>. Puede saltar en cualquier hilo.</summary>
    public event EventHandler<EstadoDelEmisorCw>? EstadoCambiado;

    /// <summary>Termina una transmision. Puede saltar en cualquier hilo.</summary>
    public event EventHandler<FinDelEnvioCw>? Terminado;

    /// <summary>Los tiempos, para cambiar el tope desde los ajustes.</summary>
    public OpcionesDelEmisorCw Opciones => _opciones;

    /// <summary>Hay una transmision de telegrafia en marcha.</summary>
    public bool Enviando
    {
        get
        {
            lock (_candado) return _enviando;
        }
    }

    /// <summary>La foto de ahora.</summary>
    public EstadoDelEmisorCw Estado
    {
        get
        {
            lock (_candado) return _estado;
        }
    }

    /// <summary>El ultimo fallo, si la ultima transmision termino en <see cref="FinDelEnvioCw.Fallo"/>.</summary>
    public Exception? UltimoFallo { get; private set; }

    /// <summary>Por que no se puede mandar telegrafia ahora, o nulo si se puede.</summary>
    public string? PorQueNoPuede => _control is IManipuladorCw m
        ? m.PorQueNoManipula ?? _vigilante.MotivoDeBloqueo
        : Textos.T("Servicios.Radio.Cw.SinManipuladorPorCat");

    /// <summary>Velocidades que admite el equipo.</summary>
    public (int Minima, int Maxima) Velocidades => _control is IManipuladorCw m ? (m.WpmMinima, m.WpmMaxima) : (5, 60);

    /// <summary>
    /// Añade texto a la transmision en marcha. Devuelve falso si no hay ninguna (entonces hay
    /// que empezar una con <see cref="TransmitirAsync"/>, que pasa por el pestillo y la pregunta).
    /// </summary>
    /// <param name="trozos">Lo que se añade.</param>
    /// <returns>Verdadero si se añadio.</returns>
    public bool Encolar(IEnumerable<TrozoCw> trozos)
    {
        ArgumentNullException.ThrowIfNull(trozos);
        lock (_candado)
        {
            if (!_enviando) return false;
            foreach (var t in trozos.Where(t => !string.IsNullOrWhiteSpace(t.Texto))) _cola.AddLast(t);
            _estado = _estado with { Pendiente = Pendiente() };
        }

        Avisar();
        return true;
    }

    /// <summary>
    /// Pide la antena al vigilante y manda el texto con el manipulador del equipo.
    /// </summary>
    /// <param name="trozos">El texto, en trozos con su velocidad.</param>
    /// <param name="motivo">Para el registro del vigilante.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <returns>Como termino.</returns>
    /// <exception cref="InvalidOperationException">Si ya hay una en marcha o el equipo no puede.</exception>
    public async Task<FinDelEnvioCw> TransmitirAsync(IEnumerable<TrozoCw> trozos, string motivo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trozos);
        if (_control is not IManipuladorCw manipulador)
        {
            throw new InvalidOperationException(Textos.T("Servicios.Radio.Cw.SinManipuladorPorCat"));
        }

        if (manipulador.PorQueNoManipula is { } porque) throw new InvalidOperationException(porque);

        CancellationTokenSource cts;
        lock (_candado)
        {
            if (_enviando) throw new InvalidOperationException(Textos.T("Servicios.Radio.Cw.YaEnviando"));
            _enviando = true;
            _cola.Clear();
            foreach (var t in trozos.Where(t => !string.IsNullOrWhiteSpace(t.Texto))) _cola.AddLast(t);
            cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _cts = cts;
            UltimoFallo = null;
        }

        var fin = FinDelEnvioCw.Completo;
        ITransmisionEnCurso? antena = null;
        var tope = Tope();
        var inicio = _opciones.Reloj();
        try
        {
            antena = await _vigilante.PedirAntenaAsync(motivo, cts.Token).ConfigureAwait(false);
            Poner(e => e with { EnAntena = true });

            int? velocidadPuesta = null;
            while (Tomar(manipulador.LetrasPorOrden, out var trozo))
            {
                cts.Token.ThrowIfCancellationRequested();
                if (_opciones.Reloj() - inicio >= tope)
                {
                    fin = FinDelEnvioCw.TiempoAgotado;
                    break;
                }

                if (!antena.EnAntena)
                {
                    fin = FinDelEnvioCw.Parado;
                    break;
                }

                if (velocidadPuesta != trozo.Wpm)
                {
                    await manipulador.PonerVelocidadAsync(trozo.Wpm, cts.Token).ConfigureAwait(false);
                    velocidadPuesta = trozo.Wpm;
                }

                Poner(e => e with { Enviando = trozo.Texto, Wpm = trozo.Wpm });
                await manipulador.ManipularAsync(trozo.Texto, cts.Token).ConfigureAwait(false);
                antena.Latir();

                // Lo que tarda en salir mas el hueco de palabra: el siguiente trozo empieza
                // como una palabra nueva y no pisa al anterior.
                var dura = (TiempoMorse.Duracion(trozo.Texto, trozo.Wpm) + (TiempoMorse.Punto(trozo.Wpm) * 7)) * _opciones.Holgura
                    + _opciones.Margen;
                if (await EsperarLatiendoAsync(dura, antena, inicio, tope, cts.Token).ConfigureAwait(false) is { } corte)
                {
                    fin = corte;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            fin = FinDelEnvioCw.Parado;
        }
        catch (Exception ex)
        {
            fin = FinDelEnvioCw.Fallo;
            UltimoFallo = ex;
            _registro.LogError(ex, "Falló la transmisión de telegrafía.");
        }
        finally
        {
            if (fin != FinDelEnvioCw.Completo)
            {
                await PararElManipuladorSinFallarAsync(manipulador).ConfigureAwait(false);
            }

            if (antena is not null)
            {
                try
                {
                    await antena.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _registro.LogError(ex, "No se ha podido bajar el PTT al terminar la telegrafía.");
                }
            }

            try
            {
                await manipulador.TerminarAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "No se ha podido dejar el manipulador como estaba.");
            }

            lock (_candado)
            {
                _enviando = false;
                _cola.Clear();
                _cts = null;
                _estado = EstadoDelEmisorCw.Parado;
            }

            cts.Dispose();
            Avisar();
            _registro.LogInformation("Telegrafía terminada: {Fin}.", fin);
            try
            {
                Terminado?.Invoke(this, fin);
            }
            catch (Exception ex)
            {
                _registro.LogWarning(ex, "Un suscriptor del fin de la telegrafía lanzó una excepción.");
            }
        }

        return fin;
    }

    /// <summary>
    /// Para en el acto: orden de parar al manipulador y bajar el PTT por el vigilante (que
    /// prueba todas sus vias si la normal falla). Se puede pulsar siempre, haya o no telegrafia.
    /// </summary>
    /// <returns>La tarea.</returns>
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
            // Ya habia terminado.
        }

        if (_control is IManipuladorCw manipulador)
        {
            await PararElManipuladorSinFallarAsync(manipulador).ConfigureAwait(false);
        }

        try
        {
            await _vigilante.SoltarYaAsync(MotivoDeSuelta.Panico).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogError(ex, "No se ha podido soltar el PTT al parar la telegrafía.");
            throw;
        }
    }

    private TimeSpan Tope()
    {
        var tope = _opciones.TiempoMaximo;
        if (tope <= TimeSpan.Zero) tope = TimeSpan.FromSeconds(1);
        return tope < _vigilante.TiempoMaximo ? tope : _vigilante.TiempoMaximo;
    }

    private async Task<FinDelEnvioCw?> EsperarLatiendoAsync(
        TimeSpan cuanto, ITransmisionEnCurso antena, TimeSpan inicio, TimeSpan tope, CancellationToken ct)
    {
        var hasta = _opciones.Reloj() + cuanto;
        while (true)
        {
            var falta = hasta - _opciones.Reloj();
            if (falta <= TimeSpan.Zero) return null;
            await _opciones.Esperar(falta < _opciones.Paso ? falta : _opciones.Paso, ct).ConfigureAwait(false);
            antena.Latir();
            if (_opciones.Reloj() - inicio >= tope) return FinDelEnvioCw.TiempoAgotado;
            if (!antena.EnAntena) return FinDelEnvioCw.Parado;
        }
    }

    private async Task PararElManipuladorSinFallarAsync(IManipuladorCw manipulador)
    {
        try
        {
            using var espera = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await manipulador.PararManipuladorAsync(espera.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _registro.LogWarning(ex, "No se ha podido mandar la orden de parar al manipulador.");
        }
    }

    /// <summary>
    /// Saca de la cola el siguiente trozo que cabe en una orden, cortando por palabras.
    /// </summary>
    private bool Tomar(int letras, out TrozoCw trozo)
    {
        lock (_candado)
        {
            while (_cola.First is { } primero)
            {
                var palabras = primero.Value.Texto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (palabras.Length == 0)
                {
                    _cola.RemoveFirst();
                    continue;
                }

                var texto = string.Empty;
                var usadas = 0;
                foreach (var palabra in palabras)
                {
                    var con = texto.Length == 0 ? palabra : texto + " " + palabra;
                    if (con.Length > letras) break;
                    texto = con;
                    usadas++;
                }

                string resto;
                if (usadas == 0)
                {
                    // Una sola palabra mas larga que una orden: se parte a lo bruto.
                    texto = palabras[0][..letras];
                    resto = string.Join(' ', new[] { palabras[0][letras..] }.Concat(palabras.Skip(1)));
                }
                else
                {
                    resto = string.Join(' ', palabras.Skip(usadas));
                }

                if (resto.Length == 0) _cola.RemoveFirst();
                else primero.Value = primero.Value with { Texto = resto };

                trozo = new TrozoCw(texto, primero.Value.Wpm);
                _estado = _estado with { Pendiente = Pendiente() };
                return true;
            }

            trozo = new TrozoCw(string.Empty, 0);
            return false;
        }
    }

    private string Pendiente() => string.Join(' ', _cola.Select(t => t.Texto));

    private void Poner(Func<EstadoDelEmisorCw, EstadoDelEmisorCw> cambio)
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
            _registro.LogWarning(ex, "Un suscriptor del estado de la telegrafía lanzó una excepción.");
        }
    }
}
