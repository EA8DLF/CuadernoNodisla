using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Audio.Procesado;
using Nodisla.Cuaderno.Idiomas;

namespace Nodisla.Cuaderno.Audio.Fonia;

/// <summary>Como acabo una pasada de fonia.</summary>
/// <param name="Motivo">Por que se solto el PTT.</param>
/// <param name="Duracion">Cuanto estuvo en antena.</param>
public sealed record FinDeFonia(MotivoDeSuelta Motivo, TimeSpan Duracion);

/// <summary>
/// La fonia por el ordenador: escuchar la radio por los altavoces del PC y hablar por el
/// microfono del PC.
/// </summary>
/// <remarks>
/// <para>
/// <b>Recepcion.</b> Un <see cref="IPuenteDeAudio"/> lleva la entrada del codec del equipo a
/// los altavoces. WASAPI en modo compartido deja que varios lean la misma entrada a la vez, asi
/// que convive con el modem propio sin quitarle nada.
/// </para>
/// <para>
/// <b>Transmision.</b> El otro puente lleva el microfono del PC a la salida hacia el equipo. El
/// orden importa: primero se abre el camino <b>en silencio</b> —con el VOX de la radio puesto,
/// un ruido antes de tiempo la sacaria al aire—, luego se pide la antena <b>al vigilante</b>,
/// se calla la escucha para que el microfono no oiga los altavoces, y solo entonces se deja
/// pasar la voz. Al terminar, al reves: silencio, PTT abajo, camino cerrado y escucha de
/// vuelta.
/// </para>
/// <para>
/// <b>Seguridad.</b> Nunca se toca el PTT por fuera de <see cref="IVigilantePtt"/>. Encima de
/// sus redes se ponen dos propias: un tiempo maximo de fonia (manda el menor de los dos) y
/// latir solo mientras el audio avanza de verdad. Si el vigilante suelta por su cuenta —boton
/// de panico, tiempo agotado, equipo perdido, cierre—, aqui se entera y se recoge todo.
/// </para>
/// </remarks>
public sealed class ControlDeFonia : IAsyncDisposable
{
    private readonly IVigilantePtt _vigilante;
    private readonly IPuenteDeAudio _recepcion;
    private readonly IPuenteDeAudio _transmision;
    private readonly OpcionesDeFonia _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _turno = new(1, 1);
    private readonly ITimer _vigilancia;

    private ITransmisionEnCurso? _enCurso;
    private ReproductorEnElAire? _mensaje;
    private DateTimeOffset? _inicio;
    private bool _escuchaSilenciadaPorElOperador;
    private int _terminandoPorVigilancia;
    private bool _desechado;

    /// <summary>Monta la fonia. No abre ningun dispositivo ni toca la radio.</summary>
    /// <param name="vigilante">Unico camino para poner el equipo en antena.</param>
    /// <param name="recepcion">Camino del codec del equipo a los altavoces.</param>
    /// <param name="transmision">Camino del microfono a la entrada del equipo.</param>
    /// <param name="opciones">Tiempos; si es nulo, los de partida.</param>
    /// <param name="reloj">Reloj; si es nulo, el del sistema.</param>
    /// <param name="registro">Donde se anota; si es nulo, en ningun sitio.</param>
    public ControlDeFonia(
        IVigilantePtt vigilante,
        IPuenteDeAudio recepcion,
        IPuenteDeAudio transmision,
        OpcionesDeFonia? opciones = null,
        TimeProvider? reloj = null,
        ILogger? registro = null)
    {
        ArgumentNullException.ThrowIfNull(vigilante);
        ArgumentNullException.ThrowIfNull(recepcion);
        ArgumentNullException.ThrowIfNull(transmision);

        _vigilante = vigilante;
        _recepcion = recepcion;
        _transmision = transmision;
        _opciones = opciones ?? new OpcionesDeFonia();
        _reloj = reloj ?? TimeProvider.System;
        _registro = registro ?? NullLogger.Instance;

        // El procesado va montado siempre; cada paso se enciende y apaga en vivo. El grabador
        // ve la recepcion tal como llega; la cadena de escucha, despues del volumen.
        _recepcion.AntesDeLaGanancia = Grabador;
        _recepcion.TrasLaGanancia = Escucha;
        _transmision.TrasLaGanancia = Microfono;

        _vigilante.PttSoltado += AlSoltarseElPtt;
        _transmision.Fallo += AlFallarLaTransmision;
        _recepcion.Fallo += AlFallarLaRecepcion;

        _vigilancia = _reloj.CreateTimer(
            _ => Vigilar(),
            null,
            _opciones.PasoDeVigilancia,
            _opciones.PasoDeVigilancia);
    }

