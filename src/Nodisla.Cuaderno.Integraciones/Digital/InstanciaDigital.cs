using System.Net;
using Nodisla.Cuaderno.Aplicacion.Puertos;

namespace Nodisla.Cuaderno.Integraciones.Digital;

/// <summary>
/// Una instancia viva de un programa de modos digitales.
/// </summary>
/// <remarks>
/// Puede haber varias a la vez —WSJT-X y JTDX abiertos, o dos WSJT-X en dos radios— y el
/// protocolo no las distingue por nada mas que el campo <c>Id</c> que cada una se pone. De ahi
/// que sea la clave de todo el puente: el dialecto, el estado y la direccion a la que hay que
/// contestar cuelgan del identificador, no del puerto ni de la maquina.
/// </remarks>
public sealed class InstanciaDigital
{
    /// <summary>Crea la instancia con el identificador que ella misma declara.</summary>
    /// <param name="identificador">Campo <c>Id</c> de sus mensajes.</param>
    /// <param name="estado">Estado provisional, hasta que llegue el primero de verdad.</param>
    public InstanciaDigital(string identificador, EstadoDigital estado)
    {
        Identificador = identificador;
        Estado = estado;
    }

    /// <summary>Nombre con el que la instancia se identifica en cada mensaje.</summary>
    public string Identificador { get; }

    /// <summary>Programa con el que se esta hablando.</summary>
    public DialectoDigital Dialecto { get; internal set; } = DialectoDigital.Desconocido;

    /// <summary>Version que declaro en el latido.</summary>
    public string? Version { get; internal set; }

    /// <summary>Revision que declaro en el latido.</summary>
    public string? Revision { get; internal set; }

    /// <summary>Ultimo estado conocido.</summary>
    public EstadoDigital Estado { get; internal set; }

    /// <summary>Momento del ultimo mensaje recibido de esta instancia.</summary>
    public DateTimeOffset UltimaSenalUtc { get; internal set; }

    /// <summary>Direccion a la que hay que contestarle.</summary>
    public EndPoint? Remitente { get; internal set; }

    /// <summary>Lo que esta instancia admite que se le pida.</summary>
    public CapacidadesDigitales Capacidades => DeteccionDeDialecto.De(Dialecto);
}
