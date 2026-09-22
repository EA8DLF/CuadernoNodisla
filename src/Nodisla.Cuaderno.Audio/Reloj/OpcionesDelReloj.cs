namespace Nodisla.Cuaderno.Audio.Reloj;

/// <summary>Como se mide la hora del modem.</summary>
public sealed class OpcionesDelReloj
{
    private TimeSpan _validezDeLaMedida = TimeSpan.FromMinutes(10);
    private TimeSpan _espera = TimeSpan.FromSeconds(2);
    private double _margenDeAvisoMs = 500.0;

    /// <summary>
    /// Servidores de hora a los que se pregunta, en orden de preferencia.
    /// </summary>
    /// <remarks>
    /// El primero es el del Real Instituto y Observatorio de la Armada, que es la hora oficial
    /// en Espana y esta cerca; los demas estan para cuando ese no conteste. Se pregunta a
    /// todos a la vez y se toma la medida que menos haya tardado en ir y volver, que es la que
    /// menos incertidumbre arrastra.
    /// </remarks>
    public IList<string> Servidores { get; } = new List<string>
    {
        "hora.roa.es",
        "pool.ntp.org",
        "time.cloudflare.com",
        "time.windows.com",
    };

    /// <summary>
    /// Cuanto vale una medida antes de volver a preguntar. Por omision, diez minutos.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public TimeSpan ValidezDeLaMedida
    {
        get => _validezDeLaMedida;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "La validez de la medida tiene que ser positiva.");
            }

            _validezDeLaMedida = value;
        }
    }

    /// <summary>Lo que se espera a que un servidor conteste. Por omision, dos segundos.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public TimeSpan EsperaPorServidor
    {
        get => _espera;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "La espera por servidor tiene que ser positiva.");
            }

            _espera = value;
        }
    }

    /// <summary>
    /// A partir de que desvio hay que avisar al operador, en milisegundos. Por omision, medio
    /// segundo: por debajo de ahi FT8 va fino, y a partir de un segundo empieza a fallar.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Si no es positivo.</exception>
    public double MargenDeAvisoMs
    {
        get => _margenDeAvisoMs;
        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "El margen de aviso tiene que ser positivo.");
            }

            _margenDeAvisoMs = value;
        }
    }

    /// <summary>Copia estos ajustes, para no compartir el objeto con quien lo configuro.</summary>
    /// <returns>Una copia independiente.</returns>
    public OpcionesDelReloj Copiar()
    {
        var copia = new OpcionesDelReloj
        {
            _validezDeLaMedida = _validezDeLaMedida,
            _espera = _espera,
            _margenDeAvisoMs = _margenDeAvisoMs,
        };

        copia.Servidores.Clear();
        foreach (var servidor in Servidores)
        {
            copia.Servidores.Add(servidor);
        }

        return copia;
    }
}