    /// <summary>Opciones en uso; el tiempo maximo se puede cambiar en caliente.</summary>
    public OpcionesDeFonia Opciones => _opciones;

    /// <summary>La escucha por los altavoces esta abierta.</summary>
    public bool Escuchando => _recepcion.EstaAbierto;

    /// <summary>Hay una pasada de fonia en el aire.</summary>
    public bool Transmitiendo => Volatile.Read(ref _enCurso) is not null;

    /// <summary>Cuando empezo la pasada en curso, o nulo.</summary>
    public DateTimeOffset? InicioDeTransmision => _inicio;

    /// <summary>Lo que lleva en el aire la pasada en curso.</summary>
    public TimeSpan TiempoEnElAire => _inicio is { } inicio ? _reloj.GetUtcNow() - inicio : TimeSpan.Zero;

    /// <summary>El tope que manda de verdad: el menor entre el de fonia y el del vigilante.</summary>
    public TimeSpan TiempoMaximoEfectivo =>
        _vigilante.TiempoMaximo > TimeSpan.Zero && _vigilante.TiempoMaximo < _opciones.TiempoMaximo
            ? _vigilante.TiempoMaximo
            : _opciones.TiempoMaximo;

    /// <summary>Camino de recepcion, para medidores y volumen.</summary>
    public IPuenteDeAudio Recepcion => _recepcion;

    /// <summary>Camino de transmision, para medidores y ganancia.</summary>
    public IPuenteDeAudio Transmision => _transmision;

    /// <summary>Reductor, notch y limitador de la escucha por los altavoces.</summary>
    public CadenaDeEscucha Escucha { get; } = new();

    /// <summary>Puerta, ecualizador, compresor y techo del microfono del PC.</summary>
    public CadenaDeMicrofono Microfono { get; } = new();

    /// <summary>Los ultimos minutos de la recepcion, en memoria.</summary>
    public GrabadorCircular Grabador { get; } = new();

    /// <summary>La pasada en curso es un mensaje del voice keyer.</summary>
    public bool EnviandoMensaje => Volatile.Read(ref _mensaje) is not null && Transmitiendo;

    /// <summary>Lo que va sonado del mensaje en curso, de 0 a 1.</summary>
    public double AvanceDelMensaje => Volatile.Read(ref _mensaje)?.Avance ?? 0;

    /// <summary>Salta al acabar cada pasada, sea como sea.</summary>
    public event EventHandler<FinDeFonia>? TransmisionTerminada;

    /// <summary>Salta si la escucha se cae sola.</summary>
    public event EventHandler<Exception>? EscuchaPerdida;

