using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Idiomas;
using Nodisla.Cuaderno.Servidores.Rigctld;
using Nodisla.Cuaderno.Servidores.Tci;

namespace Nodisla.Cuaderno.Servidores;

/// <summary>Como esta un servidor.</summary>
/// <param name="Encendido">Escuchando.</param>
/// <param name="Punto">Donde escucha.</param>
/// <param name="Error">Por que no ha podido arrancar, si no ha podido.</param>
public sealed record EstadoDeServidor(bool Encendido, IPEndPoint? Punto, string? Error)
{
    /// <summary>Apagado y sin error.</summary>
    public static EstadoDeServidor Apagado { get; } = new(false, null, null);
}

/// <summary>
/// Los dos servidores para otros programas, rigctld y TCI, sobre la misma radio compartida.
/// </summary>
/// <remarks>
/// Ninguno arranca solo: hace falta encenderlo en Configuracion. Al aplicar ajustes nuevos se
/// arranca, se para o se reinicia lo que haga falta; cambiar la lista de IP no reinicia nada.
/// </remarks>
public sealed class ServidoresParaOtrosProgramas : IAsyncDisposable
{
    private readonly RadioCompartida _radio;
    private readonly IFuenteSpots? _spots;
    private readonly ILoggerFactory _registros;
    private readonly ILogger _registro;
    private readonly SemaphoreSlim _turno = new(1, 1);
    private ServidorRigctld? _rigctld;
    private ServidorTci? _tci;
    private OpcionesDeServidores? _aplicadas;

    /// <summary>Crea el conjunto, con los servidores apagados.</summary>
    /// <param name="radio">La radio compartida.</param>
    /// <param name="spots">La fuente de spots del Cuaderno (la fusionada del cluster), o nula.</param>
    /// <param name="registros">Fabrica de registros.</param>
    public ServidoresParaOtrosProgramas(RadioCompartida radio, IFuenteSpots? spots = null, ILoggerFactory? registros = null)
    {
        _radio = radio ?? throw new ArgumentNullException(nameof(radio));
        _spots = spots;
        _registros = registros ?? NullLoggerFactory.Instance;
        _registro = _registros.CreateLogger("Nodisla.Cuaderno.Servidores");
        if (_spots is not null) _spots.SpotRecibido += AlRecibirSpot;
    }

    /// <summary>La radio compartida (clientes, registro de TX).</summary>
    public RadioCompartida Radio => _radio;

    /// <summary>Como esta el servidor rigctld.</summary>
    public EstadoDeServidor Rigctld { get; private set; } = EstadoDeServidor.Apagado;

    /// <summary>Como esta el servidor TCI.</summary>
    public EstadoDeServidor Tci { get; private set; } = EstadoDeServidor.Apagado;

    /// <summary>Salta cuando arranca o para un servidor.</summary>
    public event EventHandler? EstadoCambiado;

    /// <summary>Salta con un spot que manda un cliente TCI, para pintarlo en el bandmap.</summary>
    public event EventHandler<Spot>? SpotDeCliente;

    /// <summary>Aplica unos ajustes: arranca, para o reinicia lo que haga falta.</summary>
    /// <param name="opciones">Los ajustes.</param>
    /// <param name="ct">Testigo de cancelacion.</param>
    public async Task AplicarAsync(OpcionesDeServidores opciones, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        var nuevas = opciones.Copiar().Acotar();
        await _turno.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _radio.Aplicar(nuevas);
            var antes = _aplicadas;
            var acceso = new ControlDeAcceso(nuevas);
            var cambiaLaRed = antes is null || antes.AbrirALaRedLocal != nuevas.AbrirALaRedLocal;

            // rigctld
            if (!nuevas.RigctldActivo || cambiaLaRed || antes!.PuertoRigctld != nuevas.PuertoRigctld)
            {
                await PararRigctldAsync().ConfigureAwait(false);
            }

            if (nuevas.RigctldActivo)
            {
                if (_rigctld is null)
                {
                    var servidor = new ServidorRigctld(_radio, acceso, _registros.CreateLogger<ServidorRigctld>());
                    try
                    {
                        servidor.Arrancar(nuevas.PuertoRigctld);
                        _rigctld = servidor;
                        Rigctld = new EstadoDeServidor(true, servidor.Escuchando, null);
                    }
                    catch (SocketException ex)
                    {
                        await servidor.DisposeAsync().ConfigureAwait(false);
                        Rigctld = new EstadoDeServidor(false, null, Textos.F("Servicios.Servidor.PuertoOcupado", nuevas.PuertoRigctld, ex.Message));
                        _registro.LogWarning(ex, "No se pudo arrancar el servidor rigctld en el puerto {Puerto}.", nuevas.PuertoRigctld);
                    }
                }
                else
                {
                    _rigctld.CambiarAcceso(acceso);
                }
            }

            // TCI
            if (!nuevas.TciActivo || cambiaLaRed || antes!.PuertoTci != nuevas.PuertoTci)
            {
                await PararTciAsync().ConfigureAwait(false);
            }

            if (nuevas.TciActivo)
            {
                if (_tci is null)
                {
                    var servidor = new ServidorTci(_radio, acceso, _registros.CreateLogger<ServidorTci>());
                    servidor.SpotRecibido += AlLlegarSpotDeCliente;
                    try
                    {
                        await servidor.ArrancarAsync(nuevas.PuertoTci, ct).ConfigureAwait(false);
                        _tci = servidor;
                        Tci = new EstadoDeServidor(true, servidor.Escuchando, null);
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
                    {
                        servidor.SpotRecibido -= AlLlegarSpotDeCliente;
                        await servidor.DisposeAsync().ConfigureAwait(false);
                        Tci = new EstadoDeServidor(false, null, Textos.F("Servicios.Servidor.PuertoOcupado", nuevas.PuertoTci, ex.GetBaseException().Message));
                        _registro.LogWarning(ex, "No se pudo arrancar el servidor TCI en el puerto {Puerto}.", nuevas.PuertoTci);
                    }
                }
                else
                {
                    _tci.CambiarAcceso(acceso);
                }
            }

            _aplicadas = nuevas;
        }
        finally
        {
            _turno.Release();
        }

        EstadoCambiado?.Invoke(this, EventArgs.Empty);
    }

    private async Task PararRigctldAsync()
    {
        if (_rigctld is { } r)
        {
            _rigctld = null;
            await r.DisposeAsync().ConfigureAwait(false);
        }

        Rigctld = EstadoDeServidor.Apagado;
    }

    private async Task PararTciAsync()
    {
        if (_tci is { } t)
        {
            _tci = null;
            t.SpotRecibido -= AlLlegarSpotDeCliente;
            await t.DisposeAsync().ConfigureAwait(false);
        }

        Tci = EstadoDeServidor.Apagado;
    }

    private void AlRecibirSpot(object? origen, Spot spot) => _tci?.EnviarSpot(spot);

    private void AlLlegarSpotDeCliente(object? origen, Spot spot) => SpotDeCliente?.Invoke(this, spot);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_spots is not null) _spots.SpotRecibido -= AlRecibirSpot;
        await _turno.WaitAsync().ConfigureAwait(false);
        try
        {
            await PararRigctldAsync().ConfigureAwait(false);
            await PararTciAsync().ConfigureAwait(false);
        }
        finally
        {
            _turno.Release();
        }

        await _radio.DisposeAsync().ConfigureAwait(false);
    }
}
