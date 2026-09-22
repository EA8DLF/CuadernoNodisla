using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Nodisla.Cuaderno.Audio.Reloj;

/// <summary>
/// Pregunta la hora a un servidor por SNTP.
/// </summary>
/// <remarks>
/// <para>
/// Es el protocolo de la hora de toda la vida, en su version simple: se manda un paquete de
/// cuarenta y ocho bytes al puerto 123 y se anota cuando se mandó y cuando volvio. Con esas dos
/// marcas y las dos que trae el servidor sale el desvio sin que el camino de ida y el de vuelta
/// tengan que durar lo mismo.
/// </para>
/// <para>
/// <b>Aqui no se toca el reloj del ordenador.</b> Solo se mide cuanto se desvia; corregirlo es
/// cosa del sistema y pide permisos de administrador. El modem trabaja con la hora corregida
/// por su cuenta, que es suficiente y no cambia nada fuera del programa.
/// </para>
/// </remarks>
public sealed class ClienteSntp : IFuenteDeHora
{
    /// <summary>Puerto de la hora, el de siempre.</summary>
    public const int PuertoNtp = 123;

    private const int TamanoDelPaquete = 48;

    /// <summary>Comienzo de la cuenta de NTP: 1 de enero de 1900.</summary>
    private static readonly DateTime OrigenNtp = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string _servidor;
    private readonly TimeSpan _espera;
    private readonly Func<DateTimeOffset> _relojDelSistema;
    private readonly ILogger _registro;

    /// <summary>Crea el cliente contra un servidor.</summary>
    /// <param name="servidor">Nombre o direccion del servidor de hora.</param>
    /// <param name="espera">Lo que se espera a que conteste; si es nulo, dos segundos.</param>
    /// <param name="relojDelSistema">De donde se lee la hora local; si es nulo, la del sistema.</param>
    /// <param name="registro">Donde se anota lo que pasa; si es nulo, no se anota nada.</param>
    /// <exception cref="ArgumentException">Si el servidor viene vacio.</exception>
    public ClienteSntp(
        string servidor,
        TimeSpan? espera = null,
        Func<DateTimeOffset>? relojDelSistema = null,
        ILogger? registro = null)
    {
        if (string.IsNullOrWhiteSpace(servidor))
        {
            throw new ArgumentException("Hay que decir a qué servidor se le pregunta la hora.", nameof(servidor));
        }

        _servidor = servidor.Trim();
        _espera = espera is { } valor && valor > TimeSpan.Zero ? valor : TimeSpan.FromSeconds(2);
        _relojDelSistema = relojDelSistema ?? (() => DateTimeOffset.UtcNow);
        _registro = registro ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public string Nombre => _servidor;

    /// <inheritdoc />
    public async Task<MedidaDeHora?> ConsultarAsync(CancellationToken ct = default)
    {
        try
        {
            using var socket = new UdpClient();
            socket.Client.ReceiveTimeout = (int)_espera.TotalMilliseconds;
            socket.Client.SendTimeout = (int)_espera.TotalMilliseconds;

            var peticion = new byte[TamanoDelPaquete];

            // Primer byte: sin aviso de salto, version 3, modo 3 (cliente).
            peticion[0] = 0x1B;

            using var plazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
            plazo.CancelAfter(_espera);

            await socket.SendAsync(peticion, _servidor, PuertoNtp, plazo.Token).ConfigureAwait(false);

            var salida = _relojDelSistema();
            var respuesta = await socket.ReceiveAsync(plazo.Token).ConfigureAwait(false);
            var vuelta = _relojDelSistema();

            return Interpretar(respuesta.Buffer, salida, vuelta);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _registro.LogDebug("El servidor de hora {Servidor} no contestó a tiempo.", _servidor);
            return null;
        }
        catch (SocketException fallo)
        {
            _registro.LogDebug(fallo, "No se pudo preguntar la hora a {Servidor}.", _servidor);
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Saca el desvio de un paquete de respuesta.
    /// </summary>
    /// <param name="respuesta">Los bytes que llegaron.</param>
    /// <param name="salida">Hora local cuando se mando la peticion.</param>
    /// <param name="vuelta">Hora local cuando llego la respuesta.</param>
    /// <returns>La medida, o nulo si el paquete no vale.</returns>
    /// <remarks>
    /// Se comprueba que el paquete sea de verdad una respuesta de servidor y que no venga
    /// avisando de que el propio servidor no esta sincronizado: una hora mala creida a pies
    /// juntillas es peor que no tener hora, porque el operador no se entera.
    /// </remarks>
    public MedidaDeHora? Interpretar(ReadOnlySpan<byte> respuesta, DateTimeOffset salida, DateTimeOffset vuelta)
    {
        if (respuesta.Length < TamanoDelPaquete)
        {
            return null;
        }

        var aviso = (respuesta[0] >> 6) & 0x03;
        var modo = respuesta[0] & 0x07;
        var estrato = respuesta[1];

        // Modo 4 es «servidor»; aviso 3 es «no estoy sincronizado»; estrato 0 o mayor de 15 es
        // un servidor que no sirve.
        if (modo != 4 || aviso == 3 || estrato == 0 || estrato > 15)
        {
            _registro.LogDebug(
                "El servidor de hora {Servidor} contestó algo que no vale (modo {Modo}, estrato {Estrato}).",
                _servidor,
                modo,
                estrato);
            return null;
        }

        var recepcionEnServidor = LeerMarca(respuesta[32..40]);
        var salidaDelServidor = LeerMarca(respuesta[40..48]);

        if (recepcionEnServidor is null || salidaDelServidor is null)
        {
            return null;
        }

        // Formula de siempre: el desvio del servidor respecto al local es la media de lo que se
        // adelanta en la ida y lo que se atrasa en la vuelta, con lo que el tiempo de viaje se
        // cancela aunque la ida y la vuelta no duren lo mismo.
        var t1 = salida.UtcDateTime;
        var t2 = recepcionEnServidor.Value;
        var t3 = salidaDelServidor.Value;
        var t4 = vuelta.UtcDateTime;

        var desvioDelServidor = ((t2 - t1).TotalMilliseconds + (t3 - t4).TotalMilliseconds) / 2.0;
        var idaYVuelta = (t4 - t1).TotalMilliseconds - (t3 - t2).TotalMilliseconds;

        // El puerto pide el desvio del reloj del sistema: si el servidor va por delante, el
        // sistema va por detras, de ahi el cambio de signo.
        return new MedidaDeHora(-desvioDelServidor, Math.Max(idaYVuelta, 0.0), _servidor);
    }

    /// <summary>Lee una marca de tiempo de NTP: segundos y fraccion, de 32 bits cada uno.</summary>
    private static DateTime? LeerMarca(ReadOnlySpan<byte> crudo)
    {
        var segundos = BinaryPrimitives.ReadUInt32BigEndian(crudo);
        var fraccion = BinaryPrimitives.ReadUInt32BigEndian(crudo[4..]);

        if (segundos == 0 && fraccion == 0)
        {
            return null;
        }

        var milisegundos = (segundos * 1000.0) + (fraccion * 1000.0 / 4294967296.0);
        return OrigenNtp.AddMilliseconds(milisegundos);
    }
}
