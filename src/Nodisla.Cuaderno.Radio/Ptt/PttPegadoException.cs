namespace Nodisla.Cuaderno.Radio.Ptt;

/// <summary>
/// Se lanza cuando han fallado <b>todas</b> las vias de bajar el PTT.
/// </summary>
/// <remarks>
/// Es la peor noticia que puede dar este programa: el equipo puede haberse quedado en antena.
/// Nunca se traga ni se convierte en un simple apunte del registro; llega hasta el operador
/// para que corte por lo sano (apagar el equipo o el amplificador).
/// </remarks>
public sealed class PttPegadoException : Exception
{
    /// <summary>Crea la excepcion con el detalle de cada via que fallo.</summary>
    /// <param name="mensaje">Explicacion para el operador.</param>
    /// <param name="fallos">Fallos de cada via probada, en orden.</param>
    public PttPegadoException(string mensaje, IReadOnlyList<Exception> fallos)
        : base(mensaje, fallos.Count > 0 ? fallos[0] : null)
    {
        Fallos = fallos;
    }

    /// <summary>Crea la excepcion sin detalle de vias.</summary>
    /// <param name="mensaje">Explicacion para el operador.</param>
    public PttPegadoException(string mensaje)
        : base(mensaje)
    {
        Fallos = [];
    }

    /// <summary>Crea la excepcion con una causa.</summary>
    /// <param name="mensaje">Explicacion para el operador.</param>
    /// <param name="causa">Excepcion que la provoco.</param>
    public PttPegadoException(string mensaje, Exception causa)
        : base(mensaje, causa)
    {
        Fallos = [causa];
    }

    /// <summary>Fallo de cada via probada, en el orden en que se probaron.</summary>
    public IReadOnlyList<Exception> Fallos { get; }
}
