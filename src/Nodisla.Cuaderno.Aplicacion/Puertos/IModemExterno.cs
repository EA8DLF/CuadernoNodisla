using Nodisla.Cuaderno.Dominio.Valores;

namespace Nodisla.Cuaderno.Aplicacion.Puertos;

/// <summary>Que esta haciendo un modem de modos digitales de texto.</summary>
public enum EstadoDelModem
{
    /// <summary>Sin conexion con el programa.</summary>
    Desconectado,
    /// <summary>Escuchando.</summary>
    Recibiendo,
    /// <summary>Transmitiendo.</summary>
    Transmitiendo,
    /// <summary>Ajustando la antena con portadora.</summary>
    Sintonizando,
}

/// <summary>
/// Modem externo de modos digitales de texto, como FLDigi.
/// </summary>
/// <remarks>
/// <para>
/// Es un puerto distinto del de <see cref="IPuenteDigital"/> a proposito. Aquel sirve para los
/// programas que trabajan por rachas y avisan de contactos cerrados —FT8, FT4 y compania—;
/// este es para los modos de <b>texto corrido</b>, donde lo que fluye son caracteres en los
/// dos sentidos y el contacto lo cierra el operador. Meterlos en el mismo puerto obligaria a
/// que la mitad de los metodos no hicieran nada en cada caso.
/// </para>
/// <para>
/// La idea es que <b>la ventana sea nuestra y el modem sea suyo</b>: el operador ve el texto,
/// la cascada y los controles dentro de Cuaderno NODISLA, aunque quien module y demodule sea
/// el programa externo.
/// </para>
/// </remarks>
public interface IModemExterno : IAsyncDisposable
{
    /// <summary>Nombre del programa, para mostrarlo.</summary>
    string Nombre { get; }

    /// <summary>Hay conexion con el programa.</summary>
    bool EstaConectado { get; }

    /// <summary>Estado actual.</summary>
    EstadoDelModem Estado { get; }

    /// <summary>Salta con el texto que se va recibiendo, segun llega.</summary>
    event EventHandler<string>? TextoRecibido;

    /// <summary>Salta cuando cambia el estado.</summary>
    event EventHandler<EstadoDelModem>? EstadoCambiado;

    Task ConectarAsync(CancellationToken ct = default);

    Task DesconectarAsync(CancellationToken ct = default);

    /// <summary>Lee la frecuencia que tiene puesta el modem.</summary>
    Task<Frecuencia> LeerFrecuenciaAsync(CancellationToken ct = default);

    /// <summary>Pone el modem en una frecuencia.</summary>
    Task PonerFrecuenciaAsync(Frecuencia frecuencia, CancellationToken ct = default);

    /// <summary>Lee el modo en que esta trabajando.</summary>
    Task<Modo> LeerModoAsync(CancellationToken ct = default);

    /// <summary>Cambia el modo de trabajo.</summary>
    Task PonerModoAsync(Modo modo, CancellationToken ct = default);

    /// <summary>Modos que el programa sabe hacer.</summary>
    Task<IReadOnlyList<Modo>> ModosDisponiblesAsync(CancellationToken ct = default);

    /// <summary>
    /// Pone texto en la cola de transmision.
    /// </summary>
    /// <remarks>
    /// Esto <b>transmite</b>. Como cualquier otra cosa que ponga el equipo en antena, tiene que
    /// hacerse bajo una transmision pedida a <see cref="IVigilantePtt"/>: el modem externo
    /// acciona su propio PTT y, si el programa se cuelga con la cola llena, el equipo se queda
    /// transmitiendo. El vigilante es lo que garantiza que eso acabe.
    /// </remarks>
    Task EnviarTextoAsync(string texto, CancellationToken ct = default);

    /// <summary>Corta la transmision y vacia la cola.</summary>
    Task AbortarTransmisionAsync(CancellationToken ct = default);
}
