using System.Diagnostics;
using System.IO.Ports;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Radio.Ptt;

namespace Nodisla.Cuaderno.Radio.Control.Ft710;

/// <summary>
/// Canal CAT por puerto serie.
/// </summary>
/// <remarks>
/// El FT-710 se presenta con un CP2105 doble: el <i>Enhanced</i> es el que se usa y el
/// <i>Standard</i> tambien habla. <b>La velocidad la manda el menu del equipo</b> (CAT RATE),
/// no el cable: el del operador esta a 38400. Por eso la velocidad no se negocia, se prueba.
/// <c>DTR</c> y <c>RTS</c> van en cierto salvo que el PTT vaya por una de esas lineas.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class CanalSerieCat : ICanalCat
{
    private readonly string _puerto;
    private readonly int _baudios;
    private readonly TimeSpan _espera;
    private readonly ViaDePtt _viaDePtt;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _puerta = new(1, 1);

    private SerialPort? _serie;

    /// <summary>Crea el canal sobre un puerto serie.</summary>
    /// <param name="puerto">Nombre del puerto, por ejemplo <c>COM3</c>.</param>
    /// <param name="baudios">Velocidad; en el FT-710, 115200 o 4800.</param>
    /// <param name="espera">Lo que se espera por cada respuesta.</param>
    /// <param name="registro">Donde anotar el ir y venir.</param>
    /// <param name="viaDePtt">
    /// Por donde se sube el PTT. Con <see cref="ViaDePtt.Cat"/> —lo de partida— las dos lineas
    /// van en cierto, como pide el cable del equipo. Con <c>RTS</c> o <c>DTR</c>, la linea que
    /// hace de PTT <b>abre en falso</b>: abrir el puerto no puede poner la radio en antena.
    /// </param>
    public CanalSerieCat(
        string puerto,
        int baudios = 115200,
        TimeSpan? espera = null,
        ILogger? registro = null,
        ViaDePtt viaDePtt = ViaDePtt.Cat)
    {
        _puerto = puerto;
        _baudios = baudios;
        _espera = espera ?? TimeSpan.FromMilliseconds(350);
        _viaDePtt = viaDePtt;
        _registro = registro ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public bool Abierto => _serie is { IsOpen: true };

    /// <inheritdoc />
    public string Descripcion => $"{_puerto} a {_baudios}";

    /// <inheritdoc />
    public async Task AbrirAsync(CancellationToken ct = default)
    {
        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            CerrarSinCandado();

            var serie = new SerialPort(_puerto, _baudios, Parity.None, 8, StopBits.One)
            {
                // Si el PTT va por una linea, esa linea abre en falso. Un puerto que se abre
                // con el PTT levantado pone la radio en antena al arrancar el programa.
                DtrEnable = _viaDePtt != ViaDePtt.Dtr,
                RtsEnable = _viaDePtt != ViaDePtt.Rts,
                Handshake = Handshake.None,
                ReadTimeout = (int)_espera.TotalMilliseconds,
                WriteTimeout = (int)_espera.TotalMilliseconds,
                Encoding = Encoding.ASCII,
            };

            try
            {
                serie.Open();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidOperationException or FileNotFoundException)
            {
                serie.Dispose();

                // El puerto no existe, esta cogido por otro programa o el cable no esta. No es
                // lo mismo que un equipo apagado, y la interfaz tiene que poder distinguirlo.
                throw new CanalNoDisponibleException(Descripcion, ex);
            }

            serie.DiscardInBuffer();
            serie.DiscardOutBuffer();
            _serie = serie;
            _registro.LogInformation("Abierto el canal CAT por {Canal}.", Descripcion);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc />
    public async Task<string?> PreguntarAsync(string orden, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orden);
        OrdenesFt710.ComprobarQueEsSegura(orden);

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var serie = Serie();
            serie.DiscardInBuffer();
            var bytes = Encoding.ASCII.GetBytes(orden);
            await serie.BaseStream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
            await serie.BaseStream.FlushAsync(ct).ConfigureAwait(false);

            return await LeerRespuestaAsync(serie, ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc />
    public async Task MandarAsync(string orden, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orden);
        OrdenesFt710.ComprobarQueEsSegura(orden);

        await _puerta.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var serie = Serie();
            var bytes = Encoding.ASCII.GetBytes(orden);
            await serie.BaseStream.WriteAsync(bytes.AsMemory(), ct).ConfigureAwait(false);
            await serie.BaseStream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc />
    public void MandarSincrono(string orden)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orden);

        // Aqui no se pide el semaforo a proposito: esto se llama para bajar el PTT cuando todo
        // lo demas ha fallado o el proceso se esta muriendo, y esperar a un candado tomado por
        // una orden colgada seria justo lo contrario de lo que hace falta.
        var serie = _serie ?? throw new InvalidOperationException($"El canal {Descripcion} no está abierto.");
        var bytes = Encoding.ASCII.GetBytes(orden);
        serie.Write(bytes, 0, bytes.Length);
    }

    /// <inheritdoc />
    public bool PuedeAccionarLineas => _viaDePtt != ViaDePtt.Cat;

    /// <inheritdoc />
    public Task PonerLineaDePttAsync(bool transmitir, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        PonerLineaDePttSincrono(transmitir);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void PonerLineaDePttSincrono(bool transmitir)
    {
        if (_viaDePtt == ViaDePtt.Cat)
        {
            throw new NotSupportedException(
                $"El canal {Descripcion} tiene el PTT por CAT: no hay línea que accionar.");
        }

        // Como en MandarSincrono, aqui no se pide el semaforo: esto es lo que baja el PTT
        // cuando lo demas ha fallado, y esperar a un candado tomado por una orden colgada es lo
        // ultimo que hace falta.
        var serie = _serie ?? throw new InvalidOperationException($"El canal {Descripcion} no está abierto.");

        if (_viaDePtt == ViaDePtt.Rts)
        {
            serie.RtsEnable = transmitir;
        }
        else
        {
            serie.DtrEnable = transmitir;
        }
    }

    /// <inheritdoc />
    public void Cerrar()
    {
        if (!_puerta.Wait(TimeSpan.FromMilliseconds(250)))
        {
            CerrarSinCandado();
            return;
        }

        try
        {
            CerrarSinCandado();
        }
        finally
        {
            _puerta.Release();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Cerrar();
        _puerta.Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Lee la respuesta del equipo, con un tope de tiempo que <b>se cumple de verdad</b>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Aqui hubo un cuelgue de los buenos y conviene que quede escrito.</b> Esto usaba
    /// <c>BaseStream.ReadAsync</c> con un testigo de cancelacion, y una lectura asincrona sobre
    /// un puerto serie de Windows <b>no se puede abortar una vez lanzada</b>: ni el testigo ni
    /// <c>ReadTimeout</c> la cortan. Medido en la maquina del operador con el CP2105 del FT-710: si
    /// el equipo no contesta, esa lectura se queda pendiente <b>para siempre</b>, el puerto
    /// queda cogido, la busqueda de equipos no termina nunca y la pantalla que la lanzo se
    /// queda esperando en silencio. Eso era lo que hacia que el boton Aplicar de los ajustes no
    /// hiciera nada visible.
    /// </para>
    /// <para>
    /// Asi que <b>no se lanza ninguna lectura que no se pueda terminar</b>: se mira si hay
    /// bytes esperando y solo entonces se lee, que es una lectura que devuelve en el acto.
    /// Mientras no los haya, se duerme un instante. El tope se cumple al milisegundo, el
    /// testigo de cancelacion funciona, y el puerto queda <b>sano</b> para la siguiente prueba
    /// —que es lo que permite barrer las cinco velocidades del equipo sin dejarlo inservible—.
    /// </para>
    /// </remarks>
    private async Task<string?> LeerRespuestaAsync(SerialPort serie, CancellationToken ct)
    {
        // Lo que se duerme entre vistazos al buzon del puerto.
        var siesta = TimeSpan.FromMilliseconds(5);

        var recibido = new StringBuilder();
        var buzon = new byte[256];
        var reloj = Stopwatch.StartNew();

        try
        {
            while (reloj.Elapsed < _espera)
            {
                var esperando = serie.BytesToRead;
                if (esperando <= 0)
                {
                    await Task.Delay(siesta, ct).ConfigureAwait(false);
                    continue;
                }

                // Hay bytes: esta lectura devuelve en el acto y no deja nada pendiente.
                var leidos = serie.Read(buzon, 0, Math.Min(buzon.Length, esperando));
                if (leidos <= 0)
                {
                    continue;
                }

                recibido.Append(Encoding.ASCII.GetString(buzon, 0, leidos));
                var texto = recibido.ToString();
                var fin = texto.IndexOf(Cat.Fin);
                if (fin >= 0)
                {
                    return texto[..fin];
                }

                if (recibido.Length > 4096)
                {
                    // Alguien esta escupiendo texto que no es CAT: en esta maquina, por ejemplo,
                    // COM8 es un Meshtastic que suelta su registro de depuracion.
                    return null;
                }
            }

            // Se acabo el tiempo sin respuesta completa. No es un fallo: hay ordenes que no
            // contestan, y un puerto que no es la radio tampoco.
            _registro.LogDebug("{Canal} no ha contestado en {Espera}.", Descripcion, _espera);
            return null;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private SerialPort Serie() => _serie is { IsOpen: true }
        ? _serie
        : throw new InvalidOperationException($"El canal {Descripcion} no está abierto.");

    private void CerrarSinCandado()
    {
        try
        {
            if (_serie is { IsOpen: true })
            {
                // Se baja la linea de PTT ANTES de cerrar. Cerrar el puerto suele dejar las
                // lineas caidas, pero «suele» no vale para lo unico que puede quemar la etapa
                // final: se baja a mano y luego se cierra.
                if (_viaDePtt == ViaDePtt.Rts) _serie.RtsEnable = false;
                if (_viaDePtt == ViaDePtt.Dtr) _serie.DtrEnable = false;

                _serie.Close();
            }

            _serie?.Dispose();
        }
        catch (Exception ex)
        {
            _registro.LogDebug(ex, "Fallo al cerrar {Canal}.", Descripcion);
        }
        finally
        {
            _serie = null;
        }
    }
}
