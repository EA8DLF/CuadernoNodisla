using System.Diagnostics;
using System.Net;
using Nodisla.Cuaderno.Aplicacion.Puertos;
using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Servidores;

/// <summary>Por donde ha entrado un cliente.</summary>
public enum ProtocoloExterno
{
    /// <summary>Protocolo de red de Hamlib (rigctld).</summary>
    Rigctld,

    /// <summary>TCI de Expert Electronics, por WebSocket.</summary>
    Tci,
}

/// <summary>Como acaba una orden de un cliente.</summary>
public enum ResultadoDeOrden
{
    /// <summary>Hecha.</summary>
    Hecho,

    /// <summary>No se deja: permisos, pestillo, salvaguardas o el PTT es de otro.</summary>
    Rechazado,

    /// <summary>El equipo no sabe hacerlo.</summary>
    NoDisponible,

    /// <summary>Argumentos que no se entienden.</summary>
    Invalido,

    /// <summary>El equipo ha fallado al hacerlo.</summary>
    Fallo,
}

/// <summary>Lo que devuelve una orden: el resultado y, si no se ha hecho, por que.</summary>
/// <param name="Resultado">El resultado.</param>
/// <param name="Motivo">Por que no se ha hecho, para el registro y la pantalla.</param>
public readonly record struct Respuesta(ResultadoDeOrden Resultado, string? Motivo = null)
{
    /// <summary>Hecha.</summary>
    public static Respuesta Hecha { get; } = new(ResultadoDeOrden.Hecho);

    /// <summary>Si se ha hecho.</summary>
    public bool EsHecha => Resultado == ResultadoDeOrden.Hecho;
}

/// <summary>Un cliente tal y como se ve en pantalla.</summary>
/// <param name="Id">Identificador, para desconectarlo.</param>
/// <param name="Protocolo">Por donde ha entrado.</param>
/// <param name="Direccion">IP y puerto de origen.</param>
/// <param name="DesdeUtc">Cuando se conecto.</param>
/// <param name="Transmitiendo">Tiene el PTT ahora.</param>
/// <param name="Ordenes">Ordenes atendidas.</param>
/// <param name="Rechazadas">Ordenes rechazadas.</param>
public sealed record FotoDeCliente(
    Guid Id,
    ProtocoloExterno Protocolo,
    string Direccion,
    DateTimeOffset DesdeUtc,
    bool Transmitiendo,
    long Ordenes,
    long Rechazadas);

/// <summary>Una peticion de transmitir de un cliente externo, para el registro.</summary>
/// <param name="Utc">Cuando.</param>
/// <param name="Cliente">Quien: protocolo, IP y puerto.</param>
/// <param name="Frecuencia">Frecuencia del equipo en ese momento.</param>
/// <param name="Modo">Modo del equipo en ese momento.</param>
/// <param name="Concedida">Si salio al aire.</param>
/// <param name="Detalle">Por que no, o como acabo.</param>
public sealed record PeticionDeTx(
    DateTimeOffset Utc,
    string Cliente,
    Frecuencia Frecuencia,
    Modo Modo,
    bool Concedida,
    string Detalle);

/// <summary>Un programa conectado a uno de los servidores.</summary>
public sealed class ClienteExterno
{
    private long _ordenes;
    private long _rechazadas;
    private long _ultimaActividad;

    internal ClienteExterno(ProtocoloExterno protocolo, IPEndPoint remoto, Func<Task> cerrar, DateTimeOffset desde)
    {
        Protocolo = protocolo;
        Remoto = remoto;
        Cerrar = cerrar;
        DesdeUtc = desde;
        Actividad();
    }

    /// <summary>Identificador.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Por donde ha entrado.</summary>
    public ProtocoloExterno Protocolo { get; }

    /// <summary>IP y puerto de origen.</summary>
    public IPEndPoint Remoto { get; }

    /// <summary>Cuando se conecto.</summary>
    public DateTimeOffset DesdeUtc { get; }

    /// <summary>Como se le llama en el registro y en el vigilante («rigctld 127.0.0.1:50311»).</summary>
    public string Nombre => $"{(Protocolo == ProtocoloExterno.Rigctld ? "rigctld" : "TCI")} {Remoto}";

    /// <summary>
    /// El cliente ha pedido PTT y no lo ha soltado. Cuenta como fuente pulsada para el vigilante:
    /// tras un corte de seguridad no se rearma hasta que el cliente mande soltar.
    /// </summary>
    internal volatile bool PideTx;

    /// <summary>La transmision que tiene concedida, si la tiene.</summary>
    internal ITransmisionEnCurso? Transmision;

    internal Func<Task> Cerrar { get; }

    /// <summary>Tiene el PTT ahora.</summary>
    public bool Transmitiendo => Transmision is { EnAntena: true };

    /// <summary>Marca de la ultima orden recibida (Stopwatch).</summary>
    internal long UltimaActividad => Interlocked.Read(ref _ultimaActividad);

    internal void Actividad() => Interlocked.Exchange(ref _ultimaActividad, Stopwatch.GetTimestamp());

    internal void Contar(bool rechazada)
    {
        Interlocked.Increment(ref _ordenes);
        if (rechazada) Interlocked.Increment(ref _rechazadas);
    }

    internal FotoDeCliente Foto() => new(
        Id,
        Protocolo,
        Remoto.ToString(),
        DesdeUtc,
        Transmitiendo,
        Interlocked.Read(ref _ordenes),
        Interlocked.Read(ref _rechazadas));
}