    /// <summary>Abre la escucha: codec del equipo a los altavoces del PC.</summary>
    public async Task EmpezarEscuchaAsync(string idEntradaDelEquipo, string idAltavoces, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        await _turno.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _recepcion.CerrarAsync().ConfigureAwait(false);
            _recepcion.Silenciado = _escuchaSilenciadaPorElOperador || Transmitiendo;
            await _recepcion.AbrirAsync(idEntradaDelEquipo, idAltavoces, ct).ConfigureAwait(false);
            _registro.LogInformation("Escucha de fonía por el PC abierta.");
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <summary>Cierra la escucha.</summary>
    public async Task PararEscuchaAsync()
    {
        await _turno.WaitAsync().ConfigureAwait(false);
        try
        {
            await _recepcion.CerrarAsync().ConfigureAwait(false);
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <summary>
    /// Silencia o no la escucha por decision del operador. Mientras se transmite sigue callada
    /// pase lo que pase; esto decide como queda al volver a recepcion.
    /// </summary>
    public void SilenciarEscucha(bool silenciar)
    {
        _escuchaSilenciadaPorElOperador = silenciar;
        if (!Transmitiendo) _recepcion.Silenciado = silenciar;
    }

    /// <summary>
    /// Sale al aire con el microfono del PC.
    /// </summary>
    /// <param name="idMicrofono">Microfono del PC.</param>
    /// <param name="idSalidaAlEquipo">Salida de audio hacia el codec del equipo.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <exception cref="InvalidOperationException">Si ya hay otra transmision en curso, por ejemplo del modem.</exception>
    public Task EmpezarTransmisionAsync(string idMicrofono, string idSalidaAlEquipo, CancellationToken ct = default) =>
        EmpezarAsync(idMicrofono, idSalidaAlEquipo, null, ct);

    /// <summary>
    /// Emite un mensaje grabado (voice keyer) por el mismo camino que la voz: vigilante, tope de
    /// tiempo, latido y procesado del microfono. Al acabar el mensaje se vuelve sola a recepcion.
    /// </summary>
    /// <param name="mensaje">La grabacion.</param>
    /// <param name="idMicrofono">Microfono del PC: su reloj marca el paso del mensaje.</param>
    /// <param name="idSalidaAlEquipo">Salida de audio hacia el codec del equipo.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    /// <exception cref="InvalidOperationException">
    /// Si ya se transmite, si el mensaje esta vacio o si dura mas que el tope de la pasada.
    /// </exception>
    public async Task EmitirMensajeAsync(AudioEnMemoria mensaje, string idMicrofono, string idSalidaAlEquipo, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        ArgumentNullException.ThrowIfNull(mensaje);

        if (mensaje.Muestras.Length == 0) throw new InvalidOperationException(Textos.T("Servicios.Audio.MensajeVacio"));
        if (Transmitiendo || _vigilante.EnAntena) throw new InvalidOperationException(Textos.T("Servicios.Audio.OtraTransmision"));

        var reproductor = new ReproductorEnElAire(mensaje);
        if (reproductor.Duracion >= TiempoMaximoEfectivo)
        {
            throw new InvalidOperationException(Textos.F(
                "Servicios.Audio.MensajeLargo",
                (int)Math.Ceiling(reproductor.Duracion.TotalSeconds),
                (int)TiempoMaximoEfectivo.TotalSeconds));
        }

        await EmpezarAsync(idMicrofono, idSalidaAlEquipo, reproductor, ct).ConfigureAwait(false);
    }

    private async Task EmpezarAsync(string idMicrofono, string idSalidaAlEquipo, ReproductorEnElAire? mensaje, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_desechado, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(idMicrofono);
        ArgumentException.ThrowIfNullOrWhiteSpace(idSalidaAlEquipo);

        await _turno.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Transmitiendo) return;

            if (_vigilante.EnAntena)
            {
                throw new InvalidOperationException(
                    Textos.T("Servicios.Audio.OtraTransmision"));
            }

            ITransmisionEnCurso? pedida = null;
            try
            {
                // 0. Con mensaje, el reproductor va delante de la ganancia, aun desarmado: saca
                //    silencio en vez de la voz. Si el camino no lo acepta, no se sale al aire.
                _transmision.AntesDeLaGanancia = mensaje;
                if (!ReferenceEquals(_transmision.AntesDeLaGanancia, mensaje))
                {
                    throw new InvalidOperationException(Textos.T("Servicios.Audio.SinVoiceKeyer"));
                }

                if (mensaje is not null)
                {
                    Volatile.Write(ref _mensaje, mensaje);
                    mensaje.Terminado += AlTerminarElMensaje;
                }

                // 1. El camino, abierto y callado.
                _transmision.Silenciado = true;
                await _transmision.AbrirAsync(idMicrofono, idSalidaAlEquipo, ct).ConfigureAwait(false);

                // 2. La antena, siempre por el vigilante.
                pedida = await _vigilante.PedirAntenaAsync(
                    mensaje is null ? "Fonía por el micrófono del PC" : "Fonía: mensaje de voz grabado",
                    ct).ConfigureAwait(false);
                _inicio = _reloj.GetUtcNow();
                Interlocked.Exchange(ref _terminandoPorVigilancia, 0);
                Volatile.Write(ref _enCurso, pedida);

                // 3. Nada de realimentacion: los altavoces callan mientras se habla.
                _recepcion.Silenciado = true;

                // 4. Ahora si, la voz.
                _transmision.Silenciado = false;
                if (mensaje is not null) mensaje.Armado = true;
                _registro.LogInformation(
                    "Fonía en el aire{Mensaje}. Tope {Tope}.",
                    mensaje is null ? string.Empty : " con un mensaje grabado",
                    TiempoMaximoEfectivo);
            }
            catch
            {
                _transmision.Silenciado = true;
                QuitarElMensaje();
                Volatile.Write(ref _enCurso, null);
                _inicio = null;

                if (pedida is not null)
                {
                    await SoltarSinFallarAsync(pedida).ConfigureAwait(false);
                }

                await CerrarSinFallarAsync(_transmision).ConfigureAwait(false);
                _recepcion.Silenciado = _escuchaSilenciadaPorElOperador;
                throw;
            }
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <summary>Vuelve a recepcion. No falla si no se estaba transmitiendo.</summary>
    /// <param name="motivo">Por que se termina, para el aviso y el registro.</param>
    public async Task TerminarTransmisionAsync(MotivoDeSuelta motivo = MotivoDeSuelta.Normal)
    {
        // Lo primero, sin esperar turno: que deje de sonar la voz hacia el equipo.
        if (Transmitiendo) _transmision.Silenciado = true;

        await _turno.WaitAsync().ConfigureAwait(false);
        try
        {
            var enCurso = Volatile.Read(ref _enCurso);
            if (enCurso is null) return;

            var duracion = TiempoEnElAire;
            _transmision.Silenciado = true;
            QuitarElMensaje();
            Volatile.Write(ref _enCurso, null);
            _inicio = null;

            await SoltarSinFallarAsync(enCurso).ConfigureAwait(false);
            await CerrarSinFallarAsync(_transmision).ConfigureAwait(false);
            _recepcion.Silenciado = _escuchaSilenciadaPorElOperador;

            _registro.LogInformation("Fonía fuera del aire ({Motivo}) tras {Segundos:F1} s.", motivo, duracion.TotalSeconds);
            TransmisionTerminada?.Invoke(this, new FinDeFonia(motivo, duracion));
        }
        finally
        {
            _turno.Release();
        }
    }

    /// <summary>
    /// Mira la pasada en curso: late si el audio avanza y corta si se pasa de tiempo o si el
    /// audio se ha quedado parado.
    /// </summary>
    /// <remarks>Lo llama un temporizador propio; es publico para poder probarlo sin esperar.</remarks>
    public void Vigilar()
    {
        var enCurso = Volatile.Read(ref _enCurso);
        if (enCurso is null || _inicio is not { } inicio) return;

        var ahora = _reloj.GetUtcNow();

        if (ahora - inicio >= TiempoMaximoEfectivo)
        {
            TerminarDesdeLaVigilancia(MotivoDeSuelta.TiempoAgotado, "se ha agotado el tiempo máximo de fonía");
            return;
        }

        var avance = _transmision.UltimoAvanceUtc;
        var referencia = avance is { } a && a > inicio ? a : inicio;
        var parado = ahora - referencia;

        if (avance is not null && parado <= _opciones.ToleranciaSinAvance)
        {
            enCurso.Latir();
            return;
        }

        // Sin avance no se late: que no sea el programa quien se invente senales de vida. Y si
        // dura el doble de lo tolerado, se corta aqui mismo sin esperar al vigilante.
        if (parado > _opciones.ToleranciaSinAvance + _opciones.ToleranciaSinAvance)
        {
            TerminarDesdeLaVigilancia(MotivoDeSuelta.SinLatido, "el audio del micrófono o del equipo se ha parado");
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_desechado) return;
        _desechado = true;

        _vigilancia.Dispose();
        await TerminarTransmisionAsync(MotivoDeSuelta.Cierre).ConfigureAwait(false);
        await CerrarSinFallarAsync(_recepcion).ConfigureAwait(false);

        _vigilante.PttSoltado -= AlSoltarseElPtt;
        _transmision.Fallo -= AlFallarLaTransmision;
        _recepcion.Fallo -= AlFallarLaRecepcion;
    }

    private void AlTerminarElMensaje(object? origen, EventArgs e)
    {
        // Desde el hilo de audio: el mensaje y su cola han sonado. Se vuelve a recepcion.
        if (!ReferenceEquals(origen, Volatile.Read(ref _mensaje)) || !Transmitiendo) return;
        _transmision.Silenciado = true;
        _ = TerminarSinFallarAsync(MotivoDeSuelta.Normal);
    }

    private void QuitarElMensaje()
    {
        var mensaje = Interlocked.Exchange(ref _mensaje, null);
        if (mensaje is not null)
        {
            mensaje.Armado = false;
            mensaje.Terminado -= AlTerminarElMensaje;
        }

        // Nunca se deja nada delante de la voz al acabar: la siguiente pasada es el microfono.
        _transmision.AntesDeLaGanancia = null;
    }

    private void TerminarDesdeLaVigilancia(MotivoDeSuelta motivo, string porque)
    {
        if (Interlocked.Exchange(ref _terminandoPorVigilancia, 1) != 0) return;

        _registro.LogWarning("Se corta la fonía: {Porque}.", porque);
        _transmision.Silenciado = true;
        _ = TerminarSinFallarAsync(motivo);
    }

    private void AlSoltarseElPtt(object? origen, MotivoDeSuelta motivo)
    {
        // Si el vigilante lo ha soltado por su cuenta —panico, tiempo, equipo perdido—, aqui
        // hay que recoger: callar el microfono, cerrar el camino y devolver la escucha.
        if (!Transmitiendo) return;

        _transmision.Silenciado = true;
        _ = TerminarSinFallarAsync(motivo);
    }

    private void AlFallarLaTransmision(object? origen, Exception fallo)
    {
        _registro.LogError(fallo, "El camino de audio de transmisión se ha roto.");
        if (Transmitiendo) TerminarDesdeLaVigilancia(MotivoDeSuelta.Excepcion, "se ha roto el camino de audio");
    }

    private void AlFallarLaRecepcion(object? origen, Exception fallo)
    {
        _registro.LogWarning(fallo, "La escucha por el PC se ha caído.");
        EscuchaPerdida?.Invoke(this, fallo);
    }

    private async Task TerminarSinFallarAsync(MotivoDeSuelta motivo)
    {
        try
        {
            await TerminarTransmisionAsync(motivo).ConfigureAwait(false);
        }
        catch (Exception fallo)
        {
            _registro.LogError(fallo, "Fallo al terminar la fonía; se suelta el PTT por pánico.");
            await _vigilante.SoltarYaAsync(MotivoDeSuelta.Excepcion).ConfigureAwait(false);
        }
    }

    private async Task SoltarSinFallarAsync(ITransmisionEnCurso enCurso)
    {
        try
        {
            await enCurso.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception fallo)
        {
            _registro.LogError(fallo, "No se pudo soltar la transmisión de fonía; se suelta por pánico.");
            try
            {
                await _vigilante.SoltarYaAsync(MotivoDeSuelta.Excepcion).ConfigureAwait(false);
            }
            catch (Exception otro)
            {
                _registro.LogCritical(otro, "Tampoco se pudo soltar por pánico.");
            }
        }
    }

    private async Task CerrarSinFallarAsync(IPuenteDeAudio puente)
    {
        try
        {
            await puente.CerrarAsync().ConfigureAwait(false);
        }
        catch (Exception fallo)
        {
            _registro.LogWarning(fallo, "Fallo al cerrar un camino de audio de fonía.");
        }
    }
}
